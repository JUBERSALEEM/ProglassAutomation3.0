# step5 — Project Records & Inquiry Integration

A **project register**: the jobs we build for a client, numbered, with a status,
plus the link from an inquiry to the project it belongs to. One workspace page,
one inline editor beside it, and — in the same page — the inquiries that belong
to the selected project.

`step2` owns the client companies. `step5` owns the work we do for them, and the
one column on an inquiry that says which project the inquiry is about. It does
**not** own the inquiry: the inquiry's text, its numbers and its lifecycle stay
exactly where they are.

---

## The questions the specification left open, and the answers

`docs/12.Module-Specifications.md` §`step5` recorded three questions and left
the answers to the analysis stage. Authorised by the owner on **2026-10-28**.

| Question | Answer | Why |
| --- | --- | --- |
| Is a project record required now, or is this stage only the client link? | **Project records now**, in this stage | A client link alone would be one nullable column and no module. Without a project entity there is nothing to link an inquiry *to*, so the "project" half of the acceptance test could not be demonstrated at all |
| Does a project carry a number, a status, or both? | **Both** | A number is how an operator and a future delivery note refer to a project; a status is how a finished job stops cluttering the register. Neither implies the other, and neither is derivable — `PRJ-0007` says nothing about whether the job is live |
| Can one inquiry belong to several projects? | **No — at most one** | The link is a single nullable column on `Inquiries`. A join table would let one inquiry be split across projects, which is not what an inquiry is: it is one enquiry about one job. If that ever changes it is a new, additive migration, never a reinterpretation of this column |

Three consequences that follow from those answers, decided here:

1. **A project number is allocated on save, not typed.** The editor accepts a
   number, but a blank one is filled from `PRJ-` + four digits where the digits
   are the **highest existing sequence + 1**, never a count + 1. A count
   re-uses a retired number as soon as one project is closed or removed;
   `max + 1` cannot. The unique index is the final authority; a collision is
   retried in a fresh transaction rather than trusted.
2. **A closed project keeps its number and loses nothing.** `Status` moves
   Active ⇄ Closed. There is no delete: a project that inquiries point at must
   stay resolvable, and a closed project is history, not rubbish.
3. **The client name on a project is a snapshot.** `Projects.ClientName` records
   what the client was called when the project was raised, so renaming a client
   later does not silently rewrite old projects. `ClientId` is the live link;
   the name is the label.

---

## Where it lives in the shell

| | |
| --- | --- |
| Workspace index | `11` (appended, so every earlier index keeps its meaning) |
| Rail entry | **Business → Projects** (`IconKind.Engineering`) |
| Banner subtitle | `Business · Project records and the inquiries that belong to them` |
| View | `step5.d.1.ProjectWorkspaceView.xaml` |

---

## Files

| File | Responsibility |
| --- | --- |
| `step5.a.1.Project.cs` | `ProjectStatus`, the `Project` entity, `ProjectDraft.FromProject` |
| `step5.a.2.ProjectRules.cs` | Max lengths, `PRJ-` prefix and digit count, normalisation, the comparison key, validation, `NextNumber`, `FormatNumber`, `TrySequence` — the one place the rules exist |
| `step5.b.1.ProjectSchema.cs` | Migration **1017**: the `Projects` table, its unique number index, two link columns on `Inquiries`, and `AssertShape` |
| `step5.b.2.ProjectDbContext.cs` | EF map on the existing `inquiries.db`; no `EnsureCreated`, no second database |
| `step5.b.3.IProjectRepository.cs` | Read/write port: queries, list rows, inquiry candidates, save and link results. **No delete member** |
| `step5.b.4.ProjectRepository.cs` | Initialisation (through the established initialiser, then a shape probe), and the write path: validation, UTC stamps, `SaveWithRetryAsync`, write-failure text |
| `step5.b.5.ProjectRepository.Reads.cs` | The four SELECTs, including the register's inquiry count |
| `step5.b.6.ProjectRepository.Link.cs` | Attach, detach, and the linked/unlinked inquiry reads |
| `step5.c.1.ProjectWorkspaceViewModel.cs` | Register state, filters, summary, selection, properties |
| `step5.c.2.ProjectWorkspaceViewModel.Load.cs` | Debounced load: one query, one cache, one projection |
| `step5.c.3.ProjectWorkspaceViewModel.Commands.cs` | The eleven commands and the editor round-trip |
| `step5.d.1.ProjectWorkspaceView.xaml` | The page: filter plate, chips, search, register, inquiry panel, inline editor |
| `step5.d.1.ProjectWorkspaceView.xaml.cs` | Composition only: build the view model, call `EnsureInitializedAsync` once, contain failures |
| `step5.f.tests/step5.f.1.ProjectSourceChecks.py` | The portable gate: 12 rule groups, mutation-tested |
| `step5.e.docs/step5.e.1.ProjectRecords-README.md` | This note |

