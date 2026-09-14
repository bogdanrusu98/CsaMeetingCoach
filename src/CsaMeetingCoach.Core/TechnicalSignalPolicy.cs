using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static class TechnicalSignalPolicy
{
    private const double SignalConfidence = 0.94;

    private static readonly ContextualSignal[] ContextualSignals =
    [
        new(
            "signal-single-vm-resilience",
            text => ContainsAny(text, "single virtual machine", "one vm")
                && ContainsAny(
                    text,
                    "downtime is unacceptable",
                    "cannot accept downtime",
                    "no downtime",
                    "high availability"),
            text => FirstPresent(text, "single virtual machine", "one vm"),
            "A single virtual machine is one failure domain. Workload availability depends on redundant instances, fault isolation, health detection, and a tested recovery path."),
        new(
            "signal-variable-traffic-capacity",
            text => ContainsAny(text, "traffic", "demand", "load")
                && ContainsAny(
                    text,
                    "changes significantly",
                    "varies significantly",
                    "traffic changes",
                    "traffic may increase",
                    "traffic can increase",
                    "traffic spikes",
                    "demand varies",
                    "variable traffic"),
            text => FirstPresent(
                text,
                "traffic changes",
                "traffic may increase",
                "traffic can increase",
                "traffic spikes",
                "variable traffic",
                "demand varies",
                "traffic"),
            "Variable demand can create either capacity shortages or persistent overprovisioning. Autoscaling decisions depend on measurable signals, safe limits, warm-up time, and scale-in behavior."),
        new(
            "signal-embedded-secrets",
            text => ContainsAny(text, "password", "passwords", "credential", "credentials", "secret", "secrets")
                && ContainsAny(
                    text,
                    "source code",
                    "application configuration",
                    "app configuration",
                    "configuration file",
                    "config file"),
            text => FirstPresent(
                text,
                "database passwords",
                "passwords",
                "credentials",
                "secrets"),
            "Credentials embedded in source code or application configuration can leak through repositories, logs, builds, and deployments. Centralized secret storage and workload identity reduce that exposure."),
        new(
            "signal-public-database-exposure",
            text => ContainsAny(text, "database", "azure sql", "sql database")
                && ContainsAny(
                    text,
                    "any public ip",
                    "every public ip",
                    "publicly accessible",
                    "accessible from the internet",
                    "open to the internet"),
            text => FirstPresent(
                text,
                "any public ip",
                "every public ip",
                "publicly accessible",
                "accessible from the internet",
                "open to the internet",
                "database"),
            "Broad public database reachability removes a network-level source restriction. Exposure risk then depends heavily on identity, firewall configuration, encryption, auditing, and continuous monitoring."),
        new(
            "signal-missing-backup",
            text => ContainsAny(text, "backup", "backups")
                && ContainsAny(
                    text,
                    "do not need",
                    "dont need",
                    "don't need",
                    "no backup",
                    "no backups",
                    "without backup",
                    "without backups"),
            text => FirstPresent(text, "backups", "backup"),
            "Cloud service availability does not replace workload backup. Recoverability also depends on retention, restore points, protected scope, and regular restore testing."),
        new(
            "signal-unbudgeted-premium-capacity",
            text => ContainsAny(text, "premium sku", "premium skus", "premium tier", "premium tiers")
                && ContainsAny(
                    text,
                    "without defining a budget",
                    "without a budget",
                    "no budget",
                    "have not defined a budget",
                    "havent defined a budget",
                    "haven't defined a budget"),
            text => FirstPresent(text, "premium skus", "premium sku", "premium tiers", "premium tier"),
            "Premium service tiers increase baseline spend before workload utilization is known. Budget thresholds, ownership, and usage evidence make cost variance visible during design and operation.")
    ];

    private static readonly RecommendationSignal[] RecommendationSignals =
    [
        new(
            text => ContainsAny(
                    text,
                    "rto",
                    "rpo",
                    "recovery time objective",
                    "recovery point objective")
                && ContainsAny(
                    text,
                    "no disaster recovery",
                    "without disaster recovery",
                    "do not have a disaster recovery",
                    "dont have a disaster recovery",
                    "don't have a disaster recovery"),
            "Define and validate the disaster-recovery design against RTO and RPO",
            "The stated recovery objectives need a documented replication, backup, failover, restore-testing, and ownership model before the workload can be considered recoverable."),
        new(
            text => ContainsAny(
                    text,
                    "one million users",
                    "million users",
                    "large user volume",
                    "high traffic",
                    "traffic may increase",
                    "traffic can increase")
                && ContainsAny(
                    text,
                    "no performance testing",
                    "no load testing",
                    "without performance testing",
                    "without load testing",
                    "performance testing is not planned",
                    "load testing is not planned"),
            "Establish performance testing and capacity targets before scale validation",
            "The expected user or traffic volume needs measurable latency, throughput, error-rate, and saturation targets backed by representative load tests."),
        new(
            text => ContainsAny(
                    text,
                    "manually creates every azure resource",
                    "manually create every azure resource",
                    "create every azure resource through the portal",
                    "creates every azure resource through the portal",
                    "deploy every azure resource manually",
                    "deploys every azure resource manually"),
            "Move repeatable Azure provisioning into infrastructure as code and CI/CD",
            "Manual portal-only provisioning increases drift and weakens review, repeatability, rollback, and environment consistency."),
        new(
            text => ContainsAny(
                    text,
                    "nobody has been assigned ownership",
                    "no one has been assigned ownership",
                    "no monitoring owner",
                    "monitoring has no owner",
                    "without monitoring ownership")
                && ContainsAny(text, "monitoring", "production", "observability"),
            "Assign production observability ownership and an actionable monitoring baseline",
            "Production monitoring needs named ownership, service-level signals, alert routing, response expectations, and regular review to remain actionable.")
    ];

    public static IReadOnlyList<ContextualCardProposal> SelectContextualCards(
        TranscriptSegment latestSegment)
    {
        if (!latestSegment.IsFinal || string.IsNullOrWhiteSpace(latestSegment.Text))
        {
            return [];
        }

        var normalized = HeuristicConversationCoachAgent.Normalize(latestSegment.Text);
        var signal = ContextualSignals.FirstOrDefault(candidate => candidate.Matches(normalized));
        if (signal is null)
        {
            return [];
        }

        return
        [
            new ContextualCardProposal(
                ContextualCardKind.Hint,
                signal.ResolveTitle(normalized),
                signal.Content,
                SignalConfidence,
                [latestSegment.Id])
            {
                ConceptKey = signal.ConceptKey
            }
        ];
    }

    public static IReadOnlyList<RecommendedTaskProposal> SelectRecommendations(
        TranscriptSegment latestSegment)
    {
        if (!latestSegment.IsFinal || string.IsNullOrWhiteSpace(latestSegment.Text))
        {
            return [];
        }

        var normalized = HeuristicConversationCoachAgent.Normalize(latestSegment.Text);
        var signal = RecommendationSignals.FirstOrDefault(candidate => candidate.Matches(normalized));
        return signal is null
            ? []
            :
            [
                new RecommendedTaskProposal(
                    signal.Title,
                    signal.Rationale,
                    SignalConfidence,
                    [latestSegment.Id])
            ];
    }

    public static bool TryValidateContextualCard(
        ContextualCardProposal proposal,
        IReadOnlyList<TranscriptSegment> sourceSegments,
        out ConceptCategory category)
    {
        category = ConceptCategory.DomainSpecific;
        var signal = ContextualSignals.FirstOrDefault(candidate =>
            string.Equals(candidate.ConceptKey, proposal.ConceptKey, StringComparison.Ordinal));
        if (signal is null
            || proposal.Kind != ContextualCardKind.Hint
            || sourceSegments.Count == 0)
        {
            return false;
        }

        var normalized = HeuristicConversationCoachAgent.Normalize(
            string.Join(' ', sourceSegments.Select(segment => segment.Text)));
        if (!signal.Matches(normalized)
            || !string.Equals(
                signal.ResolveTitle(normalized),
                HeuristicConversationCoachAgent.Normalize(proposal.Title),
                StringComparison.Ordinal)
            || !string.Equals(signal.Content, proposal.Content.Trim(), StringComparison.Ordinal))
        {
            return false;
        }

        category = signal.ConceptKey switch
        {
            "signal-embedded-secrets" => ConceptCategory.IdentitySecurity,
            "signal-public-database-exposure" => ConceptCategory.Networking,
            "signal-unbudgeted-premium-capacity" => ConceptCategory.ManagementGovernance,
            "signal-single-vm-resilience" or "signal-missing-backup" =>
                ConceptCategory.MonitoringReliability,
            "signal-variable-traffic-capacity" =>
                ConceptCategory.ComputeContainersAppPlatforms,
            _ => ConceptCategory.DomainSpecific
        };
        return true;
    }

    public static bool IsContextualSignalKey(string conceptKey) =>
        conceptKey.StartsWith("signal-", StringComparison.Ordinal);

    private static bool ContainsAny(string text, params string[] phrases) =>
        phrases.Any(phrase => ContainsPhrase(text, phrase));

    private static bool ContainsPhrase(string text, string phrase) =>
        $" {text} ".Contains(
            $" {HeuristicConversationCoachAgent.Normalize(phrase)} ",
            StringComparison.Ordinal);

    private static string FirstPresent(string text, params string[] phrases) =>
        phrases
            .Select(HeuristicConversationCoachAgent.Normalize)
            .First(phrase => ContainsPhrase(text, phrase));

    private sealed record ContextualSignal(
        string ConceptKey,
        Func<string, bool> Matches,
        Func<string, string> ResolveTitle,
        string Content);

    private sealed record RecommendationSignal(
        Func<string, bool> Matches,
        string Title,
        string Rationale);
}
