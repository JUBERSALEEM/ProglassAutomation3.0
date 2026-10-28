// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.f.tests/step4.f.1.CompanyProfileChecks.cs
// MODULE: step4 — verification executable (dependency-free, like step1.d).
//
// What it proves, without a display:
//   • the rules (step4.a.2): a blank name is refused, a UAE TRN is 15 digits
//     (separators tolerated, letters refused), every optional field may be
//     empty, a placement without a logo is refused while a stored logo with the
//     "no logo" placement is allowed, and the size is clamped;
//   • the logo store (step4.a.3): only PNG/JPG/JPEG, only a bare file name, an
//     imported copy lives in the app data folder under a generated name, a
//     missing file resolves to null instead of throwing, and a delete is a
//     no-op when the asset is already gone;
//   • migration 1016 on an ISOLATED database (step4.b.1): the table is created,
//     the chain advanced, a second run is a no-op, and the database itself
//     refuses a second profile (CHECK on the fixed key) — the single-profile
//     invariant is a constraint, not a hidden button;
//   • the repository round-trip (step4.b.4): an anonymous write is refused, a
//     non-administrator is refused by the write path itself, an administrator
//     saves, reloading returns the same values from the same row, updating
//     stores into that row rather than adding one, every optional field may be
//     saved empty, and a refused save leaves the stored profile and its logo
//     untouched;
//   • the logo lifecycle through the repository: import on save, replace (the
//     old asset is deleted only after the commit), clear (the asset is removed
//     and the name emptied);
//   • the editor's states (step4.c.1): unsaved changes are reported, a refused
//     save claims nothing, a successful save clears the dirty flag, a missing
//     logo file shows as a problem rather than an exception, and a
//     non-administrator cannot save.
//
// Exit code 0 = every check passed. Windows/.NET 8 only: WPF imaging is
// referenced by the view model, so this executable is a Windows build.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Company;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Company;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.ViewModels.Company;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

namespace ProGlassAutomation.Step4Tests;

