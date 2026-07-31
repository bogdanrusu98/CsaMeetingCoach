const state = {
  session: null,
  eventSource: null,
  teamsMeetingId: null
};

const elements = {
  setupView: document.querySelector("#setup-view"),
  sessionView: document.querySelector("#session-view"),
  sessionForm: document.querySelector("#session-form"),
  transcriptForm: document.querySelector("#transcript-form"),
  checklist: document.querySelector("#checklist"),
  recommendations: document.querySelector("#recommendations"),
  transcript: document.querySelector("#transcript"),
  warningsPanel: document.querySelector("#warnings-panel"),
  warnings: document.querySelector("#warnings"),
  connectionStatus: document.querySelector("#connection-status"),
  toast: document.querySelector("#toast")
};

const teamsContextReady = initializeTeamsContext();

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

async function api(url, options = {}) {
  const response = await fetch(url, {
    headers: {
      "Content-Type": "application/json",
      ...(options.headers || {})
    },
    ...options
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
