using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Api;

public sealed class SessionAuthorizationService(
    SessionAccessTokenService accessTokens,
    MeetingSessionCoordinator coordinator,
    TimeProvider timeProvider)
{
    public async Task<AuthorizedSession> RequireAsync(
        HttpContext context,
        Guid sessionId,
        SessionRole? requiredRole,
        CancellationToken cancellationToken)
    {
        if (!accessTokens.TryGetAccess(context, sessionId, out var grant))
        {
            throw new UnauthorizedAccessException(
                "A valid access token for this session is required.");
        }

        if (grant.ExpiresAtUtc <= timeProvider.GetUtcNow())
        {
            throw new SessionExpiredException(sessionId);
        }

        var session = await coordinator.GetAsync(sessionId, cancellationToken)
            ?? throw new KeyNotFoundException($"Meeting session {sessionId} was not found.");
        if (SessionLifecycle.IsExpired(session, timeProvider.GetUtcNow()))
        {
            throw new SessionExpiredException(sessionId);
        }

        var participant = session.Participants.SingleOrDefault(candidate =>
            candidate.Id == grant.ParticipantId
            && candidate.Role == grant.Role
            && candidate.Status == SessionParticipantStatus.Active);
        if (participant is null || requiredRole is { } role && grant.Role != role)
        {
            throw new UnauthorizedAccessException(
                "This participant does not have permission for the requested operation.");
        }

        return new AuthorizedSession(session, participant, grant);
    }
}

public sealed record AuthorizedSession(
    MeetingSessionState Session,
    SessionParticipantState Participant,
    SessionAccessGrant Grant);
