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
using ProGlassAutomation.Services.Clients;
using ProGlassAutomation.Services.Database;
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

        // Whole-draft validation reports each invalid field once.
        var bad = new ClientDraft { ClientName = "", Email = "nope", Trn = "abc" };
        IReadOnlyDictionary<string, string> errors = ClientRules.Validate(bad);
        True(errors.ContainsKey(nameof(ClientDraft.ClientName)), "draft reports missing name");
        True(errors.ContainsKey(nameof(ClientDraft.Email)), "draft reports bad e-mail");
        True(errors.ContainsKey(nameof(ClientDraft.Trn)), "draft reports bad TRN");

        var good = new ClientDraft { ClientName = "  Al  Noor  Glass ", ContactPerson = " Mr  Saleem " };
        True(ClientRules.Validate(good).Count == 0, "valid draft accepted");
        ClientDraft normalized = ClientRules.Normalize(good);
        Equal("Al Noor Glass", normalized.ClientName, "normalized name stored");
        Equal("Mr Saleem", normalized.ContactPerson, "normalized contact stored");
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
            Id = 7, ClientName = "Al Noor", ContactPerson = "Mr Saleem", Phone = "050",
            Email = "a@b.ae", IsActive = true,
            CreatedAt = new DateTime(2026, 10, 27, 6, 0, 0, DateTimeKind.Utc),
            UpdatedAt = new DateTime(2026, 10, 27, 6, 30, 0, DateTimeKind.Utc)
        };
        Equal("Success", active.StatusTag, "active badge tag");
        Equal("Active", active.StatusText, "active badge text");
        True(active.ContactSummary.Contains("Mr Saleem"), "contact summary includes person");
        True(active.ContactSummary.Contains("050"), "contact summary includes phone");

        var inactive = new ClientListRow { Id = 8, ClientName = "Old Co", IsActive = false };
        Equal("", inactive.StatusTag, "inactive badge is neutral");
        Equal("Inactive", inactive.StatusText, "inactive badge text");
        Equal("—", inactive.UpdatedText, "missing update stamp renders as a dash");

        // Empty contact fields must not leave dangling separators.
        var bare = new ClientListRow { ClientName = "Bare Co" };
        Equal("", bare.ContactSummary, "empty contact summary");
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
            ContactPerson = " Mr Saleem ",
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
        Equal("Al Noor Glass", created.Client.ClientName, "stored name normalized");
        Equal("100123456700003", created.Client.Trn, "stored TRN is digits only");
        True(created.Client.IsActive, "new client is active by default");
        True(created.Client.CreatedAt != default, "created timestamp stamped");
        True(created.Client.UpdatedAt != default, "updated timestamp stamped");

        // Read back
        Client? fetched = await repository.GetAsync(id);
        True(fetched != null, "client read back by id");
        Equal("Mussafah, Abu Dhabi", fetched!.Address, "address normalized on read");

        // Register projection
        IReadOnlyList<ClientListRow> rows = await repository.QueryAsync(new ClientQuery());
        Equal("1", rows.Count.ToString(), "register shows one client");
        Equal("Al Noor Glass", rows[0].ClientName, "register name");

        // Search (case-insensitive) and a miss
        Equal("1", (await repository.QueryAsync(new ClientQuery { Search = "al noor" })).Count.ToString(),
            "search matches case-insensitively");
        Equal("1", (await repository.QueryAsync(new ClientQuery { Search = "1001234" })).Count.ToString(),
            "search matches TRN digits");
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
            ContactPerson = "Mr Saleem",
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
    }
}
