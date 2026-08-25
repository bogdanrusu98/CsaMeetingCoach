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
            new CoachAgentContext(
                purpose,
                [],
                [latest],
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            [],
            [latest]);

        Assert.Contains(
            cards,
            card => string.Equals(card.ConceptKey, concept.ConceptKey, StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(
        "Azure Managed Redis is part of the Azure architecture.",
        "azure-managed-redis",
        "azure-cache-for-redis")]
    [InlineData(
        "Microsoft Entra ID Governance is part of the Azure architecture.",
        "microsoft-entra-id-governance",
        "microsoft-entra-id")]
    public void SelectContextualCards_PrefersMostSpecificOverlappingConcept(
        string text,
        string expectedConceptKey,
        string broaderConceptKey)
    {
        var latest = CreateFinalSegment(text);
        var purpose = TestData.CreatePurpose() with
        {
            MeetingType = "Azure workshop",
            Objective = "Explain Azure services and design decisions."
        };

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(purpose, [], [latest]),
            [],
            [latest]);

        Assert.Contains(cards, card => card.ConceptKey == expectedConceptKey);
        Assert.DoesNotContain(cards, card => card.ConceptKey == broaderConceptKey);
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
    public void SelectContextualCards_DoesNotTreatSoftwareRefactoringAsCloudMigration()
    {
        var latest = CreateFinalSegment(
            "We are refactoring the parser, rearchitecting the test harness, and rehosting the website.");

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            [],
            [latest]);

        Assert.DoesNotContain(
            cards,
            card => card.ConceptKey is "refactor" or "rehost");
    }

    [Fact]
    public void SelectContextualCards_DetectsFragmentedMentionAcrossSegments()
    {
        var first = CreateFinalSegment("We will explain Availability");
        var second = CreateFinalSegment("Zones in the Azure design.");

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [first, second],
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            [],
            [first, second]);

        Assert.Contains(
            cards,
            card => string.Equals(card.ConceptKey, "availability-zone", StringComparison.Ordinal));
    }

    [Fact]
    public void SelectContextualCards_FamiliarAudienceOmitsFoundationalCatalogDefinitions()
    {
        var latest = CreateFinalSegment("Cloud computing is part of the architecture.");
        var beginner = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [latest],
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            [],
            [latest]);
        var familiar = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [latest],
                AudienceFamiliarity: AudienceFamiliarity.Familiar),
            [],
            [latest]);

        Assert.Contains(beginner, card => card.ConceptKey == "cloud-computing");
        Assert.DoesNotContain(familiar, card => card.ConceptKey == "cloud-computing");
    }

    [Fact]
    public void SelectContextualCards_UsesMemberEligibleSessionGlossary()
    {
        var sourceId = Guid.NewGuid();
        var latest = CreateFinalSegment(
            "Mantecatura is required during the final stage of the risotto.");
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            Template: SessionTemplateKind.Custom,
            Knowledge:
            [
                new SessionKnowledgeSnippet(
                    sourceId,
                    "culinary-guide.pdf",
                    KnowledgeSourceVisibility.MemberEligible,
                    """
                    Mini glossary
                    Mantecatura
                    Off-heat finishing of risotto with fat and cheese for a creamy texture.
                    """)
            ],
            AudienceFamiliarity: AudienceFamiliarity.Beginner);

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            context,
            [],
            [latest]);

        var card = Assert.Single(cards);
        Assert.Equal("Mantecatura", card.Title);
        Assert.Contains("Off-heat finishing", card.Content, StringComparison.Ordinal);
        Assert.Equal([sourceId], card.SourceKnowledgeIds);
        Assert.Equal(
            "Off-heat finishing of risotto with fat and cheese for a creamy texture.",
            card.KnowledgeEvidenceQuote);
    }

    [Fact]
    public void SelectContextualCards_DoesNotExposeHostPrivateGlossary()
    {
        var latest = CreateFinalSegment(
            "Mantecatura is required during the final stage of the risotto.");
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            Template: SessionTemplateKind.Custom,
            Knowledge:
            [
                new SessionKnowledgeSnippet(
                    Guid.NewGuid(),
                    "private-notes.pdf",
                    KnowledgeSourceVisibility.HostPrivate,
                    "Mantecatura\nPrivate host-only definition that must not appear.")
            ],
            AudienceFamiliarity: AudienceFamiliarity.Beginner);

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            context,
            [],
            [latest]);

        Assert.Empty(cards);
    }

    [Fact]
    public void SelectContextualCards_ExtractsDefinitionFromFlattenedPdfText()
    {
        var sourceId = Guid.NewGuid();
        var latest = CreateFinalSegment(
            "Mantecatura is required during the final stage of the risotto.");
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            Template: SessionTemplateKind.Custom,
            Knowledge:
            [
                new SessionKnowledgeSnippet(
                    sourceId,
                    "culinary-guide.pdf",
                    KnowledgeSourceVisibility.MemberEligible,
                    "Mini glossary Term Meaning Deglaze Add liquid to a hot pan and dissolve browned deposits "
                    + "Fond Browned proteins attached to the cooking surface "
                    + "Mantecatura Off-heat finishing of risotto with fat and cheese for a creamy texture "
                    + "Carryover cooking Temperature rise after food leaves the heat")
            ],
            AudienceFamiliarity: AudienceFamiliarity.Beginner);

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            context,
            [],
            [latest]);

        var card = Assert.Single(cards);
        Assert.Equal("Mantecatura", card.Title);
        Assert.Equal(
            "Off-heat finishing of risotto with fat and cheese for a creamy texture",
            card.Content);
        Assert.Equal(card.Content, card.KnowledgeEvidenceQuote);
    }

    [Theory]
    [InlineData(AudienceFamiliarity.Beginner, true)]
    [InlineData(AudienceFamiliarity.Familiar, false)]
    [InlineData(AudienceFamiliarity.Expert, false)]
    public void SelectContextualCards_AppliesFamiliarityToShortKnowledgeTerms(
        AudienceFamiliarity familiarity,
        bool expected)
    {
        var latest = CreateFinalSegment("Miso appears in this recipe.");
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            Template: SessionTemplateKind.Custom,
            Knowledge:
            [
                new SessionKnowledgeSnippet(
                    Guid.NewGuid(),
                    "culinary-guide.txt",
                    KnowledgeSourceVisibility.MemberEligible,
                    "Miso: Fermented soybean paste used for savory depth and seasoning.")
            ],
            AudienceFamiliarity: familiarity);

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            context,
            [],
            [latest]);

        Assert.Equal(expected, cards.Count > 0);
    }

    [Theory]
    [InlineData("Finish the dish with acid.", "Finish")]
    [InlineData("Texture matters in the final dish.", "Texture")]
    public void SelectContextualCards_RejectsGenericWordsOutsideFlattenedGlossary(
        string transcript,
        string rejectedTitle)
    {
        var latest = CreateFinalSegment(transcript);
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            Template: SessionTemplateKind.Custom,
            Knowledge:
            [
                new SessionKnowledgeSnippet(
                    Guid.NewGuid(),
                    "culinary-guide.pdf",
                    KnowledgeSourceVisibility.MemberEligible,
                    "Finish rich dishes with acid, fresh herbs, or both to restore balance. "
                    + "Texture preservation rules help pastry remain crisp. "
                    + "Mini glossary Term Meaning Deglaze Add liquid to a hot pan and dissolve browned deposits "
                    + "Mantecatura Off-heat finishing of risotto with fat and cheese for a creamy texture "
                    + "Grounding verification checklist")
            ],
            AudienceFamiliarity: AudienceFamiliarity.Beginner);

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            context,
            [],
            [latest]);

        Assert.DoesNotContain(
            cards,
            card => string.Equals(card.Title, rejectedTitle, StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(
        "Dawn Market Buckwheat Galette is the selected recipe.",
        "Market Buckwheat")]
    [InlineData(
        "It is important to check the guide.",
        "It is")]
    public void SelectContextualCards_UsesExplicitMiniGlossaryNotCoverMention(
        string transcript,
        string rejectedTitle)
    {
        var latest = CreateFinalSegment(transcript);
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            Template: SessionTemplateKind.Custom,
            Knowledge:
            [
                new SessionKnowledgeSnippet(
                    Guid.NewGuid(),
                    "culinary-guide.pdf",
                    KnowledgeSourceVisibility.MemberEligible,
                    "The guide contains recipes and a glossary. It is not a substitute for professional advice. "
                    + "Dawn Market Buckwheat Galette Recipe code DMG-08 serves four people. "
                    + "Mini glossary Term Meaning Mantecatura Off-heat finishing of risotto with fat and cheese. "
                    + "Grounding verification checklist")
            ],
            AudienceFamiliarity: AudienceFamiliarity.Beginner);

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            context,
            [],
            [latest]);

        Assert.DoesNotContain(
            cards,
            card => string.Equals(card.Title, rejectedTitle, StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task Coordinator_AcceptsDomainDefinitionGroundedInMemberEligibleKnowledge()
    {
        var sourceId = Guid.NewGuid();
        const string evidence =
            "Deglazing means adding liquid to a hot pan to loosen the flavorful browned pieces.";
        var store = new InMemoryMeetingSessionStore();
        var proposal = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "Deglazing",
            evidence,
            0.92,
            [])
        {
            SourceKnowledgeIds = [sourceId],
            KnowledgeEvidenceQuote = evidence
        };
        using var coordinator = CreateCoordinator(
            store,
            new StaticCardAgent(proposal),
            knowledgeReader: new StaticKnowledgeReader(
                new SessionKnowledgeSnippet(
                    sourceId,
                    "culinary-guide.pdf",
                    KnowledgeSourceVisibility.MemberEligible,
                    $"Kitchen glossary. {evidence} Use stock, wine, or water.")));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            CancellationToken.None);
        await store.SaveAsync(
            session with
            {
                KnowledgeSources =
                [
                    new KnowledgeSourceState(
                        sourceId,
                        KnowledgeSourceKind.File,
                        "culinary-guide.pdf",
                        KnowledgeSourceVisibility.MemberEligible,
                        KnowledgeSourceStatus.Ready,
                        DateTimeOffset.UtcNow)
                ]
            },
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "We use deglazing before finishing the sauce."),
            CancellationToken.None);

        var card = Assert.Single(updated.ContextualCards);
        Assert.Equal("Deglazing", card.Title);
        Assert.Equal([sourceId], card.KnowledgeSourceIds);
        Assert.Equal(MemberAlertStatus.Published, card.MemberAlertStatus);
    }

    [Fact]
    public async Task Coordinator_SafeAutomaticUsesOnlyCitedKnowledgeVisibility()
    {
        var memberSourceId = Guid.NewGuid();
        var privateSourceId = Guid.NewGuid();
        const string evidence =
            "Deglazing means adding liquid to a hot pan to loosen browned pieces.";
        var store = new InMemoryMeetingSessionStore();
        var proposal = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "Deglazing",
            evidence,
            0.92,
            [])
        {
            SourceKnowledgeIds = [memberSourceId],
            KnowledgeEvidenceQuote = evidence
        };
        using var coordinator = CreateCoordinator(
            store,
            new StaticCardAgent(proposal),
            knowledgeReader: new StaticKnowledgeReader(
                new SessionKnowledgeSnippet(
                    memberSourceId,
                    "member-guide.pdf",
                    KnowledgeSourceVisibility.MemberEligible,
                    evidence)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            CancellationToken.None);
        await store.SaveAsync(
            session with
            {
                KnowledgeSources =
                [
                    new KnowledgeSourceState(
                        memberSourceId,
                        KnowledgeSourceKind.File,
                        "member-guide.pdf",
                        KnowledgeSourceVisibility.MemberEligible,
                        KnowledgeSourceStatus.Ready,
                        DateTimeOffset.UtcNow),
                    new KnowledgeSourceState(
                        privateSourceId,
                        KnowledgeSourceKind.File,
                        "host-notes.pdf",
                        KnowledgeSourceVisibility.HostPrivate,
                        KnowledgeSourceStatus.Ready,
                        DateTimeOffset.UtcNow)
                ]
            },
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", "Deglazing happens now."),
            CancellationToken.None);

        Assert.Equal(
            MemberAlertStatus.Published,
            Assert.Single(updated.ContextualCards).MemberAlertStatus);
    }

    [Fact]
    public async Task Coordinator_RejectsDomainDefinitionWithoutExactKnowledgeEvidence()
    {
        var sourceId = Guid.NewGuid();
        var store = new InMemoryMeetingSessionStore();
        var proposal = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "Deglazing",
            "Deglazing adds liquid to a hot pan.",
            0.92,
            [])
        {
            SourceKnowledgeIds = [sourceId],
            KnowledgeEvidenceQuote = "This quote is not in the uploaded guide."
        };
        using var coordinator = CreateCoordinator(
            store,
            new StaticCardAgent(proposal),
            knowledgeReader: new StaticKnowledgeReader(
                new SessionKnowledgeSnippet(
                    sourceId,
                    "culinary-guide.pdf",
                    KnowledgeSourceVisibility.MemberEligible,
                    "Deglazing means adding liquid to a hot pan.")));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        await store.SaveAsync(
            session with
            {
                KnowledgeSources =
                [
                    new KnowledgeSourceState(
                        sourceId,
                        KnowledgeSourceKind.File,
                        "culinary-guide.pdf",
                        KnowledgeSourceVisibility.MemberEligible,
                        KnowledgeSourceStatus.Ready,
                        DateTimeOffset.UtcNow)
                ]
            },
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "Deglazing is the next technique."),
            CancellationToken.None);

        Assert.Empty(updated.ContextualCards);
    }

    [Fact]
    public async Task Coordinator_ExpertAudienceCanReceiveTechnicalHintWithoutBasicDefinition()
    {
        using var coordinator = CreateCoordinator(
            new InMemoryMeetingSessionStore(),
            new StaticCardAgent(
                new ContextualCardProposal(
                    ContextualCardKind.Hint,
                    "RTO",
                    "A shorter RTO can require automated failover and reserved recovery capacity.",
                    0.9,
                    [])));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                AudienceFamiliarity: AudienceFamiliarity.Expert),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "The RTO target changes our failover design."),
            CancellationToken.None);

        Assert.Equal(ContextualCardKind.Hint, Assert.Single(updated.ContextualCards).Kind);
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

        Assert.DoesNotContain(
            updated.ContextualCards,
            card => string.Equals(card.Content, content, StringComparison.Ordinal));
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
        var safeAlert = Assert.Single(updated.ContextualCards);
        Assert.Equal(ContextualCardKind.Definition, safeAlert.Kind);
        Assert.Equal("Azure Policy", safeAlert.Title);
        Assert.DoesNotContain("recommend", safeAlert.Content, StringComparison.OrdinalIgnoreCase);
    }

    public static TheoryData<EducationalConcept> PositiveConcepts =>
        BuildConceptData(EducationalConceptCatalog.All);

    public static TheoryData<EducationalConcept> NegatedConcepts =>
        BuildConceptData(EducationalConceptCatalog.All);

    public static TheoryData<EducationalConcept, string> VendorMismatchConcepts
    {
        get
        {
            var data = new TheoryData<EducationalConcept, string>();
            // Exclude concepts whose non-branded alias is itself in AzureContextTerms (e.g. "arm template" is
            // listed as an Azure context term in PresentationCoachingPolicy, so it is correctly detected even
            // when AWS is in the same sentence — vendor mismatch suppression does not apply).
            var alwaysAzureByDesign = new HashSet<string>(StringComparer.Ordinal) { "arm-template" };

            foreach (var concept in EducationalConceptCatalog.All.Where(c => c.RequiresAzureVendorScope))
            {
                if (alwaysAzureByDesign.Contains(concept.ConceptKey))
                    continue;

                var nonBrandedAlias = concept.Aliases.FirstOrDefault(a =>
                    !a.Contains("azure", StringComparison.OrdinalIgnoreCase) &&
                    !a.Contains("microsoft", StringComparison.OrdinalIgnoreCase) &&
                    !a.Contains("entra", StringComparison.OrdinalIgnoreCase));

                if (nonBrandedAlias != null)
                    data.Add(concept, nonBrandedAlias);
            }

            return data;
        }
    }

    [Fact]
    public void ConceptCatalog_AllConceptsHaveUniqueKeys()
    {
        var keys = EducationalConceptCatalog.All.Select(c => c.ConceptKey).ToList();
        var distinct = keys.Distinct(StringComparer.Ordinal).Count();
        Assert.Equal(keys.Count, distinct);
    }

    [Fact]
    public void ConceptCatalog_AllConceptsHaveUniqueCanonicalTitles()
    {
        var titles = EducationalConceptCatalog.All.Select(c => c.CanonicalTitle).ToList();
        var distinct = titles.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        Assert.Equal(titles.Count, distinct);
    }

    [Fact]
    public void ConceptCatalog_AllDefinitionsAndHintsAreClientReady()
    {
        var invalid = EducationalConceptCatalog.All
            .SelectMany(concept => new[]
            {
                (concept.ConceptKey, concept.CanonicalTitle, Kind: "definition", Text: concept.DefinitionText),
                (concept.ConceptKey, concept.CanonicalTitle, Kind: "hint", Text: concept.HintText)
            })
            .Where(entry => !PresentationCoachingPolicy.IsClientReadyExplanation(
                entry.CanonicalTitle,
                entry.Text))
            .Select(entry => $"{entry.ConceptKey}:{entry.Kind}")
            .ToArray();

        Assert.True(
            invalid.Length == 0,
            $"Client-ready validation failed for: {string.Join(", ", invalid)}");
    }

    [Fact]
    public void ConceptCatalog_NormalizedAliasesDoNotConflictAcrossConcepts()
    {
        var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var conflicts = new List<string>();

        foreach (var concept in EducationalConceptCatalog.All)
        {
            foreach (var alias in concept.Aliases.Append(concept.CanonicalTitle))
            {
                var normalized = HeuristicConversationCoachAgent.Normalize(alias);

                if (seen.TryGetValue(normalized, out var existingKey))
                {
                    if (!string.Equals(existingKey, concept.ConceptKey, StringComparison.Ordinal))
                        conflicts.Add($"'{alias}' ({concept.ConceptKey}) conflicts with ({existingKey})");
                }
                else
                {
                    seen[normalized] = concept.ConceptKey;
                }
            }
        }

        Assert.Empty(conflicts);
    }

    [Fact]
    public void ConceptCatalog_EveryConceptHasAtLeastOneSafeSpeechPhrase()
    {
        var vocab = new HashSet<string>(
            EducationalConceptCatalog.BuildSpeechPhraseVocabulary(),
            StringComparer.OrdinalIgnoreCase);

        var missing = EducationalConceptCatalog.All
            .Where(c => !c.Aliases.Append(c.CanonicalTitle).Any(vocab.Contains))
            .Select(c => c.ConceptKey)
            .ToList();

        Assert.Empty(missing);
    }

    [Fact]
    public void ConceptCatalog_CoversEveryCategory()
    {
        var presentCategories = EducationalConceptCatalog.All
            .Select(c => c.Category)
            .Distinct()
            .ToHashSet();

        foreach (var category in Enum.GetValues<ConceptCategory>()
                     .Where(category => category != ConceptCategory.DomainSpecific))
        {
            Assert.Contains(category, presentCategories);
        }
    }

    [Fact]
    public void ConceptCatalog_RepresentativeNewConceptsPresent()
    {
        var keys = EducationalConceptCatalog.All.Select(c => c.ConceptKey).ToHashSet(StringComparer.Ordinal);
        string[] expected =
        [
            "microsoft-fabric",
            "microsoft-foundry",
            "azure-managed-redis",
            "azure-container-registry",
            "azure-virtual-desktop",
            "azure-virtual-wan",
            "azure-web-application-firewall",
            "microsoft-entra-id-governance",
            "microsoft-entra-global-secure-access",
            "microsoft-purview",
            "azure-chaos-studio",
            "azure-monitor-agent",
            "data-collection-rule",
            "service-level-objective",
            "azure-developer-cli",
        ];

        foreach (var key in expected)
        {
            Assert.Contains(key, keys);
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
        ILogger<MeetingSessionCoordinator>? logger = null,
        ISessionKnowledgeReader? knowledgeReader = null) =>
        new(
            store,
            new MeetingChecklistPlanner(),
            agent,
            null,
            new NullSessionUpdatePublisher(),
            logger,
            knowledgeReader: knowledgeReader);

    private sealed class StaticKnowledgeReader(SessionKnowledgeSnippet snippet)
        : ISessionKnowledgeReader
    {
        public Task<IReadOnlyList<SessionKnowledgeSnippet>> ReadAsync(
            Guid sessionId,
            IReadOnlyCollection<Guid> allowedSourceIds,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<SessionKnowledgeSnippet>>(
                allowedSourceIds.Contains(snippet.SourceId) ? [snippet] : []);
    }

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
