namespace CsaMeetingCoach.Contracts;

public enum MeetingSessionStatus
{
    Active,
    Completed
}

public enum ChecklistItemStatus
{
    Pending,
    Completed
}

public enum RecommendationStatus
{
    Proposed = 0,
    Accepted = 1,
    Dismissed = 2,
    Completed = 3
}

public sealed record MeetingPurpose(
    string Title,
    string MeetingType,
    string Objective,
    IReadOnlyList<string> SuccessCriteria);

public sealed record ChecklistSeed(
    string Title,
    string CompletionCriteria,
    IReadOnlyList<string> EvidenceHints);

public sealed record CreateMeetingSessionRequest(
    MeetingPurpose Purpose,
    IReadOnlyList<ChecklistSeed>? Checklist = null,
    string? TeamsOnlineMeetingId = null);

public sealed record AddTranscriptSegmentRequest(
    string Speaker,
    string Text,
    DateTimeOffset? OccurredAtUtc = null,
    bool IsFinal = true,
    Guid? SourceSegmentId = null);

public sealed record TranscriptSegment(
    Guid Id,
    string Speaker,
    string Text,
    DateTimeOffset OccurredAtUtc,
    bool IsFinal,
    Guid? SourceSegmentId = null,
    DateTimeOffset? AnalyzedAtUtc = null);

public sealed record ChecklistEvidence(
    Guid TranscriptSegmentId,
    string Speaker,
    string Quote,
    DateTimeOffset OccurredAtUtc,
    double Confidence);

public sealed record ChecklistItemState(
    Guid Id,
    string Title,
    string CompletionCriteria,
    IReadOnlyList<string> EvidenceHints,
    ChecklistItemStatus Status,
    bool AutoCompleted,
    double? Confidence,
    string? CompletionReason,
    DateTimeOffset? CompletedAtUtc,
    IReadOnlyList<ChecklistEvidence> Evidence);

public sealed record RecommendedTaskState(
    Guid Id,
    string Title,
    string Rationale,
    double Confidence,
    IReadOnlyList<Guid> SourceTranscriptSegmentIds,
    RecommendationStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? AcceptedAtUtc = null,
    DateTimeOffset? CompletedAtUtc = null,
    string? CompletionReason = null,
    IReadOnlyList<ChecklistEvidence>? Evidence = null);

public sealed record MeetingSessionState(
    Guid Id,
    MeetingPurpose Purpose,
    MeetingSessionStatus Status,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    long Revision,
    IReadOnlyList<ChecklistItemState> Checklist,
    IReadOnlyList<TranscriptSegment> Transcript,
    IReadOnlyList<RecommendedTaskState> RecommendedTasks,
    IReadOnlyList<string> Warnings,
    string? TeamsOnlineMeetingId = null);

public sealed record AdapterTranscriptSegmentRequest(
    string TeamsOnlineMeetingId,
    AddTranscriptSegmentRequest Segment);

public sealed record ChecklistEvaluation(
    Guid ChecklistItemId,
    bool ShouldComplete,
    double Confidence,
    string Reason,
    string EvidenceQuote);

public sealed record RecommendedTaskProposal(
    string Title,
    string Rationale,
    double Confidence,
    IReadOnlyList<Guid> SourceTranscriptSegmentIds);

public sealed record RecommendationEvaluation(
    Guid RecommendationId,
    bool ShouldComplete,
    double Confidence,
    string Reason,
    string EvidenceQuote);

public sealed record CoachAgentContext(
    MeetingPurpose Purpose,
    IReadOnlyList<ChecklistItemState> Checklist,
    IReadOnlyList<TranscriptSegment> RecentTranscript,
    IReadOnlyList<RecommendedTaskState>? RecommendedTasks = null);

public sealed record CoachAgentDecision(
    IReadOnlyList<ChecklistEvaluation> ChecklistEvaluations,
    IReadOnlyList<RecommendedTaskProposal> RecommendedTasks,
    IReadOnlyList<RecommendationEvaluation>? RecommendationEvaluations = null);
