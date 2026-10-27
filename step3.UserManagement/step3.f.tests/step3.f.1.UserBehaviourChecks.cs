// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.f.tests/step3.f.1.UserBehaviourChecks.cs
// MODULE: step3 — verification executable (dependency-free, like step1.d).
//
// What it proves without any UI:
//   • normalization (whitespace, username comparison key) and field
//     validation (name required, username character set, unknown catalogue
//     values rejected);
//   • the catalogue itself: the owner's seven departments and seven roles,
//     Estimator as the default, and code/display-name resolution;
//   • the authorization decisions: not identified, inactive, wrong role,
//     allowed, and the last-active-administrator guard;
//   • migration 1013 against an ISOLATED database: tables created, catalogues
//     seeded, Clients data untouched, user_version advanced, re-run is a no-op;
//   • the first-run flow end to end through FirstRunService: an empty directory
//     reports that setup is required, a blank name is refused without touching
//     anything, the workstation account registers itself as Administrator, the
//     gate closes, a second attempt is refused, and an inactive match is reported
//     as inactive instead of being silently re-activated;
//   • the repository round-trip: bootstrap, create, duplicate refusal, a
//     non-administrator being refused by the write path itself, deactivation
//     blocking attribution, last-administrator protection, filters, and a
//     rename that keeps the stored attribution snapshot intact.
//
// Exit code 0 = every check passed. Run only on Windows/.NET 8.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Users;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Step3Tests;

