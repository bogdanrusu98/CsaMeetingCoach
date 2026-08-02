# Azure networking, identity, security, and governance

Reviewed: 2026-08-02

Security and governance are workload requirements, not optional add-ons. Start
with business risk, data classification, identity, compliance, connectivity,
operations, and the shared-responsibility model. Do not claim that deploying a
service alone proves compliance.

## Landing zones and governance

Conversation signals:

- multiple subscriptions, teams, regions, or environments need a repeatable
  foundation;
- inconsistent identity, networking, policy, logging, tags, or ownership slows
  migration;
- the customer is starting a large Azure adoption or modernization program.

Ask about tenant and subscription structure, management groups, platform and
application ownership, connectivity, allowed regions, policy, logging,
security, deployment automation, cost allocation, and exception handling.

Candidate action: assess the Azure landing-zone design areas and create a
phased remediation backlog. Hold mass migration until minimum identity,
networking, policy, security, and operations controls are ready.

### Azure Policy

Use Azure Policy to audit or enforce resource standards such as allowed
locations and types, required configuration, diagnostics, and tags. Ask about
policy scope, exemptions, remediation identity, rollout safety, and testing.
Start with audit where enforcement could disrupt existing workloads.

### Management groups

Use management groups to organize subscriptions for policy and access
inheritance. Design from governance boundaries, not the company org chart
alone. Keep the hierarchy understandable and define ownership.

## Identity

### Microsoft Entra ID

Signals include workforce or workload authentication, MFA, Conditional Access,
single sign-on, external collaboration, or lifecycle governance.

Ask about identity sources, authentication methods, emergency access,
Conditional Access, privileged roles, guests, application identities, access
reviews, joiner-mover-leaver processes, and logging.

Candidate actions:

- assess identity security posture and MFA coverage;
- replace application secrets with managed identities where supported;
- define least-privilege roles and privileged access workflows.

Hold broad policy changes until break-glass access, service dependencies, test
groups, and rollback are defined.

### Managed identities and Key Vault

Use managed identities to avoid application-managed credentials where the
target service supports Entra authentication. Use Key Vault for secrets, keys,
and certificates that must exist. Ask about rotation, recovery, network access,
RBAC, logging, and separation of duties. Key Vault does not remove the need for
an application secret inventory and lifecycle.

## Network foundations

### Virtual Network and Private Link

Signals include private application tiers, network segmentation, private access
to PaaS, and hybrid connectivity. Ask about address space, DNS, routing,
firewalls, peering, egress, service endpoints versus private endpoints, and
operations.

Private Link is a candidate when a supported service must be reached through a
private endpoint. Validate private DNS, public-network-access policy, data
exfiltration controls, cost, and operational complexity.

### VPN Gateway and ExpressRoute

VPN Gateway is a candidate for encrypted site-to-site or point-to-site
connectivity over the internet. ExpressRoute is a candidate when a private
provider connection, predictable connectivity, or large hybrid topology is
required.

Ask about sites, bandwidth, latency, routing, BGP, redundancy, encryption,
provider availability, lead time, and failover. Do not promise ExpressRoute
delivery dates or circuit performance before provider and SKU validation.

### Azure Firewall

Signals include centralized egress and east-west network policy, threat
intelligence, IDPS, or application-aware filtering. Ask about traffic flows,
forced tunneling, DNS, TLS inspection, logging, scale, high availability, and
existing NVAs.

### Front Door and Web Application Firewall

Signals include global HTTP/S applications needing edge routing, acceleration,
TLS termination, health-based failover, or WAF protection. Ask about origins,
regions, caching, custom domains, certificates, private origins, rules,
security policy, and data residency.

### DDoS Protection

Clarify public IP exposure, business impact, baseline traffic, incident
response, and existing protection. Compare platform-provided protection and
current DDoS plan capabilities against the risk; do not recommend a paid plan
without the exposure and response requirement.

## Cloud security

### Microsoft Defender for Cloud

Signals include cloud security posture, regulatory assessment, workload
protection, attack-path analysis, vulnerability management, DevSecOps, or
multi-cloud visibility.

Ask which subscriptions and clouds are in scope, current plans, resource
coverage, regulatory standards, alert ownership, agentless scanning, data
collection, and cost.

Candidate action: review Secure Score and plan coverage, then prioritize
high-impact recommendations with owners. Hold paid-plan expansion until scope,
cost, overlap, and operating ownership are clear.

### Microsoft Sentinel

Signals include cloud-native SIEM and security orchestration across Microsoft
and third-party sources. Ask about data sources and volume, retention, existing
SIEM, detections, incident process, automation, roles, and data residency.

Candidate action: define a bounded onboarding wave with use cases and ingestion
cost controls. Ingesting every log without detection and response ownership is
not a complete security plan.

## Compliance and privacy

Ask which regulations, contractual controls, data categories, subjects, regions,
retention rules, and audit evidence apply. Map requirements to technical and
organizational controls with Privacy, Legal, Security, and Compliance owners.
Use current Microsoft compliance documentation and the customer's own legal
analysis. No Azure service automatically makes a workload GDPR compliant.

## Official sources

- Cloud Adoption Framework landing zones:
  https://learn.microsoft.com/en-us/azure/cloud-adoption-framework/ready/landing-zone/
- Azure Policy:
  https://learn.microsoft.com/en-us/azure/governance/policy/overview
- Microsoft Entra:
  https://learn.microsoft.com/en-us/entra/fundamentals/what-is-entra
- Managed identities:
  https://learn.microsoft.com/en-us/entra/identity/managed-identities-azure-resources/overview
- Key Vault:
  https://learn.microsoft.com/en-us/azure/key-vault/general/overview
- Virtual Network:
  https://learn.microsoft.com/en-us/azure/virtual-network/virtual-networks-overview
- Private Link:
  https://learn.microsoft.com/en-us/azure/private-link/private-link-overview
- ExpressRoute:
  https://learn.microsoft.com/en-us/azure/expressroute/expressroute-introduction
- Azure Firewall:
  https://learn.microsoft.com/en-us/azure/firewall/overview
- Front Door:
  https://learn.microsoft.com/en-us/azure/frontdoor/front-door-overview
- Defender for Cloud:
  https://learn.microsoft.com/en-us/azure/defender-for-cloud/defender-for-cloud-introduction
- Microsoft Sentinel:
  https://learn.microsoft.com/en-us/azure/sentinel/overview
- Microsoft Trust Center:
  https://www.microsoft.com/en-us/trust-center
