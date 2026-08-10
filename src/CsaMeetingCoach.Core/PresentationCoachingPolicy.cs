using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

internal static partial class PresentationCoachingPolicy
{
    private const string SentenceBoundaries = ".?!;\r\n";
    private const string InstructionActionPattern =
        @"(?:ask|assess|check|choose|clarify|compare|configure|confirm|consider|create|define|demonstrate|deploy|describe|discuss|enable|ensure|evaluate|explain|focus\s+on|highlight|identify|keep\s+in\s+mind|look\s+at|mention|note|point\s+out|recommend|review|select|set|show|tell|think\s+about|understand|use|validate|verify|walk\s+through)";
    private const string OptionalConnectivePrefixPattern =
        @"(?:(?:(?:and|but|so|then|also|next|now|therefore|however)(?:,\s*|\s+))|(?:after\s+that(?:,\s*|\s+)))*";
    private const string RequiredConnectivePrefixPattern =
        @"(?:(?:(?:and|but|so|then|also|next|now|therefore|however)(?:,\s*|\s+))|(?:after\s+that(?:,\s*|\s+)))+";
    private const string QuestionStarterPattern =
        @"(?:who|what|when|where|why|how|which|can|could|should|would|will|do|does|did|is|are|was|were|has|have|had|question)";

    private static readonly HashSet<string> ComparisonStopWords =
    [
        "a",
        "an",
        "and",
        "for",
        "in",
        "of",
        "on",
        "the",
        "to",
        "with"
    ];

    private static readonly string[] GenericPresentationTerms =
    [
        "accountable",
        "business outcome",
        "customer requirement",
        "follow up",
        "meeting objective",
        "next step",
        "owner",
        "production requirement",
        "success criteria"
    ];

    private static readonly string[] AzureContextTerms =
    [
        "azure",
        "microsoft entra",
        "entra id",
        "azure active directory",
        "azure ad",
        "arm template"
    ];

    private static readonly string[] ConflictingPlatformTerms =
    [
        "aws",
        "amazon web services",
        "gcp",
        "google cloud",
        "google cloud platform",
        "kubernetes",
        "openshift"
    ];

    private static readonly string[] AzureSpecificConceptTerms =
    [
        "availability zone",
        "availability zones",
        "management group",
        "management groups",
        "resource group",
        "resource groups",
        "resource manager",
        "role based access control",
        "rbac"
    ];

    private static readonly HashSet<string> AzureEvidenceAnchorStopWords =
    [
        "align",
        "assess",
        "azure",
        "choice",
        "choices",
        "connect",
        "control",
        "controls",
        "customer",
        "customers",
        "decision",
        "decisions",
        "define",
        "design",
        "discuss",
        "explain",
        "map",
        "microsoft",
        "requirement",
        "requirements",
        "resource",
        "resources",
        "review",
        "service",
        "services",
        "validate",
        "workload",
        "workloads"
    ];

    private static readonly string[][] AzureEvidenceConceptGroups =
    [
        ["availability zone", "availability zones"],
        ["management group", "management groups"],
        ["resource group", "resource groups"],
        ["resource manager", "arm template", "arm templates"],
        ["role based access control", "rbac"],
        ["load balancer"],
        ["subscription", "subscriptions"],
        ["region", "regions"],
        ["entra id", "azure active directory", "azure ad"]
    ];

    private static readonly PresentationRecommendationDefinition[]
        PresentationRecommendationCatalog =
    [
        new(
            ["Azure Load Balancer", "load balancer"],
            "Validate Azure Load Balancer traffic, health, and availability choices",
            "Turn the component overview into customer-specific design decisions for exposure, traffic rules, probe behavior, zone resilience, and outbound connectivity.",
            0.91,
            ["load balancer", "health probe"],
            [
                "availability",
                "backend",
                "exposure",
                "floating ip",
                "frontend",
                "internal",
                "nat",
                "outbound",
                "probe",
                "public",
                "session",
                "sku",
                "tcp",
                "traffic",
                "udp",
                "zone"
            ],
            RequiresAzureContext: true),
        new(
            [
                "cloud computing",
                "shared responsibility model",
                "shared responsibility",
                "Infrastructure as a Service",
                "IaaS",
                "Platform as a Service",
                "PaaS",
                "Software as a Service",
                "SaaS"
            ],
            "Connect cloud service models to control and operational responsibility",
            "Relate the service model being presented to customer control, provider responsibility, security ownership, and the operational effort the workload requires.",
            0.90,
            ["cloud computing", "shared responsibility", "service model", "iaas", "paas", "saas"],
            ["control", "customer", "operations", "provider", "responsibility", "security"]),
        new(
            [
                "Azure Resource Manager",
                "ARM template",
                "ARM templates",
                "management group",
                "management groups",
                "Azure subscription",
                "Azure subscriptions",
                "resource group",
                "resource groups"
            ],
            "Map Azure management scopes to governance, access, cost, and workload lifecycles",
            "Connect the management hierarchy to inherited policy and access, environment isolation, cost ownership, and the lifecycle boundaries of the customer's workloads.",
            0.90,
            [
                "azure resource manager",
                "arm template",
                "management group",
                "azure subscription",
                "resource group"
            ],
            ["access", "cost", "governance", "hierarchy", "lifecycle", "policy", "scope"],
            RequiresAzureContext: true),
        new(
            [
                "Azure region",
                "Azure regions",
                "Azure Availability Zone",
                "Azure Availability Zones",
                "availability zone",
                "availability zones"
            ],
            "Align Azure region and availability-zone design with residency, latency, and resilience",
            "Translate the location concepts into customer decisions about data residency, user latency, service availability, fault isolation, and recovery across zones or regions.",
            0.90,
            ["azure region", "availability zone"],
            ["availability", "data residency", "fault isolation", "latency", "recovery", "resilience"],
            RequiresAzureContext: true),
        new(
            [
                "Microsoft Entra ID",
                "Entra ID",
                "Azure Active Directory",
                "Azure AD",
                "Azure RBAC",
                "Azure role-based access control",
                "role-based access control",
                "role based access control"
            ],
            "Connect Microsoft Entra ID authentication to Azure RBAC authorization scope",
            "Separate identity verification from resource authorization, then relate principals, roles, and scope to least-privilege access for the customer's users and workloads.",
            0.90,
            [
                "microsoft entra id",
                "entra id",
                "azure active directory",
                "azure ad",
                "azure rbac",
                "role based access control"
            ],
            ["authentication", "authorization", "identity", "least privilege", "principal", "role", "scope"],
            RequiresAzureContext: true)
    ];

