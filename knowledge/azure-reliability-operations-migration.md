# Azure reliability, operations, migration, and hybrid

Reviewed: 2026-08-02

Begin with business requirements and failure modes. A product is not a recovery
strategy by itself. Define ownership, test procedures, monitoring, escalation,
and current service capabilities.

## Reliability requirements

Clarify:

- critical user journeys and dependencies;
- service-level objectives and contractual commitments;
- recovery time objective (RTO) and recovery point objective (RPO);
- acceptable degradation, data loss, downtime, and recovery cost;
- zone, region, identity, network, dependency, and operator failure modes;
- backup, restore, failover, failback, and disaster-recovery test cadence.

Use the Well-Architected Framework Reliability pillar and a failure-mode
analysis to drive options.

## Availability Zones and regions

Conversation signals:

- a production workload has explicit availability requirements;
- independent datacenter failure must not stop the service;
- a platform service offers zonal or zone-redundant deployment.

Candidate action: verify the selected service, SKU, and region support the
required zone behavior, then design and test dependency resilience.

Hold when the application or a critical dependency remains single-zone. A
zone-redundant label does not prove end-to-end resilience. Multi-region design
requires a business requirement, data strategy, traffic routing, and tested
failover.

## Azure Backup

Signals include recovery from deletion, corruption, ransomware, or operational
error and retention requirements for supported workloads.

Ask about protected workloads, data size and change rate, backup frequency,
retention, vault redundancy, immutability, soft delete, encryption, access,
restore objectives, and test cadence.

Candidate action: define a backup policy from RPO and retention, then run and
record representative restores. Backup success without restore testing is not
sufficient.

## Azure Site Recovery

Signals include orchestrated disaster recovery for supported Azure VMs or
on-premises servers and a documented RTO/RPO.

Ask about source platform, supported configuration, churn, target region,
network mapping, application consistency, recovery plans, dependencies,
failover, failback, and test isolation.

Candidate action: validate support and run a non-disruptive test failover for a
bounded workload. Hold until recovery objectives and application dependencies
are known.

## Azure Monitor and Application Insights

Signals include metrics, logs, traces, alerts, application performance,
dependency visibility, and incident investigation.

Ask which user journeys and service-level indicators matter, telemetry sources,
retention, data volume, privacy, alert thresholds, action groups, on-call
ownership, dashboards, and current observability tools.

Candidate actions:

- define a minimal observability baseline tied to SLOs;
- instrument applications with supported OpenTelemetry or Application Insights;
- route actionable alerts to an owned incident process;
- set retention and collection controls before broad diagnostic ingestion.

Hold indiscriminate logging that has no use case, owner, or cost control.

## Azure Advisor and Service Health

Advisor provides recommendations based on resource configuration and usage.
Use it as an input to human review, not an automatic authorization for
production change or commercial commitment.

Service Health helps track service issues, planned maintenance, and health
advisories relevant to the customer's environment. Ask who receives alerts and
owns response.

## Azure Migrate

Conversation signals:

- on-premises servers, databases, or web applications need discovery,
  assessment, business-case analysis, or migration;
- the estate size, utilization, dependencies, and readiness are uncertain.

Ask about inventory, virtualization platform, credentials and discovery
permissions, dependency analysis, utilization window, migration waves,
downtime, data transfer, compliance, and target landing zone.

Candidate actions:

- discover and assess before selecting target services and sizes;
- group workloads into migration waves by dependency and business risk;
- compare rehost, replatform, refactor, retain, retire, and replace per workload.

Hold a broad migration recommendation when the estate, business case, landing
zone, and application ownership are not understood.

## Azure Database Migration Service

Signals include moving supported database engines to Azure data targets with
guided online or offline migration. Ask about source and target versions,
compatibility assessment, downtime, network throughput, schema and data size,
validation, and rollback.

## Azure Data Box

Signals include large offline data transfer where network time or capacity is a
constraint. Ask about data volume, source interfaces, chain of custody,
encryption, destination, import region, timeline, and validation. Verify current
device availability and ordering requirements.

## Azure Arc

Conversation signals:

- servers or Kubernetes clusters will remain on-premises or in other clouds;
- Azure-based inventory, policy, security, update, or monitoring capabilities
  are required across a hybrid estate.

Ask which resources remain outside Azure, connectivity and proxy constraints,
data residency, supported operating systems or Kubernetes distributions,
identity, agent lifecycle, policy scope, and operating ownership.

Candidate action: onboard a representative, non-critical scope and validate
governance and operations before expanding.

Hold when the customer assumes Arc turns non-Azure resources into Azure-hosted
services or removes local platform operations.

## Official sources

- Well-Architected Reliability:
  https://learn.microsoft.com/en-us/azure/well-architected/reliability/
- Availability Zones:
  https://learn.microsoft.com/en-us/azure/reliability/availability-zones-overview
- Azure Backup:
  https://learn.microsoft.com/en-us/azure/backup/backup-overview
- Site Recovery:
  https://learn.microsoft.com/en-us/azure/site-recovery/site-recovery-overview
- Azure Monitor:
  https://learn.microsoft.com/en-us/azure/azure-monitor/fundamentals/overview
- Application Insights:
  https://learn.microsoft.com/en-us/azure/azure-monitor/app/app-insights-overview
- Azure Advisor:
  https://learn.microsoft.com/en-us/azure/advisor/advisor-overview
- Azure Service Health:
  https://learn.microsoft.com/en-us/azure/service-health/overview
- Azure Migrate:
  https://learn.microsoft.com/en-us/azure/migrate/migrate-services-overview
- Azure Arc:
  https://learn.microsoft.com/en-us/azure/azure-arc/overview
