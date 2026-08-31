using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Auth;
using AgenticHrmApi.Services.Intents;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using System.Security.Claims;
using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using AgenticHrmApi.Services.Face;
// Enable legacy timestamp behavior for Npgsql to seamlessly handle DateTime
AppContext.SetSwitch("Npgsql.EnableLegacyTimestampBehavior", true);

var builder = WebApplication.CreateBuilder(args);

// Dynamically bind to Render's PORT environment variable if provided
var renderPort = Environment.GetEnvironmentVariable("PORT");
if (!string.IsNullOrEmpty(renderPort))
{
    builder.WebHost.UseUrls($"http://0.0.0.0:{renderPort}");
}

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddOpenApi();

builder.Services.Configure<JwtOptions>(builder.Configuration.GetSection("Jwt"));
builder.Services.AddSingleton<JwtTokenService>();

var jwt = builder.Configuration.GetSection("Jwt").Get<JwtOptions>() ?? new JwtOptions();

// Never fall back to a default signing key. A known key lets anyone mint a token
// with role=Admin, which is every permission in the system including face
// enrolment. Fail fast instead of failing open. HS256 requires >= 256 bits, and
// a key shorter than that throws deep inside the token handler on first login
// rather than here, so the length is checked up front.
const int MinJwtKeyBytes = 32;
if (Encoding.UTF8.GetByteCount(jwt.Key) < MinJwtKeyBytes)
{
    throw new InvalidOperationException(
        $"Jwt:Key must be at least {MinJwtKeyBytes} bytes (it is " +
        $"{Encoding.UTF8.GetByteCount(jwt.Key)}). Set it with: dotnet user-secrets " +
        "set \"Jwt:Key\" \"<32+ random bytes, base64>\"");
}
var jwtKey = jwt.Key;

builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o => o.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,
        ValidateAudience = true,
        ValidAudience = jwt.Audience,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(30),
        RoleClaimType = ClaimTypes.Role,
    });
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.AddFixedWindowLimiter("face", opt =>
    {
        opt.PermitLimit = 10;
        opt.Window = TimeSpan.FromMinutes(1);
    });
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
});
builder.Services.AddSingleton<IClock, SystemClock>();

var faceEncryptionKey = builder.Configuration["FaceEncryptionKey"]
    ?? throw new InvalidOperationException(
        "FaceEncryptionKey not configured. dotnet user-secrets set " +
        "\"FaceEncryptionKey\" \"<32 random bytes, base64>\"");
builder.Services.AddSingleton(new TemplateCipher(faceEncryptionKey));

var yunet = Path.Combine(builder.Environment.ContentRootPath,
                         "Models", "onnx", "face_detection_yunet_2023mar.onnx");
var sface = Path.Combine(builder.Environment.ContentRootPath,
                         "Models", "onnx", "face_recognition_sface_2021dec.onnx");
foreach (var p in new[] { yunet, sface })
    if (!File.Exists(p)) throw new FileNotFoundException(
        $"Face model missing: {p}. Run scripts/fetch-models.ps1.", p);
builder.Services.AddSingleton<IFaceEngine>(_ => new OpenCvFaceEngine(yunet, sface));

builder.Services.AddScoped<AttendanceService>();
builder.Services.AddScoped<LeaveIntentHandler>();
builder.Services.AddScoped<ManagerIntentHandler>();
builder.Services.AddScoped<LocalRuleReasoner>();
builder.Services.AddScoped<FaceEnrollmentService>();
builder.Services.AddScoped<LivenessVerifier>();
builder.Services.AddHostedService<FaceAuditCleanupService>();
builder.Services.AddHttpClient<GeminiReasoner>();
builder.Services.AddScoped<IReasoner>(sp => sp.GetRequiredService<GeminiReasoner>());

builder.Services.AddScoped<IntentRouter>(sp =>
{
    var leave = sp.GetRequiredService<LeaveIntentHandler>();
    var manager = sp.GetRequiredService<ManagerIntentHandler>();
    return new IntentRouter(
    [
        new AttendanceIntentHandler(sp.GetRequiredService<AttendanceService>()),
        leave,
        manager,
        new QueryIntentHandler(sp.GetRequiredService<AppDbContext>(), sp.GetRequiredService<IClock>()),
        new ControlIntentHandler(leave, manager),
        new ChatIntentHandler()
    ]);
});

