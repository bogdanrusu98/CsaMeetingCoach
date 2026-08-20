using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CsaMeetingCoach.Contracts;

namespace CsaMeetingCoach.Core;

public sealed partial class HeuristicConversationCoachAgent : IConversationCoachAgent
{
    private const string AssignedActionPattern =
        @"(?:send|deliver|finish|complete|validate|review|prepare|present|submit|share|provide|schedule|create|draft|publish|confirm|assess|coordinate|lead|own|handle|document|finalize|finalise)";

    private static readonly string[] ObjectiveMetaActions =
    [
        "discuss",
        "clarify",
        "define",
        "review",
        "identify",
        "determine",
        "agree",
        "explore",
        "understand",
        "discuta",
        "clarifica",
        "defini",
        "stabili"
    ];

    private static readonly string[] UncertainObjectiveCues =
    [
        "maybe",
        "perhaps",
        "possibly",
        "might",
        "may need",
        "not sure",
        "uncertain",
        "unclear",
        "i think",
        "we think",
        "poate",
        "posibil",
        "nu suntem siguri"
    ];

    private readonly ISemanticTermCatalog semanticTerms;

    public HeuristicConversationCoachAgent()
        : this(new BuiltInSemanticTermCatalog())
    {
    }

    public HeuristicConversationCoachAgent(ISemanticTermCatalog semanticTerms)
    {
        this.semanticTerms = semanticTerms
            ?? throw new ArgumentNullException(nameof(semanticTerms));
    }

    private static readonly string[] CommitmentCues =
    [
        "we will",
        "i will",
        "you will",
        "need to",
        "should",
        "follow up",
        "send",
        "prepare",
        "schedule",
        "vom",
        "voi",
        "veți",
        "trebuie să",
        "ar trebui să",
        "trimite",
        "pregăti",
        "programa"
    ];

    private static readonly string[] CustomerNeedCues =
    [
        "need",
        "require",
        "problem",
        "challenge",
        "how ",
        "what ",
        "can we",
        "could you",
        "trebuie",
        "nevoie",
        "problema",
        "provocare",
        "cum ",
        "ce ",
        "putem"
    ];

    private static readonly string[] UncertainCommitmentCues =
    [
        "maybe",
        "perhaps",
        "possibly",
        "might",
        "not sure",
        "unclear",
        "not confirmed",
        "not agreed",
        "not decided",
        "i think",
        "we think",
        "don t think",
        "do not think",
        "don t believe",
        "do not believe",
        "unlikely",
        "need to decide",
        "need to determine",
        "do not know",
        "don t know",
        "poate",
        "posibil",
        "nu este clar",
        "nu am stabilit"
    ];

    private static readonly HashSet<string> InvalidAssignmentSubjectTerms =
    [
        "it",
        "this",
        "that",
        "the",
        "a",
        "an",
        "who",
        "whom",
        "whose",
        "what",
        "which",
        "whether",
        "if",
        "no",
        "none",
        "nobody",
        "nothing",
        "neither",
        "nor",
        "someone",
        "somebody",
        "anyone",
        "anybody",
        "everyone",
        "everybody",
        "one",
        "platform",
        "application",
        "service",
        "solution",
        "system",
        "migration",
        "architecture",
        "project",
        "pilot",
        "workload",
        "process",
        "technology",
        "modernization"
    ];

    private static readonly HashSet<string> PersonalAssignmentSubjects =
    [
        "i",
        "we",
        "you",
        "they"
    ];

    private static readonly string[] ExplicitMeasurableOutcomeCues =
    [
        "success means",
        "success is measured",
        "success metric is",
        "success metric will",
        "metric is",
        "metric will",
        "measured by",
        "measure success",
        "kpi is",
        "kpi will",
        "target is",
        "target of",
        "success criteria are",
        "success criteria include",
        "success criteria will",
        "success criterion is",
        "succesul inseamna",
        "succesul este masurat",
        "succesul se masoara",
        "masuram succesul",
        "metrica este",
        "kpi este",
        "tinta este",
        "criteriile de succes sunt",
        "criteriul de succes este"
    ];

    private static readonly string[] NegatedMeasurableOutcomeCues =
    [
        "no success criteria",
        "without success criteria",
        "success criteria are not",
        "success criteria have not",
        "success criteria remain undefined",
        "success criteria remain unclear",
        "metric is not",
        "metric has not",
        "metric remains undefined",
        "target is not",
        "target has not",
        "target remains undefined",
        "target remains unclear",
        "nu exista criterii de succes",
        "fara criterii de succes",
        "criteriile de succes nu",
        "metrica nu este",
        "tinta nu este"
    ];

