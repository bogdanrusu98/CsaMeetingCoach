# Azure foundations for live presentation coaching

Reviewed on 2026-08-04. This guide provides concise, client-ready explanations
for foundational Azure discussions. It does not imply that a customer selected
a service, region, management hierarchy, identity design, or commercial offer.

## Cloud responsibility and service models

Cloud computing delivers technology services over the internet with capacity
that can scale on demand. The shared responsibility model divides security and
operational duties between the provider and customer. The provider operates
physical datacenters, physical hosts, and physical networking. Customers retain
responsibility for their data, identities, accounts, and access decisions.

Infrastructure as a Service (IaaS) provides virtualized compute, storage, and
networking. The provider operates the physical infrastructure while the
customer manages the operating system, configuration, applications, and data.

Platform as a Service (PaaS) adds provider-managed operating systems,
middleware, and runtimes. The customer focuses primarily on application code,
data, access, and workload configuration.

Software as a Service (SaaS) provides a complete hosted application. The
provider manages most of the application stack while the customer manages data,
identities, access settings, and device posture.

The service model changes operational responsibility; it does not remove the
need to validate security, data governance, access, resilience, and cost.

## Regions and availability zones

An Azure region contains one or more datacenters connected by a high-capacity,
low-latency network. Region selection affects user latency, data residency,
service availability, and the resilience options available to a workload.

An availability zone is a separated group of datacenters within an Azure
region, with independent power, cooling, and networking. Some services provide
zone-redundant operation while zonal resources require a multi-zone design to
remain available through a zone failure.

A region or zone mention alone is not evidence that the location meets the
customer's residency, latency, availability, recovery, service-support, or
capacity requirements.

## Azure management hierarchy

Azure Resource Manager is Azure's deployment and management layer. It provides
consistent authorization, locks, tags, and declarative deployment through the
portal, APIs, Azure CLI, PowerShell, SDKs, ARM templates, and Bicep.

Azure has four principal management scopes: management groups, subscriptions,
resource groups, and resources. Settings applied at a higher scope can inherit
to lower scopes.

Management groups provide governance above subscriptions. Policy and role
assignments applied to a management group can inherit through child management
groups and subscriptions.

An Azure subscription is a management and billing boundary. It can isolate
environments, access, policy, budgets, quotas, and cost ownership.

A resource group contains related resources that share a management lifecycle.
Access, policy, locks, tags, deployments, updates, and coordinated deletion can
be scoped to the group.

Hierarchy design should follow governance, security, operational, billing, and
workload-lifecycle requirements rather than organizational names alone.

## Identity and resource authorization

Microsoft Entra ID is Microsoft's cloud identity and access management service.
It authenticates users, devices, applications, and workloads and supports
policy-based access controls.

Azure role-based access control (Azure RBAC) authorizes actions on Azure
resources. A role assignment combines a security principal, role definition,
and scope. Authentication proves an identity; authorization determines what
that identity can do at a selected scope.

Least-privilege design requires explicit principals, required actions, scope,
separation of duties, privileged-access controls, and an access-review process.

## Presentation coaching patterns

- For a service-model discussion, connect control and provider responsibility to
  security ownership and operational effort.
- For a region or zone discussion, connect location to residency, latency,
  service availability, fault isolation, and recovery requirements.
- For a management-scope discussion, connect inheritance to policy, access,
  cost ownership, environment isolation, and workload lifecycle.
- For an identity discussion, distinguish Microsoft Entra ID authentication
  from Azure RBAC authorization.
- Treat every recommendation as a validation or design topic until explicit
  customer requirements support a concrete decision.

## Official Microsoft sources

- https://learn.microsoft.com/training/modules/describe-cloud-compute/
- https://learn.microsoft.com/training/modules/describe-cloud-compute/4-describe-shared-responsibility-model
- https://learn.microsoft.com/training/modules/describe-cloud-service-types/
- https://learn.microsoft.com/azure/reliability/regions-overview
- https://learn.microsoft.com/azure/reliability/availability-zones-overview
- https://learn.microsoft.com/azure/azure-resource-manager/management/overview
- https://learn.microsoft.com/azure/governance/management-groups/overview
- https://learn.microsoft.com/azure/cloud-adoption-framework/ready/azure-setup-guide/organize-resources
- https://learn.microsoft.com/entra/fundamentals/what-is-entra
- https://learn.microsoft.com/azure/role-based-access-control/overview
