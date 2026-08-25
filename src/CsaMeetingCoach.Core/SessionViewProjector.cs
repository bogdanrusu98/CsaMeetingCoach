using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public static class SessionViewProjector
{
    public static MemberSessionView ForMember(MeetingSessionState session) =>
        new(
            session.Id,
            session.Purpose,
            session.Status,
            session.Template,
            session.AudienceFamiliarity,
            session.CreatedAtUtc,
            session.UpdatedAtUtc,
            session.ExpiresAtUtc,
            session.Revision,
            session.ContextualCards
                .Where(card => card.Audience is
                    ContextualCardAudience.Members or ContextualCardAudience.Everyone)
                .Where(card => card.MemberAlertStatus == MemberAlertStatus.Published)
                .Select(card => new MemberAlertView(
                    card.Id,
                    card.Kind,
                    card.Title,
                    card.Content,
                    card.CreatedAtUtc))
                .ToArray());
}
