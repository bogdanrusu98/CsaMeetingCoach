# Session Copilot engineering reference

Session Copilot is currently a standalone, privacy-conscious web application for
live customer conversations, with experimental Microsoft Teams integration paths.
It combines the session purpose with a real-time transcript stream to maintain an
evidence-backed checklist and recommend private talking points to the host.

## Current engineering MVP

- Creates a meeting checklist from the meeting type, objective, and success
  criteria.
- Accepts final transcript segments through a real-time ingestion API.
- Can transcribe an explicitly consented local microphone and, when the user
  selects it, audio played by the device through short-lived Azure Speech tokens;
  the subscription key never reaches the browser.
- Auto-completes a checklist item only above a confidence threshold and only
  when the agent supplies an exact quote from its cited final transcript segment
  and the deterministic evaluator independently approves that same source.
  Compound criteria require every configured discussion signal across the
  current window, and all supporting source fragments are retained.
- Stores the speaker, timestamp, quote, rationale, and confidence for every
  automatic completion.
- Allows users to undo any automatic completion.
- Recommends what the CSA should discuss, show, or ask next from explicit customer
  needs and meeting context. Accepted talking points auto-complete only from exact
  evidence ingested after acceptance and can be reopened.
- Shows only client-facing educational alerts in dismissible lower-right cards:
  transcript-grounded **Definition** cards and follow-on **Hint** cards.
- Lets the host select session-wide audience familiarity. Beginner sessions include
  foundational concepts, familiar sessions prioritize specialized terms, and expert
  sessions reserve alerts for non-obvious technical detail.
- Restores an authorized Host or Member view after refresh using a minimal browser
  history locator plus the existing role-scoped HttpOnly session cookie; transcript,
  guidance, and authorization tokens are never written to browser storage.
- Pushes session updates to the side panel with Server-Sent Events.
- Protects each session with a scoped HttpOnly access cookie so another local
  caller cannot read or alter a transcript by guessing its session ID.
- Binds Teams-hosted sessions to the Teams meeting ID and exposes a separate,
  disabled-by-default transcript-adapter endpoint.
- Deduplicates adapter retries by source segment ID and retries AI processing
  safely when the transcript was persisted before an agent failure.
- Supports a deterministic local agent and an optional Azure OpenAI agent.
- Persists meeting sessions as local JSON files under the current user's local
  application-data directory, outside the repository.

The browser UI includes both a transcript simulator and an explicitly started
audio source. By default, it captures only the presenter microphone. The user can
optionally combine that microphone with meeting audio explicitly selected through
the browser's screen-sharing picker. This is an admin-free demo convenience, not
a claim of native Teams participant capture or speaker attribution.

## Accented-speech resilience

Azure Speech sometimes returns homophones for accented speakers (for example,
"Asia" instead of "Azure" for a Romanian-accented presenter). The system
addresses this with a hybrid Custom Speech language model, phrase vocabulary,
and a conservative final-text fallback:

### Custom Speech language model
The optional Custom Speech endpoint is trained only from deterministic public
catalog text: all educational concepts, definitions, hints, the same 500 safe
phrase-list expressions, and generated meeting-context utterances. It improves
product vocabulary and language context, but text-only training does not adapt
acoustics or guarantee recognition of a particular accent. No meeting audio,
transcript, customer data, or user data is included in the training dataset.

### Phrase vocabulary
The speech-token endpoint now includes a bounded, deduplicated list of canonical
Azure service names and safe aliases sourced from the educational concept catalog.
The browser configures an Azure Speech SDK `PhraseListGrammar` with these phrases
and weight 2.0 before continuous recognition starts, improving recognition of Azure
terminology before results are final. Phrase vocabulary is privacy-safe (no spoken
content is logged).

The browser uses both adaptations together: it selects the configured Custom
Speech endpoint and still applies the exact 500-entry phrase list at weight 2.0
before constructing recognition evidence.

### Contextual speech normalizer (fallback)
For segments explicitly marked as speech-recognized (`IsSpeechRecognized: true`),
a conservative context-aware normalizer corrects whole-word `Asia` → `Azure` in
final segments only when the same segment contains a strong Azure ecosystem anchor
(e.g., AKS, Key Vault, resource group, Defender for Cloud) or the meeting purpose
is explicitly Azure-focused and a distinctive technical anchor is present.

