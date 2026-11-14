// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Security/AuthSessionService.cs
// PROJECT: ProGlassAutomation
// MODULE: Authentication — session lifecycle helper (Aug 2026 senior audit)
//
// PURPOSE
// ───────
// Two behaviours the app was missing:
//
//   1. **In-app "Sign out & lock"** — the previous shell had no way for
//      an operator to explicitly log out. The only way to switch users
//      was to restart the app. That is a security antipattern in a
//      shared shop-floor terminal: a shift ending has no clean handoff.
//
//   2. **Clear-saved-credentials** — the "Skip login until update" pill
//      persists a DPAPI-encrypted password in SecretVault. The user had
//      no self-service way to revoke it (short of uninstalling). If a
//      user borrowed the terminal, armed the pill, and walked away,
//      the owner's next launch would silently sign in as them.
//
// SIGN-OUT CONTRACT
// ─────────────────
// SignOutAsync():
//   • Clears the auto-sign-in vault entry (SkipLoginSecretKey).
//   • Clears AppConfig.SkipLoginUntilUpdate + SkipLoginBoundVersion.
//   • Writes an audit boundary (LogSessionBoundary(login: false)).
//   • Emits a `SignOutRequested` event so the shell can:
//        - fade out the main window,
//        - dispose any per-session VMs,
//        - restart the splash + login flow OR close the app.
//   • Never throws — every step is try/catch. The user must always
//     be able to leave the terminal.
//
// The shell (MainViewModel + App.xaml.cs) subscribes to
// `SignOutRequested` and decides the UX (restart splash vs. exit).
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.AI.AIConfig;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Views.Login;
using System;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Security
{
    /// <summary>
    /// Centralised sign-out + clear-saved-credentials helper. Static
    /// so every entry point (menu, command bar, agent tool, etc.)
    /// funnels through the same audit + cleanup sequence.
    /// </summary>
    public static class AuthSessionService
    {
        /// <summary>
        /// Raised after a successful sign-out. Shell subscribers use
        /// this to tear down per-session state and either restart the
        /// splash / login flow or exit the process.
        /// </summary>
        public static event EventHandler? SignOutRequested;

        /// <summary>
        /// Ends the current session:
        ///   • clears the "skip login" DPAPI blob,
        ///   • clears the skip-login flags on AppConfig,
        ///   • writes an audit boundary,
        ///   • raises <see cref="SignOutRequested"/>.
        /// Never throws.
        /// </summary>
        public static Task SignOutAsync()
        {
            ClearSavedCredentialsCore(auditReason: "User signed out");

            try { AuditService.LogSessionBoundary(login: false); }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "SignOutAsync boundary-audit failed: " + ex.Message,
                    new LogContext
                    {
                        Module = "Auth",
                        Class = nameof(AuthSessionService),
                        Operation = "SignOutAsync"
                    });
            }

            try { SignOutRequested?.Invoke(null, EventArgs.Empty); }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "SignOutRequested subscriber threw: " + ex.Message,
                    new LogContext
                    {
                        Module = "Auth",
                        Class = nameof(AuthSessionService),
                        Operation = "SignOutAsync"
                    });
            }

            return Task.CompletedTask;
        }

        /// <summary>
        /// Revokes the "Skip login until update" auto-sign-in on this
        /// device WITHOUT ending the current session. Bound to the
        /// "Forget this device" affordance in Settings. Never throws.
        /// </summary>
        public static void ClearSavedCredentials()
            => ClearSavedCredentialsCore(auditReason: "Skip-login credentials cleared");

        private static void ClearSavedCredentialsCore(string auditReason)
        {
            try
            {
                SecretVault.Remove(LoginPanel.SkipLoginSecretKey);
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "ClearSavedCredentials vault removal failed: " + ex.Message,
                    new LogContext
                    {
                        Module = "Auth",
                        Class = nameof(AuthSessionService),
                        Operation = nameof(ClearSavedCredentials)
                    });
            }

            try
            {
                if (App.Config != null)
                {
                    bool wasArmed = App.Config.SkipLoginUntilUpdate;
                    App.Config.SkipLoginUntilUpdate = false;
                    App.Config.SkipLoginBoundVersion = "";
                    ConfigService.Save(App.Config);

                    if (wasArmed)
                    {
                        try
                        {
                            AuditService.Log(
                                AuditSections.Settings,
                                auditReason,
                                "auto-sign-in disarmed; vault entry removed");
                        }
                        catch (System.Exception ex) { ProGlassAutomation.Services.Logging.AppLogService.Warning("[AuthSessionService] ClearSavedCredentialsCore: audit must not block security cleanup: " + ex.Message); }
                    }
                }
            }
            catch (Exception ex)
            {
                AppLogService.Warning(
                    "ClearSavedCredentials config-write failed: " + ex.Message,
                    new LogContext
                    {
                        Module = "Auth",
                        Class = nameof(AuthSessionService),
                        Operation = nameof(ClearSavedCredentials)
                    });
            }
        }
    }
}