internal static class CompanyProfileChecks
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    /// <summary>
    /// A real 1×1 PNG, written by the check itself. The preview path decodes
    /// this, so the bytes have to be a genuine image: a file renamed to .png
    /// would prove the store's extension filter and nothing about the preview.
    /// </summary>
    private const string OnePixelPngBase64 =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAIAAACQd1PeAAAADElEQVR42mNQSFgAAAHEASFcollRAAAAAElFTkSuQmCC";

    public static int Main()
    {
        Run("rules and validation", CheckRules);
        Run("logo store (isolated folder)", CheckLogoStore);
        Run("migration 1016 and the single-profile constraint (isolated database)",
            CheckMigrationAndConstraint);
        Run("repository round-trip (isolated database)", CheckRepositoryRoundTrip);
        Run("logo lifecycle through the repository (isolated database)", CheckLogoLifecycle);
        Run("editor states (isolated database)", CheckViewModelStates);

        Console.WriteLine();
        Console.WriteLine($"step4 company checks: {_passed} passed, {Failures.Count} failed.");
        foreach (string failure in Failures) Console.WriteLine("  FAIL: " + failure);
        if (Failures.Count == 0)
            Console.WriteLine("PASS: company profile rules, single-profile invariant and isolated persistence.");
        return Failures.Count == 0 ? 0 : 1;
    }

    // ── Rules ────────────────────────────────────────────────────────────

    private static void CheckRules()
    {
        // A blank name is the one thing a company profile cannot be without.
        True(CompanyProfileRules.ValidateCompanyName("") != null, "blank company name is refused");
        True(CompanyProfileRules.ValidateCompanyName("   ") != null, "whitespace company name is refused");
        True(CompanyProfileRules.ValidateCompanyName("Pro Glass LLC") == null, "a real name is accepted");

        // UAE TRN: 15 digits. Separators are tolerated because people type them;
        // letters and the wrong length are not.
        Equal("100000000000003",
            CompanyProfileRules.NormalizeTrn("100 000 000 000 003"), "TRN normalises to digits");
        True(CompanyProfileRules.ValidateTrn("100000000000003") == null, "a 15-digit TRN is accepted");
        True(CompanyProfileRules.ValidateTrn("10000000000000") != null, "a 14-digit TRN is refused");
        True(CompanyProfileRules.ValidateTrn("1000000000000031") != null, "a 16-digit TRN is refused");
        True(CompanyProfileRules.ValidateTrn("10000000000000A") != null, "a TRN with letters is refused");
        True(CompanyProfileRules.ValidateTrn("") == null, "an empty TRN is allowed: it is optional");

        // Optional fields may all be empty; that is a valid profile.
        var minimal = new CompanyProfileDraft { CompanyName = "Pro Glass LLC" };
        Equal("0", CompanyProfileRules.Validate(minimal).Count.ToString(),
            "only the company name is required");

        // Shape checks that must not invent rules: an e-mail without a domain and
        // a malformed website are refused, while a bare host is not.
        True(CompanyProfileRules.ValidateEmail("not-an-email") != null, "a malformed e-mail is refused");
        True(CompanyProfileRules.ValidateEmail("office@proglass.ae") == null, "a real e-mail is accepted");
        // docs/12: website is "optional; shape checked, NO INVENTED REQUIREMENT".
        // A dot was an invented requirement: a single-label host ("proglass") is a
        // legitimate intranet address, and the field is a caption on the letterhead,
        // not a link. What the rule does guarantee is that the value cannot be
        // nonsense the page would print.
        True(CompanyProfileRules.ValidateWebsite("pro glass") != null, "a website with a space is refused");
        True(CompanyProfileRules.ValidateWebsite("proglass@example.com") != null,
            "an e-mail typed into the website field is refused");
        True(CompanyProfileRules.ValidateWebsite("proglass") == null,
            "a single-label host is accepted (no invented requirement)");
        True(CompanyProfileRules.ValidateWebsite("https://proglass.ae") == null, "a URL is accepted");
        True(CompanyProfileRules.ValidateAccountNumber("AE07 0331 2345 6789 0123 456") == null,
            "an IBAN with spaces is accepted");
        True(CompanyProfileRules.ValidateAccountNumber("AE07-0331") != null,
            "an account number with punctuation is refused");

        // A placement without a logo would print an empty panel.
        var placementOnly = new CompanyProfileDraft
        {
            CompanyName = "Pro Glass LLC",
            LogoLayout = CompanyLogoLayout.LeftPortrait
        };
        True(CompanyProfileRules.Validate(placementOnly).ContainsKey("LogoLayout"),
            "a logo placement without a logo is refused");

        // A stored logo with the "no logo" placement is a real choice, not an error.
        var keptButHidden = new CompanyProfileDraft
        {
            CompanyName = "Pro Glass LLC",
            LogoFileName = "logo-0123456789abcdef0123456789abcdef.png",
            LogoLayout = CompanyLogoLayout.None
        };
        Equal("0", CompanyProfileRules.Validate(keptButHidden).Count.ToString(),
            "a stored logo with the text-only placement is allowed");

        // Sizes are clamped, not rejected silently: a slider cannot produce 3 mm
        // or 400 mm, but a hand-written draft can.
        Equal("8", CompanyProfileRules.ClampLogoSize(3).ToString("0", CultureInfo.InvariantCulture),
            "a small size clamps up to the minimum");
        Equal("80", CompanyProfileRules.ClampLogoSize(400).ToString("0", CultureInfo.InvariantCulture),
            "a large size clamps down to the maximum");
        Equal("24", CompanyProfileRules.ClampLogoSize(24).ToString("0", CultureInfo.InvariantCulture),
            "a size inside the range is kept");

        // Normalisation: text is trimmed and inner runs collapse, address lines
        // survive, and the TRN loses its separators.
        Equal("Pro Glass LLC", CompanyProfileRules.NormalizeText("  Pro   Glass  LLC "), "text normalises");
        // The promise (docs/12) is "lines preserved, blank lines dropped" — the
        // LINES, not a separator. NormalizeAddress joins with Environment.NewLine,
        // so comparing the raw string asserted this machine's newline and failed
        // on Windows for a run that was actually correct.
        Equal("Shop 4|Main Road|Ajman",
            JoinLines(CompanyProfileRules.NormalizeAddress("  Shop 4 \n\n Main Road\nAjman  ")),
            "address keeps its lines and drops blank ones");

        // The summary sentence names the fields, so a refused save explains itself.
        var blank = new CompanyProfileDraft();
        string described = CompanyProfileRules.Describe(CompanyProfileRules.Validate(blank));
        True(described.Length > 0, "a refused draft produces a sentence");
        True(described.Contains("Company name", StringComparison.OrdinalIgnoreCase),
            "the sentence names the missing company name");
    }

    // ── Logo store ───────────────────────────────────────────────────────

    private static void CheckLogoStore()
    {
        WithTempFolder("logo", () =>
        {
            string folder = CompanyLogoStore.Folder;
            Directory.CreateDirectory(folder);

            string png = WritePng(Path.Combine(folder, "source.png"));
            string jpg = Path.Combine(folder, "source.jpg");
            File.Copy(png, jpg);
            string text = Path.Combine(folder, "notes.txt");
            File.WriteAllText(text, "not an image");

            // Extensions.
            True(CompanyLogoStore.HasAllowedExtension("a.png"), ".png is allowed");
            True(CompanyLogoStore.HasAllowedExtension("a.JPG"), ".JPG is allowed, case-insensitively");
            True(CompanyLogoStore.HasAllowedExtension("a.jpeg"), ".jpeg is allowed");
            True(!CompanyLogoStore.HasAllowedExtension("a.gif"), ".gif is refused");
            True(!CompanyLogoStore.HasAllowedExtension("a.svg"), ".svg is refused");
            True(CompanyLogoStore.ValidateSource(text) != null, "a non-image file is refused before it is copied");

            // Bare file names only: a stored value can never escape the folder.
            True(!CompanyLogoStore.IsSafeFileName(@"..\Windows\notepad.exe"), "a parent-relative name is refused");
            True(!CompanyLogoStore.IsSafeFileName(@"C:\Temp\logo.png"), "an absolute path is refused");
            True(!CompanyLogoStore.IsSafeFileName("sub/logo.png"), "a name with a separator is refused");
            True(CompanyLogoStore.IsSafeFileName("logo-0123456789abcdef.png"), "a generated name is accepted");
            True(CompanyLogoStore.ResolvePath(@"C:\Temp\logo.png") == null, "resolving an absolute path gives null");
            True(CompanyLogoStore.ResolvePath("logo-does-not-exist.png") == null,
                "resolving a missing asset gives null instead of throwing");

            // Import copies under a generated name, and the copy is inside the app
            // data folder — never a reference to where the operator picked it.
            CompanyLogoImportResult imported = CompanyLogoStore.Import(png);
            True(imported.Succeeded, "the PNG imports: " + imported.Message);
            True(imported.FileName.StartsWith("logo-", StringComparison.Ordinal),
                "the stored name is generated, not the source name");
            True(imported.FileName.EndsWith(".png", StringComparison.Ordinal), "the extension is kept");
            True(File.Exists(Path.Combine(folder, imported.FileName)), "the imported copy exists in the app data folder");
            True(CompanyLogoStore.ResolvePath(imported.FileName) != null, "the imported asset resolves");

            // A second import of the same source never overwrites the first.
            CompanyLogoImportResult again = CompanyLogoStore.Import(png);
            True(again.Succeeded && again.FileName != imported.FileName,
                "two imports of one file are two assets");

            // Deleting is best effort and idempotent.
            CompanyLogoStore.Delete(again.FileName);
            True(!File.Exists(Path.Combine(folder, again.FileName)), "a deleted asset is gone");
            CompanyLogoStore.Delete(again.FileName);
            CompanyLogoStore.Delete(null);
            CompanyLogoStore.Delete(@"C:\Temp\nothing.png");
            True(true, "deleting a missing asset does not throw");

            // A file that disappears after it was stored must not break anything.
            File.Delete(Path.Combine(folder, imported.FileName));
            True(CompanyLogoStore.ResolvePath(imported.FileName) == null,
                "a stored asset that was deleted underneath resolves to null");
        });
    }

    // ── Migration and the single-profile constraint ──────────────────────

    private static void CheckMigrationAndConstraint()
    {
        WithTempFolder("migration", () =>
        {
            var repository = new CompanyProfileRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();

            Equal("True", repository.IsSchemaReadyAsync().GetAwaiter().GetResult().ToString(),
                "the profile table is ready after initialization");
            True(repository.IsSchemaReadyAsync().GetAwaiter().GetResult(),
                "the schema probe reports the table as ready");

            long version = QueryLong(@"PRAGMA user_version;");
            True(version >= CompanyProfileSchema.MigrationVersion,
                $"the chain advanced to at least {CompanyProfileSchema.MigrationVersion}, saw {version}");

            // Re-running must be a no-op, not a second table or a lost row.
            repository.InitializeAsync().GetAwaiter().GetResult();
            var second = new CompanyProfileRepository();
            second.InitializeAsync().GetAwaiter().GetResult();
            Equal("1", QueryLong(@"SELECT COUNT(*) FROM sqlite_master WHERE type='table' AND name='CompanyProfile';")
                .ToString(), "the table exists exactly once after two initializations");

            // The invariant: the database itself refuses a second profile. This is
            // the check that would pass if the guard were only a hidden button —
            // and it must FAIL to insert.
            AuthenticationContext.SignIn(Administrator());
            var saved = repository.SaveAsync(new CompanyProfileDraft { CompanyName = "Pro Glass LLC" })
                .GetAwaiter().GetResult();
            True(saved.Succeeded, "the first profile saves: " + saved.Message);

            bool refused = false;
            string? refusal = null;
            try
            {
                Execute(@"INSERT INTO ""CompanyProfile"" (""Id"", ""CompanyName"") VALUES (2, 'Second Company');");
            }
            catch (Exception error)
            {
                refused = true;
                refusal = error.Message;
            }

            True(refused, "a second profile row is refused by the database");
            True(refusal != null && refusal.Contains("CHECK", StringComparison.OrdinalIgnoreCase),
                "the refusal is the CHECK constraint: " + refusal);
            Equal("1", QueryLong(@"SELECT COUNT(*) FROM ""CompanyProfile"";").ToString(),
                "exactly one profile row exists");
            Equal(CompanyProfile.SingleId.ToString(CultureInfo.InvariantCulture),
                QueryLong(@"SELECT ""Id"" FROM ""CompanyProfile"";").ToString(),
                "the row uses the fixed single-profile id");

            AuthenticationContext.SignOut();
        });
    }

    // ── Repository round-trip ────────────────────────────────────────────

    private static void CheckRepositoryRoundTrip()
    {
        WithTempFolder("roundtrip", () =>
        {
            var repository = new CompanyProfileRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();

            var draft = new CompanyProfileDraft
            {
                CompanyName = "  Pro Glass   LLC ",
                Trn = "100 000 000 000 003",
                Address = "Shop 4\nMain Road\nAjman",
                Telephone = "+971 6 000 0000",
                Website = "https://proglass.ae",
                RepresentativeName = "Juber Saleem",
                RepresentativeMobile = "+971 50 000 0000",
                Email = "office@proglass.ae",
                BankName = "Emirates NBD",
                AccountNumber = "AE07 0331 2345 6789 0123 456",
                LogoLayout = CompanyLogoLayout.None,
                LogoSizeMm = 32
            };

            // Nobody signed in: the write is refused, and it says why.
            CompanyProfileSaveResult anonymous = repository.SaveAsync(draft).GetAwaiter().GetResult();
            True(!anonymous.Succeeded, "an anonymous save is refused");
            True(anonymous.Message.Contains("Sign in", StringComparison.OrdinalIgnoreCase),
                "the refusal says a sign-in is needed: " + anonymous.Message);
            True(repository.GetAsync().GetAwaiter().GetResult() == null,
                "the refused write stored nothing");

            // An authenticated non-administrator is refused by the write path, not
            // by a disabled button somewhere in the UI.
            AuthenticationContext.SignIn(new User
            {
                Id = 7,
                FullName = "Estimator",
                Username = "estimator",
                Department = "Estimation",
                Role = "Estimator",
                IsActive = true
            });
            CompanyProfileSaveResult notAdmin = repository.SaveAsync(draft).GetAwaiter().GetResult();
            True(!notAdmin.Succeeded, "a non-administrator save is refused");
            True(notAdmin.Message.Contains("Administrator", StringComparison.OrdinalIgnoreCase),
                "the refusal names the required role: " + notAdmin.Message);
            True(repository.GetAsync().GetAwaiter().GetResult() == null,
                "the non-administrator's refused write stored nothing");

            // An administrator saves, and the stored values are the NORMALISED ones.
            User admin = Administrator();
            AuthenticationContext.SignIn(admin);
            CompanyProfileSaveResult created = repository.SaveAsync(draft).GetAwaiter().GetResult();
            True(created.Succeeded, "an administrator saves the profile: " + created.Message);

            CompanyProfile? stored = repository.GetAsync().GetAwaiter().GetResult();
            True(stored != null, "the profile reads back");
            Equal("Pro Glass LLC", stored!.CompanyName, "the name is trimmed and inner runs collapsed");
            Equal("100000000000003", stored.Trn, "the TRN is stored as digits");
            // docs/12: "lines preserved, blank lines dropped" — the LINES are the
            // promise. The separator is Environment.NewLine, so a literal "\n" in
            // this file pinned a Linux assumption and only ever failed on Windows.
            Equal("Shop 4|Main Road|Ajman", JoinLines(stored.Address), "the address keeps its lines");
            Equal("Emirates NBD", stored.BankName, "the bank name round-trips");
            Equal("AE07 0331 2345 6789 0123 456", stored.AccountNumber, "the account number round-trips");
            Equal("32", stored.LogoSizeMm.ToString("0", CultureInfo.InvariantCulture), "the logo size round-trips");
            Equal(admin.Id.ToString(), (stored.UpdatedByUserId ?? 0).ToString(), "the save is attributed");
            Equal(admin.Id.ToString(), (stored.CreatedByUserId ?? 0).ToString(), "the first save records the creator");
            Equal(CompanyProfile.SingleId.ToString(CultureInfo.InvariantCulture),
                stored.Id.ToString(CultureInfo.InvariantCulture), "the row is the single profile");

            // Updating stores into the SAME row: a settings page must not grow a
            // second company every time it is saved.
            draft.CompanyName = "Pro Glass Industries LLC";
            CompanyProfileSaveResult updated = repository.SaveAsync(draft).GetAwaiter().GetResult();
            True(updated.Succeeded, "the profile updates: " + updated.Message);
            Equal("1", QueryLong(@"SELECT COUNT(*) FROM ""CompanyProfile"";").ToString(),
                "an update does not add a second profile");

            CompanyProfile? reread = repository.GetAsync().GetAwaiter().GetResult();
            Equal("Pro Glass Industries LLC", reread!.CompanyName, "the update is stored");
            Equal("Shop 4|Main Road|Ajman", JoinLines(reread.Address),
                "the rest of the profile survives an update");

            // Every optional field may be empty — only the name is required.
            CompanyProfileSaveResult onlyName = repository
                .SaveAsync(new CompanyProfileDraft { CompanyName = "Pro Glass LLC" })
                .GetAwaiter().GetResult();
            True(onlyName.Succeeded, "a name-only profile is accepted: " + onlyName.Message);
            CompanyProfile? minimal = repository.GetAsync().GetAwaiter().GetResult();
            Equal("", minimal!.Trn, "the cleared TRN stores as empty");
            Equal("", minimal.BankName, "the cleared bank name stores as empty");
            Equal("", minimal.LogoFileName, "there is no logo");

            // A refused save leaves the stored profile exactly as it was.
            CompanyProfile before = repository.GetAsync().GetAwaiter().GetResult()!;
            CompanyProfileSaveResult refused = repository
                .SaveAsync(new CompanyProfileDraft { CompanyName = "   ", Address = "changed" })
                .GetAwaiter().GetResult();
            True(!refused.Succeeded, "a blank name is refused at the repository too");
            True(refused.FieldErrors.ContainsKey("CompanyName"), "the refusal carries the field message");
            CompanyProfile after = repository.GetAsync().GetAwaiter().GetResult()!;
            Equal(before.CompanyName, after.CompanyName, "a refused save leaves the stored name untouched");
            Equal(before.Address, after.Address, "a refused save leaves the stored address untouched");

            // The read path documents will use.
            CompanyIdentitySnapshot? snapshot = repository.GetIdentityAsync().GetAwaiter().GetResult();
            True(snapshot != null, "the identity snapshot reads");
            Equal("Pro Glass LLC", snapshot!.CompanyName, "the snapshot carries the company name");
            Equal("False", snapshot.IsEmpty.ToString(), "a saved profile is not empty");

            AuthenticationContext.SignOut();
        });
    }

    // ── Logo lifecycle ───────────────────────────────────────────────────

    private static void CheckLogoLifecycle()
    {
        WithTempFolder("logos", () =>
        {
            var repository = new CompanyProfileRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();
            AuthenticationContext.SignIn(Administrator());

            string source = WritePng(Path.Combine(AppDataRoot.CurrentFolder, "picked.png"));

            // Save with a picked logo: the asset is imported and named on the row.
            CompanyProfileSaveResult withLogo = repository.SaveAsync(new CompanyProfileDraft
            {
                CompanyName = "Pro Glass LLC",
                PendingLogoSourcePath = source,
                LogoLayout = CompanyLogoLayout.LeftPortrait,
                LogoSizeMm = 24
            }).GetAwaiter().GetResult();
            True(withLogo.Succeeded, "a save with a picked logo succeeds: " + withLogo.Message);

            string firstLogo = withLogo.Profile!.LogoFileName;
            True(firstLogo.Length > 0, "the saved profile names a logo asset");
            True(File.Exists(CompanyLogoStore.ResolvePath(firstLogo) ?? ""), "the imported asset is on disk");
            Equal(CompanyLogoLayout.LeftPortrait.ToString(),
                repository.GetAsync().GetAwaiter().GetResult()!.LogoLayout.ToString(),
                "the placement is stored");

            // Replace: a new asset is stored, and the one it replaced is removed
            // only now that the row points at the new one.
            string secondSource = WritePng(Path.Combine(AppDataRoot.CurrentFolder, "picked-2.png"));
            CompanyProfileSaveResult replaced = repository.SaveAsync(new CompanyProfileDraft
            {
                CompanyName = "Pro Glass LLC",
                LogoFileName = firstLogo,
                PendingLogoSourcePath = secondSource,
                LogoLayout = CompanyLogoLayout.TopPanel,
                LogoSizeMm = 40
            }).GetAwaiter().GetResult();
            True(replaced.Succeeded, "a save with a replacement logo succeeds: " + replaced.Message);

            string secondLogo = replaced.Profile!.LogoFileName;
            True(secondLogo != firstLogo, "the replacement is a different asset");
            True(File.Exists(CompanyLogoStore.ResolvePath(secondLogo) ?? ""), "the new asset exists");
            True(!File.Exists(Path.Combine(CompanyLogoStore.Folder, firstLogo)),
                "the replaced asset is deleted after the commit");
            Equal(CompanyLogoLayout.TopPanel.ToString(),
                repository.GetAsync().GetAwaiter().GetResult()!.LogoLayout.ToString(),
                "the new placement is stored");

            // A save that fails after the import leaves the stored logo alone.
            CompanyProfileSaveResult failed = repository.SaveAsync(new CompanyProfileDraft
            {
                CompanyName = "   ",
                LogoFileName = secondLogo,
                PendingLogoSourcePath = secondSource,
                LogoLayout = CompanyLogoLayout.TopPanel
            }).GetAwaiter().GetResult();
            True(!failed.Succeeded, "a blank name with a new logo is refused");
            CompanyProfile unchanged = repository.GetAsync().GetAwaiter().GetResult()!;
            Equal(secondLogo, unchanged.LogoFileName, "the stored logo is untouched by the refused save");
            True(File.Exists(CompanyLogoStore.ResolvePath(secondLogo) ?? ""),
                "the stored logo file still exists after a refused save");

            // Clearing the logo removes the asset and empties the name.
            CompanyProfileSaveResult cleared = repository.SaveAsync(new CompanyProfileDraft
            {
                CompanyName = "Pro Glass LLC",
                LogoFileName = "",
                LogoLayout = CompanyLogoLayout.None
            }).GetAwaiter().GetResult();
            True(cleared.Succeeded, "clearing the logo succeeds: " + cleared.Message);
            Equal("", cleared.Profile!.LogoFileName, "the stored logo name is empty");
            True(!File.Exists(Path.Combine(CompanyLogoStore.Folder, secondLogo)),
                "the cleared asset is deleted");
            Equal(CompanyLogoLayout.None.ToString(), cleared.Profile.LogoLayout.ToString(),
                "clearing the logo also clears the placement");

            // A stored name that is not one this application writes is refused
            // rather than resolved against the filesystem.
            CompanyProfileSaveResult hostile = repository.SaveAsync(new CompanyProfileDraft
            {
                CompanyName = "Pro Glass LLC",
                LogoFileName = @"..\..\Windows\System32\notepad.exe",
                LogoLayout = CompanyLogoLayout.LeftPortrait
            }).GetAwaiter().GetResult();
            True(!hostile.Succeeded, "a stored name that is not an asset name is refused");

            AuthenticationContext.SignOut();
        });
    }

    // ── Editor states ────────────────────────────────────────────────────

    private static void CheckViewModelStates()
    {
        WithTempFolder("viewmodel", () =>
        {
            var repository = new CompanyProfileRepository();
            repository.InitializeAsync().GetAwaiter().GetResult();
            User admin = Administrator();
            AuthenticationContext.SignIn(admin);

            var editor = new CompanyProfileViewModel(repository);
            editor.EnsureLoadedAsync().GetAwaiter().GetResult();
            True(editor.IsLoaded, "the editor reads the (empty) profile");
            True(editor.ShowEmptyState, "an installation with no profile reports that it is not set up");
            True(editor.CanEdit, "an administrator may edit");
            Equal("False", editor.HasUnsavedChanges.ToString(), "a freshly loaded editor is clean");

            // Typing marks the page dirty, and the page says so in words.
            editor.CompanyName = "Pro Glass LLC";
            True(editor.HasUnsavedChanges, "typing marks the page dirty");
            True(editor.UnsavedText.Length > 0, "the unsaved state has a sentence, not just a flag");

            // A refused save claims nothing: no success message, still dirty.
            string savedName = editor.CompanyName;
            editor.Trn = "12345";
            bool refused = editor.SaveAsync().GetAwaiter().GetResult();
            True(!refused, "an invalid TRN refuses the save");
            True(!editor.HasStatus, "a refused save reports no success");
            True(editor.HasError, "a refused save reports a reason");
            True(editor.TrnError.Length > 0, "the TRN carries its own message");
            True(editor.HasUnsavedChanges, "the page stays dirty after a refused save");
            True(repository.GetAsync().GetAwaiter().GetResult() == null,
                "a refused save stores nothing");

            // Correcting the field clears its message as the operator types.
            editor.Trn = "100000000000003";
            Equal("", editor.TrnError, "the field message clears when the field is corrected");

            // A successful save clears the dirty flag and reports the save.
            bool saved = editor.SaveAsync().GetAwaiter().GetResult();
            True(saved, "a valid profile saves: " + editor.ErrorMessage);
            True(!editor.HasUnsavedChanges, "a successful save clears the dirty flag");
            True(editor.HasStatus, "a successful save reports it");
            Equal("False", editor.ShowEmptyState.ToString(), "a saved profile is no longer 'not set up'");

            // Reloading returns what was stored, from the same row.
            var reopened = new CompanyProfileViewModel(repository);
            reopened.EnsureLoadedAsync().GetAwaiter().GetResult();
            Equal(savedName, reopened.CompanyName, "the name comes back after reopening the page");
            Equal("100000000000003", reopened.Trn, "the TRN comes back after reopening the page");
            Equal("False", reopened.HasUnsavedChanges.ToString(), "a reopened page is clean");

            // A logo that was stored and then removed from disk shows as a problem
            // on the page — it never throws and never claims the logo is there.
            AuthenticationContext.SignIn(admin);
            string source = WritePng(Path.Combine(AppDataRoot.CurrentFolder, "editor.png"));
            reopened.SetPickedLogo(source);
            reopened.LogoLayout = CompanyLogoLayout.LeftPortrait;
            True(reopened.HasLogoPreview, "a picked image previews before it is saved");
            True(reopened.SaveAsync().GetAwaiter().GetResult(), "the picked logo saves");

            string storedLogo = repository.GetAsync().GetAwaiter().GetResult()!.LogoFileName;
            File.Delete(Path.Combine(CompanyLogoStore.Folder, storedLogo));

            var reopenedAgain = new CompanyProfileViewModel(repository);
            reopenedAgain.EnsureLoadedAsync().GetAwaiter().GetResult();
            True(!reopenedAgain.HasLogoPreview, "a missing logo file shows no preview");
            True(reopenedAgain.HasLogoPreviewProblem, "a missing logo file is explained on the page");
            True(reopenedAgain.CompanyName.Length > 0, "a missing logo file does not stop the profile loading");

            // Removing the logo marks the page dirty without touching the database
            // until the save.
            reopenedAgain.RemoveLogo();
            True(reopenedAgain.HasUnsavedChanges, "removing the logo marks the page dirty");
            Equal(storedLogo, repository.GetAsync().GetAwaiter().GetResult()!.LogoFileName,
                "the stored logo is untouched until the save");

            // A non-administrator can read but not write — and the page says why.
            AuthenticationContext.SignIn(new User
            {
                Id = 8,
                FullName = "Estimator",
                Username = "estimator",
                Department = "Estimation",
                Role = "Estimator",
                IsActive = true
            });
            var readOnly = new CompanyProfileViewModel(repository);
            readOnly.EnsureLoadedAsync().GetAwaiter().GetResult();
            True(!readOnly.CanEdit, "a non-administrator cannot edit");
            True(readOnly.HasReadOnlyReason, "the page prints why it is read-only");
            True(readOnly.CompanyName.Length > 0, "a non-administrator can still read the profile");
            readOnly.CompanyName = "Changed By The Wrong Person";
            True(!readOnly.SaveAsync().GetAwaiter().GetResult(), "the save is refused for a non-administrator");
            True(!readOnly.HasStatus, "the refused save reports no success");
            True(repository.GetAsync().GetAwaiter().GetResult()!.CompanyName != "Changed By The Wrong Person",
                "the refused save changed nothing on disk");

            AuthenticationContext.SignOut();
        });
    }

    // ── Fixtures and helpers ─────────────────────────────────────────────

    /// <summary>The administrator every write check signs in as.</summary>
    private static User Administrator() => new()
    {
        Id = 1,
        FullName = "Juber Saleem",
        Username = "juber",
        NormalizedUsername = "JUBER",
        Department = "Administration",
        Role = UserCatalog.AdministratorRole,
        IsActive = true
    };

    private static void WithTempFolder(string name, Action check)
    {
        string tempFolder = Path.Combine(
            Path.GetTempPath(), "proglass-step4-" + name + "-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(tempFolder);
        AppDataRoot.TestFolderOverride = tempFolder;
        try
        {
            check();
        }
        finally
        {
            AuthenticationContext.SignOut();
            AppDataRoot.TestFolderOverride = null;
            try { Directory.Delete(tempFolder, recursive: true); } catch { /* best effort */ }
        }
    }

    /// <summary>Writes the real one-pixel PNG above to <paramref name="path"/>.</summary>
    private static string WritePng(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, Convert.FromBase64String(OnePixelPngBase64));
        return path;
    }

    private static Microsoft.Data.Sqlite.SqliteConnection OpenDatabase()
    {
        var connection = new Microsoft.Data.Sqlite.SqliteConnection(
            SqliteResilience.BuildConnectionString(CompanyProfileDbContext.DatabaseFilePath));
        connection.Open();
        SqliteResilience.ApplyConnectionPolicy(connection);
        return connection;
    }

    private static long QueryLong(string sql)
    {
        using var connection = OpenDatabase();
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        object? value = command.ExecuteScalar();
        return value == null || value is DBNull
            ? 0L
            : Convert.ToInt64(value, CultureInfo.InvariantCulture);
    }

    private static void Execute(string sql)
    {
        using var connection = OpenDatabase();
        SchemaMigrationSql.Execute(connection, sql);
    }

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

    /// <summary>
    /// The newline-independent form of a stored multi-line value: the lines, in
    /// order, joined by "|". A test that compares the raw string is asserting the
    /// platform's newline, not the rule.
    /// </summary>
    private static string JoinLines(string? value) =>
        string.Join("|", (value ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'));

    private static void Equal(string expected, string actual, string message)
    {
        if (!string.Equals(expected, actual, StringComparison.Ordinal))
            throw new InvalidOperationException($"{message}: expected \"{expected}\", got \"{actual}\"");
    }
}
