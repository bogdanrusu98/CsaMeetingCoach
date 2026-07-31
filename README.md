# CSA Meeting Coach

CSA Meeting Coach is a privacy-conscious Microsoft Teams meeting assistant for
CSA/VBD scenarios. It combines the meeting purpose with a real-time transcript
stream to maintain an evidence-backed checklist and recommend follow-up tasks.

## Current MVP

- Creates a meeting checklist from the meeting type, objective, and success
  criteria.
- Accepts final transcript segments through a real-time ingestion API.
- Can transcribe an explicitly consented local microphone in real time through
  short-lived Azure Speech tokens; the subscription key never reaches the browser.
- Auto-completes a checklist item only above a confidence threshold and only
  when the agent supplies an exact quote from the latest transcript segment.
- Stores the speaker, timestamp, quote, rationale, and confidence for every
  automatic completion.
- Allows users to undo any automatic completion.
- Recommends tasks from explicit commitments and lets the user accept or dismiss
  each recommendation.
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
presenter-microphone source. The microphone source captures audio reaching that
device only. The demo does not claim to capture every Teams participant.

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

The admin-free live demo path uses the Azure Speech JavaScript SDK in the meeting
side panel. The user must confirm participant notice, select **Start listening**,
and grant the Teams/browser microphone permission. The app sends only final text
segments to the existing session API. It does not persist or upload raw audio to
the Coach API, does not start automatically, and stops when the user ends the
session or selects **Stop listening**.

The API exchanges the Speech subscription key for a short-lived authorization
token. Configure the key only on the server:

```powershell
$env:BrowserSpeech__Enabled = "true"
$env:BrowserSpeech__SubscriptionKey = "FROM-SECRET-STORE"
$env:BrowserSpeech__AccessKey = "RANDOM-VALUE-OF-AT-LEAST-32-CHARACTERS"
$env:BrowserSpeech__Region = "westus2"
$env:BrowserSpeech__Language = "en-US"
```

The GitHub deployment enables this path only when the repository variable
`BROWSER_SPEECH_ENABLED` is `true`. It reuses
`MEDIA_BOT_SPEECH_KEY`, `MEDIA_BOT_SPEECH_REGION`, and
`MEDIA_BOT_SPEECH_LANGUAGE`, so browser transcription can be enabled while
`MEDIA_BOT_ENABLED` remains `false`. Store a separate random value of at least
32 characters in the `BROWSER_SPEECH_ACCESS_KEY` repository secret and enter
that value in the side panel for an authorized demo. It is retained only in
page memory. The Teams manifest requests the `media` device permission; tenant
policy can still block custom app or microphone access.

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
        +-- Local or Azure OpenAI coach agent
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

## Presentation demo deployment

The private demo is hosted at:

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
$env:CoachAgent__Foundry__AgentName = "csa-meeting-coach-v2"
```

The application checks for the named agent before its first analysis and creates
its first version if the agent does not exist. The agent name is incremented when
its persisted definition changes. Agent instructions reject transcript prompt
injection, sensitive-attribute inference, unsupported checklist completion, and
tasks without transcript sources. The strict JSON schema is stored on the agent
definition; invocation requests do not override it. Responses are validated again
before the session coordinator can apply them.

On Azure, grant the VM system-assigned managed identity an approved Foundry role
on the Foundry resource or project. Automatic agent creation requires a role that
can manage agents, such as `Azure AI User`/`Foundry User` as exposed by the tenant.
If an administrator pre-creates the agent, use the tenant-approved invocation-only
role instead. The deployment remains on `Local` until the GitHub repository
variable `COACH_AGENT_PROVIDER` is explicitly set to `Foundry`.

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

`appPackage/manifest.json` defines the meeting side panel. After the Azure Bot
and Entra application exist, generate one package containing both the side panel
and the calling bot:

```powershell
.\deploy\Build-TeamsPackage.ps1 `
  -BotAppId "BOT-ENTRA-APPLICATION-ID" `
  -PackageVersion "0.3.0"
```

The script injects the real bot application ID, enables calling for the
`groupChat` scope, and creates `CsaMeetingCoach-Teams.zip`. Upload that package
through the organization's approved Teams app process. The bot ID cannot be
hard-coded before Azure assigns the Entra application ID.

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
- Recommended tasks require user acceptance before external publication.
- Do not use real customer data until Privacy, Legal, Security, and tenant
  administrators approve the pilot, lawful basis, notice, retention, access, and
  DPIA.

This repository is an engineering MVP, not a legal-compliance certification.

## Tests

```powershell
dotnet test -c Release
```
