// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/Providers/WhatsAppLauncher.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — WhatsApp channel detection + launch (Stage 3)
//
// WHY THIS EXISTS (Sept 2026 senior audit — "WhatsApp never opens")
// ─────────────────────────────────────────────────────────────────
//   The provider used to open a bare https://wa.me/<n> link. On DESKTOP
//   that is NOT a chat — it is a browser interstitial ("Continue to
//   Chat") that most operators never click through, so from their seat
//   WhatsApp simply "did not open" and all they saw was the Explorer
//   folder reveal. This launcher deep-links straight into whichever
//   WhatsApp the machine actually has, on the recipient's chat, with the
//   message prefilled.
//
// DETECTION — INSTALLED FIRST, STRICTLY (the operator's ask)
// ─────────────────────────────────────────────────────────────────
//   The FIRST audit only recognised the CLASSIC (Electron) install shape
//   (HKCR\whatsapp\shell\open\command). Today most machines run the
//   MICROSOFT STORE (UWP/AppX) WhatsApp Desktop — and WhatsApp Business
//   ships the same way — which registers the "whatsapp:" protocol through
//   its PACKAGE MANIFEST (delegated activation, no classic open-command)
//   and records itself under ...\ActivatableClasses\Package and AppX
//   keys. The old check saw none of that and always fell to web.
//
//   IsDesktopInstalled() now fires on ANY of these signals, so a real
//   install is always tried before the browser:
//     • whatsapp: URL-protocol key present (HKCU\Software\Classes\whatsapp
//       or HKCR\whatsapp) — classic OR store, with or without a classic
//       open-command (a DelegateExecute counts).
//     • A Store package whose family name contains "WhatsApp" is
//       registered (covers WhatsApp Desktop AND WhatsApp Business).
//     • The classic executable exists (%LOCALAPPDATA%\WhatsApp\WhatsApp.exe).
//
// CHANNEL ORDER
// ─────────────────────────────────────────────────────────────────
//   1. WhatsApp Desktop / Business — whatsapp://send?phone=<d>&text=<m>
//      Routed by the OS to the installed, logged-in packaged app (Store
//      or classic; regular or Business — they share the scheme).
//   2. WhatsApp Web — https://web.whatsapp.com/send?phone=<d>&text=<m>
//      Logged-in web chat, opened directly — NO wa.me interstitial.
//   3. wa.me — universal last resort (older systems / unusual browsers).
//
// HONESTY. Every channel is a HANDOFF: the message is prefilled but
// nothing is sent until the operator presses send, and WhatsApp links
// carry TEXT ONLY — the file is staged and revealed beside the chat for
// drag-in. The outcome is always ExternalAppOpened, never "Sent".
//
// Never throws: each launch attempt returns a Result; the launcher walks
// the channels and reports the first success (or a combined failure).
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Security;
using System;
using System.IO;
using System.Runtime.InteropServices;

namespace ProGlassAutomation.Services.Sharing.Providers
{
    /// <summary>Which WhatsApp channel a launch attempt used.</summary>
    public enum WhatsAppChannel
    {
        Desktop,
        Web,
        WaMe
    }

    /// <summary>Result of a WhatsApp launch: the channel and the outcome.</summary>
    public sealed class WhatsAppLaunchResult
    {
        public bool IsSuccess { get; }
        public WhatsAppChannel Channel { get; }
        public string Error { get; }

        private WhatsAppLaunchResult(bool ok, WhatsAppChannel channel, string error)
        {
            IsSuccess = ok;
            Channel = channel;
            Error = error ?? "";
        }

        public static WhatsAppLaunchResult Ok(WhatsAppChannel channel) =>
            new(true, channel, "");

        public static WhatsAppLaunchResult Fail(string error) =>
            new(false, WhatsAppChannel.WaMe, error ?? "");

        /// <summary>Human label for the channel that opened.</summary>
        public string ChannelName => Channel switch
        {
            WhatsAppChannel.Desktop => "WhatsApp Desktop",
            WhatsAppChannel.Web => "WhatsApp Web",
            _ => "WhatsApp"
        };
    }

