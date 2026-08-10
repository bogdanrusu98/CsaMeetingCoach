using System.Collections.Concurrent;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class MeetingSessionCoordinatorTests
{
    [Fact]
    public async Task AddTranscript_ExplicitCommitment_CompletesChecklistAndRecommendsTask()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "Vom agrea următorul pas, responsabilul și deadline-ul până la vineri."),
            CancellationToken.None);

        var completed = Assert.Single(updated.Checklist.Where(
            item => item.Title.Contains("next steps", StringComparison.OrdinalIgnoreCase)));
        Assert.Equal(ChecklistItemStatus.Completed, completed.Status);
        Assert.True(completed.AutoCompleted);
        Assert.NotEmpty(completed.Evidence);
        Assert.Contains("până la vineri", completed.Evidence[0].Quote);

        var recommendation = Assert.Single(updated.RecommendedTasks);
        Assert.Equal(RecommendationStatus.Proposed, recommendation.Status);
        Assert.Contains(updated.Transcript[0].Id, recommendation.SourceTranscriptSegmentIds);
    }

    [Fact]
    public async Task AddTranscript_UnrelatedStatement_DoesNotCompleteOrRecommend()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "Thank you for joining today."),
            CancellationToken.None);

        Assert.All(
            updated.Checklist,
            item => Assert.Equal(ChecklistItemStatus.Pending, item.Status));
        Assert.Empty(updated.RecommendedTasks);
    }

    [Fact]
    public async Task CreateSession_WithTeamsMeetingId_StoresNormalizedBinding()
    {
        var coordinator = CreateCoordinator(new CountingAgent());

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                TeamsOnlineMeetingId: "  meeting-123  "),
            CancellationToken.None);

        Assert.Equal("meeting-123", session.TeamsOnlineMeetingId);
    }

    [Fact]
    public async Task AddTranscript_WithDuplicateSourceId_IsIdempotent()
    {
        var agent = new CountingAgent();
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var sourceSegmentId = Guid.NewGuid();
        var request = new AddTranscriptSegmentRequest(
            "CSA",
            "This is a final statement.",
            SourceSegmentId: sourceSegmentId);

        var first = await coordinator.AddTranscriptAsync(
            session.Id,
            request,
            CancellationToken.None);
        var duplicate = await coordinator.AddTranscriptAsync(
            session.Id,
            request,
            CancellationToken.None);

        Assert.Single(duplicate.Transcript);
        Assert.Equal(first.Revision, duplicate.Revision);
        Assert.Equal(sourceSegmentId, duplicate.Transcript[0].SourceSegmentId);
        Assert.NotNull(duplicate.Transcript[0].AnalyzedAtUtc);
        Assert.Equal(1, agent.CallCount);
    }

    [Fact]
    public async Task AddTranscript_InterimSegment_IsStoredButNotAnalyzed()
    {
        var agent = new CountingAgent();
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "This is an unstable interim segment.",
                IsFinal: false),
            CancellationToken.None);

        Assert.Single(updated.Transcript);
        Assert.False(updated.Transcript[0].IsFinal);
        Assert.Equal(0, agent.CallCount);
        Assert.Empty(updated.RecommendedTasks);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "This is the final customer statement.",
                IsFinal: true),
            CancellationToken.None);

        Assert.Equal(1, agent.CallCount);
        Assert.NotNull(agent.LastContext);
        Assert.Single(agent.LastContext.RecentTranscript);
        Assert.All(
            agent.LastContext.RecentTranscript,
            transcriptSegment => Assert.True(transcriptSegment.IsFinal));
    }

    [Fact]
    public async Task AddTranscript_AgentFailure_StillPersistsFinalTranscript()
    {
        var store = new InMemoryMeetingSessionStore();
        var agent = new FlakyAgent();
        var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            agent,
            null,
            new NullSessionUpdatePublisher());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var sourceSegmentId = Guid.NewGuid();
        var request = new AddTranscriptSegmentRequest(
            "Customer",
            "Final customer statement.",
            SourceSegmentId: sourceSegmentId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            coordinator.AddTranscriptAsync(
                session.Id,
                request,
                CancellationToken.None));

        var persisted = await store.GetAsync(session.Id, CancellationToken.None);
        Assert.NotNull(persisted);
        Assert.Single(persisted.Transcript);
        Assert.Equal("Final customer statement.", persisted.Transcript[0].Text);
        Assert.Null(persisted.Transcript[0].AnalyzedAtUtc);

        var retried = await coordinator.AddTranscriptAsync(
            session.Id,
            request,
            CancellationToken.None);

        Assert.Single(retried.Transcript);
        Assert.NotNull(retried.Transcript[0].AnalyzedAtUtc);
        Assert.Equal(2, agent.CallCount);
    }

    [Fact]
    public async Task AddTranscript_AgentInventsEvidence_RejectsCompletionWithWarning()
    {
        var coordinator = CreateCoordinator(new InventedEvidenceAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "We discussed the architecture."),
            CancellationToken.None);

        Assert.All(
            updated.Checklist,
            item => Assert.Equal(ChecklistItemStatus.Pending, item.Status));
        Assert.Contains(
            updated.Warnings,
            warning => warning.Contains("evidence was not present", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddTranscript_InvalidChecklistConfidence_RejectsCompletionWithWarning()
    {
        var coordinator = CreateCoordinator(new InvalidChecklistConfidenceAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "We discussed the architecture."),
            CancellationToken.None);

        Assert.All(
            updated.Checklist,
            item => Assert.Equal(ChecklistItemStatus.Pending, item.Status));
        Assert.Contains(
            updated.Warnings,
            warning => warning.Contains("evaluation data was invalid", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AddTranscript_HigherConfidenceInventedChecklistEvidence_UsesValidEvaluation()
    {
        var coordinator = CreateCoordinator(new DuplicateChecklistEvaluationAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "We discussed the architecture."),
            CancellationToken.None);

        var completed = Assert.Single(updated.Checklist.Where(
            item => item.Status == ChecklistItemStatus.Completed));
        Assert.Equal("We discussed the architecture.", Assert.Single(completed.Evidence).Quote);
        Assert.Contains(
            updated.Warnings,
            warning => warning.Contains("evidence was not present", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ReopenChecklistItem_UndoesAutomaticCompletion()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "Vom defini următorul pas și responsabilul până la vineri."),
            CancellationToken.None);
        var completed = Assert.Single(updated.Checklist.Where(
            item => item.Status == ChecklistItemStatus.Completed));

        var reopened = await coordinator.ReopenChecklistItemAsync(
            session.Id,
            completed.Id,
            CancellationToken.None);
        var item = Assert.Single(reopened.Checklist.Where(entry => entry.Id == completed.Id));

        Assert.Equal(ChecklistItemStatus.Pending, item.Status);
        Assert.False(item.AutoCompleted);
        Assert.Empty(item.Evidence);
        Assert.Equal(1, item.CompletionEligibleFromTranscriptIndex);
    }

    [Fact]
    public async Task ReopenChecklistItem_DoesNotReuseEarlierWindowEvidence()
    {
        var coordinator = CreateCoordinator(new FragmentedDiscussionAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                [
                    new ChecklistSeed(
                        "Explain the frontend",
                        "Explain the frontend IP address.",
                        ["frontend IP address"])
                ]),
            CancellationToken.None);
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "Azure Load Balancer uses a frontend IP address."),
            CancellationToken.None);
        var completed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "It can be internal or external."),
            CancellationToken.None);
        var completedItem = Assert.Single(completed.Checklist);
        Assert.Equal(ChecklistItemStatus.Completed, completedItem.Status);

        var reopened = await coordinator.ReopenChecklistItemAsync(
            session.Id,
            completedItem.Id,
            CancellationToken.None);
        Assert.Equal(
            2,
            Assert.Single(reopened.Checklist).CompletionEligibleFromTranscriptIndex);

        var unchanged = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "Next we will discuss health probes."),
            CancellationToken.None);

        Assert.Equal(
            ChecklistItemStatus.Pending,
            Assert.Single(unchanged.Checklist).Status);
    }

    [Fact]
    public async Task SetRecommendationStatus_AcceptsProposedTask()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "We will send the assessment tomorrow."),
            CancellationToken.None);
        var recommendation = Assert.Single(updated.RecommendedTasks);

        var accepted = await coordinator.SetRecommendationStatusAsync(
            session.Id,
            recommendation.Id,
            RecommendationStatus.Accepted,
            CancellationToken.None);

        Assert.Equal(
            RecommendationStatus.Accepted,
            Assert.Single(accepted.RecommendedTasks).Status);
        Assert.NotNull(Assert.Single(accepted.RecommendedTasks).AcceptedAtUtc);
        Assert.Null(Assert.Single(accepted.RecommendedTasks).CompletedAtUtc);
        Assert.Equal(
            1,
            Assert.Single(accepted.RecommendedTasks).CompletionEligibleFromTranscriptIndex);
    }

    [Fact]
    public async Task AcceptedRecommendation_LaterExactEvidence_AutoCompletesAndReopens()
    {
        var agent = new RecommendationLifecycleAgent
        {
            UseEarlierTranscriptEvidence = true
        };
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var proposed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "How can we reduce deployment risk?"),
            CancellationToken.None);
        var recommendation = Assert.Single(proposed.RecommendedTasks);
        var accepted = await coordinator.SetRecommendationStatusAsync(
            session.Id,
            recommendation.Id,
            RecommendationStatus.Accepted,
            CancellationToken.None);
        var acceptedTask = Assert.Single(accepted.RecommendedTasks);
        var evidenceTime = acceptedTask.AcceptedAtUtc!.Value.AddSeconds(1);

        var completed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We can reduce deployment risk with staged rollout rings.",
                evidenceTime),
            CancellationToken.None);

        var completedTask = Assert.Single(completed.RecommendedTasks);
        Assert.Equal(RecommendationStatus.Completed, completedTask.Status);
        Assert.NotNull(completedTask.CompletedAtUtc);
        Assert.Equal(
            "reduce deployment risk with staged rollout rings",
            Assert.Single(completedTask.Evidence!).Quote);
        Assert.Equal("CSA", completedTask.Evidence![0].Speaker);
        Assert.Equal(evidenceTime, completedTask.Evidence[0].OccurredAtUtc);
        Assert.Equal(0.93, completedTask.Evidence[0].Confidence);
        Assert.NotNull(completedTask.CompletionReason);

        var reopened = await coordinator.ReopenRecommendationAsync(
            session.Id,
            recommendation.Id,
            CancellationToken.None);
        var reopenedTask = Assert.Single(reopened.RecommendedTasks);
        Assert.Equal(RecommendationStatus.Accepted, reopenedTask.Status);
        Assert.NotNull(reopenedTask.AcceptedAtUtc);
        Assert.Null(reopenedTask.CompletedAtUtc);
        Assert.Empty(reopenedTask.Evidence!);
        Assert.Equal(2, reopenedTask.CompletionEligibleFromTranscriptIndex);

        var unchanged = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "Let's continue to the next topic."),
            CancellationToken.None);
        Assert.Equal(
            RecommendationStatus.Accepted,
            Assert.Single(unchanged.RecommendedTasks).Status);
    }

    [Fact]
    public async Task AcceptedRecommendation_InvalidNewProposal_DoesNotBlockCompletion()
    {
        var agent = new RecommendationLifecycleAgent
        {
            IncludeInvalidProposal = true,
            IncludeNullEvaluation = true
        };
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var proposed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "How can we reduce deployment risk?"),
            CancellationToken.None);
        var recommendation = Assert.Single(proposed.RecommendedTasks);
        var accepted = await coordinator.SetRecommendationStatusAsync(
            session.Id,
            recommendation.Id,
            RecommendationStatus.Accepted,
            CancellationToken.None);

        var completed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We can reduce deployment risk with staged rollout rings.",
                Assert.Single(accepted.RecommendedTasks).AcceptedAtUtc!.Value.AddSeconds(1)),
            CancellationToken.None);

        Assert.Equal(
            RecommendationStatus.Completed,
            Assert.Single(completed.RecommendedTasks).Status);
        Assert.Contains(
            completed.Warnings,
            warning => warning.Contains("transcript evidence was missing", StringComparison.Ordinal));
        Assert.Contains(
            completed.Warnings,
            warning => warning.Contains("empty recommendation evaluation", StringComparison.Ordinal));
    }

    [Fact]
    public async Task AcceptedRecommendation_HigherConfidenceInventedEvidence_UsesValidEvaluation()
    {
        var agent = new RecommendationLifecycleAgent
        {
            IncludeHigherConfidenceInvalidDuplicate = true
        };
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var proposed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "How can we reduce deployment risk?"),
            CancellationToken.None);
        var recommendation = Assert.Single(proposed.RecommendedTasks);
        var accepted = await coordinator.SetRecommendationStatusAsync(
            session.Id,
            recommendation.Id,
            RecommendationStatus.Accepted,
            CancellationToken.None);

        var completed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We can reduce deployment risk with staged rollout rings.",
                Assert.Single(accepted.RecommendedTasks).AcceptedAtUtc!.Value.AddSeconds(1)),
            CancellationToken.None);

        var completedTask = Assert.Single(completed.RecommendedTasks);
        Assert.Equal(RecommendationStatus.Completed, completedTask.Status);
        Assert.Equal(
            "reduce deployment risk with staged rollout rings",
            Assert.Single(completedTask.Evidence!).Quote);
        Assert.Contains(
            completed.Warnings,
            warning => warning.Contains("evidence was not present", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AcceptedRecommendation_BackdatedLaterEvidence_AutoCompletes()
    {
        var agent = new RecommendationLifecycleAgent();
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var proposed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "How can we reduce deployment risk?"),
            CancellationToken.None);
        var recommendation = Assert.Single(proposed.RecommendedTasks);
        var accepted = await coordinator.SetRecommendationStatusAsync(
            session.Id,
            recommendation.Id,
            RecommendationStatus.Accepted,
            CancellationToken.None);

        var unchanged = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We can reduce deployment risk with staged rollout rings.",
                Assert.Single(accepted.RecommendedTasks).AcceptedAtUtc),
            CancellationToken.None);

        Assert.Equal(
            RecommendationStatus.Completed,
            Assert.Single(unchanged.RecommendedTasks).Status);
        Assert.DoesNotContain(
            unchanged.Warnings,
            warning => warning.Contains("after acceptance", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AcceptedRecommendation_FutureDatedEarlierEvidence_DoesNotComplete()
    {
        var agent = new RecommendationLifecycleAgent
        {
            UseEarlierTranscriptEvidence = true
        };
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var proposed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "How can we reduce deployment risk?"),
            CancellationToken.None);
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We can reduce deployment risk with staged rollout rings.",
                DateTimeOffset.UtcNow.AddDays(1)),
            CancellationToken.None);
        await coordinator.SetRecommendationStatusAsync(
            session.Id,
            Assert.Single(proposed.RecommendedTasks).Id,
            RecommendationStatus.Accepted,
            CancellationToken.None);

        var unchanged = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "Let's move to the next topic."),
            CancellationToken.None);

        Assert.Equal(
            RecommendationStatus.Accepted,
            Assert.Single(unchanged.RecommendedTasks).Status);
    }

    [Fact]
    public async Task RecommendationCompletion_InventedOrUnknownEvidence_IsRejectedWithWarning()
    {
        var agent = new RecommendationLifecycleAgent
        {
            ReturnInventedEvidence = true,
            IncludeUnknownEvaluation = true
        };
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var proposed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "How can we reduce deployment risk?"),
            CancellationToken.None);
        var recommendation = Assert.Single(proposed.RecommendedTasks);
        var accepted = await coordinator.SetRecommendationStatusAsync(
            session.Id,
            recommendation.Id,
            RecommendationStatus.Accepted,
            CancellationToken.None);

        var unchanged = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We can reduce deployment risk with staged rollout rings.",
                Assert.Single(accepted.RecommendedTasks).AcceptedAtUtc!.Value.AddSeconds(1)),
            CancellationToken.None);

        Assert.Equal(
            RecommendationStatus.Accepted,
            Assert.Single(unchanged.RecommendedTasks).Status);
        Assert.Contains(
            unchanged.Warnings,
            warning => warning.Contains("not present", StringComparison.OrdinalIgnoreCase));
        Assert.Contains(
            unchanged.Warnings,
            warning => warning.Contains("unknown recommendation", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task DismissedRecommendation_NeverCompletes()
    {
        var agent = new RecommendationLifecycleAgent();
        var coordinator = CreateCoordinator(agent);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var proposed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "How can we reduce deployment risk?"),
            CancellationToken.None);
        var recommendation = Assert.Single(proposed.RecommendedTasks);
        var dismissed = await coordinator.SetRecommendationStatusAsync(
            session.Id,
            recommendation.Id,
            RecommendationStatus.Dismissed,
            CancellationToken.None);

        var unchanged = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We can reduce deployment risk with staged rollout rings.",
                DateTimeOffset.UtcNow.AddSeconds(1)),
            CancellationToken.None);

        Assert.Equal(
            RecommendationStatus.Dismissed,
            Assert.Single(unchanged.RecommendedTasks).Status);
        Assert.Null(Assert.Single(dismissed.RecommendedTasks).AcceptedAtUtc);
    }

    [Fact]
    public async Task SetRecommendationStatus_WithUndefinedValue_RejectsUpdate()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "We will send the assessment tomorrow."),
            CancellationToken.None);
        var recommendation = Assert.Single(updated.RecommendedTasks);

        await Assert.ThrowsAsync<ArgumentException>(() =>
            coordinator.SetRecommendationStatusAsync(
                session.Id,
                recommendation.Id,
                (RecommendationStatus)99,
                CancellationToken.None));
    }

    private static MeetingSessionCoordinator CreateCoordinator(
        IConversationCoachAgent deterministicAgent,
        IConversationCoachAgent? aiAgent = null,
        AnalysisOptions? analysisOptions = null)
    {
        return new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            deterministicAgent,
            aiAgent,
            new NullSessionUpdatePublisher(),
            analysisOptions: analysisOptions);
    }

    [Fact]
    public async Task AddTranscript_ReturnsImmediatelyWhileAiIsBlocked()
    {
        var blocking = new BlockingAgent();
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: blocking,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "We need to finalize the next steps by Friday."),
            CancellationToken.None);

        // Fast lane returned immediately; AI is still running in the background.
        Assert.True(updated.IsAnalyzing);
        blocking.Release();
    }

    [Fact]
    public async Task AddTranscript_LoadBalancerCardAppearsBeforeAiCompletes()
    {
        var blocking = new BlockingAgent();
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: blocking,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "Azure Load Balancer distributes incoming traffic."),
            CancellationToken.None);

        var card = Assert.Single(updated.ContextualCards);
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.Equal("Azure Load Balancer", card.Title);
        Assert.True(updated.IsAnalyzing);
        blocking.Release();
    }

    [Fact]
    public async Task AddTranscript_FoundationalCardAppearsBeforeAiCompletes()
    {
        var blocking = new BlockingAgent();
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: blocking,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "Microsoft Entra ID provides cloud identity and access management."),
            CancellationToken.None);

        var card = Assert.Single(updated.ContextualCards);
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.Equal("Microsoft Entra ID", card.Title);
        Assert.True(updated.IsAnalyzing);
        blocking.Release();
    }

    [Theory]
    [InlineData(
        "Azure Managed Redis is part of the Azure architecture.",
        "Azure Managed Redis")]
    [InlineData(
        "Microsoft Entra ID Governance is part of the Azure architecture.",
        "Microsoft Entra ID Governance")]
    public async Task AddTranscript_PrefersSpecificOverlappingEducationalCard(
        string transcript,
        string expectedTitle)
    {
        using var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var purpose = TestData.CreatePurpose() with
        {
            MeetingType = "Azure workshop",
            Objective = "Explain Azure services and design decisions."
        };
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(purpose),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Presenter", transcript),
            CancellationToken.None);

        var card = Assert.Single(updated.ContextualCards);
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.Equal(expectedTitle, card.Title);
    }

    [Fact]
    public async Task AddTranscript_DeterministicFastLane_CompletesChecklistWithoutAi()
    {
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: null);

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "Vom agrea următorul pas, responsabilul și deadline-ul până la vineri."),
            CancellationToken.None);

        var completed = Assert.Single(updated.Checklist.Where(
            item => item.Status == ChecklistItemStatus.Completed));
        Assert.True(completed.AutoCompleted);
        Assert.False(updated.IsAnalyzing);
    }

    [Fact]
    public async Task AddTranscript_CompoundChecklist_PersistsEverySupportingFragment()
    {
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: null);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                [
                    new ChecklistSeed(
                        "Explain the Azure Load Balancer backend",
                        "Explain the backend pool and the health probe.",
                        ["backend pool", "health probe"])
                ]),
            CancellationToken.None);

        var partial = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "For Azure Load Balancer, we have a backend pool."),
            CancellationToken.None);
        var completed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "The health probe checks whether an instance can receive traffic."),
            CancellationToken.None);

        Assert.Equal(
            ChecklistItemStatus.Pending,
            Assert.Single(partial.Checklist).Status);
        var item = Assert.Single(completed.Checklist);
        Assert.Equal(ChecklistItemStatus.Completed, item.Status);
        Assert.Equal(2, item.Evidence.Count);
        Assert.Contains(item.Evidence, evidence =>
            evidence.Quote == "For Azure Load Balancer, we have a backend pool.");
        Assert.Contains(item.Evidence, evidence =>
            evidence.Quote == "The health probe checks whether an instance can receive traffic.");
    }

    [Fact]
    public async Task AddTranscript_SimpleChecklist_PersistsOnlyBestSupportingFragment()
    {
        using var coordinator = CreateCoordinator(
            new MultipleEvidenceAgent(),
            aiAgent: null);
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                [
                    new ChecklistSeed(
                        "Explain the frontend",
                        "Explain the frontend IP address.",
                        ["frontend IP"])
                ]),
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "The first frontend IP explanation."),
            CancellationToken.None);
        var completed = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "The clearer frontend IP explanation."),
            CancellationToken.None);

        var item = Assert.Single(completed.Checklist);
        var evidence = Assert.Single(item.Evidence);
        Assert.Equal("The clearer frontend IP explanation.", evidence.Quote);
        Assert.Equal(0.95, evidence.Confidence);
    }

    [Fact]
    public async Task AddTranscript_AsyncAiLane_MergesDecisionAndClearsAnalyzing()
    {
        var store = new InMemoryMeetingSessionStore();
        var publisher = new CapturingSessionUpdatePublisher();
        var aiAgent = new ControlledAgent();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "We need to finalize the next steps."),
            CancellationToken.None);

        // Release the blocked AI call and wait for the async lane to finish.
        aiAgent.Release();
        var final = await publisher.WaitForAsync(
            s => !s.IsAnalyzing,
            TimeSpan.FromSeconds(10));

        Assert.False(final.IsAnalyzing);
        Assert.Equal(1, aiAgent.CallCount);
    }

    [Fact]
    public async Task AddTranscript_CoalescingBehavior_ProcessesOneSubsequentSnapshot()
    {
        var store = new InMemoryMeetingSessionStore();
        var publisher = new CapturingSessionUpdatePublisher();
        var aiAgent = new ControlledAgent();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        // First segment starts first AI call.
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "First statement."),
            CancellationToken.None);

        // Wait for AI call 1 to be in progress before adding rapid segments.
        await aiAgent.WaitForCallAsync();

        // Rapid segments while AI is blocked; channel capacity 1 coalesces them.
        for (var i = 0; i < 5; i++)
        {
            await coordinator.AddTranscriptAsync(
                session.Id,
                new AddTranscriptSegmentRequest("CSA", $"Rapid segment {i}."),
                CancellationToken.None);
        }

        // Release first AI call; coalesced trigger starts second AI call.
        aiAgent.Release();
        await aiAgent.WaitForCallAsync(); // wait for AI call 2 to be blocked

        // Release second AI call and wait for all processing to finish.
        aiAgent.Release();
        await publisher.WaitForAsync(
            _ => aiAgent.CallCount >= 2,
            TimeSpan.FromSeconds(10));

        // All 5 rapid segments coalesced into one trigger → exactly 2 AI calls total.
        Assert.Equal(2, aiAgent.CallCount);
    }

    [Fact]
    public async Task AddTranscript_NewerSpeechPreservesGroundedSnapshotAndKeepsAnalyzing()
    {
        var store = new InMemoryMeetingSessionStore();
        var publisher = new CapturingSessionUpdatePublisher();
        var aiAgent = new ControlledRecommendationAgent();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "The first topic is deployment architecture."),
            CancellationToken.None);
        await aiAgent.WaitForCallAsync();

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "The latest topic is regional topology."),
            CancellationToken.None);
        aiAgent.Release();
        await aiAgent.WaitForCallAsync();

        var whileLatestAnalysisRuns = await coordinator.GetAsync(
            session.Id,
            CancellationToken.None);
        Assert.NotNull(whileLatestAnalysisRuns);
        Assert.True(whileLatestAnalysisRuns.IsAnalyzing);
        Assert.Contains(
            whileLatestAnalysisRuns.RecommendedTasks,
            recommendation => recommendation.Title.Contains(
                "deployment architecture",
                StringComparison.Ordinal));
        aiAgent.Release();
        var final = await publisher.WaitForAsync(
            state => !state.IsAnalyzing && state.RecommendedTasks.Count == 2,
            TimeSpan.FromSeconds(10));

        Assert.Contains(
            final.RecommendedTasks,
            recommendation => recommendation.Title.Contains(
                "regional topology",
                StringComparison.Ordinal));
        Assert.Empty(final.ContextualCards);
    }

    [Fact]
    public async Task AddTranscript_ContinuousSpeechStartsAnalysisWithinMaximumBatchWindow()
    {
        var aiAgent = new ControlledAgent();
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: aiAgent,
            analysisOptions: new AnalysisOptions(
                TimeSpan.FromMilliseconds(250),
                TimeSpan.FromMinutes(5),
                TimeSpan.FromMilliseconds(600)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var producer = Task.Run(async () =>
        {
            for (var index = 0; index < 30; index++)
            {
                await coordinator.AddTranscriptAsync(
                    session.Id,
                    new AddTranscriptSegmentRequest(
                        "Tutorial",
                        $"Continuous Azure tutorial segment {index}."),
                    CancellationToken.None);
                await Task.Delay(100);
            }
        });

        await aiAgent.WaitForCallAsync().WaitAsync(TimeSpan.FromSeconds(2));

        Assert.False(producer.IsCompleted);
        aiAgent.Release();
        await producer;
    }

    [Fact]
    public async Task AddTranscript_AsyncAiLaneReceivesCompletePersistedMeetingContext()
    {
        var publisher = new CapturingSessionUpdatePublisher();
        var aiAgent = new ControlledRecommendationAgent();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "Context segment 0."),
            CancellationToken.None);
        await aiAgent.WaitForCallAsync();

        for (var index = 1; index < 25; index++)
        {
            await coordinator.AddTranscriptAsync(
                session.Id,
                new AddTranscriptSegmentRequest("Customer", $"Context segment {index}."),
                CancellationToken.None);
        }

        aiAgent.Release();
        await aiAgent.WaitForCallAsync();

        Assert.Equal(25, aiAgent.Contexts.Last().RecentTranscript.Count);

        aiAgent.Release();
        await publisher.WaitForAsync(
            state => !state.IsAnalyzing,
            TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task AddTranscript_WithAiConfigured_DoesNotPublishGenericHeuristicTask()
    {
        var blocking = new BlockingAgent();
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: blocking,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "We will send the assessment tomorrow."),
            CancellationToken.None);

        Assert.True(updated.IsAnalyzing);
        Assert.Empty(updated.RecommendedTasks);
        blocking.Release();
    }

    [Fact]
    public async Task AddTranscript_CrossCuttingPolicyRemainsSingleAfterAiMerge()
    {
        var publisher = new CapturingSessionUpdatePublisher();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            new CrossCuttingPolicyAgent(),
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var fastLane = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "We want to migrate our workloads to Azure next quarter."),
            CancellationToken.None);
        Assert.Single(fastLane.RecommendedTasks);

        var final = await publisher.WaitForAsync(
            state => !state.IsAnalyzing && state.RecommendedTasks.Count > 0,
            TimeSpan.FromSeconds(10));

        Assert.Single(final.RecommendedTasks);
    }

    [Fact]
    public async Task GetSession_OverlaysRuntimeAnalysisStateWhenStoreResetsPersistedFlag()
    {
        var blocking = new BlockingAgent();
        using var coordinator = new MeetingSessionCoordinator(
            new ResettingAnalysisStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            blocking,
            new NullSessionUpdatePublisher(),
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "Review the current architecture."),
            CancellationToken.None);

        var current = await coordinator.GetAsync(session.Id, CancellationToken.None);
        Assert.NotNull(current);
        Assert.True(current.IsAnalyzing);
        blocking.Release();
    }

    [Fact]
    public async Task AddTranscript_AiTimeout_AddsWarningAndPreservesTranscript()
    {
        var store = new InMemoryMeetingSessionStore();
        var publisher = new CapturingSessionUpdatePublisher();
        var neverCompletes = new BlockingAgent();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            neverCompletes,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMilliseconds(200)));

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "Something important."),
            CancellationToken.None);

        // Async lane should time out and add a warning, then clear IsAnalyzing.
        var final = await publisher.WaitForAsync(
            s => !s.IsAnalyzing,
            TimeSpan.FromSeconds(10));

        Assert.Single(final.Transcript);
        Assert.Contains(
            final.Warnings,
            w => w.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase));

        neverCompletes.Release();
    }

    [Fact]
    public async Task AddTranscript_AiFailure_AddsWarningDoesNotThrow()
    {
        var store = new InMemoryMeetingSessionStore();
        var publisher = new CapturingSessionUpdatePublisher();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            new ThrowingAiAgent(),
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        // AddTranscriptAsync must not throw; the AI failure is handled in the background lane.
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "Something important."),
            CancellationToken.None);

        var final = await publisher.WaitForAsync(
            s => !s.IsAnalyzing,
            TimeSpan.FromSeconds(10));

        Assert.Single(final.Transcript);
        Assert.Contains(
            final.Warnings,
            w => w.Contains("temporarily unavailable", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddTranscript_RepeatedAiFailures_DoNotAccumulateDuplicateWarnings()
    {
        var publisher = new CapturingSessionUpdatePublisher();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            new ThrowingAiAgent(),
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "First statement."),
            CancellationToken.None);
        await publisher.WaitForAsync(
            state => !state.IsAnalyzing && state.Transcript.Count == 1,
            TimeSpan.FromSeconds(10));

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "Second statement."),
            CancellationToken.None);
        var final = await publisher.WaitForAsync(
            state => !state.IsAnalyzing && state.Transcript.Count == 2,
            TimeSpan.FromSeconds(10));

        Assert.Single(final.Warnings);
        Assert.Contains(
            "temporarily unavailable",
            final.Warnings[0],
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task AddTranscript_ActiveAiFailureWarning_RemainsUntilSuccessfulRetry()
    {
        var publisher = new CapturingSessionUpdatePublisher();
        var aiAgent = new FailureThenControlledSuccessAgent();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "First statement."),
            CancellationToken.None);
        await publisher.WaitForAsync(
            state => !state.IsAnalyzing && state.Warnings.Count == 1,
            TimeSpan.FromSeconds(10));

        var retrying = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", "Second statement."),
            CancellationToken.None);

        Assert.True(retrying.IsAnalyzing);
        Assert.Single(retrying.Warnings);
        await aiAgent.WaitForRetryAsync();
        aiAgent.ReleaseRetry();
        var recovered = await publisher.WaitForAsync(
            state => !state.IsAnalyzing
                && state.Transcript.Count == 2
                && state.Warnings.Count == 0,
            TimeSpan.FromSeconds(10));

        Assert.Empty(recovered.Warnings);
    }

    [Fact]
    public async Task AddTranscript_SupersededAiFailure_DoesNotSurfaceWarning()
    {
        var publisher = new CapturingSessionUpdatePublisher();
        var aiAgent = new StaleFailureThenSuccessAgent();
        using var coordinator = new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromMinutes(5)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "First statement."),
            CancellationToken.None);
        await aiAgent.WaitForFirstCallAsync();

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "Latest statement."),
            CancellationToken.None);
        aiAgent.ReleaseFailure();
        var final = await publisher.WaitForAsync(
            state => !state.IsAnalyzing && state.Transcript.Count == 2,
            TimeSpan.FromSeconds(10));

        Assert.Equal(2, aiAgent.CallCount);
        Assert.Empty(final.Warnings);
    }

    [Fact]
    public async Task AddTranscript_RecommendationCanReferenceKnownEarlierSegment()
    {
        using var coordinator = CreateCoordinator(new EarlierContextRecommendationAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var first = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "We need to clarify the Project Atlas ownership model."),
            CancellationToken.None);

        var second = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "The next topic is the delivery timeline."),
            CancellationToken.None);

        var recommendation = Assert.Single(second.RecommendedTasks);
        Assert.Equal(first.Transcript[0].Id, Assert.Single(recommendation.SourceTranscriptSegmentIds));
        Assert.Empty(second.Warnings);
    }

    [Fact]
    public async Task AddTranscript_FragmentedDiscussionUsesExactEarlierEvidence()
    {
        using var coordinator = CreateCoordinator(new FragmentedDiscussionAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(
                TestData.CreatePurpose(),
                [
                    new ChecklistSeed(
                        "Explain the Azure Load Balancer frontend",
                        "Discuss the frontend IP configuration.",
                        ["frontend IP address"])
                ]),
            CancellationToken.None);
        var first = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "Azure Load Balancer has a frontend IP address."),
            CancellationToken.None);

        var second = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "It can be internal or external."),
            CancellationToken.None);

        var card = Assert.Single(second.ContextualCards);
        Assert.Equal("Azure Load Balancer", card.Title);
        Assert.Equal(first.Transcript[0].Id, Assert.Single(card.SourceTranscriptSegmentIds));
        var checklistItem = Assert.Single(second.Checklist);
        Assert.Equal(ChecklistItemStatus.Completed, checklistItem.Status);
        Assert.Equal(
            first.Transcript[0].Id,
            Assert.Single(checklistItem.Evidence).TranscriptSegmentId);
        Assert.Empty(second.Warnings);
    }

    [Fact]
    public async Task AddTranscript_CurrentCleanResult_RemovesResolvedDecisionWarnings()
    {
        using var coordinator = CreateCoordinator(new InvalidProposalOnceAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var first = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "First statement."),
            CancellationToken.None);
        Assert.NotEmpty(first.Warnings);

        var second = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("Customer", "Second statement."),
            CancellationToken.None);

        Assert.Empty(second.Warnings);
    }

    [Fact]
    public async Task AddTranscript_ContextualCardsPersistAndDeduplicateByTitle()
    {
        using var coordinator = CreateCoordinator(new ContextualCardAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var first = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "Our recovery objective requires an RTO of one hour."),
            CancellationToken.None);
        var firstCard = Assert.Single(first.ContextualCards);

        var second = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We should validate the RTO with the application owner."),
            CancellationToken.None);

        var persistedCard = Assert.Single(second.ContextualCards);
        Assert.Equal(firstCard.Id, persistedCard.Id);
        Assert.Equal(ContextualCardKind.Definition, persistedCard.Kind);
        Assert.Equal("RTO", persistedCard.Title);
        Assert.Empty(second.Warnings);
    }

    [Fact]
    public async Task AddTranscript_InvalidRetainedCard_IsReplacedByExplanation()
    {
        var store = new InMemoryMeetingSessionStore();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            new HeuristicConversationCoachAgent(),
            new NullSessionUpdatePublisher());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var invalidCard = new ContextualCardState(
            Guid.NewGuid(),
            ContextualCardKind.Hint,
            "health probe",
            "The CSA should confirm the client's health probe settings.",
            0.92,
            [],
            DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.SaveAsync(
            session with { ContextualCards = [invalidCard] },
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "The health probe checks each backend instance."),
            CancellationToken.None);

        var card = Assert.Single(updated.ContextualCards);
        Assert.NotEqual(invalidCard.Id, card.Id);
        Assert.Equal(ContextualCardKind.Definition, card.Kind);
        Assert.StartsWith("A health probe", card.Content);
    }

    [Fact]
    public async Task AddTranscript_InvalidRetainedCard_IsExcludedFromAiContext()
    {
        var store = new InMemoryMeetingSessionStore();
        var publisher = new CapturingSessionUpdatePublisher();
        var aiAgent = new CountingAgent();
        using var coordinator = new MeetingSessionCoordinator(
            store,
            new MeetingChecklistPlanner(),
            new HeuristicConversationCoachAgent(),
            aiAgent,
            publisher,
            analysisOptions: new AnalysisOptions(TimeSpan.Zero, TimeSpan.FromSeconds(10)));
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        var invalidCard = new ContextualCardState(
            Guid.NewGuid(),
            ContextualCardKind.Hint,
            "RTO",
            "Then ask the client to confirm the RTO.",
            0.92,
            [],
            DateTimeOffset.UtcNow.AddMinutes(-1));
        await store.SaveAsync(
            session with { ContextualCards = [invalidCard] },
            CancellationToken.None);

        await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Presenter",
                "RTO is the target time for restoring a service."),
            CancellationToken.None);
        await publisher.WaitForAsync(
            state => !state.IsAnalyzing && aiAgent.CallCount == 1,
            TimeSpan.FromSeconds(10));

        Assert.NotNull(aiAgent.LastContext);
        Assert.All(
            aiAgent.LastContext.ContextualCards ?? [],
            card => Assert.True(PresentationCoachingPolicy.IsClientReadyExplanation(
                card.Title,
                card.Content)));
    }

    [Fact]
    public async Task AddTranscript_ContextualCardHistoryIsBounded()
    {
        using var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);
        MeetingSessionState current = session;
        var transcripts = new[]
        {
            "Cloud computing provides on-demand resources.",
            "The shared responsibility model changes operational ownership.",
            "Infrastructure as a Service provides virtualized infrastructure.",
            "Platform as a Service provides managed runtimes.",
            "Software as a Service provides a complete hosted application.",
            "Azure regions contain connected datacenters.",
            "Azure Availability Zones isolate datacenter failures.",
            "Azure Resource Manager handles management requests.",
            "Management groups organize subscriptions in Azure.",
            "An Azure subscription is a management boundary.",
            "An Azure resource group holds related resources.",
            "Microsoft Entra ID handles cloud identities.",
            "Azure RBAC controls resource authorization.",
            "Azure Policy evaluates resources against governance rules."
        };
        foreach (var transcript in transcripts)
        {
            current = await coordinator.AddTranscriptAsync(
                session.Id,
                new AddTranscriptSegmentRequest("Customer", transcript),
                CancellationToken.None);
        }

        Assert.Equal(12, current.ContextualCards.Count);
        Assert.DoesNotContain(current.ContextualCards, card => card.Title == "Cloud computing");
        Assert.Contains(current.ContextualCards, card => card.Title == "Azure RBAC");
    }

    [Fact]
    public async Task AddTranscript_CrossCuttingPolicy_AppliedInFastLane()
    {
        using var coordinator = CreateCoordinator(
            new HeuristicConversationCoachAgent(),
            aiAgent: null);

        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        // "migrate" triggers CrossCuttingRecommendationPolicy even without an AI agent.
        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "Customer",
                "We want to migrate our workloads to Azure next quarter."),
            CancellationToken.None);

        Assert.NotEmpty(updated.RecommendedTasks);
        Assert.All(
            updated.RecommendedTasks,
            task => Assert.Equal(RecommendationStatus.Proposed, task.Status));
    }

    [Fact]
    public async Task AddTranscript_SpeechSegmentWithEcosystemAnchor_CorrectsAsiaToAzure()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We deployed to Asia Key Vault.",
                IsSpeechRecognized: true),
            CancellationToken.None);

        var segment = Assert.Single(updated.Transcript);
        Assert.Equal("We deployed to Azure Key Vault.", segment.Text);
        Assert.Equal("We deployed to Asia Key Vault.", segment.RecognizedText);
        Assert.NotNull(segment.CorrectionReason);
        Assert.Contains("AsiaToAzure", segment.CorrectionReason, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddTranscript_NonSpeechSegmentWithAsia_DoesNotCorrect()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        const string text = "We deployed to Asia Key Vault.";
        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest("CSA", text, IsSpeechRecognized: false),
            CancellationToken.None);

        var segment = Assert.Single(updated.Transcript);
        Assert.Equal(text, segment.Text);
        Assert.Null(segment.RecognizedText);
        Assert.Null(segment.CorrectionReason);
    }

    [Fact]
    public async Task AddTranscript_CorrectedSpeechSegment_TriggersEducationalCardForAzureConcept()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var purpose = TestData.CreatePurpose() with
        {
            MeetingType = "Azure workshop",
            Objective = "Explain Azure services."
        };
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(purpose),
            CancellationToken.None);

        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                "We store secrets in Asia Key Vault.",
                IsSpeechRecognized: true),
            CancellationToken.None);

        var segment = Assert.Single(updated.Transcript);
        Assert.Equal("We store secrets in Azure Key Vault.", segment.Text);
        Assert.NotNull(segment.RecognizedText);
        Assert.Contains(
            updated.ContextualCards,
            card => card.ConceptKey != null
                && card.ConceptKey.Contains("key-vault", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task AddTranscript_InterimSpeechSegment_NeverCorrected()
    {
        var coordinator = CreateCoordinator(new HeuristicConversationCoachAgent());
        var session = await coordinator.CreateAsync(
            new CreateMeetingSessionRequest(TestData.CreatePurpose()),
            CancellationToken.None);

        const string text = "Asia Key Vault";
        var updated = await coordinator.AddTranscriptAsync(
            session.Id,
            new AddTranscriptSegmentRequest(
                "CSA",
                text,
                IsFinal: false,
                IsSpeechRecognized: true),
            CancellationToken.None);

        var segment = Assert.Single(updated.Transcript);
        Assert.False(segment.IsFinal);
        Assert.Equal(text, segment.Text);
        Assert.Null(segment.RecognizedText);
    }

    private sealed class BlockingAgent : IConversationCoachAgent
    {
        private readonly TaskCompletionSource _tcs = new();

        public void Release() => _tcs.TrySetResult();

        public async Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            await _tcs.Task.WaitAsync(cancellationToken);
            return new CoachAgentDecision([], []);
        }
    }

    private sealed class ControlledAgent : IConversationCoachAgent
    {
        private readonly SemaphoreSlim _permit = new(0);
        private readonly SemaphoreSlim _waiting = new(0);
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public void Release(int count = 1) => _permit.Release(count);

        /// <summary>Awaits until the next <see cref="AnalyzeAsync"/> call has started and is blocked.</summary>
        public Task WaitForCallAsync() => _waiting.WaitAsync();

        public async Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref _callCount);
            _waiting.Release();
            await _permit.WaitAsync(cancellationToken);
            return new CoachAgentDecision([], []);
        }
    }

    private sealed class ControlledRecommendationAgent : IConversationCoachAgent
    {
        private readonly SemaphoreSlim _permit = new(0);
        private readonly SemaphoreSlim _waiting = new(0);
        private readonly ConcurrentQueue<CoachAgentContext> _contexts = new();

        public IReadOnlyList<CoachAgentContext> Contexts => _contexts.ToArray();

        public void Release() => _permit.Release();

        public Task WaitForCallAsync() => _waiting.WaitAsync();

        public async Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            _contexts.Enqueue(context);
            _waiting.Release();
            await _permit.WaitAsync(cancellationToken);
            return new CoachAgentDecision(
                [],
                [
                    new RecommendedTaskProposal(
                        $"Discuss {latestSegment.Text}",
                        "The latest customer statement requires a grounded follow-up.",
                        0.9,
                        [latestSegment.Id])
                ])
            {
                ContextualCards =
                [
                    new ContextualCardProposal(
                        ContextualCardKind.Hint,
                        latestSegment.Text,
                        "This grounded topic identifies the current customer discussion.",
                        0.9,
                        [latestSegment.Id])
                ]
            };
        }
    }

    private sealed class CrossCuttingPolicyAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(CrossCuttingRecommendationPolicy.Apply(
                new CoachAgentDecision([], []),
                latestSegment));
        }
    }

    private sealed class ResettingAnalysisStore : IMeetingSessionStore
    {
        private readonly InMemoryMeetingSessionStore _inner = new();

        public async Task<MeetingSessionState?> GetAsync(
            Guid sessionId,
            CancellationToken cancellationToken)
        {
            var session = await _inner.GetAsync(sessionId, cancellationToken);
            return session is null ? null : session with { IsAnalyzing = false };
        }

        public Task SaveAsync(
            MeetingSessionState session,
            CancellationToken cancellationToken)
        {
            return _inner.SaveAsync(session, cancellationToken);
        }
    }

    private sealed class ThrowingAiAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken) =>
            throw new InvalidOperationException("AI service unavailable.");
    }

    private sealed class StaleFailureThenSuccessAgent : IConversationCoachAgent
    {
        private readonly TaskCompletionSource _firstCallStarted = new();
        private readonly TaskCompletionSource _releaseFailure = new();
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);

        public Task WaitForFirstCallAsync() => _firstCallStarted.Task;

        public void ReleaseFailure() => _releaseFailure.TrySetResult();

        public async Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                _firstCallStarted.TrySetResult();
                await _releaseFailure.Task.WaitAsync(cancellationToken);
                throw new InvalidOperationException("Superseded provider failure.");
            }

            return new CoachAgentDecision([], []);
        }
    }

    private sealed class FailureThenControlledSuccessAgent : IConversationCoachAgent
    {
        private readonly TaskCompletionSource _retryStarted = new();
        private readonly TaskCompletionSource _releaseRetry = new();
        private int _callCount;

        public Task WaitForRetryAsync() => _retryStarted.Task;

        public void ReleaseRetry() => _releaseRetry.TrySetResult();

        public async Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) == 1)
            {
                throw new InvalidOperationException("Initial provider failure.");
            }

            _retryStarted.TrySetResult();
            await _releaseRetry.Task.WaitAsync(cancellationToken);
            return new CoachAgentDecision([], []);
        }
    }

    private sealed class EarlierContextRecommendationAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            if (context.RecentTranscript.Count < 2)
            {
                return Task.FromResult(new CoachAgentDecision([], []));
            }

            var source = context.RecentTranscript[0];
            return Task.FromResult(new CoachAgentDecision(
                [],
                [
                    new RecommendedTaskProposal(
                        "Clarify the Project Atlas ownership model",
                        "The earlier customer statement introduced a concrete need.",
                        0.9,
                        [source.Id])
                ]));
        }
    }

    private sealed class InvalidProposalOnceAgent : IConversationCoachAgent
    {
        private int _callCount;

        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref _callCount) != 1)
            {
                return Task.FromResult(new CoachAgentDecision([], []));
            }

            return Task.FromResult(new CoachAgentDecision(
                [],
                [
                    new RecommendedTaskProposal(
                        "Unsupported task",
                        "This task has no real transcript source.",
                        0.9,
                        [Guid.NewGuid()])
                ]));
        }
    }

    private sealed class FragmentedDiscussionAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            if (context.RecentTranscript.Count < 2)
            {
                return Task.FromResult(new CoachAgentDecision([], []));
            }

            var source = context.RecentTranscript[0];
            var checklistItem = context.Checklist[0];
            return Task.FromResult(new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        checklistItem.Id,
                        ShouldComplete: true,
                        Confidence: 0.94,
                        "The frontend IP was explicitly discussed.",
                        "frontend IP address",
                        source.Id)
                ],
                [])
            {
                ContextualCards =
                [
                    new ContextualCardProposal(
                        ContextualCardKind.Definition,
                        "Azure Load Balancer",
                        "Azure Load Balancer distributes layer four traffic across healthy backend resources.",
                        0.92,
                        [source.Id])
                ]
            });
        }
    }

    private sealed class MultipleEvidenceAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            if (context.RecentTranscript.Count < 2)
            {
                return Task.FromResult(new CoachAgentDecision([], []));
            }

            var item = context.Checklist[0];
            var first = context.RecentTranscript[0];
            return Task.FromResult(new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        item.Id,
                        ShouldComplete: true,
                        Confidence: 0.90,
                        "The simple criterion was discussed.",
                        first.Text,
                        first.Id),
                    new ChecklistEvaluation(
                        item.Id,
                        ShouldComplete: true,
                        Confidence: 0.95,
                        "The simple criterion was discussed more clearly.",
                        latestSegment.Text,
                        latestSegment.Id)
                ],
                []));
        }
    }

    private sealed class ContextualCardAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CoachAgentDecision([], [])
            {
                ContextualCards =
                [
                    new ContextualCardProposal(
                        ContextualCardKind.Definition,
                        "RTO",
                        "Recovery Time Objective is the maximum target time for restoring a service.",
                        0.9,
                        [latestSegment.Id])
                ]
            });
        }
    }

    private sealed class UniqueContextualCardAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CoachAgentDecision([], [])
            {
                ContextualCards =
                [
                    new ContextualCardProposal(
                        ContextualCardKind.Hint,
                        latestSegment.Text,
                        $"{latestSegment.Text} is relevant to the current client discussion.",
                        0.9,
                        [latestSegment.Id])
                ]
            });
        }
    }

    private sealed class CapturingSessionUpdatePublisher : ISessionUpdatePublisher
    {
        private readonly SemaphoreSlim _signal = new(0);
        private volatile MeetingSessionState? _latest;

        public Task PublishAsync(MeetingSessionState session, CancellationToken cancellationToken)
        {
            _latest = session;
            _signal.Release();
            return Task.CompletedTask;
        }

        public async Task<MeetingSessionState> WaitForAsync(
            Func<MeetingSessionState, bool> predicate,
            TimeSpan timeout)
        {
            using var cts = new CancellationTokenSource(timeout);
            try
            {
                while (true)
                {
                    if (_latest is { } current && predicate(current))
                    {
                        return current;
                    }

                    await _signal.WaitAsync(cts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                throw new TimeoutException(
                    $"Timed out after {timeout.TotalSeconds:F1}s waiting for session condition.");
            }
        }
    }

    private sealed class InventedEvidenceAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            var item = context.Checklist[0];
            return Task.FromResult(new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        item.Id,
                        ShouldComplete: true,
                        Confidence: 0.99,
                        "Invented evidence.",
                        "The customer approved the proposal.")
                ],
                []));
        }
    }

    private sealed class InvalidChecklistConfidenceAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        context.Checklist[0].Id,
                        ShouldComplete: true,
                        Confidence: 1.1,
                        "The latest segment contains direct evidence.",
                        latestSegment.Text)
                ],
                []));
        }
    }

    private sealed class DuplicateChecklistEvaluationAgent : IConversationCoachAgent
    {
        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            var itemId = context.Checklist[0].Id;
            return Task.FromResult(new CoachAgentDecision(
                [
                    new ChecklistEvaluation(
                        itemId,
                        ShouldComplete: true,
                        Confidence: 0.99,
                        "Invented evidence.",
                        "This quote does not exist."),
                    new ChecklistEvaluation(
                        itemId,
                        ShouldComplete: true,
                        Confidence: 0.93,
                        "The latest segment contains direct evidence.",
                        latestSegment.Text)
                ],
                []));
        }
    }

    private sealed class CountingAgent : IConversationCoachAgent
    {
        public int CallCount { get; private set; }

        public CoachAgentContext? LastContext { get; private set; }

        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            CallCount++;
            LastContext = context;
            return Task.FromResult(new CoachAgentDecision([], []));
        }
    }

    private sealed class FlakyAgent : IConversationCoachAgent
    {
        public int CallCount { get; private set; }

        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            CallCount++;
            if (CallCount == 1)
            {
                throw new InvalidOperationException("Provider response was invalid.");
            }

            return Task.FromResult(new CoachAgentDecision([], []));
        }
    }

    private sealed class RecommendationLifecycleAgent : IConversationCoachAgent
    {
        public bool ReturnInventedEvidence { get; init; }

        public bool IncludeUnknownEvaluation { get; init; }

        public bool IncludeInvalidProposal { get; init; }

        public bool IncludeNullEvaluation { get; init; }

        public bool IncludeHigherConfidenceInvalidDuplicate { get; init; }

        public bool UseEarlierTranscriptEvidence { get; init; }

        public Task<CoachAgentDecision> AnalyzeAsync(
            CoachAgentContext context,
            TranscriptSegment latestSegment,
            CancellationToken cancellationToken)
        {
            var recommendation = (context.RecommendedTasks ?? [])
                .FirstOrDefault(item => item.Status is
                    RecommendationStatus.Accepted or RecommendationStatus.Dismissed);
            if (recommendation is null)
            {
                return Task.FromResult(new CoachAgentDecision(
                    [],
                    [
                        new RecommendedTaskProposal(
                            "Ask about deployment risk reduction",
                            "This helps the customer choose a safer rollout approach.",
                            0.9,
                            [latestSegment.Id])
                    ],
                    []));
            }

            var evidenceSegment = UseEarlierTranscriptEvidence
                ? context.RecentTranscript.First(segment => segment.Text.Contains(
                    "reduce deployment risk with staged rollout rings",
                    StringComparison.Ordinal))
                : latestSegment;
            var evaluations = new List<RecommendationEvaluation>
            {
                new(
                    recommendation.Id,
                    true,
                    0.93,
                    "The rollout approach was explicitly discussed.",
                    ReturnInventedEvidence
                        ? "The customer approved an impossible quote."
                        : "reduce deployment risk with staged rollout rings",
                    evidenceSegment.Id)
            };
            if (IncludeUnknownEvaluation)
            {
                evaluations.Add(new RecommendationEvaluation(
                    Guid.NewGuid(),
                    true,
                    0.99,
                    "Unknown.",
                    latestSegment.Text));
            }
            if (IncludeNullEvaluation)
            {
                evaluations.Add(null!);
            }
            if (IncludeHigherConfidenceInvalidDuplicate)
            {
                evaluations.Add(new RecommendationEvaluation(
                    recommendation.Id,
                    true,
                    0.99,
                    "Invented.",
                    "This quote does not exist."));
            }

            IReadOnlyList<RecommendedTaskProposal> proposals = IncludeInvalidProposal
                ? [
                    new RecommendedTaskProposal(
                        "Invalid unsupported proposal",
                        "This proposal references evidence outside the transcript.",
                        0.9,
                        [Guid.NewGuid()])
                ]
                : [];
            return Task.FromResult(new CoachAgentDecision([], proposals, evaluations));
        }
    }
}
