using ProGlassAutomation.Services.AI.AIConfig;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Concurrent;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Single folder every ERP SQLite file lives under.
    /// Supports a professional LAN/Wi-Fi shared data root so all company
    /// workstations can point at the same dataset from Settings.
    ///
    /// SENIOR AUDIT (2026-09-24) — shared LAN/Wi-Fi data root:
    /// The old ConfigureFrom() swallowed every exception from a bare
    /// Directory.CreateDirectory and silently reset the shared root to "",
    /// so a workstation that started while its WiFi/LAN share was
    /// unreachable (file server off, WiFi not up yet, SMB name down)
    /// opened its LOCAL AppData database while the rest of the company
    /// kept writing to the share — two diverging data sets, no warning
    /// anywhere. A downed share must not brick the app, so the fallback
    /// to local stays; but it is now EXPLICIT STATE: SharedRootConfigured
    /// + SharedRootFallbackReason + SharedRootFallbackActive, surfaced as
    /// a red banner in Settings → Data, and every database folder access
    /// goes through EnsureCurrentFolder(), which turns a missing share
    /// into an operator-readable SharedDataRootUnavailableException
    /// instead of a raw IOException deep inside a module.
    /// </summary>
    public static class AppDataRoot
    {
        public const string ProductionName = "ProGlassAutomation";
        public const string DemoName = "ProGlassAutomation.Demo";

        private static string _sharedDataRoot = "";
        private static bool _sharedFolderVerified = false;

        public static bool IsDemo { get; private set; }

        /// <summary>Live shared root actually in use right now ("" = local).</summary>
        public static bool IsSharedDataRootEnabled =>
            !string.IsNullOrWhiteSpace(_sharedDataRoot) && !IsDemo;

        public static string SharedDataRoot => _sharedDataRoot;

        /// <summary>
        /// The persisted settings asked for a shared root, whether or not
        /// it was reachable at startup. Lets the UI distinguish "shared is
        /// configured but unreachable" (must show a warning) from "local
        /// mode was chosen on purpose".
        /// </summary>
        public static bool SharedRootConfigured { get; private set; }

        /// <summary>
        /// Human-readable reason this workstation is on LOCAL data even
        /// though the company is on a shared root. Empty when there is no
        /// problem. Written once at startup by ConfigureFrom; never blanked
        /// again in the same process (the state can only be fixed by a
        /// restart with the share reachable).
        /// </summary>
        public static string SharedRootFallbackReason { get; private set; } = "";

        /// <summary>True when a configured shared root is NOT the live root.</summary>
        public static bool SharedRootFallbackActive =>
            SharedRootConfigured && !IsSharedDataRootEnabled && !IsDemo;

        public static string? TestFolderOverride { get; set; }

        public static string CurrentFolder
        {
            get
            {
                var overrideFolder = TestFolderOverride;
                if (!string.IsNullOrWhiteSpace(overrideFolder))
                    return overrideFolder;

                if (IsDemo)
                    return DemoFolder;

                if (!string.IsNullOrWhiteSpace(_sharedDataRoot))
                    return _sharedDataRoot;

                return ProductionFolder;
            }
        }

        public static string ProductionFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductionName);

        public static string DemoFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            DemoName);

        public static void ConfigureFrom(AppConfig? config)
        {
            // Reset every shared-root state, then re-derive it from config.
            _sharedDataRoot = "";
            _sharedFolderVerified = false;
            SharedRootConfigured = false;
            SharedRootFallbackReason = "";

            var data = config?.DataConnectivity;
            if (data?.UseSharedDataRoot != true) return;

            SharedRootConfigured = true;

            if (!TryNormalizeSharedRoot(data.SharedDataRootPath, out var normalized))
            {
                // Enabled in config but the path is empty/invalid — usually
                // a half-saved state from an older build. Loud, not silent.
                SharedRootFallbackReason =
                    "Shared company database mode is enabled but the saved folder path is " +
                    "empty or invalid, so this workstation is running on LOCAL data. Open " +
                    "Settings → Data, enter the shared folder, test it, and save again.";
                AppLogService.Error(
                    "Shared data root enabled in config but path is empty or invalid — " +
                    "this workstation runs on local AppData (SPLIT-BRAIN RISK).",
                    null,
                    new LogContext { Module = "Database", Operation = "ConfigureSharedRoot" });
                return;
            }

            if (TryEnsureSharedFolder(normalized, out var failure))
            {
                _sharedDataRoot = normalized;
                _sharedFolderVerified = true;
                AppLogService.Info(
                    $"Shared company database root enabled: {_sharedDataRoot}",
                    new LogContext { Module = "Database", Operation = "ConfigureSharedRoot" });
            }
            else
            {
                // The fallback to local stays (a downed share must not brick
                // the app), but it is now explicit state with a reason the
                // Settings page shows as a red banner — the operator can no
                // longer be silently writing to a different database than
                // the rest of the company.
                SharedRootFallbackReason =
                    "The shared company data folder " + normalized + " was unreachable at " +
                    "startup, so this workstation started on its LOCAL AppData database " +
                    "while the rest of the company is on the shared folder. Make sure the " +
                    "host computer is on, the folder is still shared on the WiFi/LAN, and " +
                    "this workstation is on the same network — then restart ProGlass. (" +
                    failure + ")";
                AppLogService.Error(
                    "Shared company database root unreachable at startup — fell back to " +
                    $"local AppData (SPLIT-BRAIN RISK). Path: {normalized}. {failure}",
                    null,
                    new LogContext { Module = "Database", Operation = "ConfigureSharedRoot" });
            }
        }

        /// <summary>
        /// Ensures the current data folder exists, with shared-root
        /// awareness. Every DbContext.OnConfiguring calls this instead of a
        /// bare Directory.CreateDirectory so that (1) a missing/unreachable
        /// LAN share produces a clear, actionable error instead of a raw
        /// IOException from deep inside a module, and (2) a share that is
        /// already verified reachable is not re-probed with an SMB
        /// round-trip on every context creation.
        /// </summary>
        public static void EnsureCurrentFolder()
        {
            string folder = CurrentFolder;

            if (IsSharedDataRootEnabled)
            {
                if (_sharedFolderVerified) return;
                try
                {
                    Directory.CreateDirectory(folder);
                    _sharedFolderVerified = true;
                }
                catch (Exception ex)
                {
                    _sharedFolderVerified = false;
                    throw new SharedDataRootUnavailableException(folder, ex);
                }
                return;
            }

            Directory.CreateDirectory(folder);
        }

        /// <summary>
        /// True when the folder exists and is reachable. Does NOT create —
        /// used by the startup probe where a share that is configured but
        /// not yet created by its host should count as "not there yet".
        /// </summary>
        internal static bool TryEnsureSharedFolder(string folder, out string failure)
        {
            // Directory.CreateDirectory on a dead UNC path blocks inside the
            // SMB redirector. That call used to run on the UI thread at
            // launch, so a host that was off froze this process and could
            // stall Explorer with it. Four seconds, then local data.
            try
            {
                var probe = Task.Run(() => Directory.CreateDirectory(folder));
                probe.ContinueWith(
                    t => { _ = t.Exception; },
                    TaskContinuationOptions.OnlyOnFaulted);
                if (!probe.Wait(TimeSpan.FromSeconds(4)))
                {
                    failure = "The shared folder did not respond within 4 seconds — the host " +
                        "computer may be off or the network is down.";
                    return false;
                }

                failure = "";
                return true;
            }
            catch (AggregateException ex)
            {
                failure = DescribeShareFailure(ex.GetBaseException());
                return false;
            }
            catch (Exception ex)
            {
                failure = DescribeShareFailure(ex);
                return false;
            }
        }

        /// <summary>
        /// Maps a raw .NET share exception onto a sentence an operator can
        /// act on. Shared by the startup probe, the Settings probe button
        /// and EnsureCurrentFolder so every surface says the same thing.
        /// </summary>
        internal static string DescribeShareFailure(Exception ex)
        {
            if (ex is UnauthorizedAccessException)
                return "Access was denied to the folder — check that the share's security " +
                    "permissions allow this Windows user to read AND write.";

            if (ex is IOException)
            {
                string msg = ex.Message ?? "";
                if (msg.Contains("network path", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("not found", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("cannot find", StringComparison.OrdinalIgnoreCase))
                    return "The network path could not be found — check the computer name or " +
                        "IP, that the share exists, and that this workstation is on the same WiFi/LAN.";
                return "The shared folder did not respond — the host computer may be off or " +
                    "the network is down.";
            }

            return ex.Message ?? ex.GetType().Name;
        }

        // ── Mapped-drive detection (WNetGetConnection) ────────────────────
        //
        // A company that maps the share as Z:\ on every workstation is just
        // as "shared" as one that types the UNC path. The old
        // LooksNetworkBased only recognised "\\\\" prefixes, so a mapped
        // drive was reported as a local path. WNetGetConnection asks the
        // Windows redirector directly; results are cached per drive letter.

        private static readonly ConcurrentDictionary<string, string> _mappedDriveUcnc =
            new(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Returns the UNC path a local drive letter points at ("Z:" →
        /// "\\SERVER\share"), or "" for a real local disk.
        /// </summary>
        public static string GetMappedNetworkPath(string path)
        {
            string root = GetDriveRoot(path);
            if (root.Length < 3) return "";

            if (_mappedDriveUcnc.TryGetValue(root, out var cached))
                return cached;

            string unc = "";
            try
            {
                var sb = new StringBuilder(261);
                if (WNetGetConnectionW(root, sb) == 0)
                    unc = sb.ToString();
            }
            catch
            {
                // DllImport unavailable (non-Windows host, tests) — treat as local.
            }

            _mappedDriveUcnc[root] = unc;
            return unc;
        }

        private static string GetDriveRoot(string path)
        {
            if (string.IsNullOrWhiteSpace(path)) return "";
            var trimmed = path.Trim();
            if (trimmed.Length >= 2 && trimmed[1] == ':')
            {
                char c = trimmed[0];
                if ((c >= 'A' && c <= 'Z') || (c >= 'a' && c <= 'z'))
                    return trimmed.Substring(0, 2) + @"\";
            }
            return "";
        }

        [DllImport("wtsapi32.dll", CharSet = CharSet.Unicode, BestFitMapping = false, ExactSpelling = true)]
        private static extern int WNetGetConnectionW(string localName, StringBuilder remoteName);

        public static bool TryNormalizeSharedRoot(string? path, out string normalized)
        {
            normalized = "";
            if (string.IsNullOrWhiteSpace(path)) return false;

            try
            {
                var full = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim().Trim('\"')));
                var local = Path.GetFullPath(ProductionFolder);
                var demo = Path.GetFullPath(DemoFolder);

                if (full.Equals(local, StringComparison.OrdinalIgnoreCase)
                    || full.Equals(demo, StringComparison.OrdinalIgnoreCase))
                    return false;

                normalized = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void EnterDemo()
        {
            IsDemo = true;
            _sharedFolderVerified = false;
            Directory.CreateDirectory(CurrentFolder);
        }

        public static void ExitDemo()
        {
            IsDemo = false;
            _sharedFolderVerified = false;
        }
    }

    /// <summary>
    /// Thrown when a database operation needs the shared company data folder
    /// and the share is not reachable right now. The message is written for
    /// the operator, not the developer — it surfaces verbatim in UI error
    /// toasts, so it must stay plain language.
    /// </summary>
    public sealed class SharedDataRootUnavailableException : Exception
    {
        public string SharedFolder { get; }

        public SharedDataRootUnavailableException(string folder, Exception inner)
            : base(
                "Cannot reach the shared company data folder: " + folder + "\r\n\r\n" +
                "Check that (1) the computer hosting the share is on, (2) the folder is " +
                "still shared on the WiFi/LAN, and (3) this workstation is connected to " +
                "the same network.\r\n\r\n" +
                "Technical detail: " + inner.Message,
                inner)
        {
            SharedFolder = folder;
        }
    }
}
