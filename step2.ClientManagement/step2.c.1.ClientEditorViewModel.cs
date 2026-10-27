// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.c.1.ClientEditorViewModel.cs
// MODULE: step2 — client editor (create / view / edit).
//
// Presentation state only: field values, per-field validation messages, the
// duplicate warning and the save/cancel commands. All rules come from
// ClientRules / ClientDuplicatePolicy, all persistence from IClientRepository.
//
// Duplicate handling is deliberately a warning the operator may override:
// saving a resembling client is a second, explicit save — never a silent
// merge and never an automatic rejection.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Clients;
using ProGlassAutomation.Services.Clients;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.Clients
{
    /// <summary>Edits one client; new or existing.</summary>
    public sealed class ClientEditorViewModel : ViewModelBase
    {
        private readonly IClientRepository _repository;
        private readonly int _clientId;
        private readonly bool _isNew;

        private string _clientName = "";
        private string _attentionName = "";
        private string _phone = "";
        private string _email = "";
        private string _trn = "";
        private string _address = "";
        private string _notes = "";
        private bool _isActive = true;
        private int _representativeId;

        private string _clientNameError = "";
        private string _phoneError = "";
        private string _emailError = "";
        private string _trnError = "";
        private string _attentionNameError = "";
        private string _addressError = "";
        private string _representativeError = "";

        private string _duplicateWarning = "";
        private bool _duplicateWarningAcknowledged;
        private string _statusMessage = "";
        private string _errorMessage = "";
        private bool _isBusy;

        public ClientEditorViewModel(
            IClientRepository repository,
            ClientDraft draft,
            int clientId = 0,
            IReadOnlyList<ClientRepresentative>? representatives = null)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));
            _clientId = clientId;
            _isNew = clientId <= 0;

            if (draft != null)
            {
                _clientName = draft.ClientName;
                _attentionName = draft.AttentionName;
                _phone = draft.Phone;
                _email = draft.Email;
                _trn = draft.Trn;
                _address = draft.Address;
                _notes = draft.Notes;
                _isActive = draft.IsActive;
                _representativeId = draft.RepresentativeUserId ?? 0;
            }

            // The first entry is the explicit "none" choice. A client without a
            // representative stays valid, which is why the column is nullable:
            // every client that existed before migration 1014 is in that state.
            Representatives.Add(new ClientRepresentative { Id = 0, DisplayName = "No representative" });
            if (representatives != null)
            {
                foreach (var representative in representatives) Representatives.Add(representative);
            }

            // DelegateCommand requeries through CommandManager, so no explicit
            // CanExecuteChanged plumbing is needed; each command also guards on
            // IsBusy internally.
            SaveCommand = new DelegateCommand(async () => await SaveAsync());
            CancelCommand = new DelegateCommand(() => Cancelled?.Invoke(this, EventArgs.Empty));
            ReviewDuplicatesCommand = new DelegateCommand(async () => await ReviewDuplicatesAsync());
        }

        /// <summary>Raised after a successful save, with the saved client.</summary>
        public event EventHandler<Client>? Saved;

        /// <summary>Raised when the operator leaves the editor without saving.</summary>
        public event EventHandler? Cancelled;

        public bool IsNew => _isNew;

        public int ClientId => _clientId;

        public string Title => _isNew ? "New client" : "Edit client";

        public string Subtitle => _isNew
            ? "Add a client to the directory. Only the client name is required."
            : $"Editing client #{_clientId}. Inactive clients stay in the directory for history.";

        // ── Fields ───────────────────────────────────────────────────────

        public string ClientName
        {
            get => _clientName;
            set
            {
                if (!SetField(ref _clientName, value)) return;
                ClientNameError = ClientRules.ValidateClientName(value) ?? "";
                ClearSaveFeedback();
            }
        }

        /// <summary>The person at the client to address; optional.</summary>
        public string AttentionName
        {
            get => _attentionName;
            set
            {
                if (!SetField(ref _attentionName, value)) return;
                AttentionNameError = ClientRules.ValidateAttentionName(value) ?? "";
                ClearSaveFeedback();
            }
        }

        public string Phone
        {
            get => _phone;
            set
            {
                if (!SetField(ref _phone, value)) return;
                PhoneError = ClientRules.ValidatePhone(value) ?? "";
                ClearSaveFeedback();
            }
        }

        public string Email
        {
            get => _email;
            set
            {
                if (!SetField(ref _email, value)) return;
                EmailError = ClientRules.ValidateEmail(value) ?? "";
                ClearSaveFeedback();
            }
        }

        public string Trn
        {
            get => _trn;
            set
            {
                if (!SetField(ref _trn, value)) return;
                TrnError = ClientRules.ValidateTrn(value) ?? "";
                ClearSaveFeedback();
            }
        }

        public string Address
        {
            get => _address;
            set
            {
                if (!SetField(ref _address, value)) return;
                AddressError = ClientRules.ValidateAddress(value) ?? "";
                ClearSaveFeedback();
            }
        }

        public string Notes
        {
            get => _notes;
            set
            {
                if (!SetField(ref _notes, value)) return;
                ClearSaveFeedback();
            }
        }

        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (!SetField(ref _isActive, value)) return;
                OnPropertyChanged(nameof(StatusText));
                ClearSaveFeedback();
            }
        }

        public string StatusText => IsActive
            ? "Active — selectable for current work."
            : "Inactive — kept for history and reporting.";

        // ── Validation messages ──────────────────────────────────────────

        public string ClientNameError
        {
            get => _clientNameError;
            private set { if (SetField(ref _clientNameError, value)) OnPropertyChanged(nameof(HasClientNameError)); }
        }

        public string AttentionNameError
        {
            get => _attentionNameError;
            private set { if (SetField(ref _attentionNameError, value)) OnPropertyChanged(nameof(HasAttentionNameError)); }
        }

        public string PhoneError
        {
            get => _phoneError;
            private set { if (SetField(ref _phoneError, value)) OnPropertyChanged(nameof(HasPhoneError)); }
        }

        public string EmailError
        {
            get => _emailError;
            private set { if (SetField(ref _emailError, value)) OnPropertyChanged(nameof(HasEmailError)); }
        }

        public string TrnError
        {
            get => _trnError;
            private set { if (SetField(ref _trnError, value)) OnPropertyChanged(nameof(HasTrnError)); }
        }

        public string AddressError
        {
            get => _addressError;
            private set { if (SetField(ref _addressError, value)) OnPropertyChanged(nameof(HasAddressError)); }
        }

        public bool HasClientNameError => ClientNameError.Length > 0;
        public bool HasAttentionNameError => AttentionNameError.Length > 0;
        public bool HasPhoneError => PhoneError.Length > 0;
        public bool HasEmailError => EmailError.Length > 0;
        public bool HasTrnError => TrnError.Length > 0;
        public bool HasAddressError => AddressError.Length > 0;

        /// <summary>Selectable representatives, with a leading "No representative".</summary>
        public ObservableCollection<ClientRepresentative> Representatives { get; } = new();

        /// <summary>
        /// The chosen representative as a stable user id; 0 means none. The editor
        /// edits an id, never a name, so a rename cannot rewrite a client row.
        /// </summary>
        public int RepresentativeId
        {
            get => _representativeId;
            set
            {
                if (!SetField(ref _representativeId, value)) return;
                RepresentativeError = "";
                ClearSaveFeedback();
            }
        }

        public string RepresentativeError
        {
            get => _representativeError;
            private set { if (SetField(ref _representativeError, value)) OnPropertyChanged(nameof(HasRepresentativeError)); }
        }

        public bool HasRepresentativeError => RepresentativeError.Length > 0;

        /// <summary>True when at least one salesman exists to choose from.</summary>
        public bool HasRepresentatives => Representatives.Count > 1;

        /// <summary>Explains an empty picker instead of showing a dead dropdown.</summary>
        public string RepresentativeHint => HasRepresentatives
            ? "Optional. Only active users with the Salesman role are listed."
            : "No active user has the Salesman role yet. Add or re-role one in Users first; a client can stay without a representative.";

        // ── Duplicate warning / feedback ─────────────────────────────────

        public string DuplicateWarning
        {
            get => _duplicateWarning;
            private set
            {
                if (!SetField(ref _duplicateWarning, value)) return;
                OnPropertyChanged(nameof(HasDuplicateWarning));
            }
        }

        public bool HasDuplicateWarning => DuplicateWarning.Length > 0;

        public string StatusMessage
        {
            get => _statusMessage;
            private set { if (SetField(ref _statusMessage, value)) OnPropertyChanged(nameof(HasStatusMessage)); }
        }

        public bool HasStatusMessage => StatusMessage.Length > 0;

        public string ErrorMessage
        {
            get => _errorMessage;
            private set { if (SetField(ref _errorMessage, value)) OnPropertyChanged(nameof(HasErrorMessage)); }
        }

        public bool HasErrorMessage => ErrorMessage.Length > 0;

        public bool IsBusy
        {
            get => _isBusy;
            private set => SetField(ref _isBusy, value);
        }

        public ICommand SaveCommand { get; }

        public ICommand CancelCommand { get; }

        /// <summary>Re-runs duplicate detection for the current field values.</summary>
        public ICommand ReviewDuplicatesCommand { get; }

        /// <summary>Current field values as an un-normalized draft.</summary>
        public ClientDraft ToDraft() => new()
        {
            ClientName = ClientName,
            AttentionName = AttentionName,
            Phone = Phone,
            Email = Email,
            Trn = Trn,
            Address = Address,
            Notes = Notes,
            IsActive = IsActive,
            RepresentativeUserId = RepresentativeId > 0 ? RepresentativeId : null
        };

        // ── Commands ─────────────────────────────────────────────────────

        private async Task ReviewDuplicatesAsync()
        {
            if (IsBusy) return;
            IsBusy = true;
            try
            {
                var matches = await _repository.FindDuplicatesAsync(
                    ToDraft(), _isNew ? null : _clientId).ConfigureAwait(true);
                DuplicateWarning = ClientDuplicatePolicy.DescribeWarning(matches);
                _duplicateWarningAcknowledged = false;
                if (!HasDuplicateWarning) StatusMessage = "No similar client found.";
                else StatusMessage = "";
            }
            catch (Exception ex)
            {
                ErrorMessage = "Duplicate check failed (" + ex.GetType().Name + "): " + ex.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Validates, warns about duplicates once, then saves. The first save
        /// attempt that finds a resemblance stops and asks the operator to
        /// confirm; saving again stores the client unchanged. Nothing merges.
        /// </summary>
        public async Task<bool> SaveAsync()
        {
            if (IsBusy) return false;
            ErrorMessage = "";
            StatusMessage = "";

            IReadOnlyDictionary<string, string> errors = ClientRules.Validate(ToDraft());
            if (errors.Count > 0)
            {
                ApplyErrors(errors);
                ErrorMessage = errors.ContainsKey(nameof(ClientDraft.ClientName))
                    ? errors[nameof(ClientDraft.ClientName)]
                    : "Correct the highlighted field(s) before saving.";
                return false;
            }
            ClearErrors();

            IsBusy = true;
            try
            {
                if (!_duplicateWarningAcknowledged)
                {
                    var matches = await _repository.FindDuplicatesAsync(
                        ToDraft(), _isNew ? null : _clientId).ConfigureAwait(true);
                    if (matches.Count > 0)
                    {
                        DuplicateWarning = ClientDuplicatePolicy.DescribeWarning(matches);
                        _duplicateWarningAcknowledged = true;
                        StatusMessage = "";
                        ErrorMessage = "";
                        return false;
                    }
                }

                ClientSaveResult result = _isNew
                    ? await _repository.CreateAsync(ToDraft()).ConfigureAwait(true)
                    : await _repository.UpdateAsync(_clientId, ToDraft()).ConfigureAwait(true);

                if (!result.Succeeded || result.Client == null)
                {
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The client could not be saved."
                        : result.Message;
                    return false;
                }

                DuplicateWarning = "";
                _duplicateWarningAcknowledged = false;
                StatusMessage = _isNew ? "Client added." : "Client updated.";
                Saved?.Invoke(this, result.Client);
                return true;
            }
            catch (Exception ex)
            {
                ErrorMessage = "The client could not be saved (" + ex.GetType().Name + "): " + ex.Message;
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private void ApplyErrors(IReadOnlyDictionary<string, string> errors)
        {
            ClientNameError = errors.TryGetValue(nameof(ClientDraft.ClientName), out string? name) ? name : "";
            AttentionNameError = errors.TryGetValue(nameof(ClientDraft.AttentionName), out string? attention) ? attention : "";
            PhoneError = errors.TryGetValue(nameof(ClientDraft.Phone), out string? phone) ? phone : "";
            EmailError = errors.TryGetValue(nameof(ClientDraft.Email), out string? email) ? email : "";
            TrnError = errors.TryGetValue(nameof(ClientDraft.Trn), out string? trn) ? trn : "";
            AddressError = errors.TryGetValue(nameof(ClientDraft.Address), out string? address) ? address : "";
            RepresentativeError = errors.TryGetValue(nameof(ClientDraft.RepresentativeUserId), out string? representative)
                ? representative
                : "";
        }

        private void ClearErrors()
        {
            ClientNameError = "";
            AttentionNameError = "";
            PhoneError = "";
            EmailError = "";
            TrnError = "";
            AddressError = "";
            RepresentativeError = "";
        }

        private void ClearSaveFeedback()
        {
            if (ErrorMessage.Length > 0) ErrorMessage = "";
            if (StatusMessage.Length > 0) StatusMessage = "";
            // Any field change invalidates a previous duplicate acknowledgement.
            if (HasDuplicateWarning)
            {
                _duplicateWarningAcknowledged = false;
            }
        }
    }
}