    /// <summary>
    /// Detects the available WhatsApp channel and opens the recipient's
    /// chat with a prefilled message. Pure launch logic — no staging, no
    /// audit (the provider owns those).
    /// </summary>
    public static class WhatsAppLauncher
    {
        /// <summary>
        /// Opens WhatsApp on <paramref name="digits"/> (E.164 digits, no
        /// '+') with <paramref name="message"/> prefilled, trying Desktop →
        /// Web → wa.me. Returns the channel that succeeded. Never throws.
        /// </summary>
        public static WhatsAppLaunchResult OpenChat(string digits, string message)
        {
            if (string.IsNullOrWhiteSpace(digits))
                return WhatsAppLaunchResult.Fail("No phone number to open.");

            // Defense in depth: WhatsApp deep links require a clean E.164
            // digit string (no '+', no spaces, no trunk 0). A malformed
            // number is exactly what makes WhatsApp open then immediately
            // drop to the home screen ("contact not found"). Refuse it here
            // rather than launch a dead chat, whatever the caller passed.
            if (!Contacts.PhoneNumbers.IsValidNormalized(digits))
                return WhatsAppLaunchResult.Fail(
                    "The phone number is not a valid WhatsApp number.");

            string text = message ?? "";
            string encoded = Uri.EscapeDataString(text);
            string lastError = "";

            // 1. WhatsApp Desktop / Business — INSTALLED FIRST. Only when a
            // desktop client is actually detected (classic OR Store OR
            // Business) so we never fire a whatsapp:// link into a "no app"
            // dialog on a machine that only has the browser.
            if (IsDesktopInstalled())
            {
                string deepLink = "whatsapp://send?phone=" + digits
                    + "&text=" + encoded;
                Result desktop = ProcessStartGuard.TryOpenUrl(deepLink);
                if (desktop.IsSuccess)
                    return WhatsAppLaunchResult.Ok(WhatsAppChannel.Desktop);
                lastError = desktop.Error;
                AppLogService.Debug(
                    "[Sharing.WhatsApp] desktop deep link failed, falling back to web: "
                    + desktop.Error);
            }

            // 2. WhatsApp Web — opens the logged-in web chat directly
            // (no wa.me interstitial).
            string webUrl = "https://web.whatsapp.com/send?phone=" + digits
                + "&text=" + encoded;
            Result web = ProcessStartGuard.TryOpenUrl(webUrl);
            if (web.IsSuccess)
                return WhatsAppLaunchResult.Ok(WhatsAppChannel.Web);
            lastError = web.Error.Length > 0 ? web.Error : lastError;
            AppLogService.Debug(
                "[Sharing.WhatsApp] web link failed, falling back to wa.me: "
                + web.Error);

            // 3. wa.me — universal last resort.
            string waMe = "https://wa.me/" + digits + "?text=" + encoded;
            Result universal = ProcessStartGuard.TryOpenUrl(waMe);
            if (universal.IsSuccess)
                return WhatsAppLaunchResult.Ok(WhatsAppChannel.WaMe);

            return WhatsAppLaunchResult.Fail(
                universal.Error.Length > 0 ? universal.Error : lastError);
        }

        /// <summary>
        /// True when a WhatsApp desktop client (regular OR Business,
        /// Store/UWP OR classic) is installed on this machine. Uses
        /// several independent signals because different install kinds
        /// register very differently — a Store package does NOT create a
        /// classic shell\open\command, so requiring one (the first audit's
        /// mistake) hid every Store install. ANY signal is enough to try
        /// the app before the browser. Never throws; false on any
        /// non-Windows / access issue so the caller falls back to web.
        /// </summary>
        public static bool IsDesktopInstalled()
        {
            try
            {
                if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                    return false;

                if (ProtocolRegistered()) return true;      // classic OR store scheme
                if (StorePackageInstalled()) return true;   // UWP WhatsApp / Business
                if (ClassicExecutableExists()) return true; // Electron install

                return false;
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    "[Sharing.WhatsApp] desktop detection failed: " + ex.Message);
                return false;
            }
        }

