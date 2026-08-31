namespace AgenticHrmApi.Services;

public class LocalRuleReasoner : IReasoner
{
    public Task<ReasoningResult> ReasonAsync(ReasoningInput input, CancellationToken ct = default)
    {
        var text = input.Utterance.ToLowerInvariant();

        // 1. Explicit commands that override pending flows (Intent Switch)
        if (Has(text, "am i checked", "was i late", "my attendance"))
            return Done("query.attendance");

        if (Has(text, "check out", "checkout", "leaving", "chole jachhi") || (text.Contains("check") && text.Contains("out")))
            return Done("attendance.checkout");

        if (Has(text, "check in", "checkin", "arrived", "present", "eshechi") || (text.Contains("check") && text.Contains("in")))
            return Done("attendance.checkin");

        if (Has(text, "how many leave", "my leaves", "pending leave"))
            return Done("query.leaves");

        if (Has(text, "what can you do", "help", "who are you"))
            return Done("chat.help");

        // 2. Pending action responses (affirmative, negative, cancel, slot filling)
        if (input.Pending is not null)
        {
            var kind = AnswerClassifier.Classify(text);
            var intent = kind switch
            {
                AnswerKind.Affirmative => "control.confirm",
                AnswerKind.Negative    => "control.deny",
                AnswerKind.Cancelling  => "control.cancel",
                AnswerKind.Correction  => "control.confirm",
                _ => null
            };
            if (intent is not null) return Done(intent);

            if (input.Pending.Kind == "collectingSlots" && input.Pending.Intent == "leave.apply")
            {
                if (text.Contains("kal") || text.Contains("august") || text.Contains("tomorrow") || text.Contains("yesterday") || text.Contains("next week") || text.Contains(" to ") || text.Contains(" - "))
                {
                    var slots = new Dictionary<string, string>();
                    if (text.Contains("kal"))
                    {
                        slots["startDate"] = "ambiguous:kal";
                        return Task.FromResult(new ReasoningResult { Intent = "leave.apply", Slots = slots });
                    }

                    var parts = input.Utterance.Split(new[] { " to ", " - ", " until ", " through " }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length == 2)
                    {
                        if (DateTime.TryParse(parts[0].Trim() + $" {input.Today.Year}", out var sDt) &&
                            DateTime.TryParse(parts[1].Trim() + $" {input.Today.Year}", out var eDt))
                        {
                            slots["startDate"] = sDt.ToString("yyyy-MM-dd");
                            slots["endDate"] = eDt.ToString("yyyy-MM-dd");
                        }
                        else if (DateTime.TryParse(parts[0].Trim(), out sDt) &&
                                 DateTime.TryParse(parts[1].Trim(), out eDt))
                        {
                            slots["startDate"] = sDt.ToString("yyyy-MM-dd");
                            slots["endDate"] = eDt.ToString("yyyy-MM-dd");
                        }
                    }
                    else if (DateTime.TryParse(input.Utterance.Trim() + $" {input.Today.Year}", out var sDt))
                    {
                        slots["startDate"] = sDt.ToString("yyyy-MM-dd");
                    }
                    else if (DateTime.TryParse(input.Utterance.Trim(), out sDt))
                    {
                        slots["startDate"] = sDt.ToString("yyyy-MM-dd");
                    }

                    return Task.FromResult(new ReasoningResult { Intent = "leave.apply", Slots = slots });
                }
                else if (input.Pending.Slots.ContainsKey("startDate") && input.Pending.Slots.ContainsKey("endDate") && !input.Pending.Slots.ContainsKey("reason"))
                {
                    var slots = new Dictionary<string, string> { ["reason"] = input.Utterance.Trim() };
                    return Task.FromResult(new ReasoningResult { Intent = "leave.apply", Slots = slots });
                }
                else
                {
                    // Unusable slot input, return leave.apply with no new slots so re-ask count increases
                    return Done("leave.apply");
                }
            }

            if (input.Pending.Kind is "applyLeave" or "approveLeave" or "rejectLeave")
            {
                // Awaiting confirmation but got unparseable control answer
                return Done("control.confirm");
            }
        }

        // 3. New non-pending triggers
        if (Has(text, "approve", "reject"))
        {
            var intent = text.Contains("reject") ? "leave.reject" : "leave.approve";
            var slots = new Dictionary<string, string>();
            var whoText = text
                .Replace("approve", "")
                .Replace("reject", "")
                .Replace("'s", "")
                .Replace("leave", "")
                .Replace("chuti", "")
                .Trim();
            if (!string.IsNullOrWhiteSpace(whoText))
            {
                slots["who"] = whoText;
            }
            return Task.FromResult(new ReasoningResult { Intent = intent, Slots = slots });
        }

        if (Has(text, "leave", "chuti", "sick", "vacation"))
            return Done("leave.apply");

        return Done("chat.smalltalk");
    }

    private static bool Has(string text, params string[] needles) =>
        needles.Any(text.Contains);

    private static Task<ReasoningResult> Done(string intent) =>
        Task.FromResult(new ReasoningResult { Intent = intent });
}
