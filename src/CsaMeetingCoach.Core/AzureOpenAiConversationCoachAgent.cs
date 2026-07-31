using System.Net.Http.Json;
using System.Text.Json;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed record AzureOpenAiOptions(
    Uri Endpoint,
    string Deployment,
    string ApiVersion,
    string ApiKey);

public sealed class AzureOpenAiConversationCoachAgent(
    HttpClient httpClient,
    AzureOpenAiOptions options) : IConversationCoachAgent
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task<CoachAgentDecision> AnalyzeAsync(
        CoachAgentContext context,
        TranscriptSegment latestSegment,
        CancellationToken cancellationToken)
    {
        var requestUri = new Uri(
            options.Endpoint,
            $"openai/deployments/{Uri.EscapeDataString(options.Deployment)}/chat/completions?api-version={Uri.EscapeDataString(options.ApiVersion)}");

        using var request = new HttpRequestMessage(HttpMethod.Post, requestUri);
        request.Headers.Add("api-key", options.ApiKey);
        request.Content = JsonContent.Create(new
        {
            temperature = 0.1,
            response_format = new { type = "json_object" },
            messages = new object[]
            {
                new
                {
                    role = "system",
                    content = """
                        You are a private CSA meeting coach. Evaluate only explicit evidence in the
                        transcript. Never infer emotion, sentiment, employee performance, health,
                        ethnicity, or other sensitive attributes. A checklist item may be completed
                        only when the latest segment contains direct evidence. The evidenceQuote must
                        be an exact substring of the latest segment. Recommend tasks only for explicit
                        commitments, requested follow-ups, unanswered questions, or presentation
                        improvements. Return JSON only with this shape:
                        {
                          "checklistEvaluations": [{
                            "checklistItemId": "guid",
                            "shouldComplete": true,
                            "confidence": 0.0,
                            "reason": "string",
                            "evidenceQuote": "exact quote"
                          }],
                          "recommendedTasks": [{
                            "title": "string",
                            "rationale": "string",
                            "confidence": 0.0,
                            "sourceTranscriptSegmentIds": ["guid"]
                          }]
                        }
                        """
                },
                new
                {
                    role = "user",
                    content = JsonSerializer.Serialize(new
                    {
                        meetingPurpose = context.Purpose,
                        pendingChecklist = context.Checklist
                            .Where(item => item.Status == ChecklistItemStatus.Pending),
                        recentTranscript = context.RecentTranscript,
                        latestSegment
                    }, JsonOptions)
                }
            }
        }, options: JsonOptions);

        using var response = await httpClient.SendAsync(request, cancellationToken);
        var responseBody = await response.Content.ReadAsStringAsync(cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException(
                $"Azure OpenAI returned {(int)response.StatusCode}: {responseBody}",
                null,
                response.StatusCode);
        }

        using var document = JsonDocument.Parse(responseBody);
        var content = document.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new InvalidOperationException("Azure OpenAI returned an empty coaching decision.");
        }

        var decision = JsonSerializer.Deserialize<CoachAgentDecision>(content, JsonOptions)
            ?? throw new InvalidOperationException("Azure OpenAI returned an invalid coaching decision.");

        if (decision.ChecklistEvaluations is null || decision.RecommendedTasks is null)
        {
            throw new InvalidOperationException(
                "Azure OpenAI returned a coaching decision with missing collections.");
        }

        if (decision.ChecklistEvaluations.Any(item =>
                item is null
                || string.IsNullOrWhiteSpace(item.Reason)
                || string.IsNullOrWhiteSpace(item.EvidenceQuote))
            || decision.RecommendedTasks.Any(item =>
                item is null
                || string.IsNullOrWhiteSpace(item.Title)
                || string.IsNullOrWhiteSpace(item.Rationale)
                || item.SourceTranscriptSegmentIds is null))
        {
            throw new InvalidOperationException(
                "Azure OpenAI returned an incomplete coaching decision.");
        }

        return decision;
    }
}
