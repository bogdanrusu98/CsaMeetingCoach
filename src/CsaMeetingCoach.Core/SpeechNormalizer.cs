using System.Text;
using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed record SpeechNormalizeResult(
    string NormalizedText,
    bool WasCorrected,
    string? CorrectionReason);

public static partial class SpeechNormalizer
{
    private const int MaxContextRadius = 40;

    private static readonly string[] StrongAzureAnchors =
    [
        "AKS", "Kubernetes Service", "Entra ID", "Key Vault", "resource group",
        "App Service", "Cosmos DB", "Blob Storage", "Bicep", "ARM template",
        "Defender for Cloud", "Sentinel", "Microsoft cloud", "management group",
        "managed identity", "Azure DevOps"
    ];

    private static readonly string[] PurposeSupportedAnchors =
    [
        "subscription", "Functions", "Logic Apps", "Service Bus", "Event Hubs",
        "API Management", "Container Apps", "Azure Monitor", "Log Analytics"
    ];

    private static readonly HashSet<string> GeographicPrefixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "southeast", "east", "south", "north", "west", "central", "near"
    };

    private static readonly HashSet<string> GeographicSuffixes = new(StringComparer.OrdinalIgnoreCase)
    {
        "pacific", "region", "regions", "minor", "continent", "customer", "customers",
        "market", "markets", "office", "offices", "business", "operations", "user", "users",
        "team", "teams", "country", "countries", "workload", "workloads", "branch", "branches",
        "based"
    };

    private static readonly HashSet<string> GeographicPrepositions = new(StringComparer.OrdinalIgnoreCase)
    {
        "in", "to", "from", "across", "throughout", "outside", "within", "over", "into"
    };

    private static readonly string[] SegmentBlockingKeywords =
    [
        "AWS", "GCP", "google cloud", "aws region", "travel", "geography", "continent"
    ];

    private static readonly HashSet<string> AzureContinuationTokens = BuildAzureContinuationTokens();

    [GeneratedRegex(@"\bAsia\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AsiaRegex();

    [GeneratedRegex(
        @"^\s*(?:(?:,|&|and|or|together\s+with|along\s+with|as\s+well\s+as|alongside|versus|vs\.?)\s*)+(?:the\s+)?(?:Europe|Africa|Australia|Antarctica|North America|South America|Middle East)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex GeographicCoordinationRegex();

    public static SpeechNormalizeResult Normalize(
        string text,
        bool isSpeechRecognized,
        MeetingPurpose? purpose)
    {
        if (!isSpeechRecognized || string.IsNullOrWhiteSpace(text))
        {
            return new SpeechNormalizeResult(text, false, null);
        }

        var matches = AsiaRegex().Matches(text);
        if (matches.Count == 0)
        {
            return new SpeechNormalizeResult(text, false, null);
        }

        var purposeAzureFocused = IsPurposeAzureFocused(purpose);
        var hasStrongAnchor = HasAnchorInText(text, StrongAzureAnchors);
        var hasPurposeSupportedAnchor = purposeAzureFocused
            && HasAnchorInText(text, PurposeSupportedAnchors);
        if (!hasStrongAnchor && !hasPurposeSupportedAnchor)
        {
            return new SpeechNormalizeResult(text, false, null);
        }

        var result = new StringBuilder(text);
        var offset = 0;
        var anyCorrected = false;
        var usedStrongAnchor = false;

        for (var matchIndex = 0; matchIndex < matches.Count; matchIndex++)
        {
            var match = matches[matchIndex];
            if (IsGeographicConstruction(text, match.Index, match.Length))
            {
                continue;
            }

            var continuationStart = match.Index + match.Length;
            var continuationEnd = Math.Min(
                text.Length,
                continuationStart + MaxContextRadius);
            if (!HasAzureContinuation(text[continuationStart..continuationEnd]))
            {
                continue;
            }

            var occurrenceContext = GetOccurrenceContext(text, matches, matchIndex);
            if (HasBlockedSegmentContext(occurrenceContext))
            {
                continue;
            }

            var occurrenceHasStrongAnchor = HasAnchorInText(
                occurrenceContext,
                StrongAzureAnchors);
            var occurrenceHasPurposeSupportedAnchor = purposeAzureFocused
                && HasAnchorInText(occurrenceContext, PurposeSupportedAnchors);
            if (!occurrenceHasStrongAnchor && !occurrenceHasPurposeSupportedAnchor)
            {
                continue;
            }

            var adjustedStart = match.Index + offset;
            result.Remove(adjustedStart, match.Length);
            result.Insert(adjustedStart, "Azure");
            offset += "Azure".Length - match.Length;
            anyCorrected = true;
            usedStrongAnchor |= occurrenceHasStrongAnchor;
        }

        if (!anyCorrected)
        {
            return new SpeechNormalizeResult(text, false, null);
        }

        return new SpeechNormalizeResult(
            result.ToString(),
            true,
            usedStrongAnchor
                ? "AsiaToAzure:EcosystemAnchor"
                : "AsiaToAzure:AzurePurpose+Anchor");
    }

    private static bool IsGeographicConstruction(string text, int matchIndex, int matchLength)
    {
        var contextStart = Math.Max(0, matchIndex - MaxContextRadius);
        var contextEnd = Math.Min(text.Length, matchIndex + matchLength + MaxContextRadius);
        var before = text[contextStart..matchIndex];
        var after = text[(matchIndex + matchLength)..contextEnd];

        if (before.Contains("Southeast ", StringComparison.OrdinalIgnoreCase))
        {
            var previousWord = GetPreviousWord(before);
            if (string.Equals(previousWord, "southeast", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        var previous = GetPreviousWord(before);
        if (previous is not null
            && (GeographicPrefixes.Contains(previous)
                || (GeographicPrepositions.Contains(previous) && !HasAzureContinuation(after))))
        {
            return true;
        }

        var next = GetGeographicSuffixWord(after);
        return (next is not null && GeographicSuffixes.Contains(next))
            || GeographicCoordinationRegex().IsMatch(after);
    }

    private static string? GetGeographicSuffixWord(string text)
    {
        var trimmed = text.AsSpan().TrimStart();
        if (trimmed.Length >= 2
            && (trimmed[0] == '\'' || trimmed[0] == '\u2019')
            && (trimmed[1] == 's' || trimmed[1] == 'S'))
        {
            trimmed = trimmed[2..];
        }

        return GetNextWord(trimmed.ToString());
    }

    private static string GetOccurrenceContext(
        string text,
        MatchCollection matches,
        int matchIndex)
    {
        var match = matches[matchIndex];
        var contextStart = Math.Max(0, match.Index - MaxContextRadius);
        var contextEnd = Math.Min(
            text.Length,
            match.Index + match.Length + MaxContextRadius);

        if (matchIndex > 0)
        {
            var previous = matches[matchIndex - 1];
            contextStart = Math.Max(
                contextStart,
                previous.Index + previous.Length);
        }

        if (matchIndex + 1 < matches.Count)
        {
            var next = matches[matchIndex + 1];
            contextEnd = Math.Min(
                contextEnd,
                next.Index);
        }

        return text[contextStart..contextEnd];
    }

    private static bool HasAnchorInText(string text, IEnumerable<string> anchors)
    {
        foreach (var anchor in anchors)
        {
            if (Regex.IsMatch(
                text,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(anchor)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPurposeAzureFocused(MeetingPurpose? purpose)
    {
        if (purpose is null)
        {
            return false;
        }

        return (purpose.Title?.Contains("azure", StringComparison.OrdinalIgnoreCase) ?? false)
            || (purpose.MeetingType?.Contains("azure", StringComparison.OrdinalIgnoreCase) ?? false)
            || (purpose.Objective?.Contains("azure", StringComparison.OrdinalIgnoreCase) ?? false);
    }

    private static bool HasBlockedSegmentContext(string text)
    {
        foreach (var keyword in SegmentBlockingKeywords)
        {
            if (Regex.IsMatch(
                text,
                $@"(?<![\p{{L}}\p{{N}}]){Regex.Escape(keyword)}(?![\p{{L}}\p{{N}}])",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasAzureContinuation(string after)
    {
        var nextWord = GetNextWord(after);
        return nextWord is not null && AzureContinuationTokens.Contains(nextWord);
    }

    private static HashSet<string> BuildAzureContinuationTokens()
    {
        return StrongAzureAnchors
            .Concat(PurposeSupportedAnchors)
            .Select(anchor => anchor.Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private static string? GetPreviousWord(string text)
    {
        for (var index = text.Length - 1; index >= 0; index--)
        {
            if (!char.IsLetterOrDigit(text[index]))
            {
                continue;
            }

            var end = index + 1;
            while (index >= 0 && char.IsLetterOrDigit(text[index]))
            {
                index--;
            }

            return text[(index + 1)..end];
        }

        return null;
    }

    private static string? GetNextWord(string text)
    {
        for (var index = 0; index < text.Length; index++)
        {
            if (!char.IsLetterOrDigit(text[index]))
            {
                continue;
            }

            var start = index;
            while (index < text.Length && char.IsLetterOrDigit(text[index]))
            {
                index++;
            }

            return text[start..index];
        }

        return null;
    }
}
