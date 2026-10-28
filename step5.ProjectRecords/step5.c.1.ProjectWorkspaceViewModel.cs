// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.c.1.ProjectWorkspaceViewModel.cs
// MODULE: step5 — project register, editor and the inquiry link (presentation).
//
// Presentation state only. Every rule lives in step5.a, every query and write in
// step5.b. There is no delete command anywhere in this module: a project is
// closed, never removed, exactly as a client is deactivated rather than deleted.
//
// The workspace has three visible parts, and the model keeps them honest:
//   • the register, with status chips that carry their own counts (the register
//     rule: a filter states the scope it would show);
//   • the editor, which can also be opened for a new project, and shows the
//     number the next project would receive;
//   • the inquiry link, which is only enabled for a saved project — an unsaved
//     project has no id to link to, and offering the picker would be a command
//     that cannot work.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Projects;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;

namespace ProGlassAutomation.ViewModels.Projects
{
    /// <summary>One status chip on the register, with the count it would show.</summary>
    public sealed class ProjectStatusTab : ViewModelBase
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

        public string LabelWithCount => $"{Label} · {Count.ToString("N0", CultureInfo.CurrentCulture)}";
    }

    /// <summary>The Project Records workspace.</summary>
    public sealed partial class ProjectWorkspaceViewModel : ViewModelBase, IDisposable
    {
        private readonly IProjectRepository _repository;
        private readonly bool _ownsRepository;
        private CancellationTokenSource? _loadCts;
        private bool _initialized;

        private string _searchText = "";
        private string _selectedStatus = "All";
        private ProjectListRow? _selectedRow;
        private bool _isEditorOpen;
        private bool _isNewProject;
        private int _editingId;

        private string _editNumber = "";
        private string _editName = "";
        private string _editClientName = "";
        private string _editLocation = "";
        private string _editDescription = "";
        private ProjectClientChoice? _editClient;

        private string _inquirySearch = "";
        private InquiryCandidateRow? _selectedLinked;
        private InquiryCandidateRow? _selectedCandidate;

        /// <summary>Every row the last read returned, before the status chip filters it.</summary>
        private readonly List<ProjectListRow> _allRowsCache = new();

        private string _errorMessage = "";
        private string _statusMessage = "";
        private bool _isBusy;

        public ProjectWorkspaceViewModel() : this(new ProjectRepository(), ownsRepository: true) { }

        /// <summary>Test seam: the checks drive the real view model over a fake port.</summary>
        public ProjectWorkspaceViewModel(IProjectRepository repository, bool ownsRepository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _ownsRepository = ownsRepository;

            StatusTabs = new ObservableCollection<ProjectStatusTab>
            {
                new() { Key = "All", Label = "All", IsSelected = true },
                new() { Key = ProjectStatus.Active, Label = ProjectStatus.Active },
                new() { Key = ProjectStatus.Closed, Label = ProjectStatus.Closed }
            };

            NewProjectCommand = new DelegateCommand(() => _ = OpenEditorForNewAsync());
            EditSelectedCommand = new DelegateCommand(() => _ = OpenEditorForSelectedAsync());
            SaveCommand = new DelegateCommand(() => _ = SaveAsync());
            CancelCommand = new DelegateCommand(CloseEditor);
            ToggleStatusCommand = new DelegateCommand(() => _ = ToggleStatusAsync());
            RefreshCommand = new DelegateCommand(() => _ = ReloadAsync());
            SelectStatusTabCommand = new DelegateCommand<ProjectStatusTab>(tab => { if (tab != null) SelectStatusTab(tab); });
            ClearSearchCommand = new DelegateCommand(() => SearchText = "");
            ClearClientCommand = new DelegateCommand(() => { EditClient = null; EditClientName = ""; });
            AttachInquiryCommand = new DelegateCommand(() => _ = AttachAsync());
            DetachInquiryCommand = new DelegateCommand(() => _ = DetachAsync());
        }

        // ── Collections and commands ────────────────────────────────────

        public ObservableCollection<ProjectStatusTab> StatusTabs { get; }
        public ObservableCollection<ProjectListRow> Rows { get; } = new();
        public ObservableCollection<ProjectClientChoice> Clients { get; } = new();
        public ObservableCollection<InquiryCandidateRow> LinkedInquiries { get; } = new();
        public ObservableCollection<InquiryCandidateRow> CandidateInquiries { get; } = new();

        public ICommand NewProjectCommand { get; }
        public ICommand EditSelectedCommand { get; }
        public ICommand SaveCommand { get; }
        public ICommand CancelCommand { get; }
        public ICommand ToggleStatusCommand { get; }
        public ICommand RefreshCommand { get; }
        public ICommand SelectStatusTabCommand { get; }
        public ICommand ClearSearchCommand { get; }
        public ICommand ClearClientCommand { get; }
        public ICommand AttachInquiryCommand { get; }
        public ICommand DetachInquiryCommand { get; }

        // ── Register state ──────────────────────────────────────────────

        public string SearchText
        {
            get => _searchText;
            set { if (SetField(ref _searchText, value ?? "")) { OnPropertyChanged(nameof(HasSearch)); _ = ReloadAsync(); } }
        }

        public bool HasSearch => !string.IsNullOrWhiteSpace(_searchText);

        public string SelectedStatus
        {
            get => _selectedStatus;
            private set { if (SetField(ref _selectedStatus, value)) OnPropertyChanged(nameof(RegisterSummary)); }
        }

        public ProjectListRow? SelectedRow
        {
            get => _selectedRow;
            set
            {
                if (!SetField(ref _selectedRow, value)) return;
                OnPropertyChanged(nameof(HasSelection));
                OnPropertyChanged(nameof(SelectedToggleLabel));
                OnPropertyChanged(nameof(CanEditSelected));
                _ = LoadLinkedInquiriesAsync();
            }
        }

        public bool HasSelection => _selectedRow != null;
        public bool CanEditSelected => _selectedRow != null && !IsEditorOpen;

        public string SelectedToggleLabel =>
            _selectedRow == null ? "Close project" : (_selectedRow.IsClosed ? "Re-open project" : "Close project");

        /// <summary>The sentence above the register: what is shown and why.</summary>
        public string RegisterSummary
        {
            get
            {
                string scope = SelectedStatus == "All" ? "all statuses" : SelectedStatus.ToLowerInvariant();
                string searching = HasSearch ? $" matching “{SearchText.Trim()}”" : "";
                return $"{Rows.Count} project(s) · {scope}{searching}";
            }
        }

        // ── Editor state ────────────────────────────────────────────────

        public bool IsEditorOpen
        {
            get => _isEditorOpen;
            private set
            {
                if (!SetField(ref _isEditorOpen, value)) return;
                OnPropertyChanged(nameof(IsRegisterOpen));
                OnPropertyChanged(nameof(EditorHeading));
                OnPropertyChanged(nameof(CanEditSelected));
                OnPropertyChanged(nameof(CanUseInquiryLink));
            }
        }

        public bool IsRegisterOpen => !_isEditorOpen;

        public bool IsNewProject => _isNewProject;

        public string EditorHeading => _isNewProject ? "New project" : "Edit project";

        public string EditNumber
        {
            get => _editNumber;
            set => SetField(ref _editNumber, value ?? "");
        }

        public string EditName
        {
            get => _editName;
            set => SetField(ref _editName, value ?? "");
        }

        public string EditClientName
        {
            get => _editClientName;
            set => SetField(ref _editClientName, value ?? "");
        }

        public string EditLocation
        {
            get => _editLocation;
            set => SetField(ref _editLocation, value ?? "");
        }

        public string EditDescription
        {
            get => _editDescription;
            set => SetField(ref _editDescription, value ?? "");
        }

        /// <summary>Null means "no client recorded", which is a legal project.</summary>
        public ProjectClientChoice? EditClient
        {
            get => _editClient;
            set
            {
                if (!SetField(ref _editClient, value)) return;
                // Choosing a client fills the snapshot name; clearing the choice
                // leaves whatever name was typed, so a one-off customer survives.
                if (_editClient != null) EditClientName = _editClient.DisplayName;
            }
        }

        // ── Inquiry link state ──────────────────────────────────────────

        /// <summary>
        /// The link exists only for a saved project. A new, unsaved project has no
        /// id, so the picker is disabled rather than failing when used.
        /// </summary>
        public bool CanUseInquiryLink =>
            !_isEditorOpen && _selectedRow != null && _selectedRow.Id > 0;

        public string InquirySearch
        {
            get => _inquirySearch;
            set { if (SetField(ref _inquirySearch, value ?? "")) _ = LoadCandidatesAsync(); }
        }

        public InquiryCandidateRow? SelectedLinked
        {
            get => _selectedLinked;
            set { if (SetField(ref _selectedLinked, value)) OnPropertyChanged(nameof(CanDetach)); }
        }

        public InquiryCandidateRow? SelectedCandidate
        {
            get => _selectedCandidate;
            set { if (SetField(ref _selectedCandidate, value)) OnPropertyChanged(nameof(CanAttach)); }
        }

        public bool CanAttach => CanUseInquiryLink && _selectedCandidate != null;
        public bool CanDetach => CanUseInquiryLink && _selectedLinked != null;

        // ── Surfaces ────────────────────────────────────────────────────

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasError)); }
        }

        public bool HasError => !string.IsNullOrWhiteSpace(_errorMessage);

        public string StatusMessage
        {
            get => _statusMessage;
            private set { if (SetField(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatus)); }
        }

        public bool HasStatus => !string.IsNullOrWhiteSpace(_statusMessage);

        public bool IsBusy { get => _isBusy; private set => SetField(ref _isBusy, value); }
    }
}
