using AgenticHrmApi.Services;

namespace AgenticHrmApi.Tests;

public sealed class FixedClock(DateTime at) : IClock
{
    public DateTime UtcNow { get; set; } = at;
}
