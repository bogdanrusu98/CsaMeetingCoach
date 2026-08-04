using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace CsaMeetingCoach.Core;

public interface ISemanticTermCatalog
{
    IReadOnlyList<string> Expand(string termOrPhrase);

    IReadOnlyList<IReadOnlyList<string>> FindEquivalentTermGroups(string text);
}

public sealed partial class BuiltInSemanticTermCatalog : ISemanticTermCatalog
{
    private static readonly IReadOnlyList<IReadOnlyList<string>> BuiltInGroups =
    [
        ["objective", "goal", "desired outcome"],
        ["success criteria", "success criterion", "kpi", "acceptance criteria", "definition of done"],
        ["roi", "return on investment"],
        ["tco", "total cost of ownership"],
        ["business value", "business benefit"],
        ["risk", "blocker", "constraint", "dependency"],
        ["rehost", "rehosting", "lift and shift", "move as is"],
        ["refactor", "refactoring", "rearchitect", "rearchitecting", "code modernization"],
        ["landing zone", "cloud foundation"],
        ["identity governance", "access governance"],
        ["disaster recovery", "business continuity"],
        ["rto", "recovery time objective"],
        ["rpo", "recovery point objective"],
        ["tcp", "transmission control protocol"],
        ["udp", "user datagram protocol"],
        ["health probe", "health check"],
        ["frontend ip", "frontend ip address", "front end ip"],
        ["backend pool", "backend address pool"],
        ["external", "public"],
        ["availability zone", "zone redundancy", "zonal redundancy"],
        ["effort", "engineering effort", "implementation effort"],
        ["operations", "operational work", "ongoing support"],
        ["cost", "costs", "spend", "spending", "expense", "expenses", "budget", "budgets"]
    ];

    private readonly IReadOnlyList<IReadOnlyList<string>> groups;

    public BuiltInSemanticTermCatalog()
        : this(BuiltInGroups)
    {
    }

    internal BuiltInSemanticTermCatalog(
        IEnumerable<IReadOnlyList<string>> equivalentTermGroups)
    {
        ArgumentNullException.ThrowIfNull(equivalentTermGroups);

        var normalizedGroups = new List<IReadOnlyList<string>>();
        var owners = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var sourceGroup in equivalentTermGroups)
        {
            ArgumentNullException.ThrowIfNull(sourceGroup);
            var group = sourceGroup.Select(Normalize).ToArray();
            if (group.Length < 2)
            {
                throw new ArgumentException(
                    "Every semantic term group must contain at least two aliases.",
                    nameof(equivalentTermGroups));
            }

            for (var aliasIndex = 0; aliasIndex < group.Length; aliasIndex++)
            {
                var alias = group[aliasIndex];
                if (alias.Length < 3)
                {
                    throw new ArgumentException(
                        "Semantic aliases must contain at least three normalized characters.",
                        nameof(equivalentTermGroups));
                }

                if (owners.TryGetValue(alias, out var owner))
                {
                    var collision = owner == normalizedGroups.Count
                        ? "is duplicated in one group"
                        : "appears in multiple groups";
                    throw new ArgumentException(
                        $"Semantic alias '{alias}' {collision}.",
                        nameof(equivalentTermGroups));
                }

                owners.Add(alias, normalizedGroups.Count);
            }

            normalizedGroups.Add(Array.AsReadOnly(group));
        }

        if (normalizedGroups.Count == 0)
        {
            throw new ArgumentException(
                "At least one semantic term group is required.",
                nameof(equivalentTermGroups));
        }

        groups = normalizedGroups.AsReadOnly();
    }

    public IReadOnlyList<string> Expand(string termOrPhrase)
    {
        var normalized = Normalize(termOrPhrase);
        if (normalized.Length == 0)
        {
            return [];
        }

        var expansions = new HashSet<string>(StringComparer.Ordinal)
        {
            normalized
        };
        foreach (var group in FindEquivalentTermGroups(normalized))
        {
            var matchedAliases = group
                .Where(alias => ContainsWholePhrase(normalized, alias))
                .ToArray();
            foreach (var matchedAlias in matchedAliases)
            {
                foreach (var replacement in group)
                {
                    expansions.Add(ReplaceWholePhrase(
                        normalized,
                        matchedAlias,
                        replacement));
                }
            }
        }

        return expansions.Order(StringComparer.Ordinal).ToArray();
    }

    public IReadOnlyList<IReadOnlyList<string>> FindEquivalentTermGroups(
        string text)
    {
        var normalized = Normalize(text);
        var matches = groups
            .Select(group => new
            {
                Group = group,
                Aliases = group.Where(alias =>
                    ContainsWholePhrase(normalized, alias)).ToArray()
            })
            .Where(match => match.Aliases.Length > 0)
            .ToArray();
        return matches
            .Where(match => !matches.Any(other =>
                !ReferenceEquals(other.Group, match.Group)
                && match.Aliases.All(alias => other.Aliases.Any(otherAlias =>
                    otherAlias.Length > alias.Length
                    && ContainsWholePhrase(otherAlias, alias)))))
            .Select(match => match.Group)
            .ToArray();
    }

    private static string Normalize(string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character)
                != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return WhitespaceRegex()
            .Replace(NonWordRegex().Replace(builder.ToString(), " "), " ")
            .Trim();
    }

    private static bool ContainsWholePhrase(string text, string phrase) =>
        $" {text} ".Contains($" {phrase} ", StringComparison.Ordinal);

    private static string ReplaceWholePhrase(
        string text,
        string value,
        string replacement) =>
        $" {text} ".Replace(
                $" {value} ",
                $" {replacement} ",
                StringComparison.Ordinal)
            .Trim();

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();
}
