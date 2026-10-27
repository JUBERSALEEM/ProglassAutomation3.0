# step3 — User Management

The staff directory the rest of the application will attribute work to: who
exists, which team they sit in, what they are allowed to do, and whether they are
still active.

Approved 2026-10-27 for User Management only. Company Profile, Project Records,
Inquiry integration and every document module remain unbuilt.

## What it does

- **Register** of users with search, status chips (All / Active / Inactive) and
  department and role filters.
- **Add / edit** a user: full name, username, department, role, active flag, and
  the password that lets them sign in.
- **Activate / deactivate** — deactivation replaces deletion, so historical
  attribution keeps a target. There is no delete command anywhere.
- **Sign in**: every user has a password. The shell shows a sign-in gate before
  any page, and the verified row is the identity every write is attributed to.
- **First run**: while the directory is empty, the person at the workstation
  registers the Windows account they are already using as the first
  Administrator, choosing a password on the same form. The door closes as soon as
  any user exists.
- **Guard rails**: only an active Administrator may change the directory; the
  last active Administrator cannot be deactivated or demoted; nobody can
  deactivate their own account; a non-administrator reads their own record only.

## What it deliberately does not do

- **No password reset by email, no external identity provider, no token.** The
  credential is local, salted and hashed (PBKDF2-HMAC-SHA256, 210 000 iterations,
  16-byte salt, 32-byte hash, constant-time compare — `step3.a.5.UserPassword`).
  There is no "forgot password" link and no directory integration; an
  administrator resets a password and the person replaces it at the next sign-in.
- **No screen-hiding as security.** A screen that only an administrator may use is
  refused by the repository as well, and the read scope is applied in the query
  (see *Sign-in and read scope* below).
- **No invented permission matrix.** Accountant, Salesman, Estimator, Foreman,
  Cutting and Double Glazing have no per-module rights in this stage because
  those modules do not exist. Only the rules stated by the owner are enforced.
- **No department-as-permission.** A department names a team; it never grants or
  denies anything, and the authorization code does not read it.
- **No client representative column.** `step2` reserved that for this stage, but
  the owner scoped this round to User Management only; the dependency is recorded
  in `docs/05` rather than half-built.

## First run (empty directory)

A new installation has no users, so nobody can be identified and no administrator
can exist. The application therefore cannot be administered, and telling the
operator to "ask an administrator" is circular. One service owns the whole flow —
`step3.b.6.FirstRunService` — and two screens present it: the startup gate and the
card on the Users page.

1. **The gate comes first.** After the shell loads, `MainWindow` asks
   `IFirstRunService.InspectAsync()`. When the directory is empty it shows
   `step3.d.3.FirstRunSetupView` as an overlay over the whole shell (its dimmed
   background swallows clicks, so nothing behind it can be used), with the caret in
   the first field. `Set up later` dismisses the gate for this session; the Users
   page keeps the same step.
2. **The account is read, not typed.** The form shows the Windows account this
   process runs under (`CurrentOperator.Username`) and offers no username field.
   That is what makes "register me" actually work: the row created is the row this
   workstation matches on the next launch. A typable username is how a first
   administrator ends up with an account the workstation does not match — the
   deadlock this gate exists to prevent.
3. **The write is guarded twice.** `FirstRunService` refuses when the directory is
   no longer empty; `UserRepository.RegisterFirstAdministratorAsync` re-checks
   inside the insert transaction, inserts the Administrator row, re-counts before
   committing, and aborts if the count is not one. Two workstations racing produce
   one administrator and one refusal.
4. **The result is verified, not assumed.** After the write the service re-reads the
   directory and requires that the workstation account now matches an active row.
   If it does not, the operator is told both account names and what to edit instead
   of being left on a "not registered" screen.
5. **Then the app opens as that Administrator.** The gate closes and the shell
   selects the Users workspace, where the register is usable and further users can
   be added.

While the directory is empty the read-only notice is suppressed — the setup card is
the message. If the account matches an **inactive** user instead, the sign-in gate
refuses it with the inactive reason; the account is never silently re-activated.
There is no default account and no anonymous access anywhere in this flow.

## Sign-in and read scope

`step3.d.4.SignInView` covers the shell on start-up; `step3.c.4.SignInViewModel`
owns the two steps. First the username and password are verified against the
directory. Then, when the password was one an administrator set, the same card asks
for a new one before it hands the session over — a temporary password that can be
used for a week is not temporary.

- The identity is `AuthenticationContext` (`step3.b.7`), set only by a verified
  sign-in. Every write resolves its operator from it, so a directory change without
  a session is refused exactly like a directory change by a normal user.