    public Task<CoachAgentDecision> AnalyzeAsync(
        CoachAgentContext context,
        TranscriptSegment latestSegment,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        if (!latestSegment.IsFinal || string.IsNullOrWhiteSpace(latestSegment.Text))
        {
            return Task.FromResult(new CoachAgentDecision([], [], []));
        }

        var normalizedText = Normalize(latestSegment.Text);
        var evaluations = context.Checklist
            .Where(item => item.Status == ChecklistItemStatus.Pending)
            .SelectMany(item => Evaluate(
                item,
                context.RecentTranscript,
                latestSegment,
                normalizedText,
                context.Template))
            .ToArray();

        var recommendations = CreateRecommendations(
            latestSegment,
            normalizedText,
            context.Template);
        var recommendationEvaluations = (context.RecommendedTasks ?? [])
            .Where(item => item.Status == RecommendationStatus.Accepted)
            .Select(item => EvaluateRecommendation(item, latestSegment, normalizedText))
            .Where(evaluation => evaluation is not null)
            .Cast<RecommendationEvaluation>()
            .ToArray();
        return Task.FromResult(new CoachAgentDecision(
            evaluations,
            recommendations,
            recommendationEvaluations));
    }

    private IReadOnlyList<ChecklistEvaluation> Evaluate(
        ChecklistItemState item,
        IReadOnlyList<TranscriptSegment> recentTranscript,
        TranscriptSegment segment,
        string normalizedText,
        SessionTemplateKind template)
    {
        if (!PresentationChecklistEvidencePolicy.AllowsCompletion(
                template,
                item,
                segment))
        {
            return [];
        }

        if (RequiresCompoundEvidence(item))
        {
            return EvaluateCompoundEvidence(item, recentTranscript, segment);
        }

        if (PresentationCoachingPolicy.RequiresAzureEvidence(item)
            && !PresentationCoachingPolicy.HasAzureEvidence(
                item,
                recentTranscript,
                [segment.Id]))
        {
            return [];
        }

        var matchedHints = item.EvidenceHints
            .Select(Normalize)
            .Where(hint => hint.Length >= 3
                && ContainsEquivalentTerm(normalizedText, hint))
            .Distinct(StringComparer.Ordinal)
            .ToList();
        var requiresCommitment = IsCommitmentItem(item);
        var hasNamedAssignment = IsNextStepCommitmentItem(item)
            && HasConfirmedActionAssignment(normalizedText, segment.Text);
        if (hasNamedAssignment)
        {
            matchedHints.Add("named action assignment");
        }

        if (IsObjectiveItem(item)
            && HasDirectConversationalObjective(normalizedText, segment.Text))
        {
            matchedHints.Add("direct conversational objective");
        }

        var explicitPresentationSignal =
            PresentationChecklistEvidencePolicy.GetExplicitSignal(
                template,
                item,
                segment);
        if (explicitPresentationSignal is not null)
        {
            matchedHints.Add(explicitPresentationSignal);
        }

        if (matchedHints.Count == 0)
        {
            return [];
        }

        var confidence = Math.Min(0.97, 0.84 + ((matchedHints.Count - 1) * 0.03));
        var requiresMeasurableOutcome = RequiresMeasurableOutcome(item);

        var hasQuantitativeOutcome =
            QuantitativeOutcomeRegex().IsMatch(segment.Text);
        var hasExplicitMeasurableOutcome =
            ExplicitMeasurableOutcomeCues.Any(normalizedText.Contains)
            && !NegatedMeasurableOutcomeCues.Any(normalizedText.Contains);
        var hasMeasurableOutcomeEvidence =
            hasQuantitativeOutcome || hasExplicitMeasurableOutcome;

        if (requiresMeasurableOutcome && !hasMeasurableOutcomeEvidence)
        {
            confidence = Math.Min(confidence, 0.70);
        }

        if (requiresCommitment
            && !hasNamedAssignment
            && !CommitmentCues.Any(normalizedText.Contains))
        {
            confidence = Math.Min(confidence, 0.70);
        }

        return
        [
            new ChecklistEvaluation(
                item.Id,
                ShouldComplete: confidence >= MeetingSessionCoordinator.AutoCompletionThreshold,
                confidence,
                $"Matched discussion evidence: {string.Join(", ", matchedHints)}.",
                segment.Text.Trim(),
                segment.Id)
        ];
    }

