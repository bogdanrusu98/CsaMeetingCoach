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
    public async Task Analyze_PrimaryFalsePositiveForBusinessValue_IsRejected()
    {
        var purpose = TestData.CreatePurpose();
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            purpose,
            requestedChecklist: null);
        var businessValue = Assert.Single(checklist.Where(item =>
            item.Title.Contains("business value", StringComparison.OrdinalIgnoreCase)));
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter microphone",
            "The outcome was unsuccessful in phase 2; availability remains unclear.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryEvaluation = new ChecklistEvaluation(
            businessValue.Id,
            ShouldComplete: true,
            Confidence: 0.99,
            "The provider treated a generic outcome as business value.",
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
            evaluation => evaluation.ChecklistItemId == businessValue.Id
                && evaluation.ShouldComplete);
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
    public async Task Analyze_PrimaryContextualCard_IsPreserved()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Customer",
            "Our RTO is one hour.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var contextualCard = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "RTO",
            "Recovery Time Objective is the maximum target restoration time.",
            0.9,
            [latest.Id]);
        var primaryDecision = new CoachAgentDecision([], [], [])
        {
            ContextualCards = [contextualCard]
        };
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(primaryDecision),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Same(contextualCard, Assert.Single(decision.ContextualCards));
    }

    [Fact]
    public async Task Analyze_PrimaryCompletionFromEarlierFragment_RequiresMatchingDeterministicEvidence()
    {
        var checklistItem = new ChecklistItemState(
            Guid.NewGuid(),
            "Explain the Azure Load Balancer frontend",
            "Discuss the frontend IP configuration.",
            ["frontend IP address"],
            ChecklistItemStatus.Pending,
            AutoCompleted: false,
            Confidence: null,
            CompletionReason: null,
            CompletedAtUtc: null,
            Evidence: []);
        var evidenceSegment = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Azure Load Balancer has a frontend IP address.",
            DateTimeOffset.UtcNow.AddSeconds(-2),
            IsFinal: true);
        var latestSegment = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "It can be internal or external.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryEvaluation = new ChecklistEvaluation(
            checklistItem.Id,
            ShouldComplete: true,
            Confidence: 0.94,
            "The frontend IP was explicitly discussed.",
            "frontend IP address",
            evidenceSegment.Id);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([primaryEvaluation], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [checklistItem],
                [evidenceSegment, latestSegment]),
            latestSegment,
            CancellationToken.None);

        Assert.Contains(
            decision.ChecklistEvaluations,
            completion => completion.ShouldComplete
                && completion.Confidence == primaryEvaluation.Confidence
                && completion.SourceTranscriptSegmentId == evidenceSegment.Id);
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
