// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.Presentation/step6.c.5.ProformaWorkspaceViewModel.Commands.cs
// MODULE: step6 — the editor's commands, one place per action
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.Services.Proforma;

namespace ProGlassAutomation.ViewModels.Proforma
{
    public sealed partial class ProformaWorkspaceViewModel
    {
        // ── Commands ────────────────────────────────────────────────────
        public ICommand AddSectionCommand { get; private set; } = null!;
        public ICommand RemoveSectionCommand { get; private set; } = null!;
        public ICommand AddGroupCommand { get; private set; } = null!;
        public ICommand RemoveGroupCommand { get; private set; } = null!;
        public ICommand AddRowCommand { get; private set; } = null!;
        public ICommand RemoveRowCommand { get; private set; } = null!;
        public ICommand AddCustomChargeCommand { get; private set; } = null!;
        public ICommand RemoveCustomChargeCommand { get; private set; } = null!;
        public ICommand SetTargetCommand { get; private set; } = null!;
        public ICommand RefreshPartiesCommand { get; private set; } = null!;
        public ICommand ImportScopeCommand { get; private set; } = null!;
        public ICommand RemoveAttachmentCommand { get; private set; } = null!;
        public ICommand RecalculateCommand { get; private set; } = null!;

        private void BuildEditorCommands()
        {
            // Grid cells and form fields edit the draft directly; this is the one
            // control that asks the engine to recompute after such an edit.
            RecalculateCommand = new DelegateCommand(
                () => { Reapply(); SyncFromDraft(); },
                () => CanEditBody);

            AddSectionCommand = new DelegateCommand(() =>
            {
                _workspace.Draft.AddPricingSection(_newBuild);
                SyncFromDraft();
                SetStatus("A " + _newBuild + " pricing section was added.");
            }, () => CanEditBody);

            RemoveSectionCommand = new DelegateCommand(() =>
            {
                if (_selectedSection == null) return;
                if (!_workspace.Draft.RemovePricingSection(_selectedSection.SectionNumber))
                {
                    Report("That section is the only one, or a specification still uses it.");
                    return;
                }
                SyncFromDraft();
            }, () => CanEditBody && HasSection);

            AddGroupCommand = new DelegateCommand(() =>
            {
                int number = _selectedSection?.SectionNumber ?? 1;
                PricingResult result = _workspace.AddGroupFromSection(number);
                if (!result.IsComplete)
                {
                    Report(result.Warnings.Count > 0
                        ? result.Warnings[0]
                        : "That pricing section is not complete, so it cannot become a specification.");
                    return;
                }
                ClearError();
                SyncFromDraft();
            }, () => CanEditBody && HasSection);

            RemoveGroupCommand = new DelegateCommand(() =>
            {
                if (_selectedGroup == null) return;
                _workspace.Draft.RemoveGroup(_selectedGroup.GroupNumber);
                SelectedGroup = null;
                Reapply();
                SyncFromDraft();
            }, () => CanEditBody && HasGroup);

            AddRowCommand = new DelegateCommand(() =>
            {
                if (_selectedGroup == null) return;
                _workspace.AddRow(_selectedGroup.GroupNumber);
                SyncGroupRows();
                Reapply();
            }, () => CanEditBody && HasGroup);

            RemoveRowCommand = new DelegateCommand(() =>
            {
                if (_selectedGroup == null || _selectedSpecRow == null) return;
                if (_selectedGroup.Rows.Count <= 1)
                {
                    Report("A specification needs at least one measured row.");
                    return;
                }
                _selectedGroup.Rows.Remove(_selectedSpecRow);
                _selectedGroup.Renumber();
                SelectedSpecRow = null;
                SyncGroupRows();
                Reapply();
            }, () => CanEditBody && HasGroup && _selectedSpecRow != null);

            AddCustomChargeCommand = new DelegateCommand(() =>
            {
                if (_newChargeName.Trim().Length == 0)
                {
                    Report("Give the charge a name before adding it.");
                    return;
                }
                _workspace.AddCustomCharge(_newChargeName, _newChargeQuantity, _newChargeUnit, _newChargeRate);
                NewChargeName = "";
                NewChargeQuantity = 0m;
                NewChargeRate = 0m;
                ClearError();
                SyncFromDraft();
            }, () => CanEditBody);

            RemoveCustomChargeCommand = new DelegateCommand(() =>
            {
                if (_selectedCustomCharge == null) return;
                _workspace.Draft.Charges.Custom.Remove(_selectedCustomCharge);
                SelectedCustomCharge = null;
                Reapply();
                SyncFromDraft();
            }, () => CanEditBody && _selectedCustomCharge != null);

            SetTargetCommand = new DelegateCommand(() =>
            {
                if (!decimal.TryParse(_targetGrandTotalText, NumberStyles.Number,
                        CultureInfo.CurrentCulture, out decimal target))
                {
                    Report("Type the grand total you want, for example 15,500.00.");
                    return;
                }
                bool reached = _workspace.SetTargetGrandTotal(target);
                if (!reached)
                {
                    Report("That total cannot be reached by discounting this invoice.");
                    return;
                }
                ClearError();
                SetStatus("The discount was solved for a grand total of " + Money(target) + ".");
                SyncFromDraft();
            }, () => CanEditBody);

            RefreshPartiesCommand = new DelegateCommand(() => _ = RefreshPartiesAsync(), () => CanEditBody);

            ImportScopeCommand = new DelegateCommand(() => _ = ImportScopeAsync(),
                () => CanEditBody && HasInquiry);

            RemoveAttachmentCommand = new DelegateCommand(() => _ = RemoveAttachmentAsync(),
                () => _selectedAttachment != null);
        }
    }
}
