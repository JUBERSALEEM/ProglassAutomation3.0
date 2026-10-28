# step5 — what it gives you, where it lives, and how to check it

Written for the owner, 2026-10-28. Everything below is either read from the
source or is a numbered step you can perform. Where something has never been
executed, it says so.

---

## 1. What you get

`step5` is one new page and one additive database change.

**The page** — a twelfth workspace tab, **Projects**, under `Business`:

| Part | What it does |
| --- | --- |
| Filter plate | All / Active / Closed chips, plus search over number, project, client and site |
| Register | Number, Project, Client, Site, Status, Inquiries, Created — newest first, read-only |
| Inquiry panel | The inquiries already filed under the selected project, and the unlinked ones you may take. Attach and Detach are buttons |
| Editor | Opens **in place of** the register: number, project name, client picker, **NO CLIENT**, client name, site, description, Cancel / Save |

**The database change** — migration **1017**, purely additive:

- one new table, `Projects` (unique project number, status Active/Closed);
- two new **nullable** columns on `Inquiries`: `ClientId` and `ProjectRecordId`.

What it deliberately does **not** do: it never fills in your historical
inquiries, never guesses which client a past inquiry belonged to by comparing
names, never renames or drops the existing `CustomerId`, `ProjectId`,
`SalesmanId` or `PreparedByUserId` columns, and never deletes a project — a
project is **closed**, not removed.

A one-off customer still needs no client record: pick **NO CLIENT** and type the
name. That behaviour existed before and is unchanged.

---

## 2. Where the application actually is

There is **no installer** in this repository — no MSI, no Inno Setup script, no
WiX project, no publish profile. So "installed" currently means "the build
output folder you copied somewhere". Two facts follow:

