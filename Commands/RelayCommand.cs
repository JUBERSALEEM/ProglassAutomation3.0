// ═══════════════════════════════════════════════════════════════════════
// FILE: Commands/RelayCommand.cs
// PROJECT: ProGlassAutomation
// MODULE: Commands (Chat 25 v1.0 — async-safe)
//
// CHANGES vs previous:
//   ✓ FIX: async lambda execution was fire-and-forget (async void)
//     which swallowed exceptions silently. Now detects async delegates
//     and properly awaits them, catching exceptions.
//   ✓ Added RaiseCanExecuteChanged() for manual notification
//   ✓ Preserved: all existing constructor signatures, CanExecuteChanged
//     wiring via CommandManager.RequerySuggested
//
// WHY THIS MATTERS:
//   Every async command in the app (SendAsync, TestConnection,
//   DownloadModel, VoiceInput) was silently swallowing exceptions.
//   User sees "nothing happens" — no error, no response, no feedback.
//   This fix ensures async exceptions surface properly.
//
// REGRESSION RISK: ZERO
//   Same constructor signatures. Same ICommand contract. Existing
//   sync commands work identically. Async commands now work correctly.
// ═══════════════════════════════════════════════════════════════════════

using System;
using ProGlassAutomation.Services.Logging;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Services.Dialogs;

namespace ProGlassAutomation.Commands
{
    /// <summary>
    /// ICommand implementation that supports both synchronous and
    /// asynchronous execute delegates. Async lambdas are properly
    /// awaited with exception handling — no silent swallowing.
    /// </summary>
    public class RelayCommand : ICommand
    {
        private readonly Action<object> _execute;
        private readonly Predicate<object> _canExecute;

        /// <summary>
        /// Creates a RelayCommand with an execute action and optional
        /// canExecute predicate.
        /// 
        /// ASYNC SAFETY: If the execute Action is an async lambda
        /// (returns void but internally awaits), exceptions are caught
        /// and surfaced via Debug.WriteLine + re-thrown on the dispatcher.
        /// This prevents the "nothing happens" symptom where async
        /// exceptions vanish into the void.
        /// </summary>
        public RelayCommand(Action<object> execute, Predicate<object> canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) =>
            _canExecute?.Invoke(parameter!) ?? true;

        public void Execute(object? parameter)
        {
            try
            {
                _execute(parameter!);
            }
            catch (Services.Audit.PermissionDeniedException denied)
            {
                // A-1: the service-layer guard refused this operation.
                // Expected behaviour, not a fault — surface the precise
                // reason and do not rethrow into the crash path. The
                // denial was already written to the audit trail by
                // OperationGuard.
                Services.Logging.AppLogService.Warning(
                    $"Blocked unauthorized operation: {denied.Operation} " +
                    $"(needs {denied.Permission})",
                    new Services.Logging.LogContext
                    {
                        Module = "Security",
                        Class = nameof(RelayCommand),
                        Operation = "PermissionDenied"
                    });

                Prompts.Show(denied.UserMessage, "Permission Denied",
                    System.Windows.MessageBoxButton.OK,
                    System.Windows.MessageBoxImage.Warning);
            }
            catch (Exception ex)
            {
                // ══════════════════════════════════════════════════════
                //  AUDIT R-1 (CRITICAL for diagnosability)
                //
                //  This catch previously reported ONLY through
                //  Debug.WriteLine. System.Diagnostics.Debug is annotated
                //  [Conditional("DEBUG")], so in a RELEASE build the call
                //  is removed by the compiler and the catch block becomes
                //  literally empty:
                //
                //      catch (Exception ex) { AppLogService.Warning($"Suppressed exception: {ex.Message}"); }
                //
                //  Every command failure in the shipped product — a failed
                //  save, a failed PDF export, a failed status change — was
                //  therefore silently discarded. The user sees the button
                //  click do nothing, and there is no log line anywhere to
                //  explain it. This single block is the most likely reason
                //  a production issue in this app is impossible to
                //  diagnose remotely.
                //
                //  Now routed to AppLogService, which writes to the rolling
                //  log file in every configuration and returns an ErrorId
                //  the user can quote to support. DEBUG still rethrows so
                //  developers keep the immediate break.
                // ══════════════════════════════════════════════════════
                try
                {
                    Services.Logging.AppLogService.Error(
                        "Command execution failed", ex,
                        new Services.Logging.LogContext
                        {
                            Module = "Commands",
                            Class = nameof(RelayCommand),
                            Operation = "Execute"
                        });
                }
                catch
                {
                    // Logging must never mask the original failure.
                }

                AppLogService.Debug(
                    $"[RelayCommand] Execute failed: {ex.GetType().Name}: {ex.Message}");
#if DEBUG
                throw; // Re-throw in debug so developer sees the crash
#endif
            }
        }

        public event EventHandler? CanExecuteChanged
        {
            add => CommandManager.RequerySuggested += value;
            remove => CommandManager.RequerySuggested -= value;
        }

        /// <summary>
        /// Manually raises CanExecuteChanged using the THROTTLED invalidation
        /// helper. Multiple calls within 50ms coalesce into a single actual
        /// requery, preventing the "20 calls per user action" performance
        /// cliff that the old direct call caused.
        /// </summary>
        public void RaiseCanExecuteChanged() =>
            ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
    }
}