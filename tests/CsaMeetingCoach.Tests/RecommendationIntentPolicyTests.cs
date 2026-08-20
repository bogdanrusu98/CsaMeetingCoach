using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class RecommendationIntentPolicyTests
{
    [Theory]
    [InlineData(
        "Clearly introduce the key messages and objectives early in the presentation",
        RecommendationIntentPolicy.PresentationOpeningOutcome)]
    [InlineData(
        "Add an explicit closing summary and next steps to conclude the presentation on Entra ID",
        RecommendationIntentPolicy.PresentationClosingRecapActions)]
    [InlineData(
        "Add a concise, audience-relevant example illustrating lifecycle workflows",
        RecommendationIntentPolicy.PresentationLifecycleExample)]
    [InlineData(
        "Illustrate onboarding and offboarding with a practical identity lifecycle story",
        RecommendationIntentPolicy.PresentationLifecycleExample)]
    public void Classify_MapsReportParaphrasesToStableIntents(
        string title,
        string expectedIntent)
    {
        Assert.Equal(
            expectedIntent,
            RecommendationIntentPolicy.Resolve(
                SessionTemplateKind.Presentation,
                title,
                "Improve the presentation."));
    }

    [Fact]
    public void ResolveChecklistIntent_MapsCanonicalPresentationChecklistItems()
    {
        var checklist = CreatePresentationChecklist();

        Assert.Equal(
            RecommendationIntentPolicy.PresentationOpeningOutcome,
            RecommendationIntentPolicy.Resolve(
                SessionTemplateKind.Presentation,
                checklist.Single(item => item.Title == "Frame the audience outcome").Title,
                checklist.Single(item => item.Title == "Frame the audience outcome")
                    .CompletionCriteria));
        Assert.Equal(
            RecommendationIntentPolicy.PresentationClosingRecapActions,
            RecommendationIntentPolicy.Resolve(
                SessionTemplateKind.Presentation,
                checklist.Single(item => item.Title == "Close with the intended action").Title,
                checklist.Single(item => item.Title == "Close with the intended action")
                    .CompletionCriteria));
    }

    [Fact]
    public void Consolidate_FiltersChecklistCoveredTasksAndKeepsStrongestLifecycleProposal()
    {
        var checklist = CreatePresentationChecklist();
        var firstSources = Enumerable.Range(0, 15).Select(_ => Guid.NewGuid()).ToArray();
        var secondSources = Enumerable.Range(0, 15).Select(_ => Guid.NewGuid()).ToArray();
        var opening = CreateTask(
            "Add a clear summary of the key messages and objectives early in the presentation",
            "Clarify the audience outcome.",
            confidence: 0.91);
        var closing = CreateTask(
            "Add an explicit verbal summary of the key messages and next actions to close the presentation",
            "Close with a recap.",
            confidence: 0.93);
        var firstLifecycle = CreateTask(
            "Add an audience-relevant example illustrating lifecycle workflows",
            "Use a joiner-mover-leaver example.",
            confidence: 0.84,
            sourceIds: firstSources);
        var strongestLifecycle = CreateTask(
            "Add a concrete joiner-mover-leaver scenario with explicit access actions",
            "Connect onboarding and offboarding to practical access changes.",
            confidence: 0.96,
            sourceIds: secondSources);

        var result = RecommendationIntentPolicy.Consolidate(
            SessionTemplateKind.Presentation,
            checklist,
            [opening, strongestLifecycle, closing, firstLifecycle]);

        var lifecycle = Assert.Single(result);
        Assert.Equal(
            RecommendationIntentPolicy.PresentationLifecycleExample,
            lifecycle.IntentKey);
        Assert.Equal(strongestLifecycle.Title, lifecycle.Title);
        Assert.Equal(strongestLifecycle.Rationale, lifecycle.Rationale);
        Assert.Equal(0.96, lifecycle.Confidence);
        Assert.Equal(20, lifecycle.SourceTranscriptSegmentIds.Count);
        Assert.All(
            strongestLifecycle.SourceTranscriptSegmentIds,
            sourceId => Assert.Contains(sourceId, lifecycle.SourceTranscriptSegmentIds));
        Assert.Equal(RecommendationStatus.Proposed, lifecycle.Status);
    }

    [Fact]
    public void Resolve_DoesNotApplyPersistedPresentationIntentToOtherTemplates()
    {
        var task = CreateTask(
            "Illustrate lifecycle workflows",
            "Use a practical example.") with
        {
            IntentKey = RecommendationIntentPolicy.PresentationLifecycleExample
        };

        Assert.Null(RecommendationIntentPolicy.Resolve(
            SessionTemplateKind.Training,
            task));
    }

    [Fact]
    public void Merge_AfterSourceCap_RotatesSecondarySourcesButKeepsWordingGrounding()
    {
        var now = DateTimeOffset.UtcNow;
        var wordingSourceId = Guid.NewGuid();
        var retained = CreateTask(
            "Add a concrete joiner-mover-leaver scenario with explicit access actions",
            "Show access changes across the employee lifecycle.",
            confidence: 0.96,
            sourceIds: [wordingSourceId],
            createdAtUtc: now);
        var additionalSourceIds = new List<Guid>();

        for (var index = 0; index < 25; index++)
        {
            var sourceId = Guid.NewGuid();
            additionalSourceIds.Add(sourceId);
            var duplicate = CreateTask(
                "Add an audience-relevant example illustrating lifecycle workflows",
                "Connect the concepts to a practical example.",
                confidence: 0.84,
                sourceIds: [sourceId],
                createdAtUtc: now.AddMinutes(index + 1));
            retained = RecommendationIntentPolicy.Merge(
                [retained, duplicate],
                RecommendationIntentPolicy.PresentationLifecycleExample);
        }

        Assert.Equal(20, retained.SourceTranscriptSegmentIds.Count);
        Assert.Equal(
            [wordingSourceId],
            retained.WordingSourceTranscriptSegmentIds);
        Assert.Contains(wordingSourceId, retained.SourceTranscriptSegmentIds);
        Assert.Contains(additionalSourceIds[^1], retained.SourceTranscriptSegmentIds);
        Assert.DoesNotContain(additionalSourceIds[0], retained.SourceTranscriptSegmentIds);
    }

    [Theory]
    [InlineData(RecommendationStatus.Accepted)]
    [InlineData(RecommendationStatus.Completed)]
    [InlineData(RecommendationStatus.Dismissed)]
    public void SelectRecommendations_BlocksParaphraseAfterIntentReachedTerminalState(
        RecommendationStatus status)
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Lifecycle workflows automate identity changes.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var existing = CreateTask(
            "Illustrate onboarding and offboarding with a practical identity lifecycle story",
            "Connect the concept to a memorable scenario.",
            status);
        var context = new CoachAgentContext(
            CreatePurpose(),
            CreatePresentationChecklist(),
            [latest],
            [existing],
            Template: SessionTemplateKind.Presentation);
        var proposal = new RecommendedTaskProposal(
            "Add a concrete joiner-mover-leaver scenario with explicit access actions",
            "Show how access changes across an employee lifecycle.",
            0.94,
            [latest.Id]);

        var selected = PresentationCoachingPolicy.SelectRecommendations(
            context,
            [proposal],
            [latest]);

        Assert.Empty(selected);
        Assert.Contains(
            RecommendationIntentPolicy.PresentationLifecycleExample,
            RecommendationIntentPolicy.GetCoveredIntents(context));
    }

    private static IReadOnlyList<ChecklistItemState> CreatePresentationChecklist()
    {
        var planner = new MeetingChecklistPlanner();
        return planner.CreateChecklist(
            CreatePurpose(),
            requestedChecklist: null,
            SessionTemplateKind.Presentation);
    }

    private static MeetingPurpose CreatePurpose() =>
        new(
            "Microsoft Entra ID presentation",
            "Presentation",
            "Explain Entra ID identity and access capabilities",
            ["Audience understands the key governance options"]);

    private static RecommendedTaskState CreateTask(
        string title,
        string rationale,
        RecommendationStatus status = RecommendationStatus.Proposed,
        double confidence = 0.9,
        IReadOnlyList<Guid>? sourceIds = null,
        DateTimeOffset? createdAtUtc = null) =>
        new(
            Guid.NewGuid(),
            title,
            rationale,
            confidence,
            sourceIds ?? [],
            status,
            createdAtUtc ?? DateTimeOffset.UtcNow,
            AcceptedAtUtc: null,
            CompletedAtUtc: null,
            CompletionReason: null,
            Evidence: [],
            CompletionEligibleFromTranscriptIndex: null);
}
