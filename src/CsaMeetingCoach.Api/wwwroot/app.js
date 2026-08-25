const state = {
  session: null,
  role: null,
  joinCode: null,
  eventSource: null,
  eventProbePending: false,
  teamsMeetingId: null,
  browserSpeechAvailable: false,
  browserSpeechAuthorized: false,
  microphoneRecognizer: null,
  microphoneAudioConfig: null,
  microphoneCapture: null,
  microphoneSourceLabel: "Presenter microphone",
  microphoneRefreshTimer: null,
  sessionExpiryTimer: null,
  sessionExpiryHandling: false,
  microphoneStartupAbortController: null,
  microphoneRefreshAbortController: null,
  microphoneBusy: false,
  microphoneOperation: Promise.resolve(),
  speechPublishQueue: Promise.resolve(),
  speechPublishAbortController: null,
  seenRecognitionIds: new Set(),
  speechPhraseList: null,
  speechDiagnostics: {
    interim: 0,
    final: 0,
    queued: 0,
    published: 0,
    publishFailures: 0,
    corrections: 0,
    phraseVocabCount: 0,
    customSpeechActive: false,
    lastStage: "Waiting for microphone activity.",
    lastEventAt: null
  },
  activeContextualToastIds: new Set(),
  dismissedContextualCardIds: new Set(),
  notifiedRecommendationIds: new Set(),
  notifiedWarningMessages: new Set(),
  knownParticipantIds: new Set(),
  participantTrackingInitialized: false,
  fallbackToastTimer: null,
  systemAudioCaptureAvailable: Boolean(
    navigator.mediaDevices?.getDisplayMedia
      && (window.AudioContext || window.webkitAudioContext))
};

const THEME_STORAGE_KEY = "session-copilot-theme";
const SESSION_HISTORY_KEY = "sessionCopilot";

const templateProfiles = Object.freeze({
  presentation: {
    label: "Presentation",
    meetingType: "Presentation",
    setupHost: "Narrative clarity, audience relevance, examples, transitions, questions, and takeaway.",
    setupMember: "Definitions, distinctions, implications, and context that help the audience follow.",
    titlePlaceholder: "e.g. Product roadmap presentation",
    objectivePlaceholder: "e.g. Help the audience understand the proposal and the decision required",
    criteriaPlaceholder: "Audience outcome is explicit\nKey messages are covered\nQuestions and closing action are addressed",
    focusLabel: "Presentation coaching",
    guidanceEmpty: "Listening for a narrative gap, audience question, useful example, or clearer takeaway.",
    planLabel: "Presentation progress",
    planHeading: "Audience-ready plan",
    alertReviewLabel: "Audience experience",
    alertReviewHeading: "Presentation context review",
    memberLayerLabel: "Live audience context",
    memberLayerHeading: "Definitions and presentation context"
  },
  workshop: {
    label: "Workshop",
    meetingType: "Workshop",
    setupHost: "Participation, assumptions, trade-offs, decisions, unresolved items, owners, and actions.",
    setupMember: "Terminology, assumptions, constraints, options, and trade-offs already in discussion.",
    titlePlaceholder: "e.g. Service design workshop",
    objectivePlaceholder: "e.g. Reach a shared decision and leave with owned actions",
    criteriaPlaceholder: "Shared outcome is confirmed\nAssumptions and options are surfaced\nDecisions and owners are captured",
    focusLabel: "Workshop facilitation",
    guidanceEmpty: "Listening for missing perspectives, assumptions, trade-offs, decisions, or owners.",
    planLabel: "Workshop progress",
    planHeading: "Decision and action plan",
    alertReviewLabel: "Participant experience",
    alertReviewHeading: "Workshop context review",
    memberLayerLabel: "Live workshop context",
    memberLayerHeading: "Terms, options, and trade-offs"
  },
  training: {
    label: "Training",
    meetingType: "Training",
    setupHost: "Explanations, examples, demonstrations, practice, understanding checks, and recap.",
    setupMember: "Definitions, examples, prerequisites, distinctions, mechanisms, and common pitfalls.",
    titlePlaceholder: "e.g. Platform fundamentals training",
    objectivePlaceholder: "e.g. Enable learners to explain and apply the core concepts",
    criteriaPlaceholder: "Learning objectives are introduced\nCore concepts are demonstrated\nUnderstanding is checked and resources are shared",
    focusLabel: "Training guidance",
    guidanceEmpty: "Listening for a concept that needs an example, practice, misconception check, or recap.",
    planLabel: "Learning progress",
    planHeading: "Evidence-backed learning plan",
    alertReviewLabel: "Learner experience",
    alertReviewHeading: "Learning aid review",
    memberLayerLabel: "Live learning layer",
    memberLayerHeading: "Concepts, examples, and useful context"
  },
  custom: {
    label: "Custom",
    meetingType: null,
    setupHost: "Only the configured objective, success criteria, discussion, and supplied knowledge.",
    setupMember: "Concepts that help members follow the configured purpose and member-eligible knowledge.",
    titlePlaceholder: "Name your custom session",
    objectivePlaceholder: "Describe the exact outcome this custom session should achieve",
    criteriaPlaceholder: "Add one observable success criterion per line",
    focusLabel: "Objective-based guidance",
    guidanceEmpty: "Listening for the most useful next step against your configured objective.",
    planLabel: "Configured progress",
    planHeading: "Custom evidence-backed plan",
    alertReviewLabel: "Member experience",
    alertReviewHeading: "Custom context review",
    memberLayerLabel: "Live session context",
    memberLayerHeading: "Definitions and objective-relevant context"
  },
  csaVbd: {
    label: "CSA / VBD",
    meetingType: "CSA / VBD",
    setupHost: "Discovery, solution fit, measurable value, risks, Azure decision signals, and next actions.",
    setupMember: "Grounded business and Azure definitions, mechanisms, implications, and limitations.",
    titlePlaceholder: "e.g. Cloud value discovery",
    objectivePlaceholder: "e.g. Align customer priorities to measurable outcomes and next decisions",
    criteriaPlaceholder: "Customer objectives are explicit\nValue and risks are discussed\nOwners and next actions are agreed",
    focusLabel: "CSA / VBD guidance",
    guidanceEmpty: "Listening for a discovery gap, value link, risk, Azure decision signal, or next action.",
    planLabel: "Customer conversation progress",
    planHeading: "Value and action plan",
    alertReviewLabel: "Client experience",
    alertReviewHeading: "Client learning alert review",
    memberLayerLabel: "Live client learning layer",
    memberLayerHeading: "Business and Azure context"
  }
});

const elements = {
  setupView: document.querySelector("#setup-view"),
  sessionView: document.querySelector("#session-view"),
  memberSessionView: document.querySelector("#member-session-view"),
  rolePicker: document.querySelector("#role-picker"),
  hostSetupPanel: document.querySelector("#host-setup-panel"),
  memberJoinPanel: document.querySelector("#member-join-panel"),
  templateChoices: document.querySelectorAll("input[name='session-template']"),
  customMeetingTypeField: document.querySelector("#custom-meeting-type-field"),
  customMeetingType: document.querySelector("#custom-meeting-type"),
  templateHostSummary: document.querySelector("#template-host-summary"),
  templateMemberSummary: document.querySelector("#template-member-summary"),
  sessionForm: document.querySelector("#session-form"),
  memberJoinForm: document.querySelector("#member-join-form"),
  transcriptForm: document.querySelector("#transcript-form"),
  microphonePanel: document.querySelector("#microphone-panel"),
  microphoneConsent: document.querySelector("#microphone-consent"),
  includeSystemAudio: document.querySelector("#include-system-audio"),
  microphoneAccessKey: document.querySelector("#microphone-access-key"),
  microphoneAccessStatus: document.querySelector("#microphone-access-status"),
  microphoneToggle: document.querySelector("#microphone-toggle"),
  microphoneUnlock: document.querySelector("#microphone-unlock"),
  closeMicrophoneUnlock: document.querySelector("#close-microphone-unlock"),
  confirmMicrophone: document.querySelector("#confirm-microphone"),
  microphoneStatus: document.querySelector("#microphone-status"),
  microphonePreview: document.querySelector("#microphone-preview"),
  speechStatePill: document.querySelector("#speech-state-pill"),
  speechStateLabel: document.querySelector("#speech-state-label"),
  audioSourceLabel: document.querySelector("#audio-source-label"),
  hostLiveTranscript: document.querySelector("#host-live-transcript"),
  hostContextSignal: document.querySelector("#host-context-signal"),
  planProgressOrb: document.querySelector("#plan-progress-orb"),
  progressFill: document.querySelector("#progress-fill"),
  checklist: document.querySelector("#checklist"),
  recommendations: document.querySelector("#recommendations"),
  acceptedRecommendations: document.querySelector("#accepted-recommendations"),
  transcript: document.querySelector("#transcript"),
  contextualCardHistory: document.querySelector("#contextual-card-history"),
  speechDiagnostics: document.querySelector("#speech-diagnostics"),
  warningsPanel: document.querySelector("#warnings-panel"),
  warnings: document.querySelector("#warnings"),
  connectionStatus: document.querySelector("#connection-status"),
  memberConnectionStatus: document.querySelector("#member-connection-status"),
  diagnosticsDialog: document.querySelector("#diagnostics-dialog"),
  openDiagnostics: document.querySelector("#open-diagnostics"),
  closeDiagnostics: document.querySelector("#close-diagnostics"),
  hostSessionCode: document.querySelector("#host-session-code"),
  sessionExpiry: document.querySelector("#session-expiry"),
  sessionTemplateLabel: document.querySelector("#session-template-label"),
  meetingTypeContext: document.querySelector("#meeting-type-context"),
  hostMemberCount: document.querySelector("#host-member-count"),
  hostParticipantAvatars: document.querySelector("#host-participant-avatars"),
  hostParticipantList: document.querySelector("#host-participant-list"),
  focusSectionLabel: document.querySelector("#focus-section-label"),
  nextDiscussionHeading: document.querySelector("#next-discussion-heading"),
  planSectionLabel: document.querySelector("#plan-section-label"),
  livePlanHeading: document.querySelector("#live-plan-heading"),
  alertReviewLabel: document.querySelector("#alert-review-label"),
  alertReviewHeading: document.querySelector("#alert-review-heading"),
  knowledgeFileForm: document.querySelector("#knowledge-file-form"),
  knowledgeLinkForm: document.querySelector("#knowledge-link-form"),
  knowledgeList: document.querySelector("#knowledge-list"),
  knowledgeCount: document.querySelector("#knowledge-count"),
  memberPurposeTitle: document.querySelector("#member-purpose-title"),
  memberPurposeObjective: document.querySelector("#member-purpose-objective"),
  memberTemplateLabel: document.querySelector("#member-template-label"),
  memberSessionExpiry: document.querySelector("#member-session-expiry"),
  memberSessionStatus: document.querySelector("#member-session-status"),
  memberAudienceFamiliarity: document.querySelector("#member-audience-familiarity"),
  memberAudienceDescription: document.querySelector("#member-audience-description"),
  memberSuccessCriteria: document.querySelector("#member-success-criteria"),
  memberAlertCount: document.querySelector("#member-alert-count"),
  memberAlertUpdated: document.querySelector("#member-alert-updated"),
  memberRefreshSession: document.querySelector("#member-refresh-session"),
  memberLeaveSession: document.querySelector("#member-leave-session"),
  memberShowLatest: document.querySelector("#member-show-latest"),
  memberAlertLayerLabel: document.querySelector("#member-alert-layer-label"),
  memberAlertLayerHeading: document.querySelector("#member-alert-layer-heading"),
  memberAlertList: document.querySelector("#member-alert-list"),
  hostAlertPreviewKind: document.querySelector("#host-alert-preview-kind"),
  hostAlertPreviewTitle: document.querySelector("#host-alert-preview-title"),
  hostAlertPreviewContent: document.querySelector("#host-alert-preview-content"),
  hostMemberAlertCount: document.querySelector("#host-member-alert-count"),
  memberPreviewDialog: document.querySelector("#member-preview-dialog"),
  openMemberPreview: document.querySelector("#open-member-preview"),
  closeMemberPreview: document.querySelector("#close-member-preview"),
  memberPreviewTemplateLabel: document.querySelector("#member-preview-template-label"),
  memberPreviewLayerLabel: document.querySelector("#member-preview-layer-label"),
  memberPreviewAlertCount: document.querySelector("#member-preview-alert-count"),
  memberPreviewAlertList: document.querySelector("#member-preview-alert-list"),
  themeToggle: document.querySelector("#theme-toggle"),
  toastFallback: document.querySelector("#toast-fallback")
};

