# step6.e.1 — Proforma Invoice module record

**Module:** `step6.ProformaInvoice/` — Proforma Invoice (PI)
**Owner authorization:** 2026-11-13 — implement the proforma invoice module
completely (reference steps 1–9, merged to 7 stages in this application), using
V3's own domain, persistence, presentation and Theme Studio architecture.
No legacy application code or UI is copied. **Additional owner authorization:**
2026-11-14 — add customer-facing PDF export using QuestPDF, including all five
print styles; preserve one shared invoice composition across those styles.
**Migration:** 1018 on the single `inquiries.db` chain (additive).
**Verification:** portable source and contract checks (`step6.f.1`) run in this
environment and pass. The behaviour suite (`step6.f.2`, `dotnet run`) and a WPF
build have **not** been executed anywhere — there is no .NET SDK on the
authoring machine. Do not read this document as a build or runtime claim.

---

## 1. What the module does

It turns a quotation into a document a customer can act on: who is issuing it,
who it is for, what is being supplied, what it costs, what the customer pays,
what the shop earns, and what evidence is attached.

The document is stored as one row plus one JSON body. The body is the wizard's
state; the row is what the register and the inquiry cross-reference read. Both
are written in a single transaction, so a saved document can never have a body
that disagrees with its header.

## 2. Stage map — ADV's nine steps become seven

The reference wizard asks the operator to type the company (its step 1), the
client (step 2) and the project (step 3). This application already owns those
facts, in three modules, with their own registers, validation and audit. Asking
for them again would create a second version of the truth that could not be
reconciled — the exact defect the owner's instruction removes.

| V3 stage | ADV step | Holds | Source |
| --- | --- | --- | --- |
| 1 Header | 1 + 2 + 3 | Letterhead, client, project, prepared-by, inquiry link | `step4` company profile, `step2` clients, `step5` projects, `step3` users, `Inquiry` |
| 2 Pricing | 4 | Build, glass panes, spacer, interlayer, yield, margin | typed, or an imported inquiry rate |
| 3 Spec grid | 5 | Groups → W1/H1/W2/H2/qty rows | typed, or an imported BOQ |
| 4 Other charges | 6 | 17 catalogue charges, colour wastages, custom rows | derived from the grid |
| 5 Summary | 7 | Discount, VAT, grand total, terms, validity | computed |
| 6 Attachments | 8 | Files held beside the document | copied into the app data folder |
| 7 Profit & loss | 9 | Whole-sheet costing, margin, target price | computed from stage 2 + 3 |

Stage 1 is not a blank form. `InvoicePartyReader` composes it from the modules
that own the data, and stores a **snapshot** (name, TRN, address, client name,
project name, representative) beside the ids. That is deliberate: a proforma
must still print what it said when it was issued, even if the client is renamed
or the project is closed a year later. The snapshot is refreshed only by an
operator action, never silently by a later edit elsewhere.

## 3. Lifecycle

`Draft → Sent → Approved → Rejected → Expired → Cancelled`

- `Draft` is the only state whose body may change. A save against an issued
  document is refused with a message naming the state, never silently ignored.
- `Cancelled` is terminal — no transition leaves it.
- A rejected document is not deleted; it goes back to `Draft` for revision, or
  to `Cancelled`.
- A revision of an approved document is a **new draft with a new number**
  (`DuplicateAsync`), not an edit of the issued one.
- The transition table (`InvoiceLifecycle`) is the single authority. The
  repository consults it and does not carry its own copy of the rules.

**Numbering.** `PI-YYYY-NNN`, allocated once, on first save, inside the writing
connection, from the highest sequence already stored for that year — never from
a row count, so a cancelled document's number is never handed out again. The
database enforces uniqueness; a concurrent allocation retries (five attempts)
and then reports, rather than inventing a duplicate.

## 4. Money

- Currency is held as `decimal`, rounded to 2 places, half away from zero, in one
  place (`MoneyRounding`). Areas are rounded to 4 places.
- Waterfall: **net = glass + charges → discount (capped at net) → VAT 5% on the
  discounted subtotal → grand total**.
- Discount is either a percentage or a fixed amount; the percentage is capped at
  100% and the amount at the net, so a negative grand total is impossible.
- A grand total can also be set from outside (`SetTargetGrandTotal`); the engine
  reverse-solves the discount and refuses if the target is below zero.
- VAT is a stored value per document (default 5%, the UAE standard rate). It is
  not read from a global mutable setting at print time.
- Nothing in this module formats a currency string for storage: the body keeps
  numbers, the view formats them.

