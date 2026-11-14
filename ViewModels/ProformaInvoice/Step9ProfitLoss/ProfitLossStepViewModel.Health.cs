// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Health.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 9 — maintenance rating / improvement guidance
//
// Step 9 now exposes a plain-language rating so management can see whether
// the P&L screen is calculated, reconciled and commercially usable before a
// quote leaves the company. This does not replace detailed validation; it is
// the visible executive summary of the same hardening signals.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Linq;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public partial class ProfitLossStepViewModel : ViewModelBase
    {
        public int Step9MaintenanceScore
        {
            get
            {
                int score = 100;

                if (IsBusy || IsRecalculationPending) score -= 10;
                if (!LastCalculatedAt.HasValue) score -= 30;
                if (!HasRows) score -= 35;
                if (GrossVatInclusive <= 0m && HasRows) score -= 20;
                if (HasCalculationWarnings) score -= Math.Min(30, 8 + CalculationWarnings.Count * 4);
                if (JobTargetResult == null || !HasTargetSpecs) score -= 10;
                if (IsLoss) score -= 25;

                return Math.Max(0, Math.Min(100, score));
            }
        }

        public string Step9MaintenanceRating => Step9MaintenanceScore switch
        {
            >= 95 => "A+ · Excellent",
            >= 85 => "A · Well maintained",
            >= 75 => "B · Good, review warnings",
            >= 60 => "C · Needs improvement",
            _ => "D · Block/recalculate before relying"
        };

        public string Step9ImprovementSummary
        {
            get
            {
                if (IsBusy || IsRecalculationPending)
                    return "Calculation is still running — wait for the latest edit to settle.";
                if (!LastCalculatedAt.HasValue)
                    return "Press Recalculate or open Step 9 after Step 5/6/7 are complete.";
                if (!HasRows)
                    return "No cost rows were produced; verify Step 5 specs and Sheet Store rates.";
                if (GrossVatInclusive <= 0m)
                    return "Revenue is missing; complete Step 7 totals before judging profit.";
                if (HasCalculationWarnings)
                    return CalculationWarnings.FirstOrDefault() ?? "Resolve target/spec warnings.";
                if (IsLoss)
                    return "Invoice is below cost; revise given price or material/production assumptions.";
                if (JobTargetResult == null || !HasTargetSpecs)
                    return "Target profitability is incomplete; verify Step 4 pricing handoff.";
                return "No blocking improvement found — keep Step 4/5/6/7 reconciled before approval.";
            }
        }

        private void RaiseStep9HealthNotifications()
        {
            OnPropertyChanged(nameof(Step9MaintenanceScore));
            OnPropertyChanged(nameof(Step9MaintenanceRating));
            OnPropertyChanged(nameof(Step9ImprovementSummary));
        }
    }
}
