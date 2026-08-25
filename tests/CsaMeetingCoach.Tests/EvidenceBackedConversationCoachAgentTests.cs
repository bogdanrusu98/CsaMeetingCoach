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
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [latest],
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
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
            card => card.Kind == ContextualCardKind.Definition
                && card.Title == "health probe"
                && card.Content.StartsWith(
                    "A health probe",
                    StringComparison.Ordinal)
                && !card.Content.Contains('?'));
    }

    [Theory]
    [InlineData(
        "Cloud computing provides on-demand resources.",
        "Cloud computing")]
    [InlineData(
        "The shared responsibility model changes operational duties.",
        "shared responsibility model")]
    [InlineData(
        "Infrastructure as a Service provides virtualized infrastructure.",
        "Infrastructure as a Service")]
    [InlineData(
        "Platform as a Service provides managed runtimes.",
        "Platform as a Service")]
    [InlineData(
        "Software as a Service provides a complete hosted application.",
        "Software as a Service")]
    [InlineData(
        "Azure regions contain connected datacenters.",
        "Azure regions")]
    [InlineData(
        "Azure Availability Zones isolate datacenter failures.",
        "Azure Availability Zones")]
    [InlineData(
        "Azure Resource Manager handles management requests.",
        "Azure Resource Manager")]
    [InlineData(
        "Management groups organize subscriptions in Azure.",
        "Management groups")]
    [InlineData(
        "An Azure subscription is a management boundary.",
        "Azure subscription")]
    [InlineData(
        "An Azure resource group holds related resources.",
        "Azure resource group")]
    [InlineData(
        "Microsoft Entra ID handles cloud identities.",
        "Microsoft Entra ID")]
    [InlineData(
        "Azure RBAC controls resource authorization.",
        "Azure RBAC")]
    public async Task Analyze_FoundationalAzureTerm_AddsClientReadyDefinition(
        string text,
        string expectedTitle)
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            text,
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [latest],
                AudienceFamiliarity: AudienceFamiliarity.Beginner),
            latest,
            CancellationToken.None);

        var card = Assert.Single(decision.ContextualCards.Where(
            candidate => candidate.Title == expectedTitle));
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.Equal(expectedTitle, card.Title);
        Assert.Equal([latest.Id], card.SourceTranscriptSegmentIds);
        Assert.True(PresentationCoachingPolicy.IsClientReadyExplanation(
            card.Title,
            card.Content));
    }

    [Fact]
    public async Task Analyze_FoundationalTutorialSequence_AddsUsefulCoaching()
    {
        var transcript = new[]
        {
            "Cloud computing provides technology services over the internet.",
            "The shared responsibility model divides duties between provider and customer.",
            "Infrastructure as a Service is commonly shortened to IaaS.",
            "Platform as a Service is commonly shortened to PaaS.",
            "Software as a Service is commonly shortened to SaaS.",
            "Azure regions contain connected datacenters.",
            "Availability Zones provide fault isolation inside a region.",
            "Azure Resource Manager is the management layer.",
            "Management groups sit above Azure subscriptions.",
            "A resource group contains related Azure resources.",
            "Microsoft Entra ID authenticates identities.",
            "Azure RBAC controls authorization at a scope."
        }
            .Select((text, index) => new TranscriptSegment(
                Guid.NewGuid(),
                "Presenter",
                text,
                DateTimeOffset.UtcNow.AddSeconds(index),
                IsFinal: true))
            .ToArray();
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose() with
                {
                    MeetingType = "Azure presentation",
                    Objective = "Explain Azure foundations"
                },
                [],
                transcript),
            transcript[^1],
            CancellationToken.None);

        Assert.Equal(2, decision.ContextualCards.Count);
        Assert.All(
            decision.ContextualCards,
            card => Assert.True(
                PresentationCoachingPolicy.IsClientReadyExplanation(
                    card.Title,
                    card.Content)));
        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Contains(
            "service models",
            recommendation.Title,
            StringComparison.OrdinalIgnoreCase);
        Assert.NotEmpty(recommendation.SourceTranscriptSegmentIds);
    }

    [Fact]
    public async Task Analyze_FragmentedResourceManagerName_GroundsAcrossSources()
    {
        var first = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Every request flows through Azure Resource",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            IsFinal: true);
        var second = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Manager before reaching a resource provider.",
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

        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal("Azure Resource Manager", card.Title);
        Assert.Equal([first.Id, second.Id], card.SourceTranscriptSegmentIds);
        Assert.Equal(
            [first.Id, second.Id],
            Assert.Single(decision.RecommendedTasks)
                .SourceTranscriptSegmentIds);
    }

    [Fact]
    public async Task Analyze_AmbiguousArmWord_DoesNotTriggerAzureResourceManager()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Raise your arm if the screen is visible.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Empty(decision.ContextualCards);
    }

    [Theory]
    [InlineData("AWS Availability Zones isolate failures.")]
    [InlineData("A Kubernetes resource group controls these objects.")]
    [InlineData("Kubernetes role-based access control authorizes service accounts.")]
    public async Task Analyze_NonAzurePlatformTerm_DoesNotTriggerAzureCoaching(
        string text)
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            text,
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Empty(decision.ContextualCards);
    }

    [Fact]
    public async Task Analyze_ConflictingThenAzureMention_UsesValidAzureOccurrence()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "A Kubernetes resource group controls objects. "
            + "An Azure resource group contains Azure resources.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal("Azure resource group", card.Title);
        Assert.Single(decision.RecommendedTasks);
    }

    [Fact]
    public async Task Analyze_AzurePrimaryOutputFromAwsEvidence_IsRejected()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "AWS Availability Zones isolate failures.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Align Azure Availability Zones to resilience requirements",
            "Connect Azure zone design to the customer's recovery targets.",
            0.9,
            [latest.Id]);
        var primaryCard = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "Azure Availability Zones",
            "Azure Availability Zones isolate datacenter failures within a region.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [primaryRecommendation], [])
            {
                ContextualCards = [primaryCard]
            }),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Empty(decision.ContextualCards);
    }

    [Fact]
    public async Task Analyze_UnbrandedAzureConceptFromKubernetesEvidence_IsRejected()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "A Kubernetes resource group controls these objects.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Map resource groups to governance scopes",
            "Connect each resource group to inherited access and policy.",
            0.9,
            [latest.Id]);
        var primaryCard = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "resource group",
            "A resource group is a lifecycle boundary for related resources.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [primaryRecommendation], [])
            {
                ContextualCards = [primaryCard]
            }),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Empty(decision.ContextualCards);
    }

    [Fact]
    public async Task Analyze_PrimaryAzureOutputFromMixedSource_UsesAzureSentence()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "AWS Availability Zones isolate failures. "
            + "Azure Availability Zones isolate failures for Azure workloads.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Validate Azure Availability Zones for workload resilience",
            "Connect Azure Availability Zones to the workload's resilience target.",
            0.9,
            [latest.Id]);
        var primaryCard = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "Azure Availability Zones",
            "Azure Availability Zones provide isolated failure domains for an Azure workload.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [primaryRecommendation], [])
            {
                ContextualCards = [primaryCard]
            }),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Equal(
            primaryRecommendation.Title,
            Assert.Single(decision.RecommendedTasks).Title);
        Assert.Equal(
            primaryCard.Content,
            Assert.Single(decision.ContextualCards).Content);
    }

    [Fact]
    public async Task Analyze_UnrelatedAzureSentence_DoesNotGroundConflictingConcept()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "The Kubernetes resource manager reconciles objects. "
            + "Azure subscriptions define billing boundaries.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Map resource manager controls to governance",
            "Connect resource manager behavior to access and policy.",
            0.9,
            [latest.Id]);
        var primaryCard = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "resource manager",
            "A resource manager provides a management layer for resources.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [primaryRecommendation], [])
            {
                ContextualCards = [primaryCard]
            }),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.DoesNotContain(
            decision.RecommendedTasks,
            recommendation => recommendation.Title == primaryRecommendation.Title);
        Assert.DoesNotContain(
            decision.ContextualCards,
            card => card.Title == primaryCard.Title);
    }

    [Fact]
    public async Task Analyze_MultiConceptOutput_RequiresEvidenceForEveryConcept()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "The Kubernetes resource manager reconciles objects. "
            + "Azure subscriptions define billing boundaries.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Map resource manager controls to Azure subscriptions",
            "Connect resource manager behavior and Azure subscription boundaries.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision(
                [],
                [primaryRecommendation],
                [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.DoesNotContain(
            decision.RecommendedTasks,
            recommendation => recommendation.Title == primaryRecommendation.Title);
    }

    [Fact]
    public async Task Analyze_AdjacentAzureContext_GroundsAvailabilityZones()
    {
        var azureContext = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "We are now discussing Azure.",
            DateTimeOffset.UtcNow.AddSeconds(-1),
            IsFinal: true);
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Availability Zones isolate datacenter failures.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(
                TestData.CreatePurpose(),
                [],
                [azureContext, latest]),
            latest,
            CancellationToken.None);

        Assert.Contains(
            decision.ContextualCards,
            card => card.Title == "Availability Zones");
        Assert.Single(decision.RecommendedTasks);
    }

    [Fact]
    public async Task Analyze_AwsToAzureTransition_UsesNearestPlatformMarker()
    {
        var transcript = new[]
        {
            "AWS uses Availability Zones.",
            "Now switch to Azure.",
            "Availability Zones provide fault isolation."
        }
            .Select((text, index) => new TranscriptSegment(
                Guid.NewGuid(),
                "Presenter",
                text,
                DateTimeOffset.UtcNow.AddSeconds(index),
                IsFinal: true))
            .ToArray();
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], transcript),
            transcript[^1],
            CancellationToken.None);

        Assert.Contains(
            decision.ContextualCards,
            card => card.Title == "Availability Zones");
        Assert.Single(decision.RecommendedTasks);
    }

    [Fact]
    public async Task Analyze_PostConceptAzureQualifier_OverridesDistantAwsComparison()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Unlike AWS, Availability Zones in Azure isolate failures.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Empty(decision.ContextualCards);
        Assert.Empty(decision.RecommendedTasks);
    }

    [Fact]
    public async Task Analyze_UncatalogedAzureOutput_RequiresMatchingTechnicalAnchor()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "AWS Policy controls access. "
            + "Azure governance requirements define oversight.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Validate Azure Policy controls",
            "Connect Azure Policy to the customer's governance requirements.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision(
                [],
                [primaryRecommendation],
                [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.DoesNotContain(
            decision.RecommendedTasks,
            recommendation => recommendation.Title == primaryRecommendation.Title);
    }

    [Fact]
    public async Task Analyze_AzureKubernetesService_IsNotConflictingVendorContext()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Azure Kubernetes Service uses Availability Zones "
            + "for node-pool resilience.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision([], [], [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Contains(
            decision.ContextualCards,
            card => card.Title == "Availability Zones");
        Assert.Single(decision.RecommendedTasks);
    }

    [Fact]
    public async Task Analyze_UncatalogedAzureOutput_AcceptsMatchingAzureAnchor()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Azure Policy evaluates resources against governance rules.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var primaryRecommendation = new RecommendedTaskProposal(
            "Validate Azure Policy controls",
            "Connect Azure Policy to the customer's governance requirements.",
            0.9,
            [latest.Id]);
        var agent = new EvidenceBackedConversationCoachAgent(
            new StubAgent(new CoachAgentDecision(
                [],
                [primaryRecommendation],
                [])),
            new HeuristicConversationCoachAgent());

        var decision = await agent.AnalyzeAsync(
            new CoachAgentContext(TestData.CreatePurpose(), [], [latest]),
            latest,
            CancellationToken.None);

        Assert.Equal(
            primaryRecommendation.Title,
            Assert.Single(decision.RecommendedTasks).Title);
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
            "Azure Load Balancer is at layer four.",
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
            "Azure Load Balancer is a regional service.",
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
        Assert.Equal("Azure Load Balancer", card.Title);
        Assert.NotEqual(invalidCard.SourceTranscriptSegmentIds, card.SourceTranscriptSegmentIds);
        Assert.Equal([latest.Id], card.SourceTranscriptSegmentIds);
    }

    [Theory]
    [InlineData("Ask the customer how the health probe should be configured?")]
    [InlineData("The CSA should confirm the client's health probe settings.")]
    [InlineData("Can health probes detect backend availability")]
    [InlineData("We should confirm the health probe settings.")]
    [InlineData("The client should configure health probes.")]
    [InlineData(
        "Health probes control eligibility. How should the client configure them")]
    [InlineData(
        "Health probes control eligibility. Ask the client to confirm the settings.")]
    [InlineData(
        "Health probes control eligibility. Then how should the client configure them")]
    [InlineData(
        "Health probes control eligibility; then ask the client to confirm the settings.")]
    [InlineData(
        "Health probes control eligibility, then ask the client to confirm the settings.")]
    [InlineData("Discuss the probe settings with the client.")]
    [InlineData("Show the client the validation results.")]
    public async Task Analyze_InstructionShapedCard_IsReplacedByClientReadyExplanation(
        string content)
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "The health probe checks each backend instance.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var discoveryQuestion = new ContextualCardProposal(
            ContextualCardKind.Hint,
            "health probe",
            content,
            0.92,
            [latest.Id]);
        var primaryDecision = new CoachAgentDecision([], [], [])
        {
            ContextualCards = [discoveryQuestion]
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

        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.Equal("health probe", card.Title);
        Assert.StartsWith("A health probe", card.Content);
        Assert.DoesNotContain('?', card.Content);
        Assert.NotSame(discoveryQuestion, card);
    }

    [Fact]
    public async Task Analyze_DeclarativeValidationExplanation_IsPreserved()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "The health probe checks each backend instance.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var explanation = new ContextualCardProposal(
            ContextualCardKind.Definition,
            "health probe",
            "Health probes validate backend availability before new traffic is sent.",
            0.92,
            [latest.Id]);
        var primaryDecision = new CoachAgentDecision([], [], [])
        {
            ContextualCards = [explanation]
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

        Assert.Same(explanation, Assert.Single(decision.ContextualCards));
    }

    [Theory]
    [InlineData(
        "Can health probes detect backend availability",
        "Health probes detect unhealthy backend instances.")]
    [InlineData(
        "health probe",
        "Can health probes detect backend availability")]
    public void ClientReadyExplanation_UnpunctuatedQuestion_IsRejected(
        string title,
        string content)
    {
        Assert.False(PresentationCoachingPolicy.IsClientReadyExplanation(
            title,
            content));
    }

    [Fact]
    public void ClientReadyExplanation_HyphenatedTechnicalTerm_IsAccepted()
    {
        Assert.True(PresentationCoachingPolicy.IsClientReadyExplanation(
            "What-If deployment analysis",
            "Azure What-If previews resource changes before a deployment runs."));
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
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
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
