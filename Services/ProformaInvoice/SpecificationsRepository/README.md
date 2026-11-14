# Specifications Repository

<!-- pg-nav -->
> **[Repository root](../../../README.md)** · [All documents](../../../docs/README.md) · [Application workflow](../../../docs/WORKFLOW.md) · [Design system](../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Persistent library of glass specifications generated from Step 4 (Pricing Estimator)
and reused in Step 5 (Data Grid) of the Proforma Invoice wizard.

## Purpose

Each time a user clicks "APPLY CALCULATED RATE TO SELECTED SPECS" in Step 4,
the current glass configuration is auto-saved as a named specification with
its per-sqm price. In Step 5, users pick from these saved specs (filtered by
glass type) to quickly build proforma invoices without re-calculating.

This creates a growing library of standard specs that speeds up future invoices
and ensures pricing consistency across projects.

## Database Design

- **Separate SQLite file:** `%AppData%\ProGlassAutomation\specifications.db`
- **Isolated from main proglass.db** so the spec library can be:
  - Exported and imported independently
  - Backed up separately from proforma data
  - Shared between multiple office installations
  - Reset without affecting historical proformas
- Auto-created on first app run via `InitializeAsync()`

## Schema

**Table: SavedSpecifications**

| Column       | Type              | Description                          |
|--------------|-------------------|--------------------------------------|
| Id           | INTEGER PK        | Auto-increment primary key           |
| GlassType    | TEXT (max 20)     | SGU / DGU / LAM / DGU+LAM            |
| SpecName     | TEXT (max 500)    | Auto-generated human-readable name   |
| PricePerSqm  | DECIMAL(18,2)     | Rate from Step 4 calculation         |
| CreatedAt    | DATETIME          | When spec was first created          |
| LastUsedAt   | DATETIME          | Last time spec was picked/updated    |
| UsageCount   | INTEGER           | How often used (smart-sort helper)   |

**Indexes:**
- `IX_GlassType` — Fast filtering in Step 5 dropdown
- `IX_SpecName` — Fast searches and duplicate detection

## Auto-Generated Spec Name Examples

| Glass Type              | Example Spec Name                                                                     |
|-------------------------|---------------------------------------------------------------------------------------|
| SGU                     | `6mm Clear FT Glass`                                                                  |
| SGU (different color)   | `8mm HD Grey FT Glass`                                                                |
| DGU                     | `6mm HD Grey FT Glass + 12mm ASP + 6mm Clear FT Glass`                                |
| DGU with U-Insert       | `6mm HD Grey FT Glass + 12mm ASP With U-Insert + 6mm Clear FT Glass`                  |
| DGU with placement      | `6mm HD Grey FT Glass + 12mm ASP With U-Insert With Both Height Side + 6mm Clear FT Glass` |
| DGU (As Per DWG)        | `6mm HD Grey FT Glass + 12mm ASP With U-Insert As Per DWG + 6mm Clear FT Glass`       |
| LAM                     | `6mm Clear FT Glass + 1.52mm Clear PVB + 6mm Clear FT Glass`                          |
| LAM with polish         | `6mm Clear FT Glass + 1.52mm Clear PVB + 6mm Clear FT Glass With All Side Polish`     |

## Deduplication Logic

When `SaveOrUpdateAsync()` is called and a spec with the same `GlassType + SpecName`
already exists:

- **PricePerSqm** is updated to the latest calculated value
- **LastUsedAt** is set to now
- **UsageCount** is incremented by 1

If no match found, a new record is created with `UsageCount = 1`.

This prevents duplicate entries while tracking how often each spec is used.

## Smart Sorting

Specs are returned from `GetAllAsync()` sorted by:

1. **UsageCount DESC** — Most-used specs float to top
2. **LastUsedAt DESC** — Recently used specs come next

This surfaces the specs the user most likely wants first, without them scrolling
through a long list.

## API Usage from ViewModels

Basic operations:

    private readonly ISpecificationsRepository _specsRepo = new SpecificationsRepository();

    // Load DGU specs for Step 5 dropdown
    var dguSpecs = await _specsRepo.GetAllAsync("DGU");

    // Load all specs (no filter)
    var allSpecs = await _specsRepo.GetAllAsync();

    // Save spec from Step 4 APPLY button
    await _specsRepo.SaveOrUpdateAsync(
        glassType: "DGU",
        specName: "6mm HD Grey FT Glass + 12mm ASP + 6mm Clear FT Glass",
        pricePerSqm: 176.23m);

    // Mark spec as used when picked in Step 5 dropdown
    await _specsRepo.MarkAsUsedAsync(specId);

    // Delete a spec (user-initiated cleanup)
    await _specsRepo.DeleteAsync(specId);

## Startup Initialization

Register in `App.xaml.cs` startup flow:

    App.Specs = new SpecificationsRepository();
    await App.Specs.InitializeAsync();

The database file and tables are created automatically on first run.

## Files in This Folder

| File                              | Purpose                                          |
|-----------------------------------|--------------------------------------------------|
| `SpecificationsDbContext.cs`      | EF Core DbContext with own SQLite connection     |
| `ISpecificationsRepository.cs`    | Repository interface (for DI/testing)            |
| `SpecificationsRepository.cs`     | EF Core implementation                           |
| `README.md`                       | This documentation                               |

Related entity model lives at:
- `Models/ProformaInvoice/Specifications/SavedSpecification.cs`

## Future Enhancements

Ideas for later phases:

- Export spec library to Excel/CSV
- Import spec library from Excel/CSV
- Bulk delete unused specs (usage count = 0, older than N days)
- Spec categories/tags for organization
- Search by partial spec name
- Rename spec (update all historical references)
- Merge duplicate specs (edge cases where names slightly differ)

<!-- pg-nav -->
---

*This README documents `Services/ProformaInvoice/SpecificationsRepository/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../docs/README.md).*
<!-- pg-nav -->
