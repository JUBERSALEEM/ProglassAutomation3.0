# step2 — Client Management

Owner-approved feature `step2`. Scope: the glass company's **client directory**
only. Allocation evidence, decisions and the verification stance are recorded in
[`docs/05.Decision-Log.md`](../../docs/05.Decision-Log.md) (2026-10-27),
[`docs/06.Feature-Registry.md`](../../docs/06.Feature-Registry.md) and
[`docs/04.Project-Roadmap.md`](../../docs/04.Project-Roadmap.md).
Naming rules remain owned by [`docs/00.Project-Rules.md`](../../docs/00.Project-Rules.md).

## 1. What this module does

| Capability | Where |
| --- | --- |
| Client list (register) with active/inactive counts | `step2.d.1.ClientWorkspaceView.xaml` |
| Search across name, attention name, phone, e-mail, TRN, address | `step2.b.4.ClientRepository.QueryAsync` |
| Add / view / edit a client | `step2.d.2.ClientEditorView.xaml`, `step2.c.1.ClientEditorViewModel.cs` |
| Activate / deactivate (never delete) | `step2.c.2.ClientWorkspaceViewModel.ToggleActiveAsync` |
| Field validation | `step2.a.2.ClientRules.cs` |
| Duplicate **warnings** (never merges, never blocks) | `step2.a.3.ClientDuplicatePolicy.cs` |
| Safe error handling | repository `ClientSaveResult`, workspace/editor error surfaces |

Deliberately **not** in this stage: Company Profile, User Management /
sales representatives, Projects, Inquiry integration, proforma invoices and
any document module. No priority, no document numbering, no representative
field.

What follows this module, and in what order, is recorded in
[`docs/11.Business-Module-Roadmap.md`](../../docs/11.Business-Module-Roadmap.md):
User Management (`step3`), Company Profile (`step4`), Project Records & Inquiry
Integration (`step5`), then gate `G1`, then the document modules. This module's
own scope is closed and is not reopened by that order.

## 2. Allocated file hierarchy

Child identifiers are allocated once, here, per `docs/00`:

| Identifier | Responsibility |
| --- | --- |
| `step2.a.1.Client.cs` | `Client` entity + `ClientDraft` (editable values, no persistence) |
| `step2.a.2.ClientRules.cs` | Normalization (whitespace, Unicode Form KC keys, digits) and field validation |
| `step2.a.3.ClientDuplicatePolicy.cs` | Duplicate detection and operator-facing warning text |
| `step2.b.1.ClientSchema.cs` | `Clients` DDL, idempotent convergence, shape assertions (migration 1011) |
| `step2.b.2.IClientRepository.cs` | Persistence contract, `ClientQuery`, `ClientListRow`, `ClientSaveResult` |
| `step2.b.3.ClientDbContext.cs` | EF mapping for the one table in the shared database file |
| `step2.b.4.ClientRepository.cs` | Queries, writes, duplicate probe, isolated-database seam |
| `step2.c.1.ClientEditorViewModel.cs` | Editor state, per-field messages, duplicate confirmation, save |
| `step2.c.2.ClientWorkspaceViewModel.cs` | Register state, search, selection, lifecycle, editor host |
| `step2.d.1.*` | Workspace view (+ navigation-only code-behind) |
| `step2.d.2.*` | Editor view (+ initialisation-only code-behind) |
| `step2.e.*` | This documentation |
| `step2.f.*` | Verification: `step2.f.ClientTests.csproj` and the portable source checker |

The pair `step2.d.1.ClientWorkspaceView.xaml` / `.xaml.cs` shares one identifier
because WPF tooling pairs by identical basename — the same documented
tooling exception as `step1.b.1`.

## 3. Data model

`Clients` in the existing `inquiries.db`:

| Column | Type | Rule |
| --- | --- | --- |
| `Id` | INTEGER PK AUTOINCREMENT | stable internal identifier; never the name |
| `ClientName` | TEXT NOT NULL | required, ≤200 — **the company**, whitespace-normalized |
| `AttentionName` | TEXT NOT NULL DEFAULT '' | optional, ≤200 — **the person at that company to address** (migration 1012) |
| `Phone` | TEXT NOT NULL DEFAULT '' | optional, ≤50, ≥6 digits |
| `Email` | TEXT NOT NULL DEFAULT '' | optional, ≤200, shape-checked |
| `Trn` | TEXT NOT NULL DEFAULT '' | optional, digits only, ≤20, leading zeros preserved |
| `Address` | TEXT NOT NULL DEFAULT '' | optional, ≤500 |
| `Notes` | TEXT NOT NULL DEFAULT '' | optional free text |
| `IsActive` | INTEGER NOT NULL DEFAULT 1 | deactivation instead of deletion |
| `CreatedAt` / `UpdatedAt` | TEXT NOT NULL | UTC, stamped by the repository |

