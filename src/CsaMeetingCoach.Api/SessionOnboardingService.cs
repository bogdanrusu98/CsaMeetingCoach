using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

public sealed class SessionOnboardingService(
    MeetingSessionCoordinator coordinator,
    IMeetingSessionStore sessionStore,
    ISessionJoinCodeStore joinCodes,
    TimeProvider timeProvider)
{
    public async Task<(MeetingSessionState Session, SessionParticipantState Host, string Code)>
        CreateHostSessionAsync(
            CreateMeetingSessionRequest request,
            CancellationToken cancellationToken)
    {
        var session = await coordinator.CreateAsync(request, cancellationToken);
        try
        {
            var code = await joinCodes.CreateAsync(
                session.Id,
                session.ExpiresAtUtc,
                cancellationToken);
            var host = session.Participants.Single(participant =>
                participant.Role == SessionRole.Host);
            return (session, host, code);
        }
        catch
        {
            if (sessionStore is IExpiringMeetingSessionStore expiringStore)
            {
                await expiringStore.DeleteAsync(session.Id, CancellationToken.None);
            }

            throw;
        }
    }

    public async Task<(MeetingSessionState Session, SessionParticipantState Member)>
        JoinSessionAsync(
            JoinMeetingSessionRequest request,
            CancellationToken cancellationToken)
    {
        var sessionId = await joinCodes.ResolveAsync(request.Code, cancellationToken);
        if (sessionId is null)
        {
            throw new KeyNotFoundException("The session code is invalid or expired.");
        }

        var session = await coordinator.GetAsync(sessionId.Value, cancellationToken);
        if (session is null || SessionLifecycle.IsExpired(session, timeProvider.GetUtcNow()))
        {
            await joinCodes.DeleteAsync(sessionId.Value, cancellationToken);
            throw new KeyNotFoundException("The session code is invalid or expired.");
        }

        if (session.Status != MeetingSessionStatus.Active)
        {
            throw new InvalidOperationException("This session is no longer accepting members.");
        }

        return await coordinator.JoinParticipantAsync(
            session.Id,
            request.DisplayName,
            cancellationToken);
    }
}