    private IReadOnlyList<ChecklistEvaluation> EvaluateCompoundEvidence(
        ChecklistItemState item,
        IReadOnlyList<TranscriptSegment> recentTranscript,
        TranscriptSegment evaluatedSegment)
    {
        var finalTranscript = recentTranscript
            .Where(segment => segment.IsFinal)
            .ToArray();
        if (finalTranscript.Length == 0
            || finalTranscript[^1].Id != evaluatedSegment.Id)
        {
            return [];
        }

        var eligibleFrom = Math.Clamp(
            item.CompletionEligibleFromTranscriptIndex ?? 0,
            0,
            finalTranscript.Length);
        var eligibleWindow = finalTranscript
            .Skip(eligibleFrom)
            .TakeLast(TranscriptAnalysisWindow.MaximumSegments)
            .ToArray();
        var requiredHintGroups = BuildRequiredHintGroups(item.EvidenceHints
            .Select(Normalize)
            .Where(hint => hint.Length >= 3)
            .Distinct(StringComparer.Ordinal)
            .ToArray());
        if (requiredHintGroups.Count < 2)
        {
            return [];
        }

        var matches = requiredHintGroups
            .Select(group => (
                Group: group,
                Segment: eligibleWindow.FirstOrDefault(segment =>
                {
                    var normalizedSegment = Normalize(segment.Text);
                    return group.Any(hint =>
                        ContainsEquivalentTerm(normalizedSegment, hint));
                })))
            .ToArray();
        if (matches.Any(match => match.Segment is null))
        {
            return [];
        }

        var matchedSegments = matches
            .Select(match => match.Segment!)
            .DistinctBy(segment => segment.Id)
            .ToArray();
        if (PresentationCoachingPolicy.RequiresAzureEvidence(item)
            && !PresentationCoachingPolicy.HasAzureEvidence(
                item,
                recentTranscript,
                matchedSegments.Select(segment => segment.Id).ToArray()))
        {
            return [];
        }

        var combinedNormalizedText = string.Join(
            ' ',
            eligibleWindow.Select(segment => Normalize(segment.Text)));
        var combinedOriginalText = string.Join(
            ' ',
            eligibleWindow.Select(segment => segment.Text));
        if (RequiresMeasurableOutcome(item)
            && !QuantitativeOutcomeRegex().IsMatch(combinedOriginalText)
            && !(ExplicitMeasurableOutcomeCues.Any(combinedNormalizedText.Contains)
                && !NegatedMeasurableOutcomeCues.Any(combinedNormalizedText.Contains)))
        {
            return [];
        }

        if (IsCommitmentItem(item)
            && !CommitmentCues.Any(combinedNormalizedText.Contains)
            && !eligibleWindow.Any(segment => HasConfirmedActionAssignment(
                Normalize(segment.Text),
                segment.Text)))
        {
            return [];
        }

        var reason = $"Covered every required discussion signal: {string.Join(", ", requiredHintGroups.Select(group => group[0]))}.";
        var confidence = Math.Min(
            0.97,
            0.88 + ((requiredHintGroups.Count - 2) * 0.02));
        return matchedSegments
            .Select(segment => new ChecklistEvaluation(
                item.Id,
                ShouldComplete: true,
                confidence,
                reason,
                segment.Text.Trim(),
                segment.Id))
            .ToArray();
    }

    private IReadOnlyList<IReadOnlyList<string>> BuildRequiredHintGroups(
        IReadOnlyList<string> hints)
    {
        var groups = new List<List<string>>();
        foreach (var hint in hints)
        {
            var group = groups.FirstOrDefault(candidate =>
                candidate.Any(existing => AreEquivalentHints(existing, hint)));
            if (group is null)
            {
                groups.Add([hint]);
            }
            else
            {
                group.Add(hint);
            }
        }

        return groups
            .Select(group => (IReadOnlyList<string>)group.ToArray())
            .ToArray();
    }

    private bool AreEquivalentHints(string left, string right)
    {
        var leftExpansions = semanticTerms.Expand(left);
        var rightExpansions = semanticTerms.Expand(right);
        if (leftExpansions.Intersect(rightExpansions, StringComparer.Ordinal).Any())
        {
            return true;
        }

        return left.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2
            && right.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length >= 2
            && (ContainsEvidenceHint(left, right)
                || ContainsEvidenceHint(right, left));
    }