Indexes: `ClientName`, `Phone`, `Email`, `Trn`, `(IsActive, ClientName)`.
**None is unique** — two genuine clients can share a name (owner decision D9),
so duplicates are warned about, never blocked.

### Client name is the company; attention name is the person

`ClientName` is the legal/trading entity (`Al Noor Glass LLC`). `AttentionName`
is the human to address on quotes and correspondence (`Mr Ahmed Al Mansouri`),
equivalent to ADV's `AttentionExecutiveName`. Both are stored as independent
values; neither is derived from the other, and the attention name is never
required.

**Attention name is not a sales representative.** It describes the client's own
contact. Our sales representative (the person who owns the client relationship) is
the separate `RepresentativeUserId` reference described below.

### Representative identity is a reference, not a column of text

Per owner decision D3 there is **no** `SalesRepresentativeName` and **no**
`Salesmen` table: a free-text representative column is exactly the ADV defect this
module avoids (ADV keeps `SavedClient.SalesRepresentative` as a string next to an
unrelated name-only `Salesmen` table, so the same person exists twice).

What exists instead, added by **migration 1014** once `step3` had created the user
directory:

* `Clients.RepresentativeUserId` — a **nullable** id into `Users`, so every client
  that existed before 1014 stays valid and unassigned;
* one picker in the client editor, listing **every active user — salesmen first**
  (the role code comes from `UserCatalog.SalesmanRole`, never a retyped literal),
  with "No representative" as a real choice. The picker binds the row object and is
  seeded from the client's stored id, so it works on **edit** as well as on create;
  a stored reference whose user is no longer active keeps its own "(inactive)" row
  rather than blanking the box and being cleared by the next save;
* one register column showing the resolved name — a rename relabels the register
  and rewrites no client row;
* a write that names no existing user is refused, and 1014's verify step fails the
  upgrade if any stored reference is an orphan.

## 4. Database and migration

- Database: the existing `inquiries.db` under `AppDataRoot` — no new file.
- Chain: the existing single `AppSchemaMigrations.Inquiries` registry,
  `PRAGMA user_version`. The client directory is **1011**; the attention-name
  convergence is **1012**. 1001–1010 are untouched.
- Existing safeguards are inherited unchanged: SQLite online-API backup before
  migrating (refuses to migrate if the backup fails), all pending migrations in
  one transaction, `Verify` callbacks after each and after the whole set, and a
  post-migration `PRAGMA quick_check`.
- Both migrations are idempotent (`CREATE TABLE IF NOT EXISTS`, `ADD COLUMN`
  when missing) and assert the final shape (`RequireColumn`, `RequireIndex`,
  `RequireNoNullValues`), so a wrong result rolls the whole upgrade back.

### Why 1011 was not edited

1011 already ran on the owner's database (confirmed by the running application),
so it stays the **immutable baseline**, exactly as `1002`'s comment in the same
file requires. Migration **1012** converges both cases to one shape:

| Starting point | 1012 action |
| --- | --- |
| legacy `ContactPerson` only (an early 1011 database) | `ALTER TABLE … RENAME COLUMN` — values move with the column |
| `AttentionName` only (fresh database created from the EF model) | nothing to do |
| both, legacy empty | drop the empty legacy column |
| both, values in both | copy legacy into blank cells; **refuse** on a conflicting value, then drop legacy only after a verified preservation check |

1012 also asserts the legacy column is **gone**, so a failed merge aborts the
transaction (backup kept, `user_version` unchanged) instead of leaving two
competing person columns.

One trap is handled explicitly: `SchemaMigrationRunner` re-runs every `Verify`
callback after the whole pending set has applied, so 1011's assertions cover only
columns that survive 1012. The person column is asserted by 1012 alone.
- One database file ⇒ one `user_version` ⇒ **one** chain. A second chain, or a
  second `EnsureCreated` owner, would corrupt the contract; `ClientDbContext`
  therefore refuses both (it reuses `InquiryDbContext.DatabaseFilePath` and
  never calls `EnsureCreated`), and initialization goes through the established
  `InquiryRepository.InitializeAsync()`.

### Why the initializer is called first

EF's `EnsureCreated` creates nothing once *any* table exists. If the client
table were created before the Inquiry tables on a fresh install, the Inquiry
schema would never be created. `ClientRepository.InitializeAsync` therefore asks
the established initializer to bring the file to the current schema (which now
includes 1011) and then asserts the client table exists, failing loudly with the
recorded migration failure if not.

