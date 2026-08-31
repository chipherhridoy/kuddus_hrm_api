using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Tests;

public static class TestDb
{
    public static AppDbContext Create(string name)
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        var db = new AppDbContext(options);

        db.Users.AddRange(
            new User { Id = 1, Name = "Mahfuz Admin", Email = "admin@kuddus.com", PasswordHash = "1234", Role = "Admin",    Department = "HR" },
            new User { Id = 3, Name = "Rahim Uddin",  Email = "rahim@kuddus.com", PasswordHash = "1111", Role = "Employee", Department = "Sales" },
            new User { Id = 4, Name = "Karim Ahmed",  Email = "karim@kuddus.com", PasswordHash = "2222", Role = "Employee", Department = "Sales" },
            new User { Id = 5, Name = "Karim Hossain", Email = "karim2@kuddus.com", PasswordHash = "3333", Role = "Employee", Department = "IT" }
        );
        db.SaveChanges();
        return db;
    }
}
