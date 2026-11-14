# Saved Invoices List — Phase 7.16

<!-- pg-nav -->
> **[Repository root](../../../README.md)** · [All documents](../../../docs/README.md) · [Application workflow](../../../docs/WORKFLOW.md) · [Design system](../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Full-screen module + toolbar dialog for browsing, loading, duplicating,
and deleting previously saved proforma invoices from proformainvoices.db.

## Purpose

Phase 7.15 gave us Save. Phase 7.16 closes the round-trip with Load.

Without this, saved invoices go into a "black hole" — persisted to
disk but unreachable from the UI. This module adds:

- Browse all saved invoices in a searchable table
- Filter by status, date range, or company/customer/project text search
- Sort by newest, oldest, highest value, alphabetical
- Load any invoice → rehydrates the entire wizard (all 9 steps incl. Step 8 Attachments + Step 9 P&L)
- Duplicate an invoice with auto-incremented ID
- Delete invoices permanently
- Right-click to change status (Draft → Sent → Approved etc.)

## Files in This Folder

| File                                | Purpose                                      |
|-------------------------------------|----------------------------------------------|
| SavedInvoicesListView.xaml          | Full-screen module UI                        |
| SavedInvoicesListView.xaml.cs       | Code-behind (partial class only)             |
| SavedInvoicesListDialog.xaml        | Modal dialog wrapping the same view          |
| SavedInvoicesListDialog.xaml.cs     | Dialog code-behind (returns selected Id)     |
| README.md                           | This documentation                           |

Related files:

- ViewModels/ProformaInvoice/SavedInvoicesList/SavedInvoicesListViewModel.cs
- Models/ProformaInvoice/SavedInvoice/SavedInvoiceListItem.cs
- Services/ProformaInvoice/ProformaInvoiceRepository/ProformaInvoiceMapper.cs
  (extended with RestoreWizardFromSnapshot)

## Access Points

Users can reach this UI two ways:

### 1. Toolbar Dialog (from Step 7)

Click the 📂 LOAD INVOICE button on the Print Preview toolbar.
Opens a modal window with the same list UI embedded.
When user clicks LOAD on a row, dialog closes and the wizard is
rehydrated with that invoice's data.

### 2. Sidebar Module (from Main Menu)

Full-screen module accessible from the main app sidebar
(wired in MainViewModel navigation — see Phase 7.16 sidebar changes).

## UI Layout

Top to bottom:

1. Header (title + subtitle)
2. Stat cards: Total Invoices | Showing | Total Value + Refresh button
3. Filter row: Search | Status | From Date | To Date | Sort By | Clear
4. Table with 8 columns:
   - REF #        (invoice ProformaId, accent color, bold)
   - COMPANY      (company name)
   - CUSTOMER     (customer name)
   - PROJECT      (project name)
   - STATUS       (color-coded pill: Draft=gray, Sent=blue, Approved=green,
                   Rejected=red, Expired=amber)
   - GRAND TOTAL  (money in green, right-aligned)
   - DATE         (created date, muted)
   - ACTIONS      (Load / Duplicate / Delete)

Empty state:

> 📋 No saved invoices found
> Try adjusting filters or create a new invoice in the Proforma Invoice module.

## Data Flow

    proformainvoices.db  →  App.Invoices.GetAllAsync()
        →  SavedInvoicesListViewModel._allItems (List<SavedInvoiceListItem>)
        →  ApplyFilters()  →  FilteredItems (ObservableCollection)
        →  UI table rows

    User clicks LOAD →  OnInvoiceLoadRequested callback
        →  Dialog closes with SelectedInvoiceId set
        →  PrintableInvoiceViewModel.ExecuteLoadInvoice() fetches full entity
        →  ProformaInvoiceMapper.DeserializeSnapshot(entity)
        →  ProformaInvoiceMapper.RestoreWizardFromSnapshot(snapshot, VMs...)
        →  All 7 wizard steps populated with saved data

## Load-with-Unsaved-Changes Handling

Before opening the picker, PrintableInvoiceViewModel checks if the
current wizard has meaningful data (company name filled, customer name,
or any spec group rows). If yes, user gets a 3-button prompt:

- YES  → save current invoice first, THEN open picker
- NO   → discard current data and open picker
- CANCEL → return to editing

## Restore Logic Highlights

RestoreWizardFromSnapshot() handles each wizard section:

- Step 1-3: simple property assignments (Company / Project / Client)
- Step 5:   clears SpecGroups, rebuilds from snapshot using bulk-load
            (SetAllSilent) to avoid per-row event storms
- Step 6:   matches predefined charges by TemplateKey and updates
            IsActive/Rate/SelectedSource. For Polish rows, restores the
            enum-typed sub-type. Wastages and CustomCharges are cleared
            and rebuilt.
- Phase 7.12: parses DiscountType enum, sets discount + VAT settings
- Phase 7.14: clears Clauses, rebuilds from snapshot, preserves enum types
- Phase 7.13: assigns Prepared By / Terms / Footer text

## Actions

### 📂 LOAD
Restores the invoice into the current wizard.
See "Load-with-Unsaved-Changes Handling" above.

### 📋 DUPLICATE
Confirms with user, then:
1. Fetches original entity
2. Generates a new ProformaId via SuggestNextIdAsync
3. Copies all fields + FullSnapshotJson
4. Sets Status = "Draft", IssueDate = now, ValidUntil = today + 7 days
5. Inserts as new row
6. Refreshes the list

### 🗑 DELETE
Confirms with user, then removes the row via DeleteAsync.
Refreshes the list.

### Right-click → Change Status
Opens context menu with 5 status options (Draft / Sent / Approved /
Rejected / Expired). Selecting one triggers ChangeStatusCommand which
updates the entity and refreshes the list.

## Testing Checklist

- [ ] Open Step 7 → click LOAD INVOICE → dialog appears
- [ ] Dialog shows all saved invoices in date-desc order
- [ ] Search "PI-2026" → filters to matching rows
- [ ] Filter Status = "Sent" → shows only Sent invoices
- [ ] Set From/To date range → filters correctly
- [ ] Sort by "Highest Value" → largest grand totals first
- [ ] Clear button resets all filters
- [ ] Click LOAD on a row → wizard populated with that data
- [ ] Load with unsaved changes → 3-button prompt appears
- [ ] Click DUPLICATE → confirms → new invoice with next-ID created
- [ ] Click DELETE → confirms → row disappears from list
- [ ] Right-click row → Change Status → Approved → row status updates
- [ ] Stat cards update: Total / Showing / Total Value
- [ ] Empty state shows when no invoices match filters

## Future Enhancements

- Phase 7.17 — QuestPDF PDF export
- Phase 7.18 — Excel/CSV Import/Export
- Phase 7.19 — Version history per invoice
- Phase 7.20 — Send Email with PDF attachment
- Multi-select for bulk operations (delete N, change status of N)
- Advanced filters (grand-total range, has-argon, etc.)
- Export the list itself to Excel for reporting
- Column customization (show/hide/reorder)

<!-- pg-nav -->
---

*This README documents `Views/ProformaInvoice/SavedInvoicesList/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../docs/README.md).*
<!-- pg-nav -->
