// ═══════════════════════════════════════════════════════════════════════
// FILE: Diagnostics/tests/AppLogBehaviourChecks.cs
// MODULE: verification — the controlled-exception test for the error log.
//
// The audit report may not claim "crashes are logged" from reading the source.
// This executable raises a real exception, pushes it through the real logger
// and then reads the real file back, so the claim is evidence rather than
// source archaeology.
//
// Everything happens in a throw-away folder through
// AppDataRoot.TestFolderOverride: the owner's %APPDATA% folder, its databases
// and its log are never opened, written or deleted by this file.
//
// Exit code 0 = every check passed. Run on Windows/.NET 8:
//   dotnet run --project Diagnostics/tests/AppLogBehaviourChecks.csproj
//
// What it deliberately does NOT test: an unhandled crash on the dispatcher, a
// fatal AppDomain fault and an unobserved task fault. Those end (or threaten)
// the process, so they belong to a manual run of the application, and the audit
// document records them as not executed here rather than inferred.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.Services.Database;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace ProGlassAutomation.Diagnostics.Checks;

internal static class AppLogBehaviourChecks
{
    private static int _passed;
    private static readonly List<string> Failures = new();

    private static int Main()
    {
        string root = Path.Combine(Path.GetTempPath(), "proglass-logcheck-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);

        string? original = AppDataRoot.TestFolderOverride;
        AppDataRoot.TestFolderOverride = root;

        try
        {
            NothingIsWrittenBeforeAFailure(root);
            RealExceptionProducesReadableRecord(root);
            SecretsAndBusinessDataAreRedacted(root);
            AWriteFaultIsSwallowedAndReported(root);
            ARepeatIsCountedNotRewritten(root);
        }
        finally
        {
            AppDataRoot.TestFolderOverride = original;
            TryDelete(root);
        }

        Console.WriteLine();
        if (Failures.Count > 0)
        {
            Console.WriteLine($"Diagnostics behaviour checks: {_passed} passed, {Failures.Count} FAILED.");
            foreach (string failure in Failures) Console.WriteLine("  FAIL: " + failure);
            return 1;
        }

        Console.WriteLine($"Diagnostics behaviour checks: PASS ({_passed} assertions).");
        return 0;
    }

    // ── 1. Failure-only ────────────────────────────────────────────────
    private static void NothingIsWrittenBeforeAFailure(string root)
    {
        string logs = Path.Combine(root, "Logs");

        Check(!Directory.Exists(logs),
            "no Logs folder exists before any failure — a clean run leaves nothing on disk");
        Check(AppLogFile.ExistingFiles().Length == 0,
            "no log file exists before any failure");
        Check(AppLog.EntriesRecorded == 0,
            "the logger reports zero records before any failure");
    }

    // ── 2. A real exception, read back ─────────────────────────────────
    private static void RealExceptionProducesReadableRecord(string root)
    {
        bool written;
        try
        {
            // Deliberately thrown, not constructed: without a throw there is no
            // stack trace, and the stack is the part that makes a log useful.
            throw new InvalidOperationException("deliberate failure raised by the log check");
        }
        catch (Exception error)
        {
            written = AppLog.Error("check.controlled-exception", error,
                "the controlled-exception test");
        }

        Check(written, "Error() reported that the record was written");
        Check(File.Exists(Path.Combine(root, "Logs", AppLogFile.FileName)),
            "the log file was created by the first failure");

        string text = File.ReadAllText(AppLogFile.CurrentPath);

        Check(text.Contains("[ERROR]"), "the record states the severity ERROR");
        Check(text.Contains("System.InvalidOperationException"),
            "the record names the exception's full type");
        Check(text.Contains("deliberate failure raised by the log check"),
            "the record carries the exception message");
        Check(text.Contains("operation : check.controlled-exception"),
            "the record names the operation");
        Check(text.Contains("module    : check"),
            "the record names the module derived from the operation");
        Check(text.Contains("stack     :") && text.Contains("AppLogBehaviourChecks"),
            "the record carries the stack trace, including this frame");
        Check(text.Contains("UTC+") || text.Contains("UTC-"),
            "the timestamp states its offset, so two machines' logs can be compared");
        Check(System.Text.RegularExpressions.Regex.IsMatch(
                text, @"\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}\.\d{3}[+-]\d{2}:\d{2}"),
            "the timestamp is round-trip formatted with an explicit offset");
        Check(text.Contains("ProGlassAutomation "),
            "the record carries the application version");
        Check(text.Contains("context   : check.controlled-exception")
                  || text.Contains("context   : the controlled-exception test"),
            "the record carries the caller's context line");
    }

    // ── 3. Redaction ───────────────────────────────────────────────────
    private static void SecretsAndBusinessDataAreRedacted(string root)
    {
        var error = new InvalidOperationException(
            "password=hunter2 token: abc123def hash=9f8e7d salt=deadbeef "
            + "iban 1234 5678 9012 owner@example.com account 12345678901234");
        AppLog.Error("check.redaction", error, "password=contextsecret");

        string text = File.ReadAllText(AppLogFile.CurrentPath);

        foreach (string secret in new[]
                 {
                     "hunter2", "abc123def", "9f8e7d", "deadbeef", "contextsecret",
                     "1234 5678 9012", "owner@example.com", "12345678901234"
                 })
        {
            Check(!text.Contains(secret), $"the log does not contain '{secret}'");
        }

        Check(text.Contains("password=***") || text.Contains("password =***"),
            "the password field is present but masked");
        Check(text.Contains(AppLogRedaction.Mask),
            "redaction replaced at least one value with the mask");
    }

    // ── 4. Logging never becomes the incident ──────────────────────────
    private static void AWriteFaultIsSwallowedAndReported(string root)
    {
        string blocked = Path.Combine(root, "blocked");
        File.WriteAllText(blocked, "this is a file, so the folder beneath it cannot exist");

        int before = AppLogFile.RecordsWritten;
        AppDataRoot.TestFolderOverride = blocked;

        bool written;
        try
        {
            written = AppLog.Error("check.write-fault",
                new InvalidOperationException("the folder this log needs cannot be created"));
        }
        catch (Exception unexpected)
        {
            Check(false, "a write fault threw instead of returning false: " + unexpected.GetType().Name);
            AppDataRoot.TestFolderOverride = root;
            return;
        }

        AppDataRoot.TestFolderOverride = root;

        Check(!written, "a write that cannot happen reports false");
        Check(AppLogFile.RecordsWritten == before,
            "a failed write does not count as a record");
        Check(AppLog.LastWriteFailure.Length > 0,
            "the failure is kept in LastWriteFailure, where a check can see it");
        Check(!Directory.Exists(Path.Combine(blocked, "Logs")),
            "nothing was created where the write failed");
    }

    // ── 5. Bounded chatter ─────────────────────────────────────────────
    private static void ARepeatIsCountedNotRewritten(string root)
    {
        // Both exceptions are constructed, not thrown, so their signatures (type,
        // operation, first frame) are identical — the case a failing loop produces.
        var first = new InvalidOperationException("the same failure twice");
        var second = new InvalidOperationException("the same failure twice");

        bool one = AppLog.Error("check.repeat", first, "first");
        bool two = AppLog.Error("check.repeat", second, "second");

        Check(one, "the first of two identical failures is written");
        Check(!two, "an identical failure inside the window is counted, not re-written");

        AppLog.Error("check.repeat-distinct", new ArgumentException("a different failure"));

        string text = File.ReadAllText(AppLogFile.CurrentPath);
        Check(text.Contains("app.log.repeat-suppressed"),
            "the suppressed count is emitted with the next distinct record");
    }

    // ── helpers ────────────────────────────────────────────────────────
    private static void Check(bool condition, string message)
    {
        if (condition)
        {
            _passed++;
            return;
        }

        Failures.Add(message);
    }

    private static void TryDelete(string folder)
    {
        try
        {
            if (Directory.Exists(folder)) Directory.Delete(folder, recursive: true);
        }
        catch (Exception)
        {
            // A leftover temp folder is not a test failure.
        }
    }
}