Geographic constructions are never corrected: "Southeast Asia", "Asia Pacific",
"customers in Asia", "travel to Asia", "AWS region in Asia", etc. remain unchanged.
Manual transcript simulator and API adapter transcripts are not affected unless
they explicitly set `IsSpeechRecognized: true`.

When a correction occurs, the original recognized text is retained in
`TranscriptSegment.RecognizedText` for traceability, and a reason code is stored
in `TranscriptSegment.CorrectionReason`. The corrected text drives coaching and
appears in the transcript. The diagnostics panel shows a "Speech normalized"
indicator next to corrected segments, including the original phrase on hover.
Server logs record only the correction reason code and segment ID — never transcript content.

By default, session files are stored in
`%LOCALAPPDATA%\CsaMeetingCoach\data` on Windows.

## Important platform limitation

Microsoft Graph meeting transcripts are available after a meeting, not as a
public streaming transcript API for Teams meeting apps. The current side-panel
app therefore does not claim to capture every participant live.

A production integration must choose one approved source:

1. Microsoft-provided live Copilot/transcription capability, if a supported API
   becomes available.
2. An application-hosted Teams media bot plus Azure AI Speech, with the required
   tenant approvals and Azure Windows infrastructure. Microsoft currently treats
   real-time media bots as a specialized preview path rather than the recommended
   meeting-agent integration.
3. Another organization-approved transcript source that calls the ingestion
   endpoint.

## Browser microphone transcription

The admin-free live demo path uses the Azure Speech JavaScript SDK. The user must
confirm participant notice, select **Start listening**, and grant the
Teams/browser microphone permission. The app sends only final text segments to
the existing session API. It does not persist or upload raw audio to the Coach
API, does not start automatically, and stops when the user ends the session or
selects **Stop listening**.

Continuous recognition uses Azure Speech's `Time` segmentation strategy, with a
1.2-second silence boundary and a 20-second maximum phrase length. This ensures
that uninterrupted presentation audio still produces final recognition events.
Diagnostics exposes separate interim, final, queued, published, and failed
publish counts; interim text remains preview-only and can never become evidence.

To include what the user hears, select **Include meeting audio played by this
device** before starting. Edge or Chrome then opens its standard display-capture
picker. For Teams in a browser, select the Teams tab and enable tab audio. For the
Teams desktop client, select **Entire screen** and enable **Share system audio**.
The browser requires a display selection to authorize system-audio capture; the
coach disables the resulting video track and never processes or uploads screen
video. Web Audio mixes the selected system audio with the local microphone and
passes that in-memory stream directly to Azure Speech.

If the Teams side-panel webview does not expose display capture, open the public
coach URL as a top-level Edge or Chrome page beside the meeting. System-audio
capture has no speaker attribution and can include notifications or other sounds
played by the selected source. If sharing is canceled, no system-audio track is
returned, or the user stops sharing, the mixed recognition path stops safely.

The API exchanges the Speech subscription key for a short-lived authorization
token. Configure the key only on the server:

```powershell
$env:BrowserSpeech__Enabled = "true"
$env:BrowserSpeech__SubscriptionKey = "FROM-SECRET-STORE"
$env:BrowserSpeech__AccessKey = "RANDOM-VALUE-OF-AT-LEAST-32-CHARACTERS"
$env:BrowserSpeech__Region = "westus2"
$env:BrowserSpeech__Language = "en-US"
$env:BrowserSpeech__EndpointId = "11111111-1111-4111-8111-111111111111"
```

`BrowserSpeech__EndpointId` is optional. When present it must be a canonical
Custom Speech endpoint GUID, never a URL. Health and browser diagnostics report
only `custom` versus `base`; they do not expose the endpoint ID.

