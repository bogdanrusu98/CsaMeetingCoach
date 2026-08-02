using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public interface IConversationCoachAgent
{
    Task<CoachAgentDecision> AnalyzeAsync(
        CoachAgentContext context,
        TranscriptSegment latestSegment,
        CancellationToken cancellationToken);
}

public interface IMeetingChecklistPlanner
{
    IReadOnlyList<ChecklistItemState> CreateChecklist(
        MeetingPurpose purpose,
        IReadOnlyList<ChecklistSeed>? requestedChecklist);
}

public interface IMeetingSessionStore
{
    Task<MeetingSessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    Task SaveAsync(MeetingSessionState session, CancellationToken cancellationToken);
}

public interface ISessionUpdatePublisher
{
    Task PublishAsync(MeetingSessionState session, CancellationToken cancellationToken);
}

public sealed class NullSessionUpdatePublisher : ISessionUpdatePublisher
{
    public Task PublishAsync(MeetingSessionState session, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed record AnalysisOptions(TimeSpan DebounceWindow, TimeSpan AiTimeout)
{
    public static readonly AnalysisOptions Default = new(
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(30));

    public void Validate()
    {
        if (DebounceWindow < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(DebounceWindow),
                "The analysis debounce window cannot be negative.");
        }

        if (AiTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(AiTimeout),
                "The AI analysis timeout must be positive.");
        }
    }
}
