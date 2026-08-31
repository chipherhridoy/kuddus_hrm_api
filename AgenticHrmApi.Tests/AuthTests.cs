using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using AgenticHrmApi.Contracts;
using AgenticHrmApi.Controllers;
using AgenticHrmApi.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Xunit;
using System.Text.Json;

namespace AgenticHrmApi.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string _dbName = "AuthTestingDb_" + Guid.NewGuid().ToString();

    public CustomWebApplicationFactory()
    {
        Environment.SetEnvironmentVariable("FaceEncryptionKey", "MTIzNDU2Nzg5MDEyMzQ1Njc4OTAxMjM0NTY3ODkwMTI=");
        Environment.SetEnvironmentVariable("Jwt__Key", "SuperSecretKeyForTestingSuperSecretKeyForTesting");
        Environment.SetEnvironmentVariable("Jwt__Issuer", "TestIssuer");
        Environment.SetEnvironmentVariable("Jwt__Audience", "TestAudience");
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureServices(services =>
        {
            var descriptors = services.Where(d => 
                d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                d.ServiceType == typeof(DbContextOptions) ||
                d.ServiceType.Name.Contains("IDbContextOptionsConfiguration") ||
                d.ServiceType.Name.Contains("IConfigureOptions`1[[Microsoft.EntityFrameworkCore.DbContextOptions"))
                .ToList();
            
            foreach (var d in descriptors)
            {
                services.Remove(d);
            }

            var dbConnectionDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(System.Data.Common.DbConnection));
            if (dbConnectionDescriptor != null) services.Remove(dbConnectionDescriptor);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseInMemoryDatabase(_dbName);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
            
            // Seed the test database using the same logic
            if (!db.Users.Any())
            {
                db.Users.AddRange(
                    new Models.User { Id = 1, Name = "Mahfuz Admin", Email = "admin@kuddus.com", PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Models.User>().HashPassword(null!, "Kuddus@1234"), Role = "Admin", Department = "HR" },
                    new Models.User { Id = 3, Name = "Rahim Uddin",  Email = "rahim@kuddus.com", PasswordHash = new Microsoft.AspNetCore.Identity.PasswordHasher<Models.User>().HashPassword(null!, "Kuddus@1001"), Role = "Employee", Department = "Sales" }
                );
                db.SaveChanges();
            }
        });
    }
}

public class AuthTests : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client;
    private readonly CustomWebApplicationFactory _factory;

    public AuthTests(CustomWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task SeededPasswordLogsIn_WrongPasswordDoesNot()
    {
        // Valid
        var validReq = new UsersController.LoginRequest { Email = "admin@kuddus.com", Password = "Kuddus@1234" };
        var validRes = await _client.PostAsJsonAsync("/api/users/login", validReq);
        Assert.Equal(HttpStatusCode.OK, validRes.StatusCode);
        var authRes = await validRes.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(authRes?.Token);

        // Invalid
        var invalidReq = new UsersController.LoginRequest { Email = "admin@kuddus.com", Password = "Wrong" };
        var invalidRes = await _client.PostAsJsonAsync("/api/users/login", invalidReq);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidRes.StatusCode);
    }

    [Fact]
    public async Task UnknownEmailAndWrongPassword_ReturnByteIdenticalResponses()
    {
        var wrongPassReq = new UsersController.LoginRequest { Email = "admin@kuddus.com", Password = "Wrong" };
        var wrongPassRes = await _client.PostAsJsonAsync("/api/users/login", wrongPassReq);
        var wrongPassBody = await wrongPassRes.Content.ReadAsByteArrayAsync();

        var unknownEmailReq = new UsersController.LoginRequest { Email = "nobody@kuddus.com", Password = "Wrong" };
        var unknownEmailRes = await _client.PostAsJsonAsync("/api/users/login", unknownEmailReq);
        var unknownEmailBody = await unknownEmailRes.Content.ReadAsByteArrayAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, wrongPassRes.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, unknownEmailRes.StatusCode);
        Assert.Equal(wrongPassBody, unknownEmailBody);
    }

    [Fact]
    public async Task TokenCarriesRole()
    {
        // Admin
        var res1 = await _client.PostAsJsonAsync("/api/users/login", new UsersController.LoginRequest { Email = "admin@kuddus.com", Password = "Kuddus@1234" });
        var auth1 = await res1.Content.ReadFromJsonAsync<AuthResponse>();
        
        // Employee
        var res3 = await _client.PostAsJsonAsync("/api/users/login", new UsersController.LoginRequest { Email = "rahim@kuddus.com", Password = "Kuddus@1001" });
        var auth3 = await res3.Content.ReadFromJsonAsync<AuthResponse>();

        var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        
        var jwt1 = handler.ReadJwtToken(auth1!.Token);
        var role1 = jwt1.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;
        Assert.Equal("Admin", role1);

        var jwt3 = handler.ReadJwtToken(auth3!.Token);
        var role3 = jwt3.Claims.FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Role)?.Value;
        Assert.Equal("Employee", role3);
    }

    [Fact]
    public async Task Authorization_EndpointSecurity()
    {
        // Login as Employee
        var res = await _client.PostAsJsonAsync("/api/users/login", new UsersController.LoginRequest { Email = "rahim@kuddus.com", Password = "Kuddus@1001" });
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();

        var employeeClient = _factory.CreateClient();
        employeeClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth!.Token);

        // Employee hits admin endpoint (/api/users POST creates user)
        var createReq = new UsersController.CreateUserRequest { Name = "Test", Email = "test@kuddus.com", Password = "123" };
        var employeeRes = await employeeClient.PostAsJsonAsync("/api/users", createReq);
        Assert.Equal(HttpStatusCode.Forbidden, employeeRes.StatusCode);

        // No token gets 401
        var anonymousClient = _factory.CreateClient();
        var anonymousRes = await anonymousClient.PostAsJsonAsync("/api/users", createReq);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousRes.StatusCode);
    }

    [Theory]
    [InlineData(null, "Kuddus@1234")]   // caught by [ApiController] model validation -> 400
    [InlineData("admin@kuddus.com", null)]
    [InlineData(null, null)]
    [InlineData("", "")]                // reaches the controller guard -> 401
    [InlineData("   ", "   ")]
    [InlineData("not-an-email", "x")]
    public async Task MalformedLoginBody_NeverReturns500(string? email, string? password)
    {
        // The contract that matters: no shape of input makes login fault. Nulls are
        // rejected by model binding before the action runs; blanks are rejected by the
        // controller. Previously the blank paths threw out of ToLower() /
        // VerifyHashedPassword and surfaced as a 500 carrying a stack trace.
        var res = await _client.PostAsJsonAsync("/api/users/login",
            new UsersController.LoginRequest { Email = email!, Password = password! });

        Assert.True(
            res.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.Unauthorized,
            $"expected 400 or 401, got {(int)res.StatusCode} {res.StatusCode}");
    }

    [Fact]
    public async Task BlankCredentials_AreIndistinguishableFromWrongOnes()
    {
        // Anything that reaches the credential check must come back identical, so the
        // response never reveals which accounts exist.
        var blank = await _client.PostAsJsonAsync("/api/users/login",
            new UsersController.LoginRequest { Email = "", Password = "" });
        var wrong = await _client.PostAsJsonAsync("/api/users/login",
            new UsersController.LoginRequest { Email = "admin@kuddus.com", Password = "Wrong" });

        Assert.Equal(HttpStatusCode.Unauthorized, blank.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrong.StatusCode);
        Assert.Equal(
            await blank.Content.ReadAsByteArrayAsync(),
            await wrong.Content.ReadAsByteArrayAsync());
    }
}