builder.Services.AddScoped<ConversationService>();
builder.Services.AddHttpClient<AgenticHrmApi.Services.GroqApiService>();
builder.Services.AddHttpClient<AgenticHrmApi.Services.GeminiApiService>();

// Add CORS Policy for Web (Next.js) & Mobile (Flutter)
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() 
    ?? new[] { "http://localhost:3000", "http://localhost:5173", "http://127.0.0.1:3000" };

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAll", policy =>
    {
        if (builder.Environment.IsDevelopment())
        {
            policy.AllowAnyOrigin()
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
        else
        {
            policy.WithOrigins(allowedOrigins)
                  .AllowAnyHeader()
                  .AllowAnyMethod();
        }
    });
});

// Configure EF Core PostgreSQL for Neon DB
var rawConnectionString = builder.Configuration.GetConnectionString("DefaultConnection");
var connectionString = NormalizePostgresConnectionString(rawConnectionString);
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(connectionString));

var app = builder.Build();

// Auto-create database & tables in Neon Postgres and seed data
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var db = services.GetRequiredService<AppDbContext>();
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogInformation("Connecting to Neon PostgreSQL and applying migrations...");
        
        db.Database.Migrate();
        logger.LogInformation("Neon PostgreSQL database migrations applied & seed data verified successfully!");

        await DevDataSeeder.SeedAsync(db);
        logger.LogInformation("Database seed verification completed relative to {Today:yyyy-MM-dd}.", DateTime.UtcNow.Date);
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while initializing the Neon PostgreSQL database.");
    }
}

// Fail loudly if the Gemini model is unusable. A bad model name only ever
// produces a 404 that the reasoner swallows into its rule-based fallback, so
// without this check the whole LLM layer can be dead for the life of the
// process while every request still returns 200.
using (var scope = app.Services.CreateScope())
{
    var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
    var reasoner = scope.ServiceProvider.GetRequiredService<AgenticHrmApi.Services.GeminiReasoner>();

    if (!reasoner.HasApiKey)
    {
        logger.LogWarning(
            "No GeminiApiKey configured. Kuddus will run on the local rule parser: " +
            "intents still work, but it cannot fill slots from a single sentence.");
    }
    else if (await reasoner.VerifyModelAsync())
    {
        logger.LogInformation("Gemini model {Model} verified.", reasoner.Model);
    }
    else
    {
        logger.LogError(
            "Gemini model '{Model}' is NOT available for this API key. Every reasoning call " +
            "will silently fall back to the local rule parser. List what this key can use with: " +
            "curl \"https://generativelanguage.googleapis.com/v1beta/models?key=$GEMINI_KEY\" " +
            "then set GeminiModel in appsettings.json.",
            reasoner.Model);
    }
}

// Enable CORS
app.UseCors("AllowAll");

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
}

// Root endpoint for quick health check & status
app.MapGet("/", async (AppDbContext db) =>
{
    var userCount = await db.Users.CountAsync();
    var attendanceCount = await db.AttendanceRecords.CountAsync();
    var leaveCount = await db.LeaveRequests.CountAsync();

    return Results.Ok(new
    {
        status = "Agentic HRM API is running",
        database = "Neon PostgreSQL (Supabase compatible)",
        connected = true,
        summary = new
        {
            totalUsers = userCount,
            totalAttendanceRecords = attendanceCount,
            totalLeaveRequests = leaveCount
        },
        timestamp = DateTime.UtcNow
    });
});

app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

public partial class Program
{
    public static string NormalizePostgresConnectionString(string? raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return string.Empty;
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            var userInfo = uri.UserInfo.Split(':');
            var username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "";
            var password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "";
            var database = uri.AbsolutePath.TrimStart('/');
            var port = uri.Port > 0 ? uri.Port : 5432;

            var builder = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = port,
                Database = database,
                Username = username,
                Password = password,
                SslMode = Npgsql.SslMode.Require
            };
            return builder.ConnectionString;
        }
        return raw;
    }
}

