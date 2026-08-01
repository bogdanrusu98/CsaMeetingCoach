using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class HeuristicConversationCoachAgentTests
{
    [Theory]
    [InlineData(
        "objective",
        "Our objective is to migrate the customer portal and SQL databases to Azure.")]
    [InlineData(
        "success",
        "Success means completing the migration within nine months with 99.9 percent availability.")]
    [InlineData(
        "success",
        "The availability target is 99.9%.")]
    [InlineData(
        "business value",
        "This solution provides business value by reducing infrastructure costs by 20 percent.")]
    [InlineData(
        "risks",
        "There are open questions about authentication.")]
    public async Task Analyze_ExplicitMeetingGoal_ReturnsCompletionEvaluation(
        string titleFragment,
        string transcript)
    {
        var purpose = TestData.CreatePurpose();
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            purpose,
            requestedChecklist: null);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            transcript,
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var context = new CoachAgentContext(purpose, checklist, [latest]);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            context,
            latest,
            CancellationToken.None);

        var item = Assert.Single(checklist.Where(entry =>
            entry.Title.Contains(titleFragment, StringComparison.OrdinalIgnoreCase)));
        var evaluation = Assert.Single(decision.ChecklistEvaluations.Where(entry =>
            entry.ChecklistItemId == item.Id));
        Assert.True(evaluation.ShouldComplete);
        Assert.True(evaluation.Confidence >= MeetingSessionCoordinator.AutoCompletionThreshold);
        Assert.Equal(latest.Text, evaluation.EvidenceQuote);
    }

    [Fact]
    public async Task Analyze_SubstringOnlyHint_DoesNotCompleteBusinessValue()
    {
        var purpose = TestData.CreatePurpose();
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            purpose,
            requestedChecklist: null);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            "The outcome was unsuccessful in phase 2; availability remains unclear.",
            DateTimeOffset.UtcNow,
            IsFinal: true);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(purpose, checklist, [latest]),
            latest,
            CancellationToken.None);

        var businessValue = Assert.Single(checklist.Where(item =>
            item.Title.Contains("business value", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            decision.ChecklistEvaluations,
            evaluation => evaluation.ChecklistItemId == businessValue.Id
                && evaluation.ShouldComplete);
    }

    [Theory]
    [InlineData("The migration demo was successful.")]
    [InlineData("The first migration attempt was unsuccessful.")]
    [InlineData("The outcome was unsuccessful.")]
    [InlineData("The outcome was unsuccessful in phase 2.")]
    [InlineData("The target remains unclear.")]
    [InlineData("We need to discuss our success criteria.")]
    [InlineData("Success criteria are not defined.")]
    [InlineData("There are no success criteria yet.")]
    [InlineData("The target is not defined for phase 2; availability remains unclear.")]
    public async Task Analyze_GenericSuccessStatement_DoesNotCompleteSuccessCriteria(
        string transcript)
    {
        var purpose = TestData.CreatePurpose();
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            purpose,
            requestedChecklist: null);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            transcript,
            DateTimeOffset.UtcNow,
            IsFinal: true);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(purpose, checklist, [latest]),
            latest,
            CancellationToken.None);

        var item = Assert.Single(checklist.Where(entry =>
            entry.Title.Contains("success", StringComparison.OrdinalIgnoreCase)));
        Assert.DoesNotContain(
            decision.ChecklistEvaluations,
            evaluation => evaluation.ChecklistItemId == item.Id
                && evaluation.ShouldComplete);
    }

    [Fact]
    public async Task Analyze_CustomMeasurableOutcomeItem_UsesMeasurableGuard()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            "The outcome was unsuccessful.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var checklistItem = new ChecklistItemState(
            Guid.NewGuid(),
            "Validate the expected result",
            "A measurable outcome is defined.",
            ["outcome"],
            ChecklistItemStatus.Pending,
            AutoCompleted: false,
            Confidence: null,
            CompletionReason: null,
            CompletedAtUtc: null,
            Evidence: []);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [checklistItem], [latest]),
            latest,
            CancellationToken.None);

        var evaluation = Assert.Single(decision.ChecklistEvaluations);
        Assert.False(evaluation.ShouldComplete);
    }

    [Fact]
    public async Task Analyze_ExplicitNamedMetric_CompletesSuccessCriteria()
    {
        var purpose = TestData.CreatePurpose();
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            purpose,
            requestedChecklist: null);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            "Our success metric is deployment frequency.",
            DateTimeOffset.UtcNow,
            IsFinal: true);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(purpose, checklist, [latest]),
            latest,
            CancellationToken.None);

        var item = Assert.Single(checklist.Where(entry =>
            entry.Title.Contains("success", StringComparison.OrdinalIgnoreCase)));
        var evaluation = Assert.Single(decision.ChecklistEvaluations.Where(entry =>
            entry.ChecklistItemId == item.Id));
        Assert.True(evaluation.ShouldComplete);
    }

    [Fact]
    public async Task Analyze_AcceptedTalkingPoint_ReturnsCoverageEvaluation()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "CSA",
            "We can reduce deployment risk by using staged rollout rings.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var accepted = new RecommendedTaskState(
            Guid.NewGuid(),
            "Discuss deployment risk and staged rollout",
            "This helps the customer reduce deployment risk.",
            0.9,
            [Guid.NewGuid()],
            RecommendationStatus.Accepted,
            latest.OccurredAtUtc.AddMinutes(-2),
            latest.OccurredAtUtc.AddMinutes(-1),
            Evidence: []);
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            [accepted]);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            context,
            latest,
            CancellationToken.None);

        var evaluation = Assert.Single(decision.RecommendationEvaluations!);
        Assert.Equal(accepted.Id, evaluation.RecommendationId);
        Assert.True(evaluation.ShouldComplete);
        Assert.Equal(latest.Text, evaluation.EvidenceQuote);
    }

    [Theory]
    [InlineData("business benefit", "The business value is faster market entry.")]
    [InlineData("total cost of ownership", "The TCO baseline is now documented.")]
    [InlineData("roi", "The return on investment supports the migration.")]
    [InlineData("landing zone", "The cloud foundation design is approved.")]
    public async Task Analyze_EquivalentEvidenceHint_CompletesItem(
        string evidenceHint,
        string transcript)
    {
        var item = CreatePendingItem(
            "Discuss the customer topic",
            "The customer topic is explicitly discussed.",
            evidenceHint);
        var latest = CreateFinalSegment(transcript);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [item], [latest]),
            latest,
            CancellationToken.None);

        var evaluation = Assert.Single(decision.ChecklistEvaluations);
        Assert.True(evaluation.ShouldComplete);
        Assert.Equal(latest.Text, evaluation.EvidenceQuote);
    }

    [Fact]
    public async Task Analyze_AcceptedTalkingPoint_UsesEquivalentTerms()
    {
        var latest = CreateFinalSegment(
            "TCO is lower with the managed platform.");
        var accepted = new RecommendedTaskState(
            Guid.NewGuid(),
            "Discuss total cost of ownership",
            "Compare the cost baseline.",
            0.9,
            [Guid.NewGuid()],
            RecommendationStatus.Accepted,
            latest.OccurredAtUtc.AddMinutes(-2),
            latest.OccurredAtUtc.AddMinutes(-1),
            Evidence: []);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [latest],
                [accepted]),
            latest,
            CancellationToken.None);

        var evaluation = Assert.Single(decision.RecommendationEvaluations!);
        Assert.True(evaluation.ShouldComplete);
        Assert.Equal(accepted.Id, evaluation.RecommendationId);
    }

    [Theory]
    [InlineData("We need to migrate the billing platform.")]
    [InlineData("We want to modernize the customer portal.")]
    [InlineData("Our priority is reducing release lead time.")]
    [InlineData("Our objective is to retire the legacy service.")]
    [InlineData("I need to reduce infrastructure cost.")]
    [InlineData("You want to improve operational resilience.")]
    [InlineData("Obiectivul nostru este să reducem costurile.")]
    public async Task Analyze_DirectConversationalObjective_CompletesObjective(
        string transcript)
    {
        var item = CreatePendingItem(
            "Clarify the customer objective",
            "The business objective is explicitly confirmed.",
            "unrelated transformation");
        var latest = CreateFinalSegment(transcript);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [item], [latest]),
            latest,
            CancellationToken.None);

        var evaluation = Assert.Single(decision.ChecklistEvaluations);
        Assert.True(evaluation.ShouldComplete);
    }

    [Theory]
    [InlineData("We need to discuss the objective.")]
    [InlineData("We need to clarify our objective.")]
    [InlineData("We need to define the objective.")]
    [InlineData("We do not need to migrate the billing platform.")]
    [InlineData("We may need to migrate the billing platform.")]
    [InlineData("Maybe we need to migrate the billing platform.")]
    [InlineData("Do we need to migrate the billing platform?")]
    [InlineData("Our objective is unclear.")]
    [InlineData("We need to discuss success criteria.")]
    [InlineData("Trebuie să clarificăm obiectivul.")]
    [InlineData("The customer asked do we need to migrate.")]
    [InlineData("It is not true that we need to migrate.")]
    [InlineData("Someone said we need to migrate.")]
    public async Task Analyze_NonExplicitObjectiveLanguage_DoesNotComplete(
        string transcript)
    {
        var item = CreatePendingItem(
            "Clarify the customer objective",
            "The business objective is explicitly confirmed.",
            "unrelated transformation");
        var latest = CreateFinalSegment(transcript);

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [item], [latest]),
            latest,
            CancellationToken.None);

        Assert.DoesNotContain(
            decision.ChecklistEvaluations,
            evaluation => evaluation.ShouldComplete);
    }

    [Fact]
    public async Task Analyze_TcoMention_DoesNotBypassMeasurableOutcomeGuard()
    {
        var item = CreatePendingItem(
            "Validate the expected result",
            "A measurable outcome is defined.",
            "total cost of ownership");
        var latest = CreateFinalSegment("The TCO is still unclear.");

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [item], [latest]),
            latest,
            CancellationToken.None);

        var evaluation = Assert.Single(decision.ChecklistEvaluations);
        Assert.False(evaluation.ShouldComplete);
    }

    [Fact]
    public async Task Analyze_DirectObjective_DoesNotCompleteRecoveryObjectiveItem()
    {
        var item = CreatePendingItem(
            "Confirm the recovery time objective",
            "An RTO is explicitly confirmed.",
            "unrelated recovery measure");
        var latest = CreateFinalSegment("We need to migrate the billing platform.");

        var decision = await new HeuristicConversationCoachAgent().AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [item], [latest]),
            latest,
            CancellationToken.None);

        Assert.Empty(decision.ChecklistEvaluations);
    }

    private static ChecklistItemState CreatePendingItem(
        string title,
        string completionCriteria,
        string evidenceHint) =>
        new(
            Guid.NewGuid(),
            title,
            completionCriteria,
            [evidenceHint],
            ChecklistItemStatus.Pending,
            AutoCompleted: false,
            Confidence: null,
            CompletionReason: null,
            CompletedAtUtc: null,
            Evidence: []);

    private static TranscriptSegment CreateFinalSegment(string text) =>
        new(
            Guid.NewGuid(),
            "Meeting participant",
            text,
            DateTimeOffset.UtcNow,
            IsFinal: true);
}
