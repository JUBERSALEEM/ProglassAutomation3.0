using System;
using System.Diagnostics;
using System.Threading.Tasks;
using System.Windows.Input;
using Microsoft.Win32;
using ProGlassAutomation.Services.Proforma;
using ProGlassAutomation.ThemeStudio;

namespace ProGlassAutomation.ViewModels.Proforma
{
    public sealed partial class ProformaWorkspaceViewModel
    {
        private ProformaPdfStyle _selectedPdfStyle = ProformaPdfStyles.Default;
        private bool _includeProfitLossInDossier;

        public IReadOnlyList<ProformaPdfStyle> PdfStyles => ProformaPdfStyles.All;

        public ProformaPdfStyle SelectedPdfStyle
        {
            get => _selectedPdfStyle;
            set => SetField(ref _selectedPdfStyle, value ?? ProformaPdfStyles.Default);
        }

        public bool IncludeProfitLossInDossier
        {
            get => _includeProfitLossInDossier;
            set => SetField(ref _includeProfitLossInDossier, value);
        }

        public ICommand ExportPdfCommand { get; } =
            new DelegateCommand(() => _ = ExportPdfAsync());

        public ICommand ExportDossierPdfCommand { get; } =
            new DelegateCommand(() => _ = ExportDossierPdfAsync());

        private async Task ExportDossierPdfAsync()
        {
            if (!Draft.IsPersisted)
            {
                Report("Save the invoice first. A dossier must have a permanent invoice number.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Proforma Invoice Dossier",
                Filter = "PDF document (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = ProformaPdfExporter.SuggestedDossierFileName(Draft),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var exporter = new ProformaPdfExporter();
                await exporter.ExportDossierAsync(
                    Draft, SelectedPdfStyle, dialog.FileName, IncludeProfitLossInDossier);
                SetStatus(IncludeProfitLossInDossier
                    ? "INTERNAL dossier exported. It contains cost and margin; do not send to the customer."
                    : "Dossier exported without internal cost and margin.");
                ClearError();
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch (Exception openError)
                {
                    SetStatus(IncludeProfitLossInDossier
                        ? "INTERNAL dossier saved with cost and margin. Do not send to the customer. Viewer could not open it: "
                            + openError.GetBaseException().Message
                        : "Dossier saved, but the viewer could not be opened: "
                            + openError.GetBaseException().Message);
                }
            }
            catch (Exception error)
            {
                Report("The dossier could not be exported: " + error.GetBaseException().Message);
            }
        }

        private async Task ExportPdfAsync()
        {
            if (!Draft.IsPersisted)
            {
                Report("Save the invoice first. A PDF must have a permanent invoice number.");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Title = "Export Proforma Invoice",
                Filter = "PDF document (*.pdf)|*.pdf",
                DefaultExt = ".pdf",
                AddExtension = true,
                FileName = ProformaPdfExporter.SuggestedFileName(Draft),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog() != true) return;

            try
            {
                var exporter = new ProformaPdfExporter();
                await exporter.ExportAsync(Draft, SelectedPdfStyle, dialog.FileName);
                SetStatus("PDF exported using " + SelectedPdfStyle.Name + ".");
                ClearError();
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = dialog.FileName,
                        UseShellExecute = true
                    });
                }
                catch (Exception openError)
                {
                    SetStatus("PDF saved, but the viewer could not be opened: "
                        + openError.GetBaseException().Message);
                }
            }
            catch (Exception error)
            {
                Report("The PDF could not be exported: " + error.GetBaseException().Message);
            }
        }
    }
}