// ═══════════════════════════════════════════════════════════════════════
// FILE: AuditService.Part2.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.Audit;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using ProGlassAutomation.Services.Database;

namespace ProGlassAutomation.Services.Audit
{
        public static partial class AuditService
    {
            public const int MaxFailedAttemptsBeforeLock = 5;

        /// <summary>Length of the lockout window after the threshold is hit.</summary>
        public const int LockoutMinutes = 5;

        /// <summary>
        /// Seconds remaining in the persisted lockout window for
        /// <paramref name="username"/> (0 = not locked). Used by the sign-in
        /// UI to show a countdown. Never throws.
        /// </summary>
        public static async Task<int> LockoutRemainingSecondsAsync(
            string username)
        {
            try
            {
                using var db = new AuditDbContext();
                var user = await db.Users
                    .FirstOrDefaultAsync(u => u.Username == username).ConfigureAwait(false);
                return user == null ? 0 : LockoutRemainingSeconds(user);
            }
            catch
            {
                // A failed check must never crash the sign-in flow.
                return 0;
            }
        }

        private static int LockoutRemainingSeconds(AppUser user)
        {
            // ── SIGN-IN LOCKOUT IS DISABLED ───────────────────────────────
            // This is the single chokepoint both sign-in paths read:
            // LoginPanelViewModel.TrySignInAsync calls it through
            // LockoutRemainingSecondsAsync to show a countdown, and
            // SwitchUserAsync calls it to reject the attempt. Returning 0
            // here turns the lockout off everywhere at once, so the two
            // cannot disagree.
            //
            // Why it was removed. The window was persisted to audit.db, so it
            // survived an app restart. An operator who forgot their password,
            // guessed five times, and restarted was still locked out — and
            // with no recovery path in the product, the only way back in was
            // to wait or edit the database by hand. That is exactly what
            // happened.
            //
            // The trade-off, stated plainly: this is a desktop ERP with no
            // network listener, so the lockout was protecting against someone
            // already sitting at the operator's machine. Unlimited attempts
            // makes an offline guessing attack cheaper. The password hashing
            // is unchanged and still does the real work — PBKDF2-HMAC-SHA256
            // at 210,000 iterations, so a stolen audit.db is still expensive
            // to crack. If this product ever gains remote or multi-user
            // network access, this decision must be revisited.
            //
            // MaxFailedAttemptsBeforeLock and LockoutMinutes are kept so
            // existing references and the Phase 13 tests still compile; they
            // are now inert. LockedUntilUtc is left on the row and simply
            // never read as a lock, so no migration is needed to clear a
            // stale window — an operator who is locked out right now is not
            // locked out after this build.
            _ = user;
            return 0;
        }

        /// <summary>Seeded first-run administrator account name.</summary>
        public const string DefaultAdminUsername = "admin";

        /// <summary>
        /// Seeded first-run password. Public so the UI can detect that it
        /// is still in force; it is a well-known default, not a secret.
        /// </summary>
        public const string DefaultAdminPassword = "admin";

        private static AppUser BuildUser(
            string username, string displayName,
            string role, string password)
        {
            string salt = GenerateSalt();

            return new AppUser
            {
                Username = username,
                DisplayName = displayName,
                Role = role,
                PasswordSalt = salt,
                PasswordHash = HashPassword(password, salt),
                IsActive = true
            };
        }

        // ══════════════════════════════════════════════════════════════
        //  AUDIT A-2 — PBKDF2 replaces single-round SHA-256
        //
        //  The old scheme was SHA256(saltHex + password). The salt was
        //  fine (16 random bytes, per user) but SHA-256 is fast by
        //  design, so audit.db — an ordinary file in %AppData% that is
        //  also copied into every backup set — was brute-forceable on a
        //  commodity GPU. Hashing now goes through
        //  Services/Security/PasswordHasher (PBKDF2-HMAC-SHA256,
        //  210,000 iterations, OWASP-recommended).
        //
        //  MIGRATION IS SILENT AND REQUIRES NO USER ACTION. Existing
        //  installations already hold SHA-256 hashes; rejecting them
        //  would lock every user out of their own ERP on upgrade.
        //  VerifyPassword accepts both formats and reports when an old
        //  one was used, and the caller re-hashes with PBKDF2 using the
        //  password just proven correct.
        // ══════════════════════════════════════════════════════════════

        private static string GenerateSalt()
        {
            // Retained for the AppUser.PasswordSalt column, which stays
            // populated for legacy rows. PBKDF2 carries its own salt
            // inside the stored value, so new rows do not depend on it.
            var bytes = new byte[16];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToHexString(bytes);
        }

        private static string HashPassword(string password, string saltHex)
            => Security.PasswordHasher.Hash(password ?? "");

        /// <summary>
        /// Verifies a password against either hashing scheme.
        /// <paramref name="needsUpgrade"/> is true when the stored hash
        /// was the legacy SHA-256 form and should be re-hashed.
        /// </summary>
        private static bool VerifyPassword(
            AppUser user, string password, out bool needsUpgrade)
        {
            var result = Security.PasswordHasher.Verify(
                password, user.PasswordHash, user.PasswordSalt);

            needsUpgrade = result.NeedsUpgrade;
            return result.Success;
        }

        private static bool VerifyPassword(AppUser user, string password)
            => VerifyPassword(user, password, out _);

        /// <summary>
        /// Re-hashes a verified password with the current scheme and
        /// persists it. Best-effort: a migration failure must never
        /// block a sign-in that has already succeeded.
        /// </summary>
        private static async Task UpgradePasswordHashAsync(
            AuditDbContext db, AppUser user, string password)
        {
            try
            {
                user.PasswordHash = Security.PasswordHasher.Hash(password);
                await SqliteResilience.SaveWithRetryAsync(_ => db.SaveChangesAsync());

                Logging.AppLogService.Info(
                    $"Password hash upgraded to PBKDF2 for '{user.Username}'.",
                    new Logging.LogContext
                    {
                        Module = "Security",
                        Class = nameof(AuditService),
                        Operation = "UpgradePasswordHash"
                    });
            }
            catch (Exception ex)
            {
                // The user is already authenticated; they simply keep the
                // legacy hash and we retry on their next sign-in.
                Logging.AppLogService.Warning(
                    $"Password hash upgrade failed for '{user.Username}': {ex.Message}",
                    new Logging.LogContext
                    {
                        Module = "Security",
                        Class = nameof(AuditService),
                        Operation = "UpgradePasswordHash"
                    });
            }
        }

        // ══════════════════════════════════════════════════════════════
        //  HELPERS
        // ══════════════════════════════════════════════════════════════

        private static string Truncate(string value, int maxLen)
        {
            if (string.IsNullOrEmpty(value)) return "";
            return value.Length <= maxLen ? value : value[..maxLen];
        }

        private static void RaiseOnUi(Action action)
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher == null) return;

            if (dispatcher.CheckAccess()) action();
            else dispatcher.InvokeAsync(action);
        }
    }
}
