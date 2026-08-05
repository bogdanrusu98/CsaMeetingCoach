const state = {
  session: null,
  eventSource: null,
  teamsMeetingId: null,
  browserSpeechAvailable: false,
  browserSpeechAuthorized: false,
  microphoneRecognizer: null,
  microphoneAudioConfig: null,
  microphoneCapture: null,
  microphoneSourceLabel: "Presenter microphone",
  microphoneRefreshTimer: null,
  microphoneStartupAbortController: null,
  microphoneRefreshAbortController: null,
  microphoneBusy: false,
  microphoneOperation: Promise.resolve(),
  speechPublishQueue: Promise.resolve(),
  speechPublishAbortController: null,
  seenRecognitionIds: new Set(),
  speechDiagnostics: {
    interim: 0,
    final: 0,
    queued: 0,
    published: 0,
    publishFailures: 0,
    lastStage: "Waiting for microphone activity.",
    lastEventAt: null
  },
  contextualCardTimers: new Map(),
  dismissedContextualCardIds: new Set(),
  systemAudioCaptureAvailable: Boolean(
    navigator.mediaDevices?.getDisplayMedia
      && (window.AudioContext || window.webkitAudioContext))
};

const elements = {
  setupView: document.querySelector("#setup-view"),
  sessionView: document.querySelector("#session-view"),
  sessionForm: document.querySelector("#session-form"),
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
  diagnosticsDialog: document.querySelector("#diagnostics-dialog"),
  openDiagnostics: document.querySelector("#open-diagnostics"),
  closeDiagnostics: document.querySelector("#close-diagnostics"),
  contextualCards: document.querySelector("#contextual-cards"),
  toast: document.querySelector("#toast")
};

