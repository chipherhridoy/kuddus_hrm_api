using AgenticHrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<LeaveRequest> LeaveRequests { get; set; } = null!;
    public DbSet<AttendanceRecord> AttendanceRecords { get; set; } = null!;
    public DbSet<FaceTemplate> FaceTemplates { get; set; } = null!;
    public DbSet<FaceLoginAttempt> FaceLoginAttempts { get; set; } = null!;
    public DbSet<FaceChallenge> FaceChallenges { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure relationships
        modelBuilder.Entity<LeaveRequest>()
            .HasOne(l => l.User)
            .WithMany(u => u.LeaveRequests)
            .HasForeignKey(l => l.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AttendanceRecord>()
            .HasOne(a => a.User)
            .WithMany(u => u.AttendanceRecords)
            .HasForeignKey(a => a.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<AttendanceRecord>()
            .HasIndex(a => new { a.UserId, a.Date })
            .IsUnique()
            .HasDatabaseName("IX_AttendanceRecords_UserId_Date");

        modelBuilder.Entity<FaceTemplate>()
            .HasOne(f => f.User)
            .WithMany(u => u.FaceTemplates)
            .HasForeignKey(f => f.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<FaceTemplate>()
            .HasOne<User>()
            .WithMany()
            .HasForeignKey(f => f.EnrolledByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<FaceTemplate>()
            .HasIndex(f => new { f.UserId, f.IsActive })
            .HasDatabaseName("IX_FaceTemplates_UserId_IsActive");

        modelBuilder.Entity<FaceLoginAttempt>()
            .HasIndex(f => f.CreatedAt);

        // Seed 2 Admin accounts and 3 Employee accounts
        var seedUsers = new List<User>
        {
            new User
            {
                Id = 1,
                Name = "Mahfuz Admin",
                Email = "admin@kuddus.com",
                PasswordHash = "AQAAAAIAAYagAAAAEC4Jbn8Nd/Dwn8HU7gqnsbJo/byp6OQ2g4i6SwQZdtIfAwvYVSz5LlGh+mkM4WJT7Q==",
                Role = "Admin",
                Department = "Executive HR",
                Designation = "HR Director",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = 2,
                Name = "Kuddus SuperAdmin",
                Email = "kuddus@kuddus.com",
                PasswordHash = "AQAAAAIAAYagAAAAEHryAZ/oXThb6eVrA4LX5uttM6Mlvd1VYQtcNYxmpMbpoYnBnmDe3bHKxca34IK/fQ==",
                Role = "Admin",
                Department = "Operations",
                Designation = "Operations Head",
                CreatedAt = new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = 3,
                Name = "Rahim Chowdhury",
                Email = "rahim@kuddus.com",
                PasswordHash = "AQAAAAIAAYagAAAAECBMuMglaf8y63JHfSr3OhKNyialFduBoAcwclv1amqOBOiSBbJw2HLgbSoKcyxJuw==",
                Role = "Employee",
                Department = "Engineering",
                Designation = "Senior Software Engineer",
                CreatedAt = new DateTime(2026, 1, 15, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = 4,
                Name = "Karim Hasan",
                Email = "karim@kuddus.com",
                PasswordHash = "AQAAAAIAAYagAAAAEHtzta7ccdXQ2LvmrACEW5/lTO7FfpmIZHzsOQ1xoX91jzSYw+C35ZOdxCuu3KrE0w==",
                Role = "Employee",
                Department = "Design",
                Designation = "UI/UX Designer",
                CreatedAt = new DateTime(2026, 2, 1, 0, 0, 0, DateTimeKind.Utc)
            },
            new User
            {
                Id = 5,
                Name = "Fatima Begum",
                Email = "fatima@kuddus.com",
                PasswordHash = "AQAAAAIAAYagAAAAED1wHbm9Ta0l9qVL7LgXcq0/pwpz7DR4X90VSRLs9eDVW7f+vRYBAf+jr6bOWlD/Sw==",
                Role = "Employee",
                Department = "Customer Success",
                Designation = "Support Specialist",
                CreatedAt = new DateTime(2026, 2, 10, 0, 0, 0, DateTimeKind.Utc)
            }
        };

        modelBuilder.Entity<User>().HasData(seedUsers);

    }
}