- A non-administrator's register is narrowed in the query, not in the view: the
  repository applies the ceiling first (own row only) and the workspace passes the
  same id, so the status counts cannot leak how many people exist either.
- The Clients register applies the same rule to representatives: a normal user reads
  the clients they represent, an Administrator reads the whole book.
- The Inquiry register applies it too: an inquiry is "related to" the person who is
  its salesman or its preparer, and the single-record fetch refuses an inquiry
  outside that set, so a row cannot be opened by id alone.

## Layout contract

Every gap and size in these screens comes from the shared layout scale in the theme
contract (`UiGapField`, `UiGapGroup`, `UiGapSection`, `UiGapAction`, `UiGapCluster`,
`UiPagePadding`, `UiCardPadding`, `UiActionMinHeight`, `UiActionPadding`), defined
identically in all eighteen theme dictionaries:

- the command bar is one row of two wrapping clusters: filters in the flexible
  column, actions in a content-sized column. A `WrapPanel` wraps instead of
  overflowing, so nothing can be drawn over anything at any width (including with
  the sidebar open), yet at normal width the whole bar reads as a single strip;
- each filter carries a visible **label** on its left ("Department", "Role", the
  Inquiry toolbar's arrangement) and its **selection, and only that**, on the face:
  "All departments" / "All roles" until something is chosen, then the catalogue's
  own wording verbatim. Printing the field word inside every row was tried and
  rejected — it reads as a placeholder and doubles the word in "Cutting
  Department"; `AGENTS.md` rule E records the whole sequence. The selection is
  bound as a **ROW**, because a code-string `SelectedValue` applied before the rows
  exist is exactly what left these boxes blank, and the box is pointed at a real row
  once the catalogue loads;
- every card on the page pins `UiCardPadding` (16 px in all eighteen theme
  dictionaries). `Card` and `Plate` ship different paddings per theme, which is what
  made the register's table start a few pixels right of the bar above it;
- the register's actions are buttons, not a one-item "⋯" menu: Edit, the status
  action, Refresh and the single primary action all sit in the bar;
- page padding is applied once on the view, so hidden message rows leave no gap;
- action buttons are at least 36 px high, 8 px apart inside a cluster and 16 px
  apart between clusters;
- the editor scrolls in exactly one region and its card is sized by its content, so
  a short form has no empty band above its buttons;
- the shell keeps the sidebar and the content in separate grid columns, and the
  content column clips to its own region.

`step3.f.2` enforces the machine-checkable half of this, so a future edit fails a
check instead of returning the defect to the owner.

## Files

| File | Role |
| --- | --- |
| `step3.a.1.User.cs` | entity, status wording, editable draft |
| `step3.a.2.UserCatalog.cs` | the seven departments and seven roles, and their lookup rules |
| `step3.a.3.UserRules.cs` | normalization, validation, username comparison key |
| `step3.a.4.UserAuthorization.cs` | who may manage users, who may be attributed, last-administrator guard |
| `step3.b.1.UserSchema.cs` | migration 1013: tables, catalogue seed, indexes, shape assertions |
| `step3.b.2.IUserRepository.cs` | persistence contract and projections |
| `step3.b.3.UserDbContext.cs` | EF Core mapping for `Users` only |
| `step3.b.4.UserRepository.cs` | reads/writes, permission resolution inside the transaction |
| `step3.b.5.UserSession.cs` | the running operator as a directory row; the Prepared-By stamp |
| `step3.b.6.FirstRunService.cs` | the first-run flow: inspect, register the workstation account, verify it |
| `step3.c.1.UserEditorViewModel.cs` | editor presentation state |
| `step3.c.2.UserWorkspaceViewModel.cs` | register, filters, lifecycle commands, bootstrap card |
| `step3.c.3.FirstRunViewModel.cs` | presentation for the startup first-run gate |
| `step3.d.1.UserWorkspaceView.xaml` | register + command bar + bootstrap card |
| `step3.d.2.UserEditorView.xaml` | add/edit form |
| `step3.d.3.FirstRunSetupView.xaml` | the startup gate shown while the directory is empty |
| `step3.f.1.UserBehaviourChecks.cs` | behaviour checks against an isolated database |
| `step3.f.2.UserSourceChecks.py` | portable source, contract and layout checks |

## Verification

- `python3 step3.UserManagement/step3.f.tests/step3.f.2.UserSourceChecks.py` — portable, no SDK required.
- `dotnet run --project step3.f.UserTests.csproj` — Windows/.NET 8; uses
  `AppDataRoot.TestFolderOverride`, so the real `%APPDATA%` database is never
  touched.

Windows build, WPF runtime and nine-theme visual verification have **not** been
run by the agent that wrote this module; the owner's MSBuild is the only compiler.
