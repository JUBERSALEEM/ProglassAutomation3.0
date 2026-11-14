// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Optimization/OptimizationViewModel.CncExport.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — machine-file export (PHASE 4)
//
// WHY THIS FILE
//   The REPORT tab's CNC pills were decoration: four format names with no
//   command behind them. This wires them to the exporters in
//   Services/Reporting/CncExport and, just as importantly, refuses to
//   pretend. A format that is not implemented tells the operator what would
//   finish it rather than handing them a file that looks right and feeds a
//   machine wrong numbers.
//
// ONE FILE PER CUTTING GROUP
//   Glass does not mix on a plate, so each cutting group is its own plan and
//   gets its own file. The save dialog is shown once; the remaining groups
//   land beside the first file with the group name appended.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Win32;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Reporting.CncExport;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        private ICommand? _exportCncCommand;

        /// <summary>
        /// Exports the finished plan in the format named by the command
        /// parameter (a <see cref="CncExportFormat"/> id).
        /// </summary>
        public ICommand ExportCncCommand =>
            _exportCncCommand ??= new RelayCommand(p => ExportCnc(p as string));

        private void ExportCnc(string? formatId)
        {
            var format = CncExportFormats.ById(formatId);
            if (format == null)
            {
                SessionStatus = "Unknown export format.";
                return;
            }

            // The honest branch. Three of the five formats are proprietary and
            // undocumented; writing one from guesswork is worse than not
            // offering it, so the note IS the feature until a sample file
            // arrives from that machine.
            if (!format.IsReady)
            {
                SessionStatus = format.DisplayName + " — " + format.Note;
                return;
            }

            if (!HasResult || _groupRuns.Count == 0)
            {
                SessionStatus = format.DisplayName
                    + ": run the optimization first — there is no cut plan to export yet.";
                return;
            }

            var targets = _groupRuns
                .Where(g => g.Result != null && g.Result.Sheets.Count > 0)
                .ToList();

            if (targets.Count == 0)
            {
                SessionStatus = format.DisplayName + ": the last run produced no sheets.";
                return;
            }

            string stamp = DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture);
            string? firstPath = null;
            int written = 0;
            int cutsWritten = 0;

            foreach (var group in targets)
            {
                var plan = new CncExportPlan
                {
                    Result = group.Result,
                    Source = string.IsNullOrWhiteSpace(group.Composition)
                        ? "cutting group"
                        : group.Composition,
                    // Resolved from the group's own glass, not from the UI
                    // selection: on a laminated group the run refused trims, so
                    // the file must say so rather than claim the level the
                    // operator picked. Placed AND unplaced pieces count — the
                    // group is one composition either way.
                    CutPattern = CutPolicy.ResolveFor(CurrentCutComplexity,
                        GuillotinePacker.GroupIsLaminated(GroupPieces(group)), CurrentMinTrimWidthMm)
                        .Describe(),
                    KerfMm = group.Margins?.KerfMm ?? 0
                };

                // A sheet that was stamped rather than split can arrive here
                // without a sequence; reconstruct it so the export never ships
                // a plan with no cuts in it.
                int missing = 0;
                foreach (var sheet in group.Result.Sheets)
                {
                    if (sheet.Cuts.Count > 0 || sheet.Pieces.Count == 0) continue;
                    if (GuillotinePacker.TryDeriveGuillotineCuts(sheet)) missing++;
                }

                string text;
                try
                {
                    text = format.Writer!(plan);
                }
                catch (Exception ex)
                {
                    SessionStatus = format.DisplayName + ": export failed — " + ex.Message;
                    return;
                }

                string path;
                if (firstPath == null)
                {
                    var dialog = new SaveFileDialog
                    {
                        Title = "Export " + format.DisplayName,
                        Filter = format.DisplayName + " (*" + format.Extension + ")|*" + format.Extension,
                        DefaultExt = format.Extension,
                        AddExtension = true,
                        FileName = SuggestedFileName(format, group, stamp, targets.Count > 1)
                    };
                    if (dialog.ShowDialog() != true)
                    {
                        SessionStatus = format.DisplayName + ": export cancelled.";
                        return;
                    }
                    firstPath = dialog.FileName;
                    path = firstPath;
                }
                else
                {
                    path = SiblingPath(firstPath, group);
                }

                File.WriteAllText(path, text, new UTF8Encoding(false));
                written++;
                cutsWritten += group.Result.Sheets.Sum(s => s.Cuts.Count);

                if (missing > 0)
                    SessionStatus = format.DisplayName + ": reconstructed the cut sequence for "
                        + missing.ToString(CultureInfo.InvariantCulture) + " sheet(s) that were stamped, not split.";
            }

            if (written == 0) return;

            SessionStatus = string.Format(CultureInfo.InvariantCulture,
                "{0}: wrote {1} file(s), {2} cut(s). {3}",
                format.DisplayName, written, cutsWritten,
                written > 1 ? "One file per cutting group." : string.Empty);
        }

        /// <summary>Every piece in this cutting group: what was placed plus
        /// what was left out. Used only to decide whether the group is
        /// laminated, which decides whether trims were allowed.</summary>
        private static List<CutPiece> GroupPieces(GroupRun group)
        {
            var all = new List<CutPiece>();
            foreach (var sheet in group.Result.Sheets)
                for (int i = 0; i < sheet.Pieces.Count; i++)
                    all.Add(sheet.Pieces[i].Piece);
            for (int i = 0; i < group.Result.Unplaced.Count; i++)
                all.Add(group.Result.Unplaced[i]);
            return all;
        }

        private static string SuggestedFileName(CncExportFormat format, GroupRun group,
            string stamp, bool multiple)
        {
            string stem = "cutplan-" + stamp;
            if (multiple)
                stem += "-" + SafeFileToken(string.IsNullOrWhiteSpace(group.Composition)
                    ? "group"
                    : group.Composition);
            return stem + format.Extension;
        }

        /// <summary>Puts a sibling file next to <paramref name="firstPath"/>,
        /// named for its cutting group, so one dialog can serve several
        /// groups without asking the same question twice.</summary>
        private static string SiblingPath(string firstPath, GroupRun group)
        {
            string dir = Path.GetDirectoryName(firstPath) ?? string.Empty;
            string stem = Path.GetFileNameWithoutExtension(firstPath);
            string ext = Path.GetExtension(firstPath);
            string name = stem + "-" + SafeFileToken(string.IsNullOrWhiteSpace(group.Composition)
                ? "group"
                : group.Composition) + ext;
            return Path.Combine(dir, name);
        }

        private static string SafeFileToken(string value)
        {
            var sb = new StringBuilder(value.Length);
            foreach (char c in value)
            {
                if (char.IsLetterOrDigit(c) || c == '-' || c == '_') sb.Append(c);
                else if (c == ' ' || c == '.') sb.Append('-');
            }
            string token = sb.ToString().Trim('-');
            return token.Length == 0 ? "group" : token.Substring(0, Math.Min(token.Length, 40));
        }
    }
}
