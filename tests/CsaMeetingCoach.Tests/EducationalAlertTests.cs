using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;
using Microsoft.Extensions.Logging;

namespace CsaMeetingCoach.Tests;

public sealed class EducationalAlertTests
{
    [Theory]
    [MemberData(nameof(PositiveConcepts))]
    public void SelectContextualCards_DetectsPositiveEducationalMentions(
        EducationalConcept concept)
    {
        var latest = CreateFinalSegment(
            concept.RequiresAzureVendorScope
                ? $"{concept.Aliases[0]} is part of the Azure architecture."
                : $"{concept.Aliases[0]} is part of the architecture.");
        var purpose = TestData.CreatePurpose() with
        {
            MeetingType = "Azure workshop",
            Objective = "Explain Azure services and design decisions."
        };

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(purpose, [], [latest]),
            [],
            [latest]);

        Assert.Contains(
            cards,
            card => string.Equals(card.ConceptKey, concept.ConceptKey, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(NegatedConcepts))]
    public void SelectContextualCards_RejectsNegatedMentions(
        EducationalConcept concept)
    {
        var latest = CreateFinalSegment(
            concept.RequiresAzureVendorScope
                ? $"We will not use {concept.Aliases[0]} in Azure."
                : $"We will not use {concept.Aliases[0]}.");
        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            [],
            [latest]);