    internal static bool RequiresCompoundEvidence(ChecklistItemState item)
    {
        var criteria = Normalize(item.CompletionCriteria);
        return CompoundCriteriaRegex().IsMatch(criteria);
    }

    internal static bool RequiresMeasurableOutcome(ChecklistItemState item)
    {
        var definition = Normalize($"{item.Title} {item.CompletionCriteria}");
        return definition.Contains("success criteria", StringComparison.Ordinal)
            || definition.Contains("success criterion", StringComparison.Ordinal)
            || definition.Contains("success metric", StringComparison.Ordinal)
            || definition.Contains("measurable outcome", StringComparison.Ordinal)
            || definition.Contains("criteriu de succes", StringComparison.Ordinal)
            || definition.Contains("rezultat masurabil", StringComparison.Ordinal);
    }

    private static bool IsCommitmentItem(ChecklistItemState item)
    {
        var definition = Normalize($"{item.Title} {item.CompletionCriteria}");
        return definition.Contains("next step", StringComparison.Ordinal)
            || definition.Contains("follow up", StringComparison.Ordinal)
            || definition.Contains("owner", StringComparison.Ordinal)
            || definition.Contains("due date", StringComparison.Ordinal)
            || definition.Contains("deadline", StringComparison.Ordinal)
            || definition.Contains("concrete commitment", StringComparison.Ordinal)
            || definition.Contains("urmator", StringComparison.Ordinal)
            || definition.Contains("responsabil", StringComparison.Ordinal)
            || definition.Contains("termen", StringComparison.Ordinal);
    }

    private static bool IsNextStepCommitmentItem(ChecklistItemState item)
    {
        var title = Normalize(item.Title);
        return title.Contains("next step", StringComparison.Ordinal)
            || title.Contains("follow up", StringComparison.Ordinal)
            || title.Contains("urmatorul pas", StringComparison.Ordinal);
    }

    private static bool HasConfirmedActionAssignment(
        string normalizedText,
        string originalText)
    {
        if (originalText.Contains('?')
            || QuestionOpeningRegex().IsMatch(normalizedText)
            || UncertainCommitmentCues.Any(cue =>
                ContainsEvidenceHint(normalizedText, cue)))
        {
            return false;
        }

        var hasProperName = ProperNameActionAssignmentRegex().IsMatch(originalText);
        var hasExplicitTiming = ExplicitTimingRegex().IsMatch(normalizedText);
        return ActionAssignmentRegex()
            .Matches(normalizedText)
            .Select(match => match.Groups["subject"].Value)
            .Any(subject =>
            {
                var subjectTerms = subject
                    .Split(' ', StringSplitOptions.RemoveEmptyEntries);
                return !subjectTerms.Any(InvalidAssignmentSubjectTerms.Contains)
                    && (PersonalAssignmentSubjects.Contains(subject)
                        || hasProperName
                        || hasExplicitTiming);
            });
    }

    private static IReadOnlyList<RecommendedTaskProposal> CreateRecommendations(
        TranscriptSegment segment,
        string normalizedText,
        SessionTemplateKind template)
    {
        if (!CommitmentCues.Any(normalizedText.Contains)
            && !CustomerNeedCues.Any(normalizedText.Contains)
            && !segment.Text.Contains('?'))
        {
            return [];
        }

        var topic = WhitespaceRegex().Replace(segment.Text.Trim(), " ");
        if (topic.Length > 125)
        {
            topic = string.Concat(topic.AsSpan(0, 122), "...");
        }

        var (titlePrefix, rationale) = template switch
        {
            SessionTemplateKind.Presentation => (
                "Clarify for the audience",
                "Presentation coaching uses this explicit point to improve narrative clarity, audience relevance, and the final takeaway."),
            SessionTemplateKind.Workshop => (
                "Facilitate the group on",
                "Workshop coaching uses this explicit point to surface perspectives, reach a decision, or identify an owner."),
            SessionTemplateKind.Training => (
                "Teach and check understanding",
                "Training coaching uses this explicit point to add explanation, an example, practice, or an understanding check."),
            SessionTemplateKind.Custom => (
                "Advance the configured objective",
                "Custom coaching uses this explicit point only to advance the host-configured objective and success criteria."),
            SessionTemplateKind.CsaVbd => (
                "Discuss next",
                "CSA / VBD coaching uses this explicit need, question, or next step to advance customer discovery and a useful outcome."),
            _ => throw new ArgumentOutOfRangeException(nameof(template), template, null)
        };

        return
        [
            new(
                $"{titlePrefix}: {topic}",
                rationale,
                0.86,
                [segment.Id])
        ];
    }