## 5. Pricing (stage 2)

Four builds, the trade's own vocabulary:

| Build | Panes | Quoted rate per m² |
| --- | --- | --- |
| SGU | 1 | cutting + tempering |
| DGU | 2 | (outer + inner) ÷ yield + spacer |
| LAM | 2 | (outer + inner) ÷ yield + interlayer sell + cutting + tempering |
| DGU+LAM | 3 | (outer + middle + inner) ÷ yield + spacer + interlayer sell + cutting + tempering |

- `(Σ pane rates ÷ yield + production) × (1 + margin%)`.
- `DGU+LAM` is matched **before** `DGU`, exactly, so the most expensive build can
  never be silently read as the cheaper one.
- A yield outside 0.10–1.00 falls back to 1.00 **and adds a warning** before the
  substitution. A silent fallback was the reference's worst failure mode: the
  price looked fine and was wrong.
- A build with no spacer or interlayer carries no rate for one, so stage 7 can
  never cost a row the job does not consume.
- **Provenance.** Each group keeps its own `SpecCostCard`: pane rates, the
  build, the sheet size, and three flags recording that a rate was typed by
  hand. Re-pricing the section therefore cannot erase a deliberate cost, and
  stage 7 costs each group from its own card — never from whichever section
  happens to be selected in the view.
- A missing pane rate marks the group incomplete instead of pricing it as zero.
- DGU deliberately does **not** add pane cutting/tempering a second time: the legacy v2.0.15 DGU estimator uses only the two pane rates, yield and spacer. The V3 regression test supplies non-zero cutting/tempering inputs and asserts the quoted DGU rate remains unchanged.
- The specification name is **generated** from the build and the panes
  (`DGU 6mm Clear FT / 6mm Clear FT + 12mm Air`), never typed, so a spec cannot
  disagree with its own price.

## 6. Spec grid and charges (stages 3–4)

- A group has dimensions W1/H1/W2/H2 and a quantity; a leg is present when both
  of its dimensions are positive, which covers a single sheet, an L-shape and a
  four-leg unit without a second row model.
- Minimum billable panel is 0.5 m² per piece, matching the reference; sheets
  needed is never zero.
- Wastage rows are **derived from the grid's colours**, one per glass colour in
  use, and rebuilt whenever the grid changes. A colour used on two panes counts
  twice — that is how much glass the job buys.
- A manually added wastage row survives the rebuild, and a manual on/off switch
  (`HasManualActiveOverride`) is never overridden by automation.
- 17 catalogue charges (cutting, polishing, holes, cutouts, delivery, install,
  crane, silicone, mirror, tempering, transport, …) with metric keys
  (`TotalSqm`, `TotalLm`, `TotalPieces`, `FixedQuantity`), a stated quantity
  source, and a stored rate.
- Automation (`UseAdvancedAutomation`) may: switch on argon and the silicon bed
  for DGU/DGU+LAM; detect U-INSERT, OVERLAP (LM or SQM), SHUTTER BONDING,
  FANHOLE, CUTOUT, HOLES, SANDBLAST/FROSTED and MITRE/BEVELED when explicitly
  named in the specification; and follow `SamePaneColours` for polishing. Matched double-glass polish uses
  measured W1/H1 and W2/H2 geometry (and counts W1 twice when W2/H2 is absent),
  rather than multiplying the whole grid length blindly. The per-group `PolishSubType` selector is now ported with all legacy edge
  formulas, spec-name detection and a Custom/manual linear-metre field. It never infers
  drawing-dependent holes/cutouts from dimensions, and delivery remains an
  operator-entered decision. U-Insert has an explicit Step 4 per-spec toggle and rate captured on each
  specification; the invoice-level Step 6 catalogue rate is only the fallback
  when that seed is blank. Its billable area is the first-pane/cavity area,
  not both DGU panes.
- Wastage rows remain aggregated by colour in V3. Clear-glass wastage is marked
  optional with a reason because offcuts may be reusable; coloured/coated glass
  explains why offcuts are less reusable. The editable sheet quantity is read by
  stage 7 even when the wastage charge itself is inactive: it is a production
  fact, not a billing one.

## 7. Profit & loss (stage 7)

Costed from whole sheets, not from sell area, because that is what the shop buys:

- Rows: glass, spacer, interlayer, cutting, tempering — each with the area or
  sheet count and the cost rate that produced it.
- Wastage allowance defaults to 10% and is overridable; the operator's sheet
  quantity wins when given.
