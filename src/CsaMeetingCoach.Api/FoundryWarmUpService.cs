using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

internal sealed class FoundryWarmUpService(
    IFoundryAgentClient foundryClient,
    ILogger<FoundryWarmUpService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        cts.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            await foundryClient.WarmUpAsync(cts.Token);
            logger.LogInformation("Foundry agent warm-up completed successfully.");
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation("Foundry agent warm-up stopped during application shutdown.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Foundry agent warm-up failed. The first analysis will retry agent initialization.");
        }
    }
}
