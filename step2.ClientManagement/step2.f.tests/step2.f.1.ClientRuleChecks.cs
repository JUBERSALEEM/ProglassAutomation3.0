// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.f.tests/step2.f.1.ClientRuleChecks.cs
// MODULE: step2 — verification executable (dependency-free, like step1.d).
//
// What it proves without any UI:
//   • whitespace normalization and comparison keys;
//   • field validation (required name, e-mail shape, phone digits, TRN digits);
//   • duplicate warnings fire on name/phone/e-mail/TRN and never on a blank
//     draft field, never on the row being edited, and never block anything;
//   • register row projections (status tag/text, contact summary, dates);
//   • repository round-trip against an ISOLATED database folder: create,
//     read back, update, deactivate, re-activate, duplicate detection, and
//     proof that a deactivated client is hidden by default but retained.
//
// Exit code 0 = every check passed. Run only on Windows/.NET 8.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Clients;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Clients;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Users;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Step2Tests;

internal static class ClientRuleChecks
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    public static int Main()
    {
        Run("normalization", CheckNormalization);
        Run("validation", CheckValidation);
        Run("duplicates", CheckDuplicates);
        Run("row projection", CheckRowProjection);
        Run("migration 1012 preserves a legacy contact-person value", CheckAttentionNameMigration);
        Run("representative reference rules", CheckRepresentativeRules);
        Run("repository round-trip (isolated database)", CheckRepository);

        Console.WriteLine();
        Console.WriteLine($"step2 client checks: {_passed} passed, {Failures.Count} failed.");
        foreach (string failure in Failures) Console.WriteLine("  FAIL: " + failure);
        if (Failures.Count == 0)
            Console.WriteLine("PASS: client rules, duplicate warnings and isolated persistence.");
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

    // ── Checks ───────────────────────────────────────────────────────────

    private static void CheckNormalization()
    {
        Equal("Al Noor Glass", ClientRules.NormalizeText("  Al   Noor \t Glass  "), "whitespace collapse");
        Equal("", ClientRules.NormalizeText(null), "null text");
        Equal("", ClientRules.NormalizeText("   "), "blank text");

        // Comparison keys ignore casing and treat composed/decomposed accents
        // identically (Unicode Form KC), so a duplicate cannot hide behind
        // a different encoding of the same name.
        Equal(ClientRules.ComparisonKey("\u00e9cole"), ClientRules.ComparisonKey("e\u0301cole"),
            "unicode Forms C and D share one key");
        Equal("AL NOOR", ClientRules.ComparisonKey("al  noor"), "case-insensitive key");

        // TRN and phone keys are digits only — the display value is preserved.
        Equal("100123456700003", ClientRules.NormalizeTrn("100-123-4567 00003"), "trn digits");
        Equal("971501234567", ClientRules.PhoneKey("+971 50 123 4567"), "phone key digits");
        Equal("+971 50 123 4567", ClientRules.NormalizePhone("+971 50 123 4567"), "phone display kept");

        // A TRN that starts with zeros must keep them (identifier, not a number).
        Equal("000123", ClientRules.NormalizeTrn("000123"), "leading zeros preserved");
    }

    private static void CheckValidation()
    {
        True(ClientRules.ValidateClientName("") != null, "blank name is invalid");
        True(ClientRules.ValidateClientName("   \t ") != null, "whitespace-only name is invalid");
        True(ClientRules.ValidateClientName("Al Noor Glass") == null, "valid name accepted");
        True(ClientRules.ValidateClientName(new string('a', 201)) != null, "over-long name rejected");

        True(ClientRules.ValidateEmail("") == null, "blank e-mail allowed");
        True(ClientRules.ValidateEmail("ops@alnoor.ae") == null, "valid e-mail accepted");
        True(ClientRules.ValidateEmail("no-at-sign") != null, "e-mail without @ rejected");
        True(ClientRules.ValidateEmail("two@@alnoor.ae") != null, "double @ rejected");
        True(ClientRules.ValidateEmail("ops@alnoor") != null, "domain without dot rejected");
        True(ClientRules.ValidateEmail("op s@alnoor.ae") != null, "space rejected");

        True(ClientRules.ValidatePhone("") == null, "blank phone allowed");
        True(ClientRules.ValidatePhone("+971 50 123 4567") == null, "valid phone accepted");
        True(ClientRules.ValidatePhone("123") != null, "phone with too few digits rejected");

        True(ClientRules.ValidateTrn("") == null, "blank TRN allowed");
        True(ClientRules.ValidateTrn("100123456700003") == null, "digit TRN accepted");
        True(ClientRules.ValidateTrn("1001234567000AB") != null, "non-digit TRN rejected");
        // The module has always stripped the separators a certificate prints
        // (the TRN digits check in the normalization section, and the dashed TRN
        // saved by the repository round-trip), so the validator refusing them made
        // that stripping unreachable from any typed or pasted value (round 34.7).
        True(ClientRules.ValidateTrn("100-123-456-700-003") == null, "dashed TRN accepted");
        True(ClientRules.ValidateTrn("100 123 456 700 003") == null, "spaced TRN accepted");
        True(ClientRules.ValidateTrn("100-123-456-700-0O3") != null,
            "a letter inside separators is still rejected");
        Equal("100123456700003", ClientRules.NormalizeTrn("100-123-456-700-003"),
            "a dashed TRN is stored as digits");
        Equal(ClientRules.TrnKey("100-123-456-700-003"), ClientRules.TrnKey("100123456700003"),
            "both spellings are one registration for duplicate detection");

        // Whole-draft validation reports each invalid field once.
        var bad = new ClientDraft { ClientName = "", Email = "nope", Trn = "abc" };
        IReadOnlyDictionary<string, string> errors = ClientRules.Validate(bad);
        True(errors.ContainsKey(nameof(ClientDraft.ClientName)), "draft reports missing name");
        True(errors.ContainsKey(nameof(ClientDraft.Email)), "draft reports bad e-mail");
        True(errors.ContainsKey(nameof(ClientDraft.Trn)), "draft reports bad TRN");

        True(ClientRules.ValidateAttentionName("") == null, "blank attention name allowed");
        True(ClientRules.ValidateAttentionName("Mr Ahmed Al Mansouri") == null, "attention name accepted");
        True(ClientRules.ValidateAttentionName(new string('a', 201)) != null, "over-long attention name rejected");

        var good = new ClientDraft { ClientName = "  Al  Noor  Glass ", AttentionName = " Mr  Saleem " };
        True(ClientRules.Validate(good).Count == 0, "valid draft accepted");
        ClientDraft normalized = ClientRules.Normalize(good);
        Equal("Al Noor Glass", normalized.ClientName, "normalized company name stored");
        Equal("Mr Saleem", normalized.AttentionName, "normalized attention name stored");
        // The company and the person are separate values, never derived.
        True(normalized.ClientName != normalized.AttentionName, "attention name is not the company name");
    }

    private static void CheckRepresentativeRules()
    {
        // Null is a real state: every client that existed before migration 1014,
        // and every client that genuinely has no salesman yet.
        True(ClientRules.Validate(new ClientDraft { ClientName = "A", RepresentativeUserId = null }).Count == 0,
            "no representative is valid");
        True(ClientRules.Validate(new ClientDraft { ClientName = "A", RepresentativeUserId = 7 }).Count == 0,
            "a positive user id is accepted by the rules");

        var bad = new ClientDraft { ClientName = "A", RepresentativeUserId = 0 };
        True(ClientRules.Validate(bad).ContainsKey(nameof(ClientDraft.RepresentativeUserId)),
            "zero is refused: it names no user");

        var negative = new ClientDraft { ClientName = "A", RepresentativeUserId = -3 };
        True(ClientRules.Validate(negative).ContainsKey(nameof(ClientDraft.RepresentativeUserId)),
            "a negative id is refused");

        // Normalization keeps the reference but never invents one.
        True(ClientRules.Normalize(new ClientDraft { ClientName = "A", RepresentativeUserId = 0 })
            .RepresentativeUserId is null, "zero normalizes to none");
        Equal("7", (ClientRules.Normalize(new ClientDraft { ClientName = "A", RepresentativeUserId = 7 })
            .RepresentativeUserId ?? 0).ToString(), "a real reference survives normalization");

        // The projection never prints a raw id.
        Equal("—", new ClientListRow { RepresentativeUserId = null }.RepresentativeText,
            "no representative reads as a dash");
        Equal("Sara", new ClientListRow { RepresentativeUserId = 4, RepresentativeName = "Sara" }.RepresentativeText,
            "the resolved name is what the register shows");
        Equal("(missing user)", new ClientListRow { RepresentativeUserId = 4 }.RepresentativeText,
            "a reference without a readable name is never mistaken for an empty cell");
    }

    private static void CheckDuplicates()
    {
        var existing = new List<ClientDuplicateCandidate>
        {
            new() { Id = 1, ClientName = "Al Noor Glass LLC", Phone = "+971 50 111 2222",
                    Email = "ops@alnoor.ae", Trn = "100123456700003", IsActive = true },
            new() { Id = 2, ClientName = "Villa Town Contracting", Phone = "+971 4 333 4444",
                    Email = "info@villatown.ae", Trn = "200999888700001", IsActive = false }
        };

        // Same name in different casing/spacing is a warning.
        var sameName = new ClientDraft { ClientName = "  al  noor   glass llc " };
        IReadOnlyList<ClientDuplicateMatch> nameMatches =
            ClientDuplicatePolicy.Find(existing, sameName);
        True(nameMatches.Count == 1, "name match found");
        Equal("1", nameMatches[0].ClientId.ToString(), "name match points at client 1");
        True(nameMatches[0].Fields.Contains("name"), "match records the name field");

        // Shared phone digits (formatting differs) is a warning.
        var samePhone = new ClientDraft { ClientName = "Different Trading", Phone = "(+971) 50-111-2222" };
        IReadOnlyList<ClientDuplicateMatch> phoneMatches =
            ClientDuplicatePolicy.Find(existing, samePhone);
        True(phoneMatches.Any(m => m.ClientId == 1), "phone match found despite formatting");

        // Shared TRN and e-mail are warnings; the inactive client is still reported.
        var sameTrn = new ClientDraft { ClientName = "New Name", Trn = "200-999-8887 00001" };
        IReadOnlyList<ClientDuplicateMatch> trnMatches =
            ClientDuplicatePolicy.Find(existing, sameTrn);
        True(trnMatches.Any(m => m.ClientId == 2), "inactive client still reported");
        True(trnMatches.First(m => m.ClientId == 2).IsActive == false, "match keeps active flag");

        var sameEmail = new ClientDraft { ClientName = "Another", Email = "OPS@ALNOOR.AE" };
        True(ClientDuplicatePolicy.Find(existing, sameEmail).Count == 1, "e-mail match ignores casing");

        // A blank draft field never matches anything.
        var blank = new ClientDraft { ClientName = "Totally New Client" };
        True(ClientDuplicatePolicy.Find(existing, blank).Count == 0, "no false positive");

        // The row being edited never matches itself.
        True(ClientDuplicatePolicy.Find(existing, sameName, 1).Count == 0, "self excluded");

        // Warning text is informative and explicitly non-destructive.
        string warning = ClientDuplicatePolicy.DescribeWarning(nameMatches);
        True(warning.Contains("Al Noor Glass LLC"), "warning names the existing client");
        True(warning.Contains("warning only"), "warning says it is advisory");
        True(warning.Contains("inactive") == false, "active match not called inactive");
        Equal("", ClientDuplicatePolicy.DescribeWarning(Array.Empty<ClientDuplicateMatch>()),
            "no warning when nothing matches");
    }

    private static void CheckRowProjection()
    {
        var active = new ClientListRow
        {
            Id = 7, ClientName = "Al Noor Glass LLC", AttentionName = "Mr Saleem", Phone = "050",
            Email = "a@b.ae", IsActive = true,
            CreatedAt = new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 10, 27, 6, 30, 0, DateTimeKind.Utc)
        };
        Equal("Success", active.StatusTag, "active badge tag");
        Equal("Active", active.StatusText, "active badge text");
        True(active.AttentionSummary.Contains("Mr Saleem"), "summary includes the attention name");
        True(active.AttentionSummary.Contains("050"), "summary includes phone");

        var inactive = new ClientListRow { Id = 8, ClientName = "Old Co", IsActive = false };
        Equal("", inactive.StatusTag, "inactive badge is neutral");
        Equal("Inactive", inactive.StatusText, "inactive badge text");
        Equal("—", inactive.UpdatedText, "missing update stamp renders as a dash");

        // Empty contact fields must not leave dangling separators.
        var bare = new ClientListRow { ClientName = "Bare Co" };
        Equal("", bare.AttentionSummary, "empty attention summary");
    }

    /// <summary>
    /// The risky path: a database that already ran 1011 (so it has the
    /// superseded ContactPerson column) must converge to AttentionName with its
    /// data intact when 1012 runs. Built here by hand, so the real migration
    /// code is exercised against the exact shape the owner's file has.
    /// </summary>
    private static void CheckAttentionNameMigration()
    {
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step2-migration-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempFolder);
        string dbPath = Path.Combine(tempFolder, "inquiries.db");

        try
        {
            using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(
                       SqliteResilience.BuildConnectionString(dbPath)))
            {
                connection.Open();
                SqliteResilience.ApplyConnectionPolicy(connection);

                // Simulate the post-1011 shape, with a real stored value.
                SchemaMigrationSql.Execute(connection, @"
                    CREATE TABLE ""Clients"" (
                        ""Id"" INTEGER NOT NULL CONSTRAINT ""PK_Clients"" PRIMARY KEY AUTOINCREMENT,
                        ""ClientName"" TEXT NOT NULL DEFAULT '',
                        ""ContactPerson"" TEXT NOT NULL DEFAULT '',
                        ""Phone"" TEXT NOT NULL DEFAULT '',
                        ""Email"" TEXT NOT NULL DEFAULT '',
                        ""Trn"" TEXT NOT NULL DEFAULT '',
                        ""Address"" TEXT NOT NULL DEFAULT '',
                        ""Notes"" TEXT NOT NULL DEFAULT '',
                        ""IsActive"" INTEGER NOT NULL DEFAULT 1,
                        ""CreatedAt"" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                        ""UpdatedAt"" TEXT NOT NULL DEFAULT '0001-01-01 00:00:00');");
                SchemaMigrationSql.Execute(connection,
                    @"INSERT INTO ""Clients"" (""ClientName"", ""ContactPerson"", ""IsActive"")
                      VALUES ('Al Noor Glass LLC', 'Mr Ahmed Al Mansouri', 1);");
                // Everything up to and including 1011 has already been applied.
                SchemaMigrationSql.Execute(connection, "PRAGMA user_version = 1011;");
            }

            AppDataRoot.TestFolderOverride = tempFolder;
            bool upgraded = SchemaMigrationRunner.Upgrade(
                dbPath, AppSchemaMigrations.Inquiries, "Client attention-name check");
            True(upgraded, "migration run reported success: " + SchemaMigrationRunner.FailureSummary);

            using var reader = new Microsoft.Data.Sqlite.SqliteConnection(
                SqliteResilience.BuildConnectionString(dbPath));
            reader.Open();
            SqliteResilience.ApplyConnectionPolicy(reader);

            var columns = new List<string>();
            using (var pragma = reader.CreateCommand())
            {
                pragma.CommandText = "PRAGMA table_info(\"Clients\");";
                using var cursor = pragma.ExecuteReader();
                while (cursor.Read()) columns.Add(cursor.GetString(1));
            }
            True(columns.Contains("AttentionName"), "AttentionName column exists after upgrade");
            True(!columns.Contains("ContactPerson"), "legacy ContactPerson column is gone");

            using (var value = reader.CreateCommand())
            {
                value.CommandText = "SELECT \"AttentionName\" FROM \"Clients\" LIMIT 1;";
                Equal("Mr Ahmed Al Mansouri", Convert.ToString(value.ExecuteScalar()) ?? "",
                    "stored person value was preserved through the rename");
            }

            // The chain's TIP, not the version this migration happened to be when
            // the check was written. 1016 (Company Profile) now follows 1012, so the
            // old hard-coded "1012" asserted the calendar rather than the behaviour
            // and failed the moment another step extended the chain (round 34.7).
            // Derived from the registry, the check stays honest as the chain grows.
            int chainTip = AppSchemaMigrations.Inquiries.Max(migration => migration.Version);
            True(chainTip >= ClientSchema.AttentionNameMigrationVersion,
                "the chain still contains the attention-name migration");
            using (var version = reader.CreateCommand())
            {
                version.CommandText = "PRAGMA user_version;";
                Equal(chainTip.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    Convert.ToString(version.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture) ?? "",
                    "user_version advanced to the chain tip");
            }

            // Re-running must be a no-op, not an error.
            True(SchemaMigrationRunner.Upgrade(dbPath, AppSchemaMigrations.Inquiries,
                    "Client attention-name check (re-run)"),
                "a second upgrade is a safe no-op: " + SchemaMigrationRunner.FailureSummary);
        }
        finally
        {
            AppDataRoot.TestFolderOverride = null;
            try { Directory.Delete(tempFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    private static void CheckRepository()
    {
        // Isolated folder: the production %APPDATA% directory is never opened.
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step2-checks-" + Guid.NewGuid().ToString("N")[..8]);
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

    private static async Task RunRepositoryChecks()
    {
        var repository = new ClientRepository();
        await repository.InitializeAsync();
        True(await repository.IsSchemaReadyAsync(), "Clients table exists after initialization");

        var empty = await repository.QueryAsync(new ClientQuery());
        Equal("0", empty.Count.ToString(), "new directory is empty");

        // Create
        ClientSaveResult created = await repository.CreateAsync(new ClientDraft
        {
            ClientName = "  Al   Noor Glass  ",
            AttentionName = " Mr  Saleem  Al Mansouri ",
            Phone = "+971 50 123 4567",
            Email = "ops@alnoor.ae",
            Trn = "100-123-456-700-003",
            Address = " Mussafah,  Abu Dhabi ",
            Notes = "Key account"
        });
        True(created.Succeeded, "create succeeded: " + created.Message);
        True(created.Client != null, "create returned the client");
        int id = created.Client!.Id;
        True(id > 0, "created client has a stable id");
        Equal("Al Noor Glass", created.Client.ClientName, "stored company name normalized");
        Equal("Mr Saleem Al Mansouri", created.Client.AttentionName, "stored attention name normalized");
        Equal("100123456700003", created.Client.Trn, "stored TRN is digits only");
        True(created.Client.IsActive, "new client is active by default");
        True(created.Client.CreatedAt != default, "created timestamp stamped");
        True(created.Client.UpdatedAt != default, "updated timestamp stamped");

        // Read back
        Client? fetched = await repository.GetAsync(id);
        True(fetched != null, "client read back by id");
        Equal("Mussafah, Abu Dhabi", fetched!.Address, "address normalized on read");
        Equal("Mr Saleem Al Mansouri", fetched.AttentionName, "attention name survives the round-trip");

        // Register projection
        IReadOnlyList<ClientListRow> rows = await repository.QueryAsync(new ClientQuery());
        Equal("1", rows.Count.ToString(), "register shows one client");
        Equal("Al Noor Glass", rows[0].ClientName, "register name");

        // Search (case-insensitive) and a miss
        Equal("1", (await repository.QueryAsync(new ClientQuery { Search = "al noor" })).Count.ToString(),
            "search matches case-insensitively");
        Equal("1", (await repository.QueryAsync(new ClientQuery { Search = "1001234" })).Count.ToString(),
            "search matches TRN digits");
        Equal("1", (await repository.QueryAsync(new ClientQuery { Search = "mansouri" })).Count.ToString(),
            "search matches the attention name");
        Equal("0", (await repository.QueryAsync(new ClientQuery { Search = "zzz-not-here" })).Count.ToString(),
            "search misses cleanly");

        // Duplicate warning against a stored row, excluding itself
        IReadOnlyList<ClientDuplicateMatch> duplicates = await repository.FindDuplicatesAsync(
            new ClientDraft { ClientName = "al noor glass", Trn = "100123456700003" }, id);
        Equal("0", duplicates.Count.ToString(), "edited row does not duplicate itself");
        duplicates = await repository.FindDuplicatesAsync(
            new ClientDraft { ClientName = "al noor glass" });
        Equal("1", duplicates.Count.ToString(), "new draft with same name warns");

        // Validation is enforced by the repository too, not only the UI.
        ClientSaveResult invalid = await repository.CreateAsync(new ClientDraft { ClientName = "   " });
        True(!invalid.Succeeded, "empty name refused by the repository");
        True(invalid.Message.Length > 0, "refusal carries a message");

        // Update
        ClientSaveResult updated = await repository.UpdateAsync(id, new ClientDraft
        {
            ClientName = "Al Noor Glass LLC",
            AttentionName = "Mr Saleem Al Mansouri",
            Phone = "+971 50 123 4567",
            Email = "ops@alnoor.ae",
            Trn = "100123456700003",
            Address = "Mussafah, Abu Dhabi",
            Notes = "Key account",
            IsActive = true
        });
        True(updated.Succeeded, "update succeeded: " + updated.Message);
        Equal("Al Noor Glass LLC", (await repository.GetAsync(id))!.ClientName, "update persisted");
        True(updated.Client!.UpdatedAt >= updated.Client.CreatedAt, "updated stamp advanced");

        // Update of a missing row fails softly
        ClientSaveResult missing = await repository.UpdateAsync(999999, new ClientDraft { ClientName = "Ghost" });
        True(!missing.Succeeded, "updating a missing client fails");
        True(missing.Message.Contains("no longer exists"), "missing-row message is actionable");

        // Deactivate: retained, hidden by default, visible when asked
        ClientSaveResult deactivated = await repository.SetActiveAsync(id, false);
        True(deactivated.Succeeded, "deactivate succeeded");
        Equal("0", (await repository.QueryAsync(new ClientQuery())).Count.ToString(),
            "inactive client hidden by default");
        Equal("1", (await repository.QueryAsync(new ClientQuery { IncludeInactive = true })).Count.ToString(),
            "inactive client retained and shown on request");
        True((await repository.GetAsync(id)) != null, "deactivated client is never deleted");

        // Re-activate
        True((await repository.SetActiveAsync(id, true)).Succeeded, "re-activate succeeded");
        Equal("1", (await repository.QueryAsync(new ClientQuery())).Count.ToString(),
            "re-activated client is selectable again");

        // A second client with a similar name may still be saved (warn only).
        ClientSaveResult second = await repository.CreateAsync(new ClientDraft
        {
            ClientName = "Al Noor Glass LLC", Trn = "300999888700002"
        });
        True(second.Succeeded, "similar name is allowed (warning only, no block)");
        Equal("2", (await repository.QueryAsync(new ClientQuery())).Count.ToString(),
            "both clients retained as separate records");

        // ── Representative reference (migration 1014) ────────────────────
        // The picker's list comes from the user directory: active salesmen only.
        var users = new UserRepository();
        // Round 24: every user has a password, so the fixture administrator gets
        // one too — the row is otherwise unusable and cannot be signed in as.
        const string adminPassword = "Fixture-Admin!2026";
        UserSaveResult admin = await users.RegisterFirstAdministratorAsync(
            "Directory Admin", UserCatalog.AdministrationDepartment, adminPassword, @"MALIK\ADMIN");
        True(admin.Succeeded, "a first administrator exists for the reference checks: " + admin.Message);

        UserSaveResult salesman = await users.CreateAsync(new UserDraft
        {
            FullName = "Sara Sales",
            Username = @"MALIK\SARA",
            Department = UserCatalog.DefaultDepartment,
            Role = UserCatalog.SalesmanRole,
            Password = "Fixture-Sales!2026", PasswordConfirm = "Fixture-Sales!2026"
        }, @"MALIK\ADMIN");
        True(salesman.Succeeded, "a salesman was created: " + salesman.Message);

        UserSaveResult estimator = await users.CreateAsync(new UserDraft
        {
            FullName = "Eddie Estimator",
            Username = @"MALIK\EDDIE",
            Department = UserCatalog.DefaultDepartment,
            Role = UserCatalog.DefaultRole,
            Password = "Fixture-Estimate!2026", PasswordConfirm = "Fixture-Estimate!2026"
        }, @"MALIK\ADMIN");
        True(estimator.Succeeded, "a non-salesman was created: " + estimator.Message);

        // Every active user can be assigned — a company that has no salesman yet
        // must still be able to record who owns a client. Salesmen come first.
        IReadOnlyList<ClientRepresentative> representatives = await repository.GetRepresentativesAsync();
        Equal("3", representatives.Count.ToString(), "every active user is offered as a representative");
        Equal("Sara Sales", representatives[0].DisplayName, "the salesman is offered first");
        True(representatives.Any(row => row.DisplayName == "Eddie Estimator"),
            "a non-salesman is still selectable");

        int salesId = salesman.User!.Id;

        // Assign the reference and read it back through the register projection.
        ClientSaveResult assigned = await repository.UpdateAsync(id, new ClientDraft
        {
            ClientName = "Al Noor Glass LLC",
            RepresentativeUserId = salesId
        });
        True(assigned.Succeeded, "a client can be assigned a representative: " + assigned.Message);
        Equal(salesId.ToString(), (assigned.Client!.RepresentativeUserId ?? 0).ToString(),
            "the stored value is the user id, not a name");

        IReadOnlyList<ClientListRow> withRep = await repository.QueryAsync(new ClientQuery());
        ClientListRow alNoor = withRep.First(row => row.Id == id);
        Equal("Sara Sales", alNoor.RepresentativeText, "the register resolves the name");

        // A rename relabels the register without rewriting the client row.
        // Round 24: "the current user" is the SIGNED-IN user, and this fixture
        // never signs in — so there is none, which is the honest answer here.
        User? sara = await users.FindCurrentUserAsync();
        True(sara == null, "a fixture with nobody signed in has no current user");
        UserSaveResult renamed = await users.UpdateAsync(new UserDraft
        {
            Id = salesId,
            FullName = "Sara Al Saleh",
            Username = @"MALIK\SARA",
            Department = UserCatalog.DefaultDepartment,
            Role = UserCatalog.SalesmanRole,
            OriginalRole = UserCatalog.SalesmanRole,
            OriginalIsActive = true
        }, @"MALIK\ADMIN");
        True(renamed.Succeeded, "the representative was renamed: " + renamed.Message);

        ClientListRow afterRename = (await repository.QueryAsync(new ClientQuery()))
            .First(row => row.Id == id);
        Equal("Sara Al Saleh", afterRename.RepresentativeText, "the register shows the new name");
        Equal(salesId.ToString(), (afterRename.RepresentativeUserId ?? 0).ToString(),
            "the client row still stores the same id");

        // A stale id is refused rather than written as an orphan.
        ClientSaveResult stale = await repository.UpdateAsync(id, new ClientDraft
        {
            ClientName = "Al Noor Glass LLC",
            RepresentativeUserId = 999999
        });
        True(!stale.Succeeded, "an unknown representative is refused");
        True(stale.Message.Contains("no longer exists"), "the refusal says what to do");

        // Clearing is valid and returns the row to the pre-1014 state.
        ClientSaveResult cleared = await repository.UpdateAsync(id, new ClientDraft
        {
            ClientName = "Al Noor Glass LLC"
        });
        True(cleared.Succeeded, "the reference can be cleared");
        True(cleared.Client!.RepresentativeUserId is null, "clearing stores null, not zero");
        Equal("—", (await repository.QueryAsync(new ClientQuery())).First(row => row.Id == id).RepresentativeText,
            "an unassigned client reads as a dash");

        // A deactivated salesman disappears from the picker but keeps the client's
        // stored reference: history is not rewritten by a status change.
        True((await repository.UpdateAsync(id, new ClientDraft
        {
            ClientName = "Al Noor Glass LLC",
            RepresentativeUserId = salesId
        })).Succeeded, "reference restored for the deactivation case");
        True((await users.SetActiveAsync(salesId, false, @"MALIK\ADMIN")).Succeeded,
            "the salesman was deactivated");
        IReadOnlyList<ClientRepresentative> stillOffered = await repository.GetRepresentativesAsync();
        Equal("2", stillOffered.Count.ToString(), "an inactive user drops out of the picker");
        True(!stillOffered.Any(row => row.Id == salesId),
            "the deactivated salesman is no longer offered");
        Equal(salesId.ToString(),
            ((await repository.QueryAsync(new ClientQuery())).First(row => row.Id == id)
                .RepresentativeUserId ?? 0).ToString(),
            "the existing client keeps its reference");
    }
}
