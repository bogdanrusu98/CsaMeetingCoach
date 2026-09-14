using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class TechnicalSignalPolicyTests
{
    [Theory]
    [InlineData(
        "The application runs on one VM, but downtime is unacceptable.",
        "signal-single-vm-resilience")]
    [InlineData(
        "Traffic changes significantly during the day.",
        "signal-variable-traffic-capacity")]
    [InlineData(
        "We will store database passwords directly in the source code.",
        "signal-embedded-secrets")]
    [InlineData(
        "The Azure SQL database will be accessible from any public IP.",
        "signal-public-database-exposure")]
    [InlineData(
        "We do not need backups because Azure handles everything automatically.",
        "signal-missing-backup")]
    [InlineData(
        "Let's deploy Premium SKUs everywhere without defining a budget.",
        "signal-unbudgeted-premium-capacity")]
    public void SelectContextualCards_MapsExplicitRiskSignalsToHints(
        string transcript,
        string expectedConceptKey)
    {
        var segment = CreateSegment(transcript);

        var card = Assert.Single(
            TechnicalSignalPolicy.SelectContextualCards(segment));

        Assert.Equal(ContextualCardKind.Hint, card.Kind);
        Assert.Equal(expectedConceptKey, card.ConceptKey);
        Assert.DoesNotContain(
            "recommend",
            card.Content,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "The required RTO is 30 minutes and RPO is 5 minutes, but we have no disaster recovery design.",
        "disaster-recovery")]
    [InlineData(
        "The customer expects one million users, but no performance testing is planned.",
        "performance testing")]
    [InlineData(
        "The team manually creates every Azure resource through the portal.",
        "infrastructure as code")]
    [InlineData(
        "Nobody has been assigned ownership for monitoring production.",
        "observability ownership")]
    public void SelectRecommendations_MapsExplicitTechnicalGaps(
        string transcript,
        string expectedTitleFragment)
    {
        var recommendation = Assert.Single(
            TechnicalSignalPolicy.SelectRecommendations(CreateSegment(transcript)));

        Assert.Contains(
            expectedTitleFragment,
            recommendation.Title,
            StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(
        "The application runs on one VM, but downtime is unacceptable.",
        "signal-single-vm-resilience")]
    [InlineData(
        "Traffic changes significantly during the day.",
        "signal-variable-traffic-capacity")]
    [InlineData(
        "We will store database passwords directly in the source code.",
        "signal-embedded-secrets")]
    [InlineData(
        "The Azure SQL database will be accessible from any public IP.",
        "signal-public-database-exposure")]
    [InlineData(
        "We do not need backups because Azure handles everything automatically.",
        "signal-missing-backup")]
    [InlineData(
        "Let's deploy Premium SKUs everywhere without defining a budget.",
        "signal-unbudgeted-premium-capacity")]
    public async Task Coordinator_AcceptsRiskHintWithoutPriorDefinition(
        string transcript,
        string expectedConceptKey)
    {
        using var coordinator = CreateCoordinator();
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                Template: SessionTemplateKind.Presentation,
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                transcript),
            CancellationToken.None);

        var card = Assert.Single(updated.ContextualCards);
        Assert.Equal(ContextualCardKind.Hint, card.Kind);
        Assert.Equal(expectedConceptKey, card.ConceptKey);
    }

    [Fact]
    public async Task Coordinator_PublishesTechnicalRecommendationWhileAiLaneIsEnabled()
    {
        using var coordinator = CreateCoordinator(new EmptyAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                Template: SessionTemplateKind.Presentation),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "The customer expects one million users, but no performance testing is planned."),
            CancellationToken.None);

        Assert.Contains(
            updated.RecommendedTasks,
            task => task.Title.Contains(
                "performance testing",
                StringComparison.OrdinalIgnoreCase));
    }

    private static MeetingSessionCoordinator CreateCoordinator(
        IConversationCoachAgent? aiAgent = null) =>
        new(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            new NullSessionUpdatePublisher());

    private static TranscriptSegment CreateSegment(string text) =>
        new(
            Guid.NewGuid(),
            "Presenter",
            text,
            DateTimeOffset.UtcNow,
            IsFinal: true);

    private sealed class EmptyAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken) =>
            Task.FromResult(new CoachAgentDecision([], []));
    }
}
