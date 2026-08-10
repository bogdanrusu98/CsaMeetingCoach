using System.Globalization;
using System.Text;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class CustomSpeechLanguageCorpusBuilderTests
{
    [Fact]
    public void CorpusIsDeterministicUniqueAndPrivacySafe()
    {
        var first = CustomSpeechLanguageCorpusBuilder.Build();
        var second = CustomSpeechLanguageCorpusBuilder.Build();

        Assert.Equal(first, second);
        Assert.True(first.Count >= 2_000);
        Assert.Equal(first.Count, first.Distinct(StringComparer.Ordinal).Count());
        Assert.All(first, line =>
        {
            Assert.NotEmpty(line);
            Assert.DoesNotContain('\r', line);
            Assert.DoesNotContain('\n', line);
            Assert.DoesNotContain("http://", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("https://", line, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("www.", line, StringComparison.OrdinalIgnoreCase);
            Assert.All(line, character => Assert.InRange(character, ' ', '~'));
        });
    }

    [Fact]
    public void CorpusRepresentsEverySafePhraseAndEducationalConcept()
    {
        var corpus = CustomSpeechLanguageCorpusBuilder.Build();
        var phrases = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();

        Assert.Equal(500, phrases.Count);
        Assert.All(phrases, phrase => Assert.Contains(ToAscii(phrase), corpus));
        Assert.Equal(183, EducationalConceptCatalog.All.Count);
        Assert.All(
            EducationalConceptCatalog.All,
            concept => Assert.Contains(ToAscii(concept.CanonicalTitle), corpus));
    }

    private static string ToAscii(string value)
    {
        var builder = new StringBuilder();
        foreach (var character in value.Normalize(NormalizationForm.FormD))
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            if (character is >= ' ' and <= '~')
            {
                builder.Append(character);
            }
            else if (character is '\u2013' or '\u2014' or '\u2212')
            {
                builder.Append('-');
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

        return builder.ToString();
    }
}
