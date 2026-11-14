# Sheet Store History

<!-- pg-nav -->
> **[Repository root](../../../README.md)** · [All documents](../../../docs/README.md) · [Application workflow](../../../docs/WORKFLOW.md) · [Design system](../../../docs/DESIGN_SYSTEM.md)
<!-- pg-nav -->
Audit trail for Sheet Store — logs every action (Add, Edit, Delete,
Buy, Use) with timestamp, user, and change details.

---

## Purpose

Answers the question **"who did what and when?"** for glass inventory.

Every mutation to `GlassSheet` records a `SheetHistory` entry
automatically. History is immutable — entries are never edited or
deleted.

---

## File Structure

```
Models/SheetStore/SheetStoreHistory/
  SheetHistory.cs                 Entity (stored in DB)

Services/SheetStore/SheetStoreHistory/
  ISheetHistoryRepository.cs      Contract
  SheetHistoryRepository.cs       SQLite implementation
  README.md                       This file

ViewModels/SheetStore/SheetStoreHistory/
  HistoryViewModel.cs             Dialog logic (filters, search, reload)

Views/SheetStore/SheetStoreHistory/
  HistoryView.xaml                Dialog UI
  HistoryView.xaml.cs             Code-behind
```

---

## Entity: SheetHistory

| Field | Type | Notes |
|---|---|---|
| Id | int | Auto PK |
| SheetId | int | Reference to `GlassSheet` (may be deleted) |
| SheetDescription | string(200) | Snapshot — survives sheet deletion |
| Action | string(20) | Added / Edited / Deleted / Bought / Used |
| Details | string(500) | Human-readable ("Stock: 50 → 70") |
| QuantityChanged | int | Sheets affected (0 for pure edits) |
| ValueChanged | decimal(18,2)? | Monetary impact in AED |
| UserName | string(100) | `Environment.UserName` |
| Timestamp | DateTime | When action occurred |

**Display helpers**: `DateDisplay`, `DayDisplay`, `TimeDisplay`,
`TimestampDisplay`, `ActionColor` (hex string for UI badge).

---

## Database

- **Table**: `SheetHistories` (in shared `proglass.db`)
- **Indexes**:
  - `IX_SheetHistory_SheetId` — per-sheet lookup
  - `IX_SheetHistory_Timestamp` — chronological queries

---

## Repository API

```csharp
public interface ISheetHistoryRepository
{
    Task<List<SheetHistory>> GetAllAsync();
    Task<List<SheetHistory>> GetBySheetIdAsync(int sheetId);
    Task LogAsync(int sheetId, string sheetDescription, string action,
                  string details, int quantityChanged = 0,
                  decimal? valueChanged = null);
    Task<DateTime?> GetLastUpdatedAsync();
    Task<int> GetTotalCountAsync();
}
```

### Usage from SheetStoreViewModel

```csharp
await _historyRepo.LogAsync(
    sheet.Id,
    DescribeSheet(sheet),
    "Bought",
    $"Purchased 20 sheets. Stock: 50 -> 70. Value: 2,000 AED.",
    quantityChanged: 20,
    valueChanged: 2000m);
```

---

## UI: HistoryView

- Modal dialog (900 x 640)
- Header with entry counter badge
- Filter dropdown: All / Added / Edited / Deleted / Bought / Used
- Search box (description, details, username)
- Refresh button
- Chronological list (newest first) with:
  - Color-coded action badge (green / amber / red / blue / purple)
  - Sheet description + change details
  - Username
  - Date / Day / Time (right-aligned)
- Empty state ("No history yet")

---

## Status

### Completed
- Entity with computed display helpers
- Added to shared `AppDbContext` with indexes
- Repository (async CRUD + `LastUpdatedAsync` + `TotalCountAsync`)
- `HistoryViewModel` with reentrancy guard and error handling
- Filter by action type + search by text
- `HistoryView` dialog with color-coded badges
- Integrated in `SheetStoreViewModel` — logs on all mutations
- "History" button in Sheet Store header
- "Last Updated" timestamp bar

### To-Do

**Phase 2 — Enhanced UI**
- Export history to CSV / Excel
- Date range filter (from -> to)
- Group by date / user / action

**Phase 3 — Per-Sheet History**
- Right-click Sheet Store row -> "View history for this sheet"
- Uses existing `GetBySheetIdAsync`

**Phase 4 — Undo/Redo**
- Reverse last action
- Restore deleted sheet from history snapshot

**Phase 5 — Multi-User**
- Real user tracking (not `Environment.UserName`)
- Permissions (who can view/delete)
- Auto-archive after N months

---

## Design Principles

1. **Immutable log** — entries never edited, only inserted
2. **Snapshot pattern** — `SheetDescription` preserved even if sheet deleted
3. **Automatic** — every mutation logs, no manual "save history" needed
4. **Async everywhere** — no UI blocking on DB writes
5. **Reentrancy safe** — concurrent load attempts guarded via `_isLoading` flag
6. **Graceful errors** — try/catch shows user dialog, never crashes

---

## Adding a New Loggable Action

1. Do the work in `SheetStoreViewModel`
2. Call `_historyRepo.LogAsync(...)` immediately after
3. Add new action to the filter dropdown in `HistoryView.xaml`
4. Add color mapping in `SheetHistory.ActionColor`
5. Reload the list

Example:

```csharp
await _repo.SomeOperationAsync(...);
await _historyRepo.LogAsync(sheet.Id, DescribeSheet(sheet), "Reserved",
    $"Reserved {qty} sheets for Job Order #{jobId}.", qty, value);
await LoadAsync();
```

---

## Known Issues

None. Reentrancy guard fixes the crash on rapid Refresh clicks.

---

## Database Notes

- File: `%AppData%\ProGlassAutomation\proglass.db`
- Table auto-created on first launch via `EnsureCreated()`
- Existing DBs need deletion once (dev mode) after adding this feature
- Production will use EF migrations

<!-- pg-nav -->
---

*This README documents `Services/SheetStore/SheetStoreHistory/`. For the end-to-end business chain see [`docs/WORKFLOW.md`](../../../docs/WORKFLOW.md); for every other document see [`docs/README.md`](../../../docs/README.md).*
<!-- pg-nav -->
