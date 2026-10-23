// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Database/AppDataRoot.cs
// MODULE: Core — single folder every ERP SQLite file lives under.
//
// V3 scope: the rebuilt app opens the same product data folder the
// legacy installs used (ProGlassAutomation under %APPDATA%), so an
// existing inquiries.db is inherited and converged by
// SchemaMigrationRunner. The shared LAN/Wi-Fi data-root feature of the
// legacy build is intentionally NOT ported — V3 starts single-station
// and the root can be revisited as its own module.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.IO;

namespace ProGlassAutomation.Services.Database
{
    /// <summary>
    /// Single folder every ERP SQLite file lives under.
    /// </summary>
    public static class AppDataRoot
    {
        public const string ProductionName = "ProGlassAutomation";

        /// <summary>
        /// Test hook: when set, <see cref="CurrentFolder"/> resolves here
        /// instead of the real AppData location. Unit tests only.
        /// </summary>
        public static string? TestFolderOverride { get; set; }

        public static string CurrentFolder
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(TestFolderOverride))
                    return TestFolderOverride;
                return ProductionFolder;
            }
        }

        public static string ProductionFolder => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            ProductionName);

        /// <summary>Creates the current data folder if it is missing.</summary>
        public static void EnsureCurrentFolder()
        {
            Directory.CreateDirectory(CurrentFolder);
        }
    }
}
