using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed record AudienceFamiliarityBehavior(
    string DisplayName,
    string DefinitionScope,
    string HintDepth,
    string AlertCadence);

public static class AudienceFamiliarityPolicy
{
    private static readonly HashSet<string> FamiliarFoundations =
        new(StringComparer.Ordinal)
        {
            "cloud-computing",
            "shared-responsibility-model",
            "infrastructure-as-a-service",
            "platform-as-a-service",
            "software-as-a-service",
            "public-cloud",
            "private-cloud",
            "hybrid-cloud"
        };

    public static AudienceFamiliarityBehavior For(AudienceFamiliarity familiarity) =>
        familiarity switch
        {
            AudienceFamiliarity.Beginner => new(
                "Beginner",
                "Explain foundational terms, acronyms, and specialized concepts when they first appear.",
                "Use plain language and concrete examples, prerequisites, or simple distinctions.",
                "Prefer one useful card for each newly introduced concept, without repeating a concept."),
            AudienceFamiliarity.Familiar => new(
                "Familiar",
                "Omit widely understood foundations and explain specialized, ambiguous, or domain-specific terms.",
                "Focus on mechanisms, practical implications, distinctions, and limitations.",
                "Return cards selectively when they materially improve understanding."),
            AudienceFamiliarity.Expert => new(
                "Expert",
                "Do not define basic or commonly used domain terms. Cover only rare, ambiguous, novel, or session-specific concepts.",
                "Prefer concise technical hints about edge cases, constraints, trade-offs, or validation implications.",
                "Return at most one card and only when it adds non-obvious value."),
            _ => throw new ArgumentOutOfRangeException(nameof(familiarity), familiarity, null)
        };

    public static bool IncludeCatalogConcept(
        EducationalConcept concept,
        AudienceFamiliarity familiarity) =>
        familiarity switch
        {
            AudienceFamiliarity.Beginner => true,
            AudienceFamiliarity.Familiar =>
                !FamiliarFoundations.Contains(concept.ConceptKey),
            AudienceFamiliarity.Expert => false,
            _ => false
        };
}
