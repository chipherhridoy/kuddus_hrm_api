using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using AgenticHrmApi.Models;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Services.Intents;

public class ManagerIntentHandler(AppDbContext db, IClock clock) : IIntentHandler
{
    public const string SlotWho = "who";
    public static readonly TimeSpan PendingActionTtl = TimeSpan.FromMinutes(2);

    public bool CanHandle(string intent) => intent is "leave.approve" or "leave.reject";

    public async Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default)
    {
        if (!IsAdmin(ctx))
            return HandlerResult.Open("Only an admin can approve or reject leave.");

        if (!ctx.Slots.TryGetValue(SlotWho, out var who) || string.IsNullOrWhiteSpace(who))
            return HandlerResult.Open("Whose leave request?");

        var approving = ctx.Intent == "leave.approve";

        // Pending only — an already-decided row is never a candidate.
        var candidates = await db.LeaveRequests
            .Include(l => l.User)
            .Where(l => l.Status == "Pending" && l.User != null &&
                        l.User.Name.ToLower().Contains(who.ToLower()))
            .ToListAsync(ct);

        if (candidates.Count == 0)
            return HandlerResult.Open($"I don't see a pending leave request for {who}.");

        var people = candidates.Select(c => c.UserId).Distinct().ToList();
        if (people.Count > 1)
        {
            var opts = candidates
                .GroupBy(c => c.UserId)
                .Select(g => $"{g.First().User!.Name} in {g.First().User!.Department}")
                .ToList();
            return HandlerResult.Open($"{string.Join(", or ", opts)}?", Collecting(ctx));
        }

        if (candidates.Count > 1)
        {
            var ranges = candidates.Select(c => $"{c.StartDate:MMM d} to {c.EndDate:MMM d}");
            return HandlerResult.Open(
                $"{candidates[0].User!.Name} has {candidates.Count} pending — {string.Join(", and ", ranges)}. Which one?",
                Collecting(ctx));
        }

        var leave = candidates[0];
        var verb = approving ? "Approve" : "Reject";
        return HandlerResult.Open(
            $"{leave.User!.Name}, {leave.StartDate:MMM d} to {leave.EndDate:MMM d}, {leave.Reason}. {verb} it?",
            new PendingAction
            {
                Kind = approving ? "approveLeave" : "rejectLeave",
                Intent = ctx.Intent,
                LeaveId = leave.Id,
                IssuedAt = clock.UtcNow
            });
    }

    /// Re-validates from scratch. Never trusts the incoming token (spec 7.4).
    public async Task<HandlerResult> CommitAsync(IntentContext ctx, CancellationToken ct = default)
    {
        var pending = ctx.Pending!;
        if (!IsAdmin(ctx))
            return HandlerResult.Open("Only an admin can approve or reject leave.");

        if (clock.UtcNow - pending.IssuedAt > PendingActionTtl)
            return HandlerResult.Open("That took too long — ask me again.");

        if (pending.LeaveId is null)
            return HandlerResult.Open("I lost track of which request. Ask me again.");

        var leave = await db.LeaveRequests.Include(l => l.User)
            .FirstOrDefaultAsync(l => l.Id == pending.LeaveId.Value, ct);

        if (leave is null)  return HandlerResult.Open("That request no longer exists.");
        if (leave.Status != "Pending") return HandlerResult.Open($"That one is already {leave.Status.ToLower()}.");

        leave.Status = pending.Kind == "approveLeave" ? "Approved" : "Rejected";
        await db.SaveChangesAsync(ct);

        return HandlerResult.Acted($"{leave.Status}. Anything else?");
    }

    private static bool IsAdmin(IntentContext ctx) => 
        ctx.Principal?.IsInRole("Admin") ?? ctx.User.Role.Equals("Admin", StringComparison.OrdinalIgnoreCase);

    private PendingAction Collecting(IntentContext ctx) => new()
    {
        Kind = "collectingSlots",
        Intent = ctx.Intent,
        Slots = new Dictionary<string, string>(ctx.Slots),
        IssuedAt = clock.UtcNow,
        Attempts = ctx.Pending?.Attempts ?? 0
    };
}
