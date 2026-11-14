# Proforma Invoice Repository

<!-- pg-nav -->
> **[Repository root](../../../README.md)** · [All documents](../../../docs/README.md) · [Application workflow](../../../docs/WORKFLOW.md) · [Design system](../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Persistent storage layer for saved proforma invoices.
Uses a dedicated SQLite database file separate from the master-data and
specifications databases.

## Purpose

The entire wizard state across all **9 steps** persists as a single JSON
snapshot (`ProformaInvoiceSnapshot` ⇄ `ProformaInvoiceMapper`), so users can:

1. Save an in-progress proforma and resume later
2. Load an existing invoice to reuse it as a template
3. Update saved invoices (e.g. change price, status)
4. Browse a searchable list of all past proformas (Phase 7.17)

Phase 7.15 implements the SAVE half of this flow. The LOAD/LIST/RESTORE
functionality is added in Phase 7.17.

## Database Design

- **Separate SQLite file:** `%AppData%\ProGlassAutomation\proformainvoices.db`
- **Isolated from main proglass.db and specifications.db** so invoices can be:
  - Backed up independently
  - Exported/imported without touching inventory or spec library
  - Reset without losing sheet stock or saved specs
- Auto-created on first app run via `InitializeAsync()`

## Schema

**Table: ProformaInvoices**

| Column                    | Type              | Description                          |
|---------------------------|-------------------|--------------------------------------|
| Id                        | INTEGER PK        | Auto-increment primary key           |
| ProformaId                | TEXT (max 50)     | User-facing ID, e.g. "PI-2026-001" — UNIQUE |
| CompanyName               | TEXT (max 200)    | For list search filtering            |
| CustomerName              | TEXT (max 200)    | For list search filtering            |
| ProjectName               | TEXT (max 200)    | For list search filtering            |
| Status                    | TEXT (max 50)     | Draft / Sent / Approved / etc.       |
| IssueDate                 | DATETIME          | Invoice issue date                   |
| ValidUntil                | DATETIME          | Quotation expiry date                |
| GlassMaterialsSubtotal    | DECIMAL(18,2)     | For reports without deserializing    |
| OtherChargesSubtotal      | DECIMAL(18,2)     | For reports                          |
| DiscountAmount            | DECIMAL(18,2)     | For reports                          |
| VatAmount                 | DECIMAL(18,2)     | For reports                          |
| GrandTotalVatInclusive    | DECIMAL(18,2)     | For reports                          |
| FullSnapshotJson          | TEXT              | Serialized entire wizard state       |
| CreatedAt                 | DATETIME          | When first saved                     |
| UpdatedAt                 | DATETIME          | Last modification                    |

**Indexes:**

- `IX_ProformaId` UNIQUE — Enforces unique invoice IDs
- `IX_CompanyName` — Fast search in Load List UI
- `IX_CustomerName` — Fast search in Load List UI
- `IX_Status` — Filter by draft/sent/approved
- `IX_CreatedAt` — Sort by most recent

## Why JSON Blob Instead of Fully Relational?

Phase 7.15 uses a hybrid approach:

- **Top-level searchable fields** (ProformaId, CompanyName, financials)
  are surfaced as real indexed columns
- **The entire wizard state** is serialized as JSON into FullSnapshotJson

Benefits:

- Fast to build (no 7+ EF entity tree with foreign keys)
- Schema evolution is trivial (add fields to ProformaInvoiceSnapshot freely)
- Save/Load are single-table operations
- Fully supports whole-invoice roundtripping

Trade-off:

- Deep queries like "all invoices with Argon Gas active" require
  deserializing every row. If this becomes needed, we can migrate to
  full relational tables in a future phase.

## Snapshot Structure

ProformaInvoiceSnapshot captures all wizard state:

    ProformaInvoiceSnapshot
    ├── Company         (CompanySnapshot)      Step 1 fields
    ├── Project         (ProjectSnapshot)      Step 2 fields
    ├── Client          (ClientSnapshot)       Step 3 fields
    ├── SpecGroups[]    (SpecGroupSnapshot)    Step 5 spec groups
    │    └── ItemRows[] (GlassItemRowSnapshot) glass panel rows
    ├── OtherCharges    (OtherChargesSnapshot) Step 6 wrapper
    │    ├── PredefinedCharges[] (PredefinedChargeSnapshot)
    │    ├── Wastages[]           (WastageRowSnapshot)
    │    └── CustomCharges[]      (CustomChargeSnapshot)
    ├── Summary         (InvoiceSummarySnapshot) Phase 7.12 Discount + VAT
    ├── TermClauses[]   (TermClauseSnapshot)     Phase 7.14 T&C
    └── PreparedBy      (PreparedBySnapshot)     Phase 7.13 signature block

All snapshot classes are POCOs — no WPF types, no ObservableCollections,
no ViewModelBase inheritance — so they serialize cleanly with
System.Text.Json.

## Duplicate ProformaId Handling

When user clicks SAVE and the ProformaId already exists, the app:

1. Detects the conflict via ExistsByProformaIdAsync()
2. Shows a 3-button dialog:
   - **Update Existing** — overwrite the existing invoice
   - **Save as New (PI-2026-002)** — auto-suggests next available ID
   - **Cancel** — return to editing

The SuggestNextIdAsync() method parses the trailing number in any
ProformaId format and increments until an unused value is found.

Examples:

- "PI-2026-001" → "PI-2026-002"
- "INVOICE-42"  → "INVOICE-43"
- "ABC"         → "ABC-001" (no digits → appends -001)

## API Usage

From ViewModels:

    private readonly IProformaInvoiceRepository _repo = new ProformaInvoiceRepository();

    // Startup — once
    await _repo.InitializeAsync();

    // Save
    var entity = ProformaInvoiceMapper.BuildEntityFromWizard(
        step1, step2, step3, step5, step6, summary, terms, printable);

    if (await _repo.ExistsByProformaIdAsync(entity.ProformaId))
    {
        // prompt user: update or auto-increment
        await _repo.UpdateAsync(entity);
    }
    else
    {
        await _repo.InsertAsync(entity);
    }

    // Load
    var loaded = await _repo.GetByProformaIdAsync("PI-2026-001");
    var snapshot = ProformaInvoiceMapper.DeserializeSnapshot(loaded);

    // List (Phase 7.17)
    var all = await _repo.GetAllAsync();

## Startup Initialization

Register in App.xaml.cs startup flow, alongside Specs repository:

    App.Invoices = new ProformaInvoiceRepository();
    await App.Invoices.InitializeAsync();

The database file and tables are created automatically on first run.

## Files in This Folder

| File                              | Purpose                                          |
|-----------------------------------|--------------------------------------------------|
| ProformaInvoicesDbContext.cs      | EF Core DbContext with own SQLite connection     |
| IProformaInvoiceRepository.cs     | Repository interface                             |
| ProformaInvoiceRepository.cs      | EF Core implementation                           |
| ProformaInvoiceMapper.cs          | VM ↔ Snapshot ↔ Entity conversion                |
| README.md                         | This documentation                               |

Related model files:

- Models/ProformaInvoice/SavedInvoice/SavedProformaInvoice.cs (EF entity)
- Models/ProformaInvoice/SavedInvoice/ProformaInvoiceSnapshot.cs (JSON DTO)

## User-Facing Save Flow

When user clicks the 💾 SAVE INVOICE button on Step 7:

1. Validation — rejects save if ProformaId is empty (prompts user to
   fill in Step 1 first)
2. Snapshot build — walks all 7 step VMs via ProformaInvoiceMapper
3. Duplicate check — queries ExistsByProformaIdAsync()
4. **If unique**: InsertAsync() creates a new row, shows success message
5. **If duplicate**: 3-button dialog:
   - YES → UpdateAsync() overwrites the existing row
   - NO  → SuggestNextIdAsync() finds next free ID (e.g. PI-2026-002),
           updates Company.ProformaId in Step 1, then InsertAsync()
   - CANCEL → returns to editing, no changes saved
6. Success feedback — MessageBox confirms save/update with final ID

## Testing Checklist

- [ ] First-time app launch creates proformainvoices.db in AppData
- [ ] Clicking SAVE with empty ProformaId shows validation warning
- [ ] Clicking SAVE with fresh ID → creates row, shows success
- [ ] Clicking SAVE again with same ID → shows 3-button prompt
- [ ] Clicking YES → row is updated, UpdatedAt timestamp changes
- [ ] Clicking NO → new row created with auto-incremented ID
- [ ] Clicking CANCEL → no database change
- [ ] Step 1 ProformaId reflects the new auto-incremented value
- [ ] Financial columns match wizard totals after save
- [ ] FullSnapshotJson can be manually inspected in SQLite browser
- [ ] Deleting proformainvoices.db and restarting app recreates it

## Future Enhancements

- Phase 7.17 — Load Saved Invoice List UI with search + filter
- Phase 7.17 — Restore path: Snapshot → VMs rehydration
- Phase 7.18 — Export invoices to Excel/CSV bulk
- Phase 7.19 — Full relational migration if deep queries needed
- Phase 7.19 — Version history (each save creates new version)
- Phase 7.20 — Cloud sync backup

<!-- pg-nav -->
---

*This README documents `Services/ProformaInvoice/ProformaInvoiceRepository/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../docs/README.md).*
<!-- pg-nav -->
