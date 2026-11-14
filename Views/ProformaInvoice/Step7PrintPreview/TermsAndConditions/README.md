# Terms & Conditions — Phase 7.14

<!-- pg-nav -->
> **[Repository root](../../../../README.md)** · [All documents](../../../../docs/README.md) · [Application workflow](../../../../docs/WORKFLOW.md) · [Design system](../../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Editable Terms and Conditions section shown on the proforma invoice.
Ships with 6 default UAE glass industry clauses that users can edit,
reorder, disable, or replace.

## Purpose

Every proforma invoice needs clear terms of service. Real-world
UAE glass industry PIs typically include:

1. Payment terms with multiple installment lines
2. Delivery timeline
3. Warranty coverage
4. Quotation validity window
5. Tax treatment (5% UAE VAT)
6. Installation scope

Phase 7.14 provides a structured, editable clause system that
handles all of these while allowing custom additions.

## Files in This Folder

| File                             | Purpose                                   |
|----------------------------------|-------------------------------------------|
| TermsAndConditionsView.xaml      | Editor panel UI (above A4 preview)        |
| TermsAndConditionsView.xaml.cs   | Code-behind (partial class only)          |
| README.md                        | This documentation                        |

Related files:

- ViewModels/ProformaInvoice/Step7PrintPreview/TermsAndConditions/TermsAndConditionsViewModel.cs
- Models/ProformaInvoice/Step7PrintPreview/TermsAndConditions/TermClauseType.cs
- Models/ProformaInvoice/Step7PrintPreview/TermsAndConditions/TermClause.cs
- Models/ProformaInvoice/Step7PrintPreview/TermsAndConditions/TermsAndConditionsDefaults.cs

## Architecture

    PrintPreviewViewModel (Step 7 host)
    +-- InvoiceMainSummary  (Phase 7.12)
    +-- Terms               (Phase 7.14 — this module)
    +-- PrintableInvoice    (Phase 7.13 — receives Terms reference)

The Terms VM lives on the Step 7 host and is passed by reference into
the PrintableInvoice VM. When user edits a clause, both the editor
panel and the A4 preview update live via shared binding.

## Default Clauses

| # | Type          | Default Content                                              |
|---|---------------|--------------------------------------------------------------|
| 1 | PaymentTerms  | 50% advance + 50% before dispatch (with sub-line bullets)    |
| 2 | Delivery      | 3-4 weeks from PO confirmation                               |
| 3 | Warranty      | 12 months manufacturing defects only                         |
| 4 | Validity      | 7 days (user picks 2-7 via dropdown)                         |
| 5 | Taxes         | 5% UAE VAT inclusive                                         |
| 6 | Installation  | Not included unless specified                                |

Custom clauses are always added at the end and titled
"Custom Clause N".

## User Actions

| Action                | Effect                                                    |
|-----------------------|-----------------------------------------------------------|
| Uncheck IsActive      | Clause hidden from printed invoice (kept in editor)       |
| Edit Title textbox    | Updates the numbered header (e.g. "1. PAYMENT TERMS")     |
| Edit Body textbox     | Multi-line body text of clause                            |
| Move up / down arrows | Reorders clauses (auto-renumbers)                         |
| ✕ Delete              | Removes clause permanently                                |
| + ADD LINE            | (Payment Terms only) Adds a bullet sub-line               |
| Validity dropdown     | (Validity clause only) Picks 2-7 days, auto-updates body  |
| + ADD CUSTOM CLAUSE   | Appends a new blank clause of Type=Custom                 |
| ⟲ RESET TO DEFAULTS   | Wipes all clauses and restores the 6 UAE defaults         |

## Data Flow to Printable Preview

    User edits clause in TermsAndConditionsView
        v
    TermClause.Title / Body / SubLines / ValidityDays PropertyChanged fires
        v
    ObservableCollection<TermClause>.Clauses updates
        v
    Next print or preview refresh:
        PrintableInvoiceViewModel.BuildTermsAndConditionsSection()
        walks Terms.GetActivePrintableClauses() and renders each clause
        with the current Order, Title, Body, and SubLines

## Printable Rendering

For each active clause, the invoice paper shows:

    1. PAYMENT TERMS
        Payment is required as per the following schedule:
        • 50% advance upon PO confirmation
        • 50% balance before dispatch from factory
        • All payments in AED via bank transfer or cheque

    2. DELIVERY
        Delivery within 3-4 weeks from PO confirmation...

Body text is indented 12 DIPs from the clause title. Sub-line
bullets are indented 24 DIPs with accent-colored bullet marks.

## Testing Checklist

- [ ] Open Step 7 -> Terms section shows 6 default clauses
- [ ] Edit clause 1 title -> preview updates live
- [ ] Uncheck clause 3 (Warranty) -> disappears from preview
- [ ] Change Validity dropdown from 7 to 3 -> body auto-updates to "3 days"
- [ ] Add a payment sub-line -> bullet appears in preview
- [ ] Delete a payment sub-line -> bullet disappears from preview
- [ ] Move clause 5 (Taxes) up -> renumbering reflects in both editor + preview
- [ ] + ADD CUSTOM CLAUSE -> new blank clause appears numbered next
- [ ] ⟲ RESET TO DEFAULTS -> back to 6 original clauses (custom clauses lost)
- [ ] Print invoice -> all active clauses appear in the printed page

## Future Enhancements

- Global default template that persists across app restarts (Phase 7.15+)
- Multiple named templates (Standard / Fast-Track / Government / etc.)
- Rich text formatting (bold, italic, underline) via RichTextBox
- Import/export T&C templates as JSON or XML
- Multi-language support (English / Arabic side-by-side)

<!-- pg-nav -->
---

*This README documents `Views/ProformaInvoice/Step7PrintPreview/TermsAndConditions/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../../docs/README.md).*
<!-- pg-nav -->
