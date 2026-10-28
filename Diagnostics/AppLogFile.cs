// ═══════════════════════════════════════════════════════════════════════
// FILE: Diagnostics/AppLogFile.cs
// MODULE: application foundation — where the diagnostic log lives, and how
//         large it is allowed to become.
//
// Placement (owner instruction, foundation audit): the product's established
// app-data folder, the SAME root every ERP database already lives under, so a
// backup of that folder carries the diagnostics with it —
//
//     %APPDATA%\ProGlassAutomation\Logs\proglass-errors.log
//
// NOT the source repository, the working directory, the folder the executable
// lives in, or a temporary folder: those are either read-only for a normal user,
// cleaned up by Windows, or would put the owner's diagnostics into Git. The
// folder is resolved through AppDataRoot, so an isolated test run redirects the
// log with AppDataRoot.TestFolderOverride and can never write to real data.
//
// Growth is bounded two ways, because an unbounded error log is its own incident:
//   • one record per failure, no routine entries (see AppLog);
//   • at most MaxFileBytes per file and MaxFiles files, oldest deleted first.
//     5 × 1 MB is the whole disk cost of this feature.
//
// The directory is created when the FIRST failure is written, never at startup:
// a run with no errors leaves nothing behind at all.
//
// Everything here is defensive. A logging failure must never reach the caller
// and must never hide the exception being logged — the failure is recorded in
// LastFailure instead, which the checks assert on.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Database;
using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace ProGlassAutomation.Diagnostics
{
    /// <summary>Bounded, rotating, failure-only diagnostic log file.</summary>
    public static class AppLogFile
    {
        /// <summary>The file name the owner (or support) is told to open.</summary>
        public const string FileName = "proglass-errors.log";

        /// <summary>Largest single file. A record is ~2 KB, so this is ~500 failures.</summary>
        public const int MaxFileBytes = 1024 * 1024;

        /// <summary>Files kept, including the current one. Oldest is deleted first.</summary>
        public const int MaxFiles = 5;

        /// <summary>
        /// Largest single record. A pathological stack trace could otherwise exceed
        /// the whole file budget in one entry, which would make "bounded" untrue.
        /// </summary>
        public const int MaxEntryBytes = 64 * 1024;

        private static readonly object Gate = new();

        private static readonly UTF8Encoding Utf8 = new(encoderShouldEmitUTF8Identifier: false);

        /// <summary>
        /// Recursion guard: a failure while writing must not start a second write.
        /// Thread-static so one thread's failure cannot silence another's entry.
        /// </summary>
        [ThreadStatic]
        private static bool _writing;

        /// <summary>
        /// The folder the log is written to. Derived from the product's app-data
        /// root, so it is the same root the databases use and the same root a test
        /// run redirects.
        /// </summary>
        public static string Folder => Path.Combine(AppDataRoot.CurrentFolder, "Logs");

        /// <summary>The file currently being appended to.</summary>
        public static string CurrentPath => Path.Combine(Folder, FileName);

        /// <summary>
        /// Why the last write did not happen, or "" when it did. Reading this is
        /// the only way a logging fault is ever visible, because writing must not
        /// throw into the code that was already failing.
        /// </summary>
        public static string LastFailure { get; private set; } = "";

        /// <summary>Records appended successfully. Used by the checks and support.</summary>
        public static int RecordsWritten { get; private set; }

        /// <summary>
        /// Appends one already-formatted entry, rotating first when the current
        /// file is full. Returns false (and sets <see cref="LastFailure"/>) instead
        /// of throwing.
        /// </summary>
        public static bool Append(string entry)
        {
            if (string.IsNullOrWhiteSpace(entry)) return false;
            if (_writing) return false;                    // a fault inside the fault path

            try
            {
                _writing = true;
                string text = entry.Replace("\r\n", "\n").TrimEnd();
                byte[] bytes = Utf8.GetBytes(text + Environment.NewLine);

                if (bytes.Length > MaxEntryBytes)
                {
                    // Keep the head of the record — timestamp, severity, operation and
                    // the first frames — and say plainly that the rest was dropped.
                    // The character budget is a quarter of the byte budget, so even
                    // four-byte characters cannot push the entry over the limit.
                    string marker = "\n... [record truncated: " + bytes.Length
                        + " bytes, limit " + MaxEntryBytes + "]\n";
                    int keepChars = Math.Max(0, (MaxEntryBytes / 4) - marker.Length);
                    if (keepChars > text.Length) keepChars = text.Length;
                    bytes = Utf8.GetBytes(text.Substring(0, keepChars) + marker);
                }

                lock (Gate)
                {
                    Directory.CreateDirectory(Folder);
                    RotateIfFull(bytes.Length);

                    using var stream = new FileStream(
                        CurrentPath, FileMode.Append, FileAccess.Write, FileShare.Read);
                    stream.Write(bytes, 0, bytes.Length);

                    // Straight to disk: a fatal path has no guarantee that the
                    // process survives long enough for a lazy flush.
                    stream.Flush(flushToDisk: true);
                }

                RecordsWritten++;
                LastFailure = "";
                return true;
            }
            catch (Exception error)
            {
                LastFailure = error.GetType().Name + ": " + error.Message;
                return false;
            }
            finally
            {
                _writing = false;
            }
        }

        /// <summary>
        /// Rotates <c>proglass-errors.log → .1 → .2 …</c> when the next entry would
        /// exceed <see cref="MaxFileBytes"/>. Internal so a check can exercise the
        /// policy without writing a megabyte.
        /// </summary>
        internal static void RotateIfFull(int incomingBytes)
        {
            if (!File.Exists(CurrentPath)) return;

            long length = new FileInfo(CurrentPath).Length;
            if (length + incomingBytes <= MaxFileBytes) return;

            // Delete the oldest, then shift every older file one slot down.
            string oldest = PathFor(MaxFiles - 1);
            if (File.Exists(oldest)) File.Delete(oldest);

            for (int index = MaxFiles - 2; index >= 1; index--)
            {
                string from = PathFor(index);
                if (!File.Exists(from)) continue;
                string to = PathFor(index + 1);
                if (File.Exists(to)) File.Delete(to);
                File.Move(from, to);
            }

            File.Move(CurrentPath, PathFor(1), overwrite: true);
        }

        /// <summary>0 = the current file; 1..MaxFiles-1 = the rotated ones.</summary>
        public static string PathFor(int index)
        {
            if (index <= 0) return CurrentPath;
            return Path.Combine(Folder,
                "proglass-errors." + index.ToString(CultureInfo.InvariantCulture) + ".log");
        }

        /// <summary>Every file this feature may have created, newest first.</summary>
        public static string[] ExistingFiles()
        {
            try
            {
                var found = new System.Collections.Generic.List<string>();
                for (int index = 0; index < MaxFiles; index++)
                {
                    string path = PathFor(index);
                    if (File.Exists(path)) found.Add(path);
                }
                return found.ToArray();
            }
            catch (Exception error)
            {
                LastFailure = error.GetType().Name + ": " + error.Message;
                return Array.Empty<string>();
            }
        }
    }
}