## 5. Inquiry data is untouched

- No `Inquiries` column is added, renamed or dropped.
- `CustomerId`, `ProjectId`, `SalesmanId` and `PreparedByUserId` remain
  unconstrained integers with their indexes. None is repurposed as a client or
  project key.
- No `Inquiries → Clients` foreign key exists, and none is added here.
- No historical inquiry is read, rewritten or matched to a client by name.

Inquiry integration belongs to the separately approved Project Records stage.

## 6. Duplicate warnings

Matching is key-based (`ClientRules.ComparisonKey` = normalized + Unicode Form
KC + uppercase; phone/TRN compared on digits). A match on name, phone, e-mail or
TRN produces one advisory sentence naming the existing client, e.g.

> Possible duplicate: an existing client shares "Al Noor Glass LLC" (name, TRN) — inactive. This is a warning only — nothing was merged. Save again to keep this client separate.

The first save that finds a resemblance stops and shows the warning; a second
explicit save stores the new client unchanged. Nothing is merged, nothing is
rejected, and the self-row is excluded while editing.

## 7. Theme compliance

The module consumes the existing contract only — it adds nothing to any theme
package and has no theme-specific branch:

- colours, brushes, radii, metrics, paddings and fonts come from
  `ThemeContract` roles via `DynamicResource`;
- card roles (`Card`, `HeroCard`, `MetricCard`, `StatusBadge`, `StatusText`) are
  looked up dynamically, never `BasedOn="{StaticResource Card}"`;
- theme-templated controls (`TextBox`, `CheckBox`, `DataGrid`) bind the active
  implicit style explicitly (`Style="{DynamicResource {x:Type …}}"`) so a theme
  switch replaces the template as well as the tokens;
- visibility is driven by `Style` `DataTrigger`s, so no converter or theme hook
  is required;
- `step2.f.2.ClientSourceChecks.py` asserts all of the above, plus that all nine
  installed packages still provide the roles and the `DataGrid` style.

`ThemeCatalog.Installed` (nine complete packages) is the source of truth — not
the older registry prose that still described themes 6–9 as unregistered.

## 8. Buttons, marks and field heights

- Every action button carries a mark from the **one** shared icon system
  (`IconService` + `ShellMark`), so the icons follow the operator's chosen icon
  style (Icons Studio), the active theme's colour and the theme's mark size. No
  module-local glyphs or duplicated icon control exist.
  `Add` / `Edit` / `Refresh` / `Search` / `Save` / `Cancel` were added to
  `IconKind` as generic verbs — a shared-infrastructure extension, not a step2
  feature number (see `docs/05`, 2026-10-27).
- The row action's mark flips with the client state: cross while the client is
  active (deactivate), plus while it is inactive (re-activate).
- Fields that share a row with a taller neighbour set `VerticalAlignment="Center"`
  (for example the TRN box beside the Status block). A stretched `TextBox`
  otherwise grows to its neighbour's height and looks oversized.
- Paired field rows are **equal halves** (`1*` / `1*`): Phone above TRN, E-mail
  above Status. The two rows therefore line up as one two-by-two block instead
  of the old `1 : 1.4` split, which left a long empty TRN box.
- The register is centred end to end: cell values **and** column headers, and
  the view does the centring itself. Setting `HorizontalContentAlignment` on the
  cell or `HorizontalAlignment` on the value was measured and does not work: the
  package hands a cell a **content-sized** slot, so a centred value still
  renders flush left (owner: "still data left side"). `ClientCell` and
  `ClientColumnHeader` therefore own their `ControlTemplate`s; the alignment
  lives on the cell/header (a `ContentControl`, which declares
  `HorizontalContentAlignment` / `VerticalContentAlignment`) and the presenter
  carries it as `HorizontalAlignment` / `VerticalAlignment` through a
  `TemplateBinding`. Every colour in both templates stays a `TemplateBinding`,
  so the package still owns background, foreground, selection and the focus
  ring.

  `ContentPresenter` is **not** a `ContentControl` and has no
  `HorizontalContentAlignment`. Writing that attribute there is a compile error
  (`MC3072` / `XLS0413` / `XDG0012`) — it shipped once and broke the build, which
  is why `step1.d.17` now refuses the property on any presenter, refuses any
  attribute outside a reviewed property set for the hand-templated types, checks
  every `TemplateBinding` against the template target's real properties, and
  fails a view-owned `StaticResource` key that is not defined in its own file.
