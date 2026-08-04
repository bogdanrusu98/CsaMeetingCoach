# Knowledge source policy

This folder is the reviewed input for the Foundry knowledge indexer.
Supported source types are Markdown, plain text, HTML, JSON, PDF, DOCX, PPTX,
and XLSX.
Sources may be replaced or extended after review.

Index only approved, non-secret organizational or public material. Do not add
customer transcripts, credentials, private customer data, personal data, special
category data, or material whose use would violate sensitivity labels, retention
rules, copyright, GDPR, or organizational policy. Remove stale material from the
source set and create a reviewed replacement store rather than indexing it
silently.

## Azure recommendation catalog

The Azure catalog was reviewed on 2026-08-02 and is synthesized from the
official Microsoft sources linked in each file:

- `azure-application-platforms.md`
- `azure-foundations.md`
- `azure-data-storage-ai.md`
- `azure-network-security-governance.md`
- `azure-reliability-operations-migration.md`
- `azure-commercial-finops-support.md`
- `azure-solution-playbooks.md`

The catalog is decision support, not a price list or a substitute for workload
assessment. Product fit, commercial eligibility, current pricing, licensing,
regional availability, feature status, quotas, service limits, and support
terms must be verified before a customer commitment. Use the Azure Pricing
Calculator, Azure Advisor, the customer's billing scope, current Microsoft
documentation, and the account team or licensing partner as applicable.

For a concrete service, project, system, or migration discussion, recommendations
use one integrated task: the primary action plus no more than two grounded
Microsoft dependencies across identity, security, networking, governance,
reliability, observability, operations, data protection, or cost. Unconfirmed
dependencies are framed as assessments, never as selected products or an
automatic bundle.

When refreshing a source, record the review date in the file, keep only concise
summaries, and replace time-sensitive claims rather than appending conflicting
versions.
