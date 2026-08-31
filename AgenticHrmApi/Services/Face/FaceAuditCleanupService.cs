using AgenticHrmApi.Data;
using Microsoft.EntityFrameworkCore;

namespace AgenticHrmApi.Services.Face;

public class FaceAuditCleanupService : BackgroundService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<FaceAuditCleanupService> _logger;
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(24);

    public FaceAuditCleanupService(
        IServiceProvider serviceProvider,
        ILogger<FaceAuditCleanupService> logger)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PruneOldAttemptsAsync(stoppingToken);
            }
            catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.LogError(ex, "Error occurred during face login attempt audit log cleanup.");
            }

            try
            {
                await Task.Delay(CheckInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    public async Task<int> PruneOldAttemptsAsync(CancellationToken cancellationToken = default)
    {
        using var scope = _serviceProvider.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var cutoff = DateTime.UtcNow.AddDays(-FaceTuning.AttemptRetentionDays);
        var deletedCount = await db.FaceLoginAttempts
            .Where(a => a.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);

        if (deletedCount > 0)
        {
            _logger.LogInformation("Pruned {Count} face login attempt logs older than {Cutoff}.", deletedCount, cutoff);
        }

        return deletedCount;
    }
}
