# Proforma Invoice Module

<!-- pg-nav -->
> **[Repository root](../../README.md)** · [All documents](../../docs/README.md) · [Application workflow](../../docs/WORKFLOW.md) · [Design system](../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Enterprise-grade **9-step wizard** for building professional glass proforma invoices — the backbone of the whole business chain
(Inquiry → **PI** → Job Order → Delivery Note → Tax Invoice → Profitability).

## Wizard Steps

1. **Company** — Hard-linked to Company Center: loading a card copies all 8 legal-entity fields (name, TRN, location, rep, rep mobile, e-mail, bank account, bank name, logo). Edits enrich the module record at save under a preserve-blank contract. *(The legacy default-mark no longer exists — the selected company is the only authority; see `../../docs/ERP_UPGRADE_ROADMAP.md` post-P5.)*
2. **Client** — Customer/TRN/billing profile, suggestion dedup, auto-registration.
3. **Project** — Project identifier, contract reference, site mapping.
4. **Pricing Estimator** — Per build type (SGU / DGU / LAM / DGU+LAM): sheet selection, air spacer, processing rates, per-section panels. **Per-group production-rate provenance (Phase 1, `0f1a67e`)**: each group persists *its own* inputs — a multi-section quote never silently costs every group with section 1's panel.
5. **Spec Data Grid** — Multi-spec rows (W1/H1/W2/H2/qty → SQM, value), inquiry BOQ imports (`InquiryBoqConversion`, Phase 2), manual ownership guards vs Step-4 sync.
6. **Other Charges** — Predefined + wastage + custom charges; **sheet colours auto-imported from Step 5** (3 specs ⇒ N colour rows — dynamic, never hardcoded), colour-driven pane targeting, manual-row preservation.
7. **Summary** — Discount, UAE VAT (5%, round-safe), totals waterfall.
8. **Attachments** — Any-file evidence chain per invoice.
9. **Profit & Loss** — *The single pricing engine.* Sheet cost from Bin/Sheet Store buy prices, production rows from the spec's own provenance, target-price KPI model, wastage accounting. Snapshot feeds Job Profitability, Statements and Reports — and carries a **price-basis signal** so reporting flags drift instead of silently recomputing (Phase 3).

## Persistence

The entire wizard state round-trips through a JSON snapshot (`ProformaInvoiceSnapshot` →
`Services/ProformaInvoice/ProformaInvoiceRepository/`) including Step 4 sections, spec groups + production-rate provenance, charges, summary, P&L (with `GlassPriceStamp` + `ComputedAtUtc`), attachments and audit ids. Silent draft saves backstop dirty-session crashes.

## Cross-module links

| Direction | Behaviour |
|---|---|
| ← **Inquiry** | BOQ line items convert to Step 5 groups (permission-gated, audited); partial rows stay on the inquiry as notes |
| → **Job Order / Delivery Note / Tax Invoice** | Converted with full snapshot contents; status workflows stay vocabulary-funneled (Phase 5) |
| ← **Company / Client / Project centers** | Master data enriches on invoice save — preserve-blank, field-diff audit trail |
| → **Job Profitability / Reporting / Statement** | Read-only consumers of the persisted snapshot |

## Folders

```
    Views/ProformaInvoice/
        Step1CompanySetup/  Step2ClientProfile/  Step3ProjectSpecs/
        Step4PricingEstimator/  Step5DataGrid/  Step6OtherCharges/
        Step7PrintPreview/  Step8Attachments/  Step9ProfitLoss/
        SavedInvoicesList/
    ViewModels/ProformaInvoice/    (matching step view models)
```

<!-- pg-nav -->
---

*This README documents `Views/ProformaInvoice/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../docs/README.md).*
<!-- pg-nav -->
