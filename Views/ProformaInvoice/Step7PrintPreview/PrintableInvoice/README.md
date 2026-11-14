# Printable Invoice — Phase 7.13

<!-- pg-nav -->
> **[Repository root](../../../../README.md)** · [All documents](../../../../docs/README.md) · [Application workflow](../../../../docs/WORKFLOW.md) · [Design system](../../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Full A4 white-paper preview of the proforma invoice, rendering data
from every previous wizard step in a printable layout.

## Purpose

Users need a print-ready visual of the invoice BEFORE hitting Print
or Save. This view aggregates data from Steps 1-6 plus the financial
summary (Phase 7.12) into a single scrollable A4-sized preview that:

- Simulates real A4 paper (white background, subtle shadow)
- Prints directly via WPF PrintDialog (also works as Print-to-PDF)
- Contains inline-editable Terms text and Prepared By fields
- Serves as the design blueprint for future QuestPDF export (Phase 7.16)

## Layout Sections (top to bottom)

1. Company Header (from Step 1) — brand block + Proforma badge with Ref/Date
2. Client + Project Blocks (from Steps 2, 3) — side-by-side info panels
3. Specification Tables (from Step 5) — one table per spec group with rows and sub-total
4. Other Charges Detail List (from Step 6) — active predefined + wastages + custom lines
5. Financial Summary Waterfall (from Phase 7.12) — subtotal, discount, VAT, grand total
6. Terms + Footer Disclaimer — editable inline
7. Prepared By + Client Signature — signature blocks

## Files in This Folder

| File                                | Purpose                                          |
|-------------------------------------|--------------------------------------------------|
| PrintableInvoiceView.xaml           | A4 paper canvas — all sections in one document   |
| PrintableInvoiceView.xaml.cs        | Wires the PrintablePaper element to the VM       |
| README.md                           | This documentation                               |

Related files:

- ViewModels/ProformaInvoice/Step7PrintPreview/PrintableInvoice/PrintableInvoiceViewModel.cs
- Converters/DecimalIsPositiveConverter.cs (used for conditional Subtotal Before VAT display)

## Architecture

    PrintPreviewViewModel (Step 7 host)
    +-- InvoiceMainSummaryViewModel  (Phase 7.12 — financial card)
    +-- PrintableInvoiceViewModel    (Phase 7.13 — A4 preview)
          |
          +-- Company        (Step 1 VM reference)
          +-- Project        (Step 2 VM reference)
          +-- Client         (Step 3 VM reference)
          +-- DataGrid       (Step 5 VM reference)
          +-- OtherCharges   (Step 6 VM reference)
          +-- MainSummary    (Phase 7.12 VM reference)
          |
          +-- PreparedByName / Dept / Status (editable)
          +-- TermsText / FooterDisclaimer   (editable)
          |
          +-- Commands: Print / ExportPdf / SaveInvoice / SendEmail

All step data is bound directly via property paths (e.g.
`Company.CompanyName`, `DataGrid.SpecGroups`) — no copy is made.
This keeps the preview live: any change in previous steps
appears here immediately.

## Print Function

The PrintCommand uses WPF's built-in `PrintDialog.PrintVisual()` on the
`PrintablePaper` Border element. On button click:

1. Print dialog opens with system printers listed
2. User can pick any physical printer OR "Microsoft Print to PDF"
3. `PrintVisual()` renders the A4 paper element exactly as displayed

Note: Full QuestPDF branded export coming in Phase 7.16 for automated
PDF file generation without user interaction.

## Editable Fields

| Field                   | Default Value                                        |
|-------------------------|------------------------------------------------------|
| PreparedByName          | Lead Glass Estimator                                 |
| PreparedByDept          | ProGlass QA & Estimations Desk                       |
| PreparedByStatus        | APPROVED                                             |
| TermsText               | Standard warranties apply to manufactured glass ...  |
| FooterDisclaimer        | Confidential Document generated automatically ...    |

These are the only fields user can edit inside the preview.
Everything else is derived from earlier wizard steps.

## Design Notes

- Fixed A4 width (794 px @ 96 DPI, portrait)
- White background with subtle drop shadow (feels like real paper)
- All fonts sized for print readability (8-13 pt)
- Colors use HEX values compatible with print (not dark-theme resources)
- Section headers use accent purple (#4F46E5) to match brand
- Money values use green (#059669) for positive, red (#DC2626) for discount
- Table headers use dark navy (#1F2937) with white text

## Testing Checklist

- [ ] All 7 wizard steps have data → preview shows correctly
- [ ] Empty invoice (no spec groups) → preview shows headers only, no crash
- [ ] Add spec group in Step 5 → new spec table appears in preview
- [ ] Toggle Argon in Step 6 → charge appears/disappears in preview
- [ ] Change discount in Step 7 summary → cascades to preview totals
- [ ] Edit PreparedByName inline → updates immediately
- [ ] Edit TermsText inline → updates immediately
- [ ] Click Print → PrintDialog opens
- [ ] Print to "Microsoft Print to PDF" → saves PDF file successfully
- [ ] Grand total in preview matches Grand total in summary card

## Future Enhancements

- Phase 7.14 — Multi-page pagination (currently one long page)
- Phase 7.15 — Save Invoice: persist current state to database
- Phase 7.16 — Export PDF: QuestPDF branded PDF generation
- Phase 7.17 — Load saved PI: rehydrate all step VMs from database
- Phase 7.18 — Excel/CSV Import/Export
- Phase 7.20 — Send Email: attach PDF and send via SMTP

<!-- pg-nav -->
---

*This README documents `Views/ProformaInvoice/Step7PrintPreview/PrintableInvoice/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../../docs/README.md).*
<!-- pg-nav -->
