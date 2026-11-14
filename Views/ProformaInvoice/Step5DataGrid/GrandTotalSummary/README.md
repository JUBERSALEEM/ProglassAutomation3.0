# Grand Total Summary

<!-- pg-nav -->
> **[Repository root](../../../../README.md)** · [All documents](../../../../docs/README.md) · [Application workflow](../../../../docs/WORKFLOW.md) · [Design system](../../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Read-only per-spec rollup section at the bottom of Step 5 (Data Grid)
of the Proforma Invoice wizard.

## Purpose

Each Spec Group in Step 5 has its own footer showing that group's totals
(qty, sqm, lm, value). But when a user builds a proforma with multiple spec
groups, they need to see the combined picture across ALL groups at a glance.

This section provides that summary: one row per spec group showing that
group's totals, followed by a highlighted Grand Total row aggregating
everything.

This creates a clear rollup before the user proceeds to Step 6 (Other Charges),
without needing to mentally add up per-group footers.

## What This Is NOT

This is NOT the final invoice grand total. It intentionally excludes:

- Other Charges (Phase 7.11) — Argon Gas, Silicon Bed, Polish, Cutouts, etc.
- Discount (Phase 7.12) — Percentage or flat amount deduction
- VAT 5% (Phase 7.12) — UAE tax
- Final Grand Total (Phase 7.12) — After all above additions/deductions

Those values live in the separate Invoice Main Summary module built
after Other Charges is complete. This section only rolls up raw DataGrid
data — nothing more.

## Placement in Workflow

    Step 5: DATA GRID
    ├── Spec Group 1 (with own footer: qty, sqm, lm, value)
    ├── Spec Group 2 (with own footer: qty, sqm, lm, value)
    ├── Spec Group N ...
    ├── [+ Add New Spec Group button]
    └── GRAND TOTAL SUMMARY  ← this module
        ├── Per-spec breakdown row (one per group)
        └── Grand total row (aggregates all groups)

## Design

- Read-only — user cannot edit values here, only view
- Auto-updates — reflects any change in any spec group in real time
- Self-contained sub-feature — lives in its own folder per project convention
- Zero coupling to Other Charges / VAT / Discount — those come later

## Files in This Folder

| File                              | Purpose                                       |
|-----------------------------------|-----------------------------------------------|
| `GrandTotalSummaryView.xaml`      | UI card with per-spec table + grand row       |
| `GrandTotalSummaryView.xaml.cs`   | Code-behind (partial class only)              |
| `README.md`                       | This documentation                            |

Related files live at:

- `ViewModels/ProformaInvoice/Step5DataGrid/GrandTotalSummary/GrandTotalSummaryViewModel.cs`
- `Models/ProformaInvoice/Step5DataGrid/GrandTotalSummary/SpecGroupSummaryRow.cs`

## Data Flow

    SpecGroup.FooterTotalQty     ─┐
    SpecGroup.FooterTotalSqm     ─┤
    SpecGroup.FooterTotalLm1     ─┤─► GrandTotalSummaryViewModel.Refresh()
    SpecGroup.FooterTotalValue   ─┤              │
    SpecGroup.SelectedGlassType  ─┤              ▼
    SpecGroup.SelectedSpecName   ─┘      SummaryRows + Grand Totals
                                                  │
                                                  ▼
                                      GrandTotalSummaryView (UI)

### How Change Tracking Works

1. `DataGridViewModel` owns `ObservableCollection<SpecGroup> SpecGroups`.
2. It creates ONE `GrandTotalSummaryViewModel` and passes the collection.
3. The summary VM subscribes to two things:
   - `SpecGroups.CollectionChanged` — for add/remove/move of groups
   - Each `SpecGroup.PropertyChanged` — for footer total or label changes
4. On any relevant change, `Refresh()` runs.
5. `Refresh()` is cheap because footer totals are already cached on each
   SpecGroup (from Phase 7.10b/c performance work).

### What Triggers a Refresh

| User Action                                | Triggers Refresh?      |
|--------------------------------------------|------------------------|
| Add a new spec group                       | Yes                    |
| Remove a spec group                        | Yes                    |
| Add a row inside any spec group            | Yes (via footer cache) |
| Remove a row from any spec group           | Yes                    |
| Edit W1 / H1 / W2 / H2 / Qty in any row    | Yes                    |
| Change Base Rate or Surcharge %            | Yes                    |
| Change Glass Type dropdown                 | Yes (label update)     |
| Change Specification dropdown              | Yes (label update)     |
| Paste 150 rows from Excel                  | Yes (batched, once)    |
| Rename internal GroupName field only       | No (not displayed)     |
| Scroll / focus / hover                     | No                     |

## Display Format

### Per-Spec Row Example

    [1]  DGU · 6mm HD Grey FT Glass    55 pcs   172.660 sqm   697.05 lm   AED 25,898.85

| Column     | Source                          | Style                        |
|------------|---------------------------------|------------------------------|
| #          | `SpecGroup.GroupNumber`         | White text on accent badge   |
| Spec Group | `GlassType · SpecName`          | Primary foreground, ellipsis |
| Qty        | `SpecGroup.FooterTotalQty`      | Primary foreground           |
| Total SQM  | `SpecGroup.FooterTotalSqm`      | Blue (#3B82F6)               |
| Total LM   | `SpecGroup.FooterTotalLm1`      | Purple (#A855F7)             |
| Value      | `SpecGroup.FooterTotalValue`    | Green (#22C55E), bold        |

### Grand Total Row Example (highlighted band)

    🏆  GRAND TOTAL    85 pcs   316.960 sqm   1397.50 lm   AED 47,468.00

- Full accent brush background
- All text in white
- Trophy emoji icon on the left
- Slightly larger font for emphasis
- Sums all values from the per-spec rows above

### Label Format Logic

The "Spec Group" column shows `{GlassType} · {SpecName}` where:

- GlassType — SGU, DGU, LAM, or DGU+LAM (from dropdown)
- SpecName — user's chosen or typed specification name

If either is empty, safe fallbacks are used:

- Missing GlassType → `—`
- Missing SpecName → `Spec {N}` (where N is the group number)

Examples:

| Selected Values                                | Displayed Label              |
|------------------------------------------------|------------------------------|
| GlassType=DGU, SpecName="6mm HD Grey FT Glass" | `DGU · 6mm HD Grey FT Glass` |
| GlassType=SGU, SpecName=""                     | `SGU · Spec 1`               |
| GlassType="", SpecName=""                      | `— · Spec 2`                 |

### Empty State

When zero spec groups exist:

> No spec groups yet. Add a spec group above to see totals here.

Shown centered in italic secondary-foreground text.

## Color Coding Rationale

| Metric        | Color         | Reason                              |
|---------------|---------------|-------------------------------------|
| Total SQM     | Blue          | Distinguishes area from length      |
| Total LM      | Purple        | Distinguishes length from area      |
| Value         | Green         | Money — consistent with row values  |
| Grand Total   | Accent brush  | Draws eye to the summary conclusion |
| Group # badge | Accent brush  | Matches spec group header badges    |

Consistent colors let the user's eye track the same metric across rows
without re-reading column labels.

## Why This Is Its Own Sub-Folder

Follows the project convention:

> Main files stay flat, features get sub-folders.

Since this is a distinct sub-feature of Step 5 (DataGrid) with its own
model, view-model, and view, placing it in
`Step5DataGrid/GrandTotalSummary/` keeps the DataGrid folder clean and
lets future sub-features (e.g. `Step5DataGrid/ExcelImportExport/`) be
added without clutter.

## API Usage from DataGridViewModel

The summary VM is instantiated once in `DataGridViewModel`'s constructor
and exposed as a bindable property:

    public GrandTotalSummaryViewModel SummaryViewModel { get; }

    public DataGridViewModel()
    {
        SpecGroups = new();
        // ... other setup ...

        // Wire the summary after spec groups exist
        SummaryViewModel = new GrandTotalSummaryViewModel(SpecGroups);
    }

The view is embedded at the bottom of `DataGridView.xaml`:

    <summary:GrandTotalSummaryView DataContext="{Binding SummaryViewModel}"/>

No further wiring needed. The summary auto-refreshes based on collection
and property change events.

## Performance Notes

- Cached footer values — Summary reads `FooterTotalQty`, `FooterTotalSqm`,
  etc. from SpecGroup. Those are already cached during Phase 7.10b/c work,
  so no recalculation happens here.
- Guarded property changes — Summary only reacts to specific property
  names (footer totals + label fields). Other property changes (scroll,
  focus, etc.) are ignored via a `PropertyName` check.
- Batched paste updates — During Excel paste (150+ rows), SpecGroup
  sets `_isBulkAdding = true` which suppresses per-row events. The summary
  refreshes once after the batch completes.
- Single-pass loop — `Refresh()` uses one `foreach` over SpecGroups.
  No LINQ, no repeated enumeration.

## Testing Checklist

- [ ] Add spec groups — each appears as a row
- [ ] Remove a spec group — row disappears, grand total updates
- [ ] Add rows / edit dimensions — per-spec row updates, grand total updates
- [ ] Empty spec group (0 rows) still shows with zeros
- [ ] Change GlassType or SpecName — label updates in real time
- [ ] Paste 150 rows into a spec group — summary updates once (batched)
- [ ] Rename GroupName alone — no summary refresh (perf guard)
- [ ] Grand Total values match sum of visible per-spec rows
- [ ] Currency always AED, formatted with thousand separators
- [ ] Empty state shows when zero spec groups exist

## Future Enhancements

Ideas for later phases:

- Collapsible view — Toggle to show/hide the per-spec breakdown, keeping
  only the grand total visible.
- Copy summary to clipboard — Right-click → "Copy Summary as Text"
  to paste into emails or notes.
- Export summary to Excel — Standalone summary export separate from
  the full proforma export.
- Chart view toggle — Pie chart of value distribution across spec groups.
- Highlight biggest contributor — Bold the spec group with highest value.
- Compare to previous invoice — Diff badge showing % change vs last saved.

## Related Modules

| Module                             | Relationship                                       |
|------------------------------------|----------------------------------------------------|
| Step 5: Data Grid                  | Parent — this summary lives inside it              |
| Step 6: Other Charges (Phase 7.11) | Consumer — will read Grand Total as starting point |
| Invoice Main Summary (Phase 7.12)  | Uses this + Other Charges + Discount + VAT         |
| Specifications Repository          | Source of dropdown values that build labels        |

<!-- pg-nav -->
---

*This README documents `Views/ProformaInvoice/Step5DataGrid/GrandTotalSummary/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../../docs/README.md).*
<!-- pg-nav -->
