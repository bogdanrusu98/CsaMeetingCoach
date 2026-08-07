using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class SpeechPhraseVocabularyTests
{
    [Fact]
    public void BuildSpeechPhraseVocabulary_AlwaysIncludesAzure()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.Contains("Azure", vocab, StringComparer.Ordinal);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_ContainsRepresentativeCatalogPhrases()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.Contains("Azure Kubernetes Service (AKS)", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Key Vault", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure App Service", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Blob Storage", vocab, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_ExcludesUnsafeShortAliases()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.DoesNotContain("cloud", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("arc", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("batch", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("monitor", vocab, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_IsDeduplicatedCaseInsensitive()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        var distinct = vocab
            .Select(phrase => phrase.ToUpperInvariant())
            .Distinct()
            .Count();
        Assert.Equal(vocab.Count, distinct);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_MaxFiveHundredEntries()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.True(vocab.Count <= 500, $"Vocabulary has {vocab.Count} entries, exceeding 500.");
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_IsDeterministic()
    {
        var v1 = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        var v2 = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.Equal(v1, v2);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_IncludesSafeAcronyms()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.Contains("AKS", vocab, StringComparer.OrdinalIgnoreCase);
    }
}
