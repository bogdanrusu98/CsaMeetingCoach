using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed partial class MeetingChecklistPlanner : IMeetingChecklistPlanner
{
    public IReadOnlyList<ChecklistItemState> CreateChecklist(
        MeetingPurpose purpose,
        IReadOnlyList<ChecklistSeed>? requestedChecklist)
    {
        ArgumentNullException.ThrowIfNull(purpose);

        if (string.IsNullOrWhiteSpace(purpose.Title)
            || string.IsNullOrWhiteSpace(purpose.MeetingType)
            || string.IsNullOrWhiteSpace(purpose.Objective)
            || purpose.SuccessCriteria is null)
        {
            throw new ArgumentException(
                "Meeting title, type, objective, and success criteria are required.",
                nameof(purpose));
        }

        IReadOnlyList<ChecklistSeed> seeds;
        if (requestedChecklist is { Count: > 0 })
        {
            ValidateRequestedChecklist(requestedChecklist);
            seeds = requestedChecklist;
        }
        else
        {
            seeds = CreateDefaultSeeds(purpose);
        }

        return seeds
            .Where(seed => !string.IsNullOrWhiteSpace(seed.Title))
            .Select(seed => new ChecklistItemState(
                Guid.NewGuid(),
                seed.Title.Trim(),
                seed.CompletionCriteria.Trim(),
                seed.EvidenceHints
                    .Where(hint => !string.IsNullOrWhiteSpace(hint))
                    .Select(hint => hint.Trim())
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .ToArray(),
                ChecklistItemStatus.Pending,
                AutoCompleted: false,
                Confidence: null,
                CompletionReason: null,
                CompletedAtUtc: null,
                Evidence: []))
            .ToArray();
    }

    private static void ValidateRequestedChecklist(IReadOnlyList<ChecklistSeed> checklist)
    {
        if (checklist.Any(seed =>
                seed is null
                || string.IsNullOrWhiteSpace(seed.Title)
                || string.IsNullOrWhiteSpace(seed.CompletionCriteria)
                || seed.EvidenceHints is null
                || seed.EvidenceHints.Any(string.IsNullOrWhiteSpace)))
        {
            throw new ArgumentException(
                "Each checklist item requires a title, completion criteria, and valid evidence hints.",
                nameof(checklist));
        }
    }

    private static IReadOnlyList<ChecklistSeed> CreateDefaultSeeds(MeetingPurpose purpose)
    {
        var seeds = new List<ChecklistSeed>
        {
            new(
                "Clarify the customer objective",
                "The discussion explicitly confirms the business or technical objective.",
                BuildHints(purpose.Objective, "objective", "goal", "obiectiv", "scop", "dorim")),
            new(
                "Validate success criteria",
                "At least one measurable outcome or success criterion is discussed.",
                BuildHints(
                    string.Join(' ', purpose.SuccessCriteria),
                    "success criteria",
                    "success means",
                    "success is measured",
                    "outcome",
                    "metric",
                    "target",
                    "KPI",
                    "percent",
                    "criteriu",
                    "rezultat")),
            new(
                "Capture risks and open questions",
                "A risk, blocker, constraint, concern, or unresolved question is explicitly discussed.",
                ["risk", "blocker", "constraint", "concern", "open question", "risc", "blocaj", "limitare", "întrebare"]),
            new(
                "Agree next steps, owners, and timing",
                "The discussion includes a concrete commitment, owner, or due date.",
                ["next step", "follow-up", "owner", "deadline", "we will", "I will", "următorul pas", "responsabil", "vom", "voi", "până la"])
        };

        if (purpose.MeetingType.Contains("VBD", StringComparison.OrdinalIgnoreCase))
        {
            seeds.Insert(2, new ChecklistSeed(
                "Connect the solution to business value",
                "The proposed solution is connected to impact, value, ROI, cost, or business outcomes.",
                ["business value", "impact", "ROI", "TCO", "cost", "valoare", "beneficiu", "economii"]));
        }

        return seeds;
    }

    private static IReadOnlyList<string> BuildHints(string source, params string[] fixedHints)
    {
        var meaningfulWords = WordRegex()
            .Matches(source)
            .Select(match => match.Value)
            .Where(word => word.Length >= 5)
            .Take(8);

        return fixedHints
            .Concat(meaningfulWords)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
