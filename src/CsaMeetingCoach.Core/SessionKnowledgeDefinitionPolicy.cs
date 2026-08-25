using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static partial class SessionKnowledgeDefinitionPolicy
{
    private const int MaximumTitleWords = 4;
    private static readonly char[] TitleTrimCharacters =
        [' ', '\t', ':', ';', ',', '.', '-', '–', '—', '(', ')', '[', ']'];
    private static readonly HashSet<string> RejectedSingleWords =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "about", "after", "before", "chapter", "cooking", "example", "guide",
            "important", "ingredients", "method", "notes", "recipe", "section",
            "table", "temperature", "timing"
        };

    public static IReadOnlyList<ContextualCardProposal> Select(
        CoachAgentContext context,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        if (context.AudienceFamiliarity == AudienceFamiliarity.Expert)
        {
            return [];
        }

        var existingTitles = (context.ContextualCards ?? [])
            .Select(card => Normalize(card.Title))
            .ToHashSet(StringComparer.Ordinal);
        foreach (var segment in analysisWindow.Reverse())
        {
            foreach (var source in (context.Knowledge ?? [])
                         .Where(item =>
                             item.Visibility == KnowledgeSourceVisibility.MemberEligible))
            {
                var lines = source.Content
                    .Replace("\r\n", "\n", StringComparison.Ordinal)
                    .Replace('\r', '\n')
                    .Split('\n', StringSplitOptions.RemoveEmptyEntries)
                    .Select(line => string.Join(
                        ' ',
                        line.Split(
                            (char[]?)null,
                            StringSplitOptions.RemoveEmptyEntries
                            | StringSplitOptions.TrimEntries)))
                    .Where(line => line.Length > 0)
                    .ToArray();
                ContextualCardProposal? proposal = null;
                for (var lineIndex = 0; lineIndex < lines.Length; lineIndex++)
                {
                    if (!TryCreateDefinition(
                            segment,
                            lines,
                            lineIndex,
                            context.AudienceFamiliarity,
                            out var title,
                            out var content,
                            out var evidenceQuote)
                        || existingTitles.Contains(Normalize(title)))
                    {
                        continue;
                    }

                    proposal = CreateProposal(
                        segment.Id,
                        source.SourceId,
                        title,
                        content,
                        evidenceQuote);
                    break;
                }

                if (proposal is null
                    && TryCreateDefinitionFromFlattenedContent(
                        segment,
                        source.Content,
                        context.AudienceFamiliarity,
                        out var flattenedTitle,
                        out var flattenedContent,
                        out var flattenedEvidence)
                    && !existingTitles.Contains(Normalize(flattenedTitle)))
                {
                    proposal = CreateProposal(
                        segment.Id,
                        source.SourceId,
                        flattenedTitle,
                        flattenedContent,
                        flattenedEvidence);
                }

                if (proposal is not null)
                {
                    return [proposal];
                }
            }
        }

        return [];
    }

    private static bool TryCreateDefinition(
        TranscriptSegment segment,
        IReadOnlyList<string> lines,
        int lineIndex,
        AudienceFamiliarity familiarity,
        out string title,
        out string content,
        out string evidenceQuote)
    {
        title = string.Empty;
        content = string.Empty;
        evidenceQuote = string.Empty;
        var line = lines[lineIndex];
        if (line.Length > 600)
        {
            return false;
        }

        var words = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var wordCount = Math.Min(MaximumTitleWords, words.Length);
             wordCount >= 1;
             wordCount--)
        {
            var candidate = string.Join(' ', words.Take(wordCount))
                .Trim(TitleTrimCharacters);
            if (!IsEligibleTitle(candidate, familiarity)
                || !ContainsWholeTerm(segment.Text, candidate))
            {
                continue;
            }

            var remainder = string.Join(' ', words.Skip(wordCount))
                .Trim(TitleTrimCharacters);
            var separatorIndex = Math.Min(candidate.Length, line.Length);
            var hasExplicitSeparator = separatorIndex < line.Length
                && line[separatorIndex] is ':' or ';' or '-' or '–' or '—';
            var hasDefinitionCue =
                remainder.StartsWith("is ", StringComparison.OrdinalIgnoreCase)
                || remainder.StartsWith("means ", StringComparison.OrdinalIgnoreCase)
                || remainder.StartsWith("refers ", StringComparison.OrdinalIgnoreCase);
            string definition;
            string quote;
            if (remainder.Length >= 20
                && (hasExplicitSeparator || hasDefinitionCue))
            {
                definition = hasDefinitionCue
                    ? $"{candidate} {remainder}"
                    : remainder;
                quote = line;
            }
            else if (lineIndex + 1 < lines.Count
                     && lines[lineIndex + 1].Length is >= 20 and <= 600)
            {
                definition = lines[lineIndex + 1];
                quote = definition;
            }
            else
            {
                continue;
            }

            title = candidate;
            content = definition.Length <= 320
                ? definition
                : string.Concat(definition.AsSpan(0, 317), "...");
            evidenceQuote = quote;
            return true;
        }

        return false;
    }

    private static bool TryCreateDefinitionFromFlattenedContent(
        TranscriptSegment segment,
        string sourceContent,
        AudienceFamiliarity familiarity,
        out string title,
        out string content,
        out string evidenceQuote)
    {
        title = string.Empty;
        content = string.Empty;
        evidenceQuote = string.Empty;
        var searchRegion = GetDefinitionSearchRegion(sourceContent);
        var searchContent = searchRegion.Content;
        var transcriptWords = WordRegex().Matches(segment.Text)
            .Select(match => match.Value)
            .ToArray();
        DefinitionCandidate? best = null;
        for (var wordCount = Math.Min(MaximumTitleWords, transcriptWords.Length);
             wordCount >= 1;
             wordCount--)
        {
            for (var start = 0; start <= transcriptWords.Length - wordCount; start++)
            {
                var transcriptTerm = string.Join(
                    ' ',
                    transcriptWords.Skip(start).Take(wordCount));
                foreach (var match in FindWholeTermMatches(
                             searchContent,
                             transcriptTerm))
                {
                    var sourceTitle = searchContent
                        .Substring(match.Index, match.Length)
                        .Trim(TitleTrimCharacters);
                    if (!IsEligibleTitle(sourceTitle, familiarity)
                        || !TryExtractFollowingDefinition(
                            searchContent,
                            match.Index + match.Length,
                            requireExplicitCue: !searchRegion.IsExplicitGlossary,
                            out var definition,
                            out var quote))
                    {
                        continue;
                    }

                    var glossaryIndex = searchContent.LastIndexOf(
                        "glossary",
                        match.Index,
                        StringComparison.OrdinalIgnoreCase);
                    var score = wordCount * 20
                        + (char.IsUpper(sourceTitle[0]) ? 10 : 0)
                        + (glossaryIndex >= 0 && match.Index - glossaryIndex < 1500 ? 30 : 0);
                    var candidate = new DefinitionCandidate(
                        sourceTitle,
                        definition,
                        quote,
                        score);
                    if (best is null || candidate.Score > best.Score)
                    {
                        best = candidate;
                    }
                }
            }
        }

        if (best is null)
        {
            return false;
        }

        title = best.Title;
        content = best.Content;
        evidenceQuote = best.EvidenceQuote;
        return true;
    }

    private static DefinitionSearchRegion GetDefinitionSearchRegion(string sourceContent)
    {
        var glossaryIndex = sourceContent.LastIndexOf(
            "Mini glossary",
            StringComparison.OrdinalIgnoreCase);
        var markerLength = "Mini glossary".Length;
        if (glossaryIndex < 0)
        {
            glossaryIndex = sourceContent.LastIndexOf(
                "Glossary Term Meaning",
                StringComparison.OrdinalIgnoreCase);
            markerLength = "Glossary".Length;
        }
        if (glossaryIndex < 0)
        {
            return new DefinitionSearchRegion(sourceContent, false);
        }

        var start = glossaryIndex + markerLength;
        var termMeaningIndex = sourceContent.IndexOf(
            "Term Meaning",
            start,
            StringComparison.OrdinalIgnoreCase);
        if (termMeaningIndex >= 0 && termMeaningIndex - start < 200)
        {
            start = termMeaningIndex + "Term Meaning".Length;
        }

        var endMarkers = new[]
        {
            "Grounding verification",
            "References",
            "Appendix",
            "Recommended demo prompt"
        };
        var end = sourceContent.Length;
        foreach (var marker in endMarkers)
        {
            var markerIndex = sourceContent.IndexOf(
                marker,
                start,
                StringComparison.OrdinalIgnoreCase);
            if (markerIndex >= 0)
            {
                end = Math.Min(end, markerIndex);
            }
        }

        return new DefinitionSearchRegion(sourceContent[start..end], true);
    }

    private static bool TryExtractFollowingDefinition(
        string sourceContent,
        int startIndex,
        bool requireExplicitCue,
        out string content,
        out string evidenceQuote)
    {
        content = string.Empty;
        evidenceQuote = string.Empty;
        var available = sourceContent.AsSpan(startIndex);
        var afterWhitespace = available.TrimStart();
        var hasSeparator = afterWhitespace.Length > 0
            && afterWhitespace[0] is ':' or ';' or '-' or '–' or '—';
        var trimCharacters = ":;,.()-–—[] \t".AsSpan();
        var trimmed = available.TrimStart(trimCharacters);
        var skipped = available.Length - trimmed.Length;
        var hasDefinitionCue =
            trimmed.StartsWith("is ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("means ", StringComparison.OrdinalIgnoreCase)
            || trimmed.StartsWith("refers to ", StringComparison.OrdinalIgnoreCase);
        if (requireExplicitCue && !hasSeparator && !hasDefinitionCue)
        {
            return false;
        }
        if (trimmed.Length < 20)
        {
            return false;
        }

        var maximumLength = Math.Min(320, trimmed.Length);
        var window = trimmed[..maximumLength].ToString();
        var end = FindDefinitionBoundary(window);
        var definition = window[..end].Trim(TitleTrimCharacters);
        if (definition.Length < 20)
        {
            return false;
        }

        content = definition;
        evidenceQuote = sourceContent.Substring(
            startIndex + skipped,
            definition.Length);
        return true;
    }

    private static int FindDefinitionBoundary(string value)
    {
        var punctuation = value.IndexOfAny(['.', '!', '?', '•'], 20);
        var inferred = GlossaryBoundaryRegex().Match(value, 20);
        var boundary = value.Length;
        if (punctuation >= 20)
        {
            boundary = punctuation + 1;
        }
        if (inferred.Success && inferred.Index < boundary)
        {
            boundary = inferred.Index;
        }
        return boundary;
    }

    private static ContextualCardProposal CreateProposal(
        Guid segmentId,
        Guid sourceId,
        string title,
        string content,
        string evidenceQuote) =>
        new(
            ContextualCardKind.Definition,
            title,
            content,
            0.9,
            [segmentId])
        {
            SourceKnowledgeIds = [sourceId],
            KnowledgeEvidenceQuote = evidenceQuote
        };

    private static bool IsEligibleTitle(
        string candidate,
        AudienceFamiliarity familiarity)
    {
        if (candidate.Length is < 4 or > 80
            || candidate.Any(char.IsControl)
            || char.IsLower(candidate[0]))
        {
            return false;
        }

        var words = candidate.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 1)
        {
            if (RejectedSingleWords.Contains(candidate))
            {
                return false;
            }

            return familiarity == AudienceFamiliarity.Beginner
                || candidate.Length >= 7;
        }

        return true;
    }

    private static Match[] FindWholeTermMatches(string text, string term)
    {
        try
        {
            return Regex.Matches(
                    text,
                    $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(term)}(?![\p{{L}}\p{{N}}])",
                    RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
                    TimeSpan.FromMilliseconds(100))
                .Cast<Match>()
                .ToArray();
        }
        catch (RegexMatchTimeoutException)
        {
            return [];
        }
    }

    private static bool ContainsWholeTerm(string text, string term) =>
        FindWholeTermMatches(text, term).Length > 0;

    private static string Normalize(string value) =>
        MultiWhitespaceRegex().Replace(value.Trim().ToLowerInvariant(), " ");

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex MultiWhitespaceRegex();

    [GeneratedRegex(@"[\p{L}\p{N}]+(?:[-'][\p{L}\p{N}]+)*", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();

    [GeneratedRegex(
        @"(?<=[a-z])\s+(?=[A-Z][\p{L}-]*(?:\s+[a-z][\p{L}-]*){0,3}\s+[A-Z][\p{L}-]*)",
        RegexOptions.CultureInvariant)]
    private static partial Regex GlossaryBoundaryRegex();

    private sealed record DefinitionCandidate(
        string Title,
        string Content,
        string EvidenceQuote,
        int Score);

    private sealed record DefinitionSearchRegion(
        string Content,
        bool IsExplicitGlossary);
}