| | |
| --- | --- |
| Program file | `ProGlassAutomation.exe` in the build output, `bin\Debug\net8.0-windows7.0\` or `bin\Release\net8.0-windows7.0\` |
| Requirements | **.NET 8 Desktop Runtime (x64)** on the machine. Not bundled |
| Elevation | The manifest requests **no** elevation. It runs as a normal user |
| Registry | **Nothing.** The application writes no registry keys at all |
| Working folder | Irrelevant — the exe never reads or writes beside itself |

So the only thing that matters about "where the app is" is where you keep the
`.exe`; the data is not there.

## 3. Where the data actually is

Everything is per **Windows user account**, not per machine. If the owner signs
in as `Juber` and a colleague signs in as `Ali`, they see **two different
databases** on the same PC. That is the single most important thing on this page.

| What | Path |
| --- | --- |
| **Database** | `%APPDATA%\ProGlassAutomation\inquiries.db` |
| Database write-ahead files (normal, must stay beside it) | `inquiries.db-wal`, `inquiries.db-shm` |
| Pre-migration backups | `%APPDATA%\ProGlassAutomation\MigrationBackups\inquiries_pre_v1017_<stamp>.db` |
| Error log (the only technical log) | `%APPDATA%\ProGlassAutomation\Logs\proglass-errors.log` (+ `.1`–`.4`, 1 MB each) |
| Company logo files | `%APPDATA%\ProGlassAutomation\CompanyProfile\` |
| Inquiry attachments | `%APPDATA%\ProGlassAutomation\Inquiries\<inquiry id>\Attachments\` |
| Website-inbox settings (Supabase URL, encrypted bridge password) | `%APPDATA%\ProGlassAutomation\InquiryInbox\config.json` |
| Website-inbox drop folder (shared, may be used by a Node bridge) | `C:\ProgramData\ProGlass\CustomerInquiryInbox` *(default; configurable in the inbox settings)* |
| Theme customisation, imported fonts, remembered sign-in | `%LOCALAPPDATA%\ProGlassAutomation\` — `bento-customization.json`, `fonts\`, `device-session.json` |

**Fastest way to open it:** press `Win+R`, paste `%APPDATA%\ProGlassAutomation`,
press Enter. That folder is the whole application's state.

Why the split: the database, backups, logs, logos and attachments live in
`Roaming` because they are the business's data; the theme file, the fonts and
the "remember me on this device" token live in `Local` because they are a
property of *this* machine, not of the account. Nothing is written to
`Program Files`, and no second database exists anywhere.

---

## 4. How to check it — in order

Do these in sequence. Each has an expected result, so a failure is visible
immediately rather than at the end.

### Step 0 — protect the real data first (2 minutes, do not skip)

1. Close the application.
2. `Win+R` → `%APPDATA%\ProGlassAutomation` → Enter.
3. If `inquiries.db` exists, copy it (and `inquiries.db-wal` if present) to the
   Desktop. **Do not** copy only the `.db` while the app is running — the newest
   committed rows may still be in the `-wal`.

This is your undo. The application makes its own backup at migration time as
well, but a backup you took yourself is the one you can trust without reading
any code.

### Step 1 — it builds

```
dotnet build ProGlassAutomation.slnx -c Release
```

**Expected:** `Build succeeded`, 0 errors. Warnings: the five known ones
(`CS0168` in `step3.c.3.FirstRunViewModel.cs:273`, `CS0414` `_savedSummary`,
`CS4014` in `InquirySupport.cs:89`, `CS8600` in `SchemaMigrationRunner.cs:90`,
`WFAC010`) plus the pre-existing `NU1701` package-target warnings. **Any new
error is a real find — send me the first 20 lines.**

### Step 2 — it starts and the data is where this page says

1. Run the exe.
2. `Win+R` → `%APPDATA%\ProGlassAutomation` → Enter.
3. Look at the `Modified` column on `inquiries.db`.

**Expected:** it changed to just now. If the timestamp did not move, the
application is using a different folder than the one above — stop and tell me.

### Step 3 — the migration landed (the important one)

Open the database read-only and run these three queries. Either use **DB Browser
for SQLite** (free, GUI: *Open Database* → *Execute SQL*), or `sqlite3`:

```
sqlite3 "%APPDATA%\ProGlassAutomation\inquiries.db" "PRAGMA user_version;"
```

**Expected: `1017`.** That single number is the proof that migration 1017 ran and
committed. If it says `1016`, the new migration did not run — send me the log.

```
sqlite3 "%APPDATA%\ProGlassAutomation\inquiries.db" ".schema Projects"
```

**Expected:** a `CREATE TABLE Projects (...)` with `IX_Projects_Number` as a
**unique** index, and a `CHECK ("Status" IN ('Active','Closed'))`.

```
sqlite3 "%APPDATA%\ProGlassAutomation\inquiries.db" ^
  "SELECT COUNT(*) AS inquiries, COUNT(ClientId) AS with_client, COUNT(ProjectRecordId) AS with_project FROM Inquiries;"