    private RecommendationEvaluation? EvaluateRecommendation(
        RecommendedTaskState recommendation,
        TranscriptSegment segment,
        string normalizedText)
    {
        var normalizedTitle = Normalize(recommendation.Title);
        var semanticGroups = semanticTerms.FindEquivalentTermGroups(
            normalizedTitle);
        var semanticWords = semanticGroups
            .SelectMany(group => group)
            .SelectMany(alias => alias.Split(
                ' ',
                StringSplitOptions.RemoveEmptyEntries))
            .ToHashSet(StringComparer.Ordinal);
        var titleTerms = normalizedTitle
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(term => term.Length >= 5
                && term is not "discuss"
                && !semanticWords.Contains(term))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        var matches = titleTerms.Count(term =>
                ContainsEquivalentTerm(normalizedText, term))
            + semanticGroups.Count(group => group.Any(term =>
                ContainsEvidenceHint(normalizedText, term)));
        var candidateCount = titleTerms.Length + semanticGroups.Count;
        var requiredMatches = Math.Min(2, candidateCount);
        if (matches < requiredMatches || requiredMatches == 0)
        {
            return null;
        }

        var confidence = Math.Min(0.95, 0.82 + ((matches - 1) * 0.03));
        return new RecommendationEvaluation(
            recommendation.Id,
            ShouldComplete: true,
            confidence,
            "The latest final segment explicitly discusses the accepted talking point.",
            segment.Text.Trim(),
            segment.Id);
    }

