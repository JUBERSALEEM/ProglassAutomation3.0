// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.Presentation/step6.c.6.ProformaWorkspaceViewModel.Sync.cs
// MODULE: step6 — one recalculation seam and the projection refresh
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Services.Proforma;
using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.ViewModels.Proforma
{
    public sealed partial class ProformaWorkspaceViewModel
    {
        // ── Editor lifecycle ────────────────────────────────────────────
        private void PrepareEditor()
        {
            IsEditorOpen = true;
            SelectedSection = null;
            SelectedGroup = null;
            SyncFromDraft();
            _workspace.GoToStage(InvoiceStage.Company);
            SyncStages();
        }

        internal void SelectStage(string key)
        {
            if (!Enum.TryParse(key, out InvoiceStage stage)) return;
            _workspace.GoToStage(stage);
            SyncStages();
            OnPropertyChanged(nameof(CurrentStageKey));
        }

        /// <summary>Rebuild the stage rail from the engine's own evaluation.</summary>
        private void SyncStages()
        {
            IReadOnlyList<StageStatus> statuses = _workspace.StageStatuses;
            if (Stages.Count == 0)
            {
                foreach (InvoiceStage stage in new[] { InvoiceStage.Company, InvoiceStage.Client, InvoiceStage.Project, InvoiceStage.Pricing, InvoiceStage.SpecGrid, InvoiceStage.OtherCharges, InvoiceStage.Summary, InvoiceStage.Attachments, InvoiceStage.ProfitLoss })
                {
                    Stages.Add(new ProformaStageChip
                    {
                        Key = StageKey(stage),
                        Label = StageLabel(stage),
                        Number = (int)stage
                    });
                }
            }

            foreach (ProformaStageChip chip in Stages)
            {
                Enum.TryParse(chip.Key, out InvoiceStage chipStage);
                StageStatus? status = statuses.FirstOrDefault(item => item.Stage == chipStage);
                chip.IsComplete = status?.IsComplete ?? false;
                chip.ProblemCount = status?.Problems.Count ?? 0;
                chip.IsSelected = string.Equals(chip.Key, CurrentStageKey, StringComparison.Ordinal);
            }

            SelectedStageChip = Stages.FirstOrDefault(chip => chip.IsSelected);
            OnPropertyChanged(nameof(StageHeadline));
        }

        private static string StageKey(InvoiceStage stage) => stage switch
        {
            InvoiceStage.Company => "Company",
            InvoiceStage.Client => "Client",
            InvoiceStage.Project => "Project",
            InvoiceStage.Pricing => "Pricing",
            InvoiceStage.SpecGrid => "SpecGrid",
            InvoiceStage.OtherCharges => "OtherCharges",
            InvoiceStage.Summary => "Summary",
            InvoiceStage.Attachments => "Attachments",
            _ => "ProfitLoss"
        };

        private static string StageLabel(InvoiceStage stage) => stage switch
        {
            InvoiceStage.Company => "Company",
            InvoiceStage.Client => "Client",
            InvoiceStage.Project => "Project",
            InvoiceStage.Pricing => "Pricing",
            InvoiceStage.SpecGrid => "Spec grid",
            InvoiceStage.OtherCharges => "Charges",
            InvoiceStage.Summary => "Summary",
            InvoiceStage.Attachments => "Documents",
            _ => "Profit & loss"
        };

        /// <summary>Refresh everything that is a projection of the draft.</summary>
        internal void SyncFromDraft()
        {
            SyncList(Sections, Draft.PricingSections);
            SyncList(GridGroups, Draft.Groups);
            SyncList(PredefinedCharges, Draft.Charges.Predefined);
            SyncList(WastageCharges, Draft.Charges.Wastages);
            SyncList(CustomCharges, Draft.Charges.Custom);
            SyncList(CostRows, Draft.ProfitLoss.Rows);
            SyncList(TargetRows, Draft.ProfitLoss.SpecTargets);

            if (_selectedSection != null && !Sections.Contains(_selectedSection)) SelectedSection = null;
            if (_selectedGroup != null && !GridGroups.Contains(_selectedGroup)) SelectedGroup = null;

            _selectedDiscountOption ??= DiscountOptions.FirstOrDefault(
                option => option.Value == Draft.Summary.DiscountMethod);

            SyncGroupRows();
            SyncStages();
            RaiseTotals();
            OnPropertyChanged(nameof(EditorHeading));
            OnPropertyChanged(nameof(EditorStatusText));
            OnPropertyChanged(nameof(EditorStatusTag));
            OnPropertyChanged(nameof(IsPersisted));
            OnPropertyChanged(nameof(CanEditBody));
            OnPropertyChanged(nameof(CanSend));
            OnPropertyChanged(nameof(CanApprove));
            OnPropertyChanged(nameof(CanReject));
            OnPropertyChanged(nameof(CanCancelDocument));
            OnPropertyChanged(nameof(HasInquiry));
            OnPropertyChanged(nameof(CompanyNameText));
            OnPropertyChanged(nameof(CompanyTrnText));
            OnPropertyChanged(nameof(CompanyAddressText));
            OnPropertyChanged(nameof(ClientNameText));
            OnPropertyChanged(nameof(ClientTrnText));
            OnPropertyChanged(nameof(ClientAttentionText));
            OnPropertyChanged(nameof(ClientAddressText));
            OnPropertyChanged(nameof(ProjectText));
            OnPropertyChanged(nameof(PreparedByText));
            OnPropertyChanged(nameof(InquiryText));
            OnPropertyChanged(nameof(PartiesRefreshedText));
            OnPropertyChanged(nameof(HasAttachments));
            OnPropertyChanged(nameof(SectionFormulaText));
            OnPropertyChanged(nameof(HasSection));
            OnPropertyChanged(nameof(HasGroup));
        }

        private void SyncGroupRows()
        {
            SyncList(GroupRows, _selectedGroup?.Rows ?? new List<SpecRow>());
        }

        /// <summary>
        /// The one place the page asks the engine to recalculate after an edit made
        /// in a grid cell or a field. It never rebuilds a collection in the middle
        /// of an edit session unless the engine itself replaced the list.
        /// </summary>
        public void Reapply()
        {
            try
            {
                _workspace.RefreshDerived();
            }
            catch (Exception error)
            {
                Report("The totals could not be recalculated: " + error.GetBaseException().Message);
                return;
            }

            OnPropertyChanged(nameof(SectionFormulaText));
            RaiseTotals();
            SyncStages();
        }

        private void RaiseTotals()
        {
            OnPropertyChanged(nameof(GlassSubtotalText));
            OnPropertyChanged(nameof(OtherChargesText));
            OnPropertyChanged(nameof(NetSubtotalText));
            OnPropertyChanged(nameof(DiscountAmountText));
            OnPropertyChanged(nameof(BeforeVatText));
            OnPropertyChanged(nameof(VatAmountText));
            OnPropertyChanged(nameof(GrandTotalText));
            OnPropertyChanged(nameof(TotalCostText));
            OnPropertyChanged(nameof(ProfitText));
            OnPropertyChanged(nameof(MarginText));
            OnPropertyChanged(nameof(CostBasisNote));
            OnPropertyChanged(nameof(GridAreaText));
            OnPropertyChanged(nameof(WastagePercent));
            LossWarnings.Clear();
            foreach (string warning in Draft.ProfitLoss.Warnings) LossWarnings.Add(warning);
        }

        private static void SyncList<T>(ObservableCollection<T> target, IEnumerable<T> source)
        {
            var wanted = new List<T>(source);
            for (int index = target.Count - 1; index >= 0; index--)
                if (!wanted.Contains(target[index])) target.RemoveAt(index);
            for (int index = 0; index < wanted.Count; index++)
            {
                if (index < target.Count && EqualityComparer<T>.Default.Equals(target[index], wanted[index]))
                    continue;
                if (index < target.Count) target[index] = wanted[index];
                else target.Add(wanted[index]);
            }
        }

        private async Task RefreshPartiesAsync()
        {
            try
            {
                await _workspace.RefreshPartiesAsync().ConfigureAwait(false);
                SyncFromDraft();
                SetStatus(_workspace.Messages.Count > 0 ? _workspace.Messages[0]
                    : "The header was re-read from the master records.");
                ClearError();
            }
            catch (Exception error)
            {
                Report("The header could not be refreshed: " + error.GetBaseException().Message);
            }
        }

        private async Task ImportScopeAsync()
        {
            try
            {
                BoqImportResult result = await _workspace.ImportInquiryScopeAsync().ConfigureAwait(false);
                ImportMessage = result.Messages.Count > 0
                    ? string.Join(" ", result.Messages)
                    : result.GroupsAdded + " specification(s) imported.";
                SyncFromDraft();
                ClearError();
            }
            catch (Exception error)
            {
                Report("The inquiry scope could not be imported: " + error.GetBaseException().Message);
            }
        }

        private async Task LoadAttachmentsAsync()
        {
            Attachments.Clear();
            if (Draft.Id <= 0)
            {
                OnPropertyChanged(nameof(HasAttachments));
                return;
            }

            IReadOnlyList<ProformaAttachment> rows =
                await _repository.GetAttachmentsAsync(Draft.Id).ConfigureAwait(false);
            foreach (ProformaAttachment attachment in rows) Attachments.Add(attachment);
            OnPropertyChanged(nameof(HasAttachments));
        }

        /// <summary>Adds a file chosen by the view's file dialog.</summary>
        public async Task AttachFileAsync(string path)
        {
            if (Draft.Id <= 0)
            {
                Report("Save the invoice before attaching files.");
                return;
            }

            ProformaAttachmentAddResult result = await _repository
                .AddAttachmentAsync(Draft.Id, path, "Document", "").ConfigureAwait(false);
            if (!result.Succeeded)
            {
                Report(result.Message);
                return;
            }

            ClearError();
            SetStatus("Attached " + (result.Attachment?.FileName ?? "the file") + ".");
            await LoadAttachmentsAsync().ConfigureAwait(false);
        }

        private async Task RemoveAttachmentAsync()
        {
            if (_selectedAttachment == null) return;
            long id = _selectedAttachment.Id;
            try
            {
                bool removed = await _repository.RemoveAttachmentAsync(id).ConfigureAwait(false);
                if (!removed)
                {
                    Report("The attachment could not be removed. Its row or file is missing.");
                    return;
                }
                SelectedAttachment = null;
                ClearError();
                SetStatus("The attachment was removed.");
                await LoadAttachmentsAsync().ConfigureAwait(false);
            }
            catch (Exception error)
            {
                Report("The attachment could not be removed: " + error.GetBaseException().Message);
            }
        }

        private static string Money(decimal value) =>
            value.ToString("N2", CultureInfo.CurrentCulture) + " AED";
    }
}
