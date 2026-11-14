# Legacy Proforma Invoice port — parity inventory

**Working branch:** `feature/proforma-invoice-full-port`  
**Reference:** `JUBERSALEEM/ProGlassAutomationADV` tag `v2.0.15`  
**Target:** this repository's seven-stage Proforma Invoice module and Theme Studio design system.

This inventory is a source-based migration record, not a claim that a Windows build or runtime test has passed. The port must preserve business behaviour, not copy the legacy screen implementation or its dependency graph.

## 1. Migration boundary

- Keep the V3 seven-stage workflow. ADV stages 1–3 resolve to V3 Header; ADV 4–9 map to V3 Pricing, Specification Grid, Other Charges, Summary, Attachments, and Profit & Loss.
- Use V3's Company Profile, Client Management, Project Records, User Management, and Inquiry modules as the owners of master data. Store invoice-time snapshots so issued documents remain historically accurate.
- Keep V3's Theme Studio tokens, dynamic resources, and shared controls. Do not transplant ADV styles, resource dictionaries, window chrome, or view models.
- Keep the existing V3 database/migration chain and additive persistence. Do not introduce a second SQLite database or a second source of truth for clients/projects/inquiries.
- Do not edit the source branch, the legacy repository, or unrelated modules.

## 2. Legacy capability inventory and V3 landing zones

| Legacy capability / source area | V3 landing zone | Port/verification requirement |
| --- | --- | --- |
| Company, client, project, prepared-by and inquiry context (ADV Steps 1–3; `ViewModels/ProformaInvoice/Step1CompanySetup`, `Step2ClientProfile`, `Step3ProjectSpecs`) | Stage 1 Header; `InvoicePartyReader`; party snapshot models | Resolve from V3 owning modules; support walk-in client only where explicitly allowed; preserve invoice-time identity and refresh only on operator action. |
| Glass builds, panes, spacer, interlayer, yield, margin (ADV Step 4; `Step4PricingEstimator`; `SheetStorePricingService`) | `step6.a.5.PricingModels.cs`, `step6.a.6.PricingEstimator.cs`; Pricing stage | SGU, DGU, LAM and DGU+LAM; formula parity; yield bounds and warnings; price provenance and section-specific cost cards. |
| Spec grid, dimensions, quantity, billable area, group and row handling (ADV Step 5; `Step5DataGrid`; `Step5Grid`) | `step6.a.7.SpecGrid.cs`; Grid stage; import/export services | Single-, L- and four-leg geometry; minimum billable area; row numbering; quantity and area rounding; preserve group-level pricing provenance. |
| Charge catalogue, units/metrics, manual and automatic charge rules (ADV Step 6; `Step6OtherCharges`; `OtherChargeAutomationRules.cs`) | `step6.a.8.ChargeCatalog.cs`, `step6.a.9.OtherCharges.cs`, `step6.a.10.OtherChargeAutomation.cs`; Charges stage | Catalogue coverage; metric quantity sources; advanced automation; manual active overrides; colour wastage rebuilds; custom rows; disabled wastage billing must not erase consumption cost. |
| Discount, VAT, totals, print terms and summary (ADV Step 7; `Step7PrintPreview`) | `step6.a.11.InvoiceSummary.cs`; Summary stage and export layouts | Financial waterfall, fixed/percentage discount, target-total reverse solve, VAT snapshot, terms and validity; PDF/Excel/CSV output must agree with saved totals. |
| Attachments and evidence chain (ADV Step 8; `Attachments`) | `step6.b.12.ProformaInvoiceAttachmentStore.cs`; Documents stage | Copy/atomic placement, generated safe names, original display names, size/type validation, remove/open failure handling, and no orphan files after failed operations. |
| Profit/loss, stock sheet costing and target-price KPIs (ADV Step 9; `Step9ProfitLoss`, `TargetPricing`) | `step6.a.12.ProfitLoss.cs`; Profit stage | Whole-sheet costing, production cost rows, wastage sheet quantities, VAT-exclusive profit, explicit KPI definitions/tolerance, warnings and reproducible price-basis provenance. |
| Saved invoice register, lifecycle and duplicate/revision workflow | `step6.a.1.InvoiceLifecycle.cs`; repository/read/lifecycle files; workspace commands | Number uniqueness, valid transitions, terminal cancellation, immutable issued documents, duplicate-as-new-draft, concurrency, read-only enforcement. |
| Persistence, schema and JSON snapshots | `step6.b.*` schema, JSON, record, repository and context files | Single transaction for row/body; additive migration; schema constraints/indexes; legacy-safe parsing; corrupt body must report rather than become an empty invoice. |
| Inquiry BOQ conversion and PI link sync | `step6.b.11.InquiryBoqConverter.cs`; Inquiry integration | Preserve inquiry BOQ and unrelated fields; link via Inquiry module's own API; failures after PI commit are surfaced as warnings. |
| Import/export and cut-list interoperability | Stage 3 and stage 5 services; export layouts | Excel/CSV import parsing, malformed-row feedback, export formulas/layouts, no silent field truncation, round-trip tests. |

## 3. Dependencies: do not copy blindly

The legacy application references a much larger package set, including ClosedXML, CsvHelper, EF Core SQLite, QuestPDF, PdfSharp-WPF and SkiaSharp. V3 currently declares EF Core SQLite and LiveChartsCore/SkiaSharp WPF at the application level. Before adding any package:

1. Confirm the V3 target framework and existing transitive package versions.
2. Identify the exact legacy feature that needs the package and whether a V3 implementation already exists.
3. Prefer the existing V3 package/API where parity can be achieved without weakening output fidelity.
4. Add only the smallest required package set, with pinned compatible versions.
5. Add an explicit compile/test project boundary so test entry points and test-only packages never enter the WPF application.

Legacy package presence alone is not evidence that every package is required by the port.

## 4. Integration and quality gates

- All invoice screens remain V3 views and consume Theme Studio dynamic resources; no hard-coded palette copied from ADV.
- Keep business calculations in the domain layer and callable without WPF.
- Keep persistence and attachment IO behind services/repositories.
- Keep the seven-stage workspace as the single orchestration point; view models must not duplicate calculations.
- Add parity tests for each migrated formula/rule and regression tests for each legacy defect guard.
- Verify migration against a copy of an existing V3 database; do not run destructive experiments against a production database.
- Build and run tests on Windows with the .NET 8 SDK. If this authoring environment cannot run WPF/.NET, report that limitation rather than marking those checks passed.

## 5. Work sequence

1. Inventory current V3 domain, repository, presentation, views, project file, tests and migration registration.
2. Compare each legacy service/model with the V3 landing zone; record missing behaviours and intentional differences.
3. Port missing domain rules first, with isolated tests.
4. Port persistence, attachments, import/export and inquiry integration through V3 interfaces.
5. Wire the V3 seven-stage presentation and Theme Studio views; do not introduce a second wizard.
6. Run source gates, domain behaviour tests, migration-preservation checks and Windows build/runtime verification.
7. Review the branch diff for unrelated changes and document remaining gaps before merge.

## 6. Current source baseline

The V3 branch already contains a substantial PI implementation: domain files for lifecycle, rounding, glass measures, parties, pricing, specification grid, charge catalogue/automation, summary, P&L, draft and rules; persistence files for schema, JSON, record, repository, access, readers, inquiry BOQ conversion and attachments; a seven-stage workspace; presentation view models; seven stage views; and dedicated source/behaviour test projects.

Therefore this work is a **parity audit and completion port**, not a blind copy of the legacy module. The next code changes should be driven by verified gaps and must not overwrite working V3 architecture.
