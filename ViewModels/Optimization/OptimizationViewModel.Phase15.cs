// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Optimization/OptimizationViewModel.Phase15.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — Phase 15 Work Center extensions
//   Extends Optimization UI with full Cutting + 2D workflow:
//   Job, Spec, Material, Stock, Rotation, Margin, Split mode, Effort,
//   Prepare, Optimize, Verify, Allocate, Open Layout, Sheets Used,
//   Utilization, Wastage, Verified, Pieces, Stock, traceability.
//   Reuses existing GDS, no new design system, no fake metrics.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.Production.Cutting;
using ProGlassAutomation.Models.Production.SavedProduction;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Production;
using ProGlassAutomation.Services.Production.Cutting;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        // ── Phase 15: Cutting Workflow Integration ───────────────────────

        public ObservableCollection<SavedCuttingSession> CuttingSessions { get; } = new();
        public ObservableCollection<SavedCuttingSheet> CuttingSheets { get; } = new();
        public ObservableCollection<SavedCuttingPlacedPiece> CuttingPlacedPieces { get; } = new();

        private SavedCuttingSession? _selectedCuttingSession;
        public SavedCuttingSession? SelectedCuttingSession
        {
            get => _selectedCuttingSession;
            set
            {
                if (_selectedCuttingSession == value) return;
                _selectedCuttingSession = value;
                OnPropertyChanged();
                if (value != null)
                    AsyncUtility.SafeFireAndForget(() => LoadCuttingSessionDetailsAsync(value.CuttingSessionId), "Opt cutting details");
            }
        }

        private string _cuttingTraceabilityLabel = "—";
        public string CuttingTraceabilityLabel
        {
            get => _cuttingTraceabilityLabel;
            private set { if (_cuttingTraceabilityLabel == value) return; _cuttingTraceabilityLabel = value; OnPropertyChanged(); }
        }

        public ICommand LoadCuttingSessionsCommand { get; private set; }
        public ICommand OpenCuttingLayoutCommand { get; private set; }

        private void InitPhase15Optimization()
        {
            LoadCuttingSessionsCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(LoadCuttingSessionsAsync, "Opt load cutting sessions"),
                _ => true);

            OpenCuttingLayoutCommand = new RelayCommand(
                p =>
                {
                    if (p is SavedCuttingSheet sheet)
                    {
                        // Jump to 2D VIEW tab and select sheet
                        var matchingLayout = _flatSheets.FirstOrDefault(s => s.SheetIndex + 1 == sheet.SheetNo);
                        if (matchingLayout != null)
                        {
                            SelectedSheetIndex = _flatSheets.IndexOf(matchingLayout);
                            SelectedTabIndex = TabIndexTwoDView;
                        }
                    }
                },
                p => p is SavedCuttingSheet);

            AsyncUtility.SafeFireAndForget(LoadCuttingSessionsAsync, "Opt initial cutting sessions");
        }

        private async Task LoadCuttingSessionsAsync()
        {
            try
            {
                // Use BridgeJobRef as filter if set, else load recent
                string filter = BridgeJobRef;
                var sessions = await CuttingWorkflowService.GetSessionsByJobAsync(filter).ConfigureAwait(true);

                CuttingSessions.Clear();
                foreach (var s in sessions.Take(50))
                    CuttingSessions.Add(s);

                if (sessions.Count > 0)
                {
                    CuttingTraceabilityLabel = $"Loaded {sessions.Count} cutting sessions for {filter} — avg utilization {sessions.Average(s => (double)s.UtilizationPct):0.##}%";
                }
            }
            catch (Exception ex)
            {
                CuttingTraceabilityLabel = $"Load cutting sessions failed: {ex.Message}";
            }
        }

        private async Task LoadCuttingSessionDetailsAsync(string cuttingSessionId)
        {
            try
            {
                var sheets = await CuttingWorkflowService.GetSheetsBySessionAsync(cuttingSessionId).ConfigureAwait(true);
                var pieces = await CuttingWorkflowService.GetPlacedPiecesBySessionAsync(cuttingSessionId).ConfigureAwait(true);

                CuttingSheets.Clear();
                foreach (var sh in sheets) CuttingSheets.Add(sh);

                CuttingPlacedPieces.Clear();
                foreach (var p in pieces) CuttingPlacedPieces.Add(p);

                if (pieces.Count > 0)
                {
                    var first = pieces.First();
                    CuttingTraceabilityLabel = $"PI {first.JobRef} → JO {first.JobRef} → Spec {first.SpecGroupNumber}/{first.SerialNumber} {first.SpecName} → Production Piece {first.PieceId} → Cutting Session {first.CuttingSessionId} → Sheet {first.CuttingSheetId} → Placed Piece {first.PieceLabel} {first.WidthMm}×{first.HeightMm} {(first.IsRotated ? "90°" : "0°")} at {first.X},{first.Y} seq {first.CutSequence}";
                }
            }
            catch (Exception ex)
            {
                CuttingTraceabilityLabel = $"Load session details failed: {ex.Message}";
            }
        }
    }
}
