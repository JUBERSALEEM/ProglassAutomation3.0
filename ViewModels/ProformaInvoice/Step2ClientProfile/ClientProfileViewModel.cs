// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step2ClientProfile/ClientProfileViewModel.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — ALL FIXES APPLIED
//   V-01  Namespace Step2ClientProfile
//   V-02  SavedClientCards from masterdata.db
//   V-03  LoadCardsAsync()
//   V-04  LoadClientCommand
//   V-05  IsLoadingCards, HasNoSavedClients, CardsErrorMessage
//   V-06  SalesmanOptions from GetAllSalesmenAsync()
//   V-07  IsLoadingSalesmanOptions
//   V-08  _isLoadingProfile guard + LoadedProfileName + HasLoadedProfile
//   V-09  SelectedClientName published for Project filter
//   V-10  ShowAllClientsCommand + HasMoreClients + TotalClientCount
//   V-11  TrnMasked on card
//   V-12  CustomerNameValidationMessage + HasCustomerNameError
//   V-13  BUG-38 CustomerTrnValidationMessage preserved
//   V-14  RefreshCardsCommand + RefreshSalesmanCommand
//   E-FIX DateTime.HasValue removed — LastUsedAt is DateTime not DateTime?
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
namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step2ClientProfile
{
    public class ClientProfileViewModel : ViewModelBase
    {
        private readonly IMasterDataRepository _masterData = null!;

        public ClientProfileViewModel(IMasterDataRepository? masterData)
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
        private List<ClientProfileCard> _allClientCards = new();
        private bool _showingAll = false;

        // ═══════════════════════════════════════════════════════════════
        //  CLIENT IDENTITY FIELDS
        // ═══════════════════════════════════════════════════════════════

        private string _customerName = string.Empty;
        public string CustomerName
        {
            get => _customerName;
            set
            {
                if (_customerName == value) return;
                _customerName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CustomerNameValidationMessage));
                OnPropertyChanged(nameof(HasCustomerNameError));
                MarkUserEditedIfNotLoading();
                RebuildVisibleCards(); // Update suggestions as user types
            }
        }

        private string _customerTrn = string.Empty;
        public string CustomerTrn
        {
            get => _customerTrn;
            set
            {
                if (_customerTrn == value) return;
                _customerTrn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CustomerTrnValidationMessage));
                OnPropertyChanged(nameof(IsCustomerTrnValid));
                MarkUserEditedIfNotLoading();
            }
        }

        private string _customerReferenceCode = string.Empty;
        public string CustomerReferenceCode
        {
            get => _customerReferenceCode;
            set
            {
                if (_customerReferenceCode == value) return;
                _customerReferenceCode = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  TEAM MEMBER FIELDS
        // ═══════════════════════════════════════════════════════════════

        private string _salesRepresentative = string.Empty;
        public string SalesRepresentative
        {
            get => _salesRepresentative;
            set
            {
                if (_salesRepresentative == value) return;
                _salesRepresentative = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        // Phase 1: defaults to the active User Center user so
        // "Document Prepared By" fills itself on new invoices.
        private string _documentPreparedBy =
            Services.Audit.AuditService.CurrentUser?.DisplayName ?? "";
        public string DocumentPreparedBy
        {
            get => _documentPreparedBy;
            set
            {
                if (_documentPreparedBy == value) return;
                _documentPreparedBy = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  BILLING & CONTACT FIELDS
        // ═══════════════════════════════════════════════════════════════

        private string _billingAddress = string.Empty;
        public string BillingAddress
        {
            get => _billingAddress;
            set
            {
                if (_billingAddress == value) return;
                _billingAddress = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _attentionExecutiveName = string.Empty;
        public string AttentionExecutiveName
        {
            get => _attentionExecutiveName;
            set
            {
                if (_attentionExecutiveName == value) return;
                _attentionExecutiveName = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _clientContactPhone = string.Empty;
        public string ClientContactPhone
        {
            get => _clientContactPhone;
            set
            {
                if (_clientContactPhone == value) return;
                _clientContactPhone = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  V-09: SELECTED CLIENT NAME — published for Project filter
        // ═══════════════════════════════════════════════════════════════

        private string _selectedClientName = "";
        public string SelectedClientName
        {
            get => _selectedClientName;
            private set
            {
                if (_selectedClientName == value) return;
                _selectedClientName = value;
                OnPropertyChanged();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  BUG-38: UAE TRN Validation
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns null when CustomerTrn is valid or empty.
        /// Returns error message when invalid.
        /// </summary>
        public string? CustomerTrnValidationMessage
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_customerTrn))
                    return null;

                var digitsOnly = new string(
                    _customerTrn.Where(char.IsDigit).ToArray());

                if (digitsOnly.Length != 15)
                    return "Customer TRN must be exactly 15 digits.";

                if (digitsOnly != _customerTrn.Trim())
                    return "TRN should contain only digits (no spaces or letters).";

                return null;
            }
        }

        /// <summary>True when TRN is empty or valid 15-digit UAE TRN.</summary>
        public bool IsCustomerTrnValid => CustomerTrnValidationMessage == null;

        // ═══════════════════════════════════════════════════════════════
        //  V-12: REQUIRED FIELD VALIDATION
        // ═══════════════════════════════════════════════════════════════

        public string? CustomerNameValidationMessage =>
            string.IsNullOrWhiteSpace(_customerName)
                ? "Customer name is required."
                : null;

        public bool HasCustomerNameError => CustomerNameValidationMessage != null;

        // ═══════════════════════════════════════════════════════════════
        //  V-06/V-07: SALESMAN DROPDOWN
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Salesman names for editable ComboBox.
        /// Populated from masterdata.db Salesmen table.
        /// New names typed by user are auto-saved on invoice save.
        /// </summary>
        public ObservableCollection<string> SalesmanOptions { get; }
            = new ObservableCollection<string>();

        private bool _isLoadingSalesmanOptions;
        public bool IsLoadingSalesmanOptions
        {
            get => _isLoadingSalesmanOptions;
            set { _isLoadingSalesmanOptions = value; OnPropertyChanged(); }
        }

        // ═══════════════════════════════════════════════════════════════
        //  V-02: SAVED CLIENT CARDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cards loaded from masterdata.db.
        /// Max 6 visible by default.
        /// </summary>
        public ObservableCollection<ClientProfileCard> SavedClientCards { get; }
            = new ObservableCollection<ClientProfileCard>();

        private bool _isLoadingCards;
        public bool IsLoadingCards
        {
            get => _isLoadingCards;
            set { _isLoadingCards = value; OnPropertyChanged(); }
        }

        public bool HasNoSavedClients =>
            !IsLoadingCards
            && string.IsNullOrEmpty(CardsErrorMessage)
            && SavedClientCards.Count == 0
            && _allClientCards.Count == 0;

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

        public bool HasMoreClients =>
            !_showingAll && _allClientCards.Count > 6;

        public int TotalClientCount => _allClientCards.Count;

        // ═══════════════════════════════════════════════════════════════
        //  V-08: LOADED PROFILE INDICATOR
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

        public ICommand LoadClientCommand { get; private set; }
        public ICommand ShowAllClientsCommand { get; private set; }
        public ICommand RefreshCardsCommand { get; private set; }
        public ICommand RefreshSalesmanCommand { get; private set; }

        // ═══════════════════════════════════════════════════════════════
        //  CONSTRUCTOR
        // ═══════════════════════════════════════════════════════════════

        public ClientProfileViewModel()
         : this(
            App.ServiceProvider?.GetService<IMasterDataRepository>()!){ }

        // ── Shared initialization (ERR-633EEB) ─────────────────────
        // Extracted verbatim from the parameterless ctor so both it
        // and the DI-selected parameterized ctor initialize fully.
        private void InitializeCore()
        {
            LoadClientCommand = new RelayCommand(param =>
            {
                if (param is ClientProfileCard card)
                    LoadClientFromCard(card);
            });

            ShowAllClientsCommand = new RelayCommand(_ =>
            {
                _showingAll = true;
                RebuildVisibleCards();
                OnPropertyChanged(nameof(HasMoreClients));
            });

            RefreshCardsCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(LoadCardsAsync, "PI Step2"));

            RefreshSalesmanCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(LoadSalesmanOptionsAsync, "PI Step2"));
        }

        // ═══════════════════════════════════════════════════════════════
        //  V-03: LOAD CLIENT CARDS FROM MASTERDATA.DB
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads saved clients from masterdata.db.
        /// E-FIX: LastUsedAt is DateTime (not DateTime?) — use default check.
        /// </summary>
        public async Task LoadCardsAsync()
        {
            if (_masterData == null) return;

            IsLoadingCards = true;
            CardsErrorMessage = "";
            _showingAll = false;

            try
            {
                var clients = await _masterData.GetAllClientsAsync();

                _allClientCards = clients
                    .Select(c => new ClientProfileCard
                    {
                        Id = c.Id,
                        CustomerName = c.CustomerName ?? "",
                        CustomerTrn = c.CustomerTrn ?? "",
                        CustomerReferenceCode = c.CustomerReferenceCode ?? "",
                        SalesRepresentative = c.SalesRepresentative ?? "",
                        DocumentPreparedBy = c.DocumentPreparedBy ?? "",
                        BillingAddress = c.BillingAddress ?? "",
                        AttentionExecutiveName = c.AttentionExecutiveName ?? "",
                        ClientContactPhone = c.ClientContactPhone ?? "",
                        UsageCount = c.UsageCount,
                        // E-FIX: DateTime is non-nullable — check against default
                        LastUsed = c.LastUsedAt != default(DateTime)
                                                    ? c.LastUsedAt.ToString("dd MMM yyyy")
                                                    : ""
                    })
                    .ToList();

                RebuildVisibleCards();
            }
            catch (Exception ex)
            {
                CardsErrorMessage =
                    $"Could not load saved clients: {ExceptionMessages.Deep(ex)}";
            }
            finally
            {
                IsLoadingCards = false;
                OnPropertyChanged(nameof(HasNoSavedClients));
                OnPropertyChanged(nameof(HasMoreClients));
                OnPropertyChanged(nameof(TotalClientCount));
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  V-06: LOAD SALESMAN OPTIONS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads salesman names for the editable ComboBox.
        /// Silent fail — dropdown degrades to manual entry.
        /// </summary>
        public async Task LoadSalesmanOptionsAsync()
        {
            if (_masterData == null) return;

            IsLoadingSalesmanOptions = true;

            // BUG (fields blank on reopen): SalesRepresentative binds to an
            // EDITABLE ComboBox whose ItemsSource is SalesmanOptions. When a
            // saved invoice is reopened the wizard first RESTORES the value,
            // then calls this method — and clearing/repopulating an editable
            // ComboBox's ItemsSource makes WPF reset its edit text to empty
            // and push that empty string back through the TwoWay binding,
            // silently wiping the restored salesman (which the next save then
            // persists as blank). Capture the true value before the rebuild
            // and restore it after, so the dropdown never eats the data.
            string preserved = _salesRepresentative ?? string.Empty;

            try
            {
                var salesmen = await _masterData.GetAllSalesmenAsync();

                SalesmanOptions.Clear();
                foreach (var s in salesmen)
                {
                    if (!string.IsNullOrWhiteSpace(s.Name))
                        SalesmanOptions.Add(s.Name);
                }

                // Make sure the current value is offered by the list so the
                // editable ComboBox shows it as a proper item (covers a
                // salesman typed on this invoice but not yet in master data).
                if (!string.IsNullOrWhiteSpace(preserved) &&
                    !SalesmanOptions.Any(o =>
                        string.Equals(o, preserved, StringComparison.OrdinalIgnoreCase)))
                {
                    SalesmanOptions.Insert(0, preserved);
                }
            }
            catch (System.Exception ex)
            {
                // Silent — user can still type salesman name manually
                ProGlassAutomation.Services.Logging.AppLogService.Debug("[ClientProfileViewModel] Salesman options load failed; manual entry still works: " + ExceptionMessages.Deep(ex));
            }
            finally
            {
                // Restore the value the ItemsSource swap may have cleared.
                // Reassigning through the property re-raises PropertyChanged
                // so the ComboBox text re-syncs to the correct salesman.
                if (!string.IsNullOrWhiteSpace(preserved) &&
                    !string.Equals(_salesRepresentative, preserved, StringComparison.Ordinal))
                {
                    SalesRepresentative = preserved;
                }

                IsLoadingSalesmanOptions = false;
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PRIVATE HELPERS
        // ═══════════════════════════════════════════════════════════════

        public void AdoptExternalCustomerSelection(string? customerName)
        {
            SelectedClientName = customerName ?? string.Empty;
        }

        public void ClearExternalCustomerSelection()
        {
            SelectedClientName = string.Empty;
        }

        private void RebuildVisibleCards()
        {
            SavedClientCards.Clear();

            // FILTER: Only show cards that match the current CustomerName text
            // If CustomerName is empty, show nothing (user hasn't started typing)
            // If CustomerName has text, show matching suggestions
            var filtered = string.IsNullOrWhiteSpace(CustomerName)
                ? new List<ClientProfileCard>() // Empty = no suggestions
                : _allClientCards
                    .Where(c => c.CustomerName.IndexOf(CustomerName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            var visible = _showingAll
                ? filtered
                : filtered.Take(6).ToList();

            foreach (var card in visible)
                SavedClientCards.Add(card);

            OnPropertyChanged(nameof(HasNoSavedClients));
            OnPropertyChanged(nameof(HasMoreClients));
        }

        /// <summary>
        /// V-04/V-08: Fills all client fields from selected card.
        /// V-09: Sets SelectedClientName for Project filter.
        /// </summary>
        private void LoadClientFromCard(ClientProfileCard card)
        {
            _isLoadingProfile = true;
            try
            {
                CustomerName = card.CustomerName;
                CustomerTrn = card.CustomerTrn;
                CustomerReferenceCode = card.CustomerReferenceCode;
                SalesRepresentative = card.SalesRepresentative;
                DocumentPreparedBy = card.DocumentPreparedBy;
                BillingAddress = card.BillingAddress;
                AttentionExecutiveName = card.AttentionExecutiveName;
                ClientContactPhone = card.ClientContactPhone;
                LoadedProfileName = card.CustomerName;
                SelectedClientName = card.CustomerName;
            }
            finally
            {
                _isLoadingProfile = false;
            }
        }

        /// <summary>
        /// BUG-37 pattern: Clears LoadedProfileName on manual edit.
        /// Does NOT clear SelectedClientName — project filter stays active.
        /// </summary>
        private void MarkUserEditedIfNotLoading()
        {
            if (_isLoadingProfile) return;
            if (!string.IsNullOrEmpty(_loadedProfileName))
                LoadedProfileName = "";
        }

        
    }
}
