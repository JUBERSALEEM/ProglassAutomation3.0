# ProGlass Automation 3.0

> **A glass-manufacturing ERP rebuilt on the V3 theme engine.**
> Theme purity. Business honesty. No invented features.

`WPF` · `.NET 8` · `EF Core 8 (SQLite)` · `9 themes` · `V3 contract tokens only`

---

## Contents

- [Overview](#overview)
- [Repository map](#repository-map)
- [Theme engine (V3)](#theme-engine-v3)
- [The Inquiry module](#the-inquiry-module)
  - [Pages](#pages)
  - [Status flow](#status-flow)
  - [Scope of supply (BOQ)](#scope-of-supply-boq)
  - [Validation](#validation)
  - [Persistence](#persistence)
  - [Attachments](#attachments)
  - [Import / export](#import--export)
- [Shell integration](#shell-integration)
- [Work tree — what shipped, and when](#work-tree--what-shipped-and-when)
- [Verification status](#verification-status)
- [Roadmap](#roadmap)

---

## Overview

ProGlass Automation 3.0 is a Windows desktop ERP for a glass business. It was
rebuilt around a strict separation between **presentation** and **business**:

- The **V3 theme engine** owns every visual decision. Business screens may only
  consume the engine's contract tokens (`{DynamicResource …}`) — no hex colours,
  no screen-local palettes, no `StaticResource` colours.
- The **business modules** own every business decision. Each module ships a
  register (list) page and a detail (editor) page on a shared page pattern,
  with validation and workflow rules ported verbatim from the legacy system —
  nothing was weakened or "fixed" to make the UI prettier.

The first business module is **Inquiries**: the commercial intake register where
a customer's glass order scope is captured, priced later, and tracked through
the commercial chain *Inquiry → Proforma Invoice → Job Order → Delivery Note →
Tax Invoice → Statement*.

## Repository map

```
ProGlassAutomation3.0/
├─ ProGlassAutomation.csproj      explicit item groups; WPF, net8.0-windows7.0
├─ App.xaml(.cs)                  theme applied at startup (ThemeService.Apply)
├─ MainWindow.xaml(.cs)           shell: sidebar + banner + workspace tabs
├─ Theme/
│  ├─ step1.a.ThemeEngine/        contract tokens + ThemeService (the engine)
│  ├─ step1.g.Customization/      icon service + IconKind (navigation marks)
│  ├─ step1.f.Analytics/          chart theming
│  └─ Theme<Name>/                9 themes: Bento, Claymorphism, Fluent,
│                                 Glassmorphism, Holographic, Liquid Glass,
│                                 Neumorphism, Spatial, Swiss
│      └─ step1.e.Components/     per-theme component styles (Card, Caption,
│                                 StatusBadge, PrimaryButton, …)
├─ step1.ThemeStudio/
│  └─ step1.b.ThemeStudioScreen/  shell VM (workspaces 0–7), studio screens
└─ Inquiry/                       first business module
   ├─ Models/                     InquiryRecord (dimension-first BOQ), lifecycle,
   │                              inbox state ledger
   ├─ Services/
   │  ├─ Database/                AppDataRoot, SqliteResilience, migrations
   │  │  └─ SchemaMigrations/     runner + registry (1001–1005)
   │  ├─ Inquiry/                 repository, workflow, attachments,
   │  │  │                        import/export, lifecycle, validation
   │  │  └─ InquiryInbox/         website inbox: protocol models, Supabase
   │  │                            client, sync service, ingest, tray notifier
   │  ├─ Audit/                   CurrentOperator (environment-user actor)
   │  ├─ Reporting/               PagedResult
   │  └─ Support/                 FileOpener, AsyncUtility, limits, errors
   ├─ ViewModels/                 list VM, editor VM, inbox VM, row models
   └─ Views/                      InquiryListView, InquiryInboxView,
                                   InquiryEditorView, InquiryWorkspaceView
                                   (register ↔ inbox ↔ detail)
```

> `tests/ui-mockups/` holds local UI mockup images only. It is git-ignored and
> is **never** part of the product source.

## Theme engine (V3)

Nine themes, one contract. Every theme ships the same component vocabulary —
`Card`, `MetricCard`, `PageHeading`, `SectionHeading`, `Caption`, `StatusBadge`,
`StatusText`, `PrimaryButton`, `SubtleButton`, `KeyboardFocus` — resolved from
the same token names (`ApplicationBackground`, `SurfacePrimary/Secondary/Elevated`,
`TextPrimary/Secondary/Muted`, `AccentPrimary/Hover/Pressed/Soft`, `TextOnAccent`,
`BorderSubtle/Strong`, `FocusIndicator`, `StateHover/Pressed/Selected`,
`Status*`, `Padding*`, `Margin*`).

The **Texts / Colors / Icons / Pages studios are overlays, not themes** — they
customize type, color plates, navigation marks and page sizing on top of the
active theme and are grouped under *Settings* in the shell. The Pages studio
pins one of four compact layouts (Compact · Cozy · Comfortable · Spacious —
spacing, card padding, control height and text size) above every theme engine;
*Reset pages* returns to the theme's own rhythm.

## The Inquiry module

### Pages

| Page | View | What it does |
|---|---|---|
| **Register (list)** | `InquiryListView` | **Website-inbox card at the top** (ready / in-register counts, last check, one-click *Open inbox*) · page heading + caption · KPI strip (filtered pipeline value, open, awaiting customer, confirmed) · filter card (search with 300 ms debounce, received-date range, salesman, prepared-by, priority, sort, files-only) · seven status chips with live counts · data grid with every column (number, customer, project, salesman, **prepared by**, status badge, value, received, updated, **PI / JO numbers, files**) · error surface · empty state · pagination · full right-click workflow menu |
| **Inbox (website)** | `InquiryInboxView` | Separate page for inquiries arriving from the customer website · connection status strip (last check, automatic interval, ready / in-register / failed counts) · **Website connection** card (Supabase URL, anon key, bridge account, DPAPI-protected password, inbox folder, sync interval, notification + sound toggles, Test / Save) · **Website deliveries** grid (reference, company, project, prepared by, files, received, state badge, local inquiry) · *Check now*, *Create local inquiry* (ingest), *Open inquiry*, *Open folder* |
| **Detail (editor)** | `InquiryEditorView` | Header (number, customer, status badge, dirty dot) · status actions (Prepare / Send / Confirm / Hold / Cancel) · Save · **Header fields** card (customer/project/salesman register-sourced suggestions, location auto-fill, value with inline validation, notes) · **Scope of supply (BOQ)** grid · **Attachments** card · **Communications & activity** feed · **Lifecycle** card (INQ → PI → JO → DN → TI → STMT) |
| **Workspace** | `InquiryWorkspaceView` | Owns the single list view-model; switches register ↔ inbox ↔ detail in place; hosts file dialogs and confirmations; initializes the local database on first open; runs the inbox auto-sync timer and owns the tray notifier |

The list ↔ detail pattern is the standing page pattern: every future module
(PI, JO, DN, TI) reuses the same structure. The inbox is the first module
to host a third page type (an intake page) inside the same workspace.

### Website inbox (customer website → local register)

The customer-website intake portal stores new submissions in Supabase. The
desktop pulls them into an isolated inbox folder and, on demand, creates
local inquiries — the desktop remains fully usable offline at all times.

| Step | Detail |
|---|---|
| **Pull** | `plan_customer_inquiry_deliveries_v2` returns accepted, not-yet-delivered submissions (up to 100), each with its primary estimator and assignment snapshot |
| **Download** | per-document `plan_customer_inquiry_delivery_documents` → Supabase Storage download; size **and** SHA-256 verified before anything touches disk (fail closed) |
| **Stage & commit** | files land in `.staging/{id}.{uuid}.partial` and the whole folder is atomically renamed to `{root}/{unassigned\|assigned/{CODE}}/{WEB-ref}_{id}/` with `manifest.json` + `READY` — byte-identical to the Node bridge, so both can share one root |
| **Acknowledge** | `acknowledge_customer_inquiry_delivery_v2` with the manifest hash at **download-complete** (remote status flips to `downloaded`); re-ack of an identical delivery is a no-op |
| **Ingest** | *Create local inquiry* maps the submission onto an `InquiryRecord` (company → customer, project/location, description, salesman, **primary estimator → Prepared by**, `inquiry_type` → reference, `WEB-…` reference + submission UUID kept on the record for traceability), adds a *Website* communication entry, copies every document through the normal attachment pipeline, and links the ledger row to the new `INQ-…` number — idempotent |
| **Notify** | a tray-icon balloon **with sound** the moment a sync sees a submission the local ledger has not seen |
| **Auto-sync** | on the configured interval (default 5 min) plus an instant *Check now* button |

**Folder contract** (shared with the Node bridge):

```
C:\ProgramData\ProGlass\CustomerInquiryInbox\
├─ unassigned\{WEB-ref}_{submissionId}\{manifest.json, READY, documents\…}
├─ assigned\{ESTIMATOR_CODE}\{WEB-ref}_{submissionId}\{…}
├─ .staging\                          (interrupted downloads, cleared on start)
└─ checkpoint.json
```

**Offline is a state, not an error**: an unreachable backend shows
"Website unreachable — the inbox keeps working locally"; already-downloaded
deliveries stay ingestible, and the register never depends on the network.
A local ledger table (`InquiryInboxItems`, migration **1005**) records
`downloaded → ingested` (or `failed`) per submission, and READY folders
already on disk (delivered by the Node bridge) are adopted on startup.

The manifest is canonical JSON (sorted keys, 2-space indent, JS string
escaping, trailing newline) so the SHA-256 that travels to the
acknowledgement RPC is the same value the Node bridge would compute —
WPF and Node interoperate on one inbox root without drift.

Local attachments accept the website's file set (pdf, images, csv, xlsx,
**zip, txt**) up to **30 MB** per file — WhatsApp-style manual
attachments and web-ingested documents share one rule.

### Status flow

```
            ┌────────────┐
            │  Received  │
            └─────┬──────┘
        Prepare   │   Hold
         ┌────────┼────────┐
         ▼        │        ▼
    ┌─────────┐   │   ┌────────┐
    │ Prepared│   │   │  Hold  │
    └────────┘   │   ───┬────┘
        Send      │       │ Prepare
         │        │       │
         ▼        │       │
    ┌─────────┐   │       │
    │  Sent   │◄─────────┘
    └────┬────┘
      Confirm
         ▼
    ┌───────────┐
    │ Confirmed │
    └───────────┘

    Any non-terminal state ──► Cancelled (terminal, alongside Confirmed)
```

Transitions are enforced in **one place** — `InquiryWorkflowService.CanTransition` —
and the UI (row menu, editor buttons) only exposes what the rule allows.

### Scope of supply (BOQ)

Dimension-first lines, exactly as written on paper: **1200×1500, 40 nos, DGU**.

| Column | Note |
|---|---|
| Glass build | `SGU` · `DGU` · `LAM` · `DGU+LAM` (exact-match normalization for PI import) |
| Item / specification | free text |
| Outer / Inner | glass colours per pane |
| W mm / H mm | millimetres, capped at 20 000 |
| Qty | capped at 100 000 |
| Rate / m² | **expected rate only — `0` means "priced later in the PI"** |
| Notes | per-line remarks |

The PI utilized sheet and the JO cutting sheet are different things; **no mix /
cutting logic exists or belongs on an inquiry.**

### Validation

`InquiryValidationRules` is the single shared gate (port, unmodified):

| Rule | Limit |
|---|---|
| Dimension (W / H) | ≤ 20 000 mm |
| Quantity per line | ≤ 100 000 |
| Rate per m² | ≤ 100 000 |
| Inquiry value (AED) | ≤ 999 999 999 |
| Header | Customer **and** Project required |
| PI-ready lines | exact normalized build required |

The editor validates inline (value hint, red error surface) **and** `SaveAsync`
re-runs the full gate before any write — the button state can never be bypassed.

### Persistence

- **Local EF Core 8 + SQLite** (`inquiries.db` under the app data root) — the
  same store engine the legacy system used; only the Inquiry tables exist.
- **Schema migrations 1001–1005** converge an existing database: create tables,
  drop legacy columns, add the `InquiryLineItems` table, add the `RowVersion`
  concurrency token, add the website-inbox ledger (`InquiryInboxItems`) and the
  `WebSubmissionId` / `WebSubmissionReference` traceability columns.
- **Numbering**: `INQ-{yyyy}-####` (max+1) with up to 20 jittered uniqueness
  attempts; `BUSY`/`LOCKED` writes retry with back-off (`SqliteResilience`).
- **Concurrency**: `RowVersion` optimistic token on every update — a stale save
  fails loudly with "changed by someone else", it never clobbers.
- **Delete guard**: an inquiry with a downstream Proforma Invoice (or JO / DN /
  TI / Statement link) cannot be deleted — cancel it instead.

### Attachments

Manifest-backed storage: `{appdata}/…/Inquiries/{id}/Attachments/` with a
`manifest.json` source of truth, files renamed to `{guid}_{safeName}`.

- Allowed: `pdf, jpg, jpeg, png, bmp, gif, webp, dwg, dxf, xlsx, xls, csv`
- Max size: **25 MB per file**
- Legacy EF-only rows are migrated to disk on first read.
- The editor auto-saves the draft before the first attachment, so attaching
  never dead-ends.

### Import / export

- **Export**: every row matching the *current filters* (paged fetch, capped at
  5 000 rows) as RFC-quoted CSV.
- **Import**: parses the register CSV, skips rows without customer/project,
  regenerates numbers for duplicates, creates via the same workflow service,
  and reports `imported / invalid-skipped / duplicates / failed`.

## Shell integration

- **Inquiries is the first business page** — its own *Business* group in the
  sidebar, above *Settings* — and the app's **default landing workspace**.
- Workspace index 2 of 8 (`0 Studio · 1 Home · 2 Inquiries · 3 Texts · 4 Colors
  · 5 About · 6 Icons · 7 Pages`), with its own clipboard-check navigation mark
  (`IconKind.Inquiry`) in the icon service.
- Banner shows the workspace title/subtitle ("Inquiries — Business · Inquiry
  register") on every page, including Inquiry.

## Work tree — what shipped, and when

```
branch arena/da841e06-proglassautomation3-0
│
└─ 2026-10-23
   ├─ 06:18   2ce9856   Base (ThemeStudio-9 lineage): V3 theme engine,
   │                   9 themes, shell, Texts/Colors/Icons studios,
   │                   page marks resolved from heading title/category
   │
   └─ 14:51   f01060b   INQUIRY MODULE — list + detail, end to end
      ├─ Models          InquiryRecord (dimension-first BOQ) + lifecycle snapshot
      ├─ Persistence     EF Core SQLite, migrations 1001–1004, resilience,
      │                  INQ-{yyyy}-#### numbering, RowVersion concurrency
      ├─ Services        repository, workflow (CanTransition), attachments
      │                  (manifest-backed), CSV import/export, validation (verbatim)
      ├─ ViewModels      list (chips/filters/KPI/pagination/context workflow),
      │                  editor (dirty tracking, inline validation, BOQ grid)
      ├─ Views           InquiryListView, InquiryEditorView, InquiryWorkspaceView
      └─ Shell           Business nav group, Inquiries default landing,
                         IconKind.Inquiry, workspace range 0–6
```

Each entry above is a commit on this branch — hash, time and content. When the
next module lands, add its branch to this tree.

## Verification status

| Check | Status |
|---|---|
| XAML well-formedness (all views) | ✅ |
| No `Ov.` / `Pg.` / `VisualThemeEngine` / ADV theme leakage in module files | ✅ |
| All `FontSize/Margin/Padding/Foreground/Background/BorderBrush` values are `{DynamicResource}` / `{Binding}` | ✅ (120/120) |
| No `#hex` colours in views; no `StaticResource` colours | ✅ |
| C# brace/paren balance (string-aware scan) | ✅ |
| XAML bindings cross-checked against VM / row / contract members | ✅ |
| `dotnet build` | ⏳ **no .NET SDK in the authoring sandbox** — build on Windows |
| Visual proof (fonts, theme rendering) | ⏳ requires Windows — not claimable from Linux |

## Roadmap

The approved order is owned by
[`docs/11.Business-Module-Roadmap.md`](docs/11.Business-Module-Roadmap.md), with
each module's scope in
[`docs/12.Module-Specifications.md`](docs/12.Module-Specifications.md). In short:

| Order | Module | State |
| --- | --- | --- |
| — | `step1` Theme Studio · `step2` Client Management | completed for their approved scope (Windows runtime/visual verification outstanding) |
| 1 | `step3` User Management | implemented 2026-10-27 for its approved scope (Windows runtime/visual verification outstanding) |
| 2 | `step4` Company Profile | allocated; needs approval |
| 3 | `step5` Project Records & Inquiry Integration | allocated; needs approval |
| — | **`G1` Foundation Review** | gate — no document module starts until the owner accepts it |
| 4 | `step6` Proforma Invoice (PI) | allocated; blocked by `G1` |
| 5 | `step7` Job Order (JO) | allocated; blocked by `G1` |
| 6 | `step8` Delivery Note (DN) | allocated; blocked by `G1` |
| 7 | `step9` Tax Invoice (TI) | allocated; blocked by `G1` |
| 8 | `step10` Statement | allocated; blocked by `G1` |
| 9 | `step11` Reports and other required modules | scope deliberately undefined |

Every business list page reuses the register pattern recorded in
[`docs/10.List-Page-Pattern.md`](docs/10.List-Page-Pattern.md) — one command bar,
state in dropdowns, collapsible filters — and the same validation and migration
gate. Allocation is not authorization: each module starts only on the owner's
explicit approval.

---

*ProGlass Automation 3.0 — built on the V3 theme engine. If a pixel is not a
contract token, it does not ship.*
