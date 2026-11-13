// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.Presentation/step6.c.4.ProformaWorkspaceViewModel.Stages.cs
// MODULE: step6 — stages 2 to 6: pricing, grid, charges, summary and documents
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Proforma;

namespace ProGlassAutomation.ViewModels.Proforma
{
    public sealed partial class ProformaWorkspaceViewModel
    {
        // ── Stage 2: pricing ────────────────────────────────────────────
        public PricingSection? SelectedSection
        {
            get => _selectedSection;
            set
            {
                if (!SetField(ref _selectedSection, value)) return;
                OnPropertyChanged(nameof(HasSection));
                OnPropertyChanged(nameof(SectionFormulaText));
            }
        }

        public bool HasSection => _selectedSection != null;

        public string NewBuild
        {
            get => _newBuild;
            set => SetField(ref _newBuild, value ?? GlassBuilds.Dgu);
        }

        /// <summary>The formula the engine used, spelled out — the operator can check it.</summary>
        public string SectionFormulaText
        {
            get
            {
                if (_selectedSection == null) return "";
                PricingResult result = _workspace.CalculateSection(_selectedSection.SectionNumber);
                string warnings = result.Warnings.Count == 0 ? "" : " · " + string.Join(" · ", result.Warnings);
                return (result.IsComplete
                    ? result.RatePerSqm.ToString("N2", CultureInfo.CurrentCulture) + " AED/m² · " + result.FormulaText
                    : "Not priced yet") + warnings;
            }
        }

        // ── Stage 3: grid ───────────────────────────────────────────────
        public SpecGroup? SelectedGroup
        {
            get => _selectedGroup;
            set
            {
                if (!SetField(ref _selectedGroup, value)) return;
                OnPropertyChanged(nameof(HasGroup));
                SyncGroupRows();
            }
        }

        public bool HasGroup => _selectedGroup != null;

        public SpecRow? SelectedSpecRow
        {
            get => _selectedSpecRow;
            set => SetField(ref _selectedSpecRow, value);
        }

        // ── Stage 4: charges ────────────────────────────────────────────
        public string NewChargeName
        {
            get => _newChargeName;
            set => SetField(ref _newChargeName, value ?? "");
        }

        public decimal NewChargeQuantity
        {
            get => _newChargeQuantity;
            set => SetField(ref _newChargeQuantity, value);
        }

        public decimal NewChargeRate
        {
            get => _newChargeRate;
            set => SetField(ref _newChargeRate, value);
        }

        public ChargeUnit NewChargeUnit
        {
            get => _newChargeUnit;
            set => SetField(ref _newChargeUnit, value);
        }

        public CustomChargeRow? SelectedCustomCharge
        {
            get => _selectedCustomCharge;
            set => SetField(ref _selectedCustomCharge, value);
        }

        // ── Stage 5: summary ────────────────────────────────────────────
        public DiscountOption? SelectedDiscountOption
        {
            get => _selectedDiscountOption;
            set
            {
                if (!SetField(ref _selectedDiscountOption, value) || value == null) return;
                _workspace.SetDiscount(value.Value, Draft.Summary.DiscountValue);
                Reapply();
            }
        }

        public decimal DiscountValue
        {
            get => Draft.Summary.DiscountValue;
            set
            {
                _workspace.SetDiscount(Draft.Summary.DiscountMethod, value);
                OnPropertyChanged();
                Reapply();
            }
        }

        public bool VatApplicable
        {
            get => Draft.Summary.IsVatApplicable;
            set
            {
                _workspace.SetVat(value, Draft.Summary.VatPercent);
                OnPropertyChanged();
                Reapply();
            }
        }

        public decimal VatPercent
        {
            get => Draft.Summary.VatPercent;
            set
            {
                _workspace.SetVat(Draft.Summary.IsVatApplicable, value);
                OnPropertyChanged();
                Reapply();
            }
        }

        public decimal WastagePercent
        {
            get => Draft.ProfitLossSettings.WastagePercent;
            set
            {
                _workspace.SetWastagePercent(value);
                OnPropertyChanged();
                Reapply();
            }
        }

        public string TargetGrandTotalText
        {
            get => _targetGrandTotalText;
            set => SetField(ref _targetGrandTotalText, value ?? "");
        }

        public string Notes
        {
            get => Draft.Notes;
            set { Draft.Notes = value ?? ""; OnPropertyChanged(); }
        }

        public string TermsText
        {
            get => Draft.TermsText;
            set { Draft.TermsText = value ?? ""; OnPropertyChanged(); }
        }

        // ── Totals (display only) ───────────────────────────────────────
        public string GlassSubtotalText => Money(Draft.Summary.GlassSubtotal);
        public string OtherChargesText => Money(Draft.Summary.OtherChargesSubtotal);
        public string NetSubtotalText => Money(Draft.Summary.NetSubtotal);
        public string DiscountAmountText => Money(Draft.Summary.DiscountAmount);
        public string BeforeVatText => Money(Draft.Summary.SubtotalBeforeVat);
        public string VatAmountText => Money(Draft.Summary.VatAmount);
        public string GrandTotalText => Money(Draft.Summary.GrandTotal);
        public string TotalCostText => Money(Draft.ProfitLoss.TotalCost);
        public string ProfitText => Money(Draft.ProfitLoss.Profit);
        public string MarginText => Draft.ProfitLoss.MarginPercent is decimal margin
            ? margin.ToString("N1", CultureInfo.CurrentCulture) + " %"
            : "—";
        public string CostBasisNote => Draft.ProfitLoss.BasisNote;
        public string GridAreaText => Draft.GridTotals.TotalSqm.ToString("N2", CultureInfo.CurrentCulture) + " m²";

        // ── Stage 6: attachments ────────────────────────────────────────
        public ProformaAttachment? SelectedAttachment
        {
            get => _selectedAttachment;
            set => SetField(ref _selectedAttachment, value);
        }

        public bool HasAttachments => Attachments.Count > 0;
    }
}
