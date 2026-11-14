using ProGlassAutomation.Commands;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.ProformaInvoice.Step5Grid;
using ProGlassAutomation.Services.Sharing;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Services.Localization;

using ProGlassAutomation.Services.Dialogs;
namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid
{
    public partial class DataGridViewModel
    {
        public ICommand ImportCutListCommand { get; private set; }
        public ICommand ExportExcelCommand { get; private set; }
        public ICommand ExportCsvCommand { get; private set; }
        public ICommand ExportPdfCommand { get; private set; }
        public ICommand ShareCutListCommand { get; private set; }

        private string _ioStatus = "";
        public string IoStatus
        {
            get => _ioStatus;
            private set
            {
                if (_ioStatus == value) return;
                _ioStatus = value ?? "";
                OnPropertyChanged();
            }
        }

        private void WireImportExportCommands()
        {
            ImportCutListCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(ImportCutListAsync, "PI Step5"));
            ExportExcelCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(() =>
            {
                var (id, customer, project) = CurrentDocumentParts();
                return CutListGridIo.ExportExcelAsync(SpecGroups, id, customer, project);
            }, "PI Step5"));
            ExportCsvCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(() =>
            {
                var (id, customer, project) = CurrentDocumentParts();
                return CutListGridIo.ExportCsvAsync(SpecGroups, id, customer, project);
            }, "PI Step5"));
            ExportPdfCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(() =>
            {
                var (id, customer, project) = CurrentDocumentParts();
                return CutListGridIo.ExportPdfAsync(SpecGroups, id, customer, project);
            }, "PI Step5"));
            ShareCutListCommand = new RelayCommand(_ => AsyncUtility.SafeFireAndForget(
                ShareAsync, "PI Step5 share"));
        }

        // Sharing Center — the SAME CutListGridIo exporter staged once
        // into the share temp folder (Report rule's default format),
        // then the composer. The cut list is a working analysis, so it
        // shares under the Report rule/template — never the PI invoice
        // wording, which would misdescribe the attachment.
        private async Task ShareAsync()
        {
            if (SpecGroups == null || SpecGroups.Count == 0)
            {
                IoStatus = LocalizationService.Instance.T("Msg.NothingToExportTheCurrent2");
                return;
            }

            var (id, customer, project) = CurrentDocumentParts();
            var context = ShareContextFactory.FromListReport(
                "Report", $"Cut List · {id}", "ProformaInvoice", customer);
            context.DocumentNumber = id;

            await ShareLauncher.GenerateAndShareAsync(
                context,
                async folder =>
                {
                    try
                    {
                        ExportFormat format = ShareLauncher.RuleFormat("Report");
                        string safeId = string.Concat((id ?? "PI")
                            .Select(ch => Path.GetInvalidFileNameChars()
                                .Contains(ch) ? '_' : ch));
                        if (safeId.Length == 0) safeId = "PI";
                        string ext = ExportFileNamer.Extension(format);
                        string path = Path.Combine(folder,
                            $"CutList_{safeId}_{DateTime.Now:yyyy-MM-dd_HH-mm}.{ext}");

                        ExportResult result = format switch
                        {
                            ExportFormat.Excel => await CutListGridIo.ExportExcelAsync(
                                SpecGroups, id, customer, project, path),
                            ExportFormat.Csv => await CutListGridIo.ExportCsvAsync(
                                SpecGroups, id, customer, project, path),
                            _ => await CutListGridIo.ExportPdfAsync(
                                SpecGroups, id, customer, project, path)
                        };

                        if (!result.Success)
                        {
                            IoStatus = "Share failed.";
                            _dialogs.Error(
                                result.ErrorMessage ?? "Export failed.", "Share");
                        }
                        return result.SavedPath;
                    }
                    catch (Exception ex)
                    {
                        IoStatus = "Share failed.";
                        _dialogs.Error(
                            "Share failed.\n\n" + ExceptionMessages.Deep(ex), "Share");
                        return null;
                    }
                },
                _dialogs);
        }

        private static (string Id, string Customer, string Project) CurrentDocumentParts()
        {
            try
            {
                if (Application.Current?.MainWindow?.DataContext is
                    ProformaInvoice.ProformaInvoiceViewModel shell)
                {
                    return (
                        shell.Step1VM?.ProformaId ?? "PI",
                        shell.Step2VM?.CustomerName ?? "",
                        shell.Step3VM?.ProjectIdentifierName ?? "");
                }
            }
            catch (Exception ex)
            {
                ProGlassAutomation.Services.Logging.AppLogService.Debug(
                    $"[Step5] CurrentDocumentParts suppressed: {ex.Message}");
            }
            return ("PI", "", "");
        }

        private async Task ImportCutListAsync()
        {
            string? path = CutListGridIo.PromptOpenPath();
            if (string.IsNullOrWhiteSpace(path)) return;

            IoStatus = "Importing…";

            CutListImportResult parsed;
            try
            {
                // Parse off the UI thread: ClosedXML and large CSV/TXT reads
                // otherwise freeze the grid, which makes import look "stuck"
                // at "Importing…" even when it is still running.
                parsed = await Task.Run(() => CutListGridIo.ImportFileAsync(path))
                    .ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                IoStatus = "Import failed.";
                _dialogs.Show(
                    "Could not read the cut list.\n\n" + ExceptionMessages.Deep(ex) +
                    "\n\nMake sure the file is a valid Excel (.xlsx), CSV or TXT file.",
                    LocalizationService.Instance.T("Msg.CapImportCutList"), MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            if (parsed.Rows.Count == 0)
            {
                IoStatus = parsed.Warning ?? "No rows imported.";
                _dialogs.Show(IoStatus, LocalizationService.Instance.T("Msg.CapImportCutList"),
                    MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            int added = ApplyImportedRowsBySpec(parsed.Rows);
            string extra = parsed.Skipped > 0 ? $"  ·  skipped {parsed.Skipped}" : "";
            IoStatus = added > 0
                ? $"Imported {added} row(s){extra}."
                : parsed.Warning ?? "Nothing was imported.";
        }

        /// <summary>
        /// Splits an imported cut list by its "Spec Name" column (when
        /// present) so a cut list with two specs lands in the matching two
        /// spec groups instead of collapsing into one. Rows without a spec
        /// name fall back to the currently selected group.
        /// </summary>
        private int ApplyImportedRowsBySpec(
            IReadOnlyList<Services.ProformaInvoice.Step5Grid.CutListImportRow> rows)
        {
            if (rows == null || rows.Count == 0) return 0;

            int added = 0;

            var named = rows
                .Where(r => !string.IsNullOrWhiteSpace(r.SpecName))
                .GroupBy(r => r.SpecName.Trim(), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var g in named)
            {
                var target = SpecGroups.FirstOrDefault(sg =>
                    string.Equals(sg.SelectedSpecName, g.Key, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(sg.GroupName, g.Key, StringComparison.OrdinalIgnoreCase));

                if (target == null)
                    target = AddNewSpecGroup(g.Key);

                ApplyGlassType(target, g);
                added += target.ApplyImportedRows(g.ToList());
            }

            var unnamed = rows.Where(r => string.IsNullOrWhiteSpace(r.SpecName)).ToList();
            if (unnamed.Count > 0)
            {
                var group = SelectedSpecGroup;
                if (group == null)
                    group = AddNewSpecGroup();
                ApplyGlassType(group!, unnamed);
                added += group!.ApplyImportedRows(unnamed);
            }

            return added;
        }

        /// <summary>
        /// If any imported row carries a Glass Type, adopt it on the target
        /// group before the rows are applied — so perimeter factors (LAM = 4
        /// vs 2) and the GLASS TYPE dropdown both reflect the file. Values are
        /// normalised to the app's SGU / DGU / LAM / DGU+LAM set.
        /// </summary>
        private static void ApplyGlassType(
            SpecGroup group, IEnumerable<Services.ProformaInvoice.Step5Grid.CutListImportRow> rows)
        {
            var type = rows
                .Select(r => NormalizeGlassType(r.GlassType))
                .FirstOrDefault(t => t != null);

            if (type != null &&
                !string.Equals(group.SelectedGlassType, type, StringComparison.OrdinalIgnoreCase))
                group.SelectedGlassType = type;
        }

        private static string? NormalizeGlassType(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return null;
            string n = raw.Trim().ToUpperInvariant()
                .Replace(" ", "").Replace("_", "").Replace("-", "").Replace("+", "");
            return n switch
            {
                "SGU" or "SINGLE" => "SGU",
                "DGU" or "DOUBLE" => "DGU",
                "LAM" or "LAMINATED" => "LAM",
                "DGULAM" or "DGULAMINATED" or "LAMINATEDDGU" => "DGU+LAM",
                _ => null
            };
        }
    }
}
