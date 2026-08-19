import React from "react";
import { createRoot } from "react-dom/client";
import { Slide, ToastContainer, toast } from "react-toastify";
import "react-toastify/dist/ReactToastify.css";

const supportedKinds = new Set([
  "definition",
  "error",
  "hint",
  "info",
  "recommendation",
  "success",
  "warning"
]);

const defaultDurations = {
  definition: 14000,
  error: 9000,
  hint: 14000,
  info: 6000,
  recommendation: 16000,
  success: 5000,
  warning: 9000
};

const iconLabels = {
  definition: "D",
  error: "!",
  hint: "H",
  info: "i",
  recommendation: "R",
  success: "✓",
  warning: "!"
};

function normalizeKind(kind) {
  const normalized = String(kind || "info").toLowerCase();
  return supportedKinds.has(normalized) ? normalized : "info";
}

function ToastContent({ kind, message, title }) {
  return (
    <div className="session-toast__layout">
      <span className="session-toast__icon" aria-hidden="true">
        {iconLabels[kind]}
      </span>
      <span className="session-toast__copy">
        {title ? <strong>{title}</strong> : null}
        <span>{message}</span>
      </span>
    </div>
  );
}

function showToast({
  autoClose,
  id,
  kind,
  message,
  source = "system",
  sourceId,
  title
}) {
  const normalizedKind = normalizeKind(kind);
  const toastId = id || window.crypto.randomUUID();
  const closeDelay = autoClose === false
    ? false
    : Number.isFinite(autoClose)
      ? autoClose
      : defaultDurations[normalizedKind];
  const content = (
    <ToastContent
      kind={normalizedKind}
      message={String(message || "")}
      title={title ? String(title) : ""}
    />
  );
  const options = {
    autoClose: closeDelay,
    className: `session-toast session-toast--${normalizedKind}`,
    closeButton: true,
    closeOnClick: false,
    draggable: true,
    hideProgressBar: false,
    onClose: () => {
      window.dispatchEvent(new CustomEvent("session-toast-closed", {
        detail: { source, sourceId, toastId }
      }));
    },
    pauseOnFocusLoss: true,
    pauseOnHover: true,
    role: normalizedKind === "error" ? "alert" : "status",
    toastId
  };

  if (toast.isActive(toastId)) {
    toast.update(toastId, { ...options, render: content });
  } else {
    toast(content, options);
  }
  return toastId;
}

const rootElement = document.querySelector("#toast-root");
if (!rootElement) {
  throw new Error("React Toastify root is missing.");
}

createRoot(rootElement).render(
  <ToastContainer
    closeOnClick={false}
    draggable
    limit={4}
    newestOnTop
    pauseOnFocusLoss
    pauseOnHover
    position="top-right"
    theme="dark"
    transition={Slide}
  />
);

window.sessionToast = {
  clear() {
    toast.dismiss();
    toast.clearWaitingQueue();
  },
  dismiss(id) {
    toast.dismiss(id);
  },
  isActive(id) {
    return toast.isActive(id);
  },
  show: showToast
};
window.dispatchEvent(new Event("session-toast-ready"));
