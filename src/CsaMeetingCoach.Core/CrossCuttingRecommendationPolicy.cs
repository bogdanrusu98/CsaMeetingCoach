using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static class CrossCuttingRecommendationPolicy
{
    private const double GroundedConfidence = 0.88;

    private static readonly Scenario[] Scenarios =
    [
        new(
            MatchesDatabaseMigration,
            "Assess the database move with Azure Database Migration Service, then validate Microsoft Entra managed identities and Azure Private Link",
            "The customer discussed a database migration. This task validates migration compatibility while treating identity and private connectivity as readiness checks, not assumed selections."),
        new(
            MatchesEnterpriseAi,
            "Prototype with Microsoft Foundry and Azure AI Search, then validate Microsoft Entra ID permission boundaries",
            "The customer discussed an enterprise AI workload. This task combines the primary AI and retrieval path with an explicit identity-boundary validation before production use."),
        new(
            MatchesKubernetes,
            "Assess AKS, then validate Microsoft Entra workload identity and Defender for Containers",
            "The customer discussed a Kubernetes platform. This task validates cluster fit, workload identity, and container security operations without assuming production readiness."),
        new(
            MatchesContainers,
            "Assess Azure Container Apps, then validate Microsoft Entra workload identity and Azure Monitor",
            "The customer discussed a containerized workload. This task validates a managed container platform together with workload identity and operational visibility."),
        new(
            MatchesHybridOperations,
            "Assess Azure Arc, Azure Policy, and Defender for Cloud for the hybrid operating scope",
            "The customer discussed resources that remain outside Azure. This task validates hybrid inventory, governance, and security operations without claiming that Azure Arc migrates or hosts those resources."),
        new(
            MatchesDisasterRecovery,
            "Validate Azure Site Recovery and Azure Backup against the recovery objectives, then define Azure Monitor alerts",
            "The customer discussed recovery requirements. This task ties replication, backup, and operational detection to documented RTO, RPO, restore, and ownership expectations."),
        new(
            MatchesMigration,
            "Assess a representative wave with Azure Migrate, then validate Microsoft Entra ID and Defender for Cloud controls",
            "The customer discussed a migration. This task establishes workload and dependency readiness while treating identity and security controls as validations for the target landing zone."),
        new(
            MatchesApplicationModernization,
            "Compare Azure App Service with Azure Container Apps, then validate Microsoft Entra ID access",
            "The customer discussed application modernization. This task evaluates managed hosting fit and validates identity requirements without assuming either platform has already been selected."),
        new(
            MatchesAzureSql,
            "Assess Azure SQL fit, then validate Microsoft Entra managed identities and Azure Private Link",
            "The customer discussed a SQL workload. This task combines database fit with explicit identity and private-connectivity readiness checks.")
    ];

    public static CoachAgentDecision Apply(
        CoachAgentDecision decision,
        TranscriptSegment latestSegment)
    {
        var normalizedText = HeuristicConversationCoachAgent.Normalize(latestSegment.Text);
        var scenario = Scenarios.FirstOrDefault(item => item.Matches(normalizedText));
        if (scenario is null)
        {
            return decision;
        }

        var replacement = new RecommendedTaskProposal(
            scenario.Title,
            scenario.Rationale,
            GroundedConfidence,
            [latestSegment.Id]);
        return decision with { RecommendedTasks = [replacement] };
    }

    private static bool MatchesDatabaseMigration(string text) =>
        MatchesMigration(text)
        && ContainsAny(text, "database", "databases", "sql", "postgresql", "mysql")
        && !IsExplicitlyExcluded(text, "database migration");

    private static bool MatchesMigration(string text) =>
        ContainsAny(
            text,
            "migration",
            "migrate",
            "migrating",
            "move to azure",
            "moving to azure",
            "rehost",
            "rehosting",
            "lift and shift")
        && !ContainsAny(
            text,
            "no migration",
            "not migrating",
            "do not migrate",
            "dont migrate",
            "don't migrate",
            "will not migrate",
            "wont migrate",
            "won't migrate",
            "migration is not in scope",
            "migration is out of scope")
        && !IsExplicitlyExcluded(text, "migration");

    private static bool MatchesEnterpriseAi(string text) =>
        ContainsAny(
            text,
            "enterprise rag",
            "rag application",
            "rag architecture",
            "rag assistant",
            "rag solution",
            "rag system",
            "rag workload",
            "retrieval augmented generation",
            "generative ai",
            "ai assistant",
            "ai agent")
        && !IsExplicitlyExcluded(
            text,
            "ai",
            "rag",
            "generative ai",
            "ai assistant",
            "ai agent");

    private static bool MatchesKubernetes(string text) =>
        ContainsAny(text, "kubernetes", "aks")
        && !IsExplicitlyExcluded(text, "kubernetes", "aks");

    private static bool MatchesContainers(string text) =>
        ContainsAny(
            text,
            "container",
            "containers",
            "containerized",
            "microservice",
            "microservices")
        && !IsExplicitlyExcluded(
            text,
            "container",
            "containers",
            "container platform",
            "microservices");

    private static bool MatchesHybridOperations(string text) =>
        ContainsAny(
            text,
            "hybrid",
            "multi cloud",
            "multicloud",
            "remain on premises",
            "remain on premise",
            "remain on prem",
            "stay on premises",
            "stay on premise",
            "stay on prem")
        && !ContainsAny(
            text,
            "not hybrid",
            "will not remain on premises",
            "wont remain on premises",
            "won't remain on premises",
            "will not remain on prem",
            "wont remain on prem",
            "won't remain on prem")
        && !IsExplicitlyExcluded(
            text,
            "hybrid",
            "multi cloud",
            "multicloud",
            "azure arc");

    private static bool MatchesDisasterRecovery(string text) =>
        ContainsAny(
            text,
            "disaster recovery",
            "business continuity",
            "rto",
            "rpo",
            "recovery time objective",
            "recovery point objective")
        && !IsExplicitlyExcluded(
            text,
            "disaster recovery",
            "business continuity",
            "rto",
            "rpo",
            "recovery time objective",
            "recovery point objective");

    private static bool MatchesApplicationModernization(string text) =>
        ContainsAny(text, "modernize", "modernizing", "modernization")
        && ContainsAny(text, "application", "applications", "web app", "web application", "api")
        && !ContainsAny(
            text,
            "not modernizing",
            "do not modernize",
            "dont modernize",
            "don't modernize",
            "will not modernize",
            "wont modernize",
            "won't modernize")
        && !IsExplicitlyExcluded(text, "modernization", "application modernization");

    private static bool MatchesAzureSql(string text) =>
        ContainsAny(text, "azure sql", "sql database", "sql managed instance")
        && !IsExplicitlyExcluded(
            text,
            "azure sql",
            "sql database",
            "sql managed instance");

    private static bool IsExplicitlyExcluded(
        string text,
        params string[] subjects)
    {
        foreach (var subject in subjects)
        {
            if (ContainsAny(
                text,
                $"{subject} is out of scope",
                $"{subject} is not in scope",
                $"{subject} isnt in scope",
                $"{subject} isn't in scope",
                $"{subject} is not required",
                $"{subject} are not required",
                $"do not use {subject}",
                $"dont use {subject}",
                $"don't use {subject}",
                $"will not use {subject}",
                $"wont use {subject}",
                $"won't use {subject}",
                $"not using {subject}"))
            {
                return true;
            }
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] phrases) =>
        phrases.Any(phrase => ContainsPhrase(text, phrase));

    private static bool ContainsPhrase(string text, string phrase) =>
        $" {text} ".Contains(
            $" {HeuristicConversationCoachAgent.Normalize(phrase)} ",
            StringComparison.Ordinal);

    private sealed record Scenario(
        Func<string, bool> Matches,
        string Title,
        string Rationale);
}
