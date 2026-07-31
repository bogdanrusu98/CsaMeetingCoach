const state = {
  session: null,
  eventSource: null,
  teamsMeetingId: null,
  browserSpeechAvailable: false,
  microphoneRecognizer: null,
  microphoneAudioConfig: null,
  microphoneRefreshTimer: null,
  microphoneStartupAbortController: null,
  microphoneRefreshAbortController: null,
  microphoneBusy: false,
  microphoneOperation: Promise.resolve(),
  speechPublishQueue: Promise.resolve(),
  speechPublishAbortController: null,
  seenRecognitionIds: new Set()
};

const elements = {
  setupView: document.querySelector("#setup-view"),
  sessionView: document.querySelector("#session-view"),
  sessionForm: document.querySelector("#session-form"),
  transcriptForm: document.querySelector("#transcript-form"),
  microphonePanel: document.querySelector("#microphone-panel"),
  microphoneConsent: document.querySelector("#microphone-consent"),
  microphoneAccessKey: document.querySelector("#microphone-access-key"),
  microphoneToggle: document.querySelector("#microphone-toggle"),
  microphoneStatus: document.querySelector("#microphone-status"),
  microphonePreview: document.querySelector("#microphone-preview"),
  checklist: document.querySelector("#checklist"),
  recommendations: document.querySelector("#recommendations"),
  transcript: document.querySelector("#transcript"),
  warningsPanel: document.querySelector("#warnings-panel"),
  warnings: document.querySelector("#warnings"),
  connectionStatus: document.querySelector("#connection-status"),
  toast: document.querySelector("#toast")
};

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
}

const teamsContextReady = initializeTeamsContext();
initializeBrowserSpeechAvailability();

elements.microphoneConsent.addEventListener("change", renderMicrophoneControls);
elements.microphoneAccessKey.addEventListener("input", renderMicrophoneControls);
elements.microphoneToggle.addEventListener("click", async () => {
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
  const button = event.target.closest("[data-reopen]");
  if (!button) {
    return;
  }

  await runWithButton(button, async () => {
    state.session = await api(
      `/api/sessions/${state.session.id}/checklist/${button.dataset.reopen}/reopen`,
      { method: "POST" });
    render();
  });
});

elements.recommendations.addEventListener("click", async event => {
  const button = event.target.closest("[data-recommendation]");
  if (!button) {
    return;
  }

  await runWithButton(button, async () => {
    state.session = await api(
      `/api/sessions/${state.session.id}/recommendations/${button.dataset.recommendation}/status`,
      {
        method: "POST",
        body: JSON.stringify({ status: button.dataset.status })
      });
    render();
  });
});

