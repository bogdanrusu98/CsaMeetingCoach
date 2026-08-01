using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class EvidenceBackedConversationCoachAgentTests
{
    [Fact]
    public async Task Analyze_PrimaryOmitsChecklistCompletion_AddsDeterministicEvidence()
    {
        var purpose = TestData.CreatePurpose();
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            purpose,
            requestedChecklist: null);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            "Our objective is to migrate the customer portal to Azure.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Explain the migration assessment",
            "The customer asked for a migration path.",
            0.9,
            [latest.Id]);
        var primary = new StubAgent(new CoachAgentDecision(
            [],
            [primaryRecommendation],
            []));
        var agent = new EvidenceBackedConversationCoachAgent(
            primary,
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(purpose, checklist, [latest]),
            latest,
            CancellationToken.None);

        var objective = Assert.Single(checklist.Where(item =>
            item.Title.Contains("objective", StringComparison.OrdinalIgnoreCase)));
        Assert.Contains(
            decision.ChecklistEvaluations,
            evaluation => evaluation.ChecklistItemId == objective.Id
                && evaluation.ShouldComplete);
        Assert.Same(primaryRecommendation, Assert.Single(decision.RecommendedTasks));
    }

    [Fact]
    public async Task Analyze_PrimaryFalsePositiveForSuccessCriteria_IsRejected()
    {
        var purpose = TestData.CreatePurpose();
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            purpose,
            requestedChecklist: null);
        var successCriteria = Assert.Single(checklist.Where(item =>
            item.Title.Contains("success", StringComparison.OrdinalIgnoreCase)));
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            "The outcome was unsuccessful.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryEvaluation = new ChecklistEvaluation(
            successCriteria.Id,
            ShouldComplete: true,
            Confidence: 0.99,
            "The provider treated outcome as measurable.",
            latest.Text);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([primaryEvaluation], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(purpose, checklist, [latest]),
            latest,
            CancellationToken.None);

        Assert.DoesNotContain(
            decision.ChecklistEvaluations,
            evaluation => evaluation.ChecklistItemId == successCriteria.Id
                && evaluation.ShouldComplete);
    }

    [Fact]
    public async Task Analyze_PrimaryFails_DoesNotSilentlyFallBack()
    {
        var expected = new InvalidOperationException("Primary provider failed.");
        var deterministic = new CountingAgent();
        var agent = new EvidenceBackedConversationCoachAgent(
            new ThrowingAgent(expected),
            deterministic);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "CSA",
            "Our objective is explicit.",
            DateTimeOffset.UtcNow,
            IsFinal: true);

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.AnalyzeAsync(
                new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
                latest,
                CancellationToken.None));

        Assert.Same(expected, observed);
        Assert.Equal(0, deterministic.CallCount);
    }

    private sealed class StubAgent(CoachAgentDecision decision)
        : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken) =>
            Task.FromResult(decision);
    }

    private sealed class ThrowingAgent(Exception exception)
        : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken) =>
            Task.FromException<CoachAgentDecision>(exception);
    }

    private sealed class CountingAgent : IConversationCoachAgent
    {
        public int CallCount { get; private set; }

        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult(new CoachAgentDecision([], [], []));
        }
    }
}
