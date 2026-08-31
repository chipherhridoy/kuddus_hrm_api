using AgenticHrmApi.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticHrmApi.Tests;

public class AttendanceServiceTests
{
    private static AttendanceService Make(string name, DateTime at, out AgenticHrmApi.Data.AppDbContext db)
    {
        db = TestDb.Create(name);
        return new AttendanceService(db, new FixedClock(at));
    }

    [Fact]
    public async Task CheckIn_before_0915_is_Present()
    {
        var svc = Make(nameof(CheckIn_before_0915_is_Present), new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc), out var db);
        var r = await svc.CheckInAsync(3, null, null, null);
        Assert.True(r.Success);
        Assert.Equal("Present", r.Record!.Status);
    }

    [Fact]
    public async Task CheckIn_after_0915_is_Late()
    {
        var svc = Make(nameof(CheckIn_after_0915_is_Late), new DateTime(2026, 8, 24, 9, 30, 0, DateTimeKind.Utc), out var db);
        var r = await svc.CheckInAsync(3, null, null, null);
        Assert.Equal("Late", r.Record!.Status);
    }

    [Fact]
    public async Task Second_CheckIn_same_day_fails_and_writes_nothing()
    {
        var svc = Make(nameof(Second_CheckIn_same_day_fails_and_writes_nothing), new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc), out var db);
        await svc.CheckInAsync(3, null, null, null);
        var second = await svc.CheckInAsync(3, null, null, null);

        Assert.False(second.Success);
        Assert.Equal(1, await db.AttendanceRecords.CountAsync(a => a.UserId == 3));
    }

    [Fact]
    public async Task CheckOut_without_CheckIn_fails()
    {
        var svc = Make(nameof(CheckOut_without_CheckIn_fails), new DateTime(2026, 8, 24, 18, 0, 0, DateTimeKind.Utc), out var db);
        var r = await svc.CheckOutAsync(3, null);
        Assert.False(r.Success);
    }

    [Fact]
    public async Task CheckOut_sets_CheckOutTime_on_todays_record()
    {
        var svc = Make(nameof(CheckOut_sets_CheckOutTime_on_todays_record), new DateTime(2026, 8, 24, 9, 0, 0, DateTimeKind.Utc), out var db);
        await svc.CheckInAsync(3, null, null, null);
        var r = await svc.CheckOutAsync(3, null);

        Assert.True(r.Success);
        Assert.NotNull(await db.AttendanceRecords.Where(a => a.UserId == 3).Select(a => a.CheckOutTime).FirstAsync());
    }
}
