using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static partial class PresentationCoachingPolicy
{
    private static readonly HashSet<string> ComparisonStopWords =
    [
        "a",
        "an",
        "and",
        "for",
        "in",
        "of",
        "on",
        "the",
        "to",
        "with"
    ];

    private static readonly string[] GenericPresentationTerms =
    [
        "accountable",
        "business outcome",
        "customer requirement",
        "follow up",
        "meeting objective",
        "next step",
        "owner",
        "production requirement",
        "success criteria"
    ];

    private static readonly string[] LoadBalancerTechnicalTerms =
    [
        "availability",
        "backend",
        "exposure",
        "floating ip",
        "frontend",
        "internal",
        "load balancer",
        "nat",
        "outbound",
        "probe",
        "public",
        "session",
        "sku",
        "tcp",
        "traffic",
        "udp",
        "zone"
    ];

    public static IReadOnlyList<RecommendedTaskProposal> SelectRecommendations(
        CoachAgentContext context,
        IReadOnlyList<RecommendedTaskProposal> primaryRecommendations,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        var analysisWindowIds = analysisWindow
            .Select(segment => segment.Id)
            .ToHashSet();
        var loadBalancer = FindMention(
            analysisWindow,
            "Azure Load Balancer",
            "load balancer");
        var coveredContext = context.Checklist
            .SelectMany(item => new[] { item.Title, item.CompletionCriteria })
            .Concat(context.Purpose.SuccessCriteria)
            .Concat((context.RecommendedTasks ?? []).Select(item => item.Title))
            .Select(HeuristicConversationCoachAgent.Normalize)
            .Where(value => value.Length > 0)
            .ToArray();
        var safePrimary = primaryRecommendations
            .Where(proposal => proposal is not null)
            .Where(proposal =>
            {
                var title = HeuristicConversationCoachAgent.Normalize(proposal.Title);
                return title.Length is > 0 and <= 180
                    && !string.IsNullOrWhiteSpace(proposal.Rationale)
                    && proposal.Rationale.Trim().Length <= 600
                    && double.IsFinite(proposal.Confidence)
                    && proposal.Confidence is >= 0.70 and <= 1
                    && proposal.SourceTranscriptSegmentIds is { Count: > 0 }
                    && proposal.SourceTranscriptSegmentIds.All(
                        analysisWindowIds.Contains)
                    && (loadBalancer is null
                        || !GenericPresentationTerms.Any(term =>
                                title.Contains(term, StringComparison.Ordinal))
                            && (title.Contains(
                                    "health probe",
                                    StringComparison.Ordinal)
                                || title.Contains(
                                    "load balancer",
                                    StringComparison.Ordinal)
                                || LoadBalancerTechnicalTerms.Count(term =>
                                    title.Contains(
                                        term,
                                        StringComparison.Ordinal)) >= 2))
                    && !coveredContext.Any(covered =>
                        HasSubstantialOverlap(title, covered));
            })
            .Take(1)
            .ToArray();
        if (safePrimary.Length > 0)
        {
            return safePrimary;
        }

        if (loadBalancer is null)
        {
            return [];
        }

        return
        [
            new RecommendedTaskProposal(
                "Validate Azure Load Balancer traffic, health, and availability choices",
                "Turn the component overview into customer-specific design decisions for exposure, traffic rules, probe behavior, zone resilience, and outbound connectivity.",
                0.91,
                loadBalancer.SourceTranscriptSegmentIds)
        ];
    }

    public static IReadOnlyList<ContextualCardProposal> SelectContextualCards(
        CoachAgentContext context,
        IReadOnlyList<ContextualCardProposal> primaryCards,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        var analysisWindowById = analysisWindow.ToDictionary(segment => segment.Id);
        var validPrimaryCards = primaryCards
            .Where(card => card is not null
                && Enum.IsDefined(card.Kind)
                && !string.IsNullOrWhiteSpace(card.Title)
                && card.Title.Trim().Length <= 80
                && !string.IsNullOrWhiteSpace(card.Content)
                && card.Content.Trim().Length <= 320
                && double.IsFinite(card.Confidence)
                && card.Confidence is >= 0.75 and <= 1
                && card.SourceTranscriptSegmentIds is { Count: > 0 }
                && card.SourceTranscriptSegmentIds.All(analysisWindowById.ContainsKey)
                && IsTitleGrounded(
                    card.Title,
                    card.SourceTranscriptSegmentIds,
                    analysisWindow))
            .ToArray();
        var knownTitles = (context.ContextualCards ?? [])
            .Select(card => HeuristicConversationCoachAgent.Normalize(card.Title))
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<ContextualCardProposal>();
        foreach (var card in validPrimaryCards)
        {
            var normalizedTitle = HeuristicConversationCoachAgent.Normalize(card.Title);
            if (knownTitles.Any(title => HasSubstantialOverlap(
                    normalizedTitle,
                    title)))
            {
                continue;
            }

            knownTitles.Add(normalizedTitle);
            result.Add(card);
            if (result.Count == 2)
            {
                break;
            }
        }

        AddCard(
            result,
            knownTitles,
            FindMention(analysisWindow, "Azure Load Balancer", "load balancer"),
            ContextualCardKind.Definition,
            "A Layer 4 Azure service that distributes TCP or UDP flows across healthy backend instances by using frontend IP configurations, load-balancing rules, and health probes.");
        AddCard(
            result,
            knownTitles,
            FindMention(analysisWindow, "health probes", "health probe"),
            ContextualCardKind.Hint,
            "Ask how the probe protocol, port, interval, and failure threshold represent real application health, because probe results control which backends receive new flows.");

        return result.Take(2).ToArray();
    }

    internal static bool IsTitleGrounded(
        string title,
        IReadOnlyList<Guid> sourceTranscriptSegmentIds,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        var sourceIds = sourceTranscriptSegmentIds.ToHashSet();
        var sources = analysisWindow
            .Select((segment, index) => (Segment: segment, Index: index))
            .Where(candidate => sourceIds.Contains(candidate.Segment.Id))
            .ToArray();
        if (sources.Any(source => ContainsWholeTerm(
                source.Segment.Text,
                title.Trim())))
        {
            return true;
        }

        return sources.Length is >= 2 and <= 3
            && sources.Select(source => source.Segment.Id).ToHashSet()
                .SetEquals(sourceIds)
            && sources
                .Zip(sources.Skip(1))
                .All(pair => pair.Second.Index == pair.First.Index + 1)
            && ContainsWholeTerm(
                string.Join(
                    ' ',
                    sources.Select(source => source.Segment.Text.Trim())),
                title.Trim());
    }

    internal static bool HasSubstantialOverlap(string left, string right)
    {
        if (left.Equals(right, StringComparison.Ordinal))
        {
            return true;
        }

        var leftTerms = SignificantTerms(left);
        var rightTerms = SignificantTerms(right);
        if (leftTerms.Count < 2 || rightTerms.Count < 2)
        {
            return false;
        }

        if (left.Contains(right, StringComparison.Ordinal)
            || right.Contains(left, StringComparison.Ordinal))
        {
            return true;
        }

        var shared = leftTerms.Count(rightTerms.Contains);
        return shared >= 2
            && shared / (double)Math.Min(leftTerms.Count, rightTerms.Count) >= 0.75;
    }

    private static HashSet<string> SignificantTerms(string value) =>
        WordRegex()
            .Matches(value)
            .Select(match => match.Value)
            .Where(term => term.Length >= 2 && !ComparisonStopWords.Contains(term))
            .ToHashSet(StringComparer.Ordinal);

    private static void AddCard(
        ICollection<ContextualCardProposal> cards,
        ISet<string> knownTitles,
        ContextualMention? mention,
        ContextualCardKind kind,
        string content)
    {
        if (cards.Count >= 2 || mention is null)
        {
            return;
        }

        var normalizedTitle = HeuristicConversationCoachAgent.Normalize(
            mention.Title);
        if (knownTitles.Any(title => HasSubstantialOverlap(
                normalizedTitle,
                title)))
        {
            return;
        }

        knownTitles.Add(normalizedTitle);
        cards.Add(new ContextualCardProposal(
            kind,
            mention.Title,
            content,
            0.93,
            mention.SourceTranscriptSegmentIds));
    }

    private static ContextualMention? FindMention(
        IReadOnlyList<TranscriptSegment> segments,
        params string[] terms)
    {
        foreach (var segment in segments.Reverse())
        {
            foreach (var term in terms)
            {
                var index = FindWholeTermIndex(segment.Text, term);
                if (index < 0
                    || IsNegatedOrOutOfScope(segment.Text, term))
                {
                    continue;
                }

                return new ContextualMention(
                    segment.Text.Substring(index, term.Length),
                    [segment.Id]);
            }
        }

        for (var start = segments.Count - 2; start >= 0; start--)
        {
            var adjacentSegments = segments
                .Skip(start)
                .Take(Math.Min(3, segments.Count - start))
                .ToArray();
            for (var count = 2; count <= adjacentSegments.Length; count++)
            {
                var combinedText = string.Join(
                    ' ',
                    adjacentSegments.Take(count).Select(segment => segment.Text.Trim()));
                var matchedTerm = terms.FirstOrDefault(term =>
                    ContainsWholeTerm(combinedText, term)
                    && !IsNegatedOrOutOfScope(combinedText, term));
                if (matchedTerm is not null)
                {
                    return new ContextualMention(
                        matchedTerm,
                        adjacentSegments
                            .Take(count)
                            .Select(segment => segment.Id)
                            .ToArray());
                }
            }
        }

        return null;
    }

    private static bool ContainsWholeTerm(string text, string term)
        => FindWholeTermIndex(text, term) >= 0;

    private static int FindWholeTermIndex(string text, string term)
    {
        var searchFrom = 0;
        while (searchFrom <= text.Length - term.Length)
        {
            var index = text.IndexOf(
                term,
                searchFrom,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                return -1;
            }

            var startsAtBoundary = index == 0
                || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + term.Length;
            var endsAtBoundary = end == text.Length
                || !char.IsLetterOrDigit(text[end]);
            if (startsAtBoundary && endsAtBoundary)
            {
                return index;
            }

            searchFrom = index + 1;
        }

        return -1;
    }

    private static bool IsNegatedOrOutOfScope(string text, string term)
    {
        var normalizedText = HeuristicConversationCoachAgent.Normalize(text);
        var normalizedTerm = HeuristicConversationCoachAgent.Normalize(term);
        var escapedTerm = Regex.Escape(normalizedTerm);
        return Regex.IsMatch(
                normalizedText,
                $@"\b(?:no|without)\s+(?:[\p{{L}}\p{{N}}]+\s+){{0,2}}{escapedTerm}\b",
                RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                normalizedText,
                $@"\b(?:not|do not|does not|did not|will not|won t)\s+(?:(?:use|using|discuss|discussing|consider|considering|include|including|select|selecting)\s+)?{escapedTerm}\b",
                RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                normalizedText,
                $@"\b{escapedTerm}\b(?:\s+[\p{{L}}\p{{N}}]+){{0,3}}\s+(?:is\s+)?(?:out of scope|not in scope|excluded)\b",
                RegexOptions.CultureInvariant);
    }

    private sealed record ContextualMention(
        string Title,
        IReadOnlyList<Guid> SourceTranscriptSegmentIds);

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
