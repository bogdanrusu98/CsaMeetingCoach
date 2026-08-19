namespace CsaMeetingCoach.Contracts;

public enum MeetingSessionStatus
{
    Active,
    Completed,
    Expired
}

public enum SessionRole
{
    Host,
    Member
}

public enum SessionTemplateKind
{
    Presentation,
    Workshop,
    Training,
    Custom,
    CsaVbd
}

public enum MemberAlertDeliveryMode
{
    Manual,
    SafeAutomatic,
    Automatic
}

public enum ContextualCardAudience
{
    Host,
    Members,
    Everyone
}

public enum MemberAlertStatus
{
    PendingApproval,
    Published,
    Hidden
}

public enum SessionParticipantStatus
{
    Active,
    Removed
}

public enum KnowledgeSourceKind
{
    File,
    Link
}

public enum KnowledgeSourceVisibility
{
    HostPrivate,
    MemberEligible
}

public enum KnowledgeSourceStatus
{
    PendingScan,
    Processing,
    Ready,
    Rejected,
    Failed
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

public enum ContextualCardKind
{
    Definition,
    Hint
}

public enum AlertRejectionReason
{
    None,
    UnknownKind,
    SalesOrRecommendationContent,
    MissingEvidence,
    ContentFingerprint,
    CooldownActive,
    VendorMismatch,
    NegatedMention,
    MentionNotFound,
    InsufficientRanking
}

public readonly record struct AlertDiagnostic(
    ContextualCardKind Kind,
    string ConceptKey,
    AlertRejectionReason Reason,
    string Details);

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
    string? TeamsOnlineMeetingId = null,
    SessionTemplateKind Template = SessionTemplateKind.CsaVbd,
    string? HostDisplayName = null,
    MemberAlertDeliveryMode MemberAlertMode = MemberAlertDeliveryMode.SafeAutomatic);

public sealed record JoinMeetingSessionRequest(
    string Code,
    string DisplayName);

public sealed record AddKnowledgeLinkRequest(
    Uri Url,
    string? DisplayName = null,
    KnowledgeSourceVisibility Visibility = KnowledgeSourceVisibility.HostPrivate);

public sealed record CreateHostSessionResponse(
    MeetingSessionState Session,
    string JoinCode);

public sealed record JoinMeetingSessionResponse(
    MemberSessionView Session);

public sealed record AddTranscriptSegmentRequest(
    string Speaker,
    string Text,
    DateTimeOffset? OccurredAtUtc = null,
    bool IsFinal = true,
    Guid? SourceSegmentId = null,
    bool IsSpeechRecognized = false);

public sealed record TranscriptSegment(
    Guid Id,
    string Speaker,
    string Text,
    DateTimeOffset OccurredAtUtc,
    bool IsFinal,
    Guid? SourceSegmentId = null,
    DateTimeOffset? AnalyzedAtUtc = null)
{
    /// <summary>Exact original recognized text, set only when a contextual correction occurred. Null otherwise.</summary>
    public string? RecognizedText { get; init; }

    /// <summary>Short reason code for the correction (e.g. "AsiaToAzure:EcosystemAnchor"). Null when no correction.</summary>
    public string? CorrectionReason { get; init; }
}

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
    IReadOnlyList<ChecklistEvidence> Evidence,
    int? CompletionEligibleFromTranscriptIndex = null);

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
    IReadOnlyList<ChecklistEvidence>? Evidence = null,
    int? CompletionEligibleFromTranscriptIndex = null)
{
    public IReadOnlyList<Guid> KnowledgeSourceIds { get; init; } = [];
}

public sealed record ContextualCardState(
    Guid Id,
    ContextualCardKind Kind,
    string Title,
    string Content,
    double Confidence,
    IReadOnlyList<Guid> SourceTranscriptSegmentIds,
    DateTimeOffset CreatedAtUtc)
{
    public string? ConceptKey { get; init; }
    public ContextualCardAudience Audience { get; init; } =
        ContextualCardAudience.Everyone;
    public MemberAlertStatus MemberAlertStatus { get; init; } =
        MemberAlertStatus.Published;
    public IReadOnlyList<Guid> KnowledgeSourceIds { get; init; } = [];
}

public sealed record SessionParticipantState(
    Guid Id,
    string DisplayName,
    SessionRole Role,
    SessionParticipantStatus Status,
    DateTimeOffset JoinedAtUtc);

