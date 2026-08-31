using System.Text.RegularExpressions;

namespace AgenticHrmApi.Services;

public enum AnswerKind { Affirmative, Negative, Cancelling, Correction, Other }

public static class AnswerClassifier
{
    // Longest-first so "lagbe na" wins over "na", and "never mind" over "no".
    private static readonly string[] Cancelling =
        ["never mind", "forget it", "baad dao", "cancel", "thak"];

    private static readonly string[] Negative =
        ["lagbe na", "nope", "nah", "don't", "stop", "no", "na", "naa"];

    private static readonly string[] Affirmative =
        ["please do", "go ahead", "thik ache", "confirm", "submit", "do it",
         "haan", "accha", "korun", "sure", "okay", "yeah", "yep", "yup",
         "yes", "ok", "ha", "hae", "ji", "jee"];

    /// Extra tokens beyond the matched keyword before a negative counts as a correction.
    public const int CorrectionTokenThreshold = 2;

    public static AnswerKind Classify(string utterance)
    {
        var norm = Normalise(utterance);
        if (norm.Length == 0) return AnswerKind.Other;

        var neg = FirstMatch(norm, Negative);
        if (neg is not null)
        {
            var remaining = TokenCount(norm) - TokenCount(neg);
            if (remaining >= CorrectionTokenThreshold) return AnswerKind.Correction;
        }

        if (FirstMatch(norm, Cancelling) is not null) return AnswerKind.Cancelling;
        if (neg is not null) return AnswerKind.Negative;
        if (FirstMatch(norm, Affirmative) is not null) return AnswerKind.Affirmative;
        return AnswerKind.Other;
    }

    private static string Normalise(string s) =>
        Regex.Replace(Regex.Replace(s.ToLowerInvariant(), @"[^\p{L}\p{N}\s]", " "), @"\s+", " ").Trim();

    private static int TokenCount(string s) =>
        s.Length == 0 ? 0 : s.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    private static string? FirstMatch(string norm, string[] phrases) =>
        phrases.FirstOrDefault(p => Regex.IsMatch(norm, $@"\b{Regex.Escape(p)}\b"));
}