---

## The page

Three parts, one page:

| Part | Contents |
| --- | --- |
| Filter plate | status chips (`All` / `Active` / `Closed`), a search box over number, name, client and site, and the register summary |
| Register | seven columns — number, project, client, site, status, inquiries, created — newest first, read-only, one row selected at a time |
| Inquiry panel | two stacked grids: the inquiries already linked to the selected project, and the unlinked ones that may be taken. Attach and detach are explicit buttons, never a drag |
| Editor | opens **in place of** the register: number, project name, client picker + a **NO CLIENT** button, client name, site, description, cancel and save |

The chips filter a cached row set; they never re-query. A single
`ReloadAsync` fills the cache and every status change projects from it, so a
chip cannot halve a list that was already halved by the previous chip — the
defect the first draft of this view model had.

### The editor round-trips

`New` opens an empty draft. `Edit` **re-reads the whole project by id** before
opening: the register row carries only what the grid prints, so editing from it
would have written an empty `Description` back over the stored one. If the
project has vanished between the load and the click, the editor says so and does
not save a stub.

### Free-text customers still work

The client picker is optional. **NO CLIENT** clears `ClientId`; the project then
belongs to nobody in `Clients` and the client name is simply what the operator
typed. That is the same freedom the inquiry form has always had, and nothing in
`step5` removes it.

---

## The database

Migration **1017** on the existing `inquiries.db`. Purely additive:

```sql
CREATE TABLE IF NOT EXISTS "Projects" (
    "Id"            INTEGER NOT NULL PRIMARY KEY AUTOINCREMENT,
    "ProjectNumber" TEXT(32)  NOT NULL,
    "ProjectName"   TEXT(200) NOT NULL,
    "ClientId"      INTEGER   NULL REFERENCES "Clients"("Id"),
    "ClientName"    TEXT(200) NOT NULL DEFAULT '',
    "Location"      TEXT(200) NOT NULL DEFAULT '',
    "Description"   TEXT(1000) NOT NULL DEFAULT '',
    "Status"        TEXT(16)  NOT NULL DEFAULT 'Active',
    "CreatedAt"     TEXT NOT NULL, "CreatedBy" TEXT NOT NULL DEFAULT '',
    "UpdatedAt"     TEXT NOT NULL, "UpdatedBy" TEXT NOT NULL DEFAULT '',
    CONSTRAINT "CK_Projects_Status" CHECK ("Status" IN ('Active','Closed'))
);
CREATE UNIQUE INDEX IF NOT EXISTS "IX_Projects_Number"       ON "Projects"("ProjectNumber");
CREATE        INDEX IF NOT EXISTS "IX_Projects_ClientId"     ON "Projects"("ClientId");
CREATE        INDEX IF NOT EXISTS "IX_Projects_Status_Name"  ON "Projects"("Status","ProjectName");

ALTER TABLE "Inquiries" ADD COLUMN "ClientId"        INTEGER NULL;   -- no default, no backfill
ALTER TABLE "Inquiries" ADD COLUMN "ProjectRecordId" INTEGER NULL;
```

`AssertShape` re-checks, inside the migration transaction: the table, the two
indexes, both new columns on `Inquiries`, **and that the legacy `CustomerId` and
`ProjectId` columns are still there**.

### The legacy placeholders

