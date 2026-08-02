# Azure data, storage, AI, and analytics

Reviewed: 2026-08-02

Choose from workload characteristics, not popularity. Clarify data model,
queries, consistency, latency, throughput, scale, availability, recovery,
residency, security, integration, team skills, and existing investments.
Pricing, capacity, quotas, model availability, and regional features require
current verification.

## Relational data

### Azure SQL Database

Conversation signals:

- a modern SQL Server application needs managed database capabilities;
- the team wants platform-managed patching, backups, high availability, and
  elastic scaling;
- a new relational cloud application does not need instance-level features.

Ask about database size and growth, compatibility, transaction and query
profile, high availability, read scaling, networking, maintenance windows, and
license eligibility.

Candidate action: run an assessment and compare the applicable purchasing and
service-tier models using measured performance data.

Hold when SQL Server instance features or cross-database dependencies require
Azure SQL Managed Instance or SQL Server on Azure VMs. Do not prescribe a tier
or compute size without assessment.

### Azure SQL Managed Instance

Signals include SQL Server migration with instance-scoped compatibility,
minimal application change, SQL Agent, or cross-database requirements. Ask for
an Azure SQL assessment, network design, feature compatibility, migration
downtime, and capacity requirements.

### Azure Database for PostgreSQL

Signals include PostgreSQL applications that want a managed open-source
database. Ask about extensions, major version, HA, read replicas, connection
scaling, networking, backup, and migration tooling. Validate extension and
feature compatibility before recommending migration.

## NoSQL and globally distributed data

### Azure Cosmos DB

Conversation signals:

- the application needs document, key-value, graph, or compatible NoSQL APIs;
- predictable low-latency access and elastic throughput are required;
- active distribution across regions or integrated vector search is justified.

Ask about access API, partition key, request-unit demand, item size, consistency,
regions, write topology, data model, and vector-search requirements.

Candidate action: model the partition key and benchmark representative queries
before selecting throughput and distribution options.

Hold when the workload is naturally relational, its access patterns are not
known, or multi-region complexity has no business requirement. Do not infer
throughput or a commitment from record count alone.

Common combination: Cosmos DB, Functions, API Management, Managed Identity,
Private Link, and Application Insights.

## Storage

### Blob Storage and Data Lake Storage Gen2

Signals include object data, documents, media, backup, archive, analytics data,
or a data lake. Ask about access frequency, namespace needs, lifecycle,
retention, immutability, encryption, network access, redundancy, and recovery.

Candidate action: define access tiers and lifecycle policy from measured access
patterns. Hold archive recommendations when fast retrieval is required.

### Azure Files

Signals include managed SMB or NFS shares and file-server migration. Ask about
protocol, identity integration, IOPS, throughput, share size, caching, backup,
and client locations. Consider Azure File Sync when local caching is required.

### Managed Disks

Signals include persistent block storage for Azure VMs. Select disk type only
from measured IOPS, throughput, capacity, latency, resilience, and cost needs.

### Azure NetApp Files

Signals include demanding enterprise NFS or SMB workloads such as SAP, Oracle,
virtual desktop, or databases needing high-performance file storage. Confirm
protocol, performance, network, backup, replication, and minimum capacity
requirements. Hold for small general-purpose file shares.

## Analytics

### Microsoft Fabric

Signals include a unified SaaS analytics platform spanning data integration,
engineering, warehousing, real-time intelligence, data science, and Power BI.
Ask about existing Power BI and data-platform investments, capacity model,
OneLake governance, region, data residency, security, and migration scope.

Candidate action: define one bounded analytics use case and compare Fabric with
the customer's current Synapse, Databricks, or data-warehouse architecture.

### Azure Synapse Analytics

Signals include an established Synapse estate or requirements for integrated
SQL, Spark, pipelines, and data exploration. Ask whether the need is serverless
exploration, dedicated warehousing, Spark, or orchestration. For greenfield
decisions, compare with current Fabric capabilities rather than assuming one
platform.

### Azure Databricks

Signals include Spark-based data engineering, lakehouse, advanced analytics,
ML, and existing Databricks skills. Clarify governance, data location, cluster
policies, private networking, workload isolation, and cost controls.

## AI

### Microsoft Foundry and Azure OpenAI

Conversation signals:

- the customer has a defined generative-AI use case, users, data, and desired
  business outcome;
- model selection, agent workflows, evaluation, responsible AI, and managed
  deployment are needed;
- the application requires approved foundation models through Azure.

Ask about use case and prohibited use, data classification, grounding sources,
quality metrics, human oversight, content filtering, prompt-injection defense,
identity, private networking, observability, model and region availability,
latency, throughput, and budget.

Candidate actions:

- define an evaluation set and measurable quality and safety criteria;
- design identity, data boundary, grounding, content safety, logging, and human
  review before production;
- compare models using the current Foundry catalog rather than naming one from
  memory.

Hold when the problem can be solved deterministically, the data is not approved,
success cannot be measured, or responsible-AI ownership is missing.

### Azure AI Search

Signals include keyword, semantic, vector, or hybrid retrieval over approved
enterprise content, including RAG. Ask about source systems, chunking, update
frequency, access trimming, index size, query latency, languages, relevance
evaluation, and network isolation.

Candidate action: prototype retrieval quality with representative questions and
permission boundaries before connecting it to a generative model.

### Foundry Tools

Speech, Language, Translator, Document Intelligence, Vision, Content Safety, and
other focused services are candidates when the requirement is a specific AI
capability rather than a custom model. Verify current service names, feature
status, retirement notices, model or language support, quotas, and regions.

### Azure Machine Learning

Signals include custom model training, ML lifecycle management, compute,
registries, pipelines, deployment, and monitoring. Ask about training data,
framework, repeatability, governance, endpoint scale, drift, and MLOps ownership.

## Official sources

- Azure data-store choices:
  https://learn.microsoft.com/en-us/azure/architecture/guide/technology-choices/data-store-overview
- Azure SQL Database:
  https://learn.microsoft.com/en-us/azure/azure-sql/database/sql-database-paas-overview
- Azure SQL Managed Instance:
  https://learn.microsoft.com/en-us/azure/azure-sql/managed-instance/sql-managed-instance-paas-overview
- PostgreSQL:
  https://learn.microsoft.com/en-us/azure/postgresql/flexible-server/overview
- Cosmos DB:
  https://learn.microsoft.com/en-us/azure/cosmos-db/overview
- Storage:
  https://learn.microsoft.com/en-us/azure/storage/common/storage-introduction
- Microsoft Fabric:
  https://learn.microsoft.com/en-us/fabric/fundamentals/microsoft-fabric-overview
- Synapse Analytics:
  https://learn.microsoft.com/en-us/azure/synapse-analytics/overview-what-is
- Microsoft Foundry:
  https://learn.microsoft.com/en-us/azure/foundry/what-is-foundry
- Azure AI Search:
  https://learn.microsoft.com/en-us/azure/search/search-what-is-azure-search
- Azure Machine Learning:
  https://learn.microsoft.com/en-us/azure/machine-learning/overview-what-is-azure-machine-learning
