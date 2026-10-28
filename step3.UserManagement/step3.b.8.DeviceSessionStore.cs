// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.8.DeviceSessionStore.cs
// MODULE: step3 — the sign-in this device remembers.
//
// Why this exists (owner report, round 32)
//   "The app asks for device login repeatedly — remember the last login until
//   Sign out." Until now the session lived in one static field, so it died with
//   the process: every launch of the same application on the same workstation
//   asked for the same password again. That is not a security feature, it is a
//   password prompt the operator learns to type without reading.
//
// What is stored, and what is NOT
//   The file holds the directory id, the username, the Windows account the
//   sign-in happened under, the password stamp at that moment and a timestamp.
//   It never holds a password, a hash, a salt or anything reversible: come back
//   and the row is re-read from the directory, and the session is only restored
//   when that row is still there, still active, still credentialed, still the
//   same person, and its password has not changed since. A reset password, a
//   deactivated account, a renamed row or another Windows account all fail the
//   check and land on the sign-in gate instead.
//
//   The password stamp (User.PasswordUpdatedAt) is the whole point of storing a
//   stamp: an administrator who resets a compromised password expects the
//   remembered session on that workstation to stop working, and it does.
//
// Where it lives
//   %LOCALAPPDATA%\ProGlassAutomation\device-session.json — LocalApplicationData,
//   which is per Windows account and per machine, so "remember me on this
//   device" means exactly that. The database lives in %APPDATA% and is not
//   touched by this class.
//
// Rules
//   • Nothing here throws. A read-only profile, a full disk or a half-written
//     file means "no remembered session", not a failed launch.
//   • An unreadable file is not silently overwritten: Load reports the problem
//     and the caller signs in again, which writes a clean file.
//   • Forget() is called on every sign-out, on deactivation, on a password
//     change and on any failed check — the record exists only while it is true.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Users;
using System;
using System.IO;
using System.Text.Json;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>One remembered sign-in, as stored on this device.</summary>
    public sealed class RememberedSession
    {
        /// <summary>Shape version. A file written by another build is discarded.</summary>
        public int Version { get; set; }

        /// <summary>The directory row id — the only identifier a session may carry.</summary>
        public int UserId { get; set; }

        /// <summary>The username as it was entered, for the sign-in card's prefill.</summary>
        public string Username { get; set; } = "";

        /// <summary>The Windows account this device remembered the sign-in under.</summary>
        public string WindowsAccount { get; set; } = "";

        /// <summary>The row's password stamp when the sign-in happened.</summary>
        public DateTime? PasswordUpdatedAtUtc { get; set; }

        public DateTime RememberedAtUtc { get; set; }
    }

    /// <summary>Reads, writes and clears the remembered sign-in.</summary>
    public static class DeviceSessionStore
    {
        public const int CurrentVersion = 1;

        /// <summary>A remembered session is a handful of fields; refuse anything else.</summary>
        public const int MaxBytes = 8 * 1024;

        private static readonly JsonSerializerOptions Options = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>Test hook: redirects the folder away from %LOCALAPPDATA%.</summary>
        public static string? FolderOverride { get; set; }

        public static string Folder => FolderOverride ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ProGlassAutomation");

        public static string FilePath => Path.Combine(Folder, "device-session.json");

        /// <summary>Why the last read or write did not do what was asked, or "".</summary>
        public static string LastProblem { get; private set; } = "";

        /// <summary>True when this device has a remembered sign-in on disk.</summary>
        public static bool Exists
        {
            get
            {
                try
                {
                    return File.Exists(FilePath);
                }
                catch (Exception error) when (error is IOException or UnauthorizedAccessException)
                {
                    return false;
                }
            }
        }

        /// <summary>The remembered session, or null when there is none or it is unusable.</summary>
        public static RememberedSession? Load()
        {
            LastProblem = "";

            try
            {
                if (!File.Exists(FilePath)) return null;

                long length = new FileInfo(FilePath).Length;
                if (length == 0 || length > MaxBytes)
                {
                    // Not a file this class wrote. Discard it rather than trusting it.
                    LastProblem = "The remembered sign-in file was not readable and has been cleared.";
                    Forget();
                    return null;
                }

                RememberedSession? session = JsonSerializer.Deserialize<RememberedSession>(
                    File.ReadAllText(FilePath), Options);

                if (session == null || session.Version != CurrentVersion || session.UserId <= 0)
                {
                    LastProblem = "The remembered sign-in file was not readable and has been cleared.";
                    Forget();
                    return null;
                }

                return session;
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                          or JsonException or NotSupportedException or ArgumentException)
            {
                LastProblem = "The remembered sign-in could not be read ("
                    + error.GetType().Name + "). Sign in once more.";
                return null;
            }
        }

        /// <summary>
        /// Records <paramref name="user"/> as this device's sign-in, under the
        /// Windows account given. Never throws; a failure is reported in
        /// <see cref="LastProblem"/> and only costs one extra password prompt.
        /// </summary>
        public static void Remember(User? user, string? windowsAccount)
        {
            LastProblem = "";
            if (user == null || user.Id <= 0) return;

            try
            {
                Directory.CreateDirectory(Folder);

                var session = new RememberedSession
                {
                    Version = CurrentVersion,
                    UserId = user.Id,
                    Username = user.Username,
                    WindowsAccount = (windowsAccount ?? "").Trim(),
                    PasswordUpdatedAtUtc = user.PasswordUpdatedAt,
                    RememberedAtUtc = DateTime.UtcNow
                };

                // Write beside the target, then move over it: a crash mid-write
                // must not leave half a session file that the next launch trusts.
                string temporary = FilePath + ".tmp";
                File.WriteAllText(temporary, JsonSerializer.Serialize(session, Options));
                File.Move(temporary, FilePath, overwrite: true);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException
                                          or JsonException or NotSupportedException or ArgumentException)
            {
                LastProblem = "This device could not remember the sign-in ("
                    + error.GetType().Name + "), so the password will be asked again next time.";
            }
        }

        /// <summary>
        /// Clears the remembered sign-in. Called on every sign-out and on every
        /// failed check: the record must never outlive the session it describes.
        /// </summary>
        public static void Forget()
        {
            try
            {
                if (File.Exists(FilePath)) File.Delete(FilePath);
                string temporary = FilePath + ".tmp";
                if (File.Exists(temporary)) File.Delete(temporary);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                LastProblem = "The remembered sign-in could not be cleared ("
                    + error.GetType().Name + ").";
            }
        }
    }
}