function normalizeTheme(theme) {
  return theme === "dark"
    ? "dark"
    : theme === "contrast"
      ? "contrast"
      : "light";
}

function updateThemeToggle(theme) {
  const darkModeActive = theme === "dark" || theme === "contrast";
  const title = darkModeActive
    ? "Switch to light mode"
    : "Switch to dark mode";
  elements.themeToggle.setAttribute("aria-pressed", String(darkModeActive));
  elements.themeToggle.title = title;
}

function applyTheme(theme, persistPreference = false) {
  const normalizedTheme = normalizeTheme(theme);
  document.documentElement.setAttribute("data-theme", normalizedTheme);
  updateThemeToggle(normalizedTheme);
  if (!persistPreference) {
    return;
  }

  try {
    window.localStorage.setItem(THEME_STORAGE_KEY, normalizedTheme);
  } catch (error) {
    console.warn("Theme preference could not be saved.", error);
  }
}

function applyTeamsTheme(theme) {
  applyTheme(theme);
}

elements.themeToggle.addEventListener("click", () => {
  const currentTheme = normalizeTheme(document.documentElement.dataset.theme);
  const nextTheme = currentTheme === "dark" || currentTheme === "contrast"
    ? "light"
    : "dark";
  applyTheme(nextTheme, true);
});
applyTheme(document.documentElement.dataset.theme);

async function initializeTeamsContext() {
  const teamsHosted = new URLSearchParams(window.location.search).get("host") === "teams";
  if (!teamsHosted) {
    return;
  }

  if (!window.microsoftTeams) {
    throw new Error("Microsoft Teams SDK could not be loaded.");
  }

  await window.microsoftTeams.app.initialize();
  const context = await window.microsoftTeams.app.getContext();
  state.teamsMeetingId = context.meeting?.id ?? null;
  if (context.app?.theme) {
    applyTeamsTheme(context.app.theme);
  }
  window.microsoftTeams.app.registerOnThemeChangeHandler?.(applyTeamsTheme);
}

const teamsContextReady = initializeTeamsContext();
initializeBrowserSpeechAvailability();
window.addEventListener("session-toast-closed", event => {
  if (event.detail?.source !== "contextual" || !event.detail.sourceId) {
    return;
  }

  state.activeContextualToastIds.delete(event.detail.sourceId);
  state.dismissedContextualCardIds.add(event.detail.sourceId);
});

document.querySelectorAll("[data-role-choice]").forEach(button => {
  button.addEventListener("click", () => showEntryPanel(button.dataset.roleChoice));
});
document.querySelectorAll("[data-back-to-roles]").forEach(button => {
  button.addEventListener("click", () => showEntryPanel(null));
});
elements.templateChoices.forEach(choice => {
  choice.addEventListener("change", applySelectedTemplate);
});
applySelectedTemplate();
document.querySelector("#member-session-code").addEventListener("input", event => {
  const normalized = event.currentTarget.value
    .toUpperCase()
    .replace(/[^2-9A-HJ-NP-Z]/g, "")
    .slice(0, 8);
  event.currentTarget.value = normalized.length > 4
    ? `${normalized.slice(0, 4)}-${normalized.slice(4)}`
    : normalized;
});

elements.microphoneConsent.addEventListener("change", renderMicrophoneControls);
elements.includeSystemAudio.addEventListener("change", renderMicrophoneControls);
elements.microphoneAccessKey.addEventListener("input", renderMicrophoneControls);
elements.microphoneToggle.addEventListener("click", async () => {
  if (!state.microphoneRecognizer
      && (!elements.microphoneConsent.checked
        || (!state.browserSpeechAuthorized
          && !elements.microphoneAccessKey.value))) {
    elements.microphoneUnlock.classList.remove("hidden");
    elements.microphoneUnlock.querySelector(
      elements.microphoneConsent.checked
        ? "#microphone-access-key"
        : "#microphone-consent")?.focus();
    return;
  }

  try {
    await queueMicrophoneOperation(async () => {
      if (state.microphoneRecognizer) {
        await stopMicrophone();
        showToast("Microphone transcription stopped.", "success");
      } else {
        await startMicrophone();
        showToast("Microphone transcription started.", "success");
      }
    });
  } catch (error) {
    showToast(normalizeMicrophoneError(error), "error");
  }
});
elements.confirmMicrophone.addEventListener("click", async () => {
  try {
    await queueMicrophoneOperation(startMicrophone);
    elements.microphoneUnlock.classList.add("hidden");
    showToast("Microphone transcription started.", "success");
  } catch (error) {
    showToast(normalizeMicrophoneError(error), "error");
  }
});
elements.closeMicrophoneUnlock.addEventListener("click", () => {
  elements.microphoneUnlock.classList.add("hidden");
  elements.microphoneToggle.focus();
});
elements.openDiagnostics.addEventListener("click", () => {
  elements.diagnosticsDialog.showModal();
});
elements.closeDiagnostics.addEventListener("click", () => {
  elements.diagnosticsDialog.close();
});
elements.diagnosticsDialog.addEventListener("click", event => {
  if (event.target === elements.diagnosticsDialog) {
    elements.diagnosticsDialog.close();
  }
});
elements.openMemberPreview.addEventListener("click", () => {
  elements.memberPreviewDialog.showModal();
});
elements.closeMemberPreview.addEventListener("click", () => {
  elements.memberPreviewDialog.close();
});
elements.memberPreviewDialog.addEventListener("click", event => {
  if (event.target === elements.memberPreviewDialog) {
    elements.memberPreviewDialog.close();
  }
});
elements.memberRefreshSession.addEventListener("click", async event => {
  await runWithButton(event.currentTarget, async () => {
    state.session = await api(`/api/sessions/${state.session.id}`);
    render();
    showToast("Member view refreshed.", "success");
  });
});
elements.memberLeaveSession.addEventListener("click", async event => {
  await runWithButton(event.currentTarget, async () => {
    await api(`/api/sessions/${state.session.id}/leave`, { method: "POST" });
    await returnToEntry();
    showToast("You left the session view.", "success");
  });
});
elements.memberShowLatest.addEventListener("click", () => {
  const latest = elements.memberAlertList.querySelector(".member-alert-card");
  latest?.scrollIntoView({ behavior: "smooth", block: "center" });
  latest?.focus({ preventScroll: true });
});
elements.sessionForm.addEventListener("submit", async event => {
  event.preventDefault();
  const template = selectedTemplate();
  const templateProfile = getTemplateProfile(template);
  const meetingType = template === "custom"
    ? elements.customMeetingType.value.trim()
    : templateProfile.meetingType;
  const successCriteria = document.querySelector("#success-criteria").value
    .split("\n")
    .map(value => value.trim())
    .filter(Boolean);

  await runWithButton(event.submitter, async () => {
    if (!meetingType) {
      throw new Error("Enter a custom session type.");
    }
    await teamsContextReady;
    const teamsHosted = new URLSearchParams(window.location.search).get("host") === "teams";
    if (teamsHosted && !state.teamsMeetingId) {
      throw new Error("The Teams meeting identifier is unavailable.");
    }

    const created = await api("/api/sessions/host", {
      method: "POST",
      body: JSON.stringify({
        purpose: {
          title: document.querySelector("#meeting-title").value.trim(),
          meetingType,
          objective: document.querySelector("#meeting-objective").value.trim(),
          successCriteria
        },
        teamsOnlineMeetingId: state.teamsMeetingId,
        template,
        hostDisplayName: document.querySelector("#host-display-name").value.trim(),
        memberAlertMode: document.querySelector("#member-alert-mode").value,
        audienceFamiliarity: document.querySelector("#audience-familiarity").value
      })
    });

    resetContextualCards();
    state.role = "host";
    state.session = created.session;
    state.joinCode = created.joinCode;
    persistSessionHistory();
    elements.setupView.classList.add("hidden");
    elements.sessionView.classList.remove("hidden");
    elements.memberSessionView.classList.add("hidden");
    window.scrollTo(0, 0);
    render();
    connectEvents(state.session.id);
  });
});

elements.memberJoinForm.addEventListener("submit", async event => {
  event.preventDefault();
  await runWithButton(event.submitter, async () => {
    const joined = await api("/api/sessions/join", {
      method: "POST",
      body: JSON.stringify({
        code: document.querySelector("#member-session-code").value,
        displayName: document.querySelector("#member-display-name").value
      })
    });
    resetContextualCards();
    state.role = "member";
    state.session = joined.session;
    state.joinCode = null;
    persistSessionHistory();
    elements.setupView.classList.add("hidden");
    elements.sessionView.classList.add("hidden");
    elements.memberSessionView.classList.remove("hidden");
    window.scrollTo(0, 0);
    render();
    connectEvents(state.session.id);
  });
});

