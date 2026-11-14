# Step 6 - Other Charges

<!-- pg-nav -->
> **[Repository root](../../../README.md)** · [All documents](../../../docs/README.md) · [Application workflow](../../../docs/WORKFLOW.md) · [Design system](../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Supplementary charges section of the Proforma Invoice wizard.
Captures fabrication, processing, and delivery charges that get
added on top of the DataGrid (Step 5) glass material subtotal.

## Purpose

DataGrid (Step 5) only prices the raw glass panels. Real proforma
invoices include additional service charges like:

- Argon gas fill inside DGU cavity
- Silicon bed sealant for double glazing
- Overlap edge stepping
- Structural shutter bonding
- Beveled mitring cuts
- Polish (with sub-types)
- Cutouts (water-jet)
- Holes (CNC drilled)
- Glass color wastage allowances (added in Phase 7.11c)

Each charge is calculated as: Quantity from DataGrid x Rate = Amount

## Phase Rollout

- Phase 7.11a  Metric Discovery Hub + basic charges (Argon, Silicon, Overlap, Shutter Bonding, Mitring)
- Phase 7.11b  Polish sub-types + Cutout/Holes                            <-- YOU ARE HERE
- Phase 7.11c  Wastages with color-based auto-aggregation across spec groups
- Phase 7.11d  Custom manual charges + full integration into Invoice Main Summary

## What This Is NOT

This is NOT the final invoice total. It intentionally excludes:

- Discount    (Phase 7.12)
- VAT 5%      (Phase 7.12)
- Grand Total (Phase 7.12)

The Total Other Charges shown here feeds INTO the Invoice Main
Summary module which is built after all Other Charges phases are done.

## Files in This Folder

| File                          | Purpose                                    |
|-------------------------------|--------------------------------------------|
| OtherChargesView.xaml         | Main UI - Metric Hub + charges table       |
| OtherChargesView.xaml.cs      | Code-behind (partial class only)           |
| README.md                     | This documentation                         |

Related files live at:

- ViewModels/ProformaInvoice/Step6OtherCharges/OtherChargesViewModel.cs
- ViewModels/ProformaInvoice/Step6OtherCharges/MetricDiscoveryHubViewModel.cs
- Models/ProformaInvoice/Step6OtherCharges/OtherChargeUnit.cs
- Models/ProformaInvoice/Step6OtherCharges/GridSourceOption.cs
- Models/ProformaInvoice/Step6OtherCharges/OtherChargeRow.cs
- Models/ProformaInvoice/Step6OtherCharges/PredefinedChargeTemplates.cs
- Models/ProformaInvoice/Step6OtherCharges/PolishSubType.cs

## Architecture

Step 6 is fully driven by live data from Step 5 (DataGrid).
No standalone database or service layer needed.

    ProformaInvoiceViewModel
    |
    +-- Step5VM (DataGridViewModel)   ---> owns SpecGroups
    |
    +-- Step6VM (OtherChargesViewModel)
           |
           +-- takes Step5VM reference in constructor
           |
           +-- MetricDiscoveryHubViewModel
           |     subscribes to Step5VM.SpecGroups
           |     aggregates: LM1, LM2, LM1+LM2, SQM1, SQM2, SQM1+SQM2,
           |                 Total Pieces, Double Glass, DGU Cavity Area
           |
           +-- AllSources (pool of GridSourceOption)
           |     kept in sync with MetricHub via PropertyChanged
           |
           +-- ChargeRows (predefined templates)
                 each row has AvailableSources filtered by Unit type
                 Polish rows have AvailablePolishSubTypes instead
                 each row calculates ResultAmount = MetricQty x Rate
                 Total = sum of active rows' ResultAmount

## Polish Charge (Phase 7.11b)

Polish is a SPECIAL row — it does NOT use the AvailableSources dropdown.
Instead, it uses AvailablePolishSubTypes, and Metric Qty is calculated
by iterating every glass item row across all spec groups.

### Polish Sub-Types

| Sub-Type                      | Formula                       | Notes                          |
|-------------------------------|-------------------------------|--------------------------------|
| All Side Polish               | (W+H)/1000 x 2 x Qty          | Full perimeter, same as LM1    |
| Both Height Side              | H/1000 x 2 x Qty              | Heights only, both sides       |
| Both Width Side               | W/1000 x 2 x Qty              | Widths only, both sides        |
| All Side Polish As Per DWG    | (W+H)/1000 x 2 x Qty          | Same as All Side + DWG note    |
| Custom (Manual)               | User types description + qty  | Free-text + manual entry       |

### Polish LM Calculation

For each glass row across all spec groups:

- If SGU: apply formula to (W1, H1) only
- If DGU/LAM: apply formula to BOTH (W1, H1) AND (W2, H2), then sum
- Multiply by Qty

Total Polish LM = sum of all rows.

Example — 5 rows in a DGU spec group, "All Side Polish":

    Row 1: W1=3000 H1=1250, W2=2900 H2=1150, Qty=10
      Glass 1: (3000+1250)/1000 * 2 = 8.5
      Glass 2: (2900+1150)/1000 * 2 = 8.1
      Row LM: (8.5 + 8.1) * 10 = 166 lm
    ...

### Custom Polish

When user selects "Custom (Manual)", the row expands to show:

- Custom Description text input (e.g. "Special water-jet polish")
- Manual Qty input (user types the LM value directly)

The formula display then reads:

    12.50 LM x 15.00 AED = 187.50 AED (SPECIAL WATER-JET POLISH)

## Cutout / Holes (Phase 7.11b)

Both use the same math and same source options (Total Pieces or Double Glass).
User chooses which one applies based on business rule for that invoice.

- SGU: 1 piece = 1 hole/cutout   -> pick "Total Pieces"
- DGU/LAM: 1 unit = 2 glasses    -> pick "Double Glass (1+1)"

Rates are user-editable per invoice (defaults: Cutout=50, Holes=15).

## Metric Discovery Hub

Top section of Step 6 shows live stats aggregated from all Spec Groups.
Values auto-refresh whenever DataGrid changes.

| Metric                 | Source                             | Unit |
|------------------------|------------------------------------|------|
| Total Glass 1 (LM1)    | Sum of Lm1 across all groups       | lm   |
| Total Glass 2 (LM2)    | Sum of Lm2 across all groups       | lm   |
| Combined Glass (LM)    | LM1 + LM2                          | lm   |
| Total Pieces           | Sum of Qty across all groups       | pcs  |
| Double Glass (1+1)     | Qty x 2 for DGU/LAM, x1 for SGU    | pcs  |
| Area Glass 1 (SQM1)    | Sum of TotalSqm across all groups  | sqm  |
| Area Glass 2 (SQM2)    | Sum of TotalSqm2 across all groups | sqm  |
| Combined Area (SQM)    | SQM1 + SQM2                        | sqm  |
| DGU Cavity Area        | SQM1 for DGU groups only           | sqm  |
| Primary Glass Refs     | Unique spec names from all groups  | list |

## Charge Rows (Phase 7.11a + 7.11b)

| Charge          | Unit | Default Rate | Default Source     | Notes           |
|-----------------|------|--------------|--------------------|-----------------|
| Argon Gas       | Sqm  | 45.00        | SQM1               |                 |
| Silicon bed     | Lm   | 6.00         | LM1                |                 |
| Overlap         | Lm   | 6.00         | LM1                |                 |
| Shutter Bonding | Lm   | 25.00        | LM1 + LM2          |                 |
| Mitring         | Lm   | 30.00        | LM1                |                 |
| Polish          | Lm   | 6.00         | (sub-type driven)  | Special row     |
| Cutout          | Qty  | 50.00        | Total Pieces       |                 |
| Holes           | Qty  | 15.00        | Total Pieces       |                 |

Each row has:

- Active checkbox (uncheck = row grayed out, excluded from total)
- Charge name + description
- Target Grid Source dropdown (or Polish sub-type for Polish row)
- Metric Qty (auto-populated, read-only except Custom Polish)
- Rate (editable per invoice)
- Formula & Result display

## Grid Source Filtering

Dropdown options are filtered by the charge's Unit type so users
cannot pick incompatible sources.

- Sqm charges see: SQM1, SQM2, SQM1+SQM2, DGU Cavity Area
- Lm  charges see: LM1, LM2, LM1+LM2
- Qty charges see: Total Pieces, Double Glass (1+1)

## Formula Display

Format: {MetricQty} {UNIT} x {Rate} AED = {Total} AED ({HINT})

Examples:

    303.33 LM x 6.00 AED = 1,819.98 AED (W1 H1 PERIMETER (LM1))
    179.52 LM x 6.00 AED = 1,077.12 AED (ALL SIDE POLISH)
    88.20 LM x 6.00 AED = 529.20 AED (BOTH HEIGHT SIDE)
    12.50 LM x 15.00 AED = 187.50 AED (SPECIAL WATER-JET POLISH)

When a row is unchecked, display shows: DISABLED

## Change Tracking

The VM subscribes to:

- MetricHub.PropertyChanged -> refresh source values + total
- SpecGroups.CollectionChanged -> hook/unhook groups
- Each SpecGroup.PropertyChanged (SelectedGlassType) -> recalc polish
- Each SpecGroup.ItemRows.CollectionChanged -> hook/unhook rows
- Each GlassItemRow.PropertyChanged (W1/H1/W2/H2/Qty) -> recalc polish

Polish recalculation walks every glass row across every spec group.
This is O(rows) but only triggered when dimensions actually change,
and dimensional edits are always followed by a single batched update
thanks to SpecGroup's _isBulkAdding flag from Phase 7.10b/c.

## Testing Checklist

- [ ] Metric Hub shows correct totals matching DataGrid footers
- [ ] Adding a spec group updates all metric cards
- [ ] Editing a row dimension updates metrics live
- [ ] Argon Gas: check + rate 45 -> correct formula
- [ ] Silicon bed dropdown offers LM1/LM2/LM1+LM2 only
- [ ] Polish: check + "All Side Polish" -> LM matches LM1 (for SGU)
- [ ] Polish: check + "Both Height Side" -> LM = H/1000*2*Qty summed
- [ ] Polish: check + DGU spec group -> LM sums Glass 1 + Glass 2 perimeters
- [ ] Polish: pick "Custom (Manual)" -> extra inputs appear
- [ ] Custom Polish: type description + qty -> formula uses custom text
- [ ] Cutout: check + Total Pieces -> qty matches Total Pieces
- [ ] Cutout: check + Double Glass -> qty matches Double Glass count
- [ ] Holes: same behavior as Cutout
- [ ] Unchecking any row shows DISABLED and excludes from Total
- [ ] Total Other Charges = sum of active rows only

## Future Phases

- Phase 7.11c - Wastages with color-based auto-aggregation across spec groups
- Phase 7.11d - Custom manual charges section (free-form line items)
- Phase 7.12  - Invoice Main Summary (Discount + VAT + Grand Total)
- Phase 7.13  - Print Preview / PDF layout
- Phase 7.14  - Save PI to database (Other Charges become part of PI record)

<!-- pg-nav -->
---

*This README documents `Views/ProformaInvoice/Step6OtherCharges/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../docs/README.md).*
<!-- pg-nav -->