The GitHub deployment enables this path only when the repository variable
`BROWSER_SPEECH_ENABLED` is `true`. It reuses
`MEDIA_BOT_SPEECH_KEY`, `MEDIA_BOT_SPEECH_REGION`, and
`MEDIA_BOT_SPEECH_LANGUAGE`, so browser transcription can be enabled while
`MEDIA_BOT_ENABLED` remains `false`. Store a separate random value of at least
32 characters in the `BROWSER_SPEECH_ACCESS_KEY` repository secret and enter
that value in the side panel for an authorized demo. The plaintext value is
retained only in page memory and cleared after a successful exchange. The
server then grants that browser a protected, HttpOnly authorization cookie for
up to 30 days; rotating the configured access code revokes existing browser
authorization. The Teams manifest requests the `media` device permission;
tenant policy can still block custom app or microphone access.

### Custom Speech lifecycle and cost

The **Deploy Custom Speech** GitHub Actions workflow is manual-only because
training and hosting a Custom Speech endpoint can incur Azure cost. Dispatch the
default `train-and-deploy` operation from **Actions > Deploy Custom Speech > Run
workflow**. It generates the public text corpus, creates a new timestamped
language dataset and model, creates or updates the exact-name endpoint, and
retains a private seven-day result artifact.
On first activation, an operator copies the endpoint ID from that artifact into
the `BROWSER_SPEECH_ENDPOINT_ID` repository variable and runs **Deploy demo VM**.
Later retraining runs verify that the reused endpoint has the same ID and request
the normal demo deployment automatically. After that request, the workflow
removes only obsolete timestamped models and datasets that match the exact
project and locale; it protects every custom model referenced by any endpoint
and all datasets used by those protected custom models. Base models are outside
the managed cleanup scope. The `cleanup-obsolete` operation runs the same
fail-closed cleanup without training a new model. This avoids granting an
Actions token broad repository-administration access. The workflow uses
`MEDIA_BOT_SPEECH_KEY`, `MEDIA_BOT_SPEECH_REGION`, and
`MEDIA_BOT_SPEECH_LANGUAGE`; it requires no Azure CLI, Blob Storage, or public
dataset URL.

The endpoint is created and verified with content/audio logging disabled. The
application uploads live audio directly to Speech recognition but does not
retain it, and the automation never uploads meeting audio. Custom models expire
according to the Azure Speech lifecycle, so rerun the manual workflow before
expiration or whenever the public catalog changes. The automated cleanup is
locale-scoped and never deletes endpoints. Remove unused hosted endpoints or
resources from other locales separately in Azure to control cost.

The Windows media-bot service is in `src/CsaMeetingCoach.BotService`. Graph is
disabled by default, so `/api/sessions/{id}/transcript` and the UI simulator
remain the transcript source until the bot is explicitly configured and
deployed.

## Experimental Windows media bot

The deployment workflow installs this component as a separate Windows service
behind `/bot/*`, but keeps Graph communications disabled by default. Enabling it
still requires the platform approvals and Azure configuration below.

The bot targets `net8.0-windows`, x64, and `win-x64`. It uses Graph
Communications SDK `1.2.0.17950` and app-hosted Teams media. The receive-only
audio socket requests PCM S16LE, 16 kHz, mono. Its receive handler is subscribed
before the Graph join. Each unmanaged 20 ms frame is copied and disposed
immediately; pre-gate frames are observably discarded and eligible frames are
offered to a bounded queue. Azure Speech uses
continuous recognition and publishes final results only, with the neutral
speaker label `Meeting participant`. Audio is never persisted.

For each explicit join, the service creates the media session before calling
Graph. It waits for the Graph call resource to reach `Established`, then calls
`updateRecordingStatus(recording)`. Audio remains gated until that call
succeeds. The service then sends final segments in order through a bounded
publish queue to the existing adapter API with a stable `SourceSegmentId`;
transient failures are retried with bounded exponential backoff. Established
calls receive a supervised keepalive approximately every 15 minutes. Calls,
workers, and Speech/media resources are cleaned up when Graph removes the call.

The Communications SDK currently documents `UpdateRecordingStatus` as
applicable only to compliance-recording bots. This required gate is retained,
but live use therefore requires the appropriate compliance-recording policy,
tenant approval, and administrator configuration. A Graph live end-to-end test
remains externally blocked without those approvals plus tenant/admin access,
Azure resources, and a supported Windows Server media-bot host.

Configure secrets only through environment variables or an approved secret
store. Required production values when `Graph__Enabled=true` are:

