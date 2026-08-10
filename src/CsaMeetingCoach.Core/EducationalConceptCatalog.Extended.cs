namespace CsaMeetingCoach.Core;

// Extended catalog entries added in August 2026.
// Each builder method returns a plain collection with no dependencies on other partial-class fields,
// avoiding static initialization-order hazards across compilation units.
public static partial class EducationalConceptCatalog
{
    private static IEnumerable<EducationalConcept> ExtendedConcepts()
    {
        return new EducationalConcept[]
        {
            // ── StorageDatabases ─────────────────────────────────────────────────

            new(
                "azure-sql-managed-instance",
                "Azure SQL Managed Instance",
                ["SQL Managed Instance", "Azure SQL Managed Instance", "azure sql managed instance"],
                "Azure SQL Managed Instance is a fully managed PaaS database engine that provides near 100% SQL Server Enterprise Edition feature compatibility with automated patching, backups, and built-in availability.",
                "Unlike Azure SQL Database, Managed Instance supports SQL Server Agent, cross-database queries, linked servers, and CLR, preserving compatibility for complex SQL Server estates.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/azure-sql/managed-instance/sql-managed-instance-paas-overview?view=azuresql"),

            new(
                "azure-database-mysql",
                "Azure Database for MySQL",
                ["Azure Database for MySQL", "MySQL flexible server", "azure mysql"],
                "Azure Database for MySQL is a fully managed relational database service based on MySQL Community Edition, with zone-redundant HA, automated backups, point-in-time restore up to 35 days, and automated patching.",
                "The flexible server model provides granular control over maintenance windows, configuration, and network isolation; single-server deployment is retired, so flexible server is the current migration target.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/mysql/flexible-server/overview"),

            new(
                "azure-storage-redundancy",
                "Azure Storage redundancy",
                ["Azure Storage redundancy", "storage redundancy options", "locally redundant storage", "zone-redundant storage", "geo-redundant storage"],
                "Azure Storage redundancy replicates data across copies using LRS, ZRS, GRS, or GZRS options to protect against hardware failures, datacenter outages, and regional disasters at different cost points.",
                "LRS replicates within one datacenter; ZRS spans availability zones; GRS adds an async cross-region copy; GZRS combines both. Read access to the secondary requires RA-GRS or RA-GZRS enabled explicitly.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/common/storage-redundancy"),

            new(
                "blob-access-tiers",
                "Azure Blob Storage access tiers",
                ["blob access tiers", "Azure Blob Storage access tiers", "hot tier", "cool tier", "cold tier", "archive tier"],
                "Azure Blob Storage offers hot, cool, cold, and archive access tiers that balance storage cost against retrieval latency, enabling lifecycle-driven cost optimization for data at rest.",
                "Archive tier has the lowest storage cost but requires hours of rehydration before data can be read; lifecycle transitions also carry early-deletion periods and retrieval charges.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/blobs/access-tiers-overview"),

            new(
                "azure-elastic-san",
                "Azure Elastic SAN",
                ["Azure Elastic SAN", "Elastic SAN", "azure elastic san"],
                "Azure Elastic SAN is a fully managed cloud-native storage area network that consolidates block storage for multiple compute workloads, simplifying SAN deployment, scaling, and lifecycle management.",
                "Elastic SAN provides iSCSI-attached volumes that VMs, VMware Solution, and AKS can share through consolidated capacity and throughput pools.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/storage/elastic-san/elastic-san-introduction"),

            new(
                "azure-netapp-files",
                "Azure NetApp Files",
                ["Azure NetApp Files", "NetApp Files", "azure netapp files"],
                "Azure NetApp Files is an Azure-native, enterprise-class high-performance file storage service that delivers NFS, SMB, and dual-protocol access with configurable service and performance levels.",
                "Designed for latency-sensitive workloads such as SAP HANA, HPC, and VDI that require sub-millisecond performance; capacity pools and service levels (Standard, Premium, Ultra) control throughput and cost.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/azure-netapp-files/azure-netapp-files-introduction"),

            new(
                "cosmos-db-consistency-levels",
                "Azure Cosmos DB consistency levels",
                ["cosmos consistency levels", "Azure Cosmos DB consistency levels", "Cosmos DB consistency levels", "cosmos db consistency"],
                "Azure Cosmos DB offers five consistency levels—strong, bounded staleness, session, consistent prefix, and eventual—letting workloads balance read consistency, availability, latency, and throughput.",
                "Session consistency is the default for most use cases. Strong consistency provides linearizability at the cost of higher write latency without support for multi-region write configurations.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/cosmos-db/consistency-levels"),

            new(
                "azure-sql-elastic-pool",
                "Azure SQL elastic pool",
                ["SQL elastic pool", "Azure SQL elastic pool", "elastic pool"],
                "An Azure SQL elastic pool is a shared resource model where multiple Azure SQL databases consume capacity from a common pool of vCores or DTUs, reducing cost for databases with variable or unpredictable usage.",
                "Elastic pools lower total compute cost when databases have non-overlapping peak demand; a single database with consistently high load can consume the entire pool, impacting co-hosted databases.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/azure-sql/database/elastic-pool-overview?view=azuresql"),

            new(
                "azure-managed-redis",
                "Azure Managed Redis",
                ["Azure Managed Redis", "Managed Redis", "azure managed redis"],
                "Azure Managed Redis is a fully managed enterprise Redis cache service built on Redis Enterprise that offers higher throughput tiers, active geo-replication, and advanced data structure support.",
                "Azure Managed Redis is the successor to Azure Cache for Redis, whose Enterprise tiers retire on March 31, 2027 and remaining tiers on September 30, 2028.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/redis/overview"),

            new(
                "azure-sql-failover-group",
                "Azure SQL failover group",
                ["SQL failover group", "Azure SQL failover group", "auto-failover group"],
                "An Azure SQL failover group replicates one or more databases to a secondary server in another region with automatic or manual failover and a listener endpoint that apps use without connection-string changes.",
                "Failover groups simplify regional disaster recovery for Azure SQL, but the replication lag, RTO, and RPO still depend on synchronization mode, workload size, and regional network conditions.",
                ConceptCategory.StorageDatabases,
                true,
                "https://learn.microsoft.com/azure/azure-sql/database/failover-group-sql-db?view=azuresql"),

            // ── DataAiIntegration ────────────────────────────────────────────────

            new(
                "microsoft-fabric",
                "Microsoft Fabric",
                ["Microsoft Fabric", "Fabric platform", "Fabric analytics"],
                "Microsoft Fabric is a unified SaaS analytics platform covering data ingestion, transformation, real-time intelligence, data warehousing, data science, and Power BI reporting over a shared OneLake storage model.",
                "All Fabric workloads—Lakehouse, Warehouse, Real-Time Intelligence, Power BI—share OneLake, eliminating data copies across workloads; capacity sizing and licensing are unified at the tenant level.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/fabric/fundamentals/microsoft-fabric-overview"),

            new(
                "microsoft-foundry",
                "Microsoft Foundry",
                ["Microsoft Foundry", "Azure AI Foundry", "azure ai foundry", "AI Foundry"],
                "Microsoft Foundry is a unified platform for building, deploying, and governing AI applications and agents, providing model access, evaluation, tracing, monitoring, and enterprise-grade safety configuration.",
                "Foundry consolidates model selection, prompt flow, evaluation, fine-tuning, and deployment; responsible AI policies and content filtering are configured per project, not per individual model endpoint.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/foundry/what-is-foundry"),

            new(
                "retrieval-augmented-generation",
                "Retrieval-Augmented Generation (RAG)",
                ["retrieval augmented generation", "retrieval-augmented generation", "RAG pattern"],
                "Retrieval-Augmented Generation (RAG) improves LLM response accuracy by retrieving relevant documents from an indexed knowledge store and providing them as grounding context inside the model prompt.",
                "RAG reduces hallucination risk, but quality depends on chunk size, index design, retrieval relevance scoring, and fitting retrieved context within the model's token context window limit.",
                ConceptCategory.DataAiIntegration,
                false,
                "https://learn.microsoft.com/azure/foundry/concepts/retrieval-augmented-generation"),

            new(
                "azure-ai-content-safety",
                "Azure AI Content Safety",
                ["Azure AI Content Safety", "azure content safety", "content safety service"],
                "Azure AI Content Safety detects harmful, hateful, sexual, and violent material in text and images through APIs, giving applications configurable content moderation capabilities.",
                "The service returns severity scores per harm category rather than binary outputs; custom blocklists and groundedness detection extend the default model coverage for domain-specific content.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/ai-services/content-safety/overview"),

            new(
                "azure-document-intelligence",
                "Azure Document Intelligence",
                ["Azure Document Intelligence", "Document Intelligence", "Form Recognizer", "form recognizer"],
                "Azure Document Intelligence is an AI service that uses machine learning to extract structured data, key-value pairs, tables, and named entities from forms, invoices, receipts, and other documents.",
                "Prebuilt models handle common document types without training data; custom models require labeled samples and perform best when document layout is consistent across instances in the target corpus.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/ai-services/document-intelligence/overview?view=doc-intel-4.0.0"),

            new(
                "azure-event-grid",
                "Azure Event Grid",
                ["Azure Event Grid", "Event Grid", "azure event grid"],
                "Azure Event Grid is a fully managed event-routing service that delivers events from Azure and custom sources to subscribers over HTTP or MQTT with at-least-once delivery.",
                "Unlike message queues, Event Grid is optimized for reactive notification patterns with short-lived event retention rather than the partition-based replay model of Event Hubs.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/event-grid/overview"),

            new(
                "azure-app-configuration",
                "Azure App Configuration",
                ["Azure App Configuration", "App Configuration", "azure app configuration"],
                "Azure App Configuration is a managed service for centralizing application settings and feature flags across environments, separate from application code and Key Vault secrets.",
                "It supports dynamic configuration refresh and feature-flag targeting; secret values remain in Key Vault while App Configuration stores references to them.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/azure-app-configuration/overview"),

            new(
                "azure-iot-hub",
                "Azure IoT Hub",
                ["Azure IoT Hub", "IoT Hub", "azure iot hub"],
                "Azure IoT Hub is a managed cloud service for bidirectional, secure communication between millions of IoT devices and a back end, with device management and message routing.",
                "Hub routing rules, device twins, and direct-method design determine how back-end logic interacts with devices; downstream targets still need routing endpoints configured.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/iot-hub/iot-concepts-and-iot-hub"),

            new(
                "azure-iot-edge",
                "Azure IoT Edge",
                ["Azure IoT Edge", "IoT Edge", "azure iot edge"],
                "Azure IoT Edge deploys cloud analytics and AI workloads as containerized modules to run locally on edge devices, enabling offline intelligence and reduced upstream bandwidth.",
                "Edge modules can operate during connectivity loss, but module deployment, versioning, and secrets management still require IoT Hub integration and a container registry.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/iot-edge/about-iot-edge"),

            new(
                "azure-digital-twins",
                "Azure Digital Twins",
                ["Azure Digital Twins", "digital twins", "azure digital twins"],
                "Azure Digital Twins is a platform for creating live, graph-based models of physical environments that reflect real-world sensor data for simulation and spatial analysis.",
                "Model update latency and fidelity depend on how sensor telemetry flows through IoT Hub or Event Grid into the twin graph; the graph does not self-update autonomously.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/digital-twins/overview"),

            new(
                "azure-durable-functions",
                "Durable Functions",
                ["Durable Functions", "durable functions", "Azure Durable Functions"],
                "Durable Functions is an extension of Azure Functions that enables stateful workflows and long-running orchestrations using an event-sourcing pattern without managing state storage directly.",
                "Orchestrator, activity, entity, and client function types compose durable workflows, but replay behavior, external event handling, and fan-out/fan-in patterns require understanding the execution model.",
                ConceptCategory.DataAiIntegration,
                true,
                "https://learn.microsoft.com/azure/azure-functions/durable/durable-functions-overview"),

            // ── ComputeContainersAppPlatforms ────────────────────────────────────

            new(
                "azure-container-registry",
                "Azure Container Registry (ACR)",
                ["Azure Container Registry", "ACR", "acr", "container registry"],
                "Azure Container Registry is a managed private registry for storing and distributing container images and OCI artifacts used across Azure and hybrid deployments.",
                "ACR supports geo-replication and automated image-build tasks, but pull access still depends on RBAC assignments and whether public network access is restricted.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/container-registry/container-registry-intro"),

            new(
                "azure-vmware-solution",
                "Azure VMware Solution (AVS)",
                ["Azure VMware Solution", "AVS", "avs"],
                "Azure VMware Solution runs VMware workloads natively in Azure on dedicated bare-metal hosts, preserving familiar vSphere, vSAN, and NSX tooling without application refactoring.",
                "AVS requires a minimum dedicated node count per cluster, so it suits migrations needing VMware operational continuity rather than cloud-native redesign.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/azure-vmware/introduction"),

            new(
                "azure-dedicated-host",
                "Azure Dedicated Host",
                ["Azure Dedicated Host", "dedicated host", "dedicated hosts"],
                "Azure Dedicated Host provides physical servers dedicated to a single customer for hosting Azure VMs with hardware isolation and control over planned maintenance event timing.",
                "It helps meet compliance requirements that prohibit shared hardware, but it costs more than multi-tenant capacity and requires planning per host family and availability zone.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/virtual-machines/dedicated-hosts"),

            new(
                "azure-static-web-apps",
                "Azure Static Web Apps",
                ["Azure Static Web Apps", "Static Web Apps", "azure static web apps"],
                "Azure Static Web Apps builds and hosts static front-end applications globally, with integrated CI/CD from GitHub or Azure DevOps and a linked API tier via Azure Functions.",
                "It suits JAMstack and SPA patterns well, but the integrated API tier uses Azure Functions, so complex back-end requirements may still need a separate hosting approach.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/static-web-apps/overview"),

            new(
                "keda",
                "KEDA (Kubernetes Event-Driven Autoscaling)",
                ["KEDA", "kubernetes event-driven autoscaling", "event-driven autoscaling"],
                "KEDA is an open-source Kubernetes autoscaler that scales workloads, including to zero, based on external event-source depth such as queues, topics, or custom metrics.",
                "KEDA scales on queue depth or custom signals rather than CPU or memory alone, but each scaler has connector-specific prerequisites and a configurable polling interval.",
                ConceptCategory.ComputeContainersAppPlatforms,
                false,
                "https://learn.microsoft.com/azure/aks/keda-about"),

            new(
                "dapr",
                "Dapr (Distributed Application Runtime)",
                ["Dapr", "distributed application runtime", "dapr sidecar"],
                "Dapr is an open-source, portable runtime providing standardized APIs for state management, pub/sub messaging, service invocation, and bindings as sidecar processes alongside microservices.",
                "Dapr sidecars abstract infrastructure provider choices, but component configuration and observability still depend on the platform and backing services selected.",
                ConceptCategory.ComputeContainersAppPlatforms,
                false,
                "https://learn.microsoft.com/azure/container-apps/dapr-overview"),

            new(
                "azure-confidential-computing",
                "Azure Confidential Computing",
                ["Azure Confidential Computing", "confidential computing", "confidential VMs"],
                "Azure Confidential Computing protects data in use inside hardware-based Trusted Execution Environments, encrypting workload memory from the cloud operator and other tenants.",
                "Confidential VMs and enclaves reduce trust assumptions for regulated workloads, but they require compatible hardware SKUs and may introduce application architecture constraints.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/confidential-computing/overview"),

            new(
                "azure-kubernetes-fleet-manager",
                "Azure Kubernetes Fleet Manager",
                ["Azure Kubernetes Fleet Manager", "Kubernetes Fleet Manager", "azure kubernetes fleet"],
                "Azure Kubernetes Fleet Manager enables unified management of multiple AKS clusters with workload orchestration, update sequencing, and fleet-wide Kubernetes resource propagation.",
                "Fleet simplifies treating many AKS clusters as a single unit, but individual cluster networking, identity, and policy boundaries still apply independently to each member cluster.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/kubernetes-fleet/overview"),

            new(
                "azure-virtual-desktop",
                "Azure Virtual Desktop (AVD)",
                ["Azure Virtual Desktop", "AVD", "avd", "windows virtual desktop"],
                "Azure Virtual Desktop is a cloud-based desktop and app virtualization service delivering Windows experiences from Azure, including multi-session Windows 11 and Microsoft 365 optimization.",
                "Multi-session host pools reduce per-user infrastructure cost, but session host sizing, FSLogix profile storage, and network path design still directly shape end-user experience quality.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/virtual-desktop/overview"),

            new(
                "windows-365",
                "Windows 365",
                ["Windows 365", "Cloud PC", "windows 365"],
                "Windows 365 provides individually assigned, persistent Cloud PCs as a managed SaaS service with a fixed per-user license, managed through Microsoft Intune and Entra ID.",
                "Unlike AVD shared host pools, Windows 365 assigns a dedicated persistent Cloud PC to each user. This model simplifies license cost prediction without multi-session density optimization.",
                ConceptCategory.ComputeContainersAppPlatforms,
                false,
                "https://learn.microsoft.com/windows-365/enterprise/overview"),

            new(
                "azure-local",
                "Azure Local",
                ["Azure Local", "Azure Stack HCI", "azure local", "azure stack hci"],
                "Azure Local is Microsoft's hyperconverged infrastructure platform for running Azure Arc-managed workloads and services on-premises on validated hardware clusters.",
                "It extends Azure services to on-premises locations, but it requires validated hardware investment, Azure Arc connectivity, and monthly cloud-based subscription billing.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/azure-local/overview"),

            new(
                "azure-stack-edge",
                "Azure Stack Edge",
                ["Azure Stack Edge", "Stack Edge", "azure stack edge"],
                "Azure Stack Edge is a hardware-as-a-service edge computing device that brings Azure compute, storage, and AI inference capabilities to on-premises or disconnected locations.",
                "Stack Edge supports local AI inferencing and data transfer scenarios where cloud connectivity is limited or latency and data sovereignty require local processing before upload.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/databox-online/azure-stack-edge-gpu-overview"),

            new(
                "azure-autoscale",
                "Azure Autoscale",
                ["Azure Autoscale", "azure autoscale", "autoscale rules", "automatic scaling"],
                "Azure Autoscale adjusts the number of compute instances or the capacity of a resource based on demand, using metric-based or schedule-based rules to maintain performance and control cost.",
                "Autoscale reacts to metrics within configured bounds; scale-in cooldown periods and instance warm-up time affect how quickly capacity changes track actual workload fluctuations.",
                ConceptCategory.ComputeContainersAppPlatforms,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/autoscale/autoscale-overview"),

            // ── Networking ───────────────────────────────────────────────────────

            new(
                "azure-virtual-wan",
                "Azure Virtual WAN",
                ["Azure Virtual WAN", "Virtual WAN", "azure virtual wan"],
                "Azure Virtual WAN is a managed networking service that connects branches, remote users, and cloud resources through a global Azure backbone hub-and-spoke topology.",
                "A Virtual WAN hub can consolidate site-to-site VPN, ExpressRoute, and user VPN connectivity, but each branch still requires compatible device configuration and circuit provisioning.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/virtual-wan/virtual-wan-about"),

            new(
                "azure-bastion",
                "Azure Bastion",
                ["Azure Bastion", "azure bastion", "Bastion host"],
                "Azure Bastion provides managed, browser-based RDP and SSH access to Azure VMs through the portal without requiring public IPs or open inbound management ports on VMs.",
                "It eliminates the jump-server pattern and exposed management ports, but the selected SKU tier determines concurrent session limits, native client support, and per-IP targeting.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/bastion/bastion-overview"),

            new(
                "azure-ddos-protection",
                "Azure DDoS Protection",
                ["Azure DDoS Protection", "DDoS Protection", "DDoS Network Protection", "DDoS IP Protection"],
                "Azure DDoS Protection detects and mitigates volumetric, protocol, and application-layer DDoS attacks against VNet-attached resources using adaptive real-time tuning.",
                "The Network Protection tier covers all resources in a protected VNet and includes cost credits and SLA guarantees that the free infrastructure-level protection does not provide.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/ddos-protection/ddos-protection-overview"),

            new(
                "azure-nat-gateway",
                "Azure NAT Gateway",
                ["Azure NAT Gateway", "NAT Gateway", "azure nat gateway", "network address translation gateway"],
                "Azure NAT Gateway provides static, scalable outbound-only source NAT for subnets, giving all resources in that subnet deterministic public IPs without exposing individual machines.",
                "It resolves SNAT port exhaustion common in large-scale outbound workloads, but it handles only outbound traffic; inbound paths still require a separate load balancer or public IP.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/nat-gateway/nat-overview"),

            new(
                "azure-web-application-firewall",
                "Azure Web Application Firewall (WAF)",
                ["Azure Web Application Firewall", "Web Application Firewall", "WAF", "azure waf"],
                "Azure Web Application Firewall provides HTTP and HTTPS traffic filtering against threats such as SQL injection and cross-site scripting using managed and custom rule sets.",
                "WAF attaches to Application Gateway or Front Door; rule mode, custom exclusions, and rate limiting affect what traffic reaches the application origin.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/web-application-firewall/overview"),

            new(
                "azure-route-server",
                "Azure Route Server",
                ["Azure Route Server", "Route Server", "azure route server"],
                "Azure Route Server enables dynamic BGP route exchange between network virtual appliances and a virtual network, removing the need for manual user-defined route management.",
                "Route Server simplifies hub designs where NVAs advertise routes into the VNet, especially when third-party firewalls or SD-WAN appliances manage traffic flow.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/route-server/overview"),

            new(
                "azure-vnet-peering",
                "Azure VNet Peering",
                ["Azure VNet Peering", "VNet Peering", "azure vnet peering", "virtual network peering"],
                "Azure VNet Peering connects two virtual networks so resources in each can communicate using private IP addresses over the Microsoft backbone without internet transit.",
                "Peering is non-transitive by default, so hub-and-spoke designs require explicit planning for path routing, and global peering extends connectivity across Azure regions.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/virtual-network/virtual-network-peering-overview"),

            new(
                "azure-private-dns-zones",
                "Azure Private DNS Zones",
                ["Azure Private DNS Zones", "Private DNS Zones", "azure private dns zones", "private dns zone"],
                "Azure Private DNS Zones provide DNS name resolution for resources within virtual networks without exposing records publicly.",
                "Private endpoint DNS records must be registered in a private zone linked to the VNet; without this, clients resolve the public IP address even when a private endpoint exists.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/dns/private-dns-overview"),

            new(
                "azure-virtual-network-manager",
                "Azure Virtual Network Manager",
                ["AVNM", "Azure Virtual Network Manager", "Virtual Network Manager", "azure virtual network manager"],
                "Azure Virtual Network Manager centrally manages and enforces network topology, connectivity configurations, and security admin rules across multiple virtual networks.",
                "Network groups and connectivity configurations let platform teams standardize hub-and-spoke or mesh topologies without configuring peering or route tables individually per VNet.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/virtual-network-manager/overview"),

            new(
                "azure-firewall-manager",
                "Azure Firewall Manager",
                ["Firewall Manager", "Azure Firewall Manager", "azure firewall manager"],
                "Azure Firewall Manager provides centralized security policy and route management for Azure Firewall deployments across multiple regions and virtual hub architectures.",
                "Firewall Manager allows global policy changes to propagate to all associated firewalls in hub-and-spoke deployments, reducing the need for per-region rule maintenance.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/firewall-manager/overview"),

            new(
                "azure-service-endpoint",
                "Azure Service Endpoint",
                ["Azure Service Endpoint", "service endpoint", "service endpoints", "VNet service endpoint"],
                "An Azure Service Endpoint extends a virtual network's private identity to supported Azure PaaS services over the Azure backbone without assigning a private IP to the service.",
                "Service Endpoints route traffic privately while the service retains a public endpoint. Private Link instead assigns a private IP inside the VNet, creating a meaningful security distinction.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/virtual-network/virtual-network-service-endpoints-overview"),

            new(
                "azure-network-security-perimeter",
                "Azure Network Security Perimeter",
                ["Network Security Perimeter", "Azure Network Security Perimeter", "azure network security perimeter"],
                "Azure Network Security Perimeter defines a logical network boundary around PaaS resources so only traffic from within the perimeter or explicitly allowed sources can reach them.",
                "Network Security Perimeter complements Private Link by also protecting management-plane access to PaaS services, reducing risk from misconfigured public network access settings.",
                ConceptCategory.Networking,
                true,
                "https://learn.microsoft.com/azure/private-link/network-security-perimeter-concepts"),

            // ── IdentitySecurity ────────────────────────────────────────────────

            new(
                "microsoft-entra-external-id",
                "Microsoft Entra External ID",
                ["external identities", "Microsoft Entra External ID", "Entra External ID", "Azure AD B2C", "azure active directory b2c"],
                "Microsoft Entra External ID provides identity management for customer and partner scenarios, supporting social identities, OIDC, SAML, and custom sign-up and sign-in flows.",
                "External ID separates customer and partner identities from the employee directory. This separation affects token audiences, app registrations, and tenant architecture design.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/external-id/external-identities-overview"),

            new(
                "microsoft-entra-id-governance",
                "Microsoft Entra ID Governance",
                ["identity governance", "Microsoft Entra ID Governance", "Entra ID Governance", "entitlement management", "access reviews"],
                "Microsoft Entra ID Governance automates identity lifecycle, access reviews, and entitlement workflows while governing who holds access to resources over time.",
                "Access packages and access reviews can automate recurring recertification, but scope, approvers, and lifecycle actions still need explicit policy decisions.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/id-governance/identity-governance-overview"),

            new(
                "microsoft-entra-workload-id",
                "Microsoft Entra Workload ID",
                ["workload identity", "Microsoft Entra Workload ID", "Entra Workload ID", "workload identities"],
                "Microsoft Entra Workload ID provides identity governance and security features for non-human identities such as service principals and applications in automated workflows.",
                "Workload identities can be assigned Conditional Access-like policies and risk detection, extending identity governance principles from user accounts to apps and services.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/workload-id/workload-identities-overview"),

            new(
                "microsoft-entra-domain-services",
                "Microsoft Entra Domain Services",
                ["Microsoft Entra Domain Services", "Entra Domain Services", "Azure AD Domain Services", "azure active directory domain services"],
                "Microsoft Entra Domain Services provides managed Active Directory domain services including LDAP, Kerberos, and NTLM authentication without deploying domain controllers.",
                "It supports legacy workloads needing domain-joined VMs or LDAP-dependent apps, but as a managed read-only replica it does not support direct domain controller or schema access.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/identity/domain-services/overview"),

            new(
                "microsoft-entra-global-secure-access",
                "Microsoft Entra Global Secure Access",
                ["Microsoft Entra Global Secure Access", "Global Secure Access", "Microsoft Entra Private Access", "Microsoft Entra Internet Access", "Security Service Edge"],
                "Microsoft Entra Global Secure Access is Microsoft's Security Service Edge solution providing identity-centric access control for internet, SaaS, and private application traffic.",
                "Private Access uses ZTNA principles for internal apps, while Internet Access provides secure web gateway filtering; both route traffic through an identity-aware policy layer.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/global-secure-access/overview-what-is-global-secure-access"),

            new(
                "microsoft-entra-id-protection",
                "Microsoft Entra ID Protection",
                ["identity protection", "Microsoft Entra ID Protection", "Entra ID Protection", "Azure AD Identity Protection", "risk-based access"],
                "Microsoft Entra ID Protection detects identity risks such as leaked credentials, anomalous sign-in patterns, and token anomalies to enable risk-based Conditional Access policies.",
                "Risk detections feed into Conditional Access policies so sign-ins or accounts with elevated risk can be automatically blocked or stepped up to stronger authentication.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/entra/id-protection/overview-identity-protection"),

            new(
                "azure-managed-hsm",
                "Azure Managed HSM",
                ["Azure Managed HSM", "Managed HSM", "azure managed hsm", "managed hardware security module"],
                "Azure Managed HSM is a fully managed single-tenant hardware security module service for storing and using cryptographic keys with FIPS 140-2 Level 3 validation.",
                "Managed HSM provides a dedicated HSM pool where the customer holds full cryptographic domain control, distinct from the shared multi-tenant boundary in Azure Key Vault Premium.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/key-vault/managed-hsm/overview"),

            new(
                "microsoft-defender-xdr",
                "Microsoft Defender XDR",
                ["Microsoft Defender XDR", "Defender XDR", "Microsoft 365 Defender", "extended detection and response"],
                "Microsoft Defender XDR is an extended detection and response platform that correlates signals across endpoint, identity, email, and cloud to detect and remediate threats.",
                "Defender XDR unifies alerts from Defender for Endpoint, Defender for Identity, Defender for Office 365, and Defender for Cloud Apps into correlated incidents with automated attack disruption.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/defender-xdr/microsoft-365-defender"),

            new(
                "defender-external-attack-surface",
                "Microsoft Defender External Attack Surface Management",
                ["Microsoft Defender External Attack Surface Management", "Defender External Attack Surface Management", "external attack surface management"],
                "Microsoft Defender External Attack Surface Management continuously discovers and monitors an organization's internet-exposed assets to identify and track attack surface risks.",
                "EASM maps externally visible hosts, domains, certificates, and services, including assets that were not intentionally registered. The resulting inventory surfaces shadow assets and expired certificate risks.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/external-attack-surface-management/overview"),

            new(
                "microsoft-defender-for-iot",
                "Microsoft Defender for IoT",
                ["Microsoft Defender for IoT", "Defender for IoT", "OT network security"],
                "Microsoft Defender for IoT provides agentless security monitoring for OT, ICS, and IoT networks by passively analyzing industrial protocols to detect threats and anomalies.",
                "Defender for IoT uses passive network monitoring that avoids sending packets to devices. This approach protects manufacturing and critical-infrastructure operations from disruption caused by active scanning.",
                ConceptCategory.IdentitySecurity,
                true,
                "https://learn.microsoft.com/azure/defender-for-iot/organizations/overview"),

            // ── ManagementGovernance ─────────────────────────────────────────────

            new(
                "microsoft-purview",
                "Microsoft Purview",
                ["Microsoft Purview", "azure purview", "Microsoft Purview governance portal", "purview"],
                "Microsoft Purview is a unified data governance and compliance platform covering data catalog, classification, data loss prevention, and regulatory compliance management.",
                "Purview combines the former Azure Purview data catalog and Microsoft 365 Compliance Center capabilities, so coverage depends on which workloads and connectors are configured.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/purview/purview"),

            new(
                "azure-deployment-stacks",
                "Azure Deployment Stacks",
                ["Azure Deployment Stacks", "Deployment Stacks", "azure deployment stacks", "deployment stack"],
                "Azure Deployment Stacks manage a collection of Azure resources as an atomic unit, enabling lifecycle operations such as update and delete while protecting managed resources from unintended changes.",
                "Stacks track resource membership and support deny assignments for managed resources. These controls reduce orphaned resources and configuration drift from changes outside the stack.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/bicep/deployment-stacks"),

            new(
                "azure-managed-applications",
                "Azure Managed Applications",
                ["Azure Managed Applications", "Managed Applications", "azure managed applications", "managed application"],
                "Azure Managed Applications allow publishers to offer preconfigured solution bundles that consumers deploy while the publisher retains management control over the underlying infrastructure.",
                "Managed Applications separate publisher-controlled infrastructure from consumer-visible usage, a pattern used for governed platform services and Azure Marketplace solutions.",
                ConceptCategory.ManagementGovernance,
                true,
                "https://learn.microsoft.com/azure/azure-resource-manager/managed-applications/overview"),

            // ── MonitoringReliability ────────────────────────────────────────────

            new(
                "azure-service-health",
                "Azure Service Health",
                ["Azure Service Health", "Service Health", "azure service health"],
                "Azure Service Health provides personalized notifications when Azure service incidents, planned maintenance, or health advisories affect resources in your subscriptions and regions.",
                "It distinguishes global Azure outages from issues specific to your own resources, but root-cause detail and SLA credit eligibility depend on the incident type and agreement tier.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/service-health/overview"),

            new(
                "azure-chaos-studio",
                "Azure Chaos Studio",
                ["Azure Chaos Studio", "Chaos Studio", "chaos engineering"],
                "Azure Chaos Studio is a managed resilience-testing service that injects controlled faults into Azure resources to validate application and infrastructure behavior under failure conditions.",
                "Experiments target specific resource types with defined fault actions. Their controlled blast radius provides evidence for resilience behavior and incident runbooks before actual outages occur.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/chaos-studio/chaos-studio-overview"),

            new(
                "azure-managed-grafana",
                "Azure Managed Grafana",
                ["Azure Managed Grafana", "Managed Grafana", "azure managed grafana"],
                "Azure Managed Grafana is a fully managed Grafana service natively integrated with Azure Monitor, managed Prometheus, and other Azure data sources for building operational dashboards.",
                "It removes Grafana server management and provides Entra ID authentication natively, but dashboard design, data-source connections, and alert routing still require explicit configuration.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/managed-grafana/overview"),

            new(
                "azure-network-watcher",
                "Azure Network Watcher",
                ["Azure Network Watcher", "Network Watcher", "azure network watcher"],
                "Azure Network Watcher provides diagnostics, flow logs, topology visualization, and packet capture for monitoring and troubleshooting Azure virtual networks.",
                "NSG flow logs, connection troubleshoot, and IP flow verify are capabilities within Network Watcher that help diagnose traffic-path and filtering problems.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/network-watcher/network-watcher-overview"),

            new(
                "azure-workbooks",
                "Azure Monitor Workbooks",
                ["Azure Workbooks", "Azure Monitor Workbooks", "azure workbooks"],
                "Azure Monitor Workbooks provide flexible, interactive report templates that combine text, metrics, logs, and parameter controls into shared dashboards for Azure resources.",
                "Workbooks visualize correlated data across multiple Azure services while relying on maintained queries and data-source connections.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/visualize/workbooks-overview"),

            new(
                "azure-monitor-agent",
                "Azure Monitor Agent (AMA)",
                ["AMA", "Azure Monitor Agent", "azure monitor agent"],
                "Azure Monitor Agent is the unified data-collection agent for Azure Monitor that replaces Log Analytics agent, Diagnostics extension, and Telegraf using Data Collection Rules.",
                "AMA uses Data Collection Rules to define collected data, destinations, and transformations. This model enables granular multi-workspace routing without reinstalling agents.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/agents/azure-monitor-agent-overview"),

            new(
                "data-collection-rule",
                "Data Collection Rule (DCR)",
                ["Data Collection Rule", "DCR", "data collection rules", "azure data collection rule"],
                "A Data Collection Rule defines the data sources, transformations, and destinations for Azure Monitor data ingestion, enabling granular control and reuse across monitored resources.",
                "DCRs are the configuration building blocks for AMA-based monitoring; a single DCR can route different data streams to different Log Analytics workspaces or other destinations.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/azure-monitor/essentials/data-collection-rule-overview"),

            new(
                "azure-update-manager",
                "Azure Update Manager",
                ["Azure Update Manager", "Update Manager", "azure update manager"],
                "Azure Update Manager is a unified service for managing OS and application updates on Azure VMs, Arc-enabled servers, and Azure Local machines across environments.",
                "It provides centralized visibility, scheduling, and compliance reporting for patches, replacing the legacy Update Management solution in Azure Automation.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/update-manager/overview"),

            new(
                "service-level-objective",
                "Service Level Objective (SLO)",
                ["Service Level Objective", "SLO", "service level objectives"],
                "A Service Level Objective is an internal reliability target defined for a service that represents the minimum acceptable performance or availability level before user experience degrades.",
                "An SLO differs from an SLA in that it is an internal engineering target set more conservatively; error budgets derived from SLOs help teams balance reliability investment against new feature work.",
                ConceptCategory.MonitoringReliability,
                false,
                "https://learn.microsoft.com/azure/well-architected/reliability/metrics"),

            new(
                "high-availability",
                "High availability",
                ["high availability", "highly available", "HA architecture"],
                "High availability describes an architecture designed to minimize downtime through redundancy, failover automation, and health monitoring across infrastructure components.",
                "True availability requires redundancy at every layer—compute, networking, data, and dependencies—since a single non-redundant component becomes the availability ceiling.",
                ConceptCategory.MonitoringReliability,
                false,
                "https://learn.microsoft.com/azure/reliability/overview"),

            new(
                "recovery-services-vault",
                "Azure Recovery Services vault",
                ["Azure Recovery Services vault", "Recovery Services vault", "azure recovery services vault"],
                "An Azure Recovery Services vault is the management and storage container for Azure Backup and Azure Site Recovery data, policies, and protected items.",
                "Vault design decisions—region, redundancy setting, soft-delete retention, and cross-region restore—directly affect recovery reliability and compliance posture.",
                ConceptCategory.MonitoringReliability,
                true,
                "https://learn.microsoft.com/azure/backup/backup-azure-recovery-services-vault-overview"),

            new(
                "business-continuity",
                "Business continuity",
                ["business continuity", "business continuity planning", "BCDR"],
                "Business continuity planning defines the processes and technical configurations needed to keep critical functions operational during and after a disruption.",
                "Continuity depends on pre-tested runbooks, declared RTO/RPO targets, and validated failover procedures; documentation alone without testing does not confirm recoverability.",
                ConceptCategory.MonitoringReliability,
                false,
                "https://learn.microsoft.com/azure/reliability/business-continuity-management-program"),

            // ── MigrationDevOpsFinOps ────────────────────────────────────────────

            new(
                "azure-developer-cli",
                "Azure Developer CLI (azd)",
                ["Azure Developer CLI", "azd", "azure developer cli"],
                "The Azure Developer CLI is a developer-focused tool that provisions, deploys, monitors, and manages Azure application environments using standardized infrastructure-as-code templates.",
                "azd templates bundle infrastructure code and application code into a single inner-loop workflow, but the azd.yaml manifest and environment variables still need per-project setup.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/developer/azure-developer-cli/overview"),

            new(
                "azure-load-testing",
                "Azure Load Testing",
                ["Azure Load Testing", "azure load testing", "load testing service"],
                "Azure Load Testing is a managed service for running high-scale load tests using JMeter, Locust, or URL-based scripts against any endpoint to evaluate application performance under stress.",
                "Tests run from Azure-managed infrastructure and integrate with CI/CD pipelines, but representative traffic patterns and meaningful pass/fail thresholds still require engineering design.",
                ConceptCategory.MigrationDevOpsFinOps,
                true,
                "https://learn.microsoft.com/azure/app-testing/load-testing/overview-what-is-azure-load-testing"),

            // ── FoundationalCloud ────────────────────────────────────────────────

            new(
                "azure-extended-zones",
                "Azure Extended Zones",
                ["Azure Extended Zones", "Extended Zones", "azure extended zones"],
                "Azure Extended Zones bring select Azure compute, networking, and storage services to specific metro locations outside traditional Azure regions for low-latency workloads.",
                "Extended Zones connect back to a parent Azure region, so service compatibility and network egress design still depend on the capabilities available in that region.",
                ConceptCategory.FoundationalCloud,
                true,
                "https://learn.microsoft.com/azure/extended-zones/overview"),
        };
    }
}