- Margin is measured against the **net revenue** (grand total less VAT), so VAT
  passing through the business never flatters a job.
- The target-price KPI compares the quoted rate with a target build-up and
  tolerates 1% so rounding does not raise a false alarm.
- Warnings start at 5% margin (below) and 10% (thin); neither blocks a save — the
  owner decides, the document remembers.

## 8. Persistence contract

- Same database file: `%APPDATA%/ProGlassAutomation/.../inquiries.db`.
- Migration **1018** adds two tables (`ProformaInvoices`,
  `ProformaInvoiceAttachments`) and their indexes, and asserts their shape. It
  does not alter, drop, rename or seed anything, and it does not touch another
  module's table.
- Authorization: the actor comes from `AuthenticationContext.Current` — a stable
  user id **and** the name as it was at the time. The Windows login
  (`CurrentOperator`) is never the author. A session that cannot be identified
  is refused, not defaulted.
- Optimistic concurrency: `RowVersion` is compared on save; a stale edit is
  refused with a message rather than overwriting a colleague's work.
- The inquiry link (`Inquiries.ProformaInvoiceId`) is set **by calling the
  Inquiry module's own method after the document commits**. A failure there is a
  warning on a saved document, not a reason to lose the document. The module
  writes no inquiry row itself.
- Attachment policy: the file is copied to a temporary name, moved into place,
  and only then is the row written; the row is committed before the file is
  removed. A crash therefore leaves either a file with a row, or an orphan file
  that a later cleanup can find — never a row pointing at nothing.
- Attachments are capped at 20 MB, stored under generated names, and resolved
  with a path-traversal guard.

## 9. Where this differs from the ADV reference

| Reference | Here | Why |
| --- | --- | --- |
| 9-step wizard, company/client/project typed in | 7 stages, header composed from modules that own it | one version of the truth |
| Sheet Store catalogue resolves pane prices | rate typed or imported; optional purchase rate | no Sheet Store module exists; nothing invents a price |
| Per-build panels priced from a catalogue hash | per-group `SpecCostCard` with provenance flags | a re-price must not erase a deliberate cost |
| JSON snapshot in `SavedProformaInvoice`, `SchemaVersion 2` | JSON body + typed row, `FormatVersion 1` | the register can query without parsing every body |
| Number from a sequence table | highest stored sequence for the year | a cancel must not free a number |
| Wizard UI, cards, plates | native seven-stage V3 WPF workspace using Theme Studio tokens | fresh V3 presentation; no legacy XAML or card templates copied |
| Print/PDF in step 7 | one V3 QuestPDF composition with five selectable print styles | explicitly authorized 2026-11-14; no legacy renderer or UI is copied |

## 10. PDF export — owner authorized 2026-11-14

The Summary stage offers a PDF style selector and an **Export PDF** action. It
exports a saved invoice only, so the document number is permanent before a
customer-facing file is created.

The five styles are:

| Style | Legacy layout kind | Operator label | Character |
| --- | --- | --- | --- |
| Classic Navy | FormalBand | EXECUTIVE | Formal commercial letterhead |
| Modern Teal | SplitConsole | MODERN | Airy contemporary quote |
| Editorial Ink | Editorial | EDITORIAL | Warm paper and restrained rules |
| Industrial Steel | Workshop | INDUSTRIAL | Dense, fully gridded production copy |
| Minimal Ivory | QuietForm | MINIMAL | Quiet archive-style whitespace |

These are five treatments of one invoice structure, not five divergent
document architectures. Every style prints the same company identity, invoice
reference and dates, client/project details, specification groups and measured
rows, active other charges, financial waterfall, notes, terms and approval
signatures. A separate full-dossier PDF uses the same QuestPDF engine and
selected style, adding the KPI band and the detailed working information.
The dossier excludes internal P&L by default. It can be explicitly opted in
for internal circulation only; that version carries a red warning banner on
every page. The customer invoice PDF never includes P&L.

QuestPDF is the only PDF renderer. V3 did not previously reference it; the
approved integration adds QuestPDF 2025.12.4 to the existing application
project. The export service renders a detached invoice snapshot off the UI
thread, writes the selected PDF path, and reports viewer-launch failures
separately from file-generation failures. Company logos are resolved through
the owning Company Profile logo store and respect None, LeftPortrait and
TopPanel placement plus the saved logo size. Company representative details
are printed from the invoice snapshot. No PdfSharp renderer or second PDF
engine is introduced.

