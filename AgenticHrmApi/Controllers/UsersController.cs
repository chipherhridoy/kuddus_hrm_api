using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services.Auth;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class UsersController : ControllerBase
{
    private readonly AppDbContext _db;
    private readonly JwtTokenService _tokenService;

    private static readonly PasswordHasher<User> Hasher = new();

    /// A valid PBKDF2 hash of a value nobody knows, verified against when the email
    /// is unknown so that a miss costs the same work as a hit. Without it, an
    /// unknown email returns before any hashing happens and the response time
    /// alone reveals which accounts exist.
    private static readonly string DummyHash =
        Hasher.HashPassword(new User(), Guid.NewGuid().ToString());

    public UsersController(AppDbContext db, JwtTokenService tokenService)
    {
        _db = db;
        _tokenService = tokenService;
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> GetAllUsers()
    {
        var users = await _db.Users
            .OrderBy(u => u.Id)
            .Select(u => new UserDto
            {
                Id = u.Id,
                Name = u.Name,
                Email = u.Email,
                Role = u.Role,
                Department = u.Department,
                Designation = u.Designation,
                FaceEnrolled = u.FaceEnrolledAt.HasValue
            })
            .ToListAsync();

        return Ok(users);
    }

    [HttpGet("{id}")]
    [Authorize]
    public async Task<IActionResult> GetUserById(int id)
    {
        var user = await _db.Users.FindAsync(id);
        if (user == null) return NotFound(new { message = "User not found" });

        return Ok(new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            Department = user.Department,
            Designation = user.Designation,
            FaceEnrolled = user.FaceEnrolledAt.HasValue
        });
    }

    public class LoginRequest
    {
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest req)
    {
        // A null email or password is a malformed body, not a server fault: without
        // these guards both paths below throw and return 500 with a stack trace.
        if (string.IsNullOrWhiteSpace(req.Email) || string.IsNullOrWhiteSpace(req.Password))
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var email = req.Email.ToLowerInvariant();
        var user = await _db.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == email);

        // Always verify a hash, even when the email is unknown, so both outcomes
        // take the same time. The response body is already identical; timing was
        // the remaining enumeration oracle.
        var verified = Hasher.VerifyHashedPassword(
            user ?? new User(),
            user?.PasswordHash is { Length: > 0 } h ? h : DummyHash,
            req.Password) == PasswordVerificationResult.Success;

        if (user == null || !verified)
        {
            return Unauthorized(new { message = "Invalid credentials" });
        }

        var token = _tokenService.CreateToken(user);
        var tokenHandler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
        var jwtToken = tokenHandler.ReadJwtToken(token);

        return Ok(new AuthResponse
        {
            Token = token,
            ExpiresAt = jwtToken.ValidTo,
            User = new UserDto
            {
                Id = user.Id,
                Name = user.Name,
                Email = user.Email,
                Role = user.Role,
                Department = user.Department,
                Designation = user.Designation,
                FaceEnrolled = user.FaceEnrolledAt.HasValue
            }
        });
    }

    public class CreateUserRequest
    {
        public string Name { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string Role { get; set; } = "Employee";
        public string Department { get; set; } = "General";
        public string Designation { get; set; } = "Staff";
    }

    [HttpPost]
    [Authorize(Roles = "Admin")]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserRequest req)
    {
        if (string.IsNullOrWhiteSpace(req.Name) || string.IsNullOrWhiteSpace(req.Password))
        {
            return BadRequest(new { message = "Name and Password are required." });
        }

        var hasher = new PasswordHasher<User>();
        var user = new User
        {
            Name = req.Name,
            Email = string.IsNullOrWhiteSpace(req.Email) ? $"{req.Name.ToLower().Replace(" ", ".")}@kuddus.com" : req.Email,
            Role = req.Role,
            Department = req.Department,
            Designation = req.Designation,
            CreatedAt = DateTime.UtcNow
        };
        user.PasswordHash = hasher.HashPassword(user, req.Password);

        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        return CreatedAtAction(nameof(GetUserById), new { id = user.Id }, new UserDto
        {
            Id = user.Id,
            Name = user.Name,
            Email = user.Email,
            Role = user.Role,
            Department = user.Department,
            Designation = user.Designation,
            FaceEnrolled = user.FaceEnrolledAt.HasValue
        });
    }
}