elements.knowledgeFileForm.addEventListener("submit", async event => {
  event.preventDefault();
  await runWithButton(event.submitter, async () => {
    const file = document.querySelector("#knowledge-file").files[0];
    if (!file) {
      throw new Error("Choose one knowledge file.");
    }

    const form = new FormData();
    form.append("file", file, file.name);
    form.append(
      "visibility",
      document.querySelector("#knowledge-file-visibility").value);
    state.session = await api(
      `/api/sessions/${state.session.id}/knowledge/files`,
      { method: "POST", body: form });
    elements.knowledgeFileForm.reset();
    render();
    showToast("Knowledge file scanned and added.", "success");
  });
});

elements.knowledgeLinkForm.addEventListener("submit", async event => {
  event.preventDefault();
  await runWithButton(event.submitter, async () => {
    state.session = await api(
      `/api/sessions/${state.session.id}/knowledge/links`,
      {
        method: "POST",
        body: JSON.stringify({
          url: document.querySelector("#knowledge-link").value,
          visibility: document.querySelector("#knowledge-link-visibility").value
        })
      });
    elements.knowledgeLinkForm.reset();
    render();
    showToast("Knowledge link validated and added.", "success");
  });
});

elements.knowledgeList.addEventListener("click", async event => {
  const button = event.target instanceof Element
    ? event.target.closest("[data-delete-knowledge]")
    : null;
  if (!button) {
    return;
  }

  await runWithButton(button, async () => {
    state.session = await api(
      `/api/sessions/${state.session.id}/knowledge/${button.dataset.deleteKnowledge}`,
      { method: "DELETE" });
    render();
    showToast("Knowledge source removed.", "success");
  });
});

elements.transcriptForm.addEventListener("submit", async event => {
  event.preventDefault();
  await runWithButton(event.submitter, async () => {
    state.session = await api(`/api/sessions/${state.session.id}/transcript`, {
      method: "POST",
      body: JSON.stringify({
        speaker: document.querySelector("#speaker").value,
        text: document.querySelector("#transcript-text").value,
        isFinal: true
      })
    });
    document.querySelector("#transcript-text").value = "";
    render();
  });
});

document.querySelector("#complete-meeting").addEventListener("click", async event => {
  await runWithButton(event.currentTarget, async () => {
    cancelMicrophoneTokenRequests();
    await queueMicrophoneOperation(stopMicrophone);
    state.session = await api(`/api/sessions/${state.session.id}/complete`, {
      method: "POST"
    });
    render();
    showToast("Meeting session completed.", "success");
  });
});

elements.checklist.addEventListener("click", async event => {
  const button = event.target instanceof Element
    ? event.target.closest("button[data-reopen]")
    : null;
  if (!button || !elements.checklist.contains(button)) {
    return;
  }

  await runWithButton(button, async () => {
    state.session = await api(
      `/api/sessions/${state.session.id}/checklist/${button.dataset.reopen}/reopen`,
      { method: "POST" });
    render();
  });
});

elements.sessionView.addEventListener("click", async event => {
  const target = event.target instanceof Element ? event.target : null;
  const alertButton = target?.closest("button[data-alert-status]");
  if (alertButton) {
    await runWithButton(alertButton, async () => {
      state.session = await api(
        `/api/sessions/${state.session.id}/alerts/${alertButton.dataset.alertId}/status`,
        {
          method: "POST",
          body: JSON.stringify({ status: alertButton.dataset.alertStatus })
        });
      render();
    });
    return;
  }

  const statusButton = target?.closest("button[data-recommendation][data-status]");
  if (statusButton && elements.recommendations.contains(statusButton)) {
    await runWithButton(statusButton, async () => {
      state.session = await api(
        `/api/sessions/${state.session.id}/recommendations/${statusButton.dataset.recommendation}/status`,
        {
          method: "POST",
          body: JSON.stringify({ status: statusButton.dataset.status })
        });
      render();
    });
    return;
  }

  const reopenButton = target?.closest("button[data-recommendation-reopen]");
  if (reopenButton && elements.acceptedRecommendations.contains(reopenButton)) {
    await runWithButton(reopenButton, async () => {
      state.session = await api(
        `/api/sessions/${state.session.id}/recommendations/${reopenButton.dataset.recommendationReopen}/reopen`,
        { method: "POST" });
      render();
    });
  }
});

function connectEvents(sessionId) {
  state.eventSource?.close();
  state.eventSource = null;
  if (setTerminalConnectionStatus()) {
    return;
  }
  setConnectionStatus("Connecting", "neutral");

  state.eventSource = new EventSource(`/api/sessions/${sessionId}/events`);
  state.eventSource.addEventListener("open", async () => {
    if (setTerminalConnectionStatus()) {
      state.eventSource?.close();
      state.eventSource = null;
      return;
    }
    setConnectionStatus("Live", "connected");
    try {
      const current = await api(`/api/sessions/${sessionId}`);
      if (!state.session || current.revision >= state.session.revision) {
        state.session = current;
        render();
      }
    } catch (error) {
      showToast(
        `The meeting state could not be refreshed: ${error.message}`,
        "error");
    }
  });
  state.eventSource.addEventListener("session", event => {
    const incoming = JSON.parse(event.data);
    if (!state.session || incoming.revision >= state.session.revision) {
      state.session = incoming;
      render();
      if (incoming.status === "completed"
          && (state.microphoneRecognizer || state.microphoneBusy)) {
        cancelMicrophoneTokenRequests();
        state.speechPublishAbortController?.abort();
        void queueMicrophoneOperation(stopMicrophone).catch(error => {
          showToast(`Microphone could not be stopped: ${error.message}`, "error");
        });
      }
    }
  });
  state.eventSource.addEventListener("expired", () => {
    void handleSessionExpired();
  });
  state.eventSource.addEventListener("error", async () => {
    if (Date.parse(state.session?.expiresAtUtc) <= Date.now()) {
      await handleSessionExpired();
      return;
    }
    setConnectionStatus("Reconnecting", "disconnected");
    if (state.eventProbePending) {
      return;
    }

    state.eventProbePending = true;
    try {
      await api(`/api/sessions/${sessionId}`);
    } catch (error) {
      if (error.status === 404 || error.status === 410) {
        await handleSessionExpired();
      }
    } finally {
      state.eventProbePending = false;
    }
  });
}

function setConnectionStatus(text, className) {
  const element = state.role === "member"
    ? elements.memberConnectionStatus
    : elements.connectionStatus;
  element.textContent = text;
  element.className = `status ${className}`;
}

function setTerminalConnectionStatus() {
  if (state.session?.status === "completed") {
    setConnectionStatus("Ended", "ended");
    return true;
  }
  if (state.session?.status === "expired") {
    setConnectionStatus("Expired", "disconnected");
    return true;
  }
  return false;
}

function render() {
  if (state.session?.expiresAtUtc && state.session.status !== "expired") {
    scheduleSessionExpiry(state.session.expiresAtUtc);
  }
  if (setTerminalConnectionStatus()) {
    state.eventSource?.close();
    state.eventSource = null;
  }
  if (state.role === "member") {
    renderMember();
    return;
  }
  renderHost();
}