    public static IReadOnlyList<RecommendedTaskProposal> SelectRecommendations(
        CoachAgentContext context,
        IReadOnlyList<RecommendedTaskProposal> primaryRecommendations,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        var analysisWindowIds = analysisWindow
            .Select(segment => segment.Id)
            .ToHashSet();
        var coveredContext = context.Checklist
            .SelectMany(item => new[] { item.Title, item.CompletionCriteria })
            .Concat(context.Purpose.SuccessCriteria)
            .Concat((context.RecommendedTasks ?? []).Select(item => item.Title))
            .Select(HeuristicConversationCoachAgent.Normalize)
            .Where(value => value.Length > 0)
            .ToArray();
        var presentationMatch = FindRecommendationMatches(
                analysisWindow,
                HasAzureMeetingContext(context.Purpose))
            .FirstOrDefault(match => !coveredContext.Any(covered =>
                HasSubstantialOverlap(
                    HeuristicConversationCoachAgent.Normalize(
                        match.Definition.Title),
                    covered)));
        var safePrimary = primaryRecommendations
            .Where(proposal => proposal is not null)
            .Where(proposal =>
            {
                var title = HeuristicConversationCoachAgent.Normalize(proposal.Title);
                return title.Length is > 0 and <= 180
                    && !string.IsNullOrWhiteSpace(proposal.Rationale)
                    && proposal.Rationale.Trim().Length <= 600
                    && double.IsFinite(proposal.Confidence)
                    && proposal.Confidence is >= 0.70 and <= 1
                    && proposal.SourceTranscriptSegmentIds is { Count: > 0 }
                    && proposal.SourceTranscriptSegmentIds.All(
                        analysisWindowIds.Contains)
                    && (!RequiresAzureEvidenceForOutput(
                            $"{proposal.Title} {proposal.Rationale}")
                        || HasAzureEvidence(
                            $"{proposal.Title} {proposal.Rationale}",
                            analysisWindow,
                            proposal.SourceTranscriptSegmentIds))
                    && (presentationMatch is null
                        || IsTechnicalRecommendationTitle(
                            title,
                            presentationMatch.Definition))
                    && !coveredContext.Any(covered =>
                        HasSubstantialOverlap(title, covered));
            })
            .Take(1)
            .ToArray();
        if (safePrimary.Length > 0)
        {
            return safePrimary;
        }

        if (presentationMatch is null)
        {
            return [];
        }

        return
        [
            new RecommendedTaskProposal(
                presentationMatch.Definition.Title,
                presentationMatch.Definition.Rationale,
                presentationMatch.Definition.Confidence,
                presentationMatch.Mention.SourceTranscriptSegmentIds)
        ];
    }