```powershell
$env:Graph__TenantId = "TENANT-ID"
$env:Graph__AppId = "BOT-APP-ID"
$env:Graph__ClientSecret = "FROM-SECRET-STORE"
$env:Graph__NotificationUrl = "https://bot.example.com/bot/calling"
$env:Media__ServiceFqdn = "bot.example.com"
$env:Media__PublicIpAddress = "203.0.113.10"
$env:Media__CertificateThumbprint = "CERTIFICATE-THUMBPRINT"
$env:Speech__SubscriptionKey = "FROM-SECRET-STORE"
$env:Speech__Region = "westeurope"
$env:CoachApi__BaseUrl = "https://coach.example.com/"
```

Coach API authentication supports:

- `DevelopmentApiKey`: set `CoachApi__DevelopmentApiKey` to a random value of
  at least 32 characters. It is sent as `X-Transcript-Adapter-Key`.
- `Entra`: set `CoachApi__EntraScope` (normally
  `api://COACH-API-APP-ID/.default`). `DefaultAzureCredential` obtains the
  token, so use managed identity in Azure.

`POST /bot/join` requires tenant, chat, organizer, coach session, and Teams
online-meeting IDs; message and media-session IDs are optional. Protect it with
either a development key (`X-Media-Bot-Key`, at least 32 characters) or Entra
and the configured `MediaBot.Controller` app role. `POST /bot/calling` forwards
Graph notifications to the communications SDK. `Media__MaxConcurrentCalls`
limits native media allocations; excess joins receive HTTP 429.
`GET /bot/health` returns configured/enabled/ready/degraded state and aggregate
call, drop, publish, pipeline, and keepalive counters without exposing
configuration values or secrets. Expected pre-gate discards are reported but do
not by themselves degrade readiness; queue drops and worker, publish, Speech, or
keepalive failures do. An enabled service that is not ready or is degraded
returns HTTP 503.

The media SDK requires a supported Windows x64 Azure VM, a public IP/FQDN,
certificate, and the documented media ports. Tenant calling permissions,
application access policy, recording/transcription notice, and organizational
approval are prerequisites. To run the disabled-by-default service locally:

```powershell
dotnet run --project .\src\CsaMeetingCoach.BotService
```

The GitHub deployment remains fail-closed unless `MEDIA_BOT_ENABLED=true`.
Non-secret settings use repository variables prefixed with `MEDIA_BOT_`;
credentials use the `MEDIA_BOT_GRAPH_CLIENT_SECRET`,
`MEDIA_BOT_SPEECH_KEY`, `MEDIA_BOT_CONTROL_API_KEY`, and
`TRANSCRIPT_ADAPTER_API_KEY` repository secrets. The transcript adapter and
media-bot coach client must use matching authentication settings. When enabled,
the deployment validates configuration, installs the service, opens only the
configured media port, and rolls back if `/bot/health` does not report `ready`.

## Transcript adapter authentication

`POST /api/adapter/sessions/{sessionId}/transcript` is separate from the browser
session endpoint. It requires:

- a session bound to the same `TeamsOnlineMeetingId`;
- a non-empty client-generated `SourceSegmentId`;
- adapter authentication.

Adapter ingestion is disabled by default. For isolated local development only:

```powershell
$env:TranscriptAdapter__AuthenticationMode = "DevelopmentApiKey"
$env:TranscriptAdapter__DevelopmentApiKey = "SET-A-RANDOM-VALUE-OF-AT-LEAST-32-CHARACTERS"
```

Send that value through `X-Transcript-Adapter-Key`. Production must use:

```powershell
$env:TranscriptAdapter__AuthenticationMode = "Entra"
$env:TranscriptAdapter__Entra__TenantId = "YOUR-TENANT-ID"
$env:TranscriptAdapter__Entra__Audience = "api://YOUR-COACH-API-APP-ID"
$env:TranscriptAdapter__Entra__RequiredRole = "TranscriptIngestor"
```

The calling application needs the `TranscriptIngestor` app role in its access
token. No client secret belongs in this repository.

## Frontend

The compact Teams side-panel client is served directly from
`src/CsaMeetingCoach.Api/wwwroot`. It uses dependency-free HTML, CSS, and
JavaScript so it can build and deploy in corporate environments where npm is
blocked. Its visual system follows Microsoft Fluent and Teams interaction
patterns without downloading runtime UI dependencies.