function renderHost() {
  const session = state.session;
  if (!session) {
    return;
  }

  const templateProfile = getTemplateProfile(session.template);
  elements.sessionView.dataset.template = session.template;
  elements.hostSessionCode.textContent = state.joinCode ?? "Unavailable";
  elements.sessionExpiry.textContent = formatExpiry(session.expiresAtUtc);
  elements.sessionExpiry.dateTime = session.expiresAtUtc;
  elements.sessionTemplateLabel.textContent = templateProfile.label;
  document.querySelector("#meeting-type-label").textContent =
    session.purpose.meetingType;
  const defaultMeetingType = templateProfile.meetingType
    ?.trim()
    .toLowerCase();
  elements.meetingTypeContext.classList.toggle(
    "hidden",
    Boolean(defaultMeetingType)
      && session.purpose.meetingType.trim().toLowerCase() === defaultMeetingType);
  document.querySelector("#purpose-title").textContent = session.purpose.title;
  document.querySelector("#purpose-objective").textContent = session.purpose.objective;
  const sessionClosed = session.status !== "active";
  document.querySelector("#complete-meeting").disabled = sessionClosed;
  document.querySelector("#transcript-form button").disabled = sessionClosed;
  elements.knowledgeFileForm.querySelector("button").disabled = sessionClosed;
  elements.knowledgeLinkForm.querySelector("button").disabled = sessionClosed;
  elements.focusSectionLabel.textContent = "Private AI guidance";
  elements.nextDiscussionHeading.textContent = templateProfile.focusLabel;
  elements.planSectionLabel.textContent = templateProfile.planHeading;
  elements.livePlanHeading.textContent = templateProfile.planLabel;
  elements.alertReviewLabel.textContent = "Member alert";
  elements.alertReviewHeading.textContent = templateProfile.memberLayerLabel;
  renderMicrophoneControls();
  const contextualCards = session.contextualCards ?? [];
  const proposedRecommendations = (session.recommendedTasks ?? []).filter(
    task => task.status === "proposed");
  renderContextualCards(contextualCards);
  renderContextualCardHistory(contextualCards);
  renderHostMemberExperience(contextualCards, templateProfile);
  renderKnowledge(session.knowledgeSources ?? []);
  renderParticipantPresence(session.participants ?? []);
  renderLiveSpeech(session, proposedRecommendations);

  const liveRecommendations = (session.recommendedTasks ?? []).filter(
    task => task.status === "accepted" || task.status === "completed");
  const completed = (session.checklist ?? []).filter(item => item.status === "completed").length
    + liveRecommendations.filter(task => task.status === "completed").length;
  const total = (session.checklist ?? []).length + liveRecommendations.length;
  const progressPercent = total === 0
    ? 0
    : Math.round((completed / total) * 100);
  document.querySelector("#progress-label").textContent =
    `${completed}/${total}`;
  elements.progressFill.style.width = `${progressPercent}%`;
  elements.planProgressOrb.style.setProperty(
    "--plan-progress",
    `${progressPercent}%`);

  elements.acceptedRecommendations.innerHTML = liveRecommendations.map(task => {
    const isComplete = task.status === "completed";
    const evidence = task.evidence?.at(-1);
    return `
      <div class="compact-item ${isComplete ? "completed" : "ready"}">
        <span class="item-indicator" aria-hidden="true">${isComplete ? "✓" : "→"}</span>
        <div class="item-copy">
          <span class="item-title">${escapeHtml(task.title)}</span>
          <span class="item-meta">
            ${isComplete && evidence
              ? `Discussed: “${escapeHtml(evidence.quote)}”`
              : escapeHtml(task.rationale)}
          </span>
        </div>
        ${isComplete ? `
          <button class="undo-button" type="button"
                  data-recommendation-reopen="${task.id}">Undo</button>
        ` : `<span class="badge ready">Ready</span>`}
      </div>`;
  }).join("");

  elements.checklist.innerHTML = (session.checklist ?? []).map(item => {
    const isComplete = item.status === "completed";
    const evidence = item.evidence.at(-1);
    return `
      <div class="compact-item ${isComplete ? "completed" : ""}">
        <span class="item-indicator" aria-hidden="true">${isComplete ? "✓" : ""}</span>
        <div class="item-copy">
          <span class="item-title">${escapeHtml(item.title)}</span>
          <span class="item-meta">
            ${evidence
              ? `“${escapeHtml(evidence.quote)}” · ${Math.round(evidence.confidence * 100)}%`
              : escapeHtml(item.completionCriteria)}
          </span>
        </div>
        ${evidence ? `
          <button class="undo-button" data-reopen="${item.id}">Undo</button>
        ` : `<span class="badge">Open</span>`}
      </div>`;
  }).join("");

  renderRecommendationToasts(proposedRecommendations);
  if (proposedRecommendations.length === 0) {
    elements.recommendations.className = session.isAnalyzing
      ? "recommendation-list"
      : "recommendation-list empty-state";
    elements.recommendations.textContent = session.isAnalyzing
      ? "Analyzing meeting context\u2026"
      : templateProfile.guidanceEmpty;
  } else {
    elements.recommendations.className = "recommendation-list";
    elements.recommendations.innerHTML = proposedRecommendations
      .map(task => {
        const basedOn = resolveSourceTranscript(task, session.transcript);
        const confidence = Math.round(Number(task.confidence) * 100);
        return `
        <article class="recommendation">
          <h3>${escapeHtml(task.title)}</h3>
          <p class="recommendation-rationale">${escapeHtml(task.rationale)}</p>
          ${basedOn.length > 0 ? `
            <details class="guidance-evidence">
              <summary>Grounding evidence</summary>
              <div class="based-on">
                ${basedOn.map(segment => `
                  <blockquote>“${escapeHtml(segment.text)}”</blockquote>
                `).join("")}
              </div>
            </details>
          ` : ""}
          <div class="actions">
            <button class="button primary" type="button"
                    data-recommendation="${task.id}" data-status="accepted">
               <span aria-hidden="true">✓</span> Accept
            </button>
            <button class="button subtle" type="button"
                    data-recommendation="${task.id}" data-status="dismissed">Dismiss</button>
            <span class="recommendation-fit">
              ${Number.isFinite(confidence) ? `${confidence}% fit` : "Grounded"}
            </span>
          </div>
        </article>`;
      }).join("");
  }

  if ((session.transcript ?? []).length === 0) {
    elements.transcript.className = "transcript empty-state";
    elements.transcript.textContent = "No transcript segments yet.";
  } else {
    elements.transcript.className = "transcript";
    elements.transcript.innerHTML = session.transcript
      .slice()
      .reverse()
      .map(segment => `
        <div class="transcript-entry">
          <strong>${escapeHtml(segment.speaker)}</strong>
          <span>${escapeHtml(segment.text)}</span>${segment.recognizedText
            ? ` <span class="speech-normalized" title="Original Speech SDK recognition retained for traceability">Speech normalized</span>`
            : ""}
        </div>`).join("");
  }

  elements.warningsPanel.classList.toggle(
    "hidden",
    (session.warnings ?? []).length === 0);
  renderWarningToasts(session.warnings ?? []);
  elements.warnings.innerHTML = (session.warnings ?? [])
    .map(warning => `<div class="warning">${escapeHtml(warning)}</div>`)
    .join("");
}

function renderLiveSpeech(session, proposedRecommendations) {
  const latestSegment = (session.transcript ?? [])
    .slice()
    .reverse()
    .find(segment => segment.isFinal !== false);
  if (!latestSegment) {
    elements.hostLiveTranscript.className =
      "host-live-transcript empty-state";
    elements.hostLiveTranscript.innerHTML = `
      <span class="speaker-avatar" aria-hidden="true">H</span>
      <div>
        <strong>Waiting for final speech</strong>
        <p>The latest grounded transcript segment will appear here.</p>
      </div>`;
  } else {
    const speaker = String(latestSegment.speaker || "Speaker");
    elements.hostLiveTranscript.className = "host-live-transcript";
    elements.hostLiveTranscript.innerHTML = `
      <span class="speaker-avatar" aria-hidden="true">
        ${escapeHtml(initialsForName(speaker))}
      </span>
      <div class="live-transcript-copy">
        <div>
          <strong>${escapeHtml(speaker)}</strong>
          <time datetime="${escapeAttribute(latestSegment.occurredAtUtc ?? "")}">
            ${escapeHtml(formatTranscriptMoment(latestSegment.occurredAtUtc))}
          </time>
        </div>
        <p>“${escapeHtml(latestSegment.text)}”</p>
      </div>`;
  }

  const recommendation = proposedRecommendations.at(0);
  if (recommendation) {
    const confidence = Math.round(Number(recommendation.confidence) * 100);
    elements.hostContextSignal.textContent = Number.isFinite(confidence)
      ? `Grounded recommendation ready · ${confidence}% confidence`
      : "Grounded recommendation ready for Host review.";
  } else if (session.isAnalyzing) {
    elements.hostContextSignal.textContent =
      "New final speech is being analyzed for grounded guidance.";
  } else if (latestSegment) {
    elements.hostContextSignal.textContent =
      "No grounded recommendation signal is active.";
  } else {
    elements.hostContextSignal.textContent =
      "Waiting for final speech evidence.";
  }
}

function renderHostMemberExperience(cards, templateProfile) {
  const publishedCards = cards
    .filter(card => card.memberAlertStatus === "published")
    .slice()
    .reverse();
  const latestCard = publishedCards.at(0);
  const countLabel = `${publishedCards.length} ${publishedCards.length === 1
    ? "alert"
    : "alerts"}`;

  elements.hostMemberAlertCount.textContent = countLabel;
  elements.memberPreviewTemplateLabel.textContent = templateProfile.label;
  elements.memberPreviewLayerLabel.textContent = templateProfile.memberLayerLabel;
  elements.memberPreviewAlertCount.textContent = String(publishedCards.length);

  if (!latestCard) {
    elements.hostAlertPreviewKind.textContent = "Waiting";
    elements.hostAlertPreviewTitle.textContent =
      "No published Member alert yet";
    elements.hostAlertPreviewContent.textContent =
      "Eligible definitions and hints will appear here after delivery.";
    elements.memberPreviewAlertList.className =
      "member-preview-alert-list empty-state";
    elements.memberPreviewAlertList.textContent =
      "No published Member alerts yet.";
    return;
  }

  elements.hostAlertPreviewKind.textContent =
    contextualCardKindLabel(latestCard);
  elements.hostAlertPreviewTitle.textContent = latestCard.title;
  elements.hostAlertPreviewContent.textContent = latestCard.content;
  elements.memberPreviewAlertList.className = "member-preview-alert-list";
  elements.memberPreviewAlertList.innerHTML = publishedCards
    .map(memberAlertMarkup)
    .join("");
}

function renderMember() {
  const session = state.session;
  if (!session) {
    return;
  }

  const templateProfile = getTemplateProfile(session.template);
  elements.memberSessionView.dataset.template = session.template;
  elements.memberPurposeTitle.textContent = session.purpose.title;
  elements.memberPurposeObjective.textContent = session.purpose.objective;
  elements.memberTemplateLabel.textContent = `${templateProfile.label} · member`;
  elements.memberAlertLayerLabel.textContent = templateProfile.memberLayerLabel;
  elements.memberAlertLayerHeading.textContent = templateProfile.memberLayerHeading;
  const familiarity = describeAudienceFamiliarity(session.audienceFamiliarity);
  elements.memberAudienceFamiliarity.textContent = familiarity.label;
  elements.memberAudienceDescription.textContent = familiarity.description;
  elements.memberSessionStatus.textContent = session.status === "active"
    ? "Live"
    : session.status === "completed"
      ? "Completed"
      : "Expired";
  elements.memberSessionStatus.dataset.status = session.status;
  elements.memberSuccessCriteria.innerHTML = (session.purpose.successCriteria ?? [])
    .map(item => `<li>${escapeHtml(item)}</li>`)
    .join("") || "<li>No explicit criteria were provided.</li>";
  elements.memberSessionExpiry.textContent = formatExpiry(session.expiresAtUtc);
  elements.memberSessionExpiry.dateTime = session.expiresAtUtc;
  const alerts = session.alerts ?? [];
  elements.memberAlertCount.textContent = String(alerts.length);
  elements.memberShowLatest.disabled = alerts.length === 0;
  const latestAlert = alerts.at(-1);
  elements.memberAlertUpdated.textContent = latestAlert
    ? `Latest approved alert ${formatRelativeMoment(latestAlert.createdAtUtc)}.`
    : "Waiting for the first approved alert.";
  renderContextualCards(alerts);
  if (alerts.length === 0) {
    elements.memberAlertList.className = "member-alert-list empty-state";
    elements.memberAlertList.textContent =
      "Approved alerts will appear here as the session progresses.";
    return;
  }

  elements.memberAlertList.className = "member-alert-list";
  elements.memberAlertList.innerHTML = alerts
    .slice()
    .reverse()
    .map((card, index) => memberAlertMarkup(card, index === 0))
    .join("");
}

function contextualCardKindLabel(card) {
  return String(card.kind).toLowerCase() === "definition"
    ? "Definition"
    : "Useful context";
}

function memberAlertMarkup(card, isLatest = false) {
  return `
    <article class="member-alert-card ${escapeHtml(String(card.kind).toLowerCase())}${isLatest ? " latest" : ""}"
             tabindex="-1">
      <span class="contextual-card-kind">${contextualCardKindLabel(card)}</span>
      <h3>${escapeHtml(card.title)}</h3>
      <p>${escapeHtml(card.content)}</p>
    </article>`;
}

function initialsForName(displayName) {
  return String(displayName || "")
    .trim()
    .split(/\s+/)
    .slice(0, 2)
    .map(part => part.charAt(0).toUpperCase())
    .join("") || "M";
}