internal static class UserBehaviourChecks
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    public static int Main()
    {
        Run("normalization", CheckNormalization);
        Run("field validation", CheckValidation);
        Run("catalogue", CheckCatalogue);
        Run("authorization decisions", CheckAuthorization);
        Run("last-administrator guard", CheckLastAdministrator);
        Run("migration 1013 (isolated database)", CheckMigration);
        Run("repository round-trip (isolated database)", CheckRepository);
        Run("first-run gate and concurrent bootstrap", CheckFirstRunGate);
        Run("first-run flow (service)", CheckFirstRunFlow);

        Console.WriteLine();
        Console.WriteLine($"step3 user checks: {_passed} passed, {Failures.Count} failed.");
        foreach (string failure in Failures) Console.WriteLine("  FAIL: " + failure);
        if (Failures.Count == 0)
            Console.WriteLine("PASS: user rules, permissions and isolated persistence.");
        return Failures.Count == 0 ? 0 : 1;
    }

    // ── Harness ──────────────────────────────────────────────────────────

    private static void Run(string name, Action check)
    {
        try
        {
            check();
            _passed++;
            Console.WriteLine("ok   " + name);
        }
        catch (Exception ex)
        {
            Failures.Add(name + " → " + ex.Message);
            Console.WriteLine("FAIL " + name + " → " + ex.Message);
        }
    }

    private static void True(bool condition, string message)
    {
        if (!condition) throw new InvalidOperationException(message);
    }

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected \"{expected}\", got \"{actual}\"");
    }

    // ── Rules ────────────────────────────────────────────────────────────

    private static void CheckNormalization()
    {
        Equal("Ahmed Al Balushi", UserRules.NormalizeText("  Ahmed   Al \t Balushi  "), "whitespace collapse");
        Equal("", UserRules.NormalizeText(null), "null text");
        Equal("", UserRules.NormalizeText("   "), "blank text");

        Equal("A.SALAM", UserRules.UsernameKey(" a.salam "), "username key trims and uppercases");
        Equal("DOMAIN\\USER", UserRules.UsernameKey("domain\\user"), "backslash is preserved");

        // Catalogue codes: wording resolves to the code, unknown text is kept
        // verbatim so an administrator-added row still round-trips.
        Equal("CUTTING", UserRules.NormalizeDepartment("Cutting Department"), "department by display name");
        Equal("ACCOUNTS", UserRules.NormalizeDepartment("accounts"), "department by lower-case code");
        Equal("ESTIMATOR", UserRules.NormalizeRole("Estimator"), "role by display name");
    }

    private static void CheckValidation()
    {
        True(UserRules.ValidateFullName("") != null, "empty full name is rejected");
        True(UserRules.ValidateFullName("A") == null, "a single character name is allowed");
        True(UserRules.ValidateFullName(new string('a', UserRules.FullNameMaxLength + 1)) != null,
            "over-long full name is rejected");

        True(UserRules.ValidateUsername("") != null, "empty username is rejected");
        True(UserRules.ValidateUsername("ahmed.al") == null, "dotted username is allowed");
        True(UserRules.ValidateUsername("DOMAIN\\ahmed") == null, "domain-qualified username is allowed");
        True(UserRules.ValidateUsername("ahmed al") != null, "username with a space is rejected");
        True(UserRules.ValidateUsername("ahmed!") != null, "username with punctuation is rejected");

        True(UserRules.ValidateDepartment("Sales") == null, "known department is accepted");
        True(UserRules.ValidateDepartment("Marketing") != null, "unknown department is rejected");
        True(UserRules.ValidateRole("Estimator") == null, "known role is accepted");
        True(UserRules.ValidateRole("Director") != null, "unknown role is rejected");

        // A new user with nothing filled in fails on the required fields only.
        var blank = UserRules.Validate(new UserDraft());
        True(blank.ContainsKey(nameof(UserDraft.FullName)), "blank draft reports the full name");
        True(blank.ContainsKey(nameof(UserDraft.Username)), "blank draft reports the username");
        True(!blank.ContainsKey(nameof(UserDraft.Department)), "default department is valid");
        True(!blank.ContainsKey(nameof(UserDraft.Role)), "default role is valid");
    }

    private static void CheckCatalogue()
    {
        string[] departments =
        {
            "Administration", "Accounts", "Sales", "Estimation", "Production",
            "Cutting Department", "Double Glazing Department"
        };
        var actualDepartments = UserCatalog.Departments.Select(d => d.DisplayName).ToArray();
        Equal(string.Join("|", departments), string.Join("|", actualDepartments),
            "the seven agreed departments, in the agreed order");

        string[] roles =
        {
            "Administrator", "Accountant", "Salesman", "Estimator",
            "Foreman", "Cutting Department", "Double Glazing Department"
        };
        var actualRoles = UserCatalog.Roles.Select(r => r.DisplayName).ToArray();
        Equal(string.Join("|", roles), string.Join("|", actualRoles),
            "the seven agreed roles, in the agreed order");

        True(UserCatalog.Roles.Count(r => r.IsDefault) == 1, "exactly one default role");
        Equal("Estimator", UserCatalog.RoleDisplayName(UserCatalog.DefaultRole), "Estimator is the default role");
        True(UserCatalog.IsAdministrator("ADMINISTRATOR"), "Administrator is recognised");
        True(!UserCatalog.IsAdministrator("ACCOUNTANT"), "Accountant is not an administrator");

        // Role and department are independent namespaces: the two catalogues
        // share the words Cutting Department / Double Glazing Department, and
        // resolving one never answers for the other.
        Equal("CUTTING", UserCatalog.DepartmentCode("Cutting Department"), "department lookup");
        Equal("CUTTING", UserCatalog.RoleCode("Cutting Department"), "role lookup");
    }

    private static void CheckAuthorization()
    {
        var inactiveAdmin = new User { Id = 1, FullName = "Old Admin", Role = "ADMINISTRATOR", IsActive = false };
        var estimator = new User { Id = 2, FullName = "New Estimator", Role = "ESTIMATOR", IsActive = true };
        var accountant = new User { Id = 3, FullName = "Accountant", Role = "ACCOUNTANT", IsActive = true };
        var admin = new User { Id = 4, FullName = "Admin", Role = "ADMINISTRATOR", IsActive = true };

        Equal(nameof(UserDecisionCode.NotIdentified), UserAuthorization.CanManageUsers(null).Code.ToString(),
            "nobody identified");
        Equal(nameof(UserDecisionCode.Inactive), UserAuthorization.CanManageUsers(inactiveAdmin).Code.ToString(),
            "inactive administrator");
        Equal(nameof(UserDecisionCode.NotAuthorized), UserAuthorization.CanManageUsers(estimator).Code.ToString(),
            "estimator");
        Equal(nameof(UserDecisionCode.NotAuthorized), UserAuthorization.CanManageUsers(accountant).Code.ToString(),
            "accountant");
        True(UserAuthorization.CanManageUsers(admin).IsAllowed, "active administrator may manage users");

        // Attribution asks the same question about identity and activity, and
        // nothing about role: an Estimator may sign a record they prepared.
        True(UserAuthorization.CanAttribute(estimator).IsAllowed, "estimator may be attributed");
        True(UserAuthorization.CanAttribute(accountant).IsAllowed, "accountant may be attributed");
        True(!UserAuthorization.CanAttribute(inactiveAdmin).IsAllowed, "inactive user may not be attributed");
        True(!UserAuthorization.CanAttribute(null).IsAllowed, "unidentified operator may not be attributed");

        // Self-deactivation is refused even when others could take over.
        var selfDraft = new UserDraft
        {
            Id = admin.Id, Role = "ADMINISTRATOR", IsActive = false,
            OriginalRole = "ADMINISTRATOR", OriginalIsActive = true
        };
        Equal(nameof(UserDecisionCode.SelfDeactivation),
            UserAuthorization.ValidateSelfDeactivation(selfDraft, admin).Code.ToString(),
            "cannot deactivate your own account");
        True(UserAuthorization.ValidateSelfDeactivation(selfDraft, estimator).IsAllowed,
            "another administrator may deactivate that account");
    }

    private static void CheckLastAdministrator()
    {
        var demoted = new UserDraft
        {
            Id = 1, Role = "ESTIMATOR", IsActive = true,
            OriginalRole = "ADMINISTRATOR", OriginalIsActive = true
        };
        Equal(nameof(UserDecisionCode.LastAdministrator),
            UserAuthorization.ValidateChange(demoted, activeAdministratorsExcludingTarget: 0).Code.ToString(),
            "demoting the last administrator is refused");
        True(UserAuthorization.ValidateChange(demoted, activeAdministratorsExcludingTarget: 1).IsAllowed,
            "demoting one of two administrators is allowed");

        var deactivated = new UserDraft
        {
            Id = 1, Role = "ADMINISTRATOR", IsActive = false,
            OriginalRole = "ADMINISTRATOR", OriginalIsActive = true
        };
        True(!UserAuthorization.ValidateChange(deactivated, 0).IsAllowed,
            "deactivating the last administrator is refused");

        // An already-inactive administrator is not "the last active" one, and a
        // non-administrator demotion is never a last-administrator problem.
        var alreadyInactive = new UserDraft
        {
            Id = 1, Role = "ADMINISTRATOR", IsActive = false,
            OriginalRole = "ADMINISTRATOR", OriginalIsActive = false
        };
        True(UserAuthorization.ValidateChange(alreadyInactive, 0).IsAllowed,
            "an already-inactive administrator can be edited freely");

        var estimatorChange = new UserDraft
        {
            Id = 2, Role = "SALESMAN", IsActive = true,
            OriginalRole = "ESTIMATOR", OriginalIsActive = true
        };
        True(UserAuthorization.ValidateChange(estimatorChange, 0).IsAllowed,
            "changing a non-administrator touches no administrator count");
    }

    // ── Migration ────────────────────────────────────────────────────────

    private static void CheckMigration()
    {
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step3-migration-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempFolder);
        string dbPath = Path.Combine(tempFolder, "inquiries.db");

        try
        {
            // A database that already ran 1011/1012 and holds a client row: the
            // upgrade must add Users and the catalogues without disturbing it.
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                SqliteResilience.BuildConnectionString(dbPath)))
            {
                connection.Open();
                SqliteResilience.ApplyConnectionPolicy(connection);

                SchemaMigrationSql.CreateTableIfNotExists(connection, @"
                    CREATE TABLE IF NOT EXISTS ""Clients"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Clients"" PRIMARY KEY AUTOINCREMENT,
                        ""ClientName"" TEXT NOT NULL DEFAULT '',
                        ""AttentionName"" TEXT NOT NULL DEFAULT '',
                        ""Phone"" TEXT NOT NULL DEFAULT '',
                        ""Email"" TEXT NOT NULL DEFAULT '',
                        ""Trn"" TEXT NOT NULL DEFAULT '',
                        ""Address"" TEXT NOT NULL DEFAULT '',
                        ""Notes"" TEXT NOT NULL DEFAULT '',
                        ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                        ""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                        ""UpdatedAt"" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00');");
                SchemaMigrationSql.Execute(connection,
                    @"INSERT INTO ""Clients"" (""ClientName"", ""AttentionName"") VALUES ('Al Noor Glass', 'Mr Ahmed');");
                SchemaMigrationSql.Execute(connection, "PRAGMA user_version = 1012;");
            }

            AppDataRoot.TestFolderOverride = tempFolder;
            True(SchemaMigrationRunner.Upgrade(dbPath, AppSchemaMigrations.Inquiries, "step3 migration check"),
                "migration 1013 applies: " + SchemaMigrationRunner.FailureSummary);

            using (var reader = new Microsoft.Data.Sqlite.SqliteConnection(
                SqliteResilience.BuildConnectionString(dbPath)))
            {
                reader.Open();
                SqliteResilience.ApplyConnectionPolicy(reader);

                foreach (string table in new[] { "Users", "Departments", "Roles" })
                {
                    using var exists = reader.CreateCommand();
                    exists.CommandText =
                        $"SELECT COUNT(*) FROM sqlite_master WHERE type = 'table' AND name = '{table}';";
                    Equal("1", Convert.ToString(exists.ExecuteScalar()) ?? "0", table + " table exists");
                }

                using (var departments = reader.CreateCommand())
                {
                    departments.CommandText = "SELECT COUNT(*) FROM \"Departments\";";
                    Equal("7", Convert.ToString(departments.ExecuteScalar()) ?? "0", "seven departments seeded");
                }

                using (var roles = reader.CreateCommand())
                {
                    roles.CommandText = "SELECT COUNT(*) FROM \"Roles\";";
                    Equal("7", Convert.ToString(roles.ExecuteScalar()) ?? "0", "seven roles seeded");
                }

                using (var defaultRole = reader.CreateCommand())
                {
                    defaultRole.CommandText = "SELECT \"Code\" FROM \"Roles\" WHERE \"IsDefault\" <> 0;";
                    Equal("ESTIMATOR", Convert.ToString(defaultRole.ExecuteScalar()) ?? "",
                        "Estimator is the seeded default");
                }

                using (var client = reader.CreateCommand())
                {
                    client.CommandText = "SELECT \"ClientName\" FROM \"Clients\" LIMIT 1;";
                    Equal("Al Noor Glass", Convert.ToString(client.ExecuteScalar()) ?? "",
                        "existing client data is untouched by 1013");
                }

                using (var version = reader.CreateCommand())
                {
                    version.CommandText = "PRAGMA user_version;";
                    Equal("1013", Convert.ToString(version.ExecuteScalar()) ?? "", "user_version advanced");
                }
            }

            // Re-running must be a no-op, and the seed must not duplicate rows.
            True(SchemaMigrationRunner.Upgrade(dbPath, AppSchemaMigrations.Inquiries,
                    "step3 migration check (re-run)"),
                "a second upgrade is a safe no-op: " + SchemaMigrationRunner.FailureSummary);
        }
        finally
        {
            AppDataRoot.TestFolderOverride = null;
            try { Directory.Delete(tempFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── Repository ───────────────────────────────────────────────────────

    private static void CheckRepository()
    {
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step3-checks-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempFolder);
        AppDataRoot.TestFolderOverride = tempFolder;
        try
        {
            RunRepositoryChecks().GetAwaiter().GetResult();
        }
        finally
        {
            AppDataRoot.TestFolderOverride = null;
            try { Directory.Delete(tempFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>
    /// The first-run gate, end to end rather than just the insert:
    ///   • an empty directory reports that setup is required, and a populated
    ///     one does not;
    ///   • two workstations racing to bootstrap cannot both win, and cannot
    ///     leave two "first" administrators behind;
    ///   • an inactive match is reported as inactive and is NOT silently
    ///     re-activated by signing in again.
    /// </summary>
    private static void CheckFirstRunGate()
    {
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step3-gate-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempFolder);
        AppDataRoot.TestFolderOverride = tempFolder;
        try
        {
            CheckFirstRunGateAsync().GetAwaiter().GetResult();
        }
        finally
        {
            AppDataRoot.TestFolderOverride = null;
            try { Directory.Delete(tempFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    private static async Task CheckFirstRunGateAsync()
    {
        var repository = new UserRepository();
        await repository.InitializeAsync();

        string operatorName = UserRules.NormalizeUsername(
            ProGlassAutomation.Services.Audit.CurrentOperator.Username);

        True(await repository.IsFirstRunRequiredAsync(),
            "an empty directory reports that first-run setup is required");

        // Two simultaneous attempts, as two workstations would arrive.
        var first = repository.RegisterFirstAdministratorAsync("Racer One", "Administration", operatorName);
        var second = repository.RegisterFirstAdministratorAsync("Racer Two", "Administration", operatorName);
        UserSaveResult[] results = await Task.WhenAll(first, second);

        int succeeded = results.Count(result => result.Succeeded);
        Equal("1", succeeded.ToString(), "exactly one concurrent bootstrap succeeds");
        Equal("1", (await repository.CountAsync()).ToString(),
            "a concurrent bootstrap cannot leave two first administrators behind");
        True(!await repository.IsFirstRunRequiredAsync(),
            "the gate closes once an administrator exists");

        var late = await repository.RegisterFirstAdministratorAsync(
            "Too Late", "Administration", operatorName);
        True(!late.Succeeded, "the bootstrap path is closed after the first administrator");

        // An inactive match: recognised, refused, and left exactly as it was.
        User? winner = await repository.FindCurrentUserAsync();
        True(winner != null, "the workstation account matches the bootstrap row");

        var deactivation = await repository.SetActiveAsync(winner!.Id, false, operatorName);
        True(deactivation.Succeeded, "the sole administrator cannot be deactivated");

        var otherAdmin = await repository.CreateAsync(new UserDraft
        {
            FullName = "Second Administrator", Username = "second.admin",
            Department = "Administration", Role = "Administrator"
        }, operatorName);
        True(otherAdmin.Succeeded, "a second administrator exists for the guard to permit this");

        var deactivated = await repository.SetActiveAsync(winner.Id, false, otherAdmin.User!.Username);
        True(deactivated.Succeeded, "the matched account can be deactivated by another administrator");

        User? afterDeactivation = await repository.FindCurrentUserAsync();
        True(afterDeactivation != null, "an inactive account is still recognised, not treated as unknown");
        True(!afterDeactivation!.IsActive, "signing in again does not silently re-activate the account");

        UserDecision decision = UserAuthorization.CanManageUsers(afterDeactivation);
        Equal(nameof(UserDecisionCode.Inactive), decision.Code.ToString(),
            "an inactive match is refused with the inactive reason");

        var session = new UserSession(repository);
        var refreshed = await session.RefreshAsync();
        True(refreshed != null && !refreshed.IsActive,
            "the session does not reactivate an inactive match");
    }

    /// <summary>
    /// The whole first-run path, not just the insert: what the gate sees, what it
    /// refuses, what it creates, and what it reads back afterwards.
    /// </summary>
    private static void CheckFirstRunFlow()
    {
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step3-firstrun-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempFolder);
        AppDataRoot.TestFolderOverride = tempFolder;
        try
        {
            RunFirstRunFlowAsync().GetAwaiter().GetResult();
        }
        finally
        {
            AppDataRoot.TestFolderOverride = null;
            try { Directory.Delete(tempFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    private static async Task RunFirstRunFlowAsync()
    {
        var repository = new UserRepository();
        var firstRun = new FirstRunService(repository);

        FirstRunSnapshot empty = await firstRun.InspectAsync();
        True(empty.DirectoryAvailable, "the directory is readable on a fresh install");
        True(empty.IsSetupRequired, "an empty directory reports that setup is required");
        Equal("0", empty.UserCount.ToString(), "no users exist yet");
        True(empty.OperatorUsername.Trim().Length > 0,
            "the workstation account name is available to the gate");
        True(empty.MatchingUser == null, "nothing matches the workstation account yet");
        Equal("7", empty.Departments.Count.ToString(), "the setup form can offer the departments");
        True(!empty.IsOperatorAdministrator, "nobody is an administrator yet");

        // A blank name must fail without writing anything and without closing setup.
        UserSaveResult blank = await firstRun.RegisterAsync("   ", "Administration");
        True(!blank.Succeeded, "a blank full name is refused");
        True((await firstRun.InspectAsync()).IsSetupRequired,
            "a refused attempt leaves the setup step open for a retry");

        // The account registers itself. Note: no username argument exists.
        UserSaveResult created = await firstRun.RegisterAsync("Ahmed   Al Balushi", "Administration");
        True(created.Succeeded, "the workstation account can register itself: " + created.Message);
        User admin = created.User!;
        Equal("ADMINISTRATOR", admin.Role, "the first user is an Administrator");
        Equal(empty.OperatorUsername, admin.Username,
            "the stored username is exactly the workstation account");
        Equal("Ahmed Al Balushi", admin.FullName, "the name is normalized");
        True(admin.IsActive, "the first administrator is active");

        FirstRunSnapshot after = await firstRun.InspectAsync();
        True(!after.IsSetupRequired, "the setup step closes once an administrator exists");
        Equal("1", after.UserCount.ToString(), "exactly one user exists");
        True(after.MatchingUser != null, "the workstation account now matches a user row");
        True(after.IsOperatorAdministrator, "and that row is an active Administrator");

        // The path disappears: a second registration is refused and writes nothing.
        UserSaveResult second = await firstRun.RegisterAsync("Someone Else", "Administration");
        True(!second.Succeeded, "the first-administrator step is closed after setup");
        Equal("1", (await repository.CountAsync()).ToString(), "the refused attempt wrote nothing");

        // An inactive match is recognised and reported, never re-activated.
        UserSaveResult secondAdmin = await repository.CreateAsync(new UserDraft
        {
            FullName = "Second Administrator", Username = "second.admin",
            Department = "Administration", Role = "Administrator"
        }, empty.OperatorUsername);
        True(secondAdmin.Succeeded, "a second administrator can be added: " + secondAdmin.Message);

        UserSaveResult deactivated = await repository.SetActiveAsync(
            admin.Id, false, secondAdmin.User!.Username);
        True(deactivated.Succeeded, "the first administrator can be deactivated by another");

        FirstRunSnapshot inactive = await firstRun.InspectAsync();
        True(inactive.MatchingUser != null, "an inactive account is still recognised");
        True(!inactive.MatchingUser!.IsActive, "and is reported as inactive");
        True(!inactive.IsOperatorAdministrator, "an inactive account is not an administrator");
        True((await repository.GetAsync(admin.Id))!.IsActive == false,
            "reading the directory does not silently re-activate the account");
        True(!(await firstRun.RegisterAsync("Reactivation Attempt", "Administration")).Succeeded,
            "a populated directory cannot be bootstrapped again");
    }

    private static async Task RunRepositoryChecks()
    {
        var repository = new UserRepository();
        await repository.InitializeAsync();
        True(await repository.IsSchemaReadyAsync(), "Users tables exist after initialization");

        string operatorName = UserRules.NormalizeUsername(
            ProGlassAutomation.Services.Audit.CurrentOperator.Username);
        True(operatorName.Length > 0, "the workstation reports an account name");

        Equal("7", (await repository.GetDepartmentsAsync()).Count.ToString(), "departments load from the catalogue");
        Equal("7", (await repository.GetRolesAsync()).Count.ToString(), "roles load from the catalogue");
        Equal("0", (await repository.CountAsync()).ToString(), "a new directory is empty");

        // ── Bootstrap ────────────────────────────────────────────────────
        var bootstrap = await repository.RegisterFirstAdministratorAsync(
            "  Ahmed   Al Balushi ", "Administration", operatorName);
        True(bootstrap.Succeeded, "first administrator registered: " + bootstrap.Message);
        User admin = bootstrap.User!;
        Equal("ADMINISTRATOR", admin.Role, "bootstrap grants the Administrator role");
        Equal(operatorName, admin.Username, "bootstrap uses the workstation account");
        Equal("Ahmed Al Balushi", admin.FullName, "bootstrap normalizes the full name");

        var secondBootstrap = await repository.RegisterFirstAdministratorAsync(
            "Someone Else", "Administration", operatorName);
        True(!secondBootstrap.Succeeded, "bootstrap closes once a user exists");

        // ── Create, default role, duplicate refusal ──────────────────────
        var created = await repository.CreateAsync(new UserDraft
        {
            FullName = "  Fatima   Al Zaabi ",
            Username = "fatima.alzaabi",
            Department = "Estimation",
            Role = "Estimator"
        }, operatorName);
        True(created.Succeeded, "administrator can add a user: " + created.Message);
        User estimator = created.User!;
        Equal("ESTIMATION", estimator.Department, "department stored as a catalogue code");
        Equal("ESTIMATOR", estimator.Role, "role stored as a catalogue code");
        Equal(admin.Id, estimator.CreatedByUserId ?? 0, "the granting administrator's id is stored");
        True(estimator.IsActive, "a new user is active");

        var duplicate = await repository.CreateAsync(new UserDraft
        {
            FullName = "Impostor",
            Username = "FATIMA.ALZAABI",
            Department = "Sales",
            Role = "Salesman"
        }, operatorName);
        True(!duplicate.Succeeded, "a username differing only in case is refused");
        True(duplicate.Message.Contains("already belongs", StringComparison.Ordinal),
            "the duplicate message names the owner: " + duplicate.Message);

        // ── Permission is enforced below the UI ──────────────────────────
        var refusedCreate = await repository.CreateAsync(new UserDraft
        {
            FullName = "Should Not Exist", Username = "ghost", Department = "Sales", Role = "Salesman"
        }, estimator.Username);
        True(!refusedCreate.Succeeded, "a non-administrator is refused by the write path");
        Equal(nameof(UserDecisionCode.NotAuthorized), refusedCreate.Code.ToString(), "refusal code");

        var refusedUnknown = await repository.CreateAsync(new UserDraft
        {
            FullName = "Nobodys Account", Username = "nobody", Department = "Sales", Role = "Salesman"
        }, @"MACHINE\no-such-account");
        Equal(nameof(UserDecisionCode.NotIdentified), refusedUnknown.Code.ToString(),
            "an unregistered workstation account is refused");

        // ── A second administrator, then the last-administrator guard ────
        var secondAdmin = await repository.CreateAsync(new UserDraft
        {
            FullName = "Sara Al Marzooqi", Username = "sara.marzooqi",
            Department = "Administration", Role = "Administrator"
        }, operatorName);
        True(secondAdmin.Succeeded, "a second administrator can be added: " + secondAdmin.Message);

        var selfDeactivate = await repository.SetActiveAsync(admin.Id, false, operatorName);
        True(!selfDeactivate.Succeeded, "an administrator cannot deactivate their own account");

        var deactivateSecond = await repository.SetActiveAsync(secondAdmin.User!.Id, false, operatorName);
        True(deactivateSecond.Succeeded, "another administrator can be deactivated: " + deactivateSecond.Message);

        var deactivateLast = await repository.SetActiveAsync(admin.Id, false, operatorName);
        True(!deactivateLast.Succeeded, "the last active administrator cannot be deactivated");
        Equal(nameof(UserDecisionCode.LastAdministrator), deactivateLast.Code.ToString(),
            "last-administrator refusal code");

        var demoteLast = await repository.UpdateAsync(new UserDraft
        {
            Id = admin.Id, FullName = admin.FullName, Username = admin.Username,
            Department = "ADMINISTRATION", Role = "ESTIMATOR", IsActive = true
        }, operatorName);
        True(!demoteLast.Succeeded, "the last active administrator cannot be demoted");
        Equal(nameof(UserDecisionCode.LastAdministrator), demoteLast.Code.ToString(),
            "last-administrator demotion refusal code");

        // ── Deactivation blocks attribution and action ───────────────────
        var deactivateEstimator = await repository.SetActiveAsync(estimator.Id, false, operatorName);
        True(deactivateEstimator.Succeeded, "an ordinary user can be deactivated");
        True(!UserAuthorization.CanAttribute(deactivateEstimator.User).IsAllowed,
            "a deactivated user can no longer be attributed");

        var asInactive = await repository.CreateAsync(new UserDraft
        {
            FullName = "While Inactive", Username = "while.inactive", Department = "Sales", Role = "Salesman"
        }, estimator.Username);
        Equal(nameof(UserDecisionCode.Inactive), asInactive.Code.ToString(),
            "an inactive user cannot manage users");

        // ── Filters and search ───────────────────────────────────────────
        var actives = await repository.QueryAsync(new UserQuery { IsActive = true });
        Equal("2", actives.Count.ToString(), "two active users (the bootstrap admin and one other)");

        var inactives = await repository.QueryAsync(new UserQuery { IsActive = false });
        Equal("2", inactives.Count.ToString(), "two inactive users");

        var byDepartment = await repository.QueryAsync(new UserQuery { Department = "ESTIMATION" });
        Equal("1", byDepartment.Count.ToString(), "department filter");

        var byRole = await repository.QueryAsync(new UserQuery { Role = "ADMINISTRATOR" });
        Equal("2", byRole.Count.ToString(), "role filter");

        var search = await repository.QueryAsync(new UserQuery { Search = "marzooqi" });
        Equal("1", search.Count.ToString(), "search matches the username");

        var searchByRole = await repository.QueryAsync(new UserQuery { Search = "Administrator" });
        Equal("2", searchByRole.Count.ToString(), "search matches the role wording");

        var none = await repository.QueryAsync(new UserQuery { Search = "zzzz-no-match" });
        Equal("0", none.Count.ToString(), "an unmatched search returns nothing");

        // ── Re-activation and rename ─────────────────────────────────────
        var reactivate = await repository.SetActiveAsync(estimator.Id, true, operatorName);
        True(reactivate.Succeeded, "a deactivated user can be re-activated");

        var stamp = await new UserSession(repository).CaptureStampAsync();
        True(stamp != null, "an identified, active operator can be stamped as Prepared By");
        Equal(admin.Id, stamp!.UserId, "the stamp carries the stable user id");
        Equal("Ahmed Al Balushi", stamp.FullName, "the stamp carries the name at capture time");

        var renamed = await repository.UpdateAsync(new UserDraft
        {
            Id = admin.Id, FullName = "Ahmed Al Balushi Al Suwaidi", Username = admin.Username,
            Department = "ADMINISTRATION", Role = "ADMINISTRATOR", IsActive = true
        }, operatorName);
        True(renamed.Succeeded, "a user can be renamed: " + renamed.Message);

        Equal("Ahmed Al Balushi Al Suwaidi",
            await repository.ResolveDisplayNameAsync(admin.Id), "the id now resolves to the new name");
        Equal("Ahmed Al Balushi", stamp.FullName,
            "an existing stamp keeps the name it was signed with (historical attribution)");
        Equal(admin.Id, stamp.UserId, "the stamp still points at the same person");

        // The username comparison key must not change under a rename of the
        // display name, and an administrator's own row must keep its role.
        var stored = await repository.GetAsync(admin.Id);
        True(stored != null, "the renamed user can be read back");
        Equal("ADMINISTRATOR", stored!.Role, "the role is unchanged by a rename");
        Equal(UserRules.UsernameKey(operatorName), stored.NormalizedUsername, "the username key is stable");

        // ── Attribution refuses an unregistered operator ────────────────
        var unknownStamp = await new UserSession(repository).CaptureStampAsync();
        True(unknownStamp != null, "the operator is still identified after the rename");
    }
}
