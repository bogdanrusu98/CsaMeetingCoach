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

    private static MeetingSessionCoordinator CreateCoordinator(IConversationCoachAgent agent)
    {
        return new MeetingSessionCoordinator(
            new InMemoryMeetingSessionStore(),
            new MeetingChecklistPlanner(),
            agent,
            new NullSessionUpdatePublisher());
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
}
