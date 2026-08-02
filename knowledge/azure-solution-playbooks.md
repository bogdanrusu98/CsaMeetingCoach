# Azure solution discovery playbooks

Reviewed: 2026-08-02

These are question paths, not reference architectures or automatic bundles.
Recommend only the next useful discovery or validation task supported by the
conversation. Name no more than three candidate services at once. Validate the
result against the Azure Well-Architected Framework and current product
documentation.

## Build one cross-cutting task

When a customer discusses a concrete service, project, system, migration, or
production rollout, create one integrated task rather than focusing only on the
primary platform:

1. Name the primary assessment, decision, or service supported by the discussion.
2. Add no more than two Microsoft dependencies whose failure could materially
   affect the project.
3. Select dependencies from identity and access, security, networking,
   governance, reliability, observability, operations, data protection, or cost
   management.
4. If the transcript has not confirmed a dependency, use `assess`, `validate`,
   or `define`; do not state that the customer selected or requires the product.

The discussed project type is enough to surface a grounded readiness check. It
is not enough to claim final product fit. Keep the recommendation to one task and
no more than three named Microsoft candidates.

Use the relevant project pattern, not the same default bundle:

| Discussion | Primary candidate | Possible cross-cutting candidates |
| --- | --- | --- |
| Estate or application migration | Azure Migrate | Microsoft Entra ID, Defender for Cloud, or Azure Policy |
| Managed application modernization | App Service or Container Apps | managed identities, Key Vault, or Azure Monitor |
| Kubernetes platform | AKS | Microsoft Entra Workload ID, Defender for Containers, or Azure Monitor |
| Database migration | Azure Database Migration Service or Azure Migrate | managed identities, Private Link, or Azure Backup |
| Enterprise AI or RAG | Microsoft Foundry or Azure AI Search | Microsoft Entra ID, Content Safety, or Application Insights |
| Hybrid operations | Azure Arc | Azure Policy, Defender for Cloud, or Azure Monitor |
| Disaster recovery | Azure Site Recovery or Azure Backup | Microsoft Entra ID access controls, Azure Monitor, or Service Health |

Choose one primary candidate and up to two cross-cutting candidates from the
columns that match material failure modes, and include them in the same task.
Do not turn the table into a product bundle or a claim of compliance.

## Legacy web application modernization

Signals:

- an IIS, Java, or other web application is hosted on-premises;
- the customer wants faster releases or less infrastructure management;
- application dependencies and migration strategy are not yet confirmed.

Ask next:

1. What runtime, OS integration, local state, background processing, and
   database dependencies exist?
2. Is the goal rehost, replatform, or refactor, and what downtime is allowed?
3. What scale, network isolation, availability, security, and operations are
   required?

Candidate task:

"Assess a representative application wave with Azure Migrate, then validate
Microsoft Entra ID access and Defender for Cloud security readiness before
selecting its target platform."

If runtime and operating requirements are already explicit, compare no more than
two fitting target platforms and use the third candidate for the most material
identity, security, or operations dependency. Do not jump directly to AKS
without a Kubernetes requirement and operating capability.

## Cloud-native API and microservices

Signals:

- containerized services, APIs, asynchronous workflows, or independent scaling;
- the team understands platform engineering or wants a managed container
  runtime.

Ask next:

1. Is direct Kubernetes API and node control required?
2. Which calls must be synchronous, queued, or event-driven?
3. What are the identity, secret, network, observability, and data requirements?

Candidate task:

"Compare Container Apps and AKS for the required platform control, then validate
Microsoft Entra workload identity for service-to-service access."

If integration is the main need, replace the identity candidate with API
Management or the one messaging service justified by message semantics. Do not
recommend every messaging product.

## Data platform and analytics

Signals:

- fragmented data, slow reporting, lakehouse, warehousing, real-time analytics,
  or Power BI scale;
- the customer is comparing Fabric, Synapse, or Databricks.

Ask next:

1. What decisions and users define success?
2. Where is the data, how sensitive is it, and how fresh must it be?
3. What platform investments, skills, governance, and capacity already exist?

