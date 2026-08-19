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
        IReadOnlyList<ChecklistSeed>? requestedChecklist,
        SessionTemplateKind template = SessionTemplateKind.CsaVbd);
}

public interface IMeetingSessionStore
{
    Task<MeetingSessionState?> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    Task SaveAsync(MeetingSessionState session, CancellationToken cancellationToken);
}

public interface IExpiringMeetingSessionStore : IMeetingSessionStore
{
    IAsyncEnumerable<MeetingSessionState> ListAsync(CancellationToken cancellationToken);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken);
}

public interface ISessionJoinCodeStore
{
    Task<string> CreateAsync(
        Guid sessionId,
        DateTimeOffset expiresAtUtc,
        CancellationToken cancellationToken);

    Task<Guid?> ResolveAsync(string code, CancellationToken cancellationToken);

    Task DeleteAsync(Guid sessionId, CancellationToken cancellationToken);
}

public interface ISessionArtifactCleaner
{
    Task DeleteSessionArtifactsAsync(
        Guid sessionId,
        CancellationToken cancellationToken);
}

public interface ISessionKnowledgeReader
{
    Task<IReadOnlyList<SessionKnowledgeSnippet>> ReadAsync(
        Guid sessionId,
        IReadOnlyCollection<Guid> allowedSourceIds,
        CancellationToken cancellationToken);
}

public interface ISessionUpdatePublisher
{
    Task PublishAsync(MeetingSessionState session, CancellationToken cancellationToken);
}

public static class SessionLifecycle
{
    public static readonly TimeSpan Lifetime = TimeSpan.FromHours(24);

    public static bool IsExpired(
        MeetingSessionState session,
        DateTimeOffset nowUtc) =>
        session.ExpiresAtUtc != default && nowUtc >= session.ExpiresAtUtc;
}

public static class SessionKnowledgeLimits
{
    public const int MaximumSourceCount = 50;
    public const long MaximumFileBytes = 50L * 1024 * 1024;
    public const long MaximumTotalBytes = 100L * 1024 * 1024;
    public const int MaximumExtractedCharactersPerSource = 100_000;
    public const int MaximumPromptCharacters = 40_000;
}

public sealed class NullSessionUpdatePublisher : ISessionUpdatePublisher
{
    public Task PublishAsync(MeetingSessionState session, CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}

public sealed record AnalysisOptions(
    TimeSpan DebounceWindow,
    TimeSpan AiTimeout,
    TimeSpan? MaximumBatchWindow = null)
{
    public static readonly AnalysisOptions Default = new(
        TimeSpan.FromSeconds(8),
        TimeSpan.FromSeconds(60),
        TimeSpan.FromSeconds(20));

    public TimeSpan EffectiveMaximumBatchWindow =>
        MaximumBatchWindow ?? TimeSpan.FromSeconds(20);

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

        if (EffectiveMaximumBatchWindow <= TimeSpan.Zero
            || EffectiveMaximumBatchWindow < DebounceWindow)
        {
            throw new ArgumentOutOfRangeException(
                nameof(MaximumBatchWindow),
                "The maximum analysis batch window must be positive and no shorter than the debounce window.");
        }
    }
}

internal static class TranscriptAnalysisWindow
{
    public const int MaximumSegments = 20;

    public static IReadOnlyList<TranscriptSegment> Select(
        IEnumerable<TranscriptSegment> transcript)
    {
        return transcript
            .Where(segment => segment.IsFinal)
            .TakeLast(MaximumSegments)
            .ToArray();
    }
}