- In the Actions card the primary action is one full-width row and Edit /
  Refresh share a second row of equal halves. The grid owns the inset and the
  buttons carry no margin of their own; nested margins made Edit narrower than
  Refresh and made the pair look collapsed. The other half of "collapsed" was
  colour: in Neumorphism a button, its card and the page were all the same
  surface, so a secondary action had no fill of its own and read as a word on
  the card. See the theme note below.
- The rules live in the **header row only**, and the view draws them. Two
  separate mechanisms put rules between the data rows: the view's own
  `GridLinesVisibility` (round 4) and the package's per-cell border, which is
  what still boxed the rows after `GridLinesVisibility` went. Both are now
  stated: `GridLinesVisibility="None"` on the grid, and a cell template that
  paints no border of its own. `step2.f.2` fails if either is removed.
- The header's column separators are one hairline per boundary
  (`ClientHeaderRule`: `BorderStrong`, width `StrokeWidth`, right-aligned), so
  the header keeps the dividers the owner asked for without inheriting a
  package's boxed header treatment. The last column has no rule — the band ends
  on the grid's own edge.
- Columns are resizable by hand (`CanUserResizeColumns`, floor
  `ControlMinWidth`). The rule is what makes the drag target visible: the
  theme's gripper is transparent until it is hovered.
- **Rebuild before judging the UI.** A WPF build does not pick up edited XAML in
  an already-running `bin` output, and a failed build leaves the previous
  executable in place, so an old window can look exactly like an unfixed bug.
  Confirm `git rev-parse HEAD` in the working copy and do a full rebuild.

### The register page is the register (round 7)

Owner direction: *"a list page has a list from the start … on header there are
too many options, move them into a dropdown … big cards fix in small cards … the
main view can be list only with column header."*

The page used to spend its top third on chrome — `HeroCard`, two `MetricCard`s, an
"Actions" card holding three buttons, then a whole `Card` for a search box and a
checkbox, then the grid, then a footer `Card` with two more buttons. That is four
cards, three headings and five buttons above a six-column register, and about
eleven visible rows at 1080p.

Now there is **one command bar**, one chip strip, the register, and one footer
strip:

| Was | Now |
| --- | --- |
| `HeroCard` + `Caption` | `PageHeading` "Clients" + `Caption` "Client directory" |
| two `MetricCard`s | one `Caption`: `{RegisterSummary}` ("3 active · 7 inactive · 10 shown") |
| "Actions" card: New / Edit / Refresh | one `PrimaryButton` (**New client**) + one `⋯` overflow `ContextMenu` (Refresh) |
| filter `Card`: search, *Include inactive*, Clear search | search field in the bar with a `Search` mark and a `Cancel` clear button; the checkbox became **All · 10 / Active · 3 / Inactive · 7** chips |
| footer `Card`: summary, Edit selected, Deactivate | footer `Plate`: same three, on the register's own surface |
| — | a per-row `⋯` menu: **Edit client…** and **Deactivate / Re-activate client**, labelled from the row projection |

Implementation notes worth keeping:

- **Filtering moved into the view model, not the query.** `ReloadAsync` always
  loads the whole (searched) directory — inactive included — because the chips
  count what a tab *would* show; the tab then decides which rows reach `Rows`.
  `IncludeInactive` survives as the underlying switch so nothing that used it
  breaks, but the checkbox is gone.
- **The chips reuse the Inquiry register's shape** (`ClientStatusTab` with
  `Key`/`Label`/`Count`/`IsSelected`), styled from theme tokens only
  (`BorderSubtle` idle, `AccentPrimary` + `TextOnAccent` selected,
  `ChipCornerRadius`, `StateHover`/`StatePressed`). Both registers now behave the
  same way and cannot drift apart.
- **Menus are themed `ContextMenu`/`MenuItem`s.** `ToolBar`, `Menu`, `Popup`,
  `StatusBar`, `ToggleButton` and `Expander` are **not** in the theme contract, so
  none of them is used: they would render unstyled in all nine packages.
- **A `Button.ContextMenu` only opens on right-click**, so `OnMoreActionsClick`
  and `OnRowMenuClick` open it on a left click. The row menu's command comes from
  the grid's `Tag` (the workspace view model) and its parameter from
  `PlacementTarget.DataContext` (the row), which keeps per-row actions independent
  of what else is selected. The footer keeps working for the selected row, so a
  row can be acted on either way — if the row menu ever misbehaves, the page is
  still fully operable.
- **Measured cost:** the chrome above the register drops from roughly 240 px to
  about 96 px, which is where the extra four to seven visible rows come from.