        Assert.DoesNotContain(
            cards,
            card => string.Equals(card.ConceptKey, concept.ConceptKey, StringComparison.Ordinal));
    }

    [Theory]
    [MemberData(nameof(VendorMismatchConcepts))]
    public void SelectContextualCards_RejectsVendorMismatchMentions(
        EducationalConcept concept,
        string alias)
    {
        var latest = CreateFinalSegment($"AWS {alias} controls the workload.");

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            [],
            [latest]);

        Assert.DoesNotContain(
            cards,
            card => string.Equals(card.ConceptKey, concept.ConceptKey, StringComparison.Ordinal));
    }

    [Fact]
    public void SelectContextualCards_DoesNotTreatHybridMeetingAsHybridCloud()
    {
        var latest = CreateFinalSegment(
            "This is a hybrid meeting with some participants joining from the office.");

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            [],
            [latest]);

        Assert.DoesNotContain(
            cards,
            card => string.Equals(card.ConceptKey, "hybrid-cloud", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectContextualCards_DetectsFragmentedMentionAcrossSegments()
    {
        var first = CreateFinalSegment("We will explain Availability");
        var second = CreateFinalSegment("Zones in the Azure design.");

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(TestData.CreatePurpose(), [], [first, second]),
            [],
            [first, second]);

        Assert.Contains(
            cards,
            card => string.Equals(card.ConceptKey, "availability-zone", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Coordinator_UsesDefinitionThenHintLifecycle()
    {
        var store = new InMemoryMeetingSessionStore();
        using var coordinator = CreateCoordinator(
            store,
            new StaticCardAgent(
                new ContextualCardProposal(
                    ContextualCardKind.Definition,
                    "RTO",
                    "RTO is the target maximum time allowed to restore a service after an outage begins.",
                    0.9,
                    [])),
            logger: new ListLogger<MeetingSessionCoordinator>());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var first = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "RTO is one hour."),
            CancellationToken.None);
        Assert.Single(first.ContextualCards);

        var persisted = await store.GetAsync(session.Id, CancellationToken.None);
        await store.SaveAsync(
            persisted! with
            {
                DefinitionCooldowns = new Dictionary<string, DateTimeOffset>(
                    persisted.DefinitionCooldowns,
                    StringComparer.Ordinal)
                {
                    ["recovery-time-objective"] = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
            },
            CancellationToken.None);

        using var hintCoordinator = CreateCoordinator(
            store,
            new StaticCardAgent(
                new ContextualCardProposal(
                    ContextualCardKind.Hint,
                    "RTO",
                    "A shorter RTO usually needs more automation and ready failover capacity.",
                    0.9,
                    [])));

        var second = await hintCoordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "The RTO target changes our failover design."),
            CancellationToken.None);

        Assert.Equal(2, second.ContextualCards.Count);
        Assert.Contains(second.ContextualCards, card => card.Kind == ContextualCardKind.Hint);
    }

    [Fact]
    public async Task Coordinator_EnforcesHintCooldownAndFingerprintDeduplication()
    {
        var store = new InMemoryMeetingSessionStore();
        var hint = new ContextualCardProposal(
            ContextualCardKind.Hint,
            "RTO",
            "A shorter RTO usually needs more automation and ready failover capacity.",
            0.9,
            []);
        using var definitionCoordinator = CreateCoordinator(
            store,
            new StaticCardAgent(
                new ContextualCardProposal(
                    ContextualCardKind.Definition,
                    "RTO",
                    "RTO is the target maximum time allowed to restore a service after an outage begins.",
                    0.9,
                    [])));
        var session = await definitionCoordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        await definitionCoordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "RTO is one hour."),
            CancellationToken.None);

        var persisted = await store.GetAsync(session.Id, CancellationToken.None);
        await store.SaveAsync(
            persisted! with
            {
                DefinitionCooldowns = new Dictionary<string, DateTimeOffset>(
                    persisted.DefinitionCooldowns,
                    StringComparer.Ordinal)
                {
                    ["recovery-time-objective"] = DateTimeOffset.UtcNow.AddMinutes(-5)
                }
            },
            CancellationToken.None);

        using var hintCoordinator = CreateCoordinator(store, new StaticCardAgent(hint));
        var firstHint = await hintCoordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "The RTO target changes our failover design."),
            CancellationToken.None);
        Assert.Equal(2, firstHint.ContextualCards.Count);

        persisted = await store.GetAsync(session.Id, CancellationToken.None);
        await store.SaveAsync(
            persisted! with
            {
                HintCooldowns = new Dictionary<string, DateTimeOffset>(
                    persisted.HintCooldowns,
                    StringComparer.Ordinal)
                {
                    ["recovery-time-objective"] = DateTimeOffset.UtcNow.AddMinutes(-20)
                }
            },
            CancellationToken.None);

        var repeatedHint = await hintCoordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "The RTO target changes our recovery design again."),
            CancellationToken.None);

        Assert.Equal(2, repeatedHint.ContextualCards.Count);
    }

    [Fact]
    public async Task Coordinator_PacesToOneEducationalCardPerSegment()
    {
        using var coordinator = CreateCoordinator(
            new InMemoryMeetingSessionStore(),
            new MultiCardAgent(
                new ContextualCardProposal(
                    ContextualCardKind.Definition,
                    "Azure region",
                    "An Azure region is a geographic area with Azure datacenter capacity.",
                    0.91,
                    []),
                new ContextualCardProposal(
                    ContextualCardKind.Definition,
                    "Azure Policy",
                    "Azure Policy evaluates resources against governance rules.",
                    0.92,
                    [])));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "Azure region selection and Azure Policy both matter in this design."),
            CancellationToken.None);

        Assert.Single(updated.ContextualCards);
    }

    [Theory]
    [MemberData(nameof(RecommendationLanguageCases))]
    public async Task Coordinator_RejectsRecommendationStyleAlerts(
        string title,
        string content)
    {
        using var coordinator = CreateCoordinator(
            new InMemoryMeetingSessionStore(),
            new StaticCardAgent(
                new ContextualCardProposal(
                    ContextualCardKind.Hint,
                    title,
                    content,
                    0.9,
                    [])));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "Azure Policy evaluates resources against rules."),
            CancellationToken.None);

        Assert.Empty(updated.ContextualCards);
    }

    [Fact]
    public async Task Coordinator_RecommendationTasksRemainSeparateFromAlerts()
    {
        using var coordinator = CreateCoordinator(
            new InMemoryMeetingSessionStore(),
            new RecommendationAndCardAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "Azure Policy evaluates resources against rules."),
            CancellationToken.None);

        Assert.Single(updated.RecommendedTasks);
        Assert.Empty(updated.ContextualCards);
    }

    public static TheoryData<EducationalConcept> PositiveConcepts =>
        BuildConceptData(EducationalConceptCatalog.All.Take(50));

    public static TheoryData<EducationalConcept> NegatedConcepts =>
        BuildConceptData(EducationalConceptCatalog.All.Take(30));

    public static TheoryData<EducationalConcept, string> VendorMismatchConcepts
    {
        get
        {
            var data = new TheoryData<EducationalConcept, string>();
            var conceptKeys = new[]
            {
                "availability-zone",
                "management-group",
                "resource-group",
                "azure-rbac",
                "network-security-group",
                "azure-load-balancer",
                "application-gateway",
                "private-link",
                "vpn-gateway",
                "azure-kubernetes-service",
                "azure-container-apps",
                "azure-cache-for-redis"
            };
            foreach (var conceptKey in conceptKeys)
            {
                var concept = EducationalConceptCatalog.All.Single(item => item.ConceptKey == conceptKey);
                var alias = concept.Aliases
                    .First(candidate => !candidate.Contains("azure", StringComparison.OrdinalIgnoreCase));
                data.Add(concept, alias);
            }

            return data;
        }
    }

    public static TheoryData<string, string> RecommendationLanguageCases =>
        new()
        {
            { "Azure Policy", "You should recommend Azure Policy as the next step." },
            { "RTO", "The client should consider a one-hour RTO." },
            { "Azure region", "Reach out to the account team as an action item." },
            { "Azure RBAC", "Next step: recommend that the customer use Azure RBAC." }
        };

    private static TheoryData<EducationalConcept> BuildConceptData(
        IEnumerable<EducationalConcept> concepts)
    {
        var data = new TheoryData<EducationalConcept>();
        foreach (var concept in concepts)
        {
            data.Add(concept);
        }

        return data;
    }

    private static TranscriptSegment CreateFinalSegment(string text) =>
        new(Guid.NewGuid(), "Presenter", text, DateTimeOffset.UtcNow, IsFinal: true);

    private static MeetingSessionCoordinator CreateCoordinator(
        InMemoryMeetingSessionStore store,
        IConversationCoachAgent agent,
        ILogger<MeetingSessionCoordinator>? logger = null) =>
        new(
            store,
            new MeetingChecklistPlanner(),
            agent,
            null,
            new NullSessionUpdatePublisher(),
            logger);

    private sealed class StaticCardAgent(ContextualCardProposal proposal) : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            var grounded = proposal with
            {
                SourceTranscriptSegmentIds = [latestSegment.Id]
            };
            return Task.FromResult(new CoachAgentDecision([], [])
            {
                ContextualCards = [grounded]
            });
        }
    }

    private sealed class MultiCardAgent(params ContextualCardProposal[] proposals) : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CoachAgentDecision([], [])
            {
                ContextualCards = proposals
                    .Select(proposal => proposal with
                    {
                        SourceTranscriptSegmentIds = [latestSegment.Id]
                    })
                    .ToArray()
            });
        }
    }

    private sealed class RecommendationAndCardAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CoachAgentDecision(
                [],
                [
                    new RecommendedTaskProposal(
                        "Validate Azure Policy scope",
                        "The customer explicitly discussed governance controls.",
                        0.9,
                        [latestSegment.Id])
                ])
            {
                ContextualCards =
                [
                    new ContextualCardProposal(
                        ContextualCardKind.Hint,
                        "Azure Policy",
                        "You should recommend Azure Policy as the next step.",
                        0.9,
                        [latestSegment.Id])
                ]
            });
        }
    }

    private sealed class ListLogger<T> : ILogger<T>
    {
        public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(
            LogLevel logLevel,
            EventId eventId,
            TState state,
            Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose()
            {
            }
        }
    }
}
