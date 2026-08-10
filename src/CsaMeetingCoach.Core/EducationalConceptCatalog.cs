namespace CsaMeetingCoach.Core;

public enum ConceptCategory
{
    FoundationalCloud,
    ManagementGovernance,
    IdentitySecurity,
    Networking,
    ComputeContainersAppPlatforms,
    StorageDatabases,
    DataAiIntegration,
    MonitoringReliability,
    MigrationDevOpsFinOps
}

public sealed record EducationalConcept(
    string ConceptKey,
    string CanonicalTitle,
    string[] Aliases,
    string DefinitionText,
    string HintText,
    ConceptCategory Category,
    bool RequiresAzureVendorScope,
    string LearnUrl);

public static partial class EducationalConceptCatalog
{
    private static readonly HashSet<string> SafeAcronyms = new(StringComparer.OrdinalIgnoreCase)
    {
        "AKS", "NSG", "VMSS", "ARO", "PIM", "RBAC", "IaaS", "PaaS", "SaaS", "MFA", "CDN",
        "NFS", "SMB", "GRS", "LRS", "ZRS",
        // Extended acronyms for new catalog entries
        "ACR", "AVD", "AVS", "WAF", "KEDA", "azd", "RAG", "SLO", "DCR", "AMA"
    };

    private static readonly HashSet<string> UnsafeSingleTokens = new(StringComparer.OrdinalIgnoreCase)
    {
        "cloud", "hybrid", "batch", "arc", "vpn", "monitor", "alerts", "budget",
        "subnet", "subnets", "tags", "tagging", "locks", "migrate", "databricks",
        "synapse", "sentinel", "lighthouse", "kusto", "finops", "rehost", "refactor",
        "vm", "vms", "arm", "gateway", "functions", "search", "redis", "blob", "blobs",
        "advisor", "blueprints", "reservations", "sla", "rto", "rpo", "tco", "roi"
    };

    public static IReadOnlyList<EducationalConcept> All { get; } = BuildAll();

    private static List<EducationalConcept> BuildAll()
    {
        var list = new List<EducationalConcept>();
        list.AddRange(CoreConcepts());
        list.AddRange(ExtendedConcepts());
        return list;
    }

