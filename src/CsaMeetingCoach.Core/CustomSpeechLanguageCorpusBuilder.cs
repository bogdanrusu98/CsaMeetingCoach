using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CsaMeetingCoach.Core;

public static partial class CustomSpeechLanguageCorpusBuilder
{
    public static IReadOnlyList<string> Build()
    {
        var lines = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var phrase in EducationalConceptCatalog.BuildSpeechPhraseVocabulary())
        {
            Add(lines, seen, phrase);
            Add(lines, seen, $"The customer mentioned {phrase} during the architecture discussion.");
            Add(lines, seen, $"We should clarify how {phrase} supports the business outcome.");
            Add(lines, seen, $"The next step is to validate the proposed use of {phrase}.");
        }

        foreach (var concept in EducationalConceptCatalog.All)
        {
            Add(lines, seen, concept.CanonicalTitle);
            Add(lines, seen, concept.DefinitionText);
            Add(lines, seen, concept.HintText);
            Add(lines, seen, $"The team discussed {concept.CanonicalTitle} in the customer meeting.");
            Add(lines, seen, $"Please explain {concept.CanonicalTitle} in the solution proposal.");
            Add(lines, seen, $"We need to confirm the requirements for {concept.CanonicalTitle}.");
        }

        return lines.AsReadOnly();
    }

    private static void Add(
        List<string> lines,
        HashSet<string> seen,
        string value)
    {
        var normalized = Normalize(value);
        if (normalized.Length > 0 && seen.Add(normalized))
        {
            lines.Add(normalized);
        }
    }

    private static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(character);
            if (category == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is >= ' ' and <= '~')
            {
                builder.Append(character);
            }
            else if (char.IsWhiteSpace(character)
                || character is '\u2013' or '\u2014' or '\u2212')
            {
                builder.Append(character is '\u2013' or '\u2014' or '\u2212' ? '-' : ' ');
            }
            else if (character is '\u2018' or '\u2019')
            {
                builder.Append('\'');
            }
            else if (character is '\u201c' or '\u201d')
            {
                builder.Append('"');
            }
        }

        return WhitespaceRegex().Replace(builder.ToString(), " ").Trim();
    }

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