### Round 8 — one bar, one row of actions, no strip

Owner feedback on the built page (screenshot annotated four times): *"this
section didn't have proper spacing in texts"*, *"why this section become ugly,
where are the filtered and other things gone"*, *"why three dot didn't have
column header"*, *"move these to top so this space can be extra … duplicated
section row data name"*.

| Symptom | Cause | Fix |
| --- | --- | --- |
| Bar looked crowded/ugly | it repeated the page title the shell banner already shows ("Clients" + a `Caption` jammed beside it at a different size), and both blocks hugged the top with no rhythm | the title is gone from the bar; the summary line sits above the chips, every control is `VerticalAlignment="Center"`, and the line spacing comes from `MarginInline`'s own bottom inset |
| Filtered things looked missing | the chips were pushed under a large heading in a three-column grid, so they read as clutter rather than as the filter | chips are the first thing in the bar, with the live count directly above them |
| Action column had no header | the column header was an empty `TextBlock` | it reads **Actions** |
| Strip under the register was wasted space | it printed `SelectedSummary`, which repeated the row already on screen, and it cost ~60 px of register height | the strip is deleted; **Edit** and **Deactivate** moved into the bar (disabled without a selection, tooltipped) and the register took the pixels |
| Wide empty area inside the register | the footer strip plus a `Card` wrapper put a floor on the visible area | the register now runs to the bottom of the page; the remaining space is genuinely empty rows, which is what a register should look like |
| `BAKAR MOHAMMED TECHNICAL SERVICES…` truncated | `ClientName` at `2*` inside a 1 400 px grid could not hold a 37-character company name | weights re-balanced (`2.6*` name, `1.1*` attention, `1.1*` phone, `1.3*` e-mail, `1*` TRN, `0.9*` status, `1*` updated) and the name cell carries `ToolTip="{Binding ClientName}"` so the full value is one hover away |
| Search field looked empty, not empty-*able* | `TextBox` has no placeholder property | a `TextBlock` overlay with a `DataTrigger` on `SearchText` shows "Search clients…" until something is typed (no converter resource needed) |

Guards: `step2.f.2` now fails if the strip returns, if `SelectedSummary` is bound
on the page, if the actions column loses its header, or if either selection
command disappears from the bar; three mutations verified failing.
`step1.d.17` refused `IsHitTestVisible` on the new hint `TextBlock` until it was
reviewed into the allow-set — the property is `UIElement`'s and the overlay needs
it so clicks fall through to the field.

### Neumorphism depth (theme package, owner direction)

The owner's screenshot was Neumorphism, not Claymorphism, and the complaint was
that it "looks plain". Measured cause: `ApplicationBackground`, `SurfacePrimary`,
`SurfaceSecondary` and `SurfaceElevated` were **all `#E6EBEF`** in Light (and all
`#2A2F38` in Dark), so the page, the cards, the plates and the buttons had no
edge against each other and the skin's soft shadow had nothing to describe. The
package now has a ramp - page `#DFE5EA`, card `#E6EBEF`, well `#D7DFE6`,
elevated `#EFF3F7` (Dark: `#24282F` / `#2A2F38` / `#232830` / `#323842`) - and a
button takes the elevated step instead of the card colour. `Plate` moves to
`SurfaceSecondary`, as seven of the nine packages already do. Contrast survives
the move: the worst text-on-surface pair in either mode is 5.31:1.

`step1.d.5` now fails any package whose page and card are the same colour, or
that exposes fewer than three distinct surface steps, so a package cannot flatten
back into one sheet without a decision being recorded.

## 9. Verification

Executable here (portable, no SDK):

- `python3 step2.ClientManagement/step2.f.tests/step2.f.2.ClientSourceChecks.py`
- `python3 Theme/step1.d.tests/step1.d.17.XamlPropertyChecks.py` — every XAML
  attribute, `TemplateBinding` target and view-owned `StaticResource` key, i.e.
  the checks that catch a property which does not exist on its type.

Requires Windows + .NET 8 (not available in the authoring environment):
`dotnet run --project step2.ClientManagement/step2.f.tests/step2.f.ClientTests.csproj`
— covers normalization, validation, duplicate policy, row projections, and a
full repository round-trip (create → read → search → duplicate → update →
deactivate → re-activate → allowed duplicate) against an isolated temporary
folder via `AppDataRoot.TestFolderOverride`. The production `%APPDATA%`
directory is never touched.

Still outstanding for this stage, and reported as **not run** rather than
passed: `dotnet build` of the application and both verification executables, the
WPF runtime checks, and nine-theme × Light/Dark visual inspection.