function applyTeamsTheme(theme) {
  const normalizedTheme = theme === "dark"
    ? "dark"
    : theme === "contrast"
      ? "contrast"
      : "light";
  document.documentElement.setAttribute("data-theme", normalizedTheme);
}

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
        showToast("Microphone transcription stopped.");
      } else {
        await startMicrophone();
        showToast("Microphone transcription started.");
      }
    });
  } catch (error) {
    showToast(normalizeMicrophoneError(error));
  }
});
elements.confirmMicrophone.addEventListener("click", async () => {
  try {
    await queueMicrophoneOperation(startMicrophone);
    elements.microphoneUnlock.classList.add("hidden");
    showToast("Microphone transcription started.");
  } catch (error) {
    showToast(normalizeMicrophoneError(error));
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
elements.contextualCards.addEventListener("click", event => {
  const button = event.target instanceof Element
    ? event.target.closest("button[data-contextual-card-dismiss]")
    : null;
  if (!button || !elements.contextualCards.contains(button)) {
    return;
  }

  dismissContextualCard(button.dataset.contextualCardDismiss);
});

elements.sessionForm.addEventListener("submit", async event => {
  event.preventDefault();
  const successCriteria = document.querySelector("#success-criteria").value
    .split("\n")
    .map(value => value.trim())
    .filter(Boolean);

  await runWithButton(event.submitter, async () => {
    await teamsContextReady;
    const teamsHosted = new URLSearchParams(window.location.search).get("host") === "teams";
    if (teamsHosted && !state.teamsMeetingId) {
      throw new Error("The Teams meeting identifier is unavailable.");
    }

    const session = await api("/api/sessions", {
      method: "POST",
      body: JSON.stringify({
        purpose: {
          title: document.querySelector("#meeting-title").value,
          meetingType: document.querySelector("#meeting-type").value,
          objective: document.querySelector("#meeting-objective").value,
          successCriteria
        },
        teamsOnlineMeetingId: state.teamsMeetingId
      })
    });

    resetContextualCards();
    state.session = session;
    elements.setupView.classList.add("hidden");
    elements.sessionView.classList.remove("hidden");
    render();
    connectEvents(session.id);
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
    showToast("Meeting session completed.");
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
  elements.connectionStatus.textContent = "Connecting";
  elements.connectionStatus.className = "status neutral";

  state.eventSource = new EventSource(`/api/sessions/${sessionId}/events`);
  state.eventSource.addEventListener("open", async () => {
    elements.connectionStatus.textContent = "Live";
    elements.connectionStatus.className = "status connected";
    try {
      const current = await api(`/api/sessions/${sessionId}`);
      if (!state.session || current.revision >= state.session.revision) {
        state.session = current;
        render();
      }
    } catch (error) {
      showToast(`The meeting state could not be refreshed: ${error.message}`);
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
          showToast(`Microphone could not be stopped: ${error.message}`);
        });
      }
    }
  });
  state.eventSource.addEventListener("error", () => {
    elements.connectionStatus.textContent = "Reconnecting";
    elements.connectionStatus.className = "status disconnected";
  });
}

function render() {
  const session = state.session;
  if (!session) {
    return;
  }

  document.querySelector("#meeting-type-label").textContent =
    session.purpose.meetingType;
  document.querySelector("#purpose-title").textContent = session.purpose.title;
  document.querySelector("#purpose-objective").textContent = session.purpose.objective;
  document.querySelector("#complete-meeting").disabled = session.status === "completed";
  document.querySelector("#transcript-form button").disabled = session.status === "completed";
  renderMicrophoneControls();
  const contextualCards = session.contextualCards ?? [];
  renderContextualCards(contextualCards);
  renderContextualCardHistory(contextualCards);

  const liveRecommendations = session.recommendedTasks.filter(
    task => task.status === "accepted" || task.status === "completed");
  const completed = session.checklist.filter(item => item.status === "completed").length
    + liveRecommendations.filter(task => task.status === "completed").length;
  const total = session.checklist.length + liveRecommendations.length;
  document.querySelector("#progress-label").textContent =
    `${completed}/${total}`;
  elements.progressFill.style.width =
    total === 0 ? "0" : `${Math.round((completed / total) * 100)}%`;

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

  elements.checklist.innerHTML = session.checklist.map(item => {
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

  const proposedRecommendations = session.recommendedTasks.filter(
    task => task.status === "proposed");
  if (proposedRecommendations.length === 0) {
    elements.recommendations.className = session.isAnalyzing
      ? "stack"
      : "stack empty-state";
    elements.recommendations.textContent = session.isAnalyzing
      ? "Analyzing meeting context\u2026"
      : "Listening for explicit discussion to suggest what to cover next.";
  } else {
    elements.recommendations.className = "stack";
    elements.recommendations.innerHTML = proposedRecommendations
      .map(task => {
        const basedOn = resolveSourceTranscript(task, session.transcript);
        return `
        <div class="recommendation">
          <h3>${escapeHtml(task.title)}</h3>
          <p>${escapeHtml(task.rationale)}</p>
          <div class="based-on">
            <strong>Based on:</strong>
            ${basedOn.map(segment => `
              <blockquote>“${escapeHtml(segment.text)}”</blockquote>
            `).join("")}
          </div>
          <div class="actions">
            <button class="button primary" type="button"
                    data-recommendation="${task.id}" data-status="accepted">Accept</button>
            <button class="button subtle" type="button"
                    data-recommendation="${task.id}" data-status="dismissed">Dismiss</button>
          </div>
        </div>`;
      }).join("");
  }

  if (session.transcript.length === 0) {
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
          <span>${escapeHtml(segment.text)}</span>
        </div>`).join("");
  }

  elements.warningsPanel.classList.toggle("hidden", session.warnings.length === 0);
  elements.warnings.innerHTML = session.warnings
    .map(warning => `<div class="warning">${escapeHtml(warning)}</div>`)
    .join("");
}

function renderContextualCards(cards) {
  const candidates = cards.filter(
    card => !state.dismissedContextualCardIds.has(card.id));
  const overflow = candidates.slice(0, Math.max(0, candidates.length - 3));
  overflow.forEach(card => state.dismissedContextualCardIds.add(card.id));
  const visibleCards = candidates.slice(-3);
  const visibleIds = new Set(visibleCards.map(card => card.id));

  elements.contextualCards
    .querySelectorAll("[data-contextual-card-id]")
    .forEach(cardElement => {
      if (!visibleIds.has(cardElement.dataset.contextualCardId)) {
        cardElement.remove();
      }
    });

  visibleCards.forEach(card => {
    if (elements.contextualCards.querySelector(
      `[data-contextual-card-id="${CSS.escape(card.id)}"]`)) {
      return;
    }

    const kind = String(card.kind).toLowerCase() === "definition"
      ? "definition"
      : "hint";
    const kindLabel = kind === "definition" ? "📖 Definition" : "💡 Hint";
    const cardElement = document.createElement("article");
    cardElement.className = `contextual-card ${kind}`;
    cardElement.dataset.contextualCardId = card.id;
    cardElement.dataset.kind = kind;
    cardElement.innerHTML = `
      <div class="contextual-card-heading">
        <span class="contextual-card-kind">${kindLabel}</span>
        <button class="contextual-card-dismiss" type="button">&times;</button>
      </div>
      <strong class="contextual-card-title">${escapeHtml(card.title)}</strong>
      <p>${escapeHtml(card.content)}</p>`;
    const dismissButton = cardElement.querySelector(".contextual-card-dismiss");
    dismissButton.dataset.contextualCardDismiss = card.id;
    dismissButton.setAttribute("aria-label", `Dismiss ${card.title}`);
    cardElement.addEventListener(
      "pointerenter",
      () => pauseContextualCardDismissal(card.id));
    cardElement.addEventListener(
      "pointerleave",
      () => scheduleContextualCardDismissal(card.id));
    cardElement.addEventListener(
      "focusin",
      () => pauseContextualCardDismissal(card.id));
    cardElement.addEventListener(
      "focusout",
      () => window.setTimeout(
        () => scheduleContextualCardDismissal(card.id),
        0));
    elements.contextualCards.append(cardElement);

    scheduleContextualCardDismissal(card.id);
  });
}

function scheduleContextualCardDismissal(cardId) {
  pauseContextualCardDismissal(cardId);
  const cardElement = elements.contextualCards.querySelector(
    `[data-contextual-card-id="${CSS.escape(cardId)}"]`);
  if (!cardElement
      || cardElement.matches(":hover")
      || cardElement.contains(document.activeElement)) {
    return;
  }

  state.contextualCardTimers.set(
    cardId,
    window.setTimeout(() => dismissContextualCard(cardId), 25_000));
}

function pauseContextualCardDismissal(cardId) {
  window.clearTimeout(state.contextualCardTimers.get(cardId));
  state.contextualCardTimers.delete(cardId);
}

function dismissContextualCard(cardId) {
  if (!cardId) {
    return;
  }

  state.dismissedContextualCardIds.add(cardId);
  window.clearTimeout(state.contextualCardTimers.get(cardId));
  state.contextualCardTimers.delete(cardId);
  const cardElement = elements.contextualCards.querySelector(
    `[data-contextual-card-id="${CSS.escape(cardId)}"]`);
  if (!cardElement) {
    return;
  }

  cardElement.classList.add("leaving");
  window.setTimeout(() => cardElement.remove(), 180);
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
        ? "📖 Definition"
        : "💡 Hint";
      return `
        <div class="contextual-card-history-entry">
          <span class="contextual-card-kind">${kindLabel}</span>
          <strong>${escapeHtml(card.title)}</strong>
          <span>${escapeHtml(card.content)}</span>
        </div>`;
    })
    .join("");
}

function resetContextualCards() {
  state.contextualCardTimers.forEach(timer => window.clearTimeout(timer));
  state.contextualCardTimers.clear();
  state.dismissedContextualCardIds.clear();
  const label = elements.contextualCards.querySelector(".contextual-card-stack-label");
  elements.contextualCards.replaceChildren();
  if (label) {
    elements.contextualCards.append(label);
  }
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
        : "Microphone recognition was canceled.");
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
    state.microphoneAudioConfig = null;
    state.microphoneCapture = null;
    state.microphoneSourceLabel = "Presenter microphone";
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
        showToast("Speech SDK did not confirm shutdown; microphone resources were closed.");
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
      showToast(`Speech authorization could not be renewed: ${error.message}`);
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
      recordSpeechDiagnostic("Coach API publish succeeded.", "published");
      elements.microphonePreview.textContent =
        "Final phrase sent. Live coaching is updating.";
    })
    .catch(error => {
      if (!abortController.signal.aborted) {
        recordSpeechDiagnostic("Coach API publish failed.", "publishFailures");
        showToast(`A recognized segment could not be processed: ${error.message}`);
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
  elements.speechDiagnostics.textContent =
    `Interim: ${diagnostics.interim} · Final: ${diagnostics.final} · `
    + `Queued: ${diagnostics.queued} · Published: ${diagnostics.published} · `
    + `Publish failures: ${diagnostics.publishFailures}. `
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
  showToast("Microphone stopped before all final speech segments could be uploaded.");
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
    showToast("Shared meeting audio stopped. Speech recognition is stopping safely.");
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
    !state.browserSpeechAvailable);
  const listening = Boolean(state.microphoneRecognizer);
  const mixedAudio = Boolean(state.microphoneCapture);
  const sessionCompleted = state.session?.status === "completed";
  elements.microphoneToggle.classList.toggle("listening", listening);
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
    ? "Device authorized. The code is not stored or copied; protected access lasts up to seven days."
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
  const response = await fetch(url, {
    ...requestOptions,
    headers: {
      "Content-Type": "application/json",
      ...headers
    }
  });

  if (!response.ok) {
    const problem = await response.json().catch(() => null);
    throw new Error(problem?.detail || `Request failed with status ${response.status}.`);
  }

  return response.json();
}

async function runWithButton(button, action) {
  button.disabled = true;
  try {
    await action();
  } catch (error) {
    showToast(error.message);
  } finally {
    button.disabled = false;
    if (state.session) {
      render();
    }
  }
}

function showToast(message) {
  elements.toast.textContent = message;
  elements.toast.classList.remove("hidden");
  window.setTimeout(() => elements.toast.classList.add("hidden"), 4500);
}

function escapeHtml(value) {
  const element = document.createElement("span");
  element.textContent = value ?? "";
  return element.innerHTML;
}

function resolveSourceTranscript(task, transcript) {
  const sourceIds = new Set(
    (task.sourceTranscriptSegmentIds ?? []).map(id => String(id).toLowerCase()));
  return transcript.filter(segment =>
    sourceIds.has(String(segment.id).toLowerCase()));
}

window.addEventListener("pagehide", () => {
  window.clearTimeout(state.microphoneRefreshTimer);
  state.contextualCardTimers.forEach(timer => window.clearTimeout(timer));
  cancelMicrophoneTokenRequests();
  state.speechPublishAbortController?.abort();
  state.microphoneRecognizer?.close();
  state.microphoneAudioConfig?.close();
  void closeMixedMeetingAudioCapture(state.microphoneCapture);
});
