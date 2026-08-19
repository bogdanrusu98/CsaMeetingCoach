using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed class SessionUpdateNotificationException(
    MeetingSessionState persistedSession,
    Exception innerException)
    : InvalidOperationException(
        "The session update was saved, but realtime notification failed.",
        innerException)
{
    public MeetingSessionState PersistedSession { get; } = persistedSession;
}