The live view intentionally keeps only the meeting bar, microphone, next
coaching action, and compact progress visible. Consent and the access-code field
are shown during microphone activation. After successful authorization the
plaintext code is cleared and is never copied to the clipboard or browser
storage; an HttpOnly protected cookie authorizes Speech access for up to 30
days. Transcript simulation, evidence, and safety warnings are kept in a single
diagnostics dialog.
Up to three contextual cards can be visible at once above the normal status
toast. Each card can be dismissed and closes automatically after 25 seconds;
dismissed cards do not replay on later SSE updates. Hovering or moving keyboard
focus into a card pauses its timer, and the latest retained cards remain
available in Diagnostics after the popup closes.

## Educational Alert System

Client alerts are limited to two card types:

- **Definition** — explains an explicitly transcript-grounded technology term.
- **Hint** — adds a concrete mechanism, example, prerequisite, distinction,
  consequence, or limitation for that grounded term.

Client alerts never contain CSA recommendations, sales guidance, presenter
coaching, questions for the client, next-best actions, or recommended tasks.
Recommended tasks remain in the separate CSA panel.

The educational catalog now contains 183 concepts across these categories:

- FoundationalCloud
- ManagementGovernance
- IdentitySecurity
- Networking
- ComputeContainersAppPlatforms
- StorageDatabases
- DataAiIntegration
- MonitoringReliability
- MigrationDevOpsFinOps

### Alert rejection reasons

Diagnostics use structured rejection reasons:

- `UnknownKind`
- `SalesOrRecommendationContent`
- `MissingEvidence`
- `ContentFingerprint`
- `CooldownActive`
- `VendorMismatch`
- `NegatedMention`
- `MentionNotFound`
- `InsufficientRanking`

### Definition → Hint lifecycle

- A definition is shown at most once per concept per session.
- A hint can appear only after a definition cooldown (3 minutes).
- Hint repeats are blocked for 15 minutes.
- Identical alert wording is deduplicated with a normalized SHA-256 content
  fingerprint.
- Pacing allows at most one new educational alert per final transcript segment.

### Privacy and diagnostics

- Only final transcript evidence can ground an alert.
- Consent behavior and no-raw-audio persistence are unchanged.
- Diagnostics log structured alert decisions without logging transcript text,
  evidence quotes, or speaker names.

## Architecture

```text
Teams meeting side panel
        |
        +-- meeting purpose and controls
        +-- SSE session updates
        |
ASP.NET Core API
        |
        +-- MeetingSessionCoordinator
        +-- evidence validation and confidence threshold
        +-- Local, Azure AI Foundry, or Azure OpenAI coach agent
        +-- JSON session store
        |
Approved live transcript adapter
        +-- POST final transcript segments to the ingestion API
```

## Run locally

Use .NET 8:

```powershell
dotnet restore
dotnet run --project .\src\CsaMeetingCoach.Api --urls http://127.0.0.1:5055
```

Open `http://127.0.0.1:5055`.

## Live presentation demo

The public hackathon demo is hosted at:

`https://csa-meeting-coach-demo.westus2.cloudapp.azure.com`

The Azure VM uses a repository-scoped GitHub Actions runner installed as a
Windows service. Pushes to `main` run the complete build and test suite, publish
a self-contained Windows x64 API, and deploy it with a one-version local rollback.
Caddy terminates public HTTPS and forwards only to Kestrel on
`127.0.0.1:5055`.

The runner bootstrap script is `deploy/Bootstrap-GitHubRunner.ps1`. Its
registration token must be generated immediately before use, expires after one
hour, and must never be committed. The runner intentionally uses LocalSystem so
the private repository workflow can manage the API and proxy services. Do not
reuse this demo VM or runner trust model for unrelated repositories.

`deploy/Deploy-Demo.ps1` pins the Caddy download and verifies its SHA-256 before
installation. It validates the proxy configuration, opens only local TCP 443 in
Windows Firewall, verifies the real certificate and health endpoint, and restores
the prior application and proxy configuration on failure. Runtime meeting state
and Data Protection keys are stored under `C:\ProgramData\CsaMeetingCoach`,
outside the deployment directory.

## Agent providers

