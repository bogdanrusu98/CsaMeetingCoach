using CsaMeetingCoach.Contracts;
using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class SemanticTermCatalogTests
{
    [Fact]
    public void EducationalConceptCatalog_ContainsAtLeastOneHundredConcepts()
    {
        Assert.True(EducationalConceptCatalog.All.Count >= 100);
    }

    [Theory]
    [MemberData(nameof(AllEducationalConcepts))]
    public void EducationalConceptCatalog_EntriesAreWellFormed(
        EducationalConcept concept)
    {
        Assert.False(string.IsNullOrWhiteSpace(concept.ConceptKey));
        Assert.Matches("^[a-z0-9]+(?:-[a-z0-9]+)*$", concept.ConceptKey);
        Assert.False(string.IsNullOrWhiteSpace(concept.CanonicalTitle));
        Assert.NotEmpty(concept.Aliases);
        Assert.All(concept.Aliases, alias => Assert.False(string.IsNullOrWhiteSpace(alias)));
        Assert.InRange(concept.DefinitionText.Length, 1, 300);
        Assert.InRange(concept.HintText.Length, 1, 350);
        Assert.StartsWith("https://learn.microsoft.com", concept.LearnUrl, StringComparison.Ordinal);
        Assert.True(Enum.IsDefined(concept.Category));
    }

    [Theory]
    [MemberData(nameof(AllEducationalConcepts))]
    public void EducationalConceptCatalog_PrimaryAliasIsDetectable(
        EducationalConcept concept)
    {
        var latest = new TranscriptSegment(
            Guid.NewGuid(),
            "Presenter",
            concept.RequiresAzureVendorScope
                ? $"{concept.Aliases[0]} is part of the Azure design."
                : $"{concept.Aliases[0]} is part of the design.",
            DateTimeOffset.UtcNow,
            IsFinal: true);
        var purpose = TestData.CreatePurpose() with
        {
            MeetingType = "Azure presentation",
            Objective = "Explain Azure foundations."
        };

        var cards = PresentationCoachingPolicy.SelectContextualCards(
            new CoachAgentContext(purpose, [], [latest]),
            [],
            [latest]);

        Assert.Contains(
            cards,
            card => string.Equals(
                card.ConceptKey,
                concept.ConceptKey,
                StringComparison.Ordinal));
    }

    [Fact]
    public void Expand_MapsPhraseAndAbbreviationDeterministically()
    {
        var catalog = new BuiltInSemanticTermCatalog();

        var expansions = catalog.Expand("reduce TCO");

        Assert.Contains("reduce tco", expansions);
        Assert.Contains("reduce total cost of ownership", expansions);
        Assert.Equal(
            expansions.Order(StringComparer.Ordinal),
            expansions);
    }

    [Theory]
    [InlineData("rehosting", "lift and shift")]
    [InlineData("refactoring", "rearchitecting")]
    [InlineData("costs", "expenses")]
    [InlineData("health probe", "health check")]
    [InlineData("tcp", "transmission control protocol")]
    [InlineData("PaaS", "platform as a service")]
    [InlineData("Azure AD", "microsoft entra id")]
    [InlineData("availability zones", "availability zone")]
    public void Expand_MapsCommonInflections(
        string source,
        string expected)
    {
        var expansions = new BuiltInSemanticTermCatalog().Expand(source);

        Assert.Contains(expected, expansions);
    }

    [Theory]
    [InlineData("zone redundancy", "availability zone")]
    [InlineData("resource manager", "azure resource manager")]
    [InlineData("role based access control", "azure rbac")]
    public void Expand_DoesNotPromoteAmbiguousTerms(
        string source,
        string unsafeExpansion)
    {
        var expansions = new BuiltInSemanticTermCatalog().Expand(source);

        Assert.DoesNotContain(unsafeExpansion, expansions);
    }

    [Theory]
    [MemberData(nameof(InvalidGroups))]
    public void Constructor_RejectsUnsafeAliases(
        IReadOnlyList<IReadOnlyList<string>> groups)
    {
        Assert.Throws<ArgumentException>(() =>
            new BuiltInSemanticTermCatalog(groups));
    }

    public static TheoryData<IReadOnlyList<IReadOnlyList<string>>>
        InvalidGroups =>
        new()
        {
            { new IReadOnlyList<string>[] { ["", "objective"] } },
            { new IReadOnlyList<string>[] { ["go", "objective"] } },
            {
                new IReadOnlyList<string>[]
                {
                    ["goal", "objective"],
                    ["goal", "desired outcome"]
                }
            },
            { new IReadOnlyList<string>[] { ["goal", "goal"] } }
        };

    public static TheoryData<EducationalConcept> AllEducationalConcepts
    {
        get
        {
            var data = new TheoryData<EducationalConcept>();
            foreach (var concept in EducationalConceptCatalog.All)
            {
                data.Add(concept);
            }

            return data;
        }
    }
}