    public static IReadOnlyList<ContextualCardProposal> SelectContextualCards(
        CoachAgentContext context,
        IReadOnlyList<ContextualCardProposal> primaryCards,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        var analysisWindowById = analysisWindow.ToDictionary(segment => segment.Id);
        var analysisWindowIndexById = analysisWindow
            .Select((segment, index) => (segment.Id, Index: index))
            .ToDictionary(item => item.Id, item => item.Index);
        var validPrimaryCards = primaryCards
            .Where(card => card is not null
                && Enum.IsDefined(card.Kind)
                && !string.IsNullOrWhiteSpace(card.Title)
                && card.Title.Trim().Length <= 80
                && !string.IsNullOrWhiteSpace(card.Content)
                && card.Content.Trim().Length <= 320
                && IsClientReadyExplanation(card.Title, card.Content)
                && double.IsFinite(card.Confidence)
                && card.Confidence is >= 0.75 and <= 1
                && card.SourceTranscriptSegmentIds is { Count: > 0 }
                && card.SourceTranscriptSegmentIds.All(analysisWindowById.ContainsKey)
                && (!RequiresAzureEvidenceForOutput(
                        $"{card.Title} {card.Content}")
                    || HasAzureEvidence(
                        $"{card.Title} {card.Content}",
                        analysisWindow,
                        card.SourceTranscriptSegmentIds))
                && IsTitleGrounded(
                    card.Title,
                    card.SourceTranscriptSegmentIds,
                    analysisWindow))
            .ToArray();
        var knownTitles = (context.ContextualCards ?? [])
            .Where(card => IsClientReadyExplanation(card.Title, card.Content))
            .Select(card => HeuristicConversationCoachAgent.Normalize(card.Title))
            .ToHashSet(StringComparer.Ordinal);
        var knownConceptKinds = (context.ContextualCards ?? [])
            .Select(card => (ConceptKey: TryResolveConceptKey(card), card.Kind))
            .Where(entry => entry.ConceptKey is not null)
            .Select(entry => $"{entry.ConceptKey}:{entry.Kind}")
            .ToHashSet(StringComparer.Ordinal);
        var result = new List<ContextualCardProposal>();
        foreach (var card in validPrimaryCards)
        {
            var resolvedConceptKey = EducationalConceptCatalog.TryResolveByAliasOrTitle(
                    card.ConceptKey ?? card.Title,
                    out var concept)
                ? concept.ConceptKey
                : null;
            if (resolvedConceptKey is not null
                && knownConceptKinds.Contains($"{resolvedConceptKey}:{card.Kind}"))
            {
                continue;
            }

            var normalizedTitle = HeuristicConversationCoachAgent.Normalize(card.Title);
            if (resolvedConceptKey is null
                && knownTitles.Any(title => HasSubstantialOverlap(
                    normalizedTitle,
                    title)))
            {
                continue;
            }

            knownTitles.Add(normalizedTitle);
            if (resolvedConceptKey is not null)
            {
                knownConceptKinds.Add($"{resolvedConceptKey}:{card.Kind}");
            }
            result.Add(card);
            if (result.Count == 2)
            {
                break;
            }
        }

        var educationalMatches = EducationalConceptCatalog.All
            .Select(concept => (
                Concept: concept,
                Mention: TryFindEducationalMention(
                    analysisWindow,
                    concept,
                    HasAzureMeetingContext(context.Purpose),
                    out _,
                    out _)))
            .Where(match => match.Mention is not null)
            .OrderByDescending(match => match.Mention!.SourceTranscriptSegmentIds
                .Select(id => analysisWindowIndexById[id])
                .Max())
            .ThenByDescending(match => SignificantTerms(match.Mention!.Title).Count)
            .ThenByDescending(match => HeuristicConversationCoachAgent.Normalize(
                match.Mention!.Title).Length)
            .ThenBy(match => match.Concept.ConceptKey, StringComparer.Ordinal);
        foreach (var match in educationalMatches)
        {
            AddEducationalConceptCard(
                result,
                knownConceptKinds,
                knownTitles,
                context.ContextualCards ?? [],
                match.Concept,
                match.Mention!);
        }

        return result.Take(2).ToArray();
    }

    internal static bool IsTitleGrounded(
        string title,
        IReadOnlyList<Guid> sourceTranscriptSegmentIds,
        IReadOnlyList<TranscriptSegment> analysisWindow)
    {
        var sourceIds = sourceTranscriptSegmentIds.ToHashSet();
        var sources = analysisWindow
            .Select((segment, index) => (Segment: segment, Index: index))
            .Where(candidate => sourceIds.Contains(candidate.Segment.Id))
            .ToArray();
        if (sources.Any(source => ContainsWholeTerm(
                source.Segment.Text,
                title.Trim())))
        {
            return true;
        }

        return sources.Length is >= 2 and <= 3
            && sources.Select(source => source.Segment.Id).ToHashSet()
                .SetEquals(sourceIds)
            && sources
                .Zip(sources.Skip(1))
                .All(pair => pair.Second.Index == pair.First.Index + 1)
            && ContainsWholeTerm(
                string.Join(
                    ' ',
                    sources.Select(source => source.Segment.Text.Trim())),
                title.Trim());
    }

    internal static bool HasSubstantialOverlap(string left, string right)
    {
        if (left.Equals(right, StringComparison.Ordinal))
        {
            return true;
        }

        var leftTerms = SignificantTerms(left);
        var rightTerms = SignificantTerms(right);
        if (leftTerms.Count < 2 || rightTerms.Count < 2)
        {
            return false;
        }

        if (left.Contains(right, StringComparison.Ordinal)
            || right.Contains(left, StringComparison.Ordinal))
        {
            return true;
        }

        var shared = leftTerms.Count(rightTerms.Contains);
        return shared >= 2
            && shared / (double)Math.Min(leftTerms.Count, rightTerms.Count) >= 0.75;
    }

    internal static bool IsClientReadyExplanation(string title, string content)
    {
        return !IsQuestionShaped(title)
            && !IsQuestionShaped(content)
            && !IsInstructionShaped(title)
            && !IsInstructionShaped(content);
    }

    private static bool IsQuestionShaped(string value)
    {
        return value.Contains('?')
            || SplitClauses(value).Any(clause => Regex.IsMatch(
                clause,
                $@"^{OptionalConnectivePrefixPattern}{QuestionStarterPattern}(?:\s|$)",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant));
    }

    private static bool IsInstructionShaped(string value)
    {
        return SplitClauses(value).Any(IsInstructionClause);
    }