The default `Local` provider is deterministic and supports development without
sending meeting data to an external model.

The preferred cloud provider is an Azure AI Foundry declarative agent. It uses
`DefaultAzureCredential`, so no model key is stored in configuration or source
control:

```powershell
$env:CoachAgent__Provider = "Foundry"
$env:CoachAgent__Foundry__ProjectEndpoint = "https://YOUR-RESOURCE.services.ai.azure.com/api/projects/YOUR-PROJECT"
$env:CoachAgent__Foundry__ModelDeployment = "gpt-4.1-mini"
$env:CoachAgent__Foundry__AgentName = "csa-meeting-coach-v5"
$env:CoachAgent__Foundry__VectorStoreIds = "vs_REVIEWED_STORE_ID"
```

Before its first analysis, the application updates the named agent
idempotently, creating its first version when it does not exist. Agent
instructions reject transcript prompt injection, sensitive-attribute inference,
unsupported checklist completion, and tasks without transcript sources. The
strict JSON schema is stored on the agent definition; invocation requests do not
override it. Responses are validated again before the session coordinator can
apply them.

Foundry analysis is asynchronous, coalesced after eight seconds of quiet, and
forced after at most 20 seconds of continuous final transcript updates. Calls
remain sequential and use the latest coalesced snapshot, with a 60-second timeout
so File Search and one structured retry have enough time. A completed grounded
snapshot can merge while newer speech remains queued, preventing continuous
audio from starving recommendations and contextual cards.
The latest 20 final Speech fragments form one analysis window, allowing adjacent
sentence and product-name fragments to be interpreted as a coherent discussion
without synthesizing transcript evidence. A simple completion still cites one
real source segment and an exact quote from it. A compound checklist criterion
must cover every configured discussion signal and retains each distinct exact
source fragment that jointly proves completion.
Server-controlled final-transcript order, rather than a client-supplied speech
timestamp, determines whether evidence arrived after an item was accepted or
reopened. This prevents old window evidence from immediately restoring a
completion that the user intentionally undid. Persisted sessions from earlier
schema versions receive the current transcript length as a conservative cutoff.
Completion claims without that exact cited-segment quote, unknown recommendation
IDs, and tasks with invented transcript IDs are filtered before merge and logged
server-side instead of appearing as routine user-facing rejection errors. A
recommendation may cite a real earlier transcript segment when that is its actual
source. Current operational warnings are deduplicated, superseded failures are
suppressed, and a later clean analysis removes resolved warning state.

Each analysis may also return at most two contextual cards. A data-driven,
reviewed fast-lane catalog covers Azure Load Balancer, health probes, cloud
computing, shared responsibility, IaaS, PaaS, SaaS, Azure regions, Availability
Zones, Azure Resource Manager, management groups, subscriptions, resource
groups, Microsoft Entra ID, and Azure RBAC. These cards can appear before
Foundry completes, while Foundry supplies broader context-sensitive definitions
and hints. Every card is declarative, client-ready
language that the CSA can state proactively; discovery questions and instructions
to ask, clarify, confirm, explain, tell, validate, verify, or perform another
presenter-directed action are rejected. The card
title must be an exact term or short topic phrase from a cited segment in the
analysis window, and generated definitions use the reviewed File Search
knowledge. The coordinator filters low-confidence, oversized, duplicate,
invented-source, and outside-window cards without turning those model artifacts
into user-facing warnings. It retains only the latest 12 cards per session.
Cards never assert unverified pricing, licensing, compliance, legal conclusions,
availability, customer intent, or product selection.

Recommendations that substantially repeat a checklist criterion are discarded.
Presentation sessions also persist canonical intent keys for opening outcomes,
closing recap/actions, and lifecycle examples. Semantic paraphrases collapse
into one active recommendation, while checklist-covered, dismissed, accepted,
or completed intents cannot be proposed again. Opening completion requires an
explicit objective or audience outcome, and closing completion requires explicit
recap or conclusion framing rather than a generic mention of an action or
decision.
Reviewed technical fallbacks cover Load Balancer design, cloud service-model
responsibilities, Azure management scopes, regions and zones, and identity
versus resource authorization when Foundry returns only a generic or
checklist-like suggestion.

