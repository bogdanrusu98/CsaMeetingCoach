namespace CsaMeetingCoach.Core;

public sealed class SessionExpiredException(Guid sessionId)
    : InvalidOperationException($"Meeting session {sessionId} has expired.")
{
    public Guid SessionId { get; } = sessionId;
}
