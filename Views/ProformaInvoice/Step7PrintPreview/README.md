# Step 7 - Summary (Print Preview & Review)

<!-- pg-nav -->
> **[Repository root](../../../README.md)** · [All documents](../../../docs/README.md) · [Application workflow](../../../docs/WORKFLOW.md) · [Design system](../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
The financial summary step of the Proforma Invoice wizard. Reviews all
invoice data, computes the totals waterfall, and provides the print /
export surface.

> Wizard is 9 steps today: Step 7 (this) → **Step 8 Attachments** →
> **Step 9 Profit & Loss** (the single pricing engine whose snapshot feeds
> Job Profitability / Reports — see `Services/ProformaInvoice/` and
> `../../../docs/ERP_UPGRADE_ROADMAP.md`).

## Purpose

Step 7 is where the user reviews everything before generating the
final printed PI. It aggregates:

- Step 5 DataGrid subtotal (raw glass materials)
- Step 6 Other Charges subtotal (fabrication, delivery, wastages, custom)
- User-entered Discount (% or AED flat)
- UAE VAT (5% default, toggleable)

And produces the **Grand Total (VAT Inclusive)** — the final number
printed on the proforma invoice.

## Phase Rollout

- Phase 7.12  Invoice Main Summary (financial waterfall card)      <-- YOU ARE HERE
- Phase 7.13  Terms & Conditions section
- Phase 7.14  Print Preview layout (matches PDF)
- Phase 7.15  Save PI to database
- Phase 7.16  Load saved PI list
- Phase 7.17  Excel/CSV Import/Export
- Phase 7.18  PDF generation (QuestPDF)

## Files in This Folder

| File                              | Purpose                              |
|-----------------------------------|--------------------------------------|
| PrintPreviewView.xaml             | Main Step 7 view                     |
| PrintPreviewView.xaml.cs          | Code-behind                          |
| README.md                         | This documentation                   |
| InvoiceMainSummary/               | Financial totals sub-feature         |

Related files:

- ViewModels/ProformaInvoice/Step7PrintPreview/PrintPreviewViewModel.cs
- ViewModels/ProformaInvoice/Step7PrintPreview/InvoiceMainSummary/InvoiceMainSummaryViewModel.cs
- Models/ProformaInvoice/Step7PrintPreview/InvoiceMainSummary/DiscountType.cs

## Invoice Main Summary (Phase 7.12)

The financial waterfall card at the top of Step 7. Data flow:

    Step 5 DataGrid.SummaryViewModel.GrandTotalValue  ─┐
                                                        │
    Step 6 OtherChargesVM.TotalOtherCharges          ─┤─► InvoiceMainSummaryVM
                                                        │
                                              User inputs (Discount + VAT)
                                                        │
                                                        ▼
                                          Live-computed totals shown on UI

### Calculation Waterfall

    Glass Materials Total (Ex VAT)     [from Step 5]
  + Other Processing/Delivery Total    [from Step 6]
  ────────────────────────────────
  = Estimated Net Subtotal

  - Discount                           [user: % of subtotal OR flat AED]
  ────────────────────────────────
  = Subtotal Before VAT

  + UAE VAT (5.0%)                     [user: editable %, toggle on/off]
  ════════════════════════════════
  = TOTAL EST VALUE (VAT Inclusive)   ← final printed grand total

### Discount Rules

- **Percentage**: `Discount = NetSubtotal * (DiscountValue / 100)`
- **Fixed Amount**: `Discount = DiscountValue`
- Discount is **capped at NetSubtotal** — subtotal can never go negative
- Discount can be 0 (default) — no discount applied
- Negative discount values are clamped to 0

### VAT Rules

- Default VAT rate: **5%** (UAE standard)
- Editable per invoice (in case of future rate changes)
- Checkbox **"UAE VAT Applicable"** allows disabling VAT for exempt clients
- When disabled: `VatAmount = 0` and `GrandTotal = SubtotalBeforeVat`

## Change Tracking

InvoiceMainSummaryViewModel auto-recalculates when:

| Trigger                                                | Effect                       |
|--------------------------------------------------------|------------------------------|
| Step 5 GrandTotalValue changes                         | GlassMaterialsSubtotal updates |
| Step 6 TotalOtherCharges changes                       | OtherChargesSubtotal updates |
| User edits DiscountValue or changes DiscountType       | Recalculate                  |
| User edits VatPercent                                  | Recalculate                  |
| User toggles IsVatApplicable                           | Recalculate                  |

All changes propagate to the "Total Est Value" band in real time.

## Testing Checklist

- [ ] Empty invoice → all values show AED 0.00
- [ ] Add rows in Step 5 → Glass Materials updates in Step 7
- [ ] Enable Argon in Step 6 → Other Charges updates in Step 7
- [ ] Type 10 in Discount (% mode) → Discount = 10% of Net Subtotal
- [ ] Switch to Fixed Amount → same value now means AED 10 flat
- [ ] Set Discount > Subtotal → Discount capped at subtotal, Grand Total = 0
- [ ] Toggle VAT off → VAT line shows AED 0.00, Grand Total = Subtotal Before VAT
- [ ] Change VAT % to 10 → VAT recalculates
- [ ] Rate/dimension edits in Step 5 → cascade all the way to Grand Total

## Future Enhancements

- Terms & Conditions text editor (Phase 7.13)
- PDF preview iframe (Phase 7.14)
- Print button (Phase 7.14)
- Save as PDF (Phase 7.18 via QuestPDF)

<!-- pg-nav -->
---

*This README documents `Views/ProformaInvoice/Step7PrintPreview/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../docs/README.md).*
<!-- pg-nav -->
