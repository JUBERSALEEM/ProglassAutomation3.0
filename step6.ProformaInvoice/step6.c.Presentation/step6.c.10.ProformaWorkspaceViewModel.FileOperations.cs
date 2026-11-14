using System;
using System.Diagnostics;
using System.Collections.Generic;
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
                Report("Select a specification group before importing a cut list.");
                return;
            }

            var dialog = new OpenFileDialog
            {
                Title = "Import cut list into the selected specification groups",
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

                SpecGroup? selected = Draft.Groups.FirstOrDefault(
                    group => group.GroupNumber == SelectedGroup.GroupNumber);
                if (selected == null)
                {
                    Report("The selected specification group is no longer available.");
                    return;
                }

                var batches = result.Rows.GroupBy(row =>
                    row.SpecName.Trim().ToUpperInvariant() + "\u001F"
                    + row.GlassType.Trim().ToUpperInvariant()).ToList();
                var plan = new System.Collections.Generic.List<
                    (SpecGroup Target, System.Collections.Generic.List<ProformaGridImportRow> Rows,
                     string SpecName, string GlassType)>();

                foreach (var batch in batches)
                {
                    ProformaGridImportRow first = batch.First();
                    string specName = first.SpecName.Trim();
                    string glassType = first.GlassType.Trim();
                    List<SpecGroup> candidates = Draft.Groups.Where(group =>
                        (specName.Length == 0 || string.Equals(group.SpecName, specName, StringComparison.OrdinalIgnoreCase))
                        && (glassType.Length == 0 || string.Equals(group.GlassType, glassType, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    SpecGroup? target = null;
                    if (candidates.Count == 1)
                        target = candidates[0];
                    else if (candidates.Any(group => group.GroupNumber == selected.GroupNumber))
                        target = selected;
                    else if (candidates.Count == 0 && selected.SpecName.Length == 0
                        && (glassType.Length == 0 || string.Equals(selected.GlassType, glassType, StringComparison.OrdinalIgnoreCase)))
                        target = selected;

                    if (target == null)
                    {
                        Report("Could not map imported specification '" + (specName.Length == 0 ? "(unspecified)" : specName)
                            + "' / glass type '" + (glassType.Length == 0 ? "(unspecified)" : glassType)
                            + "' to one existing group. Create or select the matching priced group first; no rows were imported.");
                        return;
                    }

                    plan.Add((target, batch.ToList(), specName, glassType));
                }

                foreach (var item in plan)
                {
                    if (item.Target.SpecName.Length == 0 && item.SpecName.Length > 0)
                    {
                        item.Target.SpecName = item.SpecName;
                        item.Target.HasManualSpecOverride = true;
                    }
                    if (item.Target.GlassType.Length == 0 && item.GlassType.Length > 0)
                    {
                        item.Target.GlassType = item.GlassType;
                        item.Target.HasManualGlassTypeOverride = true;
                    }

                    foreach (ProformaGridImportRow imported in item.Rows)
                    {
                        item.Target.Rows.Add(new SpecRow
                        {
                            SerialNumber = item.Target.Rows.Count + 1,
                            GlassRef = imported.GlassRef,
                            W1 = imported.W1,
                            H1 = imported.H1,
                            W2 = imported.W2,
                            H2 = imported.H2,
                            Qty = imported.Qty
                        });
                    }
                }

                foreach (SpecGroup group in plan.Select(item => item.Target)
                    .GroupBy(group => group.GroupNumber).Select(group => group.First()))
                    group.Renumber();

                Reapply();
                SyncFromDraft();
                ClearError();
                int importedGroups = plan.Select(item => item.Target.GroupNumber).Distinct().Count();
                SetStatus("Imported " + result.Rows.Count + " cut-list row(s) into "
                    + importedGroups + " specification group(s)."
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
                    SetStatus(IncludeProfitLossInWorkbook
                        ? "INTERNAL workbook saved with cost and margin. Do not send to the customer. Viewer could not open it."
                        : "Workbook saved, but the default viewer could not be opened.");
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