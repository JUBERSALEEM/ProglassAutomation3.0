// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Sizes.cs
// MODULE: Optimization — SIZES tab — merged groups, cutting-group selector and cut plan (partial of OptimizationViewModel)
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus;
using ProGlassAutomation.Models.Production;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.JobOrder.Repository;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Production;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : ViewModels.ViewModelBase
    {

        // ── SIZES tab: merged groups ───────────────────────────────────

        public sealed class SizeGroupVm
        {
            public string Composition { get; set; } = string.Empty;
            public string Thickness { get; set; } = string.Empty;
            public string PieceCount { get; set; } = string.Empty;
            /// <summary>Distinct W×H rows in this group (≠ piece count — one
            /// size row can carry a qty of many pieces).</summary>
            public string SizeRows { get; set; } = string.Empty;
            public string AreaM2 { get; set; } = string.Empty;
        }

        public ObservableCollection<SizeGroupVm> SizeGroups { get; } = new();

        // ── SIZES tab: expanded cut plan (the pieces as the saw cuts them) ──

        public sealed class CutPlanRowVm
        {
            public string Number { get; set; } = string.Empty;
            public string Ref { get; set; } = string.Empty;
            public string Composition { get; set; } = string.Empty;
            public string Size { get; set; } = string.Empty;
        }

        public ObservableCollection<CutPlanRowVm> CutPlanRows { get; } = new();

        private bool _hasCutPlan;
        public bool HasCutPlan { get => _hasCutPlan; private set { _hasCutPlan = value; OnPropertyChanged(); } }

        private string _cutPlanNote = string.Empty;
        public string CutPlanNote { get => _cutPlanNote; private set { _cutPlanNote = value; OnPropertyChanged(); OnPropertyChanged(nameof(HasCutPlanNote)); } }
        public bool HasCutPlanNote => !string.IsNullOrWhiteSpace(_cutPlanNote);

        /// <summary>Expanded rows are capped for the on-screen plan (the engine still cuts every piece).</summary>
        private const int CutPlanDisplayCap = 500;

        // ── SIZES/run: pick ONE cutting group (outer / inner / …) to
        //    optimize separately — 2D + SUMMARY show only that group ────

        public ObservableCollection<string> CuttingGroups { get; } = new();

        private string _selectedCuttingGroup = AllCuttingGroups;
        public string SelectedCuttingGroup
        {
            get => _selectedCuttingGroup;
            set
            {
                if (_rebuildingCuttingGroups || value == null) return;
                string next = CuttingGroups.FirstOrDefault(group =>
                    string.Equals(group, value, StringComparison.OrdinalIgnoreCase)) ?? AllCuttingGroups;
                if (_selectedCuttingGroup == next) return;
                _selectedCuttingGroup = next;
                OnPropertyChanged();
                SyncThicknessFromInputs();
                RefreshSizeViews();
            }
        }

        /// <summary>Sentinel: pack every cutting group (default).</summary>
        public const string AllCuttingGroups = "ALL — every glass group";

        private bool SelectedGroupMatches(string composition) =>
            string.Equals(_selectedCuttingGroup, AllCuttingGroups, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(_selectedCuttingGroup, composition, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The SIZES dropdown: every glass composition in the list (outer /
        /// inner / …) so the operator can optimize one pane group at a time
        /// against its own Step 4 sheet. Selection survives edits.
        /// </summary>
        private bool _rebuildingCuttingGroups;
        private void RebuildCuttingGroups()
        {
            _rebuildingCuttingGroups = true;
            try
            {
                string previous = _selectedCuttingGroup;

                var compositions = Pieces
                    .Where(p => p.TryParse(out _, out _, out _))
                    .Select(p => (p.GlassRef ?? string.Empty).Trim())
                    .Where(c => c.Length > 0)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(c => c, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                CuttingGroups.Clear();
                CuttingGroups.Add(AllCuttingGroups);
                foreach (var c in compositions) CuttingGroups.Add(c);

                // Keep the selection only if it still exists — and point it at
                // the canonical list item so the ComboBox selection matches.
                var matched = compositions.FirstOrDefault(c =>
                    string.Equals(c, previous, StringComparison.OrdinalIgnoreCase));
                _selectedCuttingGroup = matched ?? AllCuttingGroups;
            }
            finally { _rebuildingCuttingGroups = false; }
            OnPropertyChanged(nameof(SelectedCuttingGroup));
        }

        /// <summary>
        /// Expands the piece list into the on-screen cut plan — one numbered
        /// row per physical piece (qty expands 1..N) with its ref, composition
        /// and size. Capped at <see cref="CutPlanDisplayCap"/> rows for the
        /// screen; the engine itself always cuts every piece.
        /// </summary>
        private void RebuildCutPlan()
        {
            CutPlanRows.Clear();
            int number = 0;
            int total = 0;
            bool truncated = false;

            foreach (var p in Pieces)
            {
                if (!p.TryParse(out int w, out int h, out int q)) continue;
                string composition = string.IsNullOrWhiteSpace(p.GlassRef) ? "—" : p.GlassRef.Trim();
                if (!SelectedGroupMatches(composition)) continue;

                total += q;
                if (number >= CutPlanDisplayCap) { truncated = true; continue; }

                for (int i = 1; i <= q; i++)
                {
                    if (number >= CutPlanDisplayCap) { truncated = true; break; }
                    number++;
                    string refLabel = string.IsNullOrWhiteSpace(p.PieceLabel)
                        ? "—" : p.PieceLabel.Trim();
                    CutPlanRows.Add(new CutPlanRowVm
                    {
                        Number = number.ToString(CultureInfo.InvariantCulture),
                        Ref = q > 1
                            ? $"{refLabel} {i.ToString(CultureInfo.InvariantCulture)}/{q.ToString(CultureInfo.InvariantCulture)}"
                            : refLabel,
                        Composition = composition,
                        Size = $"{w.ToString(CultureInfo.InvariantCulture)} × {h.ToString(CultureInfo.InvariantCulture)} mm"
                    });
                }
            }

            HasCutPlan = CutPlanRows.Count > 0;
            CutPlanNote = truncated
                ? $"Showing the first {CutPlanDisplayCap.ToString(CultureInfo.InvariantCulture)} of " +
                  $"{total.ToString(CultureInfo.InvariantCulture)} pieces — the engine cuts all of them."
                : string.Empty;
        }
    }
}