function renderParticipantPresence(participants) {
  const members = participants
    .filter(participant =>
      String(participant.role).toLowerCase() === "member")
    .sort((left, right) =>
      Date.parse(left.joinedAtUtc) - Date.parse(right.joinedAtUtc));

  if (state.participantTrackingInitialized) {
    members
      .filter(member => !state.knownParticipantIds.has(member.id))
      .forEach(member => {
        showToast(`${member.displayName} joined the session.`, "success", {
          title: "Member joined"
        });
      });
  }

  members.forEach(member => state.knownParticipantIds.add(member.id));
  state.participantTrackingInitialized = true;
  elements.hostMemberCount.textContent = String(members.length);
  elements.hostParticipantAvatars.className = members.length === 0
    ? "participant-avatar-list empty-state"
    : "participant-avatar-list";
  elements.hostParticipantAvatars.innerHTML = members.length === 0
    ? "<span>No members yet</span>"
    : `${members.slice(0, 4).map((member, index) => `
        <span class="participant-avatar-chip avatar-${index + 1}"
              title="${escapeAttribute(member.displayName)}"
              aria-label="${escapeAttribute(member.displayName)}">
          ${escapeHtml(initialsForName(member.displayName))}
        </span>`).join("")}${members.length > 4 ? `
        <span class="participant-avatar-chip avatar-more"
              aria-label="${members.length - 4} more joined members">
          +${members.length - 4}
        </span>` : ""}`;

  if (members.length === 0) {
    elements.hostParticipantList.className = "participant-list empty-state";
    elements.hostParticipantList.textContent = "No members have joined yet.";
    return;
  }

  elements.hostParticipantList.className = "participant-list";
  elements.hostParticipantList.innerHTML = members.map(member => {
    const joinedAt = new Date(member.joinedAtUtc);
    const joinedLabel = Number.isNaN(joinedAt.getTime())
      ? "Joined"
      : `Joined ${joinedAt.toLocaleTimeString([], {
          hour: "2-digit",
          minute: "2-digit"
        })}`;
    const initials = initialsForName(member.displayName);
    return `
      <div class="participant-item"
           title="${escapeAttribute(member.displayName)} · ${escapeAttribute(joinedLabel)}">
        <span class="participant-avatar" aria-hidden="true">${escapeHtml(initials)}</span>
        <span class="participant-copy">
          <strong>${escapeHtml(member.displayName)}</strong>
          <span>${escapeHtml(joinedLabel)}</span>
        </span>
        <span class="participant-state">Joined</span>
      </div>`;
  }).join("");
}

function renderKnowledge(sources) {
  elements.knowledgeCount.textContent = `${sources.length}/50`;
  if (sources.length === 0) {
    elements.knowledgeList.className = "knowledge-list empty-state";
    elements.knowledgeList.textContent = "No session knowledge added.";
    return;
  }

  elements.knowledgeList.className = "knowledge-list";
  elements.knowledgeList.innerHTML = sources
    .slice()
    .reverse()
    .map(source => `
      <div class="knowledge-item">
        <span class="knowledge-source-icon" aria-hidden="true">
          ${source.kind === "link" ? "↗" : "▤"}
        </span>
        <span class="knowledge-item-copy">
          <strong>${escapeHtml(source.displayName)}</strong>
          <span>${formatEnumLabel(source.visibility)} · ${formatEnumLabel(source.status)}</span>
        </span>
        <button type="button" class="icon-button knowledge-delete"
                data-delete-knowledge="${source.id}"
                aria-label="Remove ${escapeHtml(source.displayName)}">×</button>
      </div>`)
    .join("");
}

function renderContextualCards(cards) {
  const currentIds = new Set(cards
    .filter(card => card.memberAlertStatus !== "hidden")
    .map(card => card.id));
  for (const activeId of state.activeContextualToastIds) {
    if (!currentIds.has(activeId)) {
      window.sessionToast?.dismiss(`contextual-${activeId}`);
      state.activeContextualToastIds.delete(activeId);
    }
  }

  cards
    .filter(card => card.memberAlertStatus !== "hidden")
    .filter(card => !state.dismissedContextualCardIds.has(card.id))
    .forEach(card => {
      if (state.activeContextualToastIds.has(card.id)) {
        return;
      }

      state.activeContextualToastIds.add(card.id);
      showToast(card.content, String(card.kind).toLowerCase(), {
        autoClose: 14000,
        id: `contextual-${card.id}`,
        source: "contextual",
        sourceId: card.id,
        title: card.title
      });
    });
}

function renderRecommendationToasts(recommendations) {
  recommendations.forEach(recommendation => {
    if (state.notifiedRecommendationIds.has(recommendation.id)) {
      return;
    }

    state.notifiedRecommendationIds.add(recommendation.id);
    showToast(recommendation.rationale, "recommendation", {
      autoClose: 16000,
      id: `recommendation-${recommendation.id}`,
      source: "recommendation",
      sourceId: recommendation.id,
      title: recommendation.title
    });
  });
}

function renderWarningToasts(warnings) {
  warnings.forEach(warning => {
    if (state.notifiedWarningMessages.has(warning)) {
      return;
    }

    state.notifiedWarningMessages.add(warning);
    showToast(warning, "warning", {
      id: `session-warning-${state.notifiedWarningMessages.size}`,
      source: "session-warning",
      title: "Session warning"
    });
  });
}

function renderContextualCardHistory(cards) {
  if (cards.length === 0) {
    elements.contextualCardHistory.className =
      "contextual-card-history diagnostic-content empty-state";
    elements.contextualCardHistory.textContent = "No contextual cards yet.";
    return;
  }

  elements.contextualCardHistory.className =
    "contextual-card-history diagnostic-content";
  elements.contextualCardHistory.innerHTML = cards
    .slice()
    .reverse()
    .map(card => {
      const kindLabel = String(card.kind).toLowerCase() === "definition"
        ? "Definition"
        : "Hint";
      const deliveryStatus = card.memberAlertStatus ?? "published";
      const deliveryLabel = deliveryStatus === "pendingApproval"
        ? "Waiting for host"
        : deliveryStatus === "published"
          ? "Published"
          : "Hidden";
      return `
        <div class="contextual-card-history-entry">
          <div class="alert-history-heading">
            <span class="contextual-card-kind">${kindLabel}</span>
            <span class="badge ${deliveryStatus === "published" ? "success" : ""}">
              ${deliveryLabel}
            </span>
          </div>
          <strong>${escapeHtml(card.title)}</strong>
          <span>${escapeHtml(card.content)}</span>
          ${deliveryStatus === "pendingApproval" ? `
            <div class="actions">
              <button type="button" class="button primary"
                      data-alert-id="${card.id}" data-alert-status="published">
                Publish to members
              </button>
              <button type="button" class="button subtle"
                      data-alert-id="${card.id}" data-alert-status="hidden">
                Keep private
              </button>
            </div>` : ""}
        </div>`;
    })
    .join("");
}

function resetContextualCards() {
  state.activeContextualToastIds.clear();
  state.dismissedContextualCardIds.clear();
  state.notifiedRecommendationIds.clear();
  state.notifiedWarningMessages.clear();
  state.knownParticipantIds.clear();
  state.participantTrackingInitialized = false;
  window.sessionToast?.clear();
}

