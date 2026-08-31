using AgenticHrmApi.Contracts;
using AgenticHrmApi.Models;
using AgenticHrmApi.Services;
using AgenticHrmApi.Services.Intents;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace AgenticHrmApi.Tests;

public class ManagerIntentHandlerTests
{
    private static readonly DateTime Now = new(2026, 8, 24, 10, 0, 0, DateTimeKind.Utc);

    private static (ManagerIntentHandler h, AgenticHrmApi.Data.AppDbContext db) Make(string name)
    {
        var db = TestDb.Create(name);
        return (new ManagerIntentHandler(db, new FixedClock(Now)), db);
    }

    private static LeaveRequest Pending(int userId, int day) => new()
    {
        UserId = userId,
        StartDate = new DateTime(2026, 8, day, 0, 0, 0, DateTimeKind.Utc),
        EndDate = new DateTime(2026, 8, day + 2, 0, 0, 0, DateTimeKind.Utc),
        Reason = "Family wedding",
        Status = "Pending",
        CreatedAt = Now
    };

    [Fact]
    public async Task Non_admin_is_refused_and_nothing_changes()
    {
        var (h, db) = Make(nameof(Non_admin_is_refused_and_nothing_changes));
        db.LeaveRequests.Add(Pending(3, 25)); await db.SaveChangesAsync();

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(3)!,           // Employee
            Intent = "leave.approve",
            Slots = new() { ["who"] = "Rahim" }
        });

        Assert.False(r.DidAct);
        Assert.Null(r.Pending);
        Assert.Equal("Pending", (await db.LeaveRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Single_match_proposes_confirmation_carrying_the_id()
    {
        var (h, db) = Make(nameof(Single_match_proposes_confirmation_carrying_the_id));
        db.LeaveRequests.Add(Pending(3, 25)); await db.SaveChangesAsync();
        var id = (await db.LeaveRequests.SingleAsync()).Id;

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(1)!,           // Admin
            Intent = "leave.approve",
            Slots = new() { ["who"] = "Rahim" }
        });

        Assert.Equal("approveLeave", r.Pending!.Kind);
        Assert.Equal(id, r.Pending.LeaveId);
        Assert.False(r.DidAct);
        Assert.Contains("Rahim", r.Reply);
    }

    [Fact]
    public async Task No_pending_says_so()
    {
        var (h, db) = Make(nameof(No_pending_says_so));

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(1)!,
            Intent = "leave.approve",
            Slots = new() { ["who"] = "Rahim" }
        });

        Assert.Null(r.Pending);
        Assert.Contains("don't see", r.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Multiple_pending_for_one_person_asks_which()
    {
        var (h, db) = Make(nameof(Multiple_pending_for_one_person_asks_which));
        db.LeaveRequests.AddRange(Pending(3, 25), Pending(3, 2)); await db.SaveChangesAsync();

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(1)!,
            Intent = "leave.approve",
            Slots = new() { ["who"] = "Rahim" }
        });

        Assert.Null(r.Pending!.LeaveId);
        Assert.Contains("Which one", r.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Ambiguous_person_disambiguates_by_department()
    {
        var (h, db) = Make(nameof(Ambiguous_person_disambiguates_by_department));
        db.LeaveRequests.AddRange(Pending(4, 25), Pending(5, 25)); await db.SaveChangesAsync();

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(1)!,
            Intent = "leave.approve",
            Slots = new() { ["who"] = "Karim" }
        });

        Assert.Contains("Sales", r.Reply);
        Assert.Contains("IT", r.Reply);
    }

    [Fact]
    public async Task Already_decided_leave_is_not_a_candidate()
    {
        var (h, db) = Make(nameof(Already_decided_leave_is_not_a_candidate));
        var decided = Pending(3, 25); decided.Status = "Approved";
        db.LeaveRequests.Add(decided); await db.SaveChangesAsync();

        var r = await h.HandleAsync(new IntentContext
        {
            User = db.Users.Find(1)!,
            Intent = "leave.approve",
            Slots = new() { ["who"] = "Rahim" }
        });

        Assert.Contains("don't see", r.Reply, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Commit_revalidates_and_sets_status()
    {
        var (h, db) = Make(nameof(Commit_revalidates_and_sets_status));
        db.LeaveRequests.Add(Pending(3, 25)); await db.SaveChangesAsync();
        var id = (await db.LeaveRequests.SingleAsync()).Id;

        var r = await h.CommitAsync(new IntentContext { User = db.Users.Find(1)!, Intent = "leave.approve", Pending = new PendingAction
        {
            Kind = "approveLeave", Intent = "leave.approve", LeaveId = id, IssuedAt = Now
        } });

        Assert.True(r.DidAct);
        Assert.Equal("Approved", (await db.LeaveRequests.SingleAsync()).Status);
    }

    [Fact]
    public async Task Commit_rejects_a_stale_pendingAction()
    {
        var (h, db) = Make(nameof(Commit_rejects_a_stale_pendingAction));
        db.LeaveRequests.Add(Pending(3, 25)); await db.SaveChangesAsync();
        var id = (await db.LeaveRequests.SingleAsync()).Id;

        var r = await h.CommitAsync(new IntentContext { User = db.Users.Find(1)!, Intent = "leave.approve", Pending = new PendingAction
        {
            Kind = "approveLeave", Intent = "leave.approve", LeaveId = id,
            IssuedAt = Now - ManagerIntentHandler.PendingActionTtl - TimeSpan.FromSeconds(1)
        } });

        Assert.False(r.DidAct);
        Assert.Equal("Pending", (await db.LeaveRequests.SingleAsync()).Status);
    }
}
