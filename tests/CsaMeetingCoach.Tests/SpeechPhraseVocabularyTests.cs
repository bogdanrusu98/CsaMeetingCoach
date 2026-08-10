using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class SpeechPhraseVocabularyTests
{
    [Fact]
    public void BuildSpeechPhraseVocabulary_AzureIsFirst()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.Equal("Azure", vocab[0]);
    }

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
        // New extended catalog entries
        Assert.Contains("Microsoft Fabric", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Microsoft Foundry", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Managed Redis", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Container Registry", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Virtual Desktop", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Virtual WAN", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Microsoft Purview", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Chaos Studio", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure Developer CLI", vocab, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_ExcludesUnsafeShortAliases()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.DoesNotContain("cloud", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("arc", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("batch", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("monitor", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("redis", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("finops", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("subnet", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("workbooks", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("refactoring", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("rearchitect", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("rearchitecting", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("rehosting", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure Cosmos DB for PostgreSQL", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain("Azure Cache for Redis Enterprise", vocab, StringComparer.OrdinalIgnoreCase);
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
    public void BuildSpeechPhraseVocabulary_HasExactlyFiveHundredEntries()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.Equal(500, vocab.Count);
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
        Assert.Contains("ACR", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AVD", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AVS", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("WAF", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("KEDA", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("azd", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("RAG pattern", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("SLO", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("DCR", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("AMA", vocab, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_ContainsSubnetAndFinOpsMultiwordForms()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        Assert.Contains("Azure subnet", vocab, StringComparer.OrdinalIgnoreCase);
        Assert.Contains("Azure FinOps", vocab, StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void BuildSpeechPhraseVocabulary_PrioritizesCurrentSupplementalPhrases()
    {
        var vocab = EducationalConceptCatalog.BuildSpeechPhraseVocabulary();
        string[] expected =
        [
            "Azure IoT Operations",
            "Azure Stack Edge GPU",
            "Azure AI Foundry project",
            "Microsoft Fabric OneLake",
            "Azure Managed Prometheus",
            "Azure Traffic Manager",
            "Azure DNS Private Resolver",
            "Azure Savings Plan",
            "Microsoft Entra Verified ID",
            "Azure Compute Gallery",
            "Azure Container Registry task",
            "Azure API Center governance",
            "Microsoft Defender for Servers",
        ];

        Assert.All(expected, phrase =>
            Assert.Contains(phrase, vocab, StringComparer.OrdinalIgnoreCase));
    }
}