async function startMicrophone() {
  if (!state.session || state.session.status !== "active") {
    throw new Error("Create an active coaching session before starting the microphone.");
  }
  const sessionId = state.session.id;
  if (!elements.microphoneConsent.checked) {
    throw new Error("Confirm participant notice and permission before starting.");
  }
  if (!state.browserSpeechAvailable) {
    throw new Error("Browser microphone transcription is not configured.");
  }
  if (!state.browserSpeechAuthorized
      && !elements.microphoneAccessKey.value) {
    throw new Error("Enter the demo access code before starting.");
  }
  if (!window.SpeechSDK) {
    throw new Error("Azure Speech SDK could not be loaded.");
  }

  state.microphoneBusy = true;
  resetSpeechDiagnostics();
  renderMicrophoneControls();
  const startupAbortController = new AbortController();
  const speechPublishAbortController = new AbortController();
  state.microphoneStartupAbortController = startupAbortController;
  state.speechPublishAbortController = speechPublishAbortController;
  let recognizer = null;
  let audioConfig = null;
  let capture = null;
  let recognitionStarted = false;
  try {
    if (elements.includeSystemAudio.checked) {
      capture = await createMixedMeetingAudioCapture();
      state.microphoneCapture = capture;
      attachSystemAudioEndedHandler(capture);
      ensureMixedMeetingAudioActive(capture);
      ensureMicrophoneSessionActive(sessionId);
    }
    const token = await requestSpeechToken(
      sessionId,
      startupAbortController.signal);
    ensureMixedMeetingAudioActive(capture);
    ensureMicrophoneSessionActive(sessionId);
    const speechConfig = window.SpeechSDK.SpeechConfig.fromAuthorizationToken(
      token.token,
      token.region);
    state.speechDiagnostics.customSpeechActive = Boolean(token.endpointId);
    if (token.endpointId) {
      speechConfig.endpointId = token.endpointId;
      recordSpeechDiagnostic("Custom Speech language model active.");
    }
    speechConfig.speechRecognitionLanguage = token.language;
    speechConfig.setProperty(
      window.SpeechSDK.PropertyId.Speech_SegmentationSilenceTimeoutMs,
      "1200");
    speechConfig.setProperty(
      window.SpeechSDK.PropertyId.Speech_SegmentationStrategy,
      "Time");
    speechConfig.setProperty(
      window.SpeechSDK.PropertyId.Speech_SegmentationMaximumTimeMs,
      "20000");
    speechConfig.setProperty(
      window.SpeechSDK.PropertyId.SpeechServiceConnection_EndSilenceTimeoutMs,
      "1200");
    audioConfig = capture
      ? window.SpeechSDK.AudioConfig.fromStreamInput(capture.stream)
      : window.SpeechSDK.AudioConfig.fromDefaultMicrophoneInput();
    recognizer = new window.SpeechSDK.SpeechRecognizer(speechConfig, audioConfig);

    if (token.phrases && token.phrases.length > 0
        && window.SpeechSDK.PhraseListGrammar) {
      const phraseList = window.SpeechSDK.PhraseListGrammar.fromRecognizer(recognizer);
      phraseList.addPhrases(token.phrases);
      phraseList.setWeight(2.0);
      state.speechPhraseList = phraseList;
      state.speechDiagnostics.phraseVocabCount = token.phrases.length;
      recordSpeechDiagnostic(
        `Speech phrase vocabulary configured (${token.phrases.length} entries). Contextual correction enabled.`);
    }

    recognizer.recognizing = (_, event) => {
      const text = event.result?.text?.trim();
      if (text) {
        recordSpeechDiagnostic("Interim speech received.", "interim");
        elements.microphonePreview.textContent = text;
      }
    };
    recognizer.recognized = (_, event) => {
      if (event.result?.reason === window.SpeechSDK.ResultReason.NoMatch) {
        recordSpeechDiagnostic("Azure Speech returned no final phrase.");
        elements.microphonePreview.textContent =
          "No final phrase was recognized. Pause briefly, then try again.";
        return;
      }
      if (event.result?.reason !== window.SpeechSDK.ResultReason.RecognizedSpeech) {
        return;
      }

      const text = event.result.text?.trim();
      const recognitionId = event.result.resultId;
      if (!text) {
        return;
      }
      recordSpeechDiagnostic("Final speech received.", "final");
      if (recognitionId && state.seenRecognitionIds.has(recognitionId)) {
        recordSpeechDiagnostic("Duplicate final speech ignored.");
        return;
      }
      if (recognitionId) {
        state.seenRecognitionIds.add(recognitionId);
      }

      elements.microphonePreview.textContent =
        "Final phrase received; sending it to the coach.";
      enqueueSpeechSegment(
        text,
        capture
          ? "Meeting audio (microphone + system)"
          : "Presenter microphone");
    };
    recognizer.sessionStarted = () => {
      if (state.microphoneRecognizer === recognizer) {
        recordSpeechDiagnostic("Azure Speech session active.");
      }
    };
    recognizer.canceled = (_, event) => {
      if (state.microphoneRecognizer !== recognizer) {
        return;
      }

      recordSpeechDiagnostic("Azure Speech recognition canceled.");
      const detail = event.errorDetails?.trim();
      showToast(detail
        ? `Microphone recognition stopped: ${detail}`
        : "Microphone recognition was canceled.", "error");
      void queueMicrophoneOperation(stopMicrophone);
    };
    recognizer.sessionStopped = () => {
      recordSpeechDiagnostic("Azure Speech session stopped.");
      if (state.microphoneRecognizer === recognizer && !state.microphoneBusy) {
        void queueMicrophoneOperation(stopMicrophone);
      }
    };

    state.microphoneRecognizer = recognizer;
    state.microphoneAudioConfig = audioConfig;
    state.microphoneCapture = capture;
    state.microphoneSourceLabel = capture
      ? "Meeting audio (microphone + system)"
      : "Presenter microphone";
    state.seenRecognitionIds.clear();
    await startContinuousRecognition(recognizer);
    recognitionStarted = true;
    ensureMixedMeetingAudioActive(capture);
    ensureMicrophoneSessionActive(sessionId);
    elements.microphoneUnlock.classList.add("hidden");
    elements.microphonePreview.textContent = capture
      ? "Listening to your microphone and shared meeting audio…"
      : "Listening for the next discussion point…";
    scheduleSpeechTokenRefresh(token, recognizer);
  } catch (error) {
    state.microphoneRecognizer = null;
    state.speechPhraseList = null;
    state.microphoneAudioConfig = null;
    state.microphoneCapture = null;
    state.microphoneSourceLabel = "Presenter microphone";
    state.speechDiagnostics.phraseVocabCount = 0;
    speechPublishAbortController.abort();
    if (state.speechPublishAbortController === speechPublishAbortController) {
      state.speechPublishAbortController = null;
    }
    if (recognitionStarted) {
      await stopContinuousRecognition(recognizer);
    }
    recognizer?.close();
    audioConfig?.close();
    await closeMixedMeetingAudioCapture(capture);
    state.seenRecognitionIds.clear();
    recordSpeechDiagnostic("Microphone start failed.");
    if (capture?.ended) {
      throw new Error("Shared meeting audio stopped before recognition started.");
    }
    throw error;
  } finally {
    if (state.microphoneStartupAbortController === startupAbortController) {
      state.microphoneStartupAbortController = null;
    }
    state.microphoneBusy = false;
    renderMicrophoneControls();
  }
}

async function stopMicrophone() {
  const recognizer = state.microphoneRecognizer;
  const audioConfig = state.microphoneAudioConfig;
  const capture = state.microphoneCapture;
  const speechPublishAbortController = state.speechPublishAbortController;
  state.microphoneBusy = true;
  cancelMicrophoneTokenRequests();
  state.microphoneRecognizer = null;
  state.speechPhraseList = null;
  state.microphoneAudioConfig = null;
  state.microphoneCapture = null;
  state.microphoneSourceLabel = "Presenter microphone";
  window.clearTimeout(state.microphoneRefreshTimer);
  state.microphoneRefreshTimer = null;
  renderMicrophoneControls();

  try {
    if (recognizer) {
      const stopped = await stopContinuousRecognition(recognizer);
      if (!stopped) {
        showToast(
          "Speech SDK did not confirm shutdown; microphone resources were closed.",
          "warning");
      }
      recognizer.close();
    }
    audioConfig?.close();
    await closeMixedMeetingAudioCapture(capture);
    await drainSpeechPublishQueue(speechPublishAbortController);
    recordSpeechDiagnostic("Speech publishing stopped.");
  } finally {
    speechPublishAbortController?.abort();
    if (state.speechPublishAbortController === speechPublishAbortController) {
      state.speechPublishAbortController = null;
    }
    state.microphoneBusy = false;
    elements.microphonePreview.textContent =
      "Tap the microphone to resume live coaching.";
    renderMicrophoneControls();
  }
}

async function requestSpeechToken(sessionId = state.session?.id, signal) {
  const accessKey = state.browserSpeechAuthorized
    ? ""
    : elements.microphoneAccessKey.value.trim();
  if (accessKey && !/^[\x21-\x7e]{32,256}$/.test(accessKey)) {
    throw new Error(
      "The demo access code must contain 32 to 256 printable ASCII characters.");
  }
  try {
    const token = await api(`/api/sessions/${sessionId}/speech-token`, {
      method: "POST",
      signal,
      headers: accessKey
        ? { "X-Browser-Speech-Key": accessKey }
        : {}
    });
    state.browserSpeechAuthorized = true;
    elements.microphoneAccessKey.value = "";
    return token;
  } catch (error) {
    if (/valid browser speech access code/i.test(error?.message || "")) {
      state.browserSpeechAuthorized = false;
      elements.microphoneUnlock.classList.remove("hidden");
    }
    throw error;
  }
}

function ensureMicrophoneSessionActive(sessionId) {
  if (!state.session
      || state.session.id !== sessionId
      || state.session.status !== "active") {
    throw new Error("The meeting session completed before microphone activation.");
  }
}

function scheduleSpeechTokenRefresh(token, recognizer) {
  window.clearTimeout(state.microphoneRefreshTimer);
  const expiresAt = Date.parse(token.expiresAtUtc);
  const refreshDelay = Number.isFinite(expiresAt)
    ? Math.max(60_000, expiresAt - Date.now() - 60_000)
    : 8 * 60_000;

  state.microphoneRefreshTimer = window.setTimeout(async () => {
    if (state.microphoneRecognizer !== recognizer) {
      return;
    }

    const refreshAbortController = new AbortController();
    state.microphoneRefreshAbortController = refreshAbortController;
    try {
      const refreshed = await requestSpeechToken(
        state.session?.id,
        refreshAbortController.signal);
      if (state.microphoneRecognizer === recognizer) {
        recognizer.authorizationToken = refreshed.token;
        scheduleSpeechTokenRefresh(refreshed, recognizer);
      }
    } catch (error) {
      if (refreshAbortController.signal.aborted
          || state.microphoneRecognizer !== recognizer) {
        return;
      }
      showToast(
        `Speech authorization could not be renewed: ${error.message}`,
        "error");
      await queueMicrophoneOperation(stopMicrophone);
    } finally {
      if (state.microphoneRefreshAbortController === refreshAbortController) {
        state.microphoneRefreshAbortController = null;
      }
    }
  }, refreshDelay);
}

function enqueueSpeechSegment(text, speaker = state.microphoneSourceLabel) {
  const abortController = state.speechPublishAbortController;
  if (!abortController || abortController.signal.aborted) {
    recordSpeechDiagnostic("Final speech could not be queued because publishing is stopped.");
    return;
  }
  const segment = {
    speaker,
    text,
    occurredAtUtc: new Date().toISOString(),
    isFinal: true,
    isSpeechRecognized: true,
    sourceSegmentId: window.crypto.randomUUID()
  };
  recordSpeechDiagnostic("Final speech queued for the Coach API.", "queued");

  state.speechPublishQueue = state.speechPublishQueue
    .then(async () => {
      const updated = await api(`/api/sessions/${state.session.id}/transcript`, {
        method: "POST",
        signal: abortController.signal,
        body: JSON.stringify(segment)
      });
      if (!state.session || updated.revision >= state.session.revision) {
        state.session = updated;
        render();
      }
      const latestSegment = updated?.transcript?.at(-1);
      if (latestSegment?.recognizedText) {
        state.speechDiagnostics.corrections = (state.speechDiagnostics.corrections || 0) + 1;
        recordSpeechDiagnostic(
          `Coach API publish succeeded. Speech normalization applied (count: ${state.speechDiagnostics.corrections}).`,
          "published");
      } else {
        recordSpeechDiagnostic("Coach API publish succeeded.", "published");
      }
      elements.microphonePreview.textContent =
        "Final phrase sent. Live coaching is updating.";
    })
    .catch(error => {
      if (!abortController.signal.aborted) {
        recordSpeechDiagnostic("Coach API publish failed.", "publishFailures");
        showToast(
          `A recognized segment could not be processed: ${error.message}`,
          "error");
      }
    });
}

function resetSpeechDiagnostics() {
  state.speechDiagnostics = {
    interim: 0,
    final: 0,
    queued: 0,
    published: 0,
    publishFailures: 0,
    corrections: 0,
    phraseVocabCount: 0,
    customSpeechActive: false,
    lastStage: "Waiting for speech events.",
    lastEventAt: null
  };
  renderSpeechDiagnostics();
}

function recordSpeechDiagnostic(stage, counter) {
  if (counter && Object.hasOwn(state.speechDiagnostics, counter)) {
    state.speechDiagnostics[counter] += 1;
  }
  state.speechDiagnostics.lastStage = stage;
  state.speechDiagnostics.lastEventAt = new Date();
  renderSpeechDiagnostics();
}