`Inquiries.CustomerId`, `Inquiries.ProjectId`, `Inquiries.SalesmanId` and
`Inquiries.PreparedByUserId` are historical integer placeholders that no code
path fills. `step5` does **not** adopt them:

- it does not write them (the gate reads the write clauses to prove it),
- it does not read them,
- it does not rename, retype or drop them.

A column that silently changes meaning is worse than a column that is duplicated
openly. `step5` adds `ClientId` and `ProjectRecordId`, and leaves the four
placeholders as they are.

### The link write

Attaching is **one statement**, not a read-then-write:

```sql
UPDATE "Inquiries"
   SET "ProjectRecordId" = $projectId
 WHERE "Id" = $inquiryId
   AND "ProjectRecordId" IS NULL
   AND EXISTS (SELECT 1 FROM "Projects" WHERE "Id" = $projectId);
```

- `IS NULL` in the WHERE clause means an attach can never steal an inquiry from
  another project, even if two windows race;
- `EXISTS` means a deleted project id cannot be linked to;
- `UpdatedAt` is **not** touched — linking a project is not editing the
  inquiry, and a historical inquiry's audit stamp must not move because someone
  filed it;
- no `INSERT`: this module links inquiries, it never creates them.

`DetachInquiryAsync` clears the one column, again without touching the inquiry's
own stamps.

### Historical inquiries

Every existing inquiry keeps `ClientId = NULL` and `ProjectRecordId = NULL` and
its `CustomerName`, `CustomerLocation`, `Project`, `Salesman`,
`PreparedByName` and `PreparedByRole` text byte-for-byte. There is **no
backfill and no name matching**: the gate fails if any comparison between an
inquiry's customer text and a client name appears anywhere in the module. A
guess written into the database is indistinguishable from a fact.

---

## Status, and what a closed project means

`ToggleStatusCommand` moves Active ⇄ Closed and nothing else. Closed projects
stay in the register (filterable), keep their number, keep their inquiries, and
can be reopened. There is no delete command, no `DELETE FROM`, and the gate
fails on both.

Client deactivation (`step2`) is likewise not deletion: a project whose client
is deactivated keeps its `ClientId`, and the register keeps showing the snapshot
name. `step5` never nulls a link in response to a client's status.

---

## Validation

`step5.f.1.ProjectSourceChecks.py` — 12 rule groups, all reading source:
additive-only migration; no delete path; legacy placeholders never written or
read; exactly two inquiry `UPDATE`s and only `ProjectRecordId` assigned; no
name-match comparison; numbering from the highest sequence, never a count;
attach still requires `IS NULL`, no `INSERT`, no `UpdatedAt`; every view binding
has a backing property; the editor loads the full project; 34 theme tokens
verified against the contract; the view is hosted, titled, marked and built.

The gate was **mutation-tested** — four deliberate defects, each caught:

| Mutation | Verdict |
| --- | --- |
| A `DELETE FROM "Projects"` path added to the reads partial | FAILED: *"contains DELETE FROM — a project is closed, never deleted"* |
| The link write also assigning the legacy `ProjectId` placeholder | FAILED: *"an inquiry UPDATE assigns ['ProjectId']"* |
| `NextNumber` allocating from `Count() + 1` | FAILED: *"allocates from a COUNT — closing a project would reuse its number"* |
| An `INSERT INTO "Inquiries"` added to the link partial | FAILED: *"contains an INSERT — this module links inquiries, it does not create them"* |

A checker that passes the defect it names is not a gate; this one does not.

The portable suite around it is also green with `step5` in scope — the XAML
property, layout-spacing and file-size checkers now scan
`step5.ProjectRecords/`, alongside the inquiry, theme, client, user and company
checks.

---

## What was deliberately not built

Project costing, budgets, profitability, time or material allocation, a
project-level document chain, moving inquiry ownership, a join table, a project
picker on the *inquiry* editor (the link is made from this page, so the inquiry
form is unchanged), and any automatic backfill.

## What is not yet verified

Nothing here has been compiled or rendered. There is no `dotnet` in the
development sandbox used for this round: the C# parses (tree-sitter), the XAML
parses (`ElementTree`), and every portable rule passes — but "it compiles" and
"it looks right" remain judgements until the owner builds it on Windows.
