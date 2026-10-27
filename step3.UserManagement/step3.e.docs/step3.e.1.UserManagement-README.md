# step3 — User Management

The staff directory the rest of the application will attribute work to: who
exists, which team they sit in, what they are allowed to do, and whether they are
still active.

Approved 2026-10-27 for User Management only. Company Profile, Project Records,
Inquiry integration and every document module remain unbuilt.

## What it does

- **Register** of users with search, status chips (All / Active / Inactive) and
  department and role filters.
- **Add / edit** a user: full name, username, department, role, active flag.
- **Activate / deactivate** — deactivation replaces deletion, so historical
  attribution keeps a target. There is no delete command anywhere.
- **First run**: while the directory is empty, the person at the workstation
  registers the Windows account they are already using as the first
  Administrator. The door closes as soon as any user exists.
- **Guard rails**: only an active Administrator may change the directory; the
  last active Administrator cannot be deactivated or demoted; nobody can
  deactivate their own account.

## What it deliberately does not do

- **No password, hash, salt, token or login screen.** The application has no
  credentialed login to authenticate against, and inventing one would be a
  fabricated security feature. A user's username is expected to match their
  Windows account name, which is what the app already reads through
  `Inquiry/Services/Audit/CurrentOperator.cs`.
- **No invented permission matrix.** Accountant, Salesman, Estimator, Foreman,
  Cutting and Double Glazing have no per-module rights in this stage because
  those modules do not exist. Only the rules stated by the owner are enforced.
- **No department-as-permission.** A department names a team; it never grants or
  denies anything, and the authorization code does not read it.
- **No client representative column.** `step2` reserved that for this stage, but
  the owner scoped this round to User Management only; the dependency is recorded
  in `docs/05` rather than half-built.

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
| `step3.c.1.UserEditorViewModel.cs` | editor presentation state |
| `step3.c.2.UserWorkspaceViewModel.cs` | register, filters, lifecycle commands, bootstrap |
| `step3.d.1.UserWorkspaceView.xaml` | register + command bar + bootstrap card |
| `step3.d.2.UserEditorView.xaml` | add/edit form |
| `step3.f.1.UserBehaviourChecks.cs` | behaviour checks against an isolated database |
| `step3.f.2.UserSourceChecks.py` | portable source/contract checks |

## Verification

- `python3 step3.UserManagement/step3.f.tests/step3.f.2.UserSourceChecks.py` — portable, no SDK required.
- `dotnet run --project step3.f.UserTests.csproj` — Windows/.NET 8; uses
  `AppDataRoot.TestFolderOverride`, so the real `%APPDATA%` database is never
  touched.

Windows build, WPF runtime and nine-theme visual verification have **not** been
run by the agent that wrote this module; the owner's MSBuild is the only compiler.
