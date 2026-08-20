using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class AzureOpenAiConversationCoachAgentTests
{
    [Fact]
    public async Task Analyze_ParsesRecommendationEvaluationsInSingleRequest()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "CSA",
            "We covered staged rollout rings.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var recommendation = new RecommendedTaskState(
            Guid.NewGuid(),
            "Discuss staged rollout rings",
            "This helps the customer reduce deployment risk.",
            0.9,
            [Guid.NewGuid()],
            RecommendationStatus.Accepted,
            latest.OccurredAtUtc.AddMinutes(-2),
            latest.OccurredAtUtc.AddMinutes(-1));
        var decisionJsonOptions = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        decisionJsonOptions.Converters.Add(
            new JsonStringEnumConverter(
                JsonNamingPolicy.CamelCase,
                allowIntegerValues: false));
        var decisionJson = JsonSerializer.Serialize(
            new CoachAgentDecision(
                [],
                [],
                [
                    new RecommendationEvaluation(
                        recommendation.Id,
                        true,
                        0.9,
                        "The accepted topic was explicitly covered.",
                        "staged rollout rings")
                ])
            {
                ContextualCards =
                [
                    new ContextualCardProposal(
                        ContextualCardKind.Hint,
                        "Staged rollout rings",
                        "Use progressive exposure groups to limit deployment risk.",
                        0.9,
                        [latest.Id])
                ]
            },
            decisionJsonOptions);
        var responseJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new { message = new { content = decisionJson } }
            }
        });
        var handler = new RecordingHandler(responseJson);
        var agent = new AzureOpenAiConversationCoachAgent(
            new HttpClient(handler),
            new AzureOpenAiOptions(
                new Uri("https://example.openai.azure.com/"),
                "deployment",
                "2024-10-21",
                "key"));
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            [],
            [latest],
            [recommendation]);

        var decision = await agent.AnalyzeAsync(
            context,
            latest,
            CancellationToken.None);

        Assert.Single(decision.RecommendationEvaluations!);
        Assert.Single(decision.ContextualCards);
        Assert.Equal(1, handler.CallCount);
        Assert.Contains(
            "acceptedRecommendations",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "recommendationEvaluations",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "contextualCards",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "member-ready explanation",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "Never phrase a card as a",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "question",
            handler.RequestBody,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Analyze_PresentationRequestIncludesCoveredSemanticIntents()
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            "Lifecycle workflows automate identity changes.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var lifecycleTask = new RecommendedTaskState(
            Guid.NewGuid(),
            "Illustrate onboarding and offboarding with a practical identity lifecycle story",
            "Connect the concept to a memorable access scenario.",
            0.88,
            [latest.Id],
            RecommendationStatus.Proposed,
            latest.OccurredAtUtc);
        var responseJson = JsonSerializer.Serialize(new
        {
            choices = new[]
            {
                new
                {
                    message = new
                    {
                        content =
                            """{"checklistEvaluations":[],"recommendedTasks":[],"recommendationEvaluations":[],"contextualCards":[]}"""
                    }
                }
            }
        });
        var handler = new RecordingHandler(responseJson);
        var agent = new AzureOpenAiConversationCoachAgent(
            new HttpClient(handler),
            new AzureOpenAiOptions(
                new Uri("https://example.openai.azure.com/"),
                "deployment",
                "2024-10-21",
                "key"));
        var checklist = new MeetingChecklistPlanner().CreateChecklist(
            TestData.CreatePurpose(),
            requestedChecklist: null,
            SessionTemplateKind.Presentation);
        var context = new CoachAgentContext(
            TestData.CreatePurpose(),
            checklist,
            [latest],
            [lifecycleTask],
            Template: SessionTemplateKind.Presentation);

        var decision = await agent.AnalyzeAsync(
            context,
            latest,
            CancellationToken.None);

        Assert.Empty(decision.RecommendedTasks);
        Assert.Contains(
            "coveredRecommendationIntents",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            RecommendationIntentPolicy.PresentationOpeningOutcome,
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            RecommendationIntentPolicy.PresentationClosingRecapActions,
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            RecommendationIntentPolicy.PresentationLifecycleExample,
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "mid-presentation action or decision is not closing evidence",
            handler.RequestBody,
            StringComparison.Ordinal);
    }

    private sealed class RecordingHandler(string responseJson) : HttpMessageHandler
    {
        public int CallCount { get; private set; }

        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            CallCount++;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    responseJson,
                    Encoding.UTF8,
                    "application/json")
            };
        }
    }
}
