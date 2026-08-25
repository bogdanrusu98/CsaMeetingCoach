using System.Text.Json;
using System.Text.Json.Serialization;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class FoundryConversationCoachAgentTests
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        options.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        return options;
    }

    [Fact]
    public async Task Analyze_SendsMinimalMeetingDataAndParsesDecision()
    {
        var latestSegment = CreateLatestSegment();
        var acceptedRecommendation = new RecommendedTaskState(
            Guid.NewGuid(),
            "Discuss rollout risk",
            "This helps the customer choose a safe rollout.",
            0.9,
            [Guid.NewGuid()],
            RecommendationStatus.Accepted,
            latestSegment.OccurredAtUtc.AddMinutes(-2),
            latestSegment.OccurredAtUtc.AddMinutes(-1));
        var context = CreateContext(latestSegment, [acceptedRecommendation]);
        var checklistItem = context.Checklist[0];
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        checklistItem.Id,
                        true,
                        0.93,
                        "The owner and deadline are explicit.",
                        "owner and deadline")
                ],
                [
                    new RecommendedTaskProposal(
                        "Capture the agreed owner and deadline",
                        "The latest segment contains an explicit follow-up.",
                        0.9,
                        [latestSegment.Id])
                ],
                [
                    new RecommendationEvaluation(
                        acceptedRecommendation.Id,
                        true,
                        0.91,
                        "The topic was explicitly covered.",
                        "owner and deadline")
                ]),
            JsonOptions);
        var client = new RecordingFoundryClient(response);
        var agent = new FoundryConversationCoachAgent(client);

        var decision = await agent.AnalyzeAsync(
            context,
            latestSegment,
            CancellationToken.None);

        Assert.Single(decision.ChecklistEvaluations);
        Assert.Single(decision.RecommendedTasks);
        Assert.Single(decision.RecommendationEvaluations!);
        using var payload = JsonDocument.Parse(client.InputJson!);
        var root = payload.RootElement;
        Assert.Equal(
            context.Purpose.Objective,
            root.GetProperty("meetingPurpose").GetProperty("objective").GetString());
        Assert.Equal(
            latestSegment.Id,
            root.GetProperty("latestSegment").GetProperty("id").GetGuid());
        Assert.Equal(
            latestSegment.Id,
            root.GetProperty("analysisWindow")[0].GetProperty("id").GetGuid());
        var pendingItem = root.GetProperty("pendingChecklist")[0];
        Assert.Equal(checklistItem.Id, pendingItem.GetProperty("id").GetGuid());
        Assert.False(pendingItem.TryGetProperty("status", out _));
        Assert.False(pendingItem.TryGetProperty("evidence", out _));
        Assert.False(
            root.GetProperty("latestSegment").TryGetProperty("isFinal", out _));
        Assert.Equal(
            acceptedRecommendation.Id,
            root.GetProperty("acceptedRecommendations")[0].GetProperty("id").GetGuid());
    }

    [Fact]
    public async Task Analyze_ResponseHasUnknownProperty_FailsClosed()
    {
        var latestSegment = CreateLatestSegment();
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(
                """{"checklistEvaluations":[],"recommendedTasks":[],"recommendationEvaluations":[],"extra":true}"""));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.AnalyzeAsync(
                CreateContext(latestSegment),
                latestSegment,
                CancellationToken.None));

        Assert.Contains("does not match the contract", exception.Message);
    }

    [Fact]
    public async Task Analyze_InitialInvalidDecision_RetriesOnce()
    {
        var latestSegment = CreateLatestSegment();
        var validResponse = JsonSerializer.Serialize(
            new CoachAgentDecision([], [], []),
            JsonOptions);
        var client = new SequenceFoundryClient(
            """{"checklistEvaluations":[],"recommendedTasks":[],"recommendationEvaluations":[],"extra":true}""",
            validResponse);
        var agent = new FoundryConversationCoachAgent(client);

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Equal(2, client.CallCount);
    }

    [Fact]
    public async Task Analyze_CompletionInventsEvidence_IsFilteredBeforeCoordinatorMerge()
    {
        var latestSegment = CreateLatestSegment();
        var context = CreateContext(latestSegment);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        context.Checklist[0].Id,
                        true,
                        0.9,
                        "Invented evidence.",
                        "customer approved the plan")
                ],
                [],
                []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            context,
            latestSegment,
            CancellationToken.None);

        Assert.Empty(decision.ChecklistEvaluations);
    }

    [Fact]
    public async Task Analyze_TaskReferencesUnknownSegment_IsFilteredBeforeCoordinatorMerge()
    {
        var latestSegment = CreateLatestSegment();
        var context = CreateContext(latestSegment);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision(
                [],
                [
                    new RecommendedTaskProposal(
                        "Unsupported task",
                        "No matching source.",
                        0.8,
                        [Guid.NewGuid()])
                ],
                []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            context,
            latestSegment,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
    }

    [Fact]
    public async Task Analyze_TaskReferencesKnownEarlierSegment_IsPreserved()
    {
        var latestSegment = CreateLatestSegment("The next topic is the delivery timeline.");
        var earlierSegment = CreateLatestSegment(
            "We need to assess the database migration dependencies.");
        var context = CreateContext(
            latestSegment,
            transcript: [earlierSegment, latestSegment]);
        var proposal = new RecommendedTaskProposal(
            "Assess the database migration dependencies",
            "An earlier customer statement introduced a concrete migration need.",
            0.88,
            [earlierSegment.Id]);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], [proposal], []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            context,
            latestSegment,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Equal(proposal.Title, recommendation.Title);
        Assert.Equal(proposal.Rationale, recommendation.Rationale);
        Assert.Equal(proposal.Confidence, recommendation.Confidence);
        Assert.Equal(
            proposal.SourceTranscriptSegmentIds,
            recommendation.SourceTranscriptSegmentIds);
    }

    [Fact]
    public async Task Analyze_ContextualCardsRequireLatestGroundedSegment()
    {
        var latestSegment = CreateLatestSegment(
            "The recovery objective requires an RTO of one hour.");
        var context = CreateContext(latestSegment);
        var decisionPayload = new CoachAgentDecision([], [], [])
        {
            ContextualCards =
            [
                new ContextualCardProposal(
                    ContextualCardKind.Definition,
                    "RTO",
                    "Recovery Time Objective is the maximum target time for restoring a service.",
                    0.92,
                    [latestSegment.Id]),
                new ContextualCardProposal(
                    ContextualCardKind.Hint,
                    "Unsupported hint",
                    "This card has an invented transcript source.",
                    0.95,
                    [Guid.NewGuid()])
            ]
        };
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(
                JsonSerializer.Serialize(decisionPayload, JsonOptions)));

        var decision = await agent.AnalyzeAsync(
            context,
            latestSegment,
            CancellationToken.None);

        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.Equal("RTO", card.Title);
        Assert.Equal(latestSegment.Id, Assert.Single(card.SourceTranscriptSegmentIds));
    }

    [Fact]
    public async Task Analyze_ContextualCardCanUseEarlierFragmentInAnalysisWindow()
    {
        var topicSegment = CreateLatestSegment(
            "Azure Load Balancer provides layer four traffic distribution.");
        var latestSegment = CreateLatestSegment(
            "The frontend IP can be internal or external.");
        var context = CreateContext(
            latestSegment,
            transcript: [topicSegment, latestSegment]) with
        {
            AudienceFamiliarity = AudienceFamiliarity.Familiar
        };
        var decisionPayload = new CoachAgentDecision([], [], [])
        {
            ContextualCards =
            [
                new ContextualCardProposal(
                    ContextualCardKind.Definition,
                    "Azure Load Balancer",
                    "Azure Load Balancer distributes layer four TCP and UDP traffic across healthy backend resources.",
                    0.92,
                    [topicSegment.Id])
            ]
        };
        var client = new RecordingFoundryClient(
            JsonSerializer.Serialize(decisionPayload, JsonOptions));
        var agent = new FoundryConversationCoachAgent(client);

        var decision = await agent.AnalyzeAsync(
            context,
            latestSegment,
            CancellationToken.None);

        var card = Assert.Single(decision.ContextualCards);
        Assert.Equal("Azure Load Balancer", card.Title);
        Assert.Equal(topicSegment.Id, Assert.Single(card.SourceTranscriptSegmentIds));
        using var payload = JsonDocument.Parse(client.InputJson!);
        Assert.Equal(2, payload.RootElement.GetProperty("analysisWindow").GetArrayLength());
        Assert.Equal(
            "familiar",
            payload.RootElement.GetProperty("audienceFamiliarity").GetString());
        Assert.Contains(
            "specialized",
            payload.RootElement
                .GetProperty("familiarityBehavior")
                .GetProperty("definitionScope")
                .GetString(),
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "presentation",
            FoundryAgentContract.Instructions,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "sourceKnowledgeIds",
            FoundryAgentContract.ResponseJsonSchema,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_MultipleRecommendedTasks_FailsClosed()
    {
        var latestSegment = CreateLatestSegment();
        var proposals = new[]
        {
            new RecommendedTaskProposal(
                "Clarify the workload",
                "Requirements are incomplete.",
                0.8,
                [latestSegment.Id]),
            new RecommendedTaskProposal(
                "Compare two services",
                "The customer requested options.",
                0.8,
                [latestSegment.Id])
        };
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], proposals, []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.AnalyzeAsync(
                CreateContext(latestSegment),
                latestSegment,
                CancellationToken.None));

        Assert.Contains("more than one recommended task", exception.Message);
    }

    [Fact]
    public async Task Analyze_IncludesTemplateAndIsolatedSessionKnowledge()
    {
        var latestSegment = CreateLatestSegment("Explain the recovery objective.");
        var client = new RecordingFoundryClient(
            JsonSerializer.Serialize(new CoachAgentDecision([], [], []), JsonOptions));
        var agent = new FoundryConversationCoachAgent(client);
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latestSegment],
            Template: SessionTemplateKind.Training,
            Knowledge:
            [
                new SessionKnowledgeSnippet(
                    Guid.NewGuid(),
                    "training-guide.txt",
                    KnowledgeSourceVisibility.MemberEligible,
                    "RTO is the target restoration time.")
            ]);

        await agent.AnalyzeAsync(context, latestSegment, CancellationToken.None);

        using var payload = JsonDocument.Parse(client.InputJson!);
        Assert.Equal(
            "training",
            payload.RootElement.GetProperty("sessionTemplate").GetString());
        var knowledge = Assert.Single(
            payload.RootElement.GetProperty("reviewedSessionKnowledge").EnumerateArray());
        Assert.Equal(
            "training-guide.txt",
            knowledge.GetProperty("displayName").GetString());
        Assert.Contains(
            "untrusted data",
            FoundryAgentContract.Instructions,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Analyze_ClientFailure_DoesNotFallBack()
    {
        var latestSegment = CreateLatestSegment();
        var providerException = new InvalidOperationException("Foundry unavailable.");
        var agent = new FoundryConversationCoachAgent(
            new ThrowingFoundryClient(providerException));

        var observed = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.AnalyzeAsync(
                CreateContext(latestSegment),
                latestSegment,
                CancellationToken.None));

        Assert.Same(providerException, observed);
    }

    [Fact]
    public async Task Analyze_UnknownRecommendationEvaluation_IsFilteredBeforeCoordinatorMerge()
    {
        var latestSegment = CreateLatestSegment();
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision(
                [],
                [],
                [
                    new RecommendationEvaluation(
                        Guid.NewGuid(),
                        true,
                        0.9,
                        "Unsupported.",
                        "owner and deadline")
                ]),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        Assert.Empty(decision.RecommendationEvaluations!);
    }

    [Fact]
    public async Task Analyze_RecommendationCompletionInventsEvidence_IsFilteredBeforeCoordinatorMerge()
    {
        var latestSegment = CreateLatestSegment();
        var acceptedRecommendation = new RecommendedTaskState(
            Guid.NewGuid(),
            "Confirm the delivery owner",
            "The plan needs explicit ownership.",
            0.9,
            [Guid.NewGuid()],
            RecommendationStatus.Accepted,
            latestSegment.OccurredAtUtc.AddMinutes(-2),
            latestSegment.OccurredAtUtc.AddMinutes(-1));
        var context = CreateContext(latestSegment, [acceptedRecommendation]);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision(
                [],
                [],
                [
                    new RecommendationEvaluation(
                        acceptedRecommendation.Id,
                        true,
                        0.94,
                        "The owner was confirmed.",
                        "This quote was never spoken.")
                ]),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            context,
            latestSegment,
            CancellationToken.None);

        Assert.Empty(decision.RecommendationEvaluations!);
    }

    [Fact]
    public async Task Analyze_MigrationWithGenericTask_EnforcesCrossCuttingProducts()
    {
        var latestSegment = CreateLatestSegment(
            "We plan to migrate a representative wave of on-premises applications and servers to Azure.");
        var genericProposal = new RecommendedTaskProposal(
            "Define a practical migration assessment approach",
            "The estate has not yet been assessed.",
            0.9,
            [latestSegment.Id]);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], [genericProposal], []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Contains("Azure Migrate", recommendation.Title, StringComparison.Ordinal);
        Assert.Contains("Microsoft Entra ID", recommendation.Title, StringComparison.Ordinal);
        Assert.Contains("Defender for Cloud", recommendation.Title, StringComparison.Ordinal);
        Assert.Equal(
            new[] { latestSegment.Id },
            recommendation.SourceTranscriptSegmentIds);
    }

    [Fact]
    public async Task Analyze_MigrationWithCustomTask_UsesControlledPolicyTask()
    {
        var latestSegment = CreateLatestSegment(
            "We are migrating our on-premises server estate to Azure.");
        var groundedProposal = new RecommendedTaskProposal(
            "Assess with Azure Migrate and validate Microsoft Entra ID plus Azure Policy",
            "The migration requires workload discovery and landing-zone readiness checks.",
            0.92,
            [latestSegment.Id]);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], [groundedProposal], []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.NotEqual(groundedProposal.Title, recommendation.Title);
        Assert.Contains("Azure Migrate", recommendation.Title, StringComparison.Ordinal);
        Assert.Contains("Microsoft Entra ID", recommendation.Title, StringComparison.Ordinal);
        Assert.Contains("Defender for Cloud", recommendation.Title, StringComparison.Ordinal);
        Assert.Equal(
            new[] { latestSegment.Id },
            recommendation.SourceTranscriptSegmentIds);
    }

    [Fact]
    public async Task Analyze_NegatedMigration_DoesNotInjectProducts()
    {
        var latestSegment = CreateLatestSegment(
            "Migration is out of scope and we will not migrate this application.");
        var genericProposal = new RecommendedTaskProposal(
            "Confirm the current support model",
            "The customer excluded migration.",
            0.85,
            [latestSegment.Id]);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], [genericProposal], []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Equal(genericProposal.Title, recommendation.Title);
        Assert.Equal(genericProposal.Rationale, recommendation.Rationale);
        Assert.Equal(
            genericProposal.SourceTranscriptSegmentIds,
            recommendation.SourceTranscriptSegmentIds);
    }

    [Theory]
    [InlineData(
        "We need an enterprise RAG assistant over approved internal documents.",
        "Microsoft Foundry",
        "Azure AI Search",
        "Microsoft Entra ID")]
    [InlineData(
        "Our teams need one hybrid operating model for servers that remain on premises.",
        "Azure Arc",
        "Azure Policy",
        "Defender for Cloud")]
    [InlineData(
        "The application has an RTO of one hour and an RPO of fifteen minutes for disaster recovery.",
        "Azure Site Recovery",
        "Azure Backup",
        "Azure Monitor")]
    [InlineData(
        "We are modernizing this web application to reduce infrastructure management.",
        "Azure App Service",
        "Azure Container Apps",
        "Microsoft Entra ID")]
    public async Task Analyze_KnownProjectScenario_EnforcesRelevantDependencies(
        string transcript,
        string firstProduct,
        string secondProduct,
        string thirdProduct)
    {
        var latestSegment = CreateLatestSegment(transcript);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], [], []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Contains(firstProduct, recommendation.Title, StringComparison.Ordinal);
        Assert.Contains(secondProduct, recommendation.Title, StringComparison.Ordinal);
        Assert.Contains(thirdProduct, recommendation.Title, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("RAG is out of scope; use deterministic search instead.")]
    [InlineData("Containers are not required for this application.")]
    [InlineData("Disaster recovery is out of scope for this sandbox.")]
    [InlineData("We are not modernizing this web application.")]
    [InlineData("Azure SQL is not in scope for this workload.")]
    [InlineData("We will not remain on premises, so this is not a hybrid design.")]
    [InlineData("We won't migrate this application to Azure.")]
    [InlineData("RTO is out of scope for this non-production sandbox.")]
    [InlineData("Multi-cloud is out of scope for this architecture.")]
    public async Task Analyze_ExplicitlyExcludedScenario_DoesNotInjectProducts(
        string transcript)
    {
        var latestSegment = CreateLatestSegment(transcript);
        var genericProposal = new RecommendedTaskProposal(
            "Clarify the remaining requirement",
            "The customer excluded one option.",
            0.85,
            [latestSegment.Id]);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], [genericProposal], []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Equal(genericProposal.Title, recommendation.Title);
        Assert.Equal(genericProposal.Rationale, recommendation.Rationale);
    }

    [Fact]
    public async Task Analyze_ProjectRagStatus_DoesNotTreatAcronymAsEnterpriseAi()
    {
        var latestSegment = CreateLatestSegment(
            "The project RAG status is red because the delivery milestone is late.");
        var genericProposal = new RecommendedTaskProposal(
            "Clarify the delivery blocker",
            "The project status is red.",
            0.85,
            [latestSegment.Id]);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision([], [genericProposal], []),
            JsonOptions);
        var agent = new FoundryConversationCoachAgent(
            new RecordingFoundryClient(response));

        var decision = await agent.AnalyzeAsync(
            CreateContext(latestSegment),
            latestSegment,
            CancellationToken.None);

        var recommendation = Assert.Single(decision.RecommendedTasks);
        Assert.Equal(genericProposal.Title, recommendation.Title);
        Assert.DoesNotContain(
            "Microsoft Foundry",
            recommendation.Title,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        SessionTemplateKind.Presentation,
        "Presenter",
        "narrative clarity",
        "audience")]
    [InlineData(
        SessionTemplateKind.Workshop,
        "Facilitator",
        "trade-offs",
        "assumptions")]
    [InlineData(
        SessionTemplateKind.Training,
        "Trainer",
        "understanding checks",
        "common pitfalls")]
    [InlineData(
        SessionTemplateKind.Custom,
        "Session host",
        "configured objective",
        "configured objective")]
    [InlineData(
        SessionTemplateKind.CsaVbd,
        "Customer-facing CSA",
        "business value",
        "Azure concepts")]
    public async Task Analyze_SendsTrustedTemplateBehavior(
        SessionTemplateKind template,
        string expectedRole,
        string expectedRecommendationFocus,
        string expectedMemberAlertFocus)
    {
        var latestSegment = CreateLatestSegment("An explicit session topic.");
        var client = new RecordingFoundryClient(
            """{"checklistEvaluations":[],"recommendedTasks":[],"recommendationEvaluations":[],"contextualCards":[]}""");

        await new FoundryConversationCoachAgent(client).AnalyzeAsync(
            CreateContext(latestSegment, template: template),
            latestSegment,
            CancellationToken.None);

        using var document = JsonDocument.Parse(Assert.IsType<string>(client.InputJson));
        var behavior = document.RootElement.GetProperty("templateBehavior");
        Assert.Equal(expectedRole, behavior.GetProperty("hostRole").GetString());
        Assert.Contains(
            expectedRecommendationFocus,
            behavior.GetProperty("recommendationFocus").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            expectedMemberAlertFocus,
            behavior.GetProperty("memberAlertFocus").GetString()!,
            StringComparison.OrdinalIgnoreCase);
        Assert.Contains(
            "templateBehavior is",
            FoundryAgentContract.Instructions,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_PresentationPayloadIncludesChecklistAndRecommendationIntents()
    {
        var latest = CreateLatestSegment("Lifecycle workflows automate identity changes.");
        var lifecycleTask = new RecommendedTaskState(
            Guid.NewGuid(),
            "Illustrate onboarding and offboarding with a practical identity lifecycle story",
            "Connect the concept to a memorable access scenario.",
            0.88,
            [latest.Id],
            RecommendationStatus.Proposed,
            latest.OccurredAtUtc);
        var client = new RecordingFoundryClient(
            """{"checklistEvaluations":[],"recommendedTasks":[],"recommendationEvaluations":[],"contextualCards":[]}""");

        await new FoundryConversationCoachAgent(client).AnalyzeAsync(
            CreateContext(
                latest,
                [lifecycleTask],
                template: SessionTemplateKind.Presentation),
            latest,
            CancellationToken.None);

        using var payload = JsonDocument.Parse(Assert.IsType<string>(client.InputJson));
        var covered = payload.RootElement
            .GetProperty("coveredRecommendationIntents")
            .EnumerateArray()
            .Select(element => element.GetString())
            .ToArray();
        Assert.Equal(
            [
                RecommendationIntentPolicy.PresentationClosingRecapActions,
                RecommendationIntentPolicy.PresentationLifecycleExample,
                RecommendationIntentPolicy.PresentationOpeningOutcome
            ],
            covered);
        Assert.Contains(
            "Never propose an intent listed in coveredRecommendationIntents",
            FoundryAgentContract.Instructions,
            StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(
        "Frame the audience outcome",
        "Thank you for having me. What I want to talk about today is really so that.")]
    [InlineData(
        "Close with the intended action",
        "Our next action is to set the approver to an external sponsor.")]
    public async Task Analyze_PresentationChecklistClaimWithoutExplicitFraming_IsFiltered(
        string checklistTitle,
        string transcript)
    {
        var latest = CreateLatestSegment(transcript);
        var context = CreateContext(
            latest,
            template: SessionTemplateKind.Presentation);
        var item = context.Checklist.Single(candidate => candidate.Title == checklistTitle);
        var response = JsonSerializer.Serialize(
            new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        item.Id,
                        ShouldComplete: true,
                        Confidence: 0.96,
                        "The model claimed the item was complete.",
                        transcript,
                        latest.Id)
                ],
                [],
                []),
            JsonOptions);

        var decision = await new FoundryConversationCoachAgent(
                new RecordingFoundryClient(response))
            .AnalyzeAsync(context, latest, CancellationToken.None);

        Assert.Empty(decision.ChecklistEvaluations);
    }

    private static CoachAgentContext CreateContext(
        TranscriptSegment latestSegment,
        IReadOnlyList<RecommendedTaskState>? recommendations = null,
        IReadOnlyList<TranscriptSegment>? transcript = null,
        SessionTemplateKind template = SessionTemplateKind.CsaVbd)
    {
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            TestData.CreatePurpose(),
            requestedChecklist: null,
            template);
        return new CoachAgentContext(
            TestData.CreatePurpose(),
            checklist,
            transcript ?? [latestSegment],
            recommendations,
            Template: template);
    }

    private static TranscriptSegment CreateLatestSegment(
        string text = "We agreed the owner and deadline for Friday.") =>
        new(
            Guid.NewGuid(),
            "Customer",
            text,
            DateTimeOffset.UtcNow,
            IsFinal: true);

    private sealed class RecordingFoundryClient(string response)
        : IFoundryAgentClient
    {
        public string? InputJson { get; private set; }

        public Task<string> GetDecisionJsonAsync(
            string inputJson,
            CancellationToken cancellationToken)
        {
            InputJson = inputJson;
            return Task.FromResult(response);
        }
    }

    private sealed class SequenceFoundryClient(params string[] responses)
        : IFoundryAgentClient
    {
        public int CallCount { get; private set; }

        public Task<string> GetDecisionJsonAsync(
            string inputJson,
            CancellationToken cancellationToken)
        {
            var response = responses[Math.Min(CallCount, responses.Length - 1)];
            CallCount++;
            return Task.FromResult(response);
        }
    }

    private sealed class ThrowingFoundryClient(Exception exception)
        : IFoundryAgentClient
    {
        public Task<string> GetDecisionJsonAsync(
            string inputJson,
            CancellationToken cancellationToken) =>
            Task.FromException<string>(exception);
    }
}
