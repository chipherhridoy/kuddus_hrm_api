using AgenticHrmApi.Contracts;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Xunit;

namespace AgenticHrmApi.Tests;

public class InvariantTests
{
    // Invariants 1 and 2 assert on DATABASE STATE, never on reply text.
    // "Okay, cancelled" being spoken is not evidence that nothing was written.

    [Fact]
    public async Task Invariant1_no_write_intent_reaches_the_db_without_confirmation()
    {
        var svc = ConversationHarness.Make(nameof(Invariant1_no_write_intent_reaches_the_db_without_confirmation), out var db);

        await svc.ProcessAsync(new ConverseRequest { UserId = 3, Text = "apply leave from Aug 28 to Aug 30 for a family wedding" });

        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Invariant2_cancelled_flow_writes_nothing()
    {
        var svc = ConversationHarness.Make(nameof(Invariant2_cancelled_flow_writes_nothing), out var db);

        var t1 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "apply leave from Aug 28 to Aug 30 for a family wedding"
        });
        await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "never mind",
            PendingAction = JsonSerializer.Serialize(t1.PendingAction!)
        });

        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Invariant3_truncated_readback_can_never_be_confirmed()
    {
        var svc = ConversationHarness.Make(nameof(Invariant3_truncated_readback_can_never_be_confirmed), out var db);

        var t1 = await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "apply leave from Aug 28 to Aug 30 for a family wedding"
        });

        await svc.ProcessAsync(new ConverseRequest
        {
            UserId = 3, Text = "yes",
            PendingAction = JsonSerializer.Serialize(t1.PendingAction!),
            History = JsonSerializer.Serialize(new[]
            {
                new ConversationTurn { Role = "kuddus", Text = t1.Reply, Truncated = true }
            })
        });

        Assert.Equal(0, await db.LeaveRequests.CountAsync());
    }

    [Fact]
    public async Task Invariant5_every_path_eventually_closes()
    {
        var svc = ConversationHarness.Make(nameof(Invariant5_every_path_eventually_closes), out _);
        var history = new List<ConversationTurn>();
        ConverseResponse res;

        for (var i = 0; i < 20; i++)
        {
            res = await svc.ProcessAsync(new ConverseRequest
            {
                UserId = 3, Text = "blah",
                History = JsonSerializer.Serialize(history)
            });
            if (!res.ConversationOpen) return;   // closed as required
            history.Add(new ConversationTurn { Role = "user", Text = "blah" });
            history.Add(new ConversationTurn { Role = "kuddus", Text = res.Reply });
        }

        Assert.Fail("Conversation never closed within 20 turns.");
    }

    [Fact]
    public async Task Invariant7_pendingAction_always_carries_an_id_not_a_name()
    {
        var svc = ConversationHarness.Make(nameof(Invariant7_pendingAction_always_carries_an_id_not_a_name), out var db);
        db.LeaveRequests.Add(new AgenticHrmApi.Models.LeaveRequest
        {
            UserId = 3, StartDate = new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc),
            EndDate = new DateTime(2026, 8, 27, 0, 0, 0, DateTimeKind.Utc),
            Reason = "Family wedding", Status = "Pending",
            CreatedAt = new DateTime(2026, 8, 24, 0, 0, 0, DateTimeKind.Utc)
        });
        await db.SaveChangesAsync();

        var res = await svc.ProcessAsync(new ConverseRequest { UserId = 1, Text = "approve Rahim's leave" });

        Assert.NotNull(res.PendingAction!.LeaveId);
    }
}