    private static bool IsInstructionClause(string clause)
    {
        const string humanSubject =
            @"(?:we|you|i|(?:(?:the|our|your|a)\s+)?(?:csa|presenter|client|clients|customer|customers|audience|user|users|team|teams|organization|organizations|organisation|organisations))";
        const string taskModal =
            @"(?:should|must|can|could|need(?:s)?\s+to|ought\s+to|have\s+to|has\s+to|(?:is|are)\s+required\s+to)";
        if (Regex.IsMatch(
                clause,
                $@"^{OptionalConnectivePrefixPattern}(?:please\s+)?{InstructionActionPattern}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant))
        {
            return true;
        }

        return Regex.IsMatch(
                clause,
                $@"\b{humanSubject}\s+{taskModal}\s+[\p{{L}}][\p{{L}}\p{{N}}-]*\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                clause,
                $@"^{OptionalConnectivePrefixPattern}(?:remember|try|be\s+sure|make\s+sure)\s+to\s+{InstructionActionPattern}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                clause,
                $@"^{OptionalConnectivePrefixPattern}(?:it\s+is|it's)\s+(?:important|useful|helpful|recommended|best)\s+to\s+{InstructionActionPattern}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                clause,
                $@"\b(?:next\s+step|action|recommendation)\s+is\s+to\s+{InstructionActionPattern}\b",
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }

    private static IEnumerable<string> SplitClauses(string value)
    {
        var boundaries =
            $@"[.!;:\r\n]+|,\s*(?=(?:{QuestionStarterPattern}(?:\s|$)|{RequiredConnectivePrefixPattern}(?:{QuestionStarterPattern}(?:\s|$)|{InstructionActionPattern}\b)))|\s+(?={RequiredConnectivePrefixPattern}(?:{QuestionStarterPattern}(?:\s|$)|{InstructionActionPattern}\b))";
        return Regex.Split(
                value,
                boundaries,
                RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)
            .Where(clause => !string.IsNullOrWhiteSpace(clause))
            .Select(clause => clause.Trim());
    }

    private static HashSet<string> SignificantTerms(string value) =>
        WordRegex()
            .Matches(value)
            .Select(match => match.Value)
            .Where(term => term.Length >= 2 && !ComparisonStopWords.Contains(term))
            .ToHashSet(StringComparer.Ordinal);

    private static bool IsLessSpecificOverlap(
        string candidateTitle,
        string selectedTitle)
    {
        var candidateTerms = SignificantTerms(candidateTitle);
        var selectedTerms = SignificantTerms(selectedTitle);
        return candidateTerms.Count > 0
            && candidateTerms.Count < selectedTerms.Count
            && candidateTerms.IsSubsetOf(selectedTerms);
    }

    private static void AddEducationalConceptCard(
        ICollection<ContextualCardProposal> cards,
        ISet<string> knownConceptKinds,
        ISet<string> knownTitles,
        IReadOnlyList<ContextualCardState> existingCards,
        EducationalConcept concept,
        ContextualMention mention)
    {
        if (cards.Count >= 2)
        {
            return;
        }

        var normalizedTitle = HeuristicConversationCoachAgent.Normalize(
            mention.Title);
        if (knownTitles.Any(title => HasSubstantialOverlap(
                normalizedTitle,
                title)
            || IsLessSpecificOverlap(normalizedTitle, title)))
        {
            return;
        }

        var hasDefinition = existingCards.Any(card =>
            card.Kind == ContextualCardKind.Definition
            && string.Equals(
                TryResolveConceptKey(card),
                concept.ConceptKey,
                StringComparison.Ordinal));
        var kind = hasDefinition
            ? ContextualCardKind.Hint
            : ContextualCardKind.Definition;
        if (knownConceptKinds.Contains($"{concept.ConceptKey}:{kind}"))
        {
            return;
        }

        knownTitles.Add(normalizedTitle);
        knownConceptKinds.Add($"{concept.ConceptKey}:{kind}");
        cards.Add(new ContextualCardProposal(
            kind,
            mention.Title,
            kind == ContextualCardKind.Definition
                ? concept.DefinitionText
                : concept.HintText,
            0.93,
            mention.SourceTranscriptSegmentIds)
        {
            ConceptKey = concept.ConceptKey
        });
    }

    private static IEnumerable<PresentationRecommendationMatch>
        FindRecommendationMatches(
            IReadOnlyList<TranscriptSegment> analysisWindow,
            bool hasAzureMeetingContext)
    {
        foreach (var definition in PresentationRecommendationCatalog)
        {
            var mention = FindMention(
                analysisWindow,
                definition.RequiresAzureContext,
                hasAzureMeetingContext,
                definition.MentionTerms.ToArray());
            if (mention is not null)
            {
                yield return new PresentationRecommendationMatch(
                    definition,
                    mention);
            }
        }
    }

    private static bool IsTechnicalRecommendationTitle(
        string normalizedTitle,
        PresentationRecommendationDefinition definition)
    {
        if (GenericPresentationTerms.Any(term =>
                normalizedTitle.Contains(term, StringComparison.Ordinal)))
        {
            return false;
        }

        return definition.AnchorTitleTerms.Any(term => ContainsWholeTerm(
                normalizedTitle,
                HeuristicConversationCoachAgent.Normalize(term)))
            || definition.SupportingTitleTerms.Count(term => ContainsWholeTerm(
                normalizedTitle,
                HeuristicConversationCoachAgent.Normalize(term))) >= 2;
    }

    internal static bool TryResolveEducationalProposal(
        ContextualCardProposal proposal,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        MeetingPurpose purpose,
        out EducationalConcept? concept,
        out AlertRejectionReason reason,
        out string details)
    {
        concept = null;
        reason = AlertRejectionReason.None;
        details = string.Empty;

        if (proposal.SourceTranscriptSegmentIds is not { Count: > 0 })
        {
            reason = AlertRejectionReason.MissingEvidence;
            details = "proposal has no source transcript segment IDs";
            return false;
        }

        var sourceIds = proposal.SourceTranscriptSegmentIds.ToHashSet();
        var scopedWindow = analysisWindow
            .Where(segment => sourceIds.Contains(segment.Id))
            .ToArray();
        if (scopedWindow.Length == 0)
        {
            reason = AlertRejectionReason.MissingEvidence;
            details = "proposal sources were outside the final transcript window";
            return false;
        }

        if (!EducationalConceptCatalog.TryResolveByAliasOrTitle(
                proposal.ConceptKey ?? proposal.Title,
                out var resolvedConcept))
        {
            reason = AlertRejectionReason.MentionNotFound;
            details = "proposal title did not map to the educational concept catalog";
            return false;
        }

        concept = resolvedConcept;
        var mention = TryFindEducationalMention(
            scopedWindow,
            resolvedConcept,
            HasAzureMeetingContext(purpose),
            out reason,
            out details);
        return mention is not null;
    }

    private static ContextualMention? TryFindEducationalMention(
        IReadOnlyList<TranscriptSegment> segments,
        EducationalConcept concept,
        bool hasAzureMeetingContext,
        out AlertRejectionReason rejectionReason,
        out string details)
    {
        return FindMentionDetailed(
            segments,
            concept.RequiresAzureVendorScope,
            hasAzureMeetingContext,
            concept.Aliases,
            out rejectionReason,
            out details);
    }

    private static ContextualMention? FindMention(
        IReadOnlyList<TranscriptSegment> segments,
        bool requiresAzureContext,
        bool hasAzureMeetingContext,
        params string[] terms)
    {
        return FindMentionDetailed(
            segments,
            requiresAzureContext,
            hasAzureMeetingContext,
            terms,
            out _,
            out _);
    }

    private static ContextualMention? FindMentionDetailed(
        IReadOnlyList<TranscriptSegment> segments,
        bool requiresAzureContext,
        bool hasAzureMeetingContext,
        IReadOnlyList<string> terms,
        out AlertRejectionReason rejectionReason,
        out string details)
    {
        rejectionReason = AlertRejectionReason.None;
        details = string.Empty;
        var sawNegatedMention = false;
        var sawVendorMismatch = false;

        for (var segmentIndex = segments.Count - 1; segmentIndex >= 0; segmentIndex--)
        {
            var segment = segments[segmentIndex];
            foreach (var term in terms)
            {
                foreach (var index in FindWholeTermIndexes(segment.Text, term))
                {
                    var sentence = GetSentenceContaining(
                        segment.Text,
                        index,
                        term.Length);
                    if (IsNegatedOrOutOfScope(sentence, term)
                        || HasNearbyNegation(sentence, term))
                    {
                        sawNegatedMention = true;
                        continue;
                    }

                    if (requiresAzureContext
                        && HasVendorMismatch(sentence, term))
                    {
                        sawVendorMismatch = true;
                        continue;
                    }

                    if (requiresAzureContext
                        && !HasAzureContext(
                            segments,
                            segmentIndex,
                            1,
                            segment.Text,
                            term,
                            index,
                            hasAzureMeetingContext))
                    {
                        continue;
                    }

                    return new ContextualMention(
                        segment.Text.Substring(index, term.Length),
                        [segment.Id]);
                }
            }
        }

        for (var start = segments.Count - 2; start >= 0; start--)
        {
            var adjacentSegments = segments
                .Skip(start)
                .Take(Math.Min(3, segments.Count - start))
                .ToArray();
            for (var count = 2; count <= adjacentSegments.Length; count++)
            {
                var combinedText = string.Join(
                    ' ',
                    adjacentSegments.Take(count).Select(segment => segment.Text.Trim()));
                string? matchedTerm = null;
                foreach (var term in terms)
                {
                    foreach (var index in FindWholeTermIndexes(combinedText, term))
                    {
                        var sentence = GetSentenceContaining(
                            combinedText,
                            index,
                            term.Length);
                        if (IsNegatedOrOutOfScope(sentence, term)
                            || HasNearbyNegation(sentence, term)
                            || (requiresAzureContext
                                && HasVendorMismatch(sentence, term))
                            || (requiresAzureContext
                                && !HasAzureContext(
                                    segments,
                                    start,
                                    count,
                                    combinedText,
                                    term,
                                    index,
                                    hasAzureMeetingContext)))
                        {
                            sawNegatedMention = sawNegatedMention
                                || IsNegatedOrOutOfScope(sentence, term)
                                || HasNearbyNegation(sentence, term);
                            sawVendorMismatch = sawVendorMismatch
                                || (requiresAzureContext
                                    && HasVendorMismatch(sentence, term));
                            continue;
                        }

                        matchedTerm = term;
                        break;
                    }

                    if (matchedTerm is not null)
                    {
                        break;
                    }
                }

                if (matchedTerm is not null)
                {
                    return new ContextualMention(
                        matchedTerm,
                        adjacentSegments
                            .Take(count)
                            .Select(segment => segment.Id)
                            .ToArray());
                }
            }
        }

        if (sawVendorMismatch)
        {
            rejectionReason = AlertRejectionReason.VendorMismatch;
            details = "conflicting vendor keywords appeared near the matched term without nearby Azure scoping";
        }
        else if (sawNegatedMention)
        {
            rejectionReason = AlertRejectionReason.NegatedMention;
            details = "the matched term appeared in a negated or out-of-scope context";
        }

        return null;
    }

    internal static bool RequiresAzureEvidence(ChecklistItemState item)
        => RequiresAzureEvidenceForOutput(GetChecklistEvidenceText(item));

    internal static bool HasAzureEvidence(
        ChecklistItemState item,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        IReadOnlyList<Guid> sourceTranscriptSegmentIds) =>
        HasAzureEvidence(
            GetChecklistEvidenceText(item),
            analysisWindow,
            sourceTranscriptSegmentIds);

    private static string GetChecklistEvidenceText(ChecklistItemState item)
    {
        return string.Join(
            ' ',
            new[] { item.Title, item.CompletionCriteria }
                .Concat(item.EvidenceHints));
    }

    private static bool HasAzureEvidence(
        string expectedOutputText,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        IReadOnlyList<Guid> sourceTranscriptSegmentIds)
    {
        var sourceIds = sourceTranscriptSegmentIds.ToHashSet();
        var sourceIndexes = analysisWindow
            .Select((segment, index) => new { segment.Id, Index = index })
            .Where(entry => sourceIds.Contains(entry.Id))
            .Select(entry => entry.Index)
            .ToArray();
        if (sourceIndexes.Length == 0)
        {
            return false;
        }

        var requiredConceptGroups = FindAzureEvidenceConceptGroups(
            expectedOutputText);
        return requiredConceptGroups.Length == 0
            ? HasAnchoredAzureEvidence(
                expectedOutputText,
                analysisWindow,
                sourceIndexes)
            : requiredConceptGroups.All(group =>
                sourceIndexes.Any(index =>
                {
                    var segmentText = analysisWindow[index].Text;
                    return group.Any(term =>
                        FindWholeTermIndexes(segmentText, term).Any(termIndex =>
                            HasAzureContext(
                                analysisWindow,
                                index,
                                1,
                                segmentText,
                                term,
                                termIndex,
                                hasAzureMeetingContext: false)));
                }));
    }

    private static bool HasAnchoredAzureEvidence(
        string expectedOutputText,
        IReadOnlyList<TranscriptSegment> analysisWindow,
        IReadOnlyList<int> sourceIndexes)
    {
        var definingAnchor = FindDefiningAzureAnchor(expectedOutputText);
        if (definingAnchor is null)
        {
            return false;
        }

        return sourceIndexes
            .SelectMany(index =>
                EnumerateSentences(analysisWindow[index].Text))
            .Any(sentence =>
                HasLatestAzurePlatformMarker(sentence)
                && ContainsWholeTerm(
                    HeuristicConversationCoachAgent.Normalize(sentence),
                    definingAnchor));
    }

    private static string? FindDefiningAzureAnchor(string text)
    {
        var normalized = HeuristicConversationCoachAgent.Normalize(text);
        var azureIndex = FindWholeTermIndex(normalized, "azure");
        if (azureIndex >= 0)
        {
            var suffix = normalized[(azureIndex + "azure".Length)..];
            var suffixAnchor = WordRegex()
                .Matches(suffix)
                .Select(match => match.Value)
                .FirstOrDefault(term =>
                    !AzureEvidenceAnchorStopWords.Contains(term));
            if (suffixAnchor is not null)
            {
                return suffixAnchor;
            }
        }

        return WordRegex()
            .Matches(normalized)
            .Select(match => match.Value)
            .FirstOrDefault(term =>
                !AzureEvidenceAnchorStopWords.Contains(term));
    }

    internal static bool HasAzureMeetingContext(MeetingPurpose purpose)
    {
        var purposeText = string.Join(
            ' ',
            new[] { purpose.Title, purpose.MeetingType, purpose.Objective }
                .Concat(purpose.SuccessCriteria));
        return ContainsAzureContext(purposeText)
            && !ContainsConflictingPlatformContext(purposeText);
    }

    private static bool HasAzureContext(
        IReadOnlyList<TranscriptSegment> segments,
        int matchStart,
        int matchCount,
        string matchedText,
        string matchedTerm,
        int matchIndex,
        bool hasAzureMeetingContext)
    {
        if (ContainsAzureContext(matchedTerm))
        {
            return true;
        }

        var sentence = GetSentenceContaining(
            matchedText,
            matchIndex,
            matchedTerm.Length);
        var sentenceStart = FindSentenceStart(matchedText, matchIndex);
        var sentenceMatchIndex = matchIndex - sentenceStart;
        var azureMarkerDistance = FindClosestWholeTermDistance(
            sentence,
            AzureContextTerms,
            sentenceMatchIndex,
            matchedTerm.Length);
        var conflictingMarkerDistance = FindClosestWholeTermDistance(
            sentence,
            ConflictingPlatformTerms,
            sentenceMatchIndex,
            matchedTerm.Length,
            ignoreAzureServiceReferences: true);
        if (azureMarkerDistance >= 0 || conflictingMarkerDistance >= 0)
        {
            return azureMarkerDistance >= 0
                && (conflictingMarkerDistance < 0
                    || azureMarkerDistance < conflictingMarkerDistance);
        }

        var contextStart = Math.Max(0, matchStart - 2);
        var precedingText = string.Join(
            ' ',
            segments
                .Skip(contextStart)
                .Take(matchStart - contextStart)
                .Select(segment => segment.Text));
        var matchedPrefixEnd = Math.Min(
            matchedText.Length,
            matchIndex + matchedTerm.Length);
        var scopedPrefix = string.Join(
            ' ',
            new[] { precedingText, matchedText[..matchedPrefixEnd] }
                .Where(value => value.Length > 0));
        var latestAzureMarker = FindLastWholeTermIndex(
            scopedPrefix,
            AzureContextTerms);
        var latestConflictingMarker = FindLastWholeTermIndex(
            scopedPrefix,
            ConflictingPlatformTerms,
            ignoreAzureServiceReferences: true);
        if (latestAzureMarker >= 0 || latestConflictingMarker >= 0)
        {
            return latestAzureMarker > latestConflictingMarker;
        }

        return hasAzureMeetingContext;
    }

    private static string GetSentenceContaining(
        string text,
        int index,
        int length)
    {
        var start = FindSentenceStart(text, index);

        var end = index + length;
        while (end < text.Length
            && !SentenceBoundaries.Contains(text[end]))
        {
            end++;
        }

        return text[start..end];
    }

    private static int FindSentenceStart(string text, int index)
    {
        var start = index;
        while (start > 0
            && !SentenceBoundaries.Contains(text[start - 1]))
        {
            start--;
        }

        return start;
    }

    private static IEnumerable<string> EnumerateSentences(string text)
    {
        var start = 0;
        for (var index = 0; index < text.Length; index++)
        {
            if (!SentenceBoundaries.Contains(text[index]))
            {
                continue;
            }

            var sentence = text[start..index].Trim();
            if (sentence.Length > 0)
            {
                yield return sentence;
            }

            start = index + 1;
        }

        var finalSentence = text[start..].Trim();
        if (finalSentence.Length > 0)
        {
            yield return finalSentence;
        }
    }

    private static bool RequiresAzureEvidenceForOutput(string text) =>
        ContainsAzureContext(text)
        || ContainsAnyNormalizedTerm(text, AzureSpecificConceptTerms);

    private static string[][] FindAzureEvidenceConceptGroups(string text) =>
        AzureEvidenceConceptGroups
            .Where(group => ContainsAnyNormalizedTerm(text, group))
            .ToArray();

    private static bool ContainsAnyNormalizedTerm(
        string text,
        IEnumerable<string> terms)
    {
        var normalizedText = HeuristicConversationCoachAgent.Normalize(text);
        return terms.Any(term => ContainsWholeTerm(normalizedText, term));
    }

    private static bool ContainsAzureContext(string text) =>
        AzureContextTerms.Any(term => ContainsWholeTerm(text, term));

    private static bool ContainsConflictingPlatformContext(string text) =>
        ConflictingPlatformTerms.Any(term =>
            FindWholeTermIndexes(text, term).Any(index =>
                !IsAzureServiceReference(text, index, term)));

    private static bool HasLatestAzurePlatformMarker(string text)
    {
        var latestAzureMarker = FindLastWholeTermIndex(
            text,
            AzureContextTerms,
            ignoreComparativeReferences: true);
        var latestConflictingMarker = FindLastWholeTermIndex(
            text,
            ConflictingPlatformTerms,
            ignoreComparativeReferences: true,
            ignoreAzureServiceReferences: true);
        return latestAzureMarker >= 0
            && latestAzureMarker > latestConflictingMarker;
    }

    private static int FindLastWholeTermIndex(
        string text,
        IEnumerable<string> terms,
        bool ignoreComparativeReferences = false,
        bool ignoreAzureServiceReferences = false) =>
        terms
            .SelectMany(term => FindWholeTermIndexes(text, term)
                .Where(index => !ignoreComparativeReferences
                    || !IsComparativePlatformReference(text, index))
                .Where(index => !ignoreAzureServiceReferences
                    || !IsAzureServiceReference(text, index, term)))
            .DefaultIfEmpty(-1)
            .Max();

    private static int FindClosestWholeTermDistance(
        string text,
        IEnumerable<string> terms,
        int targetIndex,
        int targetLength,
        bool ignoreAzureServiceReferences = false)
    {
        var targetEnd = targetIndex + targetLength;
        return terms
            .SelectMany(term => FindWholeTermIndexes(text, term)
                .Where(index => !IsComparativePlatformReference(text, index))
                .Where(index => !ignoreAzureServiceReferences
                    || !IsAzureServiceReference(text, index, term))
                .Select(index =>
                {
                    var markerEnd = index + term.Length;
                    return markerEnd <= targetIndex
                        ? targetIndex - markerEnd
                        : index >= targetEnd
                            ? index - targetEnd
                            : 0;
                }))
            .DefaultIfEmpty(-1)
            .Min();
    }

    private static bool IsComparativePlatformReference(
        string text,
        int markerIndex)
    {
        var prefix = HeuristicConversationCoachAgent.Normalize(
            text[..markerIndex]);
        return prefix.EndsWith("unlike", StringComparison.Ordinal)
            || prefix.EndsWith("compared to", StringComparison.Ordinal)
            || prefix.EndsWith("compared with", StringComparison.Ordinal)
            || prefix.EndsWith("versus", StringComparison.Ordinal)
            || prefix.EndsWith("vs", StringComparison.Ordinal);
    }

    private static bool IsAzureServiceReference(
        string text,
        int markerIndex,
        string markerTerm)
    {
        if (!markerTerm.Equals("kubernetes", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var prefix = text[..markerIndex].TrimEnd();
        var suffix = text[(markerIndex + markerTerm.Length)..].TrimStart();
        return prefix.EndsWith("Azure", StringComparison.OrdinalIgnoreCase)
            && FindWholeTermIndex(suffix, "service") == 0;
    }

    private static bool ContainsWholeTerm(string text, string term)
        => FindWholeTermIndex(text, term) >= 0;

    private static int FindWholeTermIndex(string text, string term)
        => FindWholeTermIndexes(text, term).FirstOrDefault(-1);

    private static IEnumerable<int> FindWholeTermIndexes(
        string text,
        string term)
    {
        var searchFrom = 0;
        while (searchFrom <= text.Length - term.Length)
        {
            var index = text.IndexOf(
                term,
                searchFrom,
                StringComparison.OrdinalIgnoreCase);
            if (index < 0)
            {
                yield break;
            }

            var startsAtBoundary = index == 0
                || !char.IsLetterOrDigit(text[index - 1]);
            var end = index + term.Length;
            var endsAtBoundary = end == text.Length
                || !char.IsLetterOrDigit(text[end]);
            if (startsAtBoundary && endsAtBoundary)
            {
                yield return index;
            }

            searchFrom = index + 1;
        }
    }

    private static bool IsNegatedOrOutOfScope(string text, string term)
    {
        var normalizedText = HeuristicConversationCoachAgent.Normalize(text);
        var normalizedTerm = HeuristicConversationCoachAgent.Normalize(term);
        var escapedTerm = Regex.Escape(normalizedTerm);
        return Regex.IsMatch(
                normalizedText,
                $@"\b(?:no|without)\s+(?:[\p{{L}}\p{{N}}]+\s+){{0,2}}{escapedTerm}\b",
                RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                normalizedText,
                $@"\b(?:not|do not|does not|did not|will not|won t)\s+(?:(?:use|using|discuss|discussing|consider|considering|include|including|select|selecting)\s+)?{escapedTerm}\b",
                RegexOptions.CultureInvariant)
            || Regex.IsMatch(
                normalizedText,
                $@"\b{escapedTerm}\b(?:\s+[\p{{L}}\p{{N}}]+){{0,3}}\s+(?:is\s+)?(?:out of scope|not in scope|excluded)\b",
                RegexOptions.CultureInvariant);
    }

    private static bool HasNearbyNegation(string text, string term)
    {
        var normalizedText = HeuristicConversationCoachAgent.Normalize(text);
        var normalizedTerm = HeuristicConversationCoachAgent.Normalize(term);
        if (!TryFindTokenWindow(normalizedText, normalizedTerm, out var tokens, out var start, out _))
        {
            return false;
        }

        var preceding = tokens
            .Skip(Math.Max(0, start - 5))
            .Take(start - Math.Max(0, start - 5))
            .ToArray();
        if (preceding.Any(token =>
                token is "not" or "no" or "without" or "avoid" or "unlike"))
        {
            return true;
        }

        return HasPhrase(preceding, "instead", "of")
            || HasPhrase(preceding, "rather", "than");
    }

    private static bool HasVendorMismatch(string text, string term)
    {
        var normalizedText = HeuristicConversationCoachAgent.Normalize(text);
        var normalizedTerm = HeuristicConversationCoachAgent.Normalize(term);
        if (!TryFindTokenWindow(normalizedText, normalizedTerm, out var tokens, out var start, out var length))
        {
            return false;
        }

        return HasNearbyTerms(tokens, start, length, 3, ConflictingPlatformTerms)
            && !HasNearbyTerms(tokens, start, length, 3, AzureContextTerms);
    }

    private static bool TryFindTokenWindow(
        string normalizedText,
        string normalizedTerm,
        out string[] tokens,
        out int start,
        out int length)
    {
        tokens = WordRegex()
            .Matches(normalizedText)
            .Select(match => match.Value)
            .ToArray();
        var termTokens = normalizedTerm
            .Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var tokenIndex = 0; tokenIndex <= tokens.Length - termTokens.Length; tokenIndex++)
        {
            if (tokens
                .Skip(tokenIndex)
                .Take(termTokens.Length)
                .SequenceEqual(termTokens, StringComparer.Ordinal))
            {
                start = tokenIndex;
                length = termTokens.Length;
                return true;
            }
        }

        start = -1;
        length = 0;
        return false;
    }

    private static bool HasNearbyTerms(
        IReadOnlyList<string> tokens,
        int start,
        int length,
        int maxDistance,
        IEnumerable<string> terms)
    {
        foreach (var term in terms)
        {
            var termTokens = HeuristicConversationCoachAgent.Normalize(term)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries);
            for (var tokenIndex = 0; tokenIndex <= tokens.Count - termTokens.Length; tokenIndex++)
            {
                if (!tokens
                        .Skip(tokenIndex)
                        .Take(termTokens.Length)
                        .SequenceEqual(termTokens, StringComparer.Ordinal))
                {
                    continue;
                }

                var beforeDistance = start - (tokenIndex + termTokens.Length);
                var afterDistance = tokenIndex - (start + length);
                if ((beforeDistance >= 0 && beforeDistance <= maxDistance)
                    || (afterDistance >= 0 && afterDistance <= maxDistance)
                    || (tokenIndex >= start && tokenIndex < start + length))
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static bool HasPhrase(
        IReadOnlyList<string> tokens,
        string first,
        string second)
    {
        for (var index = 0; index < tokens.Count - 1; index++)
        {
            if (tokens[index].Equals(first, StringComparison.Ordinal)
                && tokens[index + 1].Equals(second, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    private static string? TryResolveConceptKey(ContextualCardState card)
    {
        if (!string.IsNullOrWhiteSpace(card.ConceptKey))
        {
            return card.ConceptKey;
        }

        return EducationalConceptCatalog.TryResolveByAliasOrTitle(card.Title, out var concept)
            ? concept.ConceptKey
            : null;
    }

    private sealed record ContextualMention(
        string Title,
        IReadOnlyList<Guid> SourceTranscriptSegmentIds);

    private sealed record PresentationRecommendationDefinition(
        IReadOnlyList<string> MentionTerms,
        string Title,
        string Rationale,
        double Confidence,
        IReadOnlyList<string> AnchorTitleTerms,
        IReadOnlyList<string> SupportingTitleTerms,
        bool RequiresAzureContext = false);

    private sealed record PresentationRecommendationMatch(
        PresentationRecommendationDefinition Definition,
        ContextualMention Mention);

    [GeneratedRegex(@"[\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex WordRegex();
}
