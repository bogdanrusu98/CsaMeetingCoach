# Azure subscriptions, FinOps, commercial options, and support

Reviewed: 2026-08-02

This guide separates technical fit from commercial eligibility. Exact prices,
discounts, currencies, taxes, commitment rules, refund or exchange rights,
benefit scope, support response targets, and agreement eligibility change.
Verify them in current official documentation and the customer's billing scope.
Involve the account team, licensing specialist, Cloud Solution Provider, or
licensing partner before a commitment.

## Azure subscriptions are governance and billing boundaries

Conversation signals:

- teams need separate production, non-production, business-unit, or regulatory
  boundaries;
- ownership, quotas, policy, RBAC, invoices, or cost allocation are unclear;
- a landing-zone design or new program needs subscription vending.

Ask about tenant, management groups, billing account, agreement, environments,
business ownership, platform ownership, policy, identity, networking, quotas,
regions, tags, budgets, and chargeback or showback.

Candidate action: define a subscription-management and vending model aligned to
the landing zone. Hold creation of many subscriptions until lifecycle,
ownership, policy, and cost allocation are established.

## Agreement and channel discovery

Commercial eligibility depends on how the customer purchases Azure. Common
contexts include Enterprise Agreement, Microsoft Customer Agreement, Microsoft
Partner Agreement or Cloud Solution Provider, and web-direct or pay-as-you-go
offers.

Ask:

- What billing account and agreement or channel applies?
- Who can view costs and purchase benefits?
- Is Azure prepayment or another consumption commitment in place?
- Which currency, invoice scope, and renewal dates matter?
- Is a partner or licensing solution provider involved?
- Are Marketplace purchases governed separately?

Do not infer an agreement from company size or an email domain. Do not treat an
Azure subscription as a support plan or a consumption commitment.

## Cost Management and FinOps

Conversation signals:

- cloud spend is growing without ownership or explanation;
- budgets, forecasts, allocation, anomaly response, or unit economics are
  missing;
- engineering and finance need a shared optimization process.

Ask about cost owners, allocation model, tags, budgets, alerts, exports,
forecasting, invoice reconciliation, unit metrics, optimization cadence,
non-production controls, and decision rights.

Candidate actions:

- establish visibility with Cost Management reports, budgets, alerts, and
  exports;
- assign accountable owners and a recurring review cadence;
- use Azure Advisor and measured utilization to evaluate right-sizing;
- relate cost decisions to reliability, security, performance, and business
  value rather than minimizing spend in isolation.

Hold commitment purchases when allocation, baseline use, ownership, or workload
stability is uncertain.

## Reservations

Conversation signals:

- eligible resources have stable, predictable use over a sustained period;
- utilization data and Azure recommendations support a term commitment.

Ask about eligible service and SKU, region, scope, utilization baseline, term,
payment option, purchasing permissions, planned migrations, exchange or refund
rules, and who monitors benefit use.

Candidate action: review current reservation recommendations using actual
customer usage and compare them with savings plans and pay-as-you-go.

Never recommend quantity, scope, term, discount, exchange, or refund behavior
from memory. A technically suitable workload can still be commercially
ineligible.

## Azure savings plans

Conversation signals:

- eligible compute use is stable in aggregate but moves across regions, instance
  families, or supported services;
- the customer wants a spend-based commitment rather than a resource-specific
  reservation.

Ask about eligible hourly spend, utilization history, term, billing agreement,
currency, purchasing permissions, planned architecture changes, and unused
commitment risk.

Candidate action: compare current portal or Advisor recommendations with
reservations. Never calculate a commitment from a verbal monthly bill.

## Azure Hybrid Benefit

Conversation signals:

- the customer has qualifying Windows Server or SQL Server licenses and wants
  to use eligible licenses with Azure workloads.

Ask for product edition, license quantity, license model, active Software
Assurance or qualifying subscription status, deployment target, current use,
and licensing-owner confirmation.

Candidate action: request a licensing review and model eligible scenarios.
Never state eligibility based only on "we own Windows or SQL licenses."

## Dev/test and consumption commitments

Dev/test offers may be relevant to qualifying non-production workloads under
specific agreements and subscriptions. Confirm agreement, user and workload
eligibility, allowed use, and production separation.

Azure consumption commitments such as Azure prepayment or Microsoft Azure
Consumption Commitment are commercial constructs, not discounts automatically
attached to every subscription. Confirm term, eligible consumption, exclusions,
remaining balance, renewal, and account-team ownership. Do not promise that a
specific Marketplace or support charge will decrement a commitment.

## Azure Marketplace

Ask whether the offer is first-party or third-party, private or public, who is
the seller, what the licensing meter is, whether private offers apply, who can
purchase, and how charges interact with the customer's agreement and
commitments. Verify the actual offer and billing terms before recommendation.

## Azure support plans

Conversation signals:

- production or business-critical workloads need technical incident support;
- the customer wants proactive guidance or an enterprise-wide support
  relationship;
- current response and escalation paths do not meet business needs.

Ask about current support plan and channel, production criticality, required
coverage and response, internal support maturity, recent incidents, CSP
relationship, Microsoft-wide support needs, renewal, and budget.

Candidate action: compare current official support-plan features with the
customer's incident and advisory needs.

Hold when support is being proposed as a substitute for resilient architecture,
monitoring, ownership, or incident management. Confirm current plan names,
prices, response targets, included services, and purchase channel.

## Required commercial wording

Safe:

"The workload appears technically compatible with this option. Please verify
current eligibility, pricing, regional availability, and agreement-specific
terms in the customer's billing scope with the account team or partner."

Unsafe:

"This customer qualifies for a three-year commitment and will save a specific
percentage."

## Official sources

- Azure pricing:
  https://azure.microsoft.com/en-us/pricing/
- Azure Pricing Calculator:
  https://azure.microsoft.com/en-us/pricing/calculator/
- Cost Management best practices:
  https://learn.microsoft.com/en-us/azure/cost-management-billing/costs/cost-mgt-best-practices
- Azure Advisor:
  https://learn.microsoft.com/en-us/azure/advisor/advisor-overview
- Reservations:
  https://learn.microsoft.com/en-us/azure/cost-management-billing/reservations/save-compute-costs-reservations
- Savings plans:
  https://learn.microsoft.com/en-us/azure/cost-management-billing/savings-plan/savings-plan-overview
- Azure Hybrid Benefit:
  https://azure.microsoft.com/en-us/pricing/offers/azure-hybrid-benefit/
- Microsoft Customer Agreement:
  https://learn.microsoft.com/en-us/azure/cost-management-billing/understand/mca-overview
- Enterprise Agreement:
  https://learn.microsoft.com/en-us/azure/cost-management-billing/manage/ea-portal-agreements
- Azure Marketplace:
  https://learn.microsoft.com/en-us/marketplace/azure-marketplace-overview
- Azure support plans:
  https://azure.microsoft.com/en-us/support/plans/
- Cloud Adoption Framework FinOps:
  https://learn.microsoft.com/en-us/cloud-computing/finops/
