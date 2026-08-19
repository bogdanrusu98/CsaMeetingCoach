using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed partial class MeetingChecklistPlanner : IMeetingChecklistPlanner
{
    public IReadOnlyList<ChecklistItemState> CreateChecklist(
        MeetingPurpose purpose,
        IReadOnlyList<ChecklistSeed>? requestedChecklist,
        SessionTemplateKind template = SessionTemplateKind.CsaVbd)
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
            seeds = CreateDefaultSeeds(purpose, template);
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

    private static IReadOnlyList<ChecklistSeed> CreateDefaultSeeds(
        MeetingPurpose purpose,
        SessionTemplateKind template)
    {
        return template switch
        {
            SessionTemplateKind.Presentation => CreatePresentationSeeds(purpose),
            SessionTemplateKind.Workshop => CreateWorkshopSeeds(purpose),
            SessionTemplateKind.Training => CreateTrainingSeeds(purpose),
            SessionTemplateKind.Custom => CreateCustomSeeds(purpose),
            SessionTemplateKind.CsaVbd => CreateCsaSeeds(purpose),
            _ => throw new ArgumentException(
                "A supported session template is required.",
                nameof(template))
        };
    }

    private static IReadOnlyList<ChecklistSeed> CreatePresentationSeeds(
        MeetingPurpose purpose) =>
    [
        new(
            "Frame the audience outcome",
            "The presenter explicitly states what the audience should understand or decide.",
            BuildHints(purpose.Objective, "objective", "today", "audience", "understand", "decide")),
        new(
            "Cover the key messages",
            "The discussion explicitly covers the planned messages or success criteria.",
            BuildHints(string.Join(' ', purpose.SuccessCriteria), "key point", "important", "takeaway")),
        new(
            "Address questions and constraints",
            "An audience question, concern, limitation, or constraint is explicitly addressed.",
            ["question", "concern", "constraint", "limitation", "risk", "întrebare", "limitare"]),
        new(
            "Close with the intended action",
            "The presenter explicitly summarizes the takeaway, decision, or next action.",
            ["summary", "takeaway", "decision", "next step", "action", "conclusion"])
    ];

    private static IReadOnlyList<ChecklistSeed> CreateWorkshopSeeds(
        MeetingPurpose purpose) =>
    [
        new(
            "Confirm the workshop outcome",
            "Participants explicitly align on the outcome the workshop must produce.",
            BuildHints(purpose.Objective, "outcome", "objective", "deliverable", "align")),
        new(
            "Establish assumptions and constraints",
            "A relevant assumption, dependency, or constraint is explicitly discussed.",
            ["assumption", "dependency", "constraint", "boundary", "requirement", "limit"]),
        new(
            "Capture decisions",
            "A concrete workshop decision or agreed direction is explicitly stated.",
            ["decision", "decided", "agreed", "selected", "direction", "we will"]),
        new(
            "Assign open actions",
            "An unresolved question or follow-up action has an explicit owner or timing.",
            ["open question", "action", "owner", "deadline", "follow up", "next step"])
    ];

    private static IReadOnlyList<ChecklistSeed> CreateTrainingSeeds(
        MeetingPurpose purpose) =>
    [
        new(
            "State the learning objectives",
            "The trainer explicitly states what learners should know or be able to do.",
            BuildHints(purpose.Objective, "learn", "objective", "understand", "be able to")),
        new(
            "Explain the core concepts",
            "The planned concepts or success criteria are explicitly explained.",
            BuildHints(string.Join(' ', purpose.SuccessCriteria), "concept", "example", "demonstrate")),
        new(
            "Check learner understanding",
            "A comprehension check, learner question, or practice activity is explicitly discussed.",
            ["question", "understand", "practice", "exercise", "quiz", "example"]),
        new(
            "Summarize takeaways and resources",
            "The trainer explicitly summarizes key takeaways, resources, or next learning actions.",
            ["summary", "takeaway", "resource", "next", "practice", "reference"])
    ];

    private static IReadOnlyList<ChecklistSeed> CreateCustomSeeds(
        MeetingPurpose purpose) =>
    [
        new(
            "Confirm the session objective",
            "The session objective is explicitly stated or confirmed.",
            BuildHints(purpose.Objective, "objective", "goal", "purpose")),
        new(
            "Cover the success criteria",
            "At least one configured success criterion is explicitly discussed.",
            BuildHints(string.Join(' ', purpose.SuccessCriteria), "success", "result", "outcome")),
        new(
            "Capture open questions",
            "An open question, concern, blocker, or constraint is explicitly discussed.",
            ["question", "concern", "blocker", "constraint", "risk"]),
        new(
            "Confirm the next action",
            "A concrete next action, owner, or timing is explicitly stated.",
            ["next step", "action", "owner", "deadline", "we will"])
    ];

    private static IReadOnlyList<ChecklistSeed> CreateCsaSeeds(MeetingPurpose purpose)
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
