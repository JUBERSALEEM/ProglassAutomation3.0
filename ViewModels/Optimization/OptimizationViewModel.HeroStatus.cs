// ══════════════════════════════════════════════════════════════════
// FILE: ViewModels/Optimization/OptimizationViewModel.HeroStatus.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — hero card status line (approved 2026-09-26 top card)
//
// WHY THIS FILE
//   The approved top-card mockup carries exactly ONE status line, muted
//   lavender and right-aligned on the heading row, reading
//   "Ready — 3 cutting groups staged" when nothing else is happening.
//
//   The header used to stack four separate blocks there instead — the live
//   run stage, the validation/export message, the effort stamp and the
//   QUICK → OPTIMIZE comparison — plus a 3px indeterminate bar. Collapsing
//   all of that into one line is only safe if the line always shows the
//   most urgent thing the operator needs, so this partial owns that one
//   decision and nothing else.
//
//   Priority, highest first:
//     1. IsRunning         → the live engine stage (RunStageLabel).
//     2. HasRunMessage     → validation refusal or export block (RunMessage).
//     3. HasUpgradeSummary → what the deeper OPTIMIZE search bought.
//     4. HasResultEffort   → which effort produced the plan on screen.
//     5. otherwise         → the idle "Ready — N cutting group(s) staged".
//
//   Nothing here changes engine behaviour. The run progress MODAL still
//   shows the full determinate state, so the indeterminate bar dropped
//   from the header is not a loss of information.
// ══════════════════════════════════════════════════════════════════

using System.Globalization;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        /// <summary>
        /// The single status line on the hero's heading row. Recomputed on
        /// every read, so no cached string can drift out of sync with the
        /// flags it is derived from.
        /// </summary>
        public string HeroStatusLabel
        {
            get
            {
                if (_isRunning)
                {
                    return string.IsNullOrWhiteSpace(_runStageLabel)
                        ? "Optimizing…"
                        : _runStageLabel;
                }

                if (HasRunMessage) return _runMessage;
                if (HasUpgradeSummary) return _upgradeSummaryLabel;
                if (HasResultEffort) return _resultEffortLabel;
                return ReadyLabel;
            }
        }

        /// <summary>
        /// Idle text: how many cutting groups are staged and ready to run.
        /// "0" means no job order has been picked yet, so the line says what
        /// to do next rather than reporting an empty count.
        /// </summary>
        private string ReadyLabel
        {
            get
            {
                bool parsed = int.TryParse(
                    _kpiGroups, NumberStyles.Integer, CultureInfo.InvariantCulture, out int groups);

                if (!parsed || groups <= 0)
                {
                    return "Ready — select a confirmed Job Order to stage pieces";
                }

                return groups == 1
                    ? "Ready — 1 cutting group staged"
                    : string.Format(CultureInfo.InvariantCulture,
                          "Ready — {0} cutting groups staged", groups);
            }
        }

        /// <summary>
        /// Raises the change notification for <see cref="HeroStatusLabel"/>.
        /// Called from every setter whose value can change what the line
        /// reads, because the property is computed rather than stored.
        /// </summary>
        private void NotifyHeroStatusChanged() => OnPropertyChanged(nameof(HeroStatusLabel));
    }
}