The package is configured for QuestPDF Community licensing. Confirm the
organisation's eligibility and any commercial licensing requirement before
distribution. PDF generation and visual parity still require Windows build and
runtime verification; a source commit is not proof of a rendered PDF.

## 11. File import and spreadsheet/CSV export

The Grid stage imports cut-list data into the selected specification group:

- **XLSX:** reads the first worksheet's used range using ClosedXML, including a workbook currently open in Excel.
- **CSV and TXT:** header-aware comma, semicolon and tab parsing; decimal-comma values are supported in semicolon-delimited files.
- **Scanner JSON:** accepts the structured row array used by scanner integrations.
- **Row rules:** supports REF, W1, H1, optional W2/H2, quantity, dimension-pair shorthand, specification and glass type. Invalid dimensions/quantities are skipped and reported. Imports append to the selected group or map rows to existing groups by specification and glass type. Unmatched or ambiguous groups are rejected before any rows are changed.

PDF/image OCR is **not included** in this V3 batch: the V3 application has no Gemini file-AI service or AI settings contract. The import service reports this clearly instead of pretending to scan the file. Adding an AI provider requires a separate owner decision and credential/settings design.

The Summary stage exports:
- **Excel workbook:** Summary, Specifications, Other Charges and Terms sheets. The internal P&L and target-analysis sheets are omitted by default and appear only when explicitly enabled.
- **CSV cut list:** the 13-column legacy-compatible cut-list contract, with per-panel area and total area kept at three decimals.
- **Register Excel:** exports the currently filtered invoice register.

ClosedXML 0.105.0 is the only added spreadsheet dependency. CSV is written by the .NET runtime; CsvHelper is not added. The optional internal P&L workbook sheet is marked red and must not be sent to customers.

## 12. Files

| File | Role |
| --- | --- |
| `step6.a.1.InvoiceLifecycle.cs` | statuses, transitions, stage enum, editability |
| `step6.a.2.MoneyRounding.cs` | the one home for money/area rounding and parsing |
| `step6.a.3.GlassMeasure.cs` | panel area, leg classification, sheet counting, wastage clamp |
| `step6.a.4.InvoiceParties.cs` | header snapshot model |
| `step6.a.5.PricingModels.cs` | build vocabulary, panes, cost card, sections |
| `step6.a.6.PricingEstimator.cs` | the four formulas, provenance, warnings |
| `step6.a.7.SpecGrid.cs` | groups, rows, areas, colour areas and polish metrics |
| `step6.a.8.ChargeCatalog.cs` | the 17 templates and their metrics |
| `step6.a.9.OtherCharges.cs` | charge sheet, per-colour wastage rebuild, clear-glass guidance, sheet-cost quantity |
| `step6.a.10.OtherChargeAutomation.cs` | what may be suggested from the specification |
| `step6.a.11.InvoiceSummary.cs` | discount, VAT, totals waterfall, target total |
| `step6.a.12.ProfitLoss.cs` | whole-sheet costing, margin, target KPI |
| `step6.a.13.ProformaInvoiceDraft.cs` | the document body, structure, recalculation, clone |
| `step6.a.14.ProformaInvoiceRules.cs` | normalization, validation, stage evaluation, text limits |
| `step6.a.15.PolishSubType.cs` | all legacy per-edge polish modes, detection and geometry formulas |
| `step6.a.16.WastageChargeRow.cs` | colour wastage allowance, optimization formula and whole-sheet cost quantity |
| `step6.b.1.ProformaInvoiceSchema.cs` | migration 1018, shape assertion, numbering |
| `step6.b.2.ProformaInvoiceJson.cs` | body serialization and its refusal rules |
| `step6.b.3.ProformaInvoiceRecord.cs` | the stored row |
| `step6.b.4.IProformaInvoiceRepository.cs` | the contract and the query/result types |
| `step6.b.5.ProformaInvoiceDbContext.cs` | the module's tables and mapping |
| `step6.b.6.InvoiceAccess.cs` | who is writing, and the refusal of an anonymous session |
| `step6.b.7.ProformaInvoiceRepository.cs` | save, numbering, concurrency, inquiry link |
| `step6.b.8.ProformaInvoiceRepository.Reads.cs` | query, get, suggest number, restore, file resolution |
| `step6.b.9.ProformaInvoiceRepository.Lifecycle.cs` | status change and revision-by-copy |
| `step6.b.10.InvoicePartyReader.cs` | stage 1 from the owning modules |
| `step6.b.11.InquiryBoqConverter.cs` | inquiry BOQ → spec grid |
| `step6.b.12.ProformaInvoiceAttachmentStore.cs` | attachment add/remove, temp→move→row |
| `step6.c.1.ProformaInvoiceWorkspace.cs` | the stage engine the page drives |
| `step6.c.Presentation/step6.c.2…c.9` | `ProformaWorkspaceViewModel` parts: register, editor/header, stages, commands, recalculation, lifecycle and PDF export |
| `step6.d.Views/step6.d.0.ProformaStyles.xaml` | the page's field idiom — labels, values, totals; tokens only |
| `step6.d.Views/step6.d.1.ProformaWorkspaceView.xaml` | the page: scope plate, status chips, search, register, editor slot |
| `step6.d.Views/step6.d.2.ProformaEditorView.xaml` | the editor: heading, lifecycle commands, stage rail, stage host |
| `step6.d.Views/step6.d.3…d.9` | one view per stage — header, pricing, spec grid, charges, summary, documents, profit |
| `step6.f.tests/step6.f.1…` | portable source gate |
| `step6.f.tests/step6.f.2…f.8` | domain, storage, PDF, P&L, import/export, charge-automation and polish-formula regression checks |
| `step6.g.Export/step6.g.1.ProformaPdfStyle.cs` | five named print styles, one shared document composition |
| `step6.g.Export/step6.g.2.ProformaPdfLayout.cs` | QuestPDF invoice rendering, pagination, tables and signatures |
| `step6.g.Export/step6.g.3.ProformaPdfExporter.cs` | detached-snapshot invoice and dossier PDF export |
| `step6.g.Export/step6.g.4.ProformaDossierPdfLayout.cs` | full dossier; P&L opt-in, internal warning on every page |
| `step6.g.Export/step6.g.5.ProformaWorkbookExporter.cs` | invoice/dossier workbook and filtered register workbook |
| `step6.g.Export/step6.g.6.ProformaCsvExporter.cs` | 13-column cut-list CSV |
| `step6.g.Export/step6.g.7.ProformaPdfLetterheadRenderer.cs` | profile-owned logo placement and company identity |
| `step6.h.Import/step6.h.1.ProformaGridImportParser.cs` | header-aware CSV/TSV/TXT and scanner JSON parser |
| `step6.h.Import/step6.h.2.ProformaGridImportService.cs` | XLSX/CSV/TXT/JSON file reader; shared-read file access |
| `step6.c.Presentation/step6.c.10…FileOperations.cs` | selected-group import, workbook/CSV/register export commands |
| `step6.f.tests/step6.f.6…` | import, workbook and CSV regression checks |
| `step6.c.Presentation/step6.c.9…PdfExport.cs` | style selection, save dialog and export command |
| `step6.f.tests/step6.f.2…f.5` | behaviour, storage, five-style PDF render and P&L checks (needs .NET 8) |