Candidate task:

"Define one measurable analytics use case and compare Fabric with the existing
Synapse or Databricks platform for data integration, governance, and operations."

Do not select capacity or migration scope from user count alone.

## Generative AI and enterprise knowledge

Signals:

- assistants, document search, summarization, content generation, or agents;
- approved enterprise content should ground model responses.

Ask next:

1. What user decision or workflow improves, and how is quality measured?
2. Which data is approved, who may retrieve it, and how is access trimmed?
3. What human review, safety, privacy, content filtering, evaluation, and
   monitoring are required?

Candidate task:

"Create a bounded proof of value using Microsoft Foundry and Azure AI Search,
then validate Microsoft Entra ID permission boundaries with an approved
evaluation set and safety tests."

Do not recommend a model, region, throughput purchase, or production rollout
before current availability, data governance, and evaluation are confirmed.

## Hybrid migration and disaster recovery

Signals:

- VMware, Hyper-V, physical servers, or databases remain on-premises;
- the customer needs migration assessment, hybrid governance, backup, or DR.

Ask next:

1. Is the estate inventoried with dependency and utilization data?
2. Which workloads move, remain, retire, or require a staged migration?
3. What RTO, RPO, residency, bandwidth, landing-zone, and test requirements
   apply?

Candidate task:

"Use Azure Migrate to assess a representative wave, then validate Microsoft
Entra ID access and Defender for Cloud security readiness for its target landing
zone."

Consider Azure Arc for resources that remain outside Azure and require supported
Azure governance or operations. Add Azure Site Recovery or Azure Backup instead
of a cross-cutting candidate only when documented RTO, RPO, or restore
requirements make recovery the priority. Do not claim Arc migrates or hosts
resources.

## Security and regulated workloads

Signals:

- identity risk, private connectivity, regulatory requirements, inconsistent
  policy, security posture, or SIEM modernization.

Ask next:

1. Which data, regulations, contracts, and threat scenarios are in scope?
2. What is the current Entra, network, policy, Defender, logging, and incident
   response posture?
3. Who owns remediation and exceptions?

Candidate task:

"Run a scoped landing-zone and security-posture assessment covering Microsoft
Entra ID, Azure Policy, and Defender for Cloud ownership."

Do not say a service makes the workload compliant. Record required technical
and organizational controls and obtain Privacy, Legal, Security, and Compliance
review where applicable.

## Unexpected Azure spend

Signals:

- unexplained growth, no budgets, idle resources, poor allocation, or pressure
  to buy a discount mechanism immediately.

Ask next:

1. Who owns spend, and how is it allocated to workloads?
2. What do Cost Management and Advisor show over a representative period?
3. Which workloads are stable, variable, scheduled, non-production, or changing?
4. What agreement, license benefits, and purchasing permissions apply?

Candidate task:

"Establish Cost Management visibility and review Advisor with workload owners
before comparing right-sizing, reservations, savings plans, or Hybrid Benefit."

Do not infer a commitment amount, discount, eligibility, or term from a verbal
monthly-spend figure.

## Production readiness

Signals:

- a workload is moving from proof of concept to production;
- support, reliability, security, monitoring, and ownership are incomplete.

Ask next:

1. What SLO, RTO, RPO, security, privacy, and compliance gates apply?
2. Who owns deployment, observability, on-call, backup, restore, and incident
   response?
3. Is the support plan and escalation route appropriate for the workload?

Candidate task:

"Complete a Well-Architected production-readiness review and close named gaps
in reliability, security, cost, operations, and performance before go-live."

## Official sources

- Azure Well-Architected Framework:
  https://learn.microsoft.com/en-us/azure/well-architected/
- Cloud Adoption Framework:
  https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/
- Azure Architecture Center:
  https://learn.microsoft.com/en-us/azure/architecture/browse/
- Azure Migrate:
  https://learn.microsoft.com/en-us/azure/migrate/migrate-services-overview
- Microsoft Foundry:
  https://learn.microsoft.com/en-us/azure/foundry/what-is-foundry
- Cost Management:
  https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/cost-mgt-best-practices