        // ── Signal 1: the "whatsapp:" URL protocol is registered ─────────
        // Both the classic and Store clients register the scheme. We accept
        // the key when it declares itself a URL protocol AND resolves to
        // SOMETHING launchable — a classic shell\open\command, OR a
        // delegated/store activation (DelegateExecute / no command but a
        // present protocol key on a Store install). Requiring a classic
        // open-command was the original bug.
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool ProtocolRegistered()
        {
            return ProtocolKeyLaunchable(
                       Microsoft.Win32.Registry.CurrentUser,
                       @"Software\Classes\whatsapp")
                || ProtocolKeyLaunchable(
                       Microsoft.Win32.Registry.ClassesRoot,
                       "whatsapp");
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool ProtocolKeyLaunchable(
            Microsoft.Win32.RegistryKey baseKey, string subPath)
        {
            try
            {
                using var protocol = baseKey.OpenSubKey(subPath, writable: false);
                if (protocol == null) return false;

                // Must actually declare itself a URL protocol.
                if (protocol.GetValue("URL Protocol") == null) return false;

                using var command = protocol.OpenSubKey(
                    @"shell\open\command", writable: false);
                if (command != null)
                {
                    // Classic install: a default command string, OR the
                    // Store form that hands off via DelegateExecute.
                    string? cmd = command.GetValue(null) as string;
                    if (!string.IsNullOrWhiteSpace(cmd)) return true;
                    if (command.GetValue("DelegateExecute") != null) return true;
                }

                // A registered whatsapp: protocol key with no classic
                // command is the Store/UWP shape — the package manifest
                // owns activation. The scheme being present at all means
                // the OS can route whatsapp:// to the installed app.
                return true;
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[Sharing.WhatsApp] protocol probe '{subPath}' failed: {ex.Message}");
                return false;
            }
        }

        // ── Signal 2: a Store (AppX) package named WhatsApp is present ────
        // Covers WhatsApp Desktop AND WhatsApp Business (both ship as Store
        // packages whose family name contains "WhatsApp", e.g.
        // 5319275A.WhatsAppDesktop_cv1g1gvanyjgm). We scan the per-user
        // package family registrations under the AppX class roots.
        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool StorePackageInstalled()
        {
            // Registered package families are enumerated by the shell under
            // these hives. Matching any subkey that mentions "whatsapp" is
            // a reliable, permission-light signal (no PackageManager /
            // WinRT dependency needed).
            string[] roots =
            {
                @"Software\Classes\ActivatableClasses\Package",
                @"Software\Classes\Local Settings\Software\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages"
            };

            foreach (string root in roots)
            {
                if (SubkeyNameContains(
                        Microsoft.Win32.Registry.CurrentUser, root, "whatsapp"))
                    return true;
            }

            // Machine-wide Store repository (all-users installs).
            if (SubkeyNameContains(
                    Microsoft.Win32.Registry.LocalMachine,
                    @"SOFTWARE\Microsoft\Windows\CurrentVersion\AppModel\Repository\Packages",
                    "whatsapp"))
                return true;

            return false;
        }

        [System.Runtime.Versioning.SupportedOSPlatform("windows")]
        private static bool SubkeyNameContains(
            Microsoft.Win32.RegistryKey baseKey, string path, string needle)
        {
            try
            {
                using var key = baseKey.OpenSubKey(path, writable: false);
                if (key == null) return false;
                foreach (string name in key.GetSubKeyNames())
                {
                    if (name != null
                        && name.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
                        return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[Sharing.WhatsApp] package scan '{path}' failed: {ex.Message}");
                return false;
            }
        }

        // ── Signal 3: the classic Electron executable exists on disk ─────
        // %LOCALAPPDATA%\WhatsApp\WhatsApp.exe (non-Store install). Cheap
        // file probe, last of the three so it only runs when the registry
        // signals are silent.
        private static bool ClassicExecutableExists()
        {
            try
            {
                string local = Environment.GetFolderPath(
                    Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(local)) return false;
                string exe = Path.Combine(local, "WhatsApp", "WhatsApp.exe");
                return File.Exists(exe);
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    "[Sharing.WhatsApp] classic exe probe failed: " + ex.Message);
                return false;
            }
        }
    }
}
