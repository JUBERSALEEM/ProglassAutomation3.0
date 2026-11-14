// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Dialogs/DialogService.cs
// PROJECT: ProGlassAutomation
// MODULE: MVVM leakage sweep (Phase 2, audit item 2.1)
//
// Default IDialogService. Message prompts (Show/Info/Warning/Error/
// Confirm) render the THEMED message box — ThemedMessageBoxWindow, the
// shared ThemedDialogWindow chrome — since the 2026-09 senior dialog
// audit (previously the native OS MessageBox, which did not follow the
// theme and clipped long localized text). Window dialogs (Create/
// ShowDialog/Show) are unchanged. Registered as a singleton in DI and
// exposed as App.Dialogs.
//
// THREAD AFFINITY (Sept 2026 senior audit — Sharing Center STA crash)
// ───────────────────────────────────────────────────────────────────
//   WPF Window construction (InitializeComponent) and ShowDialog REQUIRE
//   the UI (STA) thread. Callers that reach these methods from a
//   background/thread-pool thread — e.g. an async share flow whose
//   continuation resumed off the UI thread after ConfigureAwait(false) —
//   used to crash with "The calling thread must be STA…", surfaced as a
//   TargetInvocationException ("Exception has been thrown by the target of
//   an invocation") out of Activator.CreateInstance.
//
//   Every window method below now marshals its body onto the application
//   Dispatcher when the caller is off-thread (a no-op when already on the
//   UI thread), so window dialogs open reliably from any thread, app-wide.
//   MessageBox helpers are marshalled too so error prompts keep the right
//   owner and never race the UI thread.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Windows;
using System.Windows.Threading;
using ProGlassAutomation.Views.Shared.Dialogs;

namespace ProGlassAutomation.Services.Dialogs
{
    public sealed class DialogService : IDialogService
    {
        /// <summary>
        /// Runs <paramref name="work"/> on the WPF UI (STA) thread and
        /// returns its result. Executes inline when already on the UI
        /// thread (or when no Application/Dispatcher exists, e.g. a
        /// headless test host); otherwise blocks the caller on a
        /// Dispatcher.Invoke so modal semantics are preserved.
        /// </summary>
        private static T OnUi<T>(Func<T> work)
        {
            Dispatcher? disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess())
                return work();
            return disp.Invoke(work);
        }

        private static void OnUi(Action work)
        {
            Dispatcher? disp = Application.Current?.Dispatcher;
            if (disp == null || disp.CheckAccess())
            {
                work();
                return;
            }
            disp.Invoke(work);
        }

        public TWindow Create<TWindow>(params object[] ctorArgs) where TWindow : System.Windows.Window
            => OnUi(() =>
            {
                var window = (TWindow)System.Activator.CreateInstance(typeof(TWindow), ctorArgs)!;
                if (window.Owner == null)
                    window.Owner = System.Windows.Application.Current?.MainWindow;
                return window;
            });

        public bool? ShowDialog<TWindow>(params object[] ctorArgs) where TWindow : System.Windows.Window
            => OnUi(() => Create<TWindow>(ctorArgs).ShowDialog());

        public void Show<TWindow>(params object[] ctorArgs) where TWindow : System.Windows.Window
            => OnUi(() => Create<TWindow>(ctorArgs).Show());
        // ─────────────────────────────────────────────────────────────────
        //  MESSAGE PROMPTS — themed (2026-09 senior dialog audit)
        // ─────────────────────────────────────────────────────────────────
        //  These previously delegated to the native OS MessageBox (unthemed,
        //  non-wrapping, tiny) — the root cause of the "broken text / window
        //  too small" reports on Delivery Note delete/void and ~260 other
        //  prompts. They now render ThemedMessageBoxWindow (shared dialog
        //  chrome: wrapping + scrolling message, theme surfaces, owner
        //  awareness, native default/cancel semantics) with a native
        //  fallback so a prompt can never be swallowed.

        public MessageBoxResult Show(string message, string? caption = null,
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None)
            => OnUi(() => ThemedMessageBox.Show(
                ResolveOwner(), message, caption ?? string.Empty,
                button, icon, defaultResult));

        public void Info(string message, string? caption = null)
            => Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Information);

        public void Warning(string message, string? caption = null)
            => Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Warning);

        public void Error(string message, string? caption = null)
            => Show(message, caption, MessageBoxButton.OK, MessageBoxImage.Error);

        public bool Confirm(string message, string? caption = null)
            => Show(message, caption, MessageBoxButton.YesNo, MessageBoxImage.Question)
               == MessageBoxResult.Yes;

        /// <summary>Loaded main window (or null — ThemedMessageBox then shows
        /// owner-less, centred on screen, like the native fallback).</summary>
        private static Window? ResolveOwner()
        {
            var owner = Application.Current?.MainWindow;
            return owner != null && owner.IsLoaded ? owner : null;
        }
    }

    // ═══════════════════════════════════════════════════════════════════
    //  Prompts — the entry point for NON-ViewModel code (views, services,
    //  utilities, app shell). Previously ~39 call sites rendered the raw
    //  OS MessageBox; they now go through the same themed dialog platform
    //  as everything else (2026-09 senior dialog audit).
    //
    //  Headless/test safety: when the app has not started (App.Dialogs not
    //  yet assigned) this degrades to the native MessageBox — identical
    //  behaviour to before, never a NullReferenceException.
    // ═══════════════════════════════════════════════════════════════════
    public static class Prompts
    {
        public static MessageBoxResult Show(string message, string? caption = null,
            MessageBoxButton button = MessageBoxButton.OK,
            MessageBoxImage icon = MessageBoxImage.None,
            MessageBoxResult defaultResult = MessageBoxResult.None)
        {
            IDialogService? service = ProGlassAutomation.App.Dialogs;
            if (service != null)
                return service.Show(message, caption, button, icon, defaultResult);
            return MessageBox.Show(message, caption ?? string.Empty,
                button, icon, defaultResult);
        }
    }
}
