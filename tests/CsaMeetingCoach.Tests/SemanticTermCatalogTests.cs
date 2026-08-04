using CsaMeetingCoach.Core;

namespace CsaMeetingCoach.Tests;

public sealed class SemanticTermCatalogTests
{
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
}
