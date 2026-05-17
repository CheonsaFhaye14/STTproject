using Microsoft.EntityFrameworkCore;
using STTproject.Data;

public class PriceIncreaseWorker : BackgroundService
{
    private static readonly TimeSpan PollInterval = TimeSpan.FromMinutes(5);

    private readonly IDbContextFactory<SttprojectContext> _dbContextFactory;
    private readonly ILogger<PriceIncreaseWorker> _logger;

    public PriceIncreaseWorker(
        IDbContextFactory<SttprojectContext> dbContextFactory,
        ILogger<PriceIncreaseWorker> logger)
    {
        _dbContextFactory = dbContextFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(PollInterval);

        do
        {
            try
            {
                await ExecuteStoredProcedureAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to apply due price increases via stored procedure.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }

    private async Task ExecuteStoredProcedureAsync(CancellationToken stoppingToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(stoppingToken);
        await db.Database.ExecuteSqlRawAsync(
            "EXEC ojt.sp_ApplyDuePriceIncrease",
            stoppingToken);
    }
}