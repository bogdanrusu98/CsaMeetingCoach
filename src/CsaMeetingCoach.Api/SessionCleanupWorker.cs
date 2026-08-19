using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

public sealed class SessionCleanupWorker(
    IMeetingSessionStore sessionStore,
    ISessionJoinCodeStore joinCodes,
    IEnumerable<ISessionArtifactCleaner> artifactCleaners,
    SessionEventBroker eventBroker,
    MeetingSessionCoordinator coordinator,
    TimeProvider timeProvider,
    ILogger<SessionCleanupWorker> logger) : BackgroundService
{
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DeleteExpiredSessionsAsync(stoppingToken);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                || !stoppingToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "The expired-session cleanup sweep failed and will be retried.");
            }

            await Task.Delay(SweepInterval, timeProvider, stoppingToken);
        }
    }

    public async Task DeleteExpiredSessionsAsync(CancellationToken cancellationToken)
    {
        if (sessionStore is not IExpiringMeetingSessionStore expiringStore)
        {
            logger.LogWarning(
                "Session cleanup is disabled because the configured store cannot enumerate sessions.");
            return;
        }

        await foreach (var session in expiringStore.ListAsync(cancellationToken))
        {
            if (!SessionLifecycle.IsExpired(session, timeProvider.GetUtcNow()))
            {
                continue;
            }

            coordinator.CancelSessionAnalysis(session.Id);
            eventBroker.CloseSession(session.Id);
            try
            {
                foreach (var cleaner in artifactCleaners)
                {
                    await cleaner.DeleteSessionArtifactsAsync(
                        session.Id,
                        cancellationToken);
                }

                await joinCodes.DeleteAsync(session.Id, cancellationToken);
                await expiringStore.DeleteAsync(session.Id, cancellationToken);
                logger.LogInformation(
                    "Expired session artifacts were deleted. SessionId: {SessionId}",
                    session.Id);
            }
            catch (Exception exception) when (
                exception is not OperationCanceledException
                || !cancellationToken.IsCancellationRequested)
            {
                logger.LogError(
                    exception,
                    "Expired session cleanup failed and will be retried. SessionId: {SessionId}",
                    session.Id);
            }
        }
    }
}
