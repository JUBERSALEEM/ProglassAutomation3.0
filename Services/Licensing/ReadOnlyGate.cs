// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Licensing/ReadOnlyGate.cs
// PROJECT: ProGlassAutomation
// MODULE: Licensing — read-only enforcement gate
//
// PURPOSE
// ───────
// The August 2026 licensing refactor made trial expiry and paid-license
// grace-period map to `LicenseStatus.ReadOnly` instead of blocking the
// app entirely — customer data stays reachable, but new writes should
// be refused. Before this file existed the "read-only" state was UI
// messaging only: a yellow banner on top of the shell said "read-only
// mode", but every write command (Save, Delete, Import, etc.) still
// went through and mutated the database.
//
// ReadOnlyGate is the service-layer analogue of OperationGuard: it
// evaluates the current license state, writes an audit entry, and
// throws when a write is attempted in a read-only session. Repositories
// call `Demand(...)` alongside the existing `OperationGuard.Demand(...)`
// permission check.
//
// SYMMETRY WITH OPERATIONGUARD
// ────────────────────────────
//   OperationGuard.Demand(permission, "create a proforma invoice");
//   ReadOnlyGate.Demand("create a proforma invoice");
//
// Both throw a well-named exception the caller can catch and translate
// into a friendly message. Both audit the denial. Both have an
// `Enabled` flag test-only escape hatch that never touches config.
//
// FAIL-CLOSED
// ───────────
// If LicenseService.Current has not been initialized (Initialize()
// has not run yet) the current status is `NotActivated` and `ReadOnly`
// is false — so the gate does NOT block. That is deliberate: an
// uninitialised license state is a startup race, not a licensing
// decision, and blocking writes at that moment would confuse the user
// with a "trial expired" message when they just launched the app.
// The license overlay + banner handle the missing-license case
// separately.
//
// TEST-ONLY OVERRIDE
// ──────────────────
// `OverrideReadOnlyForTests` lets a unit test flip the gate ON without
// standing up an entire license file. Deliberately named ugly and
// documented with `EditorBrowsable(Never)` so it cannot be mistaken for
// a real feature. Setting it to null returns to reading `LicenseService`.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Logging;

namespace ProGlassAutomation.Services.Licensing
{
    /// <summary>
    /// Thrown when a write is attempted while the current license is in
    /// read-only mode (trial expired, or a paid licence in its
    /// post-expiry grace period).
    /// </summary>
    public sealed class ReadOnlyModeException : InvalidOperationException
    {
        /// <summary>Short description of the blocked operation.</summary>
        public string Operation { get; }

        /// <summary>The license status that caused the block.</summary>
        public LicenseStatus Status { get; }

        public ReadOnlyModeException(string operation, LicenseStatus status)
            : base(BuildMessage(operation, status))
        {
            Operation = operation;
            Status = status;
        }

        /// <summary>User-safe message the ViewModel can show in a dialog.</summary>
        public string UserMessage =>
            "The app is in read-only mode.\n\n" +
            "Action blocked: " + Operation + "\n" +
            "Reason: " + StatusText(Status) + "\n\n" +
            "Your data is preserved. Activate a full licence in " +
            "Settings → License to keep creating and editing records.";

        private static string BuildMessage(string operation, LicenseStatus status)
            => "Read-only mode blocks '" + operation +
               "' (license status: " + status + ").";

        private static string StatusText(LicenseStatus s) => s switch
        {
            LicenseStatus.TrialExpired => "the 30-day trial has ended",
            LicenseStatus.GracePeriod  => "the paid licence expired but is still in its 7-day grace period",
            _                          => "the current licence does not permit writes"
        };
    }

    /// <summary>
    /// Headless read-only enforcement gate. Repositories call
    /// <see cref="Demand"/> at the top of every mutating method
    /// alongside the existing <c>OperationGuard.Demand</c> permission
    /// check. See file header for the full contract.
    /// </summary>
    public static class ReadOnlyGate
    {
        /// <summary>
        /// When false the gate is a no-op. Used ONLY by tests that
        /// exercise repositories without standing up a license file.
        /// Never wired to config or settings.
        /// </summary>
        public static bool Enabled { get; set; } = true;

        /// <summary>
        /// Test-only override. Not null → the gate uses this value
        /// instead of reading <see cref="LicenseService.IsReadOnlyMode"/>.
        /// Null → normal production behaviour.
        /// </summary>
        [EditorBrowsable(EditorBrowsableState.Never)]
        public static bool? OverrideReadOnlyForTests { get; set; }

        /// <summary>
        /// True when the current session is in read-only mode. Prefer
        /// <see cref="Demand"/> for the enforcement side and use this
        /// only for ViewModel-side <c>CanExecute</c> gating.
        /// </summary>
        public static bool IsReadOnly
        {
            get
            {
                if (!Enabled) return false;
                if (OverrideReadOnlyForTests.HasValue)
                    return OverrideReadOnlyForTests.Value;
                return LicenseService.IsReadOnlyMode;
            }
        }

        /// <summary>
        /// Throws <see cref="ReadOnlyModeException"/> when the current
        /// session is in read-only mode.
        ///
        /// Audit-writes the denial BEFORE throwing so a blocked write
        /// is always visible on the audit trail — a denial that was
        /// never recorded is a compliance hole.
        /// </summary>
        public static void Demand(string operation)
        {
            if (!Enabled) return;
            if (!IsReadOnly) return;

            var status = OverrideReadOnlyForTests.HasValue
                ? LicenseStatus.TrialExpired   // best-effort synthetic
                : LicenseService.Current.Status;

            try
            {
                AuditService.Log(
                    AuditSections.Settings,
                    "Read-only mode blocked write",
                    (operation ?? "") + " · status=" + status);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "ReadOnlyGate audit-write failed: " + ex.Message,
                    new LogContext
                    {
                        Module = "Licensing",
                        Class = nameof(ReadOnlyGate),
                        Operation = "Demand"
                    });
            }

            throw new ReadOnlyModeException(operation ?? "unknown", status);
        }
    }
}
