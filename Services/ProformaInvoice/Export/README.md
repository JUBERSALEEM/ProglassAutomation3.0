# ProformaInvoice Export — Phase 9.5b

<!-- pg-nav -->
> **[Repository root](../../../README.md)** · [All documents](../../../docs/README.md) · [Application workflow](../../../docs/WORKFLOW.md) · [Design system](../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Shared export engine integration for the Proforma Invoice module.
Part of the Phase 9.5 export engine migration (strangler pattern).

## Files

    Services/ProformaInvoice/Export/
        ProformaInvoicePdfLayout.cs     implements IPdfLayout
        ProformaInvoiceExcelLayout.cs   implements IExcelLayout
        ProformaInvoiceCsvLayout.cs     implements ICsvLayout
        README.md                       this file

## Usage — ViewModel

    // PDF
    var layout  = new ProformaInvoicePdfLayout(entity, snapshot);
    var engine  = new PdfExportEngine();
    var request = new ExportRequest { PromptForLocation = true, OpenAfterSave = true };
    var result  = await engine.ExportAsync(layout, request);
    if (!result.Success && !result.Cancelled)
        MessageBox.Show($"Export failed: {result.ErrorMessage}");

    // Excel
    var layout  = new ProformaInvoiceExcelLayout(entity, snapshot);
    var engine  = new ExcelExportEngine();
    var result  = await engine.ExportAsync(layout, request);

    // CSV
    var layout  = new ProformaInvoiceCsvLayout(entity, snapshot);
    var engine  = new CsvExportEngine();
    var result  = await engine.ExportAsync(layout, request);

    // Print (physical printer — Phase 9.5b Q2 addition)
    var layout  = new ProformaInvoicePdfLayout(entity, snapshot);
    var engine  = new PdfExportEngine();
    await engine.PrintAsync(layout, $"Proforma Invoice - {entity.ProformaId}");

## Constructors

Both constructors accept the same two parameters:

    ProformaInvoicePdfLayout(SavedProformaInvoice entity, ProformaInvoiceSnapshot snapshot)
    ProformaInvoiceExcelLayout(SavedProformaInvoice entity, ProformaInvoiceSnapshot snapshot)
    ProformaInvoiceCsvLayout(SavedProformaInvoice entity, ProformaInvoiceSnapshot snapshot)

The layouts work in two call paths:

Path A — Wizard export (PrintableInvoiceViewModel):
    Build entity + snapshot from live wizard VMs via ProformaInvoiceMapper,
    then pass to layout constructor.

Path B — List export (SavedInvoicesListViewModel):
    Load entity from DB, deserialize snapshot via
    ProformaInvoiceMapper.DeserializeSnapshot(entity), then pass to
    layout constructor. No wizard VMs needed (preserves gotcha #7).

## Strangler Pattern

Round 1 (this commit): New layouts + rewired ViewModels.
    Legacy services still compile:
        Services/ProformaInvoice/PdfExport/ProformaInvoicePdfService.cs
        Services/ProformaInvoice/InvoiceExportImport/InvoiceExcelExportService.cs
        Services/ProformaInvoice/InvoiceExportImport/InvoiceCsvExportService.cs
    Before deleting: grep entire solution for references to these types.

Round 2 (separate commit after Round 1 confirmed working):
    Delete the 3 legacy service files above.
    Delete Services/ProformaInvoice/PdfExport/PdfExportContextBuilder.cs
    (only after confirming no remaining references).
    PdfExportContext.cs may stay as it is still referenced by
    SavedInvoicesListViewModel.BuildExportContextFromSnapshot — check grep.

## Key design decisions

- WYSIWYG (2026-09-11): the PDF is drawn by PiPreviewParityRenderer, a
  faithful QuestPDF port of the Step 7 on-screen paper
  (PrintableInvoiceView.xaml). Same section order, same labels, same column
  geometry, same logo-placement rule — and both sides read the same
  PdfTheme/PdfMetrics tokens, so preview and export cannot drift.

- One design, five styles: the old five PdfLayoutKind page architectures
  (divergent letterheads/rainbow spec headers/misaligned charge rows) are
  no longer applied to this document. The five selectable layouts are now
  professional STYLE variants of the single design — palette, margin,
  rhythm, cell padding, zebra/grid behaviour.

- Letterhead: supplied by this layout via LetterheadStyle (engine hook).
  Top-panel logos render a full-width masthead; inline logos render the
  company identity + bordered reference box, exactly like the preview.
  Continuation pages get a slim identity strip.

- Printing: PrintAsync renders this SAME layout — print, export and
  preview are one document.

- Pricing included: All rates, totals, VAT, discount, grand total shown.
  Unlike Job Order layouts which suppress pricing for factory floor.

- Grand total source: Always entity.GrandTotalVatInclusive (indexed column).
  Never recalculated from snapshot inputs. See gotcha #20 in rules.md.

- PreparedBy override pattern: Snapshot stores the resolved values
  (PreparedByName falls back to Client.DocumentPreparedBy at save time
  via ProformaInvoiceMapper). Layout reads snapshot.PreparedBy directly.

- PdfExportContext and PdfExportContextBuilder: Left untouched in Round 1.
  The new layouts use a private PiRenderContext (internal sealed class)
  instead of PdfExportContext to avoid coupling.

## PDF output structure

Page 1 (the Step 7 paper, printed):
    [Letterhead]              — TopPanel: full-width accent masthead with
                                centred logo chip, identity left, document
                                reference right; otherwise company identity
                                (+ inline logo chip) left and bordered
                                Ref/Date reference box right
    [2 DIP accent rule]
    [Client card | Project card]
    [Hairline divider]
    [Spec group banner + table + column-aligned SPEC SUB-TOTAL × N]
    [GRAND SPEC TOTAL band]
    [Other charges panel + OTHER CHARGES SUBTOTAL]
    [Financial summary waterfall + TOTAL EST VALUE band]
    [Terms & conditions panel]
    [Disclaimer + Terms line]
    [Prepared by | Client approval signature cards]

Page 2+:
    [Slim identity strip — company · document id]
    [Content continues]

Every page:
    [Engine footer: PI-YYYY-NNN · Printed timestamp    Page X of N]
    (The footer identifies the DOCUMENT only — never the layout style.)

## Excel output structure

    Sheet 1: Invoice Summary    Company + Client + Project + Financial + PreparedBy
    Sheet 2: Glass Panels       12-col table with per-spec subtotals + grand total row
    Sheet 3: Other Charges      4-col table (only if charges exist)
    Sheet 4: Terms & Conditions 3-col table (only if clauses exist)

## CSV output structure

    Row 1:    Header (12 columns)
    Rows 2-N: One row per glass panel item (all spec groups combined)
    Row N+1:  Blank separator
    Row N+2:  Glass Materials Total
    Row N+3:  Other Charges Total
    Row N+4:  GRAND TOTAL (VAT INC)

## Filename convention (ExportFileNamer)

    PI_{CustomerName}_{ProjectName}_{IssueDate:dd-MM-yyyy}.{ext}

    Example:
        PI_EMAAR-PROPERTIES_TWR-A-L15_22-07-2026.pdf
        PI_EMAAR-PROPERTIES_TWR-A-L15_22-07-2026.xlsx
        PI_EMAAR-PROPERTIES_TWR-A-L15_22-07-2026.csv

<!-- pg-nav -->
---

*This README documents `Services/ProformaInvoice/Export/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../docs/README.md).*
<!-- pg-nav -->
