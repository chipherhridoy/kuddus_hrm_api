using AgenticHrmApi.Contracts;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Xunit;

namespace AgenticHrmApi.Tests;

public class QueryIntentHandlerTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 14, 0, 0, DateTimeKind.Utc);

    [Fact]
    public async Task Attendance_query_reports_checked_in_time_without_confirmation()
    {
        var db = TestDb.Create(nameof(Attendance_query_reports_checked_in_time_without_confirmation));
        db.AttendanceRecords.Add(new AttendanceRecord
        {
            UserId = 3, Date = Now.Date,
            CheckInTime = Now.Date.AddHours(9).AddMinutes(5), Status = "Present"
        });
        await db.SaveChangesAsync();

        var h = new QueryIntentHandler(db, new FixedClock(Now));
        var r = await h.HandleAsync(new IntentContext { User = db.Users.Find(3)!, Intent = "query.attendance" });

        Assert.Null(r.Pending);
        Assert.False(r.DidAct);
        Assert.Contains("09:05", r.Reply);
    }

    [Fact]
    public async Task Attendance_query_when_not_checked_in()
    {
        var db = TestDb.Create(nameof(Attendance_query_when_not_checked_in));
        var h = new QueryIntentHandler(db, new FixedClock(Now));
        var r = await h.HandleAsync(new IntentContext { User = db.Users.Find(3)!, Intent = "query.attendance" });

        Assert.Contains("not checked in", r.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Leaves_query_counts_pending()
    {
        var db = TestDb.Create(nameof(Leaves_query_counts_pending));
        db.LeaveRequests.AddRange(
            new LeaveRequest { UserId = 3, Status = "Pending",  StartDate = Now, EndDate = Now, Reason = "a", CreatedAt = Now },
            new LeaveRequest { UserId = 3, Status = "Approved", StartDate = Now, EndDate = Now, Reason = "b", CreatedAt = Now });
        await db.SaveChangesAsync();

        var h = new QueryIntentHandler(db, new FixedClock(Now));
        var r = await h.HandleAsync(new IntentContext { User = db.Users.Find(3)!, Intent = "query.leaves" });

        Assert.Contains("1", r.Reply);
    }

    [Fact]
    public async Task Stats_query_is_admin_only()
    {
        var db = TestDb.Create(nameof(Stats_query_is_admin_only));
        var h = new QueryIntentHandler(db, new FixedClock(Now));
        var r = await h.HandleAsync(new IntentContext { User = db.Users.Find(3)!, Intent = "query.stats" });

        Assert.Contains("admin", r.Reply, StringComparison.OrdinalIgnoreCase);
    }
}
