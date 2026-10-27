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
contact. Our sales representative (the person who owns the client relationship)
remains deferred to the User Management stage — see below.

### Representative identity is deliberately absent

Per owner decision D3 there is **no** `SalesRepresentativeId`, **no**
`SalesRepresentativeName` and **no** `Salesmen` table in this stage. A free-text
representative column is exactly the ADV defect this module avoids (ADV keeps
`SavedClient.SalesRepresentative` as a string next to an unrelated name-only
`Salesmen` table, so the same person exists twice). The User Management stage
will add a canonical user identity and a stable nullable reference from
`Clients`, by migration. Until then the client row is complete on its own.

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

## 8. Verification

Executable here (portable, no SDK): `python3 step2.ClientManagement/step2.f.tests/step2.f.2.ClientSourceChecks.py`.

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
