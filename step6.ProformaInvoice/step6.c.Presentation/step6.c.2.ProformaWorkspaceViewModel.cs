// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.Presentation/step6.c.2.ProformaWorkspaceViewModel.cs
// MODULE: step6 — the register half of the proforma invoice screen.
//
// Presentation only. Every rule lives in step6.a, every query and write in
// step6.b, and the stage engine in step6.c.1. This file decides what the
// register shows, which filter is on, and when the editor opens; it contains no
// pricing, no totals and no SQL.
//
// The register follows the product's list pattern (docs/10): one scope sentence,
// status chips that carry their own counts, a search box and a command bar on
// one row. The editor is a separate part (step6.c.3) because a page that holds
// both would be a god file.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Services.Proforma;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;

namespace ProGlassAutomation.ViewModels.Proforma
{
    /// <summary>One status chip: the filter, and how many documents it would show.</summary>
    public sealed class ProformaStatusChip : ViewModelBase
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";

        private int _count;
        public int Count
        {
            get => _count;
            set { if (SetField(ref _count, value)) OnPropertyChanged(nameof(LabelWithCount)); }
        }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        public string LabelWithCount =>
            Label + " · " + Count.ToString("N0", CultureInfo.CurrentCulture);
    }

    /// <summary>One stage marker in the editor's rail.</summary>
    public sealed class ProformaStageChip : ViewModelBase
    {
        public string Key { get; init; } = "";
        public string Label { get; init; } = "";
        public int Number { get; init; }

        private bool _isSelected;
        public bool IsSelected { get => _isSelected; set => SetField(ref _isSelected, value); }

        private bool _isComplete;
        public bool IsComplete
        {
            get => _isComplete;
            set { if (SetField(ref _isComplete, value)) OnPropertyChanged(nameof(Marker)); }
        }

        private int _problemCount;
        public int ProblemCount
        {
            get => _problemCount;
            set { if (SetField(ref _problemCount, value)) OnPropertyChanged(nameof(Marker)); }
        }

        /// <summary>What the rail prints beside the stage name.</summary>
        public string Marker => IsComplete ? "ready" : ProblemCount == 0 ? "open" : ProblemCount + " to fix";

        public string Caption => Number.ToString(CultureInfo.CurrentCulture) + " · " + Label;
    }

    /// <summary>The Proforma Invoice workspace: the register, and the editor it opens.</summary>
    public sealed partial class ProformaWorkspaceViewModel : ViewModelBase, IDisposable
    {
        private readonly ProformaInvoiceWorkspace _workspace;
        private readonly IProformaInvoiceRepository _repository;
        private readonly bool _ownsWorkspace;

        private readonly List<ProformaInvoiceListRow> _allRows = new();
        private CancellationTokenSource? _loadCts;
        private bool _initialized;

        private string _searchText = "";
        private string _selectedStatusKey = "All";
        private ProformaInvoiceListRow? _selectedRow;
        private bool _isEditorOpen;
        private bool _isBusy;
        private string _errorMessage = "";
        private string _statusMessage = "";

        public ProformaWorkspaceViewModel()
            : this(null, null) { }

        /// <summary>
        /// The test/design seam every module uses: production callers pass nothing.
        /// </summary>
        public ProformaWorkspaceViewModel(
            ProformaInvoiceWorkspace? workspace, IProformaInvoiceRepository? repository)
        {
            _ownsWorkspace = workspace == null;
            _repository = repository ?? new ProformaInvoiceRepository();
            _workspace = workspace ?? new ProformaInvoiceWorkspace(_repository);

            StatusChips = new ObservableCollection<ProformaStatusChip>(BuildStatusChips());
            Rows = new ObservableCollection<ProformaInvoiceListRow>();

            SelectStatusChipCommand = new DelegateCommand<ProformaStatusChip>(
                chip => { if (chip != null) SelectStatusChip(chip); });
            NewCommand = new DelegateCommand(() => _ = NewInvoiceAsync());
            OpenSelectedCommand = new DelegateCommand(() => _ = OpenSelectedAsync(), () => CanOpenSelected);
            RefreshCommand = new DelegateCommand(() => _ = LoadRegisterAsync());
            CloseEditorCommand = new DelegateCommand(CloseEditor);
            SaveCommand = new DelegateCommand(() => _ = SaveAsync(), () => CanEditBody);
            SendCommand = new DelegateCommand(
                () => _ = ChangeStatusAsync(ProformaInvoiceStatus.Sent), () => CanSend);
            ApproveCommand = new DelegateCommand(
                () => _ = ChangeStatusAsync(ProformaInvoiceStatus.Approved), () => CanApprove);
            RejectCommand = new DelegateCommand(
                () => _ = ChangeStatusAsync(ProformaInvoiceStatus.Rejected), () => CanReject);
            CancelDocumentCommand = new DelegateCommand(
                () => _ = ChangeStatusAsync(ProformaInvoiceStatus.Cancelled), () => CanCancelDocument);
            DuplicateCommand = new DelegateCommand(() => _ = DuplicateAsync(), () => IsPersisted);
            SelectStageCommand = new DelegateCommand<ProformaStageChip>(
                chip => { if (chip != null) SelectStage(chip.Key); });

            BuildEditorCommands();
        }

        // ── Register state ──────────────────────────────────────────────
        public ObservableCollection<ProformaStatusChip> StatusChips { get; }

        public ObservableCollection<ProformaInvoiceListRow> Rows { get; }

        public string SearchText
        {
            get => _searchText;
            set
            {
                if (!SetField(ref _searchText, value ?? "")) return;
                OnPropertyChanged(nameof(HasSearch));
                ApplyFilter();
            }
        }

        public bool HasSearch => _searchText.Trim().Length > 0;

        public ProformaInvoiceListRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetField(ref _selectedRow, value)) return;
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(CanOpenSelected));
            }
        }

        public bool HasSelection => _selectedRow != null;

        public bool CanOpenSelected => _selectedRow != null;

        public bool IsEditorOpen
        {
            get => _isEditorOpen;
            private set
            {
                if (!SetField(ref _isEditorOpen, value)) return;
                OnPropertyChanged(nameof(IsRegisterOpen));
            }
        }

        public bool IsRegisterOpen => !_isEditorOpen;

        public bool IsBusy
        {
            get => _isBusy;
            private set { if (SetField(ref _isBusy, value)) OnPropertyChanged(nameof(IsIdle)); }
        }

        public bool IsIdle => !_isBusy;

        public bool HasError => _errorMessage.Length > 0;
        public string ErrorMessage => _errorMessage;
        public bool HasStatus => _statusMessage.Length > 0;
        public string StatusMessage => _statusMessage;

        /// <summary>"7 invoices · all statuses · newest first" — the scope sentence.</summary>
        public string RegisterSummary
        {
            get
            {
                string scope = _selectedStatusKey == "All" ? "all statuses" : _selectedStatusKey.ToLowerInvariant();
                string count = Rows.Count == 1 ? "1 invoice" : Rows.Count.ToString("N0", CultureInfo.CurrentCulture) + " invoices";
                string search = HasSearch ? " matching \u201c" + _searchText.Trim() + "\u201d" : "";
                return count + search + " · " + scope + " · newest first";
            }
        }

        public bool IsEmptyRegister => Rows.Count == 0 && !_isBusy;

        // ── Commands ────────────────────────────────────────────────────
        public ICommand SelectStatusChipCommand { get; }
        public ICommand NewCommand { get; }
        public ICommand OpenSelectedCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand CloseEditorCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand SendCommand { get; }
        public ICommand ApproveCommand { get; }
        public ICommand RejectCommand { get; }
        public ICommand CancelDocumentCommand { get; }
        public ICommand DuplicateCommand { get; }
        public ICommand SelectStageCommand { get; }
    }
}
