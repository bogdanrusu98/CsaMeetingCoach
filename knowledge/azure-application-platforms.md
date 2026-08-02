# Azure application platforms

Reviewed: 2026-08-02

Use this guide to turn explicit workload signals into clarification questions
and a short list of candidates. Do not select a service from a product name
alone. Validate architecture, scale, security, operations, region, service
limits, feature status, and commercial terms before commitment.

## Start with workload discovery

Before recommending a runtime, clarify:

- application type, runtime, operating system, and deployment model;
- rehost, replatform, refactor, or rebuild tolerance;
- steady, scheduled, bursty, event-driven, batch, or HPC demand;
- state, storage, latency, availability, and recovery requirements;
- inbound and outbound networking, private connectivity, and identity;
- team skills, operational ownership, compliance, and delivery deadline.

Use the Azure Architecture Center compute decision tree as a comparison aid,
not as an automatic answer.

## Azure Virtual Machines and Virtual Machine Scale Sets

Conversation signals:

- the workload needs operating-system control, custom agents, drivers, or
  software that cannot run on a managed application platform;
- the first migration step is rehost or move as-is;
- identical VM instances must scale behind a load balancer.

Ask about supported OS versions, CPU and memory profile, disks, availability
zones, patching, backup, monitoring, licensing, and who owns guest operations.

Candidate actions:

- assess the estate with Azure Migrate before sizing;
- compare a VM or scale-set landing zone with a later modernization path;
- evaluate Azure Hybrid Benefit only after license eligibility is confirmed.

Hold when the workload can use PaaS without losing required capabilities, or
when discovery and sizing are missing. Do not infer a VM SKU or commitment
quantity from a meeting transcript.

Common combination: Azure Migrate, Virtual Machines, Managed Disks, Azure
Monitor, Azure Backup, Defender for Servers, and Site Recovery where justified.

## Azure App Service

Conversation signals:

- a web app, REST API, or backend should move to managed hosting;
- the team wants managed patching, deployment slots, autoscale, TLS, and
  integrated diagnostics rather than VM administration;
- supported application runtimes or custom containers are acceptable.

Ask about runtime support, OS dependencies, background jobs, scale pattern,
private endpoints or VNet integration, authentication, deployment slots, and
availability requirements.

Candidate action: compare App Service with Container Apps or AKS against the
customer's control and operational requirements.

Hold when the application requires kernel access, unsupported components,
specialized hardware, or capabilities not supported by the selected plan.
Verify plan features, scale limits, regional availability, and network topology.

Common combination: App Service, Microsoft Entra ID, API Management, Azure SQL,
Key Vault, Application Insights, and Front Door with WAF for a global web app.

## Azure Functions

Conversation signals:

- processing is triggered by HTTP, queues, schedules, files, or events;
- demand is intermittent or bursty;
- units of work are small and independently scalable.

Ask about trigger type, execution duration, concurrency, cold-start tolerance,
state, networking, retry and idempotency behavior, and predictable baseline
load.

Candidate action: validate a serverless event flow with Functions and the
appropriate messaging service.

Hold when long-running execution, special runtime control, or a continuously
busy workload makes another compute option a better fit. Verify the current
hosting-plan comparison and service limits.

Common combination: Functions, Service Bus or Event Grid, Storage, Cosmos DB,
Managed Identity, and Application Insights.

## Azure Container Apps

Conversation signals:

- the team has container images but does not need direct Kubernetes API access;
- microservices, jobs, event-driven scale, revisions, or internal service
  communication are required with a smaller operations surface than AKS.

Ask about Kubernetes API requirements, workload identity, ingress, networking,
GPU needs, scaling signals, persistent storage, and multi-container behavior.

Candidate action: compare Container Apps with App Service for simple web
workloads and with AKS when platform control is required.

Hold when the customer needs cluster-level extensions, custom schedulers,
specialized node pools, or direct control of Kubernetes objects.

## Azure Kubernetes Service

Conversation signals:

- the customer already operates Kubernetes or needs Kubernetes APIs and
  ecosystem compatibility;
- workloads require advanced scheduling, node-pool control, service mesh,
  Windows containers, GPUs, or platform-team governance;
- many containerized services share a managed cluster platform.

Ask about Kubernetes maturity, upgrade ownership, cluster tenancy, networking,
identity, policy, registry, observability, backup, availability zones, and
security operations.

Candidate action: run an AKS architecture and operational-readiness assessment;
compare current AKS operating modes where available.

Hold when a small team has a simple application and no Kubernetes operating
model. AKS removes control-plane management but not application and cluster
operations.

Common combination: AKS, Container Registry, Workload Identity, Key Vault,
Azure Policy, Defender for Containers, Azure Monitor, and managed Prometheus.

## Integration and messaging

### API Management

Signals include APIs that need consistent authentication, policy, throttling,
versioning, analytics, or a developer portal across Azure, on-premises, or
multi-cloud backends. Ask whether APIs are internal, partner, or public; how
they authenticate; and whether private networking or a self-hosted gateway is
required. Hold until stable API contracts and tier-specific network needs are
understood.

### Service Bus

Signals include reliable enterprise queues, topics, subscriptions, ordering,
transactions, duplicate detection, and decoupled business workflows. Ask about
delivery semantics, ordering, message size, throughput, dead-letter handling,
and disaster recovery.

### Event Grid

Signals include reactive notification that an Azure or custom event occurred.
Ask about event sources, subscribers, filtering, delivery retry, and handler
idempotency. It routes events; it is not a general transactional queue.

### Event Hubs

Signals include high-throughput telemetry, logs, clickstreams, or IoT event
ingestion. Ask about events per second, retention, partitions, consumers,
schema, capture, and analytics targets.

### Logic Apps

Signals include connector-rich workflow automation, B2B integration, or
orchestration across SaaS and enterprise systems. Ask about connectors,
networking, state, error handling, human approvals, and integration-account
requirements.

## Official sources

- Compute decision tree:
  https://learn.microsoft.com/en-us/azure/architecture/guide/technology-choices/compute-decision-tree
- App Service overview:
  https://learn.microsoft.com/en-us/azure/app-service/overview
- Functions overview:
  https://learn.microsoft.com/en-us/azure/azure-functions/functions-overview
- Container service selection:
  https://learn.microsoft.com/en-us/azure/architecture/guide/choose-azure-container-service
- AKS overview:
  https://learn.microsoft.com/en-us/azure/aks/what-is-aks
- API Management concepts:
  https://learn.microsoft.com/en-us/azure/api-management/api-management-key-concepts
- Service Bus overview:
  https://learn.microsoft.com/en-us/azure/service-bus-messaging/service-bus-messaging-overview
- Event Grid comparison:
  https://learn.microsoft.com/en-us/azure/event-grid/compare-messaging-services
- Logic Apps overview:
  https://learn.microsoft.com/en-us/azure/logic-apps/logic-apps-overview
