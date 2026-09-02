using AgenticHrmApi.Contracts;
using AgenticHrmApi.Data;
using AgenticHrmApi.Models;

namespace AgenticHrmApi.Services.Intents;

public class LeaveIntentHandler(AppDbContext db, IClock clock) : IIntentHandler
{
    public const string SlotStart  = "startDate";
    public const string SlotEnd    = "endDate";
    public const string SlotReason = "reason";

    /// Sentinel the reasoner emits for a word it cannot resolve, e.g. bare "kal".
    public const string AmbiguousPrefix = "ambiguous:";

    public bool CanHandle(IntentContext ctx) => ctx.Intent == "leave.apply";

    public Task<HandlerResult> HandleAsync(IntentContext ctx, CancellationToken ct = default)
    {
        var slots = new Dictionary<string, string>(ctx.Slots);

        foreach (var key in new[] { SlotStart, SlotEnd })
        {
            if (slots.TryGetValue(key, out var v) && v.StartsWith(AmbiguousPrefix))
                return Task.FromResult(HandlerResult.Open(
                    "Kal — tomorrow, or yesterday?", Collecting(ctx, slots)));
        }

        if (!slots.ContainsKey(SlotStart))
            return Task.FromResult(HandlerResult.Open("Sure — which dates?", Collecting(ctx, slots)));

        if (!slots.ContainsKey(SlotEnd))
            return Task.FromResult(HandlerResult.Open("Until when?", Collecting(ctx, slots)));

        if (!slots.ContainsKey(SlotReason))
            return Task.FromResult(HandlerResult.Open("Got it. What's the reason?", Collecting(ctx, slots)));

        // All slots present — read back from the slot values, never from the model.
        var readBack = $"Leave from {slots[SlotStart]} to {slots[SlotEnd]}, {slots[SlotReason]}. Shall I submit it?";

        return Task.FromResult(HandlerResult.Open(readBack, new PendingAction
        {
            Kind = "applyLeave",
            Intent = "leave.apply",
            Slots = slots,
            IssuedAt = clock.UtcNow
        }));
    }

    public async Task<HandlerResult> CommitAsync(IntentContext ctx, CancellationToken ct = default)
    {
        var pending = ctx.Pending!;
        var user = ctx.User;
        var start = DateTime.SpecifyKind(DateTime.Parse(pending.Slots[SlotStart]), DateTimeKind.Utc);
        var end   = DateTime.SpecifyKind(DateTime.Parse(pending.Slots[SlotEnd]),   DateTimeKind.Utc);

        db.LeaveRequests.Add(new LeaveRequest
        {
            UserId = user.Id,
            StartDate = start,
            EndDate = end,
            Reason = pending.Slots[SlotReason],
            Status = "Pending",
            CreatedAt = clock.UtcNow
        });
        await db.SaveChangesAsync(ct);

        return HandlerResult.Acted($"Submitted, {start:MMM d} to {end:MMM d}. Anything else?");
    }

    private PendingAction Collecting(IntentContext ctx, Dictionary<string, string> slots) => new()
    {
        Kind = "collectingSlots",
        Intent = "leave.apply",
        Slots = slots,
        IssuedAt = clock.UtcNow,
        Attempts = (ctx.Pending?.Attempts ?? 0)
    };
}