function connectEvents(sessionId) {
  state.eventSource?.close();
  elements.connectionStatus.textContent = "Connecting";
  elements.connectionStatus.className = "status neutral";

  state.eventSource = new EventSource(`/api/sessions/${sessionId}/events`);
  state.eventSource.addEventListener("open", () => {
    elements.connectionStatus.textContent = "Live";
    elements.connectionStatus.className = "status connected";
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
    `${session.purpose.meetingType} · revision ${session.revision}`;
  document.querySelector("#purpose-title").textContent = session.purpose.title;
  document.querySelector("#purpose-objective").textContent = session.purpose.objective;
  document.querySelector("#complete-meeting").disabled = session.status === "completed";
  document.querySelector("#transcript-form button").disabled = session.status === "completed";
  renderMicrophoneControls();

  const completed = session.checklist.filter(item => item.status === "completed").length;
  document.querySelector("#progress-label").textContent =
    `${completed}/${session.checklist.length} complete`;

  elements.checklist.innerHTML = session.checklist.map(item => {
    const isComplete = item.status === "completed";
    const evidence = item.evidence.at(-1);
    return `
      <div class="checklist-item ${isComplete ? "completed" : ""}">
        <div class="item-row">
          <span class="item-title">${escapeHtml(item.title)}</span>
          <span class="badge ${isComplete ? "success" : "pending"}">
            ${isComplete ? "Auto-checked" : "Pending"}
          </span>
        </div>
        <p class="muted">${escapeHtml(item.completionCriteria)}</p>
        ${evidence ? `
          <blockquote class="evidence">
            “${escapeHtml(evidence.quote)}”
            <br><strong>${escapeHtml(evidence.speaker)}</strong>
            · ${Math.round(evidence.confidence * 100)}% confidence
          </blockquote>
          <div class="actions">
            <button class="secondary" data-reopen="${item.id}">Undo completion</button>
          </div>
        ` : ""}
      </div>`;
  }).join("");

  if (session.recommendedTasks.length === 0) {
    elements.recommendations.className = "stack empty-state";
    elements.recommendations.textContent = "No recommendations yet.";
  } else {
    elements.recommendations.className = "stack";
    elements.recommendations.innerHTML = session.recommendedTasks
      .map(task => `
        <div class="recommendation">
          <div class="item-row">
            <span class="item-title">${escapeHtml(task.title)}</span>
            <span class="badge ${task.status === "accepted" ? "success" : "pending"}">
              ${escapeHtml(task.status)}
            </span>
          </div>
          <p class="muted">${escapeHtml(task.rationale)}</p>
          ${task.status === "proposed" ? `
            <div class="actions">
              <button class="primary" data-recommendation="${task.id}" data-status="accepted">Accept</button>
              <button class="secondary" data-recommendation="${task.id}" data-status="dismissed">Dismiss</button>
            </div>
          ` : ""}
        </div>`).join("");
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
  if (!elements.microphoneAccessKey.value) {
    throw new Error("Enter the demo access code before starting.");
  }
  if (!window.SpeechSDK) {
    throw new Error("Azure Speech SDK could not be loaded.");
  }

  state.microphoneBusy = true;
  renderMicrophoneControls();
  const startupAbortController = new AbortController();
  const speechPublishAbortController = new AbortController();
  state.microphoneStartupAbortController = startupAbortController;
  state.speechPublishAbortController = speechPublishAbortController;
  let recognizer = null;
  let audioConfig = null;
  let recognitionStarted = false;
  try {
    const token = await requestSpeechToken(
      sessionId,
      startupAbortController.signal);
    ensureMicrophoneSessionActive(sessionId);
    const speechConfig = window.SpeechSDK.SpeechConfig.fromAuthorizationToken(
      token.token,
      token.region);
    speechConfig.speechRecognitionLanguage = token.language;
    audioConfig = window.SpeechSDK.AudioConfig.fromDefaultMicrophoneInput();
    recognizer = new window.SpeechSDK.SpeechRecognizer(speechConfig, audioConfig);

    recognizer.recognizing = (_, event) => {
      const text = event.result?.text?.trim();
      if (text) {
        elements.microphonePreview.textContent = text;
      }
    };
    recognizer.recognized = (_, event) => {
      if (event.result?.reason !== window.SpeechSDK.ResultReason.RecognizedSpeech) {
        return;
      }

      const text = event.result.text?.trim();
      const recognitionId = event.result.resultId;
      if (!text || (recognitionId && state.seenRecognitionIds.has(recognitionId))) {
        return;
      }
      if (recognitionId) {
        state.seenRecognitionIds.add(recognitionId);
      }

      elements.microphonePreview.textContent = text;
      enqueueSpeechSegment(text);
    };
    recognizer.canceled = (_, event) => {
      if (state.microphoneRecognizer !== recognizer) {
        return;
      }

      const detail = event.errorDetails?.trim();
      showToast(detail
        ? `Microphone recognition stopped: ${detail}`
        : "Microphone recognition was canceled.");
      void queueMicrophoneOperation(stopMicrophone);
    };
    recognizer.sessionStopped = () => {
      if (state.microphoneRecognizer === recognizer && !state.microphoneBusy) {
        void queueMicrophoneOperation(stopMicrophone);
      }
    };

    state.microphoneRecognizer = recognizer;
    state.microphoneAudioConfig = audioConfig;
    state.seenRecognitionIds.clear();
    await startContinuousRecognition(recognizer);
    recognitionStarted = true;
    ensureMicrophoneSessionActive(sessionId);
    scheduleSpeechTokenRefresh(token, recognizer);
  } catch (error) {
    state.microphoneRecognizer = null;
    state.microphoneAudioConfig = null;
    speechPublishAbortController.abort();
    if (state.speechPublishAbortController === speechPublishAbortController) {
      state.speechPublishAbortController = null;
    }
    if (recognitionStarted) {
      await stopContinuousRecognition(recognizer);
    }
    recognizer?.close();
    audioConfig?.close();
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
  const speechPublishAbortController = state.speechPublishAbortController;
  state.microphoneBusy = true;
  cancelMicrophoneTokenRequests();
  state.microphoneRecognizer = null;
  state.microphoneAudioConfig = null;
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
    await drainSpeechPublishQueue(speechPublishAbortController);
  } finally {
    speechPublishAbortController?.abort();
    if (state.speechPublishAbortController === speechPublishAbortController) {
      state.speechPublishAbortController = null;
    }
    state.microphoneBusy = false;
    elements.microphonePreview.textContent =
      "Recognized speech will appear here before final segments are sent to the coach.";
    renderMicrophoneControls();
  }
}

function requestSpeechToken(sessionId = state.session?.id, signal) {
  return api(`/api/sessions/${sessionId}/speech-token`, {
    method: "POST",
    signal,
    headers: {
      "X-Browser-Speech-Key": elements.microphoneAccessKey.value
    }
  });
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

function enqueueSpeechSegment(text) {
  const abortController = state.speechPublishAbortController;
  if (!abortController || abortController.signal.aborted) {
    return;
  }
  const segment = {
    speaker: "Presenter microphone",
    text,
    occurredAtUtc: new Date().toISOString(),
    isFinal: true,
    sourceSegmentId: window.crypto.randomUUID()
  };

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
    })
    .catch(error => {
      if (!abortController.signal.aborted) {
        showToast(`A recognized segment could not be processed: ${error.message}`);
      }
    });
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
  const sessionCompleted = state.session?.status === "completed";
  elements.microphoneToggle.textContent = listening ? "Stop listening" : "Start listening";
  elements.microphoneToggle.disabled = state.microphoneBusy
    || sessionCompleted
    || (!listening
      && (!elements.microphoneConsent.checked
        || !elements.microphoneAccessKey.value));
  elements.microphoneConsent.disabled =
    state.microphoneBusy || listening || sessionCompleted;
  elements.microphoneAccessKey.disabled =
    state.microphoneBusy || listening || sessionCompleted;
  elements.microphoneStatus.textContent = listening ? "Listening" : "Microphone off";
  elements.microphoneStatus.className =
    `status ${listening ? "listening" : "neutral"}`;
  elements.microphoneToggle.setAttribute("aria-pressed", listening ? "true" : "false");
}

function queueMicrophoneOperation(operation) {
  const queued = state.microphoneOperation.then(operation, operation);
  state.microphoneOperation = queued.catch(() => {});
  return queued;
}

async function initializeBrowserSpeechAvailability() {
  try {
    const health = await api("/api/health");
    state.browserSpeechAvailable =
      health.browserMicrophoneTranscription === "ready";
  } catch {
    state.browserSpeechAvailable = false;
  }
  renderMicrophoneControls();
}

function normalizeMicrophoneError(error) {
  const message = error?.message || String(error);
  if (/permission|notallowed|denied/i.test(message)) {
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

window.addEventListener("pagehide", () => {
  window.clearTimeout(state.microphoneRefreshTimer);
  cancelMicrophoneTokenRequests();
  state.speechPublishAbortController?.abort();
  state.microphoneRecognizer?.close();
  state.microphoneAudioConfig?.close();
});
