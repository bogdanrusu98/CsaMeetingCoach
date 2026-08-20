using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static partial class PresentationChecklistEvidencePolicy
{
    private static readonly string[] ClosingCues =
    [
        "in conclusion",
        "to conclude",
        "in closing",
        "to wrap up",
        "let me summarize",
        "let s summarize",
        "let us summarize",
        "let me recap",
        "let s recap",
        "let us recap",
        "as a recap",
        "in summary",
        "to summarize",
        "to recap",
        "the key takeaways are",
        "the final takeaway is",
        "our final takeaway is",
        "in concluzie",
        "ca o concluzie",
        "pentru a incheia",
        "sa recapitulam",
        "pe scurt"
    ];

    private static readonly string[] ClosingLeadIns =
    [
        "and",
        "so",
        "finally",
        "well",
        "now",
        "si",
        "iar",
        "deci"
    ];

    private static readonly (string Contracted, string Expanded)[] NegativeContractions =
    [
        ("aren t", "are not"),
        ("isn t", "is not"),
        ("wasn t", "was not"),
        ("weren t", "were not"),
        ("hasn t", "has not"),
        ("haven t", "have not"),
        ("hadn t", "had not"),
        ("don t", "do not"),
        ("doesn t", "does not"),
        ("didn t", "did not"),
        ("won t", "will not"),
        ("can t", "cannot"),
        ("couldn t", "could not"),
        ("shouldn t", "should not"),
        ("wouldn t", "would not"),
        ("mustn t", "must not")
    ];

    private static readonly string[] ClosingContentMarkers =
    [
        "takeaway",
        "takeaways",
        "key message",
        "key messages",
        "main point",
        "main points",
        "next action",
        "next actions",
        "next step",
        "next steps",
        "decision is",
        "decision was",
        "decisions are",
        "decisions were",
        "action is",
        "actions are",
        "remember",
        "pilot",
        "mesajul cheie",
        "mesajele cheie",
        "ideea principala",
        "ideile principale",
        "urmatoarea actiune",
        "actiunile urmatoare",
        "urmatorul pas",
        "pasii urmatori",
        "decizia este",
        "deciziile sunt",
        "retineti"
    ];

    public static bool AllowsCompletion(
        SessionTemplateKind template,
        ChecklistItemState item,
        TranscriptSegment segment)
    {
        if (template != SessionTemplateKind.Presentation)
        {
            return true;
        }

        var intent = RecommendationIntentPolicy.Resolve(
            template,
            item.Title,
            item.CompletionCriteria);
        return intent switch
        {
            RecommendationIntentPolicy.PresentationOpeningOutcome =>
                HasExplicitAudienceOutcome(segment.Text),
            RecommendationIntentPolicy.PresentationClosingRecapActions =>
                HasExplicitClosingRecap(segment.Text),
            _ => true
        };
    }

    public static string? GetExplicitSignal(
        SessionTemplateKind template,
        ChecklistItemState item,
        TranscriptSegment segment)
    {
        if (template != SessionTemplateKind.Presentation)
        {
            return null;
        }

        var intent = RecommendationIntentPolicy.Resolve(
            template,
            item.Title,
            item.CompletionCriteria);
        return intent switch
        {
            RecommendationIntentPolicy.PresentationOpeningOutcome
                when HasExplicitAudienceOutcome(segment.Text) =>
                "explicit audience outcome",
            RecommendationIntentPolicy.PresentationClosingRecapActions
                when HasExplicitClosingRecap(segment.Text) =>
                "explicit closing recap",
            _ => null
        };
    }

    internal static bool HasExplicitAudienceOutcome(string text)
    {
        if (text.Contains('?'))
        {
            return false;
        }

        var normalized = NormalizeEvidenceText(text);
        var match = EnglishAudienceOutcomeRegex().Match(normalized);
        if (!match.Success)
        {
            match = RomanianAudienceOutcomeRegex().Match(normalized);
        }

        var detail = match.Groups["detail"].Value;
        return match.Success &&
               !HasUndefinedOrNegatedOutcome(detail) &&
               CountMeaningfulWords(detail) >= 2;
    }

    internal static bool HasExplicitClosingRecap(string text)
    {
        foreach (var clause in SentenceBoundaryRegex().Split(text))
        {
            var normalized = TrimClosingLeadIns(
                NormalizeEvidenceText(clause));
            foreach (var cue in ClosingCues)
            {
                if (!normalized.StartsWith(
                        $"{cue} ",
                        StringComparison.Ordinal))
                {
                    continue;
                }

                var detail = normalized[cue.Length..].Trim();
                if (HasSubstantiveClosingContent(detail))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static int CountMeaningfulWords(string value)
    {
        return value
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Count(word => word.Length > 1);
    }

    private static string NormalizeEvidenceText(string value)
    {
        var normalized = $" {RecommendationIntentPolicy.Normalize(value)} ";
        foreach (var (contracted, expanded) in NegativeContractions)
        {
            normalized = normalized.Replace(
            $" {contracted} ",
            $" {expanded} ",
            StringComparison.Ordinal);
        }

        return normalized.Trim();
    }

    private static bool HasUndefinedOrNegatedOutcome(string value)
    {
        var trimmed = value.TrimStart();
        return (!trimmed.StartsWith("not only ", StringComparison.Ordinal) &&
                trimmed.StartsWith("not ", StringComparison.Ordinal)) ||
               trimmed.StartsWith("no ", StringComparison.Ordinal) ||
               trimmed.StartsWith("nothing ", StringComparison.Ordinal) ||
               trimmed.StartsWith("nu ", StringComparison.Ordinal) ||
               trimmed.StartsWith("niciun ", StringComparison.Ordinal) ||
               trimmed.StartsWith("nicio ", StringComparison.Ordinal) ||
               InvalidAudienceOutcomeRegex().IsMatch(trimmed);
    }

    private static string TrimClosingLeadIns(string value)
    {
        var remaining = value;
        for (var index = 0; index < 3; index++)
        {
            var leadIn = ClosingLeadIns.FirstOrDefault(candidate =>
                remaining.StartsWith($"{candidate} ", StringComparison.Ordinal));
            if (leadIn is null)
            {
                break;
            }

            remaining = remaining[(leadIn.Length + 1)..];
        }

        return remaining;
    }

    private static bool HasSubstantiveClosingContent(string detail)
    {
        if (CountMeaningfulWords(detail) < 3 ||
            InvalidClosingContentRegex().IsMatch(detail) ||
            UnresolvedClosingContentRegex().IsMatch(detail) ||
            UnresolvedBeforeClosingMarkerRegex().IsMatch(detail))
        {
            return false;
        }

        var padded = $" {detail} ";
        return ClosingContentMarkers.Any(marker =>
                   padded.Contains($" {marker} ", StringComparison.Ordinal)) ||
               ClosingPredicateRegex().IsMatch(detail);
    }

    [GeneratedRegex(
        @"(?:\b(?:the|our)\s+(?:objective|objectives|purpose|goal|goals)(?:\s+of\s+(?:this|today\s+s)\s+(?:presentation|session|talk))?\s+(?:is|are)\s+(?:to\s+)?(?<detail>.+)|\bby\s+the\s+end(?:\s+of\s+(?:this|today\s+s)\s+(?:presentation|session|talk))?\s+(?:you|the\s+audience|participants|attendees|learners)\s+(?:will|should)\s+(?:understand|know|learn|be\s+able\s+to|decide|recognize)\s+(?<detail>.+)|\b(?:you|the\s+audience|participants|attendees|learners)\s+(?:will|should)\s+(?:understand|know|learn|be\s+able\s+to|decide|take\s+away)\s+(?<detail>.+)|\b(?:the\s+)?key\s+messages?\s+(?:is|are|include|includes)\s+(?<detail>.+)|\bwhat\s+(?:i|we)\s+want\s+(?:you|the\s+audience|participants|attendees|learners)\s+to\s+(?:understand|know|learn|remember|decide|take\s+away)\s+(?:is\s+)?(?<detail>.+))",
        RegexOptions.CultureInvariant)]
    private static partial Regex EnglishAudienceOutcomeRegex();

    [GeneratedRegex(
        @"(?:\b(?:obiectivul|obiectivele|scopul)(?:\s+acestei\s+(?:prezentari|sesiuni))?\s+(?:este|sunt)\s+sa\s+(?<detail>.+)|\bpana\s+la\s+final(?:ul\s+(?:prezentarii|sesiunii))?\s+(?:(?:voi\s+)?veti|(?:audienta\s+)?va|(?:participantii\s+)?vor)\s+(?:intelege|sti|invata|putea|decide)\s+(?<detail>.+)|\bmesajele?\s+cheie\s+(?:este|sunt|include|includ)\s+(?<detail>.+)|\bvreau\s+ca\s+(?:voi|audienta|participantii)\s+sa\s+(?:intelegeti|inteleaga|stiti|invete|retineti|retina)\s+(?<detail>.+))",
        RegexOptions.CultureInvariant)]
    private static partial Regex RomanianAudienceOutcomeRegex();

    [GeneratedRegex(
        @"(?:^|\s)(?:(?:not|never)\s+(?:(?:yet|currently|clearly|fully|explicitly)\s+)*(?:defined|clear|agreed|set|established|finalized|decided|known|discussed|reviewed)|(?:undefined|unclear|unknown|undecided|tbd)|(?:yet\s+)?to\s+be\s+(?:defined|agreed|set|established|finalized|decided|discussed|reviewed)|(?:(?:still|currently)\s+){0,2}being\s+(?:defined|agreed|set|established|finalized|decided|discussed|reviewed)|(?:still\s+)?under\s+(?:discussion|definition|review)|pending\s+(?:discussion|definition|review|agreement|decision)|(?:definition|agreement|decision)\s+(?:is\s+)?(?:pending|in\s+progress)|nu\s+(?:(?:este|sunt|a\s+fost|au\s+fost|e)\s+)?(?:(?:inca|clar|complet|explicit)\s+)*(?:definit|definite|clar|clara|agreat|agreate|stabilit|stabilite|finalizat|finalizate|decis|decise|cunoscut|cunoscute)|(?:nedefinit|nedefinite|neclar|neclara|neclare|necunoscut|necunoscuta|necunoscute)|(?:inca\s+)?(?:se\s+)?(?:defineste|stabileste|decide)|in\s+curs\s+de\s+(?:definire|stabilire|decizie))\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex InvalidAudienceOutcomeRegex();

    [GeneratedRegex(
        @"\b(?:is|are|was|were|means|provides|enables|allows|supports|reduces|centralizes|automates|requires|protects|manages|shows|demonstrates|este|sunt|inseamna|ofera|permite|sprijina|reduce|centralizeaza|automatizeaza|necesita|protejeaza|gestioneaza|arata|demonstreaza)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ClosingPredicateRegex();

    [GeneratedRegex(
        @"(?:^(?:thank\s+you|thanks|any\s+questions|that\s+is\s+all)\b|(?:^|\s)(?:(?:there\s+(?:is|are|was|were)|we\s+(?:have|had))\s+(?:no|not\s+any)\s+(?:(?:clear|agreed|defined|final)\s+)*(?:takeaways?|decisions?|actions?|next\s+(?:steps?|actions?))|we\s+(?:have|had)\s+not\s+(?:agreed|decided|identified|defined|set|finalized)\s+(?:on\s+)?(?:(?:a|an|any|the)\s+)?(?:takeaways?|decisions?|actions?|next\s+(?:steps?|actions?))|(?:we\s+)?(?:still\s+)?(?:need|have)\s+to\s+(?:agree|decide|identify|define|set|finalize)\s+(?:on\s+)?(?:(?:a|an|any|the)\s+)?(?:takeaways?|decisions?|actions?|next\s+(?:steps?|actions?))|no\s+(?:(?:clear|agreed|defined|final)\s+)*(?:takeaways?|decisions?|actions?|next\s+(?:steps?|actions?))|nu\s+(?:(?:avem|exista|am\s+agreat|am\s+decis)\s+)?(?:nicio|niciun|un|o)?\s*(?:mesaj|decizie|actiune|pas)))\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex InvalidClosingContentRegex();

    [GeneratedRegex(
        @"\b(?:(?:the\s+)?(?:takeaways?|decisions?|actions?|next\s+(?:steps?|actions?)|follow\s*up)|(?:intended|recommended)\s+actions?|conclusions?)\b.{0,64}\b(?:(?:is|are|was|were|has|have|had)\s+(?:not|never)\s+(?:been\s+)?(?:(?:yet|currently|clearly|fully|explicitly)\s+)*(?:clear|defined|decided|agreed|set|known|finalized)|(?:has|have|had)\s+(?:still\s+)?yet\s+to\s+be\s+(?:defined|decided|agreed|set|known|finalized|discussed|reviewed)|(?:is|are|was|were|remains?|stays?|needs?)\s+(?:still\s+)?(?:unclear|undefined|undecided|unknown|pending|absent|missing|under\s+discussion|being\s+(?:defined|decided|agreed|discussed)|(?:yet\s+)?to\s+be\s+(?:defined|decided|agreed|set|known|finalized|discussed|reviewed)))\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnresolvedClosingContentRegex();

    [GeneratedRegex(
        @"\b(?:we\s+(?:(?:have|had)\s+not\s+(?:(?:yet|still)\s+)?(?:agreed|decided|identified|defined|set|finalized)|(?:do|did)\s+not\s+(?:(?:yet|still)\s+)?(?:have|agree|decide|identify|define|set|finalize)|cannot\s+(?:(?:yet|still)\s+)?(?:agree|decide|identify|define|set|finalize))\s+(?:on\s+)?(?:(?:a|an|any|the)\s+)?(?:takeaways?|decisions?|actions?|next\s+(?:steps?|actions?))|there\s+(?:is|are|was|were)\s+not\s+(?:(?:yet|currently)\s+)?(?:(?:a|an|any|the)\s+)(?:takeaways?|decisions?|actions?|next\s+(?:steps?|actions?)))\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex UnresolvedBeforeClosingMarkerRegex();

    [GeneratedRegex(@"[.!?;\r\n]+", RegexOptions.CultureInvariant)]
    private static partial Regex SentenceBoundaryRegex();
}