On Azure, grant the VM system-assigned managed identity an approved Foundry role
on the Foundry resource or project. Automatic agent creation requires a role that
can manage agents, such as `Azure AI User`/`Foundry User` as exposed by the tenant.
If an administrator pre-creates the agent, use the tenant-approved invocation-only
role instead. The deployment remains on `Local` until the GitHub repository
variable `COACH_AGENT_PROVIDER` is explicitly set to `Foundry`.

Foundry requires one or more comma-separated, reviewed `vs_...` IDs and refuses
to start without them. Retrieved material can explain terminology and ground a
recommendation, but it is never accepted as evidence that meeting participants
discussed a topic. Exact evidence from the cited final transcript segment and
deterministic approval of that same source remain mandatory.

Review the non-sensitive sources in `knowledge` before indexing. The indexer
accepts only its documented file types and enforces file-count and size limits:

```powershell
dotnet run --project .\tools\CsaMeetingCoach.KnowledgeIndexer -- `
  --project-endpoint "https://YOUR-RESOURCE.services.ai.azure.com/api/projects/YOUR-PROJECT" `
  --source-directory .\knowledge `
  --store-name "CSA Meeting Coach Knowledge"
```

On success, stdout contains only the new `vs_...` ID; diagnostics use stderr.
Set that ID through
`CoachAgent__Foundry__VectorStoreIds` or the repository variable
`FOUNDRY_VECTOR_STORE_IDS`. The `index-knowledge` GitHub workflow prints the
validated ID as `VECTOR_STORE_ID=vs_...` after every reviewed file finishes
ingestion. Indexing remains separate from activation and deployment so stores
are not recreated on each release; activate the ID and then run the deployment.

The legacy Azure OpenAI adapter remains available for compatibility:

```powershell
$env:CoachAgent__Provider = "AzureOpenAI"
$env:CoachAgent__AzureOpenAI__Endpoint = "https://YOUR-RESOURCE.openai.azure.com/"
$env:CoachAgent__AzureOpenAI__Deployment = "YOUR-DEPLOYMENT"
$env:CoachAgent__AzureOpenAI__ApiVersion = "YOUR-APPROVED-API-VERSION"
$env:CoachAgent__AzureOpenAI__ApiKey = "SET-OUTSIDE-SOURCE-CONTROL"
```

The application fails at startup when the selected provider is missing required
settings. Provider errors are surfaced and never trigger a silent fallback.

## Teams app package

`appPackage/manifest.json` defines the meeting side panel. For the admin-free
microphone path, generate a side-panel-only package:

```powershell
.\deploy\Build-TeamsPackage.ps1 -PackageVersion "0.3.0"
```

After the Azure Bot and Entra application exist, add `-BotAppId` to generate one
package containing both the side panel and the calling bot:

```powershell
.\deploy\Build-TeamsPackage.ps1 `
  -BotAppId "BOT-ENTRA-APPLICATION-ID" `
  -PackageVersion "0.3.0"
```

The script creates `CsaMeetingCoach-Teams.zip`. With `-BotAppId`, it also injects
the real bot application ID and enables calling for the `groupChat` scope.
Upload the selected package through the organization's approved Teams app
process. The bot ID cannot be hard-coded before Azure assigns the Entra
application ID.

The local HTTP URL cannot be installed directly into Teams. Use an approved HTTPS
development tunnel or Azure deployment.

The scoped session cookie is defense-in-depth for the local MVP, not a
replacement for identity. Before any shared deployment, require Teams SSO/Entra
authentication, authorize users against tenant and meeting membership, and
authenticate the transcript adapter separately with managed identity.

## Safety and compliance boundaries

- No emotion or sentiment recognition.
- No employee scoring, ranking, or manager surveillance.
- No hidden recording or transcription.
- No raw audio persistence in this MVP.
- Automatic completion is reversible and always includes transcript evidence.
- Live talking points require user acceptance before they become trackable; only
  exact evidence ingested in a later final transcript segment can complete them.
- Do not use real customer data until Privacy, Legal, Security, and tenant
  administrators approve the pilot, lawful basis, notice, retention, access, and
  DPIA.

This repository is an engineering MVP, not a legal-compliance certification.

## Tests

```powershell
dotnet test -c Release
```