public sealed record KnowledgeSourceState(
    Guid Id,
    KnowledgeSourceKind Kind,
    string DisplayName,
    KnowledgeSourceVisibility Visibility,
    KnowledgeSourceStatus Status,
    DateTimeOffset CreatedAtUtc,
    long SizeBytes = 0,
    string? MediaType = null,
    Uri? SourceUri = null,
    string? FailureReason = null);

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
    string? TeamsOnlineMeetingId = null,
    bool IsAnalyzing = false,
    int StateSchemaVersion = 0)
{
    public const int CurrentSchemaVersion = 6;

    public IReadOnlyList<ContextualCardState> ContextualCards { get; init; } = [];
    public SessionTemplateKind Template { get; init; } = SessionTemplateKind.CsaVbd;
    public MemberAlertDeliveryMode MemberAlertMode { get; init; } =
        MemberAlertDeliveryMode.SafeAutomatic;
    public string TenantId { get; init; } = "local";
    public DateTimeOffset ExpiresAtUtc { get; init; }
    public IReadOnlyList<SessionParticipantState> Participants { get; init; } = [];
    public IReadOnlyList<KnowledgeSourceState> KnowledgeSources { get; init; } = [];
    public HashSet<string> ShownDefinitionKeys { get; init; } =
        new(StringComparer.Ordinal);
    public HashSet<string> ShownHintKeys { get; init; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> DefinitionCooldowns { get; init; } =
        new(StringComparer.Ordinal);
    public Dictionary<string, DateTimeOffset> HintCooldowns { get; init; } =
        new(StringComparer.Ordinal);
    public HashSet<string> ContentFingerprints { get; init; } =
        new(StringComparer.Ordinal);
    public int? LastEducationalCardSegmentIndex { get; init; }
}

public sealed record MemberSessionView(
    Guid Id,
    MeetingPurpose Purpose,
    MeetingSessionStatus Status,
    SessionTemplateKind Template,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset UpdatedAtUtc,
    DateTimeOffset ExpiresAtUtc,
    long Revision,
    IReadOnlyList<MemberAlertView> Alerts);

public sealed record MemberAlertView(
    Guid Id,
    ContextualCardKind Kind,
    string Title,
    string Content,
    DateTimeOffset CreatedAtUtc);

public sealed record AdapterTranscriptSegmentRequest(
    string TeamsOnlineMeetingId,
    AddTranscriptSegmentRequest Segment);

public sealed record ChecklistEvaluation(
    Guid ChecklistItemId,
    bool ShouldComplete,
    double Confidence,
    string Reason,
    string EvidenceQuote,
    Guid? SourceTranscriptSegmentId = null);

public sealed record RecommendedTaskProposal(
    string Title,
    string Rationale,
    double Confidence,
    IReadOnlyList<Guid> SourceTranscriptSegmentIds);

public sealed record ContextualCardProposal(
    ContextualCardKind Kind,
    string Title,
    string Content,
    double Confidence,
    IReadOnlyList<Guid> SourceTranscriptSegmentIds)
{
    public string? ConceptKey { get; init; }
}

public sealed record RecommendationEvaluation(
    Guid RecommendationId,
    bool ShouldComplete,
    double Confidence,
    string Reason,
    string EvidenceQuote,
    Guid? SourceTranscriptSegmentId = null);

public sealed record CoachAgentContext(
    MeetingPurpose Purpose,
    IReadOnlyList<ChecklistItemState> Checklist,
    IReadOnlyList<TranscriptSegment> RecentTranscript,
    IReadOnlyList<RecommendedTaskState>? RecommendedTasks = null,
    IReadOnlyList<ContextualCardState>? ContextualCards = null,
    SessionTemplateKind Template = SessionTemplateKind.CsaVbd,
    IReadOnlyList<SessionKnowledgeSnippet>? Knowledge = null);

public sealed record SessionKnowledgeSnippet(
    Guid SourceId,
    string DisplayName,
    KnowledgeSourceVisibility Visibility,
    string Content);

public sealed record CoachAgentDecision(
    IReadOnlyList<ChecklistEvaluation> ChecklistEvaluations,
    IReadOnlyList<RecommendedTaskProposal> RecommendedTasks,
    IReadOnlyList<RecommendationEvaluation>? RecommendationEvaluations = null)
{
    public IReadOnlyList<ContextualCardProposal> ContextualCards { get; init; } = [];
}
