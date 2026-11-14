// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step3ProjectSpecs/ProjectSpecsViewModel.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — PROJECT REFERENCE FIELD ADDED
//   4 precise additions:
//     1. ProjectReference property with change notification
//     2. ProjectProfileCard.ProjectReference field + ReferencePreview
//     3. LoadCardsAsync maps p.ProjectReference to card
//     4. LoadProjectFromCard sets ProjectReference from card
//   All P-01..P-10 features + client filter logic preserved.
// ═══════════════════════════════════════════════════════════════════════
using ProGlassAutomation.Commands;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.MasterData;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;

using ProGlassAutomation.Services.MasterData.Repository;
using Microsoft.Extensions.DependencyInjection;
namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step3ProjectSpecs
{
    public class ProjectSpecsViewModel : ViewModelBase
    {
        private readonly IMasterDataRepository _masterData = null!;

        public ProjectSpecsViewModel(IMasterDataRepository? masterData)
        {
            _masterData = masterData!;
        
            // ERR-633EEB: run shared initialization on EVERY construction
            // path. DI selects this (greediest) ctor, so without this call the
            // parameterless ctor's setup never ran and commands stayed null.
            InitializeCore();
        }

        // ── Guard while populating from card ──────────────────────────
        private bool _isLoadingProfile;

        // ── Internal full list for View all toggle ────────────────────
        private List<ProjectProfileCard> _allProjectCards = new();
        private bool _showingAll = false;

        // ── Current client filter ─────────────────────────────────────
        private string _activeClientFilter = "";

        // ═══════════════════════════════════════════════════════════════
        //  PROJECT FIELDS
        // ═══════════════════════════════════════════════════════════════

        private string _projectIdentifierName = string.Empty;
        public string ProjectIdentifierName
        {
            get => _projectIdentifierName;
            set
            {
                if (_projectIdentifierName == value) return;
                _projectIdentifierName = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
                RebuildVisibleCards(); // Update suggestions as user types
            }
        }

        private string _projectContractNo = string.Empty;
        public string ProjectContractNo
        {
            get => _projectContractNo;
            set
            {
                if (_projectContractNo == value) return;
                _projectContractNo = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        // CHAT 15: NEW field
        private string _projectReference = string.Empty;
        public string ProjectReference
        {
            get => _projectReference;
            set
            {
                if (_projectReference == value) return;
                _projectReference = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _siteDeliveryLocation = string.Empty;
        public string SiteDeliveryLocation
        {
            get => _siteDeliveryLocation;
            set
            {
                if (_siteDeliveryLocation == value) return;
                _siteDeliveryLocation = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _lpoNumber = string.Empty;
        public string LpoNumber
        {
            get => _lpoNumber;
            set
            {
                if (_lpoNumber == value) return;
                _lpoNumber = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  P-06: CLIENT FILTER INDICATOR
        // ═══════════════════════════════════════════════════════════════

        private string _filteredClientName = "";
        public string FilteredClientName
        {
            get => _filteredClientName;
            private set
            {
                _filteredClientName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(IsClientFilterActive));
                OnPropertyChanged(nameof(FilterBannerText));
            }
        }

        public bool IsClientFilterActive =>
            !string.IsNullOrEmpty(FilteredClientName);

        public string FilterBannerText =>
            IsClientFilterActive
                ? $"Showing projects for: {FilteredClientName}"
                : "All saved projects";

        // ═══════════════════════════════════════════════════════════════
        //  P-02: SAVED PROJECT CARDS
        // ═══════════════════════════════════════════════════════════════

        public ObservableCollection<ProjectProfileCard> SavedProjectCards { get; }
            = new ObservableCollection<ProjectProfileCard>();

        private bool _isLoadingCards;
        public bool IsLoadingCards
        {
            get => _isLoadingCards;
            set { _isLoadingCards = value; OnPropertyChanged(); }
        }

        public bool HasNoSavedProjects =>
            !IsLoadingCards
            && string.IsNullOrEmpty(CardsErrorMessage)
            && SavedProjectCards.Count == 0
            && _allProjectCards.Count == 0;

        private string _cardsErrorMessage = "";
        public string CardsErrorMessage
        {
            get => _cardsErrorMessage;
            set
            {
                _cardsErrorMessage = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasCardsError));
            }
        }
        public bool HasCardsError => !string.IsNullOrEmpty(CardsErrorMessage);

        public bool HasMoreProjects =>
            !_showingAll && _allProjectCards.Count > 6;

        public int TotalProjectCount => _allProjectCards.Count;

        // ═══════════════════════════════════════════════════════════════
        //  P-09: LOADED PROFILE INDICATOR
        // ═══════════════════════════════════════════════════════════════

        private string _loadedProfileName = "";
        public string LoadedProfileName
        {
            get => _loadedProfileName;
            set
            {
                _loadedProfileName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLoadedProfile));
            }
        }

        public bool HasLoadedProfile => !string.IsNullOrEmpty(LoadedProfileName);

        // ═══════════════════════════════════════════════════════════════
        //  COMMANDS
        // ═══════════════════════════════════════════════════════════════

        public ICommand LoadProjectCommand { get; private set; }
        public ICommand ShowAllProjectsCommand { get; private set; }
        public ICommand RefreshCardsCommand { get; private set; }
        public ICommand ClearClientFilterCommand { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════

        public ProjectSpecsViewModel()
         : this(
            App.ServiceProvider?.GetService<IMasterDataRepository>()!){ }

        // ── Shared initialization (ERR-633EEB) ─────────────────────
        // Extracted verbatim from the parameterless ctor so both it
        // and the DI-selected parameterized ctor initialize fully.
        private void InitializeCore()
        {
            LoadProjectCommand = new RelayCommand(param =>
            {
                if (param is ProjectProfileCard card)
                    LoadProjectFromCard(card);
            });

            ShowAllProjectsCommand = new RelayCommand(_ =>
            {
                _showingAll = true;
                RebuildVisibleCards();
                OnPropertyChanged(nameof(HasMoreProjects));
            });

            RefreshCardsCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(LoadCardsAsync, "PI Step3"));

            ClearClientFilterCommand = new RelayCommand(_ =>
            {
                SetClientFilter("");
            });
        }

        // ═══════════════════════════════════════════════════════════════
        //  P-03: LOAD PROJECT CARDS FROM MASTERDATA.DB
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads saved projects from masterdata.db.
        /// Client-matching projects float to top when filter active.
        /// E-FIX: LastUsedAt is DateTime — check against default.
        /// CHAT 15: ProjectReference mapped into card.
        /// </summary>
        public async Task LoadCardsAsync()
        {
            if (_masterData == null) return;

            IsLoadingCards = true;
            CardsErrorMessage = "";
            _showingAll = false;

            try
            {
                var projects = await _masterData.GetAllProjectsAsync();

                var hasFilter = !string.IsNullOrWhiteSpace(_activeClientFilter);

                _allProjectCards = projects
                    .Select(p => new ProjectProfileCard
                    {
                        Id = p.Id,
                        ProjectIdentifierName = p.ProjectIdentifierName ?? "",
                        ProjectContractNo = p.ProjectContractNo ?? "",
                        ProjectReference = p.ProjectReference ?? "",  // CHAT 15: NEW
                        SiteDeliveryLocation = p.SiteDeliveryLocation ?? "",
                        LpoNumber = p.LpoNumber ?? "",
                        CustomerName = p.CustomerName ?? "",
                        UsageCount = p.UsageCount,
                        LastUsed = p.LastUsedAt != default(DateTime)
                                                    ? p.LastUsedAt.ToString("dd MMM yyyy")
                                                    : ""
                    })
                    .OrderByDescending(p =>
                        hasFilter &&
                        p.CustomerName.Equals(
                            _activeClientFilter,
                            StringComparison.OrdinalIgnoreCase))
                    .ThenByDescending(p => p.UsageCount)
                    .ToList();

                RebuildVisibleCards();
            }
            catch (Exception ex)
            {
                CardsErrorMessage =
                    $"Could not load saved projects: {ExceptionMessages.Deep(ex)}";
            }
            finally
            {
                IsLoadingCards = false;
                OnPropertyChanged(nameof(HasNoSavedProjects));
                OnPropertyChanged(nameof(HasMoreProjects));
                OnPropertyChanged(nameof(TotalProjectCount));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  P-07: CLIENT FILTER
        // ═══════════════════════════════════════════════════════════════

        public void SetClientFilter(string clientName)
        {
            _activeClientFilter = clientName ?? "";
            FilteredClientName = _activeClientFilter;
            _showingAll = false;
            AsyncUtility.SafeFireAndForget(LoadCardsAsync, "PI Step3");
        }

        // ═══════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        private void RebuildVisibleCards()
        {
            SavedProjectCards.Clear();

            // FILTER: Only show cards that match the current ProjectIdentifierName text
            // If ProjectIdentifierName is empty, show nothing (user hasn't started typing)
            // If ProjectIdentifierName has text, show matching suggestions
            var filtered = string.IsNullOrWhiteSpace(ProjectIdentifierName)
                ? new List<ProjectProfileCard>() // Empty = no suggestions
                : _allProjectCards
                    .Where(c => c.ProjectIdentifierName.IndexOf(ProjectIdentifierName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            var visible = _showingAll
                ? filtered
                : filtered.Take(6).ToList();

            foreach (var card in visible)
                SavedProjectCards.Add(card);

            OnPropertyChanged(nameof(HasNoSavedProjects));
            OnPropertyChanged(nameof(HasMoreProjects));
        }

        /// <summary>
        /// P-04/P-09: Fills all project fields from selected card.
        /// CHAT 15: Also fills ProjectReference.
        /// </summary>
        private void LoadProjectFromCard(ProjectProfileCard card)
        {
            _isLoadingProfile = true;
            try
            {
                ProjectIdentifierName = card.ProjectIdentifierName;
                ProjectContractNo = card.ProjectContractNo;
                ProjectReference = card.ProjectReference;   // CHAT 15: NEW
                SiteDeliveryLocation = card.SiteDeliveryLocation;
                LpoNumber = card.LpoNumber;
                LoadedProfileName = card.DisplayProjectName;
            }
            finally
            {
                _isLoadingProfile = false;
            }
        }

        private void MarkUserEditedIfNotLoading()
        {
            if (_isLoadingProfile) return;
            if (!string.IsNullOrEmpty(_loadedProfileName))
                LoadedProfileName = "";
        }

        
    }
}
