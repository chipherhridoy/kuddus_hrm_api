using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Services;

public record AttendanceOutcome(bool Success, string Message, AttendanceRecord? Record);

public class AttendanceService(AppDbContext db, IClock clock)
{
    public static readonly TimeSpan LateAfter = new(9, 15, 0);

    public async Task<AttendanceOutcome> CheckInAsync(
        int userId, double? latitude, double? longitude, string? notes, DateTime? timestamp = null, CancellationToken ct = default)
    {
        var user = await db.Users.FindAsync([userId], ct);
        if (user is null) return new(false, "User not found.", null);

        var now = timestamp ?? clock.UtcNow;
        var today = now.Date;

        var existing = await db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Date.Date == today, ct);

        if (existing is not null)
            return new(false, $"Already checked in today at {existing.CheckInTime:hh:mm tt}.", existing);

        var record = new AttendanceRecord
        {
            UserId = userId,
            Date = today,
            CheckInTime = now,
            Status = now.TimeOfDay > LateAfter ? "Late" : "Present",
            Latitude = latitude ?? 0.0,
            Longitude = longitude ?? 0.0,
            Notes = notes ?? $"Check-in at {now:t}"
        };

        db.AttendanceRecords.Add(record);
        await db.SaveChangesAsync(ct);
        return new(true, $"Checked in at {now:hh:mm tt}.", record);
    }

    public async Task<AttendanceOutcome> CheckOutAsync(int userId, string? notes, DateTime? timestamp = null, CancellationToken ct = default)
    {
        var now = timestamp ?? clock.UtcNow;
        var today = now.Date;

        var record = await db.AttendanceRecords
            .FirstOrDefaultAsync(a => a.UserId == userId && a.Date.Date == today, ct);

        if (record is null) return new(false, "No check-in found for today.", null);

        record.CheckOutTime = now;
        if (!string.IsNullOrWhiteSpace(notes)) record.Notes += $" | {notes}";

        await db.SaveChangesAsync(ct);
        return new(true, $"Checked out at {now:hh:mm tt}.", record);
    }
}