```

**Expected:** `with_client` and `with_project` are **0** while `inquiries` is
whatever you actually have. That is not a bug — it is the guarantee: no
historical inquiry was touched, backfilled, or guessed at.

### Step 4 — the old columns survived

```
sqlite3 "%APPDATA%\ProGlassAutomation\inquiries.db" "PRAGMA table_info(Inquiries);"
```

**Expected:** `CustomerId`, `ProjectId`, `SalesmanId`, `PreparedByUserId`,
`CustomerName`, `CustomerLocation`, `Project`, `Salesman`, `PreparedByName`,
`PreparedByRole` **all still listed**, unchanged, plus the two new nullable
columns at the end. If any of the ten is missing, stop — that would be data
loss, and the copy on your Desktop is the fix.

### Step 5 — your existing inquiries still read the same

Open the **Inquiry** tab. Pick three inquiries you know well, including the
oldest you have and one with a typed customer name.

**Expected:** identical customer name, location, project, salesman and prepared-by
text. Nothing has been rewritten, re-cased or trimmed.

### Step 6 — the Projects page works

1. Tab **Projects** (`_Projects`, index 11 — the twelfth tab).
2. **NEW PROJECT**. Leave the number **blank**, type a name and a site, pick a
   client from the dropdown, **SAVE**.

**Expected:** a row appears with a number like `PRJ-0001`. The dropdown shows
client **names**, not the word `ProjectClientOption` — if it shows a type name,
a theme template is missing its item template and I need to know.

3. Save a second project with **NO CLIENT** and a typed client name.
   **Expected:** it saves, and the client column shows what you typed.

4. Click a row, then **EDIT**. Change only the description, save.

**Expected:** number, name, client and site are all still there, and only the
description changed. *(This is the defect I caught in my own draft: an editor
opened from a register row used to save an empty description over the stored
one. It re-reads the project now — this step is the test for it.)*

### Step 7 — numbering does not reuse

1. Close project #2 (the **Close** action on the row).
2. Create a third project with a blank number.

**Expected:** `PRJ-0003`, **not** `PRJ-0002`. A number is never reused, because
the next number is the highest existing sequence + 1, not a row count.

### Step 8 — linking an inquiry does not edit it

1. Select project #1 in the register.
2. In the right-hand panel, take an unlinked inquiry and press **ATTACH**.
3. Open that inquiry in the **Inquiry** tab.

**Expected:** it now appears under the project; and in the inquiry itself the
customer text, dates and *last modified* are **unchanged**. Filing an inquiry is
not the customer editing it, so the inquiry's own `UpdatedAt` must not move.

4. Try to attach the same inquiry to project #2.
   **Expected:** it is no longer offered — one inquiry belongs to at most one
   project.

### Step 9 — nine themes, Light and Dark

Switch every theme in **Light**, then in **Dark**, with the Projects tab open.

**Expected:** the register, the panel and the editor all repaint; no white box on
a dark theme, no black text on a dark card, no clipped column, no overlapping
label. This is the check I cannot do: I have no Windows and no renderer here.

### Step 10 — the log stays quiet

`Win+R` → `%APPDATA%\ProGlassAutomation\Logs` → open `proglass-errors.log`.

**Expected:** **no new entries** from steps 1–9. The log is failure-only, so
anything in it is real. Send me the tail if it is not empty.

---

## 5. If something is wrong, undo

1. Close the application.
2. Copy your Desktop backup of `inquiries.db` back over
   `%APPDATA%\ProGlassAutomation\inquiries.db`.
3. Delete `inquiries.db-wal` and `inquiries.db-shm` **before** restarting, so the
   restored file is not re-merged with the newer write-ahead log.

Migration 1017 only *adds*, so an older build of the application still reads a
database it has already migrated: the extra table and the two extra columns are
simply ignored. You are not locked in.

---

## 6. What is next

1. **These ten steps, on your machine.** That is the whole of `step5`'s
   acceptance, and it is the only thing standing between "written" and "works".
   Nothing in this module has ever been compiled or rendered.
2. **The `G1` Foundation Review** — `step2`–`step5` reviewed together. Its report
   can be written now; its *evidence* is exactly the Windows runs that are still
   missing for step2, step3, step4 and step5. `G1` cannot honestly pass on
   source-only checks.
3. **Only then `step6`** Proforma Invoice, and the rest of the document chain
   (`step7`–`step11`), which `G1` blocks by rule.
4. **Two decisions still waiting on you**, both older than this module: whether
   to attack the residual ~435 ms theme switch or accept it, and the Night Berry
   dark-mode visual check.

### Two things I found while answering this, and deliberately did not change

- **The inbox root is `C:\ProgramData\ProGlass\CustomerInquiryInbox`**, shared by
  the Node bridge. It is created by the first user who runs the app; on a
  multi-user PC a second Windows user may not be able to write into it. This is
  existing Inquiry behaviour, not `step5`, and changing it needs your decision.
- **Sign-in and the database are per Windows user.** Two people on one PC get two
  separate businesses' worth of data. If that is not what you intend, it is a
  bigger decision than this module and belongs in `G1`.
