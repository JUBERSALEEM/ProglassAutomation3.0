// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Dialogs/IDialogService.cs
// PROJECT: ProGlassAutomation
// MODULE: MVVM leakage sweep (Phase 2, audit item 2.1)
//
// Single interception point for user-facing dialogs. ViewModels must NOT
// call MessageBox.* directly — they depend on this service (currently
// resolved as App.Dialogs, the provider-backed static migration pattern).
//
// The current implementation delegates to MessageBox.Show so behaviour is
// byte-identical; later it can be swapped for a themed/owner-aware dialog
// host, or faked in unit tests, without touching 200+ call sites.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;

namespace ProGlassAutomation.Services.Dialogs
{
    /// <summary>Dialog abstraction for ViewModels (MVVM boundary).</summary>
    public interface IDialogService
    {
        /// <summary>Generic show; same signature as MessageBox.Show.
        /// <c>defaultResult</c> (None = no explicit default) is forwarded so
        /// Yes/No confirms can default to No (Phase 3.7 — picker override
        /// confirm).</summary>
        MessageBoxResult Show(string message, string? caption = null,
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None);

        /// <summary>Information message (OK).</summary>
        void Info(string message, string? caption = null);

        /// <summary>Warning message (OK).</summary>
        void Warning(string message, string? caption = null);

        /// <summary>Error message (OK).</summary>
        void Error(string message, string? caption = null);

        /// <summary>Yes/No confirmation. Returns true when the user picked
        /// Yes.</summary>
        bool Confirm(string message, string? caption = null);

        // ── Window dialogs (Phase 2.6) ──────────────────────────────────
        // ViewModels must not instantiate windows (MVVM leakage). These
        // create the window via the registered factory, attach the main
        // window as Owner, and return/show it. Result-heavy dialogs expose
        // their results as view properties read after Create/ShowDialog.
        // NOTE: the implementation references Views — it moves to the App
        // layer in the Phase 5.2 Core/App split (interface stays Core-safe).

        /// <summary>Creates the window (Owner = main window) without showing it.</summary>
        TWindow Create<TWindow>(params object[] ctorArgs) where TWindow : System.Windows.Window;

        /// <summary>Creates and shows the window modally; returns its dialog result.</summary>
        bool? ShowDialog<TWindow>(params object[] ctorArgs) where TWindow : System.Windows.Window;

        /// <summary>Creates and shows the window non-modally.</summary>
        void Show<TWindow>(params object[] ctorArgs) where TWindow : System.Windows.Window;
    }
}
