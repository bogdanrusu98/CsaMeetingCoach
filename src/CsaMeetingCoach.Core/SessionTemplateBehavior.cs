using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed record SessionTemplateBehavior(
    string DisplayName,
    string HostRole,
    string RecommendationFocus,
    string MemberAlertFocus,
    string CompletionFocus);

public static class SessionTemplateBehaviors
{
    public static SessionTemplateBehavior For(SessionTemplateKind template)
    {
        return template switch
        {
            SessionTemplateKind.Presentation => new(
                "Presentation",
                "Presenter",
                "Strengthen narrative clarity, message sequencing, audience relevance, examples, transitions, question handling, and the final takeaway.",
                "Help the audience follow the presentation through concise definitions, distinctions, implications, and context for the concept currently being presented.",
                "Track the intended audience outcome, key messages, addressed questions or constraints, and a clear closing action."),
            SessionTemplateKind.Workshop => new(
                "Workshop",
                "Facilitator",
                "Increase participation, surface assumptions and trade-offs, keep the group on the workshop outcome, and turn discussion into explicit decisions, owners, and unresolved items.",
                "Help participants contribute to decisions by clarifying workshop terminology, assumptions, constraints, options, and trade-offs already present in the discussion.",
                "Track the shared outcome, surfaced perspectives, explicit decisions, unresolved questions, owners, and follow-up actions."),
            SessionTemplateKind.Training => new(
                "Training",
                "Trainer",
                "Improve learning through clear explanations, examples, demonstrations, practice, understanding checks, misconception correction, and recap.",
                "Support learning with definitions, concrete examples, prerequisites, concept distinctions, mechanisms, and common pitfalls grounded in the lesson currently being taught.",
                "Track learning objectives, covered concepts, demonstrations or examples, understanding checks, recap, and learner resources."),
            SessionTemplateKind.Custom => new(
                "Custom",
                "Session host",
                "Follow the host-configured objective and success criteria. Use session knowledge to identify the most valuable next step without importing assumptions from another template.",
                "Explain only concepts that help members follow the configured objective, success criteria, discussion, and member-eligible session knowledge.",
                "Track only the configured objective and success criteria, using explicit discussion evidence."),
            SessionTemplateKind.CsaVbd => new(
                "CSA / VBD",
                "Customer-facing CSA",
                "Advance discovery, solution fit, measurable business value, technical and commercial risks, Azure decision signals, and owned customer next actions.",
                "Help the client understand discussed business and Azure concepts through grounded definitions, mechanisms, distinctions, implications, and limitations.",
                "Track customer objectives, measurable value, constraints and risks, solution fit, ownership, and concrete next actions."),
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, null)
        };
    }
}