    internal static string Normalize(string value)
    {
        var decomposed = value.Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);

        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) != UnicodeCategory.NonSpacingMark)
            {
                builder.Append(char.ToLowerInvariant(character));
            }
        }

        return WhitespaceRegex()
            .Replace(NonWordRegex().Replace(builder.ToString(), " "), " ")
            .Trim();
    }

    private static bool ContainsEvidenceHint(string normalizedText, string normalizedHint)
    {
        var paddedText = $" {normalizedText} ";
        var hintForms = new List<string>
        {
            normalizedHint,
            $"{normalizedHint}s",
            $"{normalizedHint}es"
        };
        if (normalizedHint.EndsWith('y'))
        {
            hintForms.Add($"{normalizedHint[..^1]}ies");
        }

        return hintForms.Any(form =>
            paddedText.Contains($" {form} ", StringComparison.Ordinal));
    }

    private bool ContainsEquivalentTerm(
        string normalizedText,
        string normalizedTerm) =>
        semanticTerms.Expand(normalizedTerm).Any(expansion =>
            ContainsEvidenceHint(normalizedText, expansion));

    private static bool IsObjectiveItem(ChecklistItemState item)
    {
        var title = Normalize(item.Title);
        var criteria = Normalize(item.CompletionCriteria);
        var explicitlyBusinessFocused =
            ContainsEvidenceHint(title, "customer objective")
            || ContainsEvidenceHint(title, "business objective")
            || ContainsEvidenceHint(criteria, "business objective")
            || ContainsEvidenceHint(title, "customer goal")
            || ContainsEvidenceHint(title, "obiectivul clientului");
        var objectiveClarification =
            (ContainsEvidenceHint(title, "objective")
                || ContainsEvidenceHint(title, "goal")
                || ContainsEvidenceHint(title, "priority")
                || ContainsEvidenceHint(title, "obiectiv")
                || ContainsEvidenceHint(title, "scop"))
            && (ContainsEvidenceHint(title, "clarify")
                || ContainsEvidenceHint(title, "confirm")
                || ContainsEvidenceHint(title, "identify")
                || ContainsEvidenceHint(title, "define")
                || ContainsEvidenceHint(title, "clarifica")
                || ContainsEvidenceHint(title, "confirma"));
        var isRecoveryMeasure =
            ContainsEvidenceHint(title, "recovery time objective")
            || ContainsEvidenceHint(title, "recovery point objective")
            || ContainsEvidenceHint(title, "rto")
            || ContainsEvidenceHint(title, "rpo");
        return !isRecoveryMeasure
            && (explicitlyBusinessFocused || objectiveClarification);
    }

    private static bool HasDirectConversationalObjective(
        string normalizedText,
        string originalText)
    {
        if (originalText.Contains('?')
            || QuestionOpeningRegex().IsMatch(normalizedText)
            || UncertainObjectiveCues.Any(cue =>
                ContainsEvidenceHint(normalizedText, cue))
            || NegatedObjectiveRegex().IsMatch(normalizedText))
        {
            return false;
        }

        var match = DirectObjectiveRegex().Match(normalizedText);
        if (!match.Success)
        {
            return false;
        }

        var action = match.Groups["action"].Value;
        return action.Length >= 3
            && !ObjectiveMetaActions.Any(meta =>
                action.Equals(meta, StringComparison.Ordinal)
                || action.StartsWith(meta, StringComparison.Ordinal))
            && !action.StartsWith("not ", StringComparison.Ordinal)
            && !action.StartsWith("nu ", StringComparison.Ordinal);
    }

    [GeneratedRegex(@"[^\p{L}\p{N}]+", RegexOptions.CultureInvariant)]
    private static partial Regex NonWordRegex();

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex WhitespaceRegex();

    [GeneratedRegex(
        @"(?:\d+(?:[.,]\d+)?\s*(?:%|\b(?:percent(?:age)?|procente?|milliseconds?|milisecunde?|seconds?|secunde?|minutes?|minute|hours?|ore|days?|zile|weeks?|saptamani|months?|luni)\b)|\b(?:availability|disponibilitate|reduction|reducere|increase|crestere|decrease|scadere)\b(?:\s+\p{L}+){0,3}\s+\d+(?:[.,]\d+)?\s*%?)",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex QuantitativeOutcomeRegex();

    [GeneratedRegex(
        @"^(?:(?:well|so|actually|currently|today)\s+)?(?:(?:we|i|you)\s+(?:need|want)\s+to|our\s+(?:priority|objective|goal)\s+is(?:\s+to)?|(?:(?:noi|eu)\s+)?(?:avem\s+nevoie|vrem|vreau|trebuie)\s+sa|(?:prioritatea|obiectivul|scopul)\s+nostru\s+este(?:\s+sa)?)\s+(?<action>.+)$",
        RegexOptions.CultureInvariant)]
    private static partial Regex DirectObjectiveRegex();

    [GeneratedRegex(
        @"^(?:do|does|did|can|could|should|would|who|whom|whose|what|which|why|how|when|where|whether|is|are|will)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex QuestionOpeningRegex();

    [GeneratedRegex(
        @"\b(?:do\s+not|don\s+t|does\s+not|doesn\s+t|did\s+not|didn\s+t|no\s+need|need\s+not|cannot|can\s+t|not\s+an?\s+(?:objective|priority|goal)|nu\s+(?:avem|vrem|este|trebuie)|fara\s+(?:obiectiv|prioritate))\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex NegatedObjectiveRegex();

    [GeneratedRegex(
        @"(?:^|\s)(?<subject>[\p{L}\p{M}][\p{L}\p{M}'’-]*(?:\s+[\p{L}\p{M}][\p{L}\p{M}'’-]*)?)\s+(?:will|shall)\s+"
            + AssignedActionPattern
            + @"\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ActionAssignmentRegex();

    [GeneratedRegex(
        @"\b\p{Lu}[\p{L}\p{M}'’-]*(?:\s+\p{Lu}[\p{L}\p{M}'’-]*)?\s+(?:will|shall)\s+"
            + AssignedActionPattern
            + @"\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ProperNameActionAssignmentRegex();

    [GeneratedRegex(
        @"\b(?:(?:january|february|march|april|may|june|july|august|september|october|november|december)\s+\d{1,2}|(?:monday|tuesday|wednesday|thursday|friday|saturday|sunday)|today|tomorrow|\d{1,2}[/-]\d{1,2}(?:[/-]\d{2,4})?)\b",
        RegexOptions.CultureInvariant)]
    private static partial Regex ExplicitTimingRegex();

    [GeneratedRegex(
        @"(?:\band\b|\bthen\b|\bas well as\b|;)",
        RegexOptions.CultureInvariant)]
    private static partial Regex CompoundCriteriaRegex();
}
