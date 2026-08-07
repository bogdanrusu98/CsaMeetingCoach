using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class SpeechNormalizerTests
{
    [Theory]
    [InlineData("Asia Kubernetes Service", "Azure Kubernetes Service", "AsiaToAzure:EcosystemAnchor")]
    [InlineData("Asia Key Vault", "Azure Key Vault", "AsiaToAzure:EcosystemAnchor")]
    [InlineData("our Asia subscription and resource group", "our Azure subscription and resource group", "AsiaToAzure:EcosystemAnchor")]
    [InlineData("The Asia App Service deployment is ready.", "The Azure App Service deployment is ready.", "AsiaToAzure:EcosystemAnchor")]
    [InlineData("We deployed to Asia Blob Storage.", "We deployed to Azure Blob Storage.", "AsiaToAzure:EcosystemAnchor")]
    public void Normalize_SpeechWithEcosystemAnchor_CorrectsAsiaToAzure(
        string input,
        string expectedText,
        string expectedReason)
    {
        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.True(result.WasCorrected);
        Assert.Equal(expectedText, result.NormalizedText);
        Assert.Equal(expectedReason, result.CorrectionReason);
    }

    [Fact]
    public void Normalize_SpeechWithAzurePurposeAndAnchor_CorrectsAsiaToAzure()
    {
        var purpose = new MeetingPurpose(
            "Azure Migration Review",
            "Azure Workshop",
            "Review Azure migration options for the customer.",
            []);
        var result = SpeechNormalizer.Normalize(
            "We discussed the Asia subscription limits.",
            isSpeechRecognized: true,
            purpose: purpose);

        Assert.True(result.WasCorrected);
        Assert.Equal("We discussed the Azure subscription limits.", result.NormalizedText);
        Assert.Equal("AsiaToAzure:AzurePurpose+Anchor", result.CorrectionReason);
    }

    [Theory]
    [InlineData("Customers in Asia use AKS.")]
    [InlineData("Our offices in Asia Pacific use Azure Key Vault.")]
    [InlineData("Southeast Asia uses Azure App Service.")]
    [InlineData("Southeast Asia and Europe use Azure DevOps.")]
    [InlineData("We travel to Asia before the Azure DevOps workshop.")]
    [InlineData("The Asia Pacific region uses Azure Monitor.")]
    [InlineData("East Asia uses Azure Kubernetes Service.")]
    [InlineData("South Asia uses Azure Blob Storage.")]
    [InlineData("Data centers across Asia use AKS.")]
    [InlineData("Asia customers use AKS.")]
    [InlineData("Our Asia-based team uses Azure Key Vault.")]
    [InlineData("Asia operations use Azure App Service.")]
    [InlineData("Asia's team uses AKS.")]
    [InlineData("Asia\u2019s team uses Azure Key Vault.")]
    [InlineData("Asia together with Europe uses AKS.")]
    [InlineData("Asia, along with Europe, uses Azure Key Vault.")]
    [InlineData("Asia as well as Europe uses Azure App Service.")]
    [InlineData("Asia alongside Europe uses Azure DevOps.")]
    public void Normalize_GeographicConstruction_DoesNotCorrect(string input)
    {
        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.False(result.WasCorrected);
        Assert.Equal(input, result.NormalizedText);
        Assert.Null(result.CorrectionReason);
    }

    [Fact]
    public void Normalize_CompetitorInSeparateOccurrenceContext_CorrectsOnlyAzureOccurrence()
    {
        const string input = "AWS is used in Asia. Asia Kubernetes Service is our target.";

        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.True(result.WasCorrected);
        Assert.Equal(
            "AWS is used in Asia. Azure Kubernetes Service is our target.",
            result.NormalizedText);
        Assert.Equal("AsiaToAzure:EcosystemAnchor", result.CorrectionReason);
    }

    [Fact]
    public void Normalize_AdjacentTechnicalOccurrences_DoesNotSplitAnchorTokens()
    {
        const string input = "Asia AKS and Asia Key Vault.";

        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.True(result.WasCorrected);
        Assert.Equal("Azure AKS and Azure Key Vault.", result.NormalizedText);
        Assert.Equal("AsiaToAzure:EcosystemAnchor", result.CorrectionReason);
    }

    [Fact]
    public void Normalize_AdjacentCompetitorContext_DoesNotSplitBlockerTokens()
    {
        const string input = "Asia AWS Asia AKS.";

        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.False(result.WasCorrected);
        Assert.Equal(input, result.NormalizedText);
        Assert.Null(result.CorrectionReason);
    }

    [Fact]
    public void Normalize_MixedGeographicAndTechnicalOccurrences_CorrectsOnlyTechnicalOccurrence()
    {
        const string input = "Asia and Europe evaluated Asia Kubernetes Service.";

        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.True(result.WasCorrected);
        Assert.Equal(
            "Asia and Europe evaluated Azure Kubernetes Service.",
            result.NormalizedText);
        Assert.Equal("AsiaToAzure:EcosystemAnchor", result.CorrectionReason);
    }

    [Fact]
    public void Normalize_GeographicCoordinationWithTechnicalAnchor_DoesNotCorrect()
    {
        const string input = "Asia and Europe evaluated Azure Kubernetes Service.";

        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.False(result.WasCorrected);
        Assert.Equal(input, result.NormalizedText);
        Assert.Null(result.CorrectionReason);
    }

    [Fact]
    public void Normalize_DistantTechnicalAnchorWithoutContinuation_DoesNotCorrect()
    {
        const string input = "Asia is expanding while the platform uses AKS.";

        var result = SpeechNormalizer.Normalize(input, isSpeechRecognized: true, purpose: null);

        Assert.False(result.WasCorrected);
        Assert.Equal(input, result.NormalizedText);
        Assert.Null(result.CorrectionReason);
    }

    [Fact]
    public void Normalize_BareAsiaWithNoAnchor_DoesNotCorrect()
    {
        var result = SpeechNormalizer.Normalize(
            "We are expanding to Asia.",
            isSpeechRecognized: true,
            purpose: null);

        Assert.False(result.WasCorrected);
    }

    [Fact]
    public void Normalize_AwsContextWithAsia_DoesNotCorrect()
    {
        var result = SpeechNormalizer.Normalize(
            "AWS region in Asia has lower latency.",
            isSpeechRecognized: true,
            purpose: null);

        Assert.False(result.WasCorrected);
    }

    [Fact]
    public void Normalize_ManualTranscript_NotSpeechRecognized_DoesNotCorrect()
    {
        var result = SpeechNormalizer.Normalize(
            "Asia Kubernetes Service",
            isSpeechRecognized: false,
            purpose: null);

        Assert.False(result.WasCorrected);
        Assert.Equal("Asia Kubernetes Service", result.NormalizedText);
    }

    [Fact]
    public void Normalize_NullPurposeWithNoPurposeAnchor_DoesNotCorrectSubscriptionAlone()
    {
        var result = SpeechNormalizer.Normalize(
            "The Asia subscription is expiring.",
            isSpeechRecognized: true,
            purpose: null);

        Assert.False(result.WasCorrected);
    }

    [Fact]
    public void Normalize_NonAzurePurpose_SubscriptionAlone_DoesNotCorrect()
    {
        var purpose = new MeetingPurpose(
            "AWS Cost Review",
            "Cost Review",
            "Review AWS subscription costs.",
            []);
        var result = SpeechNormalizer.Normalize(
            "The Asia subscription",
            isSpeechRecognized: true,
            purpose: purpose);

        Assert.False(result.WasCorrected);
    }

    [Fact]
    public void Normalize_EmptyText_ReturnsUnchanged()
    {
        var result = SpeechNormalizer.Normalize(string.Empty, isSpeechRecognized: true, purpose: null);
        Assert.False(result.WasCorrected);
        Assert.Equal(string.Empty, result.NormalizedText);
    }

    [Fact]
    public void Normalize_NoAsiaInText_ReturnsUnchanged()
    {
        const string text = "We need to review the migration timeline.";
        var result = SpeechNormalizer.Normalize(text, isSpeechRecognized: true, purpose: null);
        Assert.False(result.WasCorrected);
        Assert.Equal(text, result.NormalizedText);
    }
}
