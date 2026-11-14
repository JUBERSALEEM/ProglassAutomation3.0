using System.Collections.Generic;
using System.Linq;
using System.Windows.Input;
using ProGlassAutomation.Commands;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel
    {
        private bool CanUseJobInputs => !IsDisposed && !IsRunning && !IsHeadlessHost;
        private RelayCommand? _importPaneSizesCommand;
        private RelayCommand? _openPieceSizesCommand;
        private RelayCommand? _openImportedSizesCommand;

        /// <summary>Double-click a pane to import that pane through the same path as ADD SELECTED PANES.</summary>
        public ICommand ImportPaneSizesCommand => _importPaneSizesCommand ??= new RelayCommand(parameter =>
        {
            if (parameter is not BuildUpLayerVm pane || !CanUseJobInputs || !PaneChecklist.Contains(pane)) return;
            ImportPaneLayers(new List<BuildUpLayerVm> { pane });
            var imported = Pieces.FirstOrDefault(piece => piece.JobOrderId == pane.JobOrderId
                && piece.SpecGroupNumber == pane.SpecGroupNumber && piece.Position == pane.Position);
            if (imported != null) OpenPieceSizes(imported);
        }, parameter => CanUseJobInputs && parameter is BuildUpLayerVm pane
            && pane.IsPane && pane.Pieces.Count > 0 && PaneChecklist.Contains(pane));

        /// <summary>
        /// Cutting-list rows already ARE the imported engine inputs. Double-click
        /// opens their glass group in SIZES; copying them again would double the job.
        /// </summary>
        public ICommand OpenPieceSizesCommand => _openPieceSizesCommand ??= new RelayCommand(parameter =>
        {
            if (parameter is PieceEntryVm piece) OpenPieceSizes(piece);
        }, parameter => !IsDisposed && !IsRunning && parameter is PieceEntryVm piece && Pieces.Contains(piece));

        public ICommand OpenImportedSizesCommand => _openImportedSizesCommand ??= new RelayCommand(_ =>
        {
            if (IsDisposed || IsRunning || !HasPieces) return;
            _selectedCuttingGroup = AllCuttingGroups;
            RefreshDerived();
            SelectedTabIndex = TabIndexSizes;
        }, _ => !IsDisposed && !IsRunning && HasPieces);

        private void OpenPieceSizes(PieceEntryVm piece)
        {
            if (IsDisposed || IsRunning || !Pieces.Contains(piece)) return;
            RefreshDerived();
            SelectedCuttingGroup = (piece.GlassRef ?? string.Empty).Trim();
            SelectedTabIndex = TabIndexSizes;
        }
    }
}
