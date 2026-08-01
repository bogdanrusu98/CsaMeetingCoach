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
}
