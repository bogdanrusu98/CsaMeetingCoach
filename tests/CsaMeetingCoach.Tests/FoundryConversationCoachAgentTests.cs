using System.Text.Json;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class FoundryConversationCoachAgentTests
{
    private static readonly JsonSerializerOptions JsonOptions =
        new(JsonSerializerDefaults.Web);

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
    public async Task Analyze_CompletionInventsEvidence_FailsClosed()
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.AnalyzeAsync(
                context,
                latestSegment,
                CancellationToken.None));
    }

    [Fact]
    public async Task Analyze_TaskDoesNotReferenceLatestSegment_FailsClosed()
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

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            agent.AnalyzeAsync(
                context,
                latestSegment,
                CancellationToken.None));
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
    public async Task Analyze_UnknownRecommendationEvaluation_IsPreservedForCoordinatorWarning()
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

        Assert.Single(decision.RecommendationEvaluations!);
    }

    private static CoachAgentContext CreateContext(
        TranscriptSegment latestSegment,
        IReadOnlyList<RecommendedTaskState>? recommendations = null)
    {
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            TestData.CreatePurpose(),
            requestedChecklist: null);
        return new CoachAgentContext(
            TestData.CreatePurpose(),
            checklist,
            [latestSegment],
            recommendations);
    }

    private static TranscriptSegment CreateLatestSegment() =>
        new(
            Guid.NewGuid(),
            "Customer",
            "We agreed the owner and deadline for Friday.",
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

    private sealed class ThrowingFoundryClient(Exception exception)
        : IFoundryAgentClient
    {
        public Task<string> GetDecisionJsonAsync(
            string inputJson,
            CancellationToken cancellationToken) =>
            Task.FromException<string>(exception);
    }
}
