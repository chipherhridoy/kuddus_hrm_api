using AgenticHrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Data;

public static class DevDataSeeder
{
    public static async Task SeedAsync(AppDbContext db, CancellationToken ct = default)
    {
        if (await db.AttendanceRecords.AnyAsync(ct)) return;

        var today = DateTime.UtcNow.Date;

        db.AttendanceRecords.AddRange(
            new AttendanceRecord { UserId = 1, Date = today, CheckInTime = today.AddHours(9).AddMinutes(5),  Status = "Present", Notes = "Seeded" },
            new AttendanceRecord { UserId = 2, Date = today, CheckInTime = today.AddHours(9).AddMinutes(25), Status = "Late",    Notes = "Seeded" },
            new AttendanceRecord { UserId = 3, Date = today, CheckInTime = today.AddHours(8).AddMinutes(50), Status = "Present", Notes = "Seeded" }
        );

        db.LeaveRequests.AddRange(
            new LeaveRequest { UserId = 3, StartDate = today.AddDays(2), EndDate = today.AddDays(5), Reason = "Family wedding", Status = "Pending",  CreatedAt = today.AddDays(-1) },
            new LeaveRequest { UserId = 4, StartDate = today.AddDays(1), EndDate = today.AddDays(2), Reason = "Sick leave",     Status = "Approved", CreatedAt = today.AddDays(-2) }
        );

        await db.SaveChangesAsync(ct);
    }
}