function renderSpeechDiagnostics() {
  const diagnostics = state.speechDiagnostics;
  const time = diagnostics.lastEventAt
    ? diagnostics.lastEventAt.toLocaleTimeString([], {
        hour: "2-digit",
        minute: "2-digit",
        second: "2-digit"
      })
    : "not yet";
  const phraseInfo = diagnostics.phraseVocabCount > 0
    ? ` · Phrase vocab: ${diagnostics.phraseVocabCount}`
    : "";
  const correctionInfo = diagnostics.corrections > 0
    ? ` · Speech corrections: ${diagnostics.corrections}`
    : "";
  const modelInfo = diagnostics.customSpeechActive
    ? " · Speech model: custom"
    : " · Speech model: base";
  elements.speechDiagnostics.textContent =
    `Interim: ${diagnostics.interim} · Final: ${diagnostics.final} · `
    + `Queued: ${diagnostics.queued} · Published: ${diagnostics.published} · `
    + `Publish failures: ${diagnostics.publishFailures}${phraseInfo}${correctionInfo}${modelInfo}. `
    + `Last stage: ${diagnostics.lastStage} (${time})`;
}

async function drainSpeechPublishQueue(abortController) {
  const queue = state.speechPublishQueue;
  let timeoutId;
  const drained = await Promise.race([
    queue.then(() => true),
    new Promise(resolve => {
      timeoutId = window.setTimeout(() => resolve(false), 5000);
    })
  ]);
  window.clearTimeout(timeoutId);
  if (drained) {
    return;
  }

  abortController?.abort();
  await queue;
  showToast(
    "Microphone stopped before all final speech segments could be uploaded.",
    "warning");
}

async function createMixedMeetingAudioCapture() {
  if (!state.systemAudioCaptureAvailable) {
    throw new Error(
      "This browser cannot capture meeting audio. Open the coach in Microsoft Edge or Chrome.");
  }

  let displayStream = null;
  let microphoneStream = null;
  let audioContext = null;
  try {
    const displayPromise = navigator.mediaDevices.getDisplayMedia({
      video: {
        displaySurface: "monitor"
      },
      audio: true,
      systemAudio: "include",
      selfBrowserSurface: "exclude",
      surfaceSwitching: "exclude",
      monitorTypeSurfaces: "include"
    });
    displayStream = await displayPromise;
    if (displayStream.getAudioTracks().length === 0) {
      throw new Error(
        "System audio was not shared. Choose the Teams tab or Entire screen and enable Share system audio.");
    }

    for (const videoTrack of displayStream.getVideoTracks()) {
      videoTrack.enabled = false;
    }

    microphoneStream = await navigator.mediaDevices.getUserMedia({
      video: false,
      audio: {
        echoCancellation: true,
        noiseSuppression: true,
        autoGainControl: true
      }
    });

    const AudioContextType = window.AudioContext || window.webkitAudioContext;
    audioContext = new AudioContextType();
    const mixedDestination = audioContext.createMediaStreamDestination();
    const compressor = audioContext.createDynamicsCompressor();
    const microphoneGain = audioContext.createGain();
    const systemGain = audioContext.createGain();
    microphoneGain.gain.value = 0.85;
    systemGain.gain.value = 0.85;

    audioContext
      .createMediaStreamSource(microphoneStream)
      .connect(microphoneGain)
      .connect(compressor);
    audioContext
      .createMediaStreamSource(displayStream)
      .connect(systemGain)
      .connect(compressor);
    compressor.connect(mixedDestination);
    if (audioContext.state === "suspended") {
      await audioContext.resume();
    }

    return {
      stream: mixedDestination.stream,
      displayStream,
      microphoneStream,
      audioContext,
      closing: false,
      ended: false
    };
  } catch (error) {
    stopMediaStream(displayStream);
    stopMediaStream(microphoneStream);
    if (audioContext && audioContext.state !== "closed") {
      await audioContext.close().catch(() => {});
    }
    throw error;
  }
}

function attachSystemAudioEndedHandler(capture) {
  const handleEnded = () => {
    if (capture.closing
        || capture.ended
        || state.microphoneCapture !== capture) {
      return;
    }

    capture.ended = true;
    cancelMicrophoneTokenRequests();
    showToast(
      "Shared meeting audio stopped. Speech recognition is stopping safely.",
      "warning");
    void queueMicrophoneOperation(stopMicrophone);
  };

  for (const track of capture.displayStream.getTracks()) {
    track.addEventListener("ended", handleEnded, { once: true });
  }
}

function ensureMixedMeetingAudioActive(capture) {
  if (!capture) {
    return;
  }

  const tracks = capture.displayStream.getTracks();
  if (capture.ended
      || tracks.length === 0
      || tracks.some(track => track.readyState !== "live")) {
    capture.ended = true;
    throw new Error("Shared meeting audio stopped before recognition started.");
  }
}

async function closeMixedMeetingAudioCapture(capture) {
  if (!capture || capture.closing) {
    return;
  }

  capture.closing = true;
  stopMediaStream(capture.stream);
  stopMediaStream(capture.displayStream);
  stopMediaStream(capture.microphoneStream);
  if (capture.audioContext.state !== "closed") {
    await capture.audioContext.close().catch(() => {});
  }
}

function stopMediaStream(stream) {
  for (const track of stream?.getTracks() ?? []) {
    track.stop();
  }
}

function cancelMicrophoneTokenRequests() {
  state.microphoneStartupAbortController?.abort();
  state.microphoneRefreshAbortController?.abort();
}

function startContinuousRecognition(recognizer) {
  return new Promise((resolve, reject) => {
    let settled = false;
    const settle = callback => value => {
      if (settled) {
        return;
      }
      settled = true;
      window.clearTimeout(timeoutId);
      callback(value);
    };
    const timeoutId = window.setTimeout(
      settle(reject),
      10_000,
      new Error("Microphone recognition did not start within 10 seconds."));
    try {
      recognizer.startContinuousRecognitionAsync(
        settle(resolve),
        settle(reject));
    } catch (error) {
      settle(reject)(error);
    }
  });
}

function stopContinuousRecognition(recognizer) {
  return new Promise(resolve => {
    let settled = false;
    const settle = result => {
      if (settled) {
        return;
      }
      settled = true;
      window.clearTimeout(timeoutId);
      resolve(result);
    };
    const timeoutId = window.setTimeout(() => settle(false), 5000);
    try {
      recognizer.stopContinuousRecognitionAsync(
        () => settle(true),
        () => settle(false));
    } catch {
      settle(false);
    }
  });
}

function renderMicrophoneControls() {
  elements.microphonePanel.classList.toggle(
    "hidden",
    state.role !== "host" || !state.browserSpeechAvailable);
  const listening = Boolean(state.microphoneRecognizer);
  const mixedAudio = Boolean(state.microphoneCapture);
  const sessionCompleted = state.session?.status !== "active";
  elements.microphoneToggle.classList.toggle("listening", listening);
  elements.speechStatePill.dataset.state = state.microphoneBusy
    ? "starting"
    : listening
      ? "listening"
      : "off";
  elements.speechStateLabel.textContent = state.microphoneBusy
    ? "Starting"
    : listening
      ? "Listening"
      : sessionCompleted
        ? "Ended"
        : "Ready";
  elements.audioSourceLabel.textContent = mixedAudio
    ? "Microphone + shared audio"
    : "Presenter microphone";
  elements.microphoneToggle.disabled = state.microphoneBusy
    || sessionCompleted
    || !state.browserSpeechAvailable;
  elements.confirmMicrophone.disabled = state.microphoneBusy
    || sessionCompleted
    || !elements.microphoneConsent.checked
    || (!state.browserSpeechAuthorized
      && !elements.microphoneAccessKey.value);
  elements.microphoneConsent.disabled =
    state.microphoneBusy || listening || sessionCompleted;
  elements.includeSystemAudio.disabled =
    state.microphoneBusy
    || listening
    || sessionCompleted
    || !state.systemAudioCaptureAvailable;
  elements.microphoneAccessKey.disabled =
    state.microphoneBusy
    || listening
    || sessionCompleted
    || state.browserSpeechAuthorized;
  elements.microphoneAccessStatus.textContent = state.browserSpeechAuthorized
    ? "Device authorized. The code is not stored or copied; protected access lasts up to 30 days."
    : "Enter once per browser; the code is exchanged for protected access and is not copied to the clipboard.";
  elements.microphoneStatus.textContent = state.microphoneBusy
    ? "Starting"
    : listening
      ? mixedAudio
        ? "Mic + meeting"
        : "Listening"
      : "Off";
  elements.microphoneStatus.className =
    `status ${listening ? "listening" : "neutral"}`;
  elements.microphoneToggle.setAttribute("aria-pressed", listening ? "true" : "false");
  elements.microphoneToggle.setAttribute(
    "aria-label",
    listening
      ? mixedAudio
        ? "Stop listening to the microphone and shared meeting audio"
        : "Stop listening to the local microphone"
      : elements.microphoneConsent.checked
          && (state.browserSpeechAuthorized
            || elements.microphoneAccessKey.value)
        ? elements.includeSystemAudio.checked
          ? "Start listening to the microphone and shared meeting audio"
          : "Start listening to the local microphone"
        : "Set up meeting audio");
}

function queueMicrophoneOperation(operation) {
  const queued = state.microphoneOperation.then(operation, operation);
  state.microphoneOperation = queued.catch(() => {});
  return queued;
}

async function initializeBrowserSpeechAvailability() {
  try {
    const [health, access] = await Promise.all([
      api("/api/health"),
      api("/api/browser-speech/access")
    ]);
    state.browserSpeechAvailable =
      health.browserMicrophoneTranscription === "ready";
    state.browserSpeechAuthorized = access.authorized === true;
    if (state.browserSpeechAuthorized) {
      elements.microphoneAccessKey.value = "";
    }
  } catch {
    state.browserSpeechAvailable = false;
    state.browserSpeechAuthorized = false;
  }
  renderMicrophoneControls();
}

function normalizeMicrophoneError(error) {
  const message = error?.message || String(error);
  if (error?.name === "NotAllowedError"
      && elements.includeSystemAudio.checked) {
    return "Microphone or meeting-audio sharing was not allowed. Share the Teams tab or Entire screen with audio enabled, then try again.";
  }
  if (error?.name === "InvalidStateError"
      && elements.includeSystemAudio.checked) {
    return "Meeting audio sharing must be started directly from the Start listening button.";
  }
  if (/permission|notallowed|denied/i.test(`${error?.name || ""} ${message}`)) {
    return "Microphone permission was denied. Allow microphone access and try again.";
  }
  return message;
}