## 13. Verification status — honest

| Claim | Evidence |
| --- | --- |
| Source rules hold (no delete, no foreign writes, additive migration, one rounding home, VAT after discount, four formulas, wastage from colours, P&L provenance, no WPF types, rule K) | `step6.f.1` — executed here, passes |
| Migration 1018 is additive and registered | `step1.d.22` — executed here, passes (tip 1018) |
| No step6 file grows past 400 lines | `step1.d.19` — executed here, passes |
| Behaviour (exact totals, transitions, numbering, round-trip, attachments) | written in `step6.f.2`; **not executed** — no .NET SDK here |
| Application builds | **not verified** — no .NET SDK or WPF runtime here |
| Migration runs on a real copy of production | **not verified**; run `step5.f.2` on a copy first (see `step6.e.2`) |
| Views follow the theme contract (tokens only, implicit styles bound explicitly, shared `ListGrid`) | `step1.d.3`, `.17`, `.18`, `.20`, `.11` — executed here, pass; the step6 views are our own XAML, no reference markup copied |
| Views render correctly on Windows in nine themes | **not verified** — the XAML is written and compiles by inspection only; no WPF runtime here to render it |
| Five-style QuestPDF invoice and dossier export | source and render checks committed; **not built or rendered** here — verify on Windows, including logo, long tables, page breaks, P&L opt-in and all five styles |
| XLSX/CSV/TXT/JSON cut-list import and XLSX/CSV export | parser, service, workbook and UI wiring committed; **not executed** here — verify on Windows with representative files and Excel-open workbook locks |

Anyone reading a later report should treat the last three rows as open until a
person runs them on Windows and records the outcome in
`step6.e.2.Owner-Verification.md`.
