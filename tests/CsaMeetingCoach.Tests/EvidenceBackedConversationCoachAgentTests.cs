using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class EvidenceBackedConversationCoachAgentTests
{
    [Fact]
    public void RecommendationOverlap_RequiresMoreThanOneGenericSharedTerm()
    {
        Assert.False(PresentationCoachingPolicy.HasSubstantialOverlap(
            "validate azure load balancer traffic health and availability choices",
            "availability"));
        Assert.True(PresentationCoachingPolicy.HasSubstantialOverlap(
            "confirm customer specific production requirements and an accountable owner",
            "confirm customer production requirements and owner"));
    }

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
    public async Task Analyze_LoadBalancerPresentation_AddsUsefulFallbackCoaching()
    {
        var loadBalancer = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Azure Load Balancer distributes traffic across a backend pool.",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            IsFinal: true);
        var probe = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "A health probe checks the backend instances.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose() with
                {
                    MeetingType = "Azure presentation",
                    Objective = "Present Azure Load Balancer"
                },
                [],
                [loadBalancer, probe]),
            probe,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Contains("Load Balancer", recommendation.Title);
        Assert.Equal([loadBalancer.Id], recommendation.SourceTranscriptSegmentIds);
        Assert.Equal(2, decision.ContextualCards.Count);
        Assert.Contains(
            decision.ContextualCards,
            card => card.Kind == ContextualCardKind.Definition
                && card.Title == "Azure Load Balancer");
        Assert.Contains(
            decision.ContextualCards,
            card => card.Kind == ContextualCardKind.Hint
                && card.Title == "health probe");
    }

    [Fact]
    public async Task Analyze_ChecklistDuplicateRecommendation_IsReplacedByTechnicalGap()
    {
        var checklistItem = new ChecklistItemState(
            Guid.NewGuid(),
            "Confirm customer production requirements and owner",
            "Confirm customer-specific production requirements and an accountable owner.",
            ["production requirements", "owner"],
            ChecklistItemStatus.Pending,
            AutoCompleted: false,
            Confidence: null,
            CompletionReason: null,
            CompletedAtUtc: null,
            Evidence: []);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "The load balancer is at layer four.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var generic = new RecommendedTaskProposal(
            "Confirm customer-specific production requirements and an accountable owner",
            "This would complete the meeting plan.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [generic], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [checklistItem],
                [latest]),
            latest,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.DoesNotContain(
            "production requirements",
            recommendation.Title,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Load Balancer", recommendation.Title);
    }

    [Fact]
    public async Task Analyze_FragmentedLoadBalancerName_GroundsFallbackAcrossSources()
    {
        var first = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "This would be UDP, so this is the Azure Load",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            IsFinal: true);
        var second = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Balancer. There are two different SKUs available.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [first, second]),
            second,
            CancellationToken.None);

        Assert.Equal(
            [first.Id, second.Id],
            Assert.Single(decision.RecommendedTasks).SourceTranscriptSegmentIds);
        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal("Azure Load Balancer", card.Title);
        Assert.Equal([first.Id, second.Id], card.SourceTranscriptSegmentIds);
        Assert.True(PresentationCoachingPolicy.IsTitleGrounded(
            card.Title,
            card.SourceTranscriptSegmentIds,
            [first, second]));
    }

    [Fact]
    public async Task Analyze_GenericOrInvalidPrimaryOutputs_DoNotSuppressFallbacks()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "The load balancer is a regional service.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var generic = new RecommendedTaskProposal(
            "Review network health",
            "This would keep the meeting aligned.",
            0.9,
            [latest.Id]);
        var invalidCard = new ContextualCardProposal(
            ContextualCardKind.Hint,
            "load balancer",
            "This card cites an invented source.",
            0.9,
            [Guid.NewGuid()]);
        var primaryDecision = new CoachAgentDecision([], [generic], [])
        {
            ContextualCards = [invalidCard]
        };
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(primaryDecision),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [latest]),
            latest,
            CancellationToken.None);

        Assert.Contains(
            "Load Balancer",
            Assert.Single(decision.RecommendedTasks).Title);
        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal("load balancer", card.Title);
        Assert.NotEqual(invalidCard.SourceTranscriptSegmentIds, card.SourceTranscriptSegmentIds);
        Assert.Equal([latest.Id], card.SourceTranscriptSegmentIds);
    }

    [Fact]
    public async Task Analyze_MidWordFragmentBoundary_DoesNotInventLoadBalancerTopic()
    {
        var first = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "We will assess the workload",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            IsFinal: true);
        var second = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Balancer settings are unrelated.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [first, second]),
            second,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Empty(decision.ContextualCards);
    }

    [Fact]
    public async Task Analyze_NegatedLoadBalancerTopic_DoesNotTriggerFallbackCoaching()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "We will not use Azure Load Balancer because it is out of scope.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [latest]),
            latest,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Empty(decision.ContextualCards);
    }

    [Fact]
    public async Task Analyze_DuplicatePrimaryCards_DoNotConsumeFallbackSlots()
    {
        var loadBalancer = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Azure Load Balancer distributes traffic.",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            IsFinal: true);
        var probe = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "The health probe checks the backend.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var existing = new ContextualCardState(
            Guid.NewGuid(),
            ContextualCardKind.Definition,
            "Azure Load Balancer",
            "Existing reviewed definition.",
            0.9,
            [loadBalancer.Id],
            DateTimeOffset.UtcNow.AddMinutes(-1));
        var duplicate = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "Azure Load Balancer",
            "Duplicate generated definition.",
            0.9,
            [loadBalancer.Id]);
        var primaryDecision = new CoachAgentDecision([], [], [])
        {
            ContextualCards = [duplicate, duplicate]
        };
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(primaryDecision),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [loadBalancer, probe],
                ContextualCards: [existing]),
            probe,
            CancellationToken.None);

        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal(ContextualCardKind.Hint, card.Kind);
        Assert.Equal("health probe", card.Title);
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