async function api(url, options = {}) {
  const { headers = {}, ...requestOptions } = options;
  const method = String(requestOptions.method ?? "GET").toUpperCase();
  const isFormData = requestOptions.body instanceof FormData;
  const response = await fetch(url, {
    ...requestOptions,
    headers: {
      ...(isFormData ? {} : { "Content-Type": "application/json" }),
      ...(method === "GET" ? {} : { "X-Session-Request": "1" }),
      ...headers
    }
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    const error = new Error(
      problem?.detail || `Request failed with status ${response.status}.`);
    error.status = response.status;
    if (response.status === 410) {
      void handleSessionExpired();
    }
    throw error;
  }

  return response.status === 204 ? null : response.json();
}

function scheduleSessionExpiry(expiresAtUtc) {
  window.clearTimeout(state.sessionExpiryTimer);
  const remaining = Date.parse(expiresAtUtc) - Date.now();
  if (!Number.isFinite(remaining) || remaining <= 0) {
    void handleSessionExpired();
    return;
  }

  state.sessionExpiryTimer = window.setTimeout(
    () => void handleSessionExpired(),
    remaining);
}

async function handleSessionExpired() {
  if (state.sessionExpiryHandling || state.session?.status === "expired") {
    return;
  }
  state.sessionExpiryHandling = true;
  clearSessionHistory();
  window.clearTimeout(state.sessionExpiryTimer);
  state.sessionExpiryTimer = null;
  state.eventSource?.close();
  cancelMicrophoneTokenRequests();
  state.speechPublishAbortController?.abort();
  if (state.microphoneRecognizer || state.microphoneBusy) {
    await queueMicrophoneOperation(stopMicrophone).catch(() => {});
  }

  if (state.session) {
    state.session = { ...state.session, status: "expired" };
    render();
  }
  setConnectionStatus("Expired", "disconnected");
  showToast(
    "This session reached its 24-hour limit and access is closed.",
    "warning");
}

function getSessionHistory() {
  const value = window.history.state?.[SESSION_HISTORY_KEY];
  if (!value
      || typeof value.sessionId !== "string"
      || !/^[0-9a-f]{8}-[0-9a-f]{4}-[1-5][0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$/i
        .test(value.sessionId)) {
    return null;
  }

  return {
    sessionId: value.sessionId,
    joinCode: typeof value.joinCode === "string"
        && /^[2-9A-HJ-NP-Z]{4}-[2-9A-HJ-NP-Z]{4}$/.test(value.joinCode)
      ? value.joinCode
      : null
  };
}

function persistSessionHistory() {
  if (!state.session?.id) {
    return;
  }

  window.history.replaceState(
    {
      ...(window.history.state ?? {}),
      [SESSION_HISTORY_KEY]: {
        sessionId: state.session.id,
        joinCode: state.role === "host" ? state.joinCode : null
      }
    },
    "");
}

function clearSessionHistory() {
  const nextState = { ...(window.history.state ?? {}) };
  delete nextState[SESSION_HISTORY_KEY];
  window.history.replaceState(nextState, "");
}

async function restoreSessionAfterRefresh() {
  const locator = getSessionHistory();
  if (!locator) {
    return;
  }

  try {
    const session = await api(`/api/sessions/${locator.sessionId}`);
    const role = Array.isArray(session.transcript)
      ? "host"
      : Array.isArray(session.alerts)
        ? "member"
        : null;
    if (!role) {
      throw new Error("The restored session view was not recognized.");
    }

    resetContextualCards();
    state.role = role;
    state.session = session;
    state.joinCode = role === "host" ? locator.joinCode : null;
    elements.setupView.classList.add("hidden");
    elements.sessionView.classList.toggle("hidden", role !== "host");
    elements.memberSessionView.classList.toggle("hidden", role !== "member");
    render();
    connectEvents(session.id);
    showToast(
      role === "host"
        ? "Host session restored after refresh."
        : "Member session restored after refresh.",
      "success");
  } catch (error) {
    clearSessionHistory();
    if (![401, 404, 410].includes(error.status)) {
      showToast(`Session restore failed: ${error.message}`, "error");
    }
  }
}

async function returnToEntry() {
  state.eventSource?.close();
  state.eventSource = null;
  window.clearTimeout(state.sessionExpiryTimer);
  state.sessionExpiryTimer = null;
  cancelMicrophoneTokenRequests();
  state.speechPublishAbortController?.abort();
  if (state.microphoneRecognizer || state.microphoneBusy) {
    await queueMicrophoneOperation(stopMicrophone).catch(() => {});
  }
  clearSessionHistory();
  state.session = null;
  state.role = null;
  state.joinCode = null;
  state.sessionExpiryHandling = false;
  resetContextualCards();
  elements.sessionView.classList.add("hidden");
  elements.memberSessionView.classList.add("hidden");
  elements.setupView.classList.remove("hidden");
  showEntryPanel(null);
  window.scrollTo(0, 0);
}

function showEntryPanel(role) {
  elements.rolePicker.classList.toggle("hidden", Boolean(role));
  elements.hostSetupPanel.classList.toggle("hidden", role !== "host");
  elements.memberJoinPanel.classList.toggle("hidden", role !== "member");
  if (role === "host") {
    document.querySelector("#host-display-name").focus();
  } else if (role === "member") {
    document.querySelector("#member-display-name").focus();
  } else {
    document.querySelector("[data-role-choice='host']").focus();
  }
}

function selectedTemplate() {
  return Array.from(elements.templateChoices)
    .find(choice => choice.checked)?.value ?? "presentation";
}

function getTemplateProfile(template) {
  return templateProfiles[template] ?? templateProfiles.custom;
}

function applySelectedTemplate() {
  const template = selectedTemplate();
  const profile = getTemplateProfile(template);
  const isCustom = template === "custom";
  elements.hostSetupPanel.dataset.template = template;
  elements.templateChoices.forEach(choice => {
    choice.closest(".template-option")?.classList.toggle(
      "selected",
      choice.checked);
  });
  elements.customMeetingTypeField.classList.toggle("hidden", !isCustom);
  elements.customMeetingType.required = isCustom;
  elements.templateHostSummary.textContent = profile.setupHost;
  elements.templateMemberSummary.textContent = profile.setupMember;
  document.querySelector("#meeting-title").placeholder = profile.titlePlaceholder;
  document.querySelector("#meeting-objective").placeholder = profile.objectivePlaceholder;
  document.querySelector("#success-criteria").placeholder = profile.criteriaPlaceholder;
}

function formatTranscriptMoment(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "Latest";
  }
  if (Date.now() - date.getTime() < 60_000) {
    return "Now";
  }
  return date.toLocaleTimeString([], {
    hour: "2-digit",
    minute: "2-digit"
  });
}

function formatExpiry(value) {
  const date = new Date(value);
  return Number.isNaN(date.getTime())
    ? "at the 24-hour limit"
    : date.toLocaleString([], {
        month: "short",
        day: "numeric",
        hour: "2-digit",
        minute: "2-digit"
      });
}

function formatRelativeMoment(value) {
  const date = new Date(value);
  if (Number.isNaN(date.getTime())) {
    return "recently";
  }
  const elapsedSeconds = Math.max(0, Math.round((Date.now() - date.getTime()) / 1000));
  if (elapsedSeconds < 60) {
    return "just now";
  }
  const elapsedMinutes = Math.round(elapsedSeconds / 60);
  return elapsedMinutes < 60
    ? `${elapsedMinutes} ${elapsedMinutes === 1 ? "minute" : "minutes"} ago`
    : date.toLocaleTimeString([], { hour: "2-digit", minute: "2-digit" });
}

function describeAudienceFamiliarity(value) {
  switch (String(value).toLowerCase()) {
    case "beginner":
      return {
        label: "Beginner",
        description:
          "Foundational terms, acronyms, and specialized concepts receive plain-language support."
      };
    case "expert":
      return {
        label: "Expert",
        description:
          "Only rare, ambiguous, or non-obvious technical details should interrupt the session."
      };
    default:
      return {
        label: "Familiar",
        description:
          "Specialized terms receive concise definitions, mechanisms, implications, and limitations."
      };
  }
}

function formatEnumLabel(value) {
  if (!value) {
    return "";
  }
  if (value === "csaVbd") {
    return "CSA / VBD";
  }
  return String(value)
    .replace(/([a-z])([A-Z])/g, "$1 $2")
    .replace(/^./, character => character.toUpperCase());
}

async function runWithButton(button, action) {
  button.disabled = true;
  try {
    await action();
  } catch (error) {
    showToast(error.message, "error");
  } finally {
    button.disabled = false;
    if (state.session) {
      render();
    }
  }
}

function showToast(message, kind = "info", options = {}) {
  if (window.sessionToast) {
    return window.sessionToast.show({
      ...options,
      kind,
      message
    });
  }

  elements.toastFallback.textContent = options.title
    ? `${options.title}: ${message}`
    : message;
  const isError = kind === "error";
  elements.toastFallback.setAttribute("role", isError ? "alert" : "status");
  elements.toastFallback.setAttribute(
    "aria-live",
    isError ? "assertive" : "polite");
  elements.toastFallback.dataset.kind = kind;
  elements.toastFallback.classList.remove("hidden");
  window.clearTimeout(state.fallbackToastTimer);
  state.fallbackToastTimer = null;
  if (options.autoClose !== false) {
    const defaultDuration = isError ? 9000 : kind === "warning" ? 9000 : 5000;
    const duration = Number.isFinite(options.autoClose)
      ? options.autoClose
      : defaultDuration;
    state.fallbackToastTimer = window.setTimeout(
      () => elements.toastFallback.classList.add("hidden"),
      duration);
  }
  return null;
}

function escapeHtml(value) {
  const element = document.createElement("span");
  element.textContent = value ?? "";
  return element.innerHTML;
}

function escapeAttribute(value) {
  return escapeHtml(value)
    .replace(/"/g, "&quot;")
    .replace(/'/g, "&#39;");
}

function resolveSourceTranscript(task, transcript) {
  const sourceIds = new Set(
    (task.sourceTranscriptSegmentIds ?? []).map(id => String(id).toLowerCase()));
  return transcript.filter(segment =>
    sourceIds.has(String(segment.id).toLowerCase()));
}

window.addEventListener("pagehide", () => {
  window.clearTimeout(state.microphoneRefreshTimer);
  window.clearTimeout(state.sessionExpiryTimer);
  window.clearTimeout(state.fallbackToastTimer);
  window.sessionToast?.clear();
  cancelMicrophoneTokenRequests();
  state.speechPublishAbortController?.abort();
  state.microphoneRecognizer?.close();
  state.microphoneAudioConfig?.close();
  void closeMixedMeetingAudioCapture(state.microphoneCapture);
});

void restoreSessionAfterRefresh();
