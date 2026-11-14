// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Database/IDatabaseServices.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 2.4 — MVVM leakage sweep. ViewModels previously reached for the
// static utilities in this folder (DependencyGuard, DocumentLifecycle,
// AppDataRoot) directly. This facade funnels those calls through one
// injectable service so VMs stay testable and the statics remain the
// single implementation underneath (no behaviour change).
//
// Not wrapped here (by design, tracked in INDUSTRIAL_CODE_AUDIT §11):
//   • DatabaseBackup.BackupService / SchemaHealthService / DB diagnostics
//     — Settings-only maintenance features; extracted with the
//     SettingsViewModel god-file split (Phase 3).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Injectable facade over the static database-adjacent utilities that
    /// ViewModels legitimately need: delete dependency guards, document
    /// change notifications, and app-data folder resolution.
    /// </summary>
    public interface IDatabaseServices
    {
        /// <summary>
        /// Delete guard: would deleting this document orphan dependents?
        /// Pass <paramref name="strict"/> = true for a posted/fiscal parent so
        /// a failed verification fails CLOSED (blocks) instead of open.
        /// </summary>
        Task<DependencyCheckResult> CheckDependencyAsync(
            DependencyGuard.Kind kind, string id, bool strict = false);

        /// <summary>Subscribe to document-changed notifications (static DocumentLifecycle).</summary>
        void SubscribeDocumentsChanged(Func<Task> reload);

        /// <summary>Detach a document-changed subscriber.</summary>
        void UnsubscribeDocumentsChanged(Func<Task> reload);

        /// <summary>True when running against the isolated demo data folder.</summary>
        bool IsDemo { get; }

        /// <summary>Folder for demo data (isolated from production files).</summary>
        string DemoFolder { get; }

        /// <summary>Folder for production data.</summary>
        string ProductionFolder { get; }

        /// <summary>Folder currently in use (demo or production).</summary>
        string CurrentFolder { get; }
    }

    /// <summary>Default implementation — delegates to the existing statics.</summary>
    public sealed class DatabaseServices : IDatabaseServices
    {
        public Task<DependencyCheckResult> CheckDependencyAsync(
            DependencyGuard.Kind kind, string id, bool strict = false)
            => DependencyGuard.CheckAsync(kind, id, strict);

        public void SubscribeDocumentsChanged(Func<Task> reload)
            => DocumentLifecycle.Subscribe(reload);

        public void UnsubscribeDocumentsChanged(Func<Task> reload)
            => DocumentLifecycle.Unsubscribe(reload);

        public bool IsDemo => AppDataRoot.IsDemo;
        public string DemoFolder => AppDataRoot.DemoFolder;
        public string ProductionFolder => AppDataRoot.ProductionFolder;
        public string CurrentFolder => AppDataRoot.CurrentFolder;
    }
}
