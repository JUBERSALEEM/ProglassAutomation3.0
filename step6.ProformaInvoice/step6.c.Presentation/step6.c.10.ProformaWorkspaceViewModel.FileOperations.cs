using System;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Proforma;
using ProGlassAutomation.ThemeStudio;

namespace ProGlassAutomation.ViewModels.Proforma
{
    public sealed partial class ProformaWorkspaceViewModel
    {
        private bool _includeProfitLossInWorkbook;

        public bool IncludeProfitLossInWorkbook
        {
            get => _includeProfitLossInWorkbook;
            set => SetField(ref _includeProfitLossInWorkbook, value);
        }

        public ICommand ImportCutListCommand { get; } =
            new DelegateCommand(() => _ = ImportCutListAsync());

        public ICommand ExportWorkbookCommand { get; } =
            new DelegateCommand(() => _ = ExportWorkbookAsync());

        public ICommand ExportCsvCommand { get; } =
            new DelegateCommand(() => _ = ExportCsvAsync());

        public ICommand ExportRegisterWorkbookCommand { get; } =
            new DelegateCommand(() => _ = ExportRegisterWorkbookAsync());

        private async Task ImportCutListAsync()
        {
            if (!CanEditBody)
            {
                Report("Only a draft invoice can accept imported cut-list rows.");
                return;
            }
            if (SelectedGroup == null)
            {
                Report("Select the specification group that these cut-list rows belong to.");
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Import cut list into " + SelectedGroup.DisplayText,
                Filter = ProformaGridImportService.FileDialogFilter,
                CheckFileExists = true,
                Multiselect = false
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                ProformaGridImportResult result =
                    await ProformaGridImportService.ImportFileAsync(dialog.FileName);
                if (result.Rows.Count == 0)
                {
                    Report(result.Warning.Length == 0 ? "No valid cut-list rows were found." : result.Warning);
                    return;
                }

                SpecGroup? target = Draft.Groups.FirstOrDefault(
                    group => group.GroupNumber == SelectedGroup.GroupNumber);
                if (target == null)
                {
                    Report("The selected specification group is no longer available.");
                    return;
                }

                string[] importedSpecs = result.Rows.Select(row => row.SpecName.Trim())
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (importedSpecs.Length > 1
                    || importedSpecs.Length == 1 && target.SpecName.Length > 0
                    && !string.Equals(importedSpecs[0], target.SpecName, StringComparison.OrdinalIgnoreCase))
                {
                    Report("This file contains a different or multiple specification names. Import one specification group at a time.");
                    return;
                }

                string[] importedTypes = result.Rows.Select(row => row.GlassType)
                    .Where(value => value.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
                if (importedTypes.Length > 1
                    || importedTypes.Length == 1
                    && !string.Equals(importedTypes[0], target.GlassType, StringComparison.OrdinalIgnoreCase))
                {
                    Report("The imported glass type does not match the selected specification group.");
                    return;
                }

                if (target.SpecName.Length == 0 && importedSpecs.Length == 1)
                {
                    target.SpecName = importedSpecs[0];
                    target.HasManualSpecOverride = true;
                }

                foreach (ProformaGridImportRow imported in result.Rows)
                {
                    target.Rows.Add(new SpecRow
                    {
                        SerialNumber = target.Rows.Count + 1,
                        GlassRef = imported.GlassRef,
                        W1 = imported.W1,
                        H1 = imported.H1,
                        W2 = imported.W2,
                        H2 = imported.H2,
                        Qty = imported.Qty
                    });
                }

                target.Renumber();
                Reapply();
                SyncFromDraft();
                ClearError();
                SetStatus("Imported " + result.Rows.Count + " cut-list row(s) into "
                    + target.DisplayText + "."
                    + (result.Skipped > 0 ? " " + result.Skipped + " invalid row(s) were skipped." : ""));
            }
            catch (Exception error)
            {
                Report("The cut list could not be imported: " + error.GetBaseException().Message);
            }
        }

        private async Task ExportWorkbookAsync()
        {
            if (!Draft.IsPersisted)
            {
                Report("Save the invoice first so the workbook carries its permanent number.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Proforma Invoice Workbook",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                FileName = ProformaWorkbookExporter.SuggestedInvoiceFileName(Draft),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                await ProformaWorkbookExporter.ExportInvoiceAsync(
                    Draft, dialog.FileName, IncludeProfitLossInWorkbook);
                SetStatus(IncludeProfitLossInWorkbook
                    ? "Workbook exported with an INTERNAL P&L sheet. Do not send it to the customer."
                    : "Invoice workbook exported without internal cost and margin.");
                ClearError();
                if (!OpenExport(dialog.FileName))
                    SetStatus("Workbook saved, but the default viewer could not be opened.");
            }
            catch (Exception error)
            {
                Report("The Excel workbook could not be exported: " + error.GetBaseException().Message);
            }
        }

        private async Task ExportCsvAsync()
        {
            if (!Draft.IsPersisted)
            {
                Report("Save the invoice first so the CSV carries its permanent number.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Proforma Cut List",
                Filter = "CSV file (*.csv)|*.csv",
                DefaultExt = ".csv",
                AddExtension = true,
                FileName = ProformaCsvExporter.SuggestedFileName(Draft),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                await ProformaCsvExporter.ExportAsync(Draft, dialog.FileName);
                SetStatus("Cut-list CSV exported.");
                ClearError();
                if (!OpenExport(dialog.FileName))
                    SetStatus("CSV saved, but the default viewer could not be opened.");
            }
            catch (Exception error)
            {
                Report("The CSV could not be exported: " + error.GetBaseException().Message);
            }
        }

        private async Task ExportRegisterWorkbookAsync()
        {
            if (Rows.Count == 0)
            {
                Report("There are no invoices in the current register filter to export.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Filtered Proforma Register",
                Filter = "Excel workbook (*.xlsx)|*.xlsx",
                DefaultExt = ".xlsx",
                AddExtension = true,
                FileName = ProformaWorkbookExporter.SuggestedRegisterFileName(),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                await ProformaWorkbookExporter.ExportRegisterAsync(Rows.ToList(), dialog.FileName);
                SetStatus("Exported " + Rows.Count + " invoice(s) from the current register filter.");
                ClearError();
                if (!OpenExport(dialog.FileName))
                    SetStatus("Register workbook saved, but the default viewer could not be opened.");
            }
            catch (Exception error)
            {
                Report("The invoice register could not be exported: " + error.GetBaseException().Message);
            }
        }

        private static bool OpenExport(string path)
        {
            try
            {
                Process.Start(new ProcessStartInfo { FileName = path, UseShellExecute = true });
                return true;
            }
            catch
            {
                return false;
            }
        }
    }
}