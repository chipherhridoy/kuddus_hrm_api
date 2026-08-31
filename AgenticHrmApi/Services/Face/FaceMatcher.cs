namespace AgenticHrmApi.Services.Face;

public readonly record struct MatchResult(int? UserId, float Score, float Margin, string Outcome);

public static class FaceMatcher
{
    public static MatchResult BestMatch(float[] probe, IReadOnlyList<(int UserId, float[] Vec)> templates)
    {
        if (templates.Count == 0)
            return new MatchResult(null, 0f, 0f, "NoMatch");

        var bestScoreByUser = new Dictionary<int, float>();

        foreach (var t in templates)
        {
            float score = CosineSimilarity(probe, t.Vec);
            if (!bestScoreByUser.TryGetValue(t.UserId, out float currentBest) || score > currentBest)
            {
                bestScoreByUser[t.UserId] = score;
            }
        }

        var sorted = bestScoreByUser.OrderByDescending(kvp => kvp.Value).ToList();
        var best = sorted[0];

        if (best.Value < FaceTuning.MatchThreshold)
            return new MatchResult(null, best.Value, 0f, "NoMatch");

        float margin = 0f;
        if (sorted.Count > 1)
        {
            var secondBest = sorted[1];
            margin = best.Value - secondBest.Value;
            if (margin < FaceTuning.IdentityMargin)
            {
                return new MatchResult(null, best.Value, margin, "AmbiguousMatch");
            }
        }

        return new MatchResult(best.Key, best.Value, margin, "Success");
    }

    public static float CosineSimilarity(float[] a, float[] b)
    {
        if (a == null || b == null || a.Length != b.Length || a.Length == 0) return 0f;
        float dot = 0f, magA = 0f, magB = 0f;
        for (int i = 0; i < a.Length; i++)
        {
            dot += a[i] * b[i];
            magA += a[i] * a[i];
            magB += b[i] * b[i];
        }
        if (magA == 0 || magB == 0) return 0f;
        return (float)(dot / (Math.Sqrt(magA) * Math.Sqrt(magB)));
    }
}