    private static IEnumerable<EducationalConcept> CoreConcepts()
    {
        return new List<EducationalConcept>
        {
            new(
                "cloud-computing",
                "Cloud computing",
                ["cloud computing"],
                "Cloud computing delivers compute, storage, networking, and software services over the internet with elastic capacity and managed operations.",
                "A cloud design decision usually changes who manages capacity, patching, resilience, and service-level responsibilities.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/get-started/cloud-concepts"),
            new(
                "shared-responsibility-model",
                "Shared responsibility model",
                ["shared responsibility model", "shared responsibility"],
                "The shared responsibility model splits security and operational duties between the provider and the customer.",
                "The customer still owns identities, data, workload configuration, and access choices even when the platform is managed.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/security/fundamentals/shared-responsibility"),
            new(
                "infrastructure-as-a-service",
                "Infrastructure as a Service (IaaS)",
                ["IaaS", "infrastructure as a service", "iaas"],
                "IaaS provides virtual machines, storage, and networking while the customer manages the operating system, runtime, and application.",
                "IaaS gives more control, but it also leaves patching, backup design, and more operations with the customer team.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/architecture/guide/technology-choices/compute-decision-tree"),
            new(
                "platform-as-a-service",
                "Platform as a Service (PaaS)",
                ["PaaS", "platform as a service", "paas"],
                "PaaS provides a managed application platform so teams focus more on code and data than server maintenance.",
                "PaaS usually reduces operating-system work, but teams still need to design identity, networking, scaling, and data protection.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/architecture/guide/technology-choices/compute-decision-tree"),
            new(
                "software-as-a-service",
                "Software as a Service (SaaS)",
                ["SaaS", "software as a service", "saas"],
                "SaaS is a complete hosted application that the provider operates for many customers as a service.",
                "SaaS minimizes infrastructure ownership, but the customer still manages data governance, identities, and product configuration.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/architecture/guide/saas-multitenant-solution-architecture/considerations/tenancy-models"),
            new(
                "public-cloud",
                "Public cloud",
                ["public cloud"],
                "A public cloud offers services from provider-operated datacenters that multiple customers use through isolated tenants.",
                "Public cloud speed comes from shared provider platforms, while isolation depends on identity, policy, and workload design choices.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/get-started/cloud-concepts"),
            new(
                "private-cloud",
                "Private cloud",
                ["private cloud"],
                "A private cloud uses dedicated infrastructure for one organization while still applying cloud-style automation and self-service patterns.",
                "Private cloud can increase control, but it usually keeps more capacity planning and hardware lifecycle work with the organization.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/get-started/cloud-concepts"),
            new(
                "hybrid-cloud",
                "Hybrid cloud",
                ["hybrid cloud"],
                "Hybrid cloud combines on-premises or edge resources with public-cloud services under one operating model.",
                "Hybrid designs often need consistent identity, policy, networking, and monitoring across multiple environments.",
                ConceptCategory.FoundationalCloud,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/scenarios/hybrid/"),
            new(
                "azure-region",
                "Azure region",
                ["Azure region", "Azure regions", "azure region", "azure regions"],
                "An Azure region is a geographic area containing one or more datacenter locations connected by a low-latency network.",
                "Region choice affects latency, residency, service availability, paired-region options, and disaster-recovery design.",
                ConceptCategory.FoundationalCloud,
                true,
                "https://learn.microsoft.com/azure/reliability/regions-list"),
            new(
                "availability-zone",
                "Azure Availability Zone",
                ["Azure Availability Zone", "Azure Availability Zones", "availability zone", "availability zones"],
                "An availability zone is a physically separate set of datacenter infrastructure within an Azure region.",
                "Zone redundancy improves fault isolation inside one region, but the workload still needs zone-aware architecture and supported services.",
                ConceptCategory.FoundationalCloud,
                true,
                "https://learn.microsoft.com/azure/reliability/availability-zones-overview"),
            new(
                "resource-group",
                "Azure Resource Group",
                ["Azure Resource Group", "resource group", "resource groups"],
                "A resource group is a logical container for related Azure resources that share a management lifecycle.",
                "Tags, locks, policy effects, and deployment boundaries are commonly applied at the resource-group scope.",
                ConceptCategory.FoundationalCloud,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/management/overview"),
            new(
                "azure-subscription",
                "Azure subscription",
                ["Azure subscription", "Azure subscriptions"],
                "An Azure subscription is a billing and management boundary for Azure resources and service quotas.",
                "Teams often separate subscriptions by environment, ownership, or compliance boundary to simplify access and cost control.",
                ConceptCategory.FoundationalCloud,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/management/overview"),
            new(
                "management-group",
                "Azure Management Group",
                ["Azure Management Group", "Azure Management Groups", "management group", "management groups"],
                "A management group is an Azure governance scope above subscriptions that supports inherited policy and access assignments.",
                "Management groups help standardize policy and RBAC across many subscriptions without repeating the same assignments in each one.",
                ConceptCategory.FoundationalCloud,
                true,
                "https://learn.microsoft.com/azure/governance/management-groups/overview"),
            new(
                "azure-resource-manager",
                "Azure Resource Manager (ARM)",
                ["Azure Resource Manager", "ARM", "arm", "resource manager"],
                "Azure Resource Manager is Azure's deployment and control plane for resources, access, tags, locks, and templates.",
                "ARM gives a consistent API layer, so tools like the portal, CLI, Bicep, and templates target the same control model.",
                ConceptCategory.FoundationalCloud,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/management/overview"),

            new(
                "azure-policy",
                "Azure Policy",
                ["Azure Policy", "azure policy"],
                "Azure Policy evaluates resources against rules and applies configured audit, deny, or modification effects to noncompliant configurations.",
                "Policy can enforce standards like allowed SKUs, required tags, or private networking, but effects depend on the assigned definition.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/governance/policy/overview"),
            new(
                "azure-blueprints",
                "Azure Blueprints",
                ["Azure Blueprints", "blueprints", "azure blueprints"],
                "Azure Blueprints package governance artifacts such as policy, role assignments, and templates into a repeatable environment definition.",
                "Blueprints help standardize deployment baselines, although many teams now pair Policy with templates or Bicep for similar outcomes.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/governance/blueprints/overview"),
            new(
                "azure-cost-management",
                "Azure Cost Management",
                ["Azure Cost Management", "cost management", "azure cost management"],
                "Azure Cost Management helps analyze spend, budgets, and cost trends across subscriptions and resource scopes.",
                "Budgets alert on thresholds, while analysis views help separate shared-platform costs from workload-specific consumption.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/cost-management-billing/cost-management-billing-overview"),
            new(
                "azure-advisor",
                "Azure Advisor",
                ["Azure Advisor", "advisor", "azure advisor"],
                "Azure Advisor reviews deployed resources and recommends actions related to reliability, security, performance, operations, and cost.",
                "Advisor suggestions are signals, not automatic decisions, so the workload context still matters before making changes.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/advisor/advisor-overview"),
            new(
                "azure-tags",
                "Azure tags",
                ["Azure tags", "azure tagging", "tagging"],
                "Azure tags are key-value metadata attached to resources for organization, automation, reporting, and cost allocation.",
                "Tagging is most useful when names, required keys, and ownership rules are standardized across deployment pipelines.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/management/tag-resources"),
            new(
                "azure-resource-locks",
                "Azure resource locks",
                ["Azure resource locks", "resource locks", "locks"],
                "Resource locks protect Azure resources from accidental deletion or modification at the control-plane level.",
                "A delete lock does not stop application traffic, but it can block operational changes until an authorized user removes it.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/management/lock-resources"),
            new(
                "arm-template",
                "ARM template",
                ["ARM template", "ARM templates", "arm template", "arm templates"],
                "An ARM template is a JSON-based declarative definition for deploying Azure resources through Azure Resource Manager.",
                "Templates enable repeatable deployments, but teams often use Bicep to author them with simpler syntax and reusable modules.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/templates/overview"),
            new(
                "bicep",
                "Bicep",
                ["Bicep", "bicep"],
                "Bicep is a domain-specific language for defining Azure infrastructure declaratively and compiling to ARM templates.",
                "Bicep modules help share environment patterns, and the compiled deployment still runs through Azure Resource Manager.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/bicep/overview"),
            new(
                "azure-arc",
                "Azure Arc",
                ["Azure Arc", "arc", "azure arc"],
                "Azure Arc extends Azure management services to servers, Kubernetes clusters, and data services outside Azure.",
                "Arc does not move workloads into Azure by itself; it adds inventory, policy, and management patterns across locations.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/azure-arc/overview"),
            new(
                "governance-baseline",
                "Governance baseline",
                ["governance baseline"],
                "A governance baseline is the minimum set of policies, access rules, naming standards, and monitoring controls required before broad deployment.",
                "A baseline reduces drift by making environment expectations explicit before large-scale provisioning starts.",
                ConceptCategory.ManagementGovernance,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/ready/landing-zone/design-area/governance"),
            new(
                "budget-alert",
                "Budget alert",
                ["budget alert", "budget alerts", "budget"],
                "A budget alert notifies stakeholders when Azure spend reaches configured thresholds for a scope and time period.",
                "Budget alerts do not stop usage by themselves; instead, they expose spend signals for investigation or control adjustments.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/cost-management-billing/costs/tutorial-acm-create-budgets"),

            new(
                "microsoft-entra-id",
                "Microsoft Entra ID",
                ["Microsoft Entra ID", "Entra ID", "Azure Active Directory", "Azure AD"],
                "Microsoft Entra ID is Microsoft's cloud identity service for users, apps, devices, and policy-based access control.",
                "Authentication verifies identity, while access still depends on app roles, Conditional Access, RBAC, and resource-specific permissions.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/fundamentals/whatis"),
            new(
                "azure-rbac",
                "Azure RBAC",
                ["Azure RBAC", "Azure role-based access control", "role based access control", "role-based access control", "rbac"],
                "Azure RBAC is Azure's authorization system for resource actions based on a role, security principal, and scope.",
                "RBAC answers who can do what at which scope, so over-broad roles can affect many resources through inheritance.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/role-based-access-control/overview"),
            new(
                "multifactor-authentication",
                "Multifactor authentication (MFA)",
                ["MFA", "multifactor authentication", "multi factor authentication", "mfa"],
                "Multifactor authentication requires an additional verification factor beyond a password to reduce account-compromise risk.",
                "MFA improves sign-in security, but it works best alongside Conditional Access, phishing-resistant methods, and least privilege.",
                ConceptCategory.IdentitySecurity,
                false,
                "https://learn.microsoft.com/entra/identity/authentication/concept-mfa-howitworks"),
            new(
                "conditional-access",
                "Conditional Access",
                ["Conditional Access", "conditional access"],
                "Conditional Access applies sign-in rules based on user, app, device, risk, location, or authentication context.",
                "It can require MFA or block access, but effective policy design depends on understanding trusted devices and break-glass paths.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/identity/conditional-access/overview"),
            new(
                "privileged-identity-management",
                "Privileged Identity Management (PIM)",
                ["PIM", "Privileged Identity Management", "privileged identity management"],
                "PIM provides just-in-time activation and governance for privileged roles in Entra ID and Azure resources.",
                "Eligible roles reduce standing privilege, but activation flows still need approvals, auditing, and emergency-access design.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/id-governance/privileged-identity-management/pim-configure"),
            new(
                "azure-key-vault",
                "Azure Key Vault",
                ["Azure Key Vault", "key vault", "azure key vault"],
                "Azure Key Vault stores and controls access to secrets, keys, and certificates used by applications and services.",
                "Key Vault centralizes secret handling, but applications still need managed identity, rotation strategy, and access-boundary design.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/key-vault/general/overview"),
            new(
                "defender-for-cloud",
                "Microsoft Defender for Cloud",
                ["Microsoft Defender for Cloud", "Defender for Cloud", "defender for cloud"],
                "Defender for Cloud is a cloud security posture and workload protection service for Azure and hybrid resources.",
                "It combines recommendations, alerts, and plans, so coverage depends on which resource types and protections are enabled.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/defender-for-cloud/defender-for-cloud-introduction"),
            new(
                "microsoft-sentinel",
                "Microsoft Sentinel",
                ["Microsoft Sentinel", "Sentinel", "sentinel"],
                "Microsoft Sentinel is a cloud-native SIEM and SOAR platform for collecting, correlating, and automating security operations.",
                "Sentinel value comes from data connectors, analytics rules, and incident workflows rather than logging alone.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/sentinel/overview"),
            new(
                "managed-identity",
                "Managed identity",
                ["Managed identity", "managed identity", "managed identities"],
                "A managed identity is an automatically managed Entra identity that Azure services use to authenticate without embedded secrets.",
                "Managed identities remove many credential-rotation tasks, but the workload still needs the right RBAC or data-plane permissions.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/identity/managed-identities-azure-resources/overview"),
            new(
                "service-principal",
                "Service principal",
                ["Service principal", "service principal"],
                "A service principal is an Entra security identity used by an application or automation process.",
                "Unlike managed identity, a service principal may rely on a secret or certificate that must be protected and rotated.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/identity-platform/app-objects-and-service-principals"),
            new(
                "zero-trust",
                "Zero Trust",
                ["Zero Trust", "zero trust"],
                "Zero Trust is a security model that assumes no implicit trust and continuously verifies identity, device, and access conditions.",
                "In practice it combines least privilege, strong authentication, segmentation, and monitoring instead of one perimeter control.",
                ConceptCategory.IdentitySecurity,
                false,
                "https://learn.microsoft.com/security/zero-trust/zero-trust-overview"),
            new(
                "azure-firewall",
                "Azure Firewall",
                ["Azure Firewall", "azure firewall"],
                "Azure Firewall is a managed network security service that filters traffic with central rules and threat-intelligence support.",
                "It controls traffic at the network boundary, but application-specific routing and private endpoints can still change the design.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/firewall/overview"),

            new(
                "azure-virtual-network",
                "Azure Virtual Network (VNet)",
                ["Azure Virtual Network", "VNet", "virtual network", "vnet"],
                "Azure Virtual Network is the private network foundation for Azure resources, similar to a logical network boundary in Azure.",
                "Address space, subnetting, DNS, routing, and connectivity choices in the VNet affect many later design decisions.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/virtual-network/virtual-networks-overview"),
            new(
                "subnet",
                "Subnet",
                ["subnet", "subnets", "Azure subnet", "virtual network subnet"],
                "A subnet is a segmented IP range inside a virtual network that groups resources for routing, policy, and security controls.",
                "Subnet boundaries matter because many Azure services inherit security and private-connectivity behavior from the subnet design.",
                ConceptCategory.Networking,
                false,
                "https://learn.microsoft.com/azure/virtual-network/virtual-networks-overview"),
            new(
                "network-security-group",
                "Network Security Group (NSG)",
                ["Network Security Group", "NSG", "network security group", "nsg"],
                "An NSG filters Azure network traffic with ordered allow and deny rules at subnet or network-interface scope.",
                "NSGs control packets, not application publishing, so teams often combine them with load balancers, gateways, or firewalls.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/virtual-network/network-security-groups-overview"),
            new(
                "azure-load-balancer",
                "Azure Load Balancer",
                ["Azure Load Balancer", "load balancer"],
                "Azure Load Balancer distributes Layer 4 TCP or UDP traffic across healthy backend instances.",
                "Design choices such as public versus internal exposure, health probes, and zone support change how traffic and failover behave.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/load-balancer/load-balancer-overview"),
            new(
                "health-probe",
                "Health probe",
                ["health probe", "health probes", "health check"],
                "A health probe checks whether a backend endpoint can receive new traffic so unhealthy instances are removed from rotation.",
                "Probe protocol, path, interval, and failure threshold directly affect failover speed and false-positive behavior.",
                ConceptCategory.Networking,
                false,
                "https://learn.microsoft.com/azure/load-balancer/load-balancer-custom-probe-overview"),
            new(
                "backend-pool",
                "Backend pool",
                ["backend pool", "backend pools", "backend address pool"],
                "A backend pool is the set of target instances that a load balancer or gateway can send traffic to.",
                "If backends are mis-scoped or unregistered, listeners and probes may work while traffic still has nowhere valid to go.",
                ConceptCategory.Networking,
                false,
                "https://learn.microsoft.com/azure/load-balancer/components"),
            new(
                "application-gateway",
                "Azure Application Gateway",
                ["Azure Application Gateway", "Application Gateway", "application gateway"],
                "Azure Application Gateway is a Layer 7 load balancer for HTTP and HTTPS traffic with routing and web-application-firewall options.",
                "Because it understands web requests, it can route by host or path instead of only balancing raw TCP or UDP flows.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/application-gateway/overview"),
            new(
                "azure-cdn",
                "Azure CDN",
                ["Azure CDN", "CDN", "content delivery network", "cdn"],
                "Azure CDN caches content closer to users to reduce latency and offload origin traffic.",
                "Static content benefits most, while dynamic responses need cache rules that avoid stale or incorrect user-specific results.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/cdn/cdn-overview"),
            new(
                "expressroute",
                "Azure ExpressRoute",
                ["ExpressRoute", "express route", "expressroute"],
                "ExpressRoute provides private network connectivity between on-premises infrastructure and Microsoft cloud services.",
                "It avoids internet transit, but it still requires circuit provisioning, routing design, and resiliency planning.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/expressroute/expressroute-introduction"),
            new(
                "vpn-gateway",
                "Azure VPN Gateway",
                ["VPN Gateway", "vpn gateway", "vpn"],
                "Azure VPN Gateway connects networks securely over IPsec or OpenVPN tunnels.",
                "VPN Gateway is usually faster to adopt than a private circuit, but throughput and path stability differ from ExpressRoute.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/vpn-gateway/vpn-gateway-about-vpngateways"),
            new(
                "azure-dns",
                "Azure DNS",
                ["Azure DNS", "azure dns"],
                "Azure DNS hosts DNS zones and records using Azure-managed name servers and control-plane APIs.",
                "DNS changes affect how clients find services, so record TTL and split-horizon design can matter during cutovers.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/dns/dns-overview"),
            new(
                "private-link",
                "Azure Private Link",
                ["Azure Private Link", "Private Link", "private link"],
                "Azure Private Link exposes supported services through private IP connectivity inside a virtual network.",
                "Private Link reduces public exposure, but teams still need DNS changes so clients resolve the private endpoint correctly.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/private-link/private-link-overview"),
            new(
                "azure-front-door",
                "Azure Front Door",
                ["Azure Front Door", "Front Door", "front door"],
                "Azure Front Door is a global entry service for web applications with edge routing, acceleration, and security features.",
                "It operates at the global edge, so it complements rather than replaces regional application hosting and origin design.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/frontdoor/front-door-overview"),

            new(
                "azure-virtual-machines",
                "Azure Virtual Machines",
                ["Azure Virtual Machines", "virtual machines", "virtual machine", "azure vms", "VMs", "VM"],
                "Azure Virtual Machines are on-demand compute instances that run a full operating system under customer control.",
                "VMs give flexibility, but patching, image hygiene, backup, and scaling strategy remain workload responsibilities.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/virtual-machines/overview"),
            new(
                "virtual-machine-scale-sets",
                "Virtual Machine Scale Sets (VMSS)",
                ["VMSS", "Azure Virtual Machine Scale Sets", "Virtual Machine Scale Sets", "vmss"],
                "VM Scale Sets manage a group of similar virtual machines that can scale and update together.",
                "Scale sets simplify fleet operations, but stateful applications still need externalized data and health-aware update design.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/virtual-machine-scale-sets/overview"),
            new(
                "azure-kubernetes-service",
                "Azure Kubernetes Service (AKS)",
                ["Azure Kubernetes Service", "AKS", "aks"],
                "AKS is a managed Kubernetes service that reduces cluster-management overhead while preserving the Kubernetes control model.",
                "AKS manages parts of the cluster platform, but teams still own workloads, networking choices, RBAC, and upgrade readiness.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/aks/what-is-aks"),
            new(
                "azure-container-apps",
                "Azure Container Apps",
                ["Azure Container Apps", "Container Apps", "container apps"],
                "Azure Container Apps runs containerized applications on a managed platform with built-in scaling and revisions.",
                "It is useful when teams want container packaging without managing the full Kubernetes surface area.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/container-apps/overview"),
            new(
                "azure-functions",
                "Azure Functions",
                ["Azure Functions", "azure functions"],
                "Azure Functions is an event-driven serverless compute service for running code in response to triggers.",
                "Function apps can scale quickly, but cold starts, networking mode, and state externalization still influence design choices.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/azure-functions/functions-overview"),
            new(
                "azure-app-service",
                "Azure App Service",
                ["Azure App Service", "App Service", "app service"],
                "Azure App Service is a managed platform for hosting web apps, APIs, and background workloads.",
                "It reduces host-management work, but app configuration, identity, deployment slots, and data dependencies still need design.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/app-service/overview"),
            new(
                "azure-logic-apps",
                "Azure Logic Apps",
                ["Azure Logic Apps", "Logic Apps", "logic apps"],
                "Azure Logic Apps is a workflow service for building integration and automation flows with connectors and triggers.",
                "Logic Apps orchestrates steps visually, but throughput, connector limits, and error handling still matter for production flows.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/logic-apps/logic-apps-overview"),
            new(
                "azure-batch",
                "Azure Batch",
                ["Azure Batch", "azure batch"],
                "Azure Batch schedules and runs large-scale parallel and high-performance computing jobs in Azure.",
                "It is optimized for batch processing patterns, so job packaging, queueing, and output handling shape the design.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/batch/batch-technical-overview"),
            new(
                "azure-spring-apps",
                "Azure Spring Apps",
                ["Azure Spring Apps", "Spring Apps", "spring apps"],
                "Azure Spring Apps is a managed Spring Boot platform with a retirement period that began in March 2025. Final service retirement is scheduled for March 31, 2028.",
                "Support continues until retirement; the published lifecycle and migration options affect planning for existing workloads.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/spring-apps/overview"),
            new(
                "service-fabric",
                "Azure Service Fabric",
                ["Service Fabric", "service fabric"],
                "Service Fabric is a distributed-systems platform for packaging and operating scalable services and containers.",
                "It offers deep control for platform-style workloads, but that also means more architectural and operational complexity.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/service-fabric/service-fabric-overview"),
            new(
                "azure-container-instances",
                "Azure Container Instances",
                ["Azure Container Instances", "Container Instances", "container instances"],
                "Azure Container Instances runs single containers or small groups without requiring VM or cluster management.",
                "It is convenient for bursty or simple jobs, but long-lived networking and orchestration needs may point elsewhere.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/container-instances/container-instances-overview"),
            new(
                "azure-red-hat-openshift",
                "Azure Red Hat OpenShift",
                ["Azure Red Hat OpenShift", "ARO", "aro", "red hat openshift"],
                "Azure Red Hat OpenShift is a jointly managed OpenShift platform running in Azure.",
                "It preserves the OpenShift operating model, so fit often depends on existing OpenShift skills, tooling, and process requirements.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/openshift/intro-openshift"),

            new(
                "azure-blob-storage",
                "Azure Blob Storage",
                ["Azure Blob Storage", "blob storage", "blobs"],
                "Azure Blob Storage stores large amounts of unstructured object data such as documents, images, backups, and logs.",
                "Access tier, redundancy, lifecycle rules, and naming patterns affect cost and retrieval behavior over time.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/blobs/storage-blobs-introduction"),
            new(
                "azure-files",
                "Azure Files",
                ["Azure Files", "azure files"],
                "Azure Files provides managed file shares accessible over SMB or NFS from Azure and supported on-premises environments.",
                "It helps lift shared-file patterns, but performance tier, identity integration, and backup approach still matter.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/files/storage-files-introduction"),
            new(
                "azure-disk-storage",
                "Azure Disk Storage",
                ["Azure Disk Storage", "disk storage", "managed disks"],
                "Azure Disk Storage provides durable block storage for Azure virtual machines.",
                "Disk type and performance tier affect latency, throughput, and cost, so they should align to workload IO needs.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/virtual-machines/managed-disks-overview"),
            new(
                "azure-sql-database",
                "Azure SQL Database",
                ["Azure SQL Database", "SQL Database", "azure sql"],
                "Azure SQL Database is a managed relational database service based on the SQL Server engine.",
                "It reduces server-management work, but service tier, networking mode, and resilience objectives still shape the design.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/azure-sql/database/sql-database-paas-overview"),
            new(
                "azure-cosmos-db",
                "Azure Cosmos DB",
                ["Azure Cosmos DB", "Cosmos DB", "cosmos db"],
                "Azure Cosmos DB is a globally distributed NoSQL database service with multiple APIs and tunable consistency models.",
                "Partitioning and chosen consistency level strongly influence scale behavior, latency, and request-unit consumption.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/cosmos-db/introduction"),
            new(
                "azure-database-postgresql",
                "Azure Database for PostgreSQL",
                ["Azure Database for PostgreSQL", "PostgreSQL", "postgresql"],
                "Azure Database for PostgreSQL is a managed PostgreSQL service for relational workloads in Azure.",
                "Managed hosting reduces operational tasks, but extension support, versioning, and connectivity mode still need evaluation.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/postgresql/flexible-server/overview"),
            new(
                "azure-cache-for-redis",
                "Azure Cache for Redis",
                ["Azure Cache for Redis", "Cache for Redis", "redis"],
                "Azure Cache for Redis is a managed in-memory data store whose Enterprise tiers retire on March 31, 2027 and remaining tiers on September 30, 2028.",
                "Azure Managed Redis is the successor service; expiry, invalidation, migration timing, and fallback behavior remain application design constraints.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/azure-cache-for-redis/cache-overview"),
            new(
                "azure-table-storage",
                "Azure Table Storage",
                ["Azure Table Storage", "table storage"],
                "Azure Table Storage is a schemaless NoSQL key-value store for large sets of structured, nonrelational data.",
                "Its partition and row-key design controls query efficiency because secondary-index behavior differs from relational databases.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/tables/table-storage-overview"),
            new(
                "azure-queue-storage",
                "Azure Queue Storage",
                ["Azure Queue Storage", "queue storage"],
                "Azure Queue Storage is a durable messaging service for simple asynchronous communication between application components.",
                "Queues decouple producers from consumers, but poison-message handling and visibility timeouts still affect reliability.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/queues/storage-queues-introduction"),
            new(
                "azure-synapse-analytics",
                "Azure Synapse Analytics",
                ["Azure Synapse Analytics", "Synapse Analytics", "synapse"],
                "Azure Synapse Analytics combines data integration, enterprise data warehousing, and analytics experiences in one service.",
                "It supports multiple analytics patterns, so architecture depends on whether the workload is SQL, Spark, pipelines, or mixed.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/synapse-analytics/overview-what-is"),
            new(
                "azure-data-lake-storage",
                "Azure Data Lake Storage",
                ["Azure Data Lake Storage", "data lake storage", "data lake"],
                "Azure Data Lake Storage adds hierarchical namespace capabilities to Azure Storage for big-data analytics workloads.",
                "Hierarchical paths simplify file-style analytics operations, especially for Spark and large-scale data processing patterns.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/blobs/data-lake-storage-introduction"),

            new(
                "azure-data-factory",
                "Azure Data Factory",
                ["Azure Data Factory", "Data Factory", "data factory"],
                "Azure Data Factory is a managed data-integration service for orchestrating movement and transformation across data sources.",
                "Pipelines coordinate data flows, but execution runtime, scheduling, and dependency design still affect reliability and cost.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/data-factory/introduction"),
            new(
                "azure-databricks",
                "Azure Databricks",
                ["Azure Databricks", "Databricks", "databricks"],
                "Azure Databricks is an analytics platform optimized for Apache Spark workloads in Azure.",
                "Cluster sizing, notebook governance, and data-location choices all influence performance and operating model.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/databricks/introduction/"),
            new(
                "azure-stream-analytics",
                "Azure Stream Analytics",
                ["Azure Stream Analytics", "Stream Analytics", "stream analytics"],
                "Azure Stream Analytics processes streaming events in near real time by applying query logic to incoming data.",
                "Continuous-event pipelines depend on explicit lateness handling and output destinations for their end-to-end behavior.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/stream-analytics/stream-analytics-introduction"),
            new(
                "azure-event-hubs",
                "Azure Event Hubs",
                ["Azure Event Hubs", "Event Hubs", "event hubs"],
                "Azure Event Hubs is a high-throughput event-ingestion service for telemetry, logs, and streaming pipelines.",
                "Partition count, retention, and consumer-group strategy influence throughput and downstream processing patterns.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/event-hubs/event-hubs-about"),
            new(
                "azure-service-bus",
                "Azure Service Bus",
                ["Azure Service Bus", "Service Bus", "service bus"],
                "Azure Service Bus is a reliable enterprise messaging service for queues, topics, and publish-subscribe workflows.",
                "Compared with simple queues, Service Bus adds richer delivery and session features for coordinated business messaging.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/service-bus-messaging/service-bus-messaging-overview"),
            new(
                "azure-api-management",
                "Azure API Management",
                ["Azure API Management", "API Management", "api management"],
                "Azure API Management publishes, secures, transforms, and monitors APIs through a managed gateway and policy engine.",
                "Gateway policies support traffic rewriting, throttling, and validation beyond simple API exposure.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/api-management/api-management-key-concepts"),
            new(
                "integration-workflow",
                "Integration workflow",
                ["integration workflow"],
                "An integration workflow coordinates steps between systems, usually combining events, data mapping, and external service calls.",
                "Connector support, retry behavior, and preserved process state determine workflow fit.",
                ConceptCategory.DataAiIntegration,
                false,
                "https://learn.microsoft.com/azure/logic-apps/logic-apps-overview"),
            new(
                "azure-ai-services",
                "Azure AI Services",
                ["Azure AI Services", "Cognitive Services", "cognitive services"],
                "Azure AI Services provides managed APIs for vision, speech, language, and related AI capabilities.",
                "These services add prebuilt AI functions, but data handling, latency, and model fit still need workload review.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/ai-services/what-are-ai-services"),
            new(
                "azure-machine-learning",
                "Azure Machine Learning",
                ["Azure Machine Learning", "Machine Learning", "azure machine learning"],
                "Azure Machine Learning is a platform for training, deploying, and governing machine-learning models and pipelines.",
                "Its value is strongest when teams need repeatable experimentation, model registry, and controlled deployment workflows.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/machine-learning/overview-what-is-azure-machine-learning"),
            new(
                "azure-openai-service",
                "Azure OpenAI Service",
                ["Azure OpenAI Service", "Azure OpenAI", "azure open ai", "openai service"],
                "Azure OpenAI Service provides access to large language and multimodal models through Azure-managed identity, networking, and governance controls.",
                "Model quality alone is not enough; retrieval, prompt grounding, and safety boundaries still shape production behavior.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/ai-services/openai/overview"),
            new(
                "azure-ai-search",
                "Azure AI Search",
                ["Azure AI Search", "AI Search", "azure ai search"],
                "Azure AI Search indexes and queries content for application search, retrieval, and enrichment scenarios.",
                "Index design, chunking, and filterable fields matter because retrieval quality depends on how content is shaped.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/search/search-what-is-azure-search"),
            new(
                "azure-data-explorer",
                "Azure Data Explorer",
                ["Azure Data Explorer", "Data Explorer", "kusto"],
                "Azure Data Explorer is an analytics service optimized for high-volume log, telemetry, and time-series exploration.",
                "It excels at interactive investigation, but table design and ingestion strategy strongly influence query performance.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/data-explorer/data-explorer-overview"),

            new(
                "azure-monitor",
                "Azure Monitor",
                ["Azure Monitor", "monitor", "azure monitor"],
                "Azure Monitor is the core Azure observability service for collecting metrics, logs, traces, and alerts.",
                "Monitoring value comes from the signals and queries you keep, not only from turning collection on.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/overview"),
            new(
                "log-analytics",
                "Log Analytics",
                ["Log Analytics", "log analytics"],
                "Log Analytics stores and queries operational log data with the Kusto query language.",
                "Useful dashboards and detections depend on workspace design, retention settings, and the queries teams actually run.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/logs/log-analytics-overview"),
            new(
                "application-insights",
                "Application Insights",
                ["Application Insights", "app insights", "application insights"],
                "Application Insights provides application performance telemetry such as requests, dependencies, failures, and traces.",
                "Instrumentation helps find runtime issues, but sampling and correlation choices affect what evidence is available later.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/app/app-insights-overview"),
            new(
                "azure-alerts",
                "Azure Alerts",
                ["Azure Alerts", "alerts", "azure alerts"],
                "Azure Alerts trigger notifications or actions when monitored metrics, logs, or activity conditions match a rule.",
                "An alert is only as useful as its threshold, action group, and signal quality; noisy rules quickly lose value.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/alerts/alerts-overview"),
            new(
                "azure-dashboard",
                "Azure Dashboard",
                ["Azure Dashboard", "azure dashboard"],
                "An Azure Dashboard is a customizable view that combines visualizations from Azure resources and monitoring data.",
                "Dashboards summarize state, but they work best when paired with underlying queries and actionable drill-down paths.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-portal/azure-portal-dashboards"),
            new(
                "service-level-agreement",
                "Service Level Agreement (SLA)",
                ["SLA", "service level agreement", "sla"],
                "An SLA is the provider's stated availability commitment for a service under defined conditions.",
                "An SLA is not the same as end-to-end application availability because workload architecture still determines total resilience.",
                ConceptCategory.MonitoringReliability,
                false,
                "https://learn.microsoft.com/azure/virtual-machines/availability"),
            new(
                "azure-backup",
                "Azure Backup",
                ["Azure Backup", "azure backup"],
                "Azure Backup is a managed service for protecting and recovering supported Azure and hybrid workloads.",
                "Backup protects recoverable data, but restore testing and retention design matter as much as taking the backup itself.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/backup/backup-overview"),
            new(
                "azure-site-recovery",
                "Azure Site Recovery",
                ["Azure Site Recovery", "Site Recovery", "site recovery"],
                "Azure Site Recovery replicates workloads so they can fail over to another location during a disruption.",
                "Replication helps meet recovery targets, but networking, sequencing, and failback procedures still need planning.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/site-recovery/site-recovery-overview"),
            new(
                "recovery-time-objective",
                "Recovery Time Objective (RTO)",
                ["RTO", "recovery time objective", "rto"],
                "RTO is the target maximum time allowed to restore a service after an outage begins.",
                "A shorter RTO usually demands more automation, standby capacity, and tested failover steps.",
                ConceptCategory.MonitoringReliability,
                false,
                "https://learn.microsoft.com/azure/site-recovery/site-recovery-faq"),
            new(
                "recovery-point-objective",
                "Recovery Point Objective (RPO)",
                ["RPO", "recovery point objective", "rpo"],
                "RPO is the maximum acceptable amount of data loss measured as time between the latest recoverable point and the disruption.",
                "RPO affects replication frequency and backup cadence because tighter data-loss targets usually increase protection cost and complexity.",
                ConceptCategory.MonitoringReliability,
                false,
                "https://learn.microsoft.com/azure/site-recovery/site-recovery-faq"),

            new(
                "azure-migrate",
                "Azure Migrate",
                ["Azure Migrate", "migrate", "azure migrate"],
                "Azure Migrate is a service family for discovering, assessing, and tracking migration of servers, databases, and applications.",
                "Assessment data helps estimate readiness and cost, but migration waves still depend on dependency, downtime, and ownership planning.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/migrate/migrate-services-overview"),
            new(
                "azure-devops",
                "Azure DevOps",
                ["Azure DevOps", "azure devops"],
                "Azure DevOps provides services for source control, work tracking, pipelines, package feeds, and test management.",
                "The platform supports delivery workflows, but branching, approvals, and environment strategy still come from team process design.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/devops/user-guide/what-is-azure-devops"),
            new(
                "github-actions-azure",
                "GitHub Actions on Azure",
                ["GitHub Actions", "github actions on azure", "github actions"],
                "GitHub Actions automates build and deployment workflows, including Azure-targeted pipelines through reusable actions.",
                "Deployment automation is most reliable when identity, secrets, and rollback logic are designed before broad rollout.",
                ConceptCategory.MigrationDevOpsFinOps,
                false,
                "https://learn.microsoft.com/azure/developer/github/github-actions"),
            new(
                "reserved-instances",
                "Reserved Instances",
                ["Reserved Instances", "reserved instances", "reservations"],
                "Reserved Instances exchange a longer usage commitment for discounted pricing on eligible Azure compute capacity.",
                "Savings depend on stable demand; if usage moves or shrinks, the financial benefit can drop.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/cost-management-billing/reservations/save-compute-costs-reservations"),
            new(
                "spot-virtual-machines",
                "Spot Virtual Machines",
                ["Spot VMs", "spot virtual machines", "spot vm", "spot vms"],
                "Spot Virtual Machines use discounted Azure capacity that can be evicted when the platform needs it back.",
                "Spot works best for interruptible jobs because eviction risk makes it unsuitable for every production workload.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/virtual-machines/spot-vms"),
            new(
                "azure-lighthouse",
                "Azure Lighthouse",
                ["Azure Lighthouse", "lighthouse", "azure lighthouse"],
                "Azure Lighthouse lets one tenant manage delegated resources in another tenant through controlled cross-tenant access.",
                "It is useful for managed-service operations, but delegation scope and role choice still define the actual access boundary.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/lighthouse/overview"),
            new(
                "cloud-adoption-framework",
                "Cloud Adoption Framework",
                ["Cloud Adoption Framework", "caf", "cloud adoption framework"],
                "The Cloud Adoption Framework is Microsoft guidance for planning and operating cloud adoption across strategy, governance, and technology.",
                "Its phased structure links technical choices to organizational readiness during transformation.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/"),
            new(
                "well-architected-framework",
                "Well-Architected Framework",
                ["Well-Architected Framework", "well architected framework", "well architected"],
                "The Well-Architected Framework is a set of design principles for reliability, security, cost, operations, and performance.",
                "It is a review lens, not a product, so teams use it to evaluate tradeoffs in an existing or planned architecture.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/well-architected/"),
            new(
                "azure-landing-zone",
                "Azure landing zone",
                ["Azure landing zone", "landing zone", "landing zones"],
                "An Azure landing zone is the target platform foundation for subscriptions, identity, networking, governance, and operations.",
                "A landing zone is not one resource; it is a prepared operating environment that new workloads can inherit from.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/ready/landing-zone/"),
            new(
                "finops",
                "FinOps",
                ["FinOps", "finops", "Azure FinOps", "FinOps practice"],
                "FinOps is an operating discipline that helps engineering, finance, and product teams make informed cloud cost decisions together.",
                "FinOps depends on timely usage data, ownership metadata, and recurring review habits rather than one dashboard alone.",
                ConceptCategory.MigrationDevOpsFinOps,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/scenarios/cloud-scale-analytics/best-practices/finops"),
            new(
                "rehost",
                "Rehost migration",
                ["rehost migration", "cloud rehosting", "lift and shift"],
                "Rehosting moves an application to cloud infrastructure with minimal code change compared with deeper modernization approaches.",
                "Rehosting can accelerate initial movement, but it may preserve legacy operating costs or technical constraints.",
                ConceptCategory.MigrationDevOpsFinOps,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/migrate/"),
            new(
                "refactor",
                "Refactor migration",
                ["refactor migration", "refactoring for migration", "cloud refactoring", "cloud rearchitecture"],
                "Refactoring changes parts of an application so it can use more managed cloud capabilities.",
                "Refactoring can reduce long-term operations, but it usually needs more code change and testing than a pure rehost.",
                ConceptCategory.MigrationDevOpsFinOps,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/migrate/"),
            new(
                "total-cost-of-ownership",
                "Total Cost of Ownership (TCO)",
                ["TCO", "total cost of ownership", "tco"],
                "TCO is the combined cost of running a solution, including infrastructure, software, labor, and supporting operations.",
                "A useful TCO comparison includes operational effort and committed-spend assumptions, not just list price for compute.",
                ConceptCategory.MigrationDevOpsFinOps,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/strategy/business-outcomes/tco"),
            new(
                "return-on-investment",
                "Return on Investment (ROI)",
                ["ROI", "return on investment", "roi"],
                "ROI compares expected benefits with the cost of an investment to show whether the change is financially worthwhile.",
                "ROI depends on assumptions like timeline, adoption, and avoided cost, so it should be tied to explicit business outcomes.",
                ConceptCategory.MigrationDevOpsFinOps,
                false,
                "https://learn.microsoft.com/azure/cloud-adoption-framework/strategy/business-outcomes/"),
        };
    }

    private static readonly IReadOnlyDictionary<string, EducationalConcept> ByConceptKey =
        All.ToDictionary(
            concept => concept.ConceptKey,
            concept => concept,
            StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, EducationalConcept> ByNormalizedAlias =
        All.SelectMany(concept =>
                concept.Aliases
                    .Append(concept.CanonicalTitle)
                    .Select(alias => new KeyValuePair<string, EducationalConcept>(
                        HeuristicConversationCoachAgent.Normalize(alias),
                        concept)))
            .GroupBy(pair => pair.Key, StringComparer.Ordinal)
            .ToDictionary(
                group => group.Key,
                group => group.First().Value,
                StringComparer.Ordinal);

    public static bool TryGetByConceptKey(
        string conceptKey,
        out EducationalConcept concept) =>
        ByConceptKey.TryGetValue(conceptKey, out concept!);

    public static bool TryResolveByAliasOrTitle(
        string value,
        out EducationalConcept concept)
    {
        var normalized = HeuristicConversationCoachAgent.Normalize(value);
        if (normalized.Length > 0
            && ByNormalizedAlias.TryGetValue(normalized, out concept!))
        {
            return true;
        }

        concept = null!;
        return false;
    }

    public static IReadOnlyList<string> BuildSpeechPhraseVocabulary()
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var phrases = new List<string>(capacity: 500);

        // Azure always first
        phrases.Add("Azure");
        seen.Add("Azure");

        // Phase 1: one primary safe phrase per concept in catalog order
        // (canonical title preferred; fall back to aliases in declaration order)
        foreach (var concept in All)
        {
            if (phrases.Count >= 500)
                break;

            if (!TryAddPhrase(concept.CanonicalTitle, seen, phrases))
            {
                foreach (var alias in concept.Aliases)
                {
                    if (TryAddPhrase(alias, seen, phrases))
                        break;
                }
            }
        }

        // Phase 2: remaining aliases for every concept, in catalog order
        var capacityReached = false;
        foreach (var concept in All)
        {
            if (capacityReached)
                break;

            foreach (var alias in concept.Aliases)
            {
                if (phrases.Count >= 500)
                {
                    capacityReached = true;
                    break;
                }

                TryAddPhrase(alias, seen, phrases);
            }
        }

        // Phase 3: deterministic supplemental phrases to fill remaining slots to 500
        foreach (var phrase in SupplementalSpeechPhrases)
        {
            if (phrases.Count >= 500)
                break;

            TryAddPhrase(phrase, seen, phrases);
        }

        return phrases.AsReadOnly();
    }

    // Curated supplemental phrases: qualified official Azure product/service variants
    // added in deterministic order to fill vocabulary to 500 when catalog alone falls short.
    private static readonly IReadOnlyList<string> SupplementalSpeechPhrases = new string[]
    {
        "Azure IoT Operations",
        "Azure Stack Edge GPU",
        "Azure AI Foundry project",
        "Microsoft Fabric OneLake",
        "Azure Managed Prometheus",
        "Azure Traffic Manager",
        "Azure DNS Private Resolver",
        "Azure Savings Plan",
        "Microsoft Entra Verified ID",
        "Azure Compute Gallery",
        "Azure Container Registry task",
        "Azure API Center governance",
        "Microsoft Defender for Servers",
        "Azure Monitor Agent",
        "AMA",
        "Azure Monitor Workbooks",
        "Azure Monitor Logs",
        "Azure Monitor Metrics",
        "Azure Monitor Workspace",
        "Log Analytics workspace",
        "Azure Managed Prometheus",
        "Microsoft Defender for Servers",
        "Microsoft Defender for Containers",
        "Microsoft Defender for Databases",
        "Microsoft Defender CSPM",
        "Microsoft Secure Score",
        "Microsoft Defender for Endpoint",
        "Microsoft Defender for Identity",
        "Microsoft Defender for Cloud Apps",
        "Microsoft Defender for Office 365",
        "Azure Key Vault secrets",
        "Azure Key Vault certificates",
        "Azure Disk Encryption",
        "Azure Storage encryption",
        "Azure DevOps Pipelines",
        "Azure DevOps Boards",
        "Azure DevOps Repos",
        "Azure DevOps Artifacts",
        "GitHub Advanced Security",
        "AKS node pool",
        "AKS cluster autoscaler",
        "Container Apps environment",
        "Container Apps job",
        "Azure Functions Premium plan",
        "Azure Functions Flex Consumption",
        "App Service Environment",
        "App Service Plan",
        "Azure OpenAI model deployment",
        "Azure AI model catalog",
        "Azure Machine Learning pipeline",
        "Azure Machine Learning registry",
        "Azure Databricks Unity Catalog",
        "Azure Databricks workspace",
        "Azure Synapse Link",
        "Azure Synapse Spark pool",
        "Azure Data Factory pipeline",
        "Azure Data Factory integration runtime",
        "Azure Event Hubs Kafka",
        "Azure Event Hubs capture",
        "Azure Service Bus topic",
        "Azure Service Bus session",
        "Azure API Management policy",
        "Azure API Management developer portal",
        "Azure Stream Analytics window",
        "Azure Data Lake Storage Gen2",
        "Azure Blob lifecycle management",
        "Azure Storage Account",
        "Azure Storage firewall",
        "Azure File Sync",
        "Azure Backup vault",
        "Azure Recovery Services vault",
        "Azure SQL Hyperscale",
        "Azure SQL serverless",
        "Azure SQL Business Critical",
        "Azure Database Migration Service",
        "Azure Cosmos DB serverless",
        "Azure Cosmos DB for MongoDB",
        "Azure Policy Initiative",
        "Azure Policy compliance",
        "Azure Policy assignment",
        "Azure Resource Graph",
        "Azure Hybrid Benefit",
        "Azure Savings Plan",
        "Azure Spot Instance",
        "Azure Pricing Calculator",
        "ExpressRoute circuit",
        "ExpressRoute Global Reach",
        "Azure Traffic Manager",
        "Azure DNS Private Resolver",
        "Azure Front Door Standard",
        "Azure Front Door Premium",
        "Azure Firewall Premium",
        "Azure Firewall Policy",
        "Azure DDoS rapid response",
        "Azure Security Benchmark",
        "Microsoft Cloud Security Benchmark",
        "Azure Subscription vending",
        "Azure Landing Zone accelerator",
        "Azure Enterprise Scale",
        "Microsoft Entra B2B collaboration",
        "Microsoft Entra SSPR",
        "Microsoft Entra passwordless",
        "Microsoft Entra Verified ID",
        "Microsoft Entra Connect",
        "Azure Compute Gallery",
        "Azure Image Builder",
        "Azure Marketplace image",
        "Azure Virtual Machine extension",
        "Azure Batch pool",
        "Azure Logic Apps Standard",
        "Durable Functions orchestration",
        "Azure Container Registry task",
        "Azure Kubernetes Fleet cluster",
        "Azure Red Hat OpenShift cluster",
        "Windows 365 Cloud PC",
        "Azure Local cluster",
        "Azure Stack Edge GPU",
        "Azure IoT Operations",
        "Azure IoT Hub device twin",
        "Azure Digital Twins model",
        "Azure Event Grid topic",
        "Azure SignalR Service",
        "Azure Web PubSub",
        "Azure Notification Hubs",
        "Azure Communication Services voice",
        "Azure API Center governance",
        "Azure App Configuration feature flag",
        "Microsoft Fabric lakehouse",
        "Microsoft Fabric OneLake",
        "Microsoft Fabric capacity",
        "Azure AI Foundry project",
        "Azure OpenAI fine-tuning",
        "Azure Machine Learning compute cluster",
        "RAG grounding",
        "DCR pipeline",
        "SLO target",
        "Azure Monitor action group",
        "Azure Chaos Studio experiment",
        "Azure Update Manager patch",
        "Azure Managed Grafana dashboard",
    };

    private static bool TryAddPhrase(string phrase, HashSet<string> seen, List<string> phrases)
    {
        if (phrases.Count >= 500 || string.IsNullOrWhiteSpace(phrase))
            return false;

        var hasSpace = phrase.Contains(' ');
        var isSafeSingleToken = SafeAcronyms.Contains(phrase)
            || (!UnsafeSingleTokens.Contains(phrase) && phrase.Length >= 5);
        if (!hasSpace && !isSafeSingleToken)
            return false;

        if (!seen.Add(phrase))
            return false;

        phrases.Add(phrase);
        return true;
    }
}
