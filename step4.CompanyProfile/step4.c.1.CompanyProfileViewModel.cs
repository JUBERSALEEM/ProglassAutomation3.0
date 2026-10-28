// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.c.1.CompanyProfileViewModel.cs
// MODULE: step4 — the Company Profile screen's behaviour.
//
// What the screen is for
//   Filling in the one company identity this installation prints on every
//   document: name, TRN, address, contact details, bank details and a logo.
//
// The states it must be honest about
//   • Never saved: the form is empty, and the page says what saving will do.
//   • Saved: every field comes back after a restart, and the page says when it
//     was last saved and by whom.
//   • Refused: the message names the fields, and NOTHING is reported as saved.
//   • Read-only: somebody who is not an administrator can read the profile but
//     cannot change it — and the reason is printed, not implied by a greyed
//     button. The repository refuses the write as well.
//
// The logo
//   Picking a file does not touch the database. The preview shows the PICKED
//   image, the old asset stays where it is, and the copy only becomes the stored
//   logo when the save succeeds (step4.b.4). Removing the logo is the same: it
//   takes effect on save, so an abandoned edit cannot lose the company's logo.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Company;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Company;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Inquiry;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGlassAutomation.ViewModels.Company
{
    /// <summary>Drives the Company Profile page.</summary>
    public sealed partial class CompanyProfileViewModel : ViewModelBase
    {
        private readonly ICompanyProfileRepository _repository;

        private CompanyProfile? _stored;
        private bool _loaded;
        private bool _isBusy;
        private bool _isDirty;

        private string _companyName = "";
        private string _trn = "";
        private string _address = "";
        private string _telephone = "";
        private string _website = "";
        private string _representativeName = "";
        private string _representativeMobile = "";
        private string _email = "";
        private string _bankName = "";
        private string _accountNumber = "";

        private string _storedLogoFileName = "";
        private string _pendingLogoPath = "";
        private bool _logoRemoved;
        private CompanyLogoLayout _logoLayout = CompanyLogoLayout.None;
        private double _logoSizeMm = CompanyProfileRules.DefaultLogoSizeMm;
        private ImageSource? _logoPreview;
        private string _logoPreviewProblem = "";
        private string _logoImageInfo = "";
        private double _logoAspectRatio = 1;

        private readonly Dictionary<string, string> _fieldErrors = new(StringComparer.Ordinal);
        private string _errorMessage = "";
        private string _statusMessage = "";
        private string _savedSummary = "";

        public CompanyProfileViewModel() : this(new CompanyProfileRepository()) { }

        public CompanyProfileViewModel(ICompanyProfileRepository repository)
        {
            _repository = repository ?? throw new ArgumentNullException(nameof(repository));

            SaveCommand = new DelegateCommand(async () => await SaveAsync());
            RevertCommand = new DelegateCommand(async () => await RevertAsync());
            ChooseLogoCommand = new DelegateCommand(() => RequestLogoPick?.Invoke(this, EventArgs.Empty));
            RemoveLogoCommand = new DelegateCommand(RemoveLogo);
            RefreshCommand = new DelegateCommand(async () => await ReloadAsync());

            // Round 32: `CanEdit` reads a PROCESS-WIDE session, so it must be told
            // when that session changes. Nothing else refreshes it, and a stale
            // false is not a cosmetic bug: it is a disabled "Choose image" button
            // with no explanation, on a page the operator has every right to edit.
            AuthenticationContext.SignedIn += OnSessionChanged;
            AuthenticationContext.SignedOut += OnSessionChanged;
        }

        private void OnSessionChanged(object? sender, EventArgs e) => RefreshAuthorization();

        /// <summary>
        /// Re-reads who is signed in and re-raises everything that depends on it.
        /// Called when the session changes and whenever the view is shown again.
        /// </summary>
        public void RefreshAuthorization()
        {
            OnPropertyChanged(nameof(CanEdit));
            OnPropertyChanged(nameof(IsReadOnly));
            OnPropertyChanged(nameof(ReadOnlyReason));
            OnPropertyChanged(nameof(HasReadOnlyReason));
            OnPropertyChanged(nameof(CanSave));
            OnPropertyChanged(nameof(ChooseLogoTooltip));
            RaiseHeaderState();
        }

        /// <summary>
        /// Raised when the operator asks for the file picker. The view owns the
        /// dialog: a view model cannot open one, and a test must not be able to.
        /// </summary>
        public event EventHandler? RequestLogoPick;

        public ICommand SaveCommand { get; }
        public ICommand RevertCommand { get; }
        public ICommand ChooseLogoCommand { get; }
        public ICommand RemoveLogoCommand { get; }
        public ICommand RefreshCommand { get; }

        // ── Loaded state ─────────────────────────────────────────────────

        /// <summary>True once the screen has read the profile (saved or not).</summary>
        public bool IsLoaded
        {
            get => _loaded;
            private set
            {
                if (!SetField(ref _loaded, value)) return;
                OnPropertyChanged(nameof(IsBusy));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ProfileStateText));
                RaiseHeaderState();
            }
        }

        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (!SetField(ref _isBusy, value)) return;
                OnPropertyChanged(nameof(IsIdle));
                OnPropertyChanged(nameof(CanSave));
            }
        }

        public bool IsIdle => !IsBusy;

        /// <summary>True when nothing has been stored yet.</summary>
        public bool ShowEmptyState => IsLoaded && _stored == null;

        /// <summary>One line describing what is on file, for the header.</summary>
        public string ProfileStateText => !IsLoaded
            ? "Reading the company profile…"
            : _stored == null
                ? "Not set up yet — this installation has no company identity on file."
                : "Last saved " + _stored.UpdatedAt.ToLocalTime()
                    .ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture)
                  + (SavedByText.Length > 0 ? " by " + SavedByText : "")
                  + ".";

        private string SavedByText { get; set; } = "";

        // ── Header state ─────────────────────────────────────────────────
        // The compact page header carries ONE chip that says where the profile
        // stands — saved (with the time), unsaved, or never set up — so the
        // operator never has to infer it from a greyed button. Three booleans
        // rather than an enum: the chip is styled by DataTrigger, and a trigger
        // can compare a bool directly.

        public string HeaderStateText
        {
            get
            {
                if (!IsLoaded) return "Reading…";
                if (_isDirty) return "Unsaved changes";
                if (_stored == null) return "Not set up yet";
                return "Saved "
                    + _stored.UpdatedAt.ToLocalTime()
                        .ToString("HH:mm", CultureInfo.InvariantCulture);
            }
        }

        /// <summary>Saved, with nothing pending: the calm state.</summary>
        public bool IsHeaderSaved => IsLoaded && !_isDirty && _stored != null;

        /// <summary>Edited but not written: the chip turns to the warning colour.</summary>
        public bool IsHeaderDirty => IsLoaded && _isDirty;

        /// <summary>Nothing stored yet: the chip says so instead of pretending.</summary>
        public bool IsHeaderEmpty => IsLoaded && !_isDirty && _stored == null;

        /// <summary>
        /// The footer's left-hand line: who may change this, or why not, or what
        /// is pending. It is never a bare "Administrator" badge — the reason is
        /// printed, because a greyed Save button does not explain itself.
        /// </summary>
        public string FooterNote
        {
            get
            {
                if (HasReadOnlyReason) return ReadOnlyReason;
                if (_isDirty) return "Unsaved changes — Save company keeps them.";
                if (_stored == null)
                    return "Not set up yet. Enter the company name and save to create the profile.";
                return "Administrator access only: only an Administrator can change these details.";
            }
        }

        private void RaiseHeaderState()
        {
            OnPropertyChanged(nameof(HeaderStateText));
            OnPropertyChanged(nameof(IsHeaderSaved));
            OnPropertyChanged(nameof(IsHeaderDirty));
            OnPropertyChanged(nameof(IsHeaderEmpty));
            OnPropertyChanged(nameof(FooterNote));
        }

        // ── Authorisation ────────────────────────────────────────────────

        /// <summary>True when the signed-in user may change the profile.</summary>
        public bool CanEdit =>
            AuthenticationContext.Current is { IsActive: true } actor
            && UserCatalog.IsAdministrator(actor.Role);

        public bool IsReadOnly => !CanEdit;

        public string ReadOnlyReason => CanEdit
            ? ""
            : AuthenticationContext.Current == null
                ? "Sign in as an administrator to change the company profile."
                : "Only an Administrator can change the company profile. You can read it here, "
                  + "and the save is refused at the storage layer too.";

        public bool HasReadOnlyReason => ReadOnlyReason.Length > 0;

        /// <summary>
        /// Why "Choose image" is or is not available. A disabled button whose
        /// tooltip still says "Choose a PNG or JPEG" is the silent refusal this
        /// page must not have, so the tooltip changes with the permission.
        /// </summary>
        public string ChooseLogoTooltip => CanEdit
            ? "Choose a PNG or JPEG image for the company logo"
            : ReadOnlyReason;

        /// <summary>
        /// True when the Save button should do something: an authorised operator,
        /// no write in progress, and a working copy that actually differs from
        /// what is stored. A Save that would write nothing is not offered.
        /// </summary>
        public bool CanSave => !IsBusy && CanEdit && _isDirty;

        // ── Fields ───────────────────────────────────────────────────────

        public string CompanyName
        {
            get => _companyName;
            set { if (SetField(ref _companyName, value ?? "")) Touch(nameof(CompanyName)); }
        }

        public string Trn
        {
            get => _trn;
            set { if (SetField(ref _trn, value ?? "")) Touch(nameof(Trn)); }
        }

        public string Address
        {
            get => _address;
            set { if (SetField(ref _address, value ?? "")) Touch(nameof(Address)); }
        }

        public string Telephone
        {
            get => _telephone;
            set { if (SetField(ref _telephone, value ?? "")) Touch(nameof(Telephone)); }
        }

        public string Website
        {
            get => _website;
            set { if (SetField(ref _website, value ?? "")) Touch(nameof(Website)); }
        }

        public string RepresentativeName
        {
            get => _representativeName;
            set { if (SetField(ref _representativeName, value ?? "")) Touch(nameof(RepresentativeName)); }
        }

        public string RepresentativeMobile
        {
            get => _representativeMobile;
            set { if (SetField(ref _representativeMobile, value ?? "")) Touch(nameof(RepresentativeMobile)); }
        }

        public string Email
        {
            get => _email;
            set { if (SetField(ref _email, value ?? "")) Touch(nameof(Email)); }
        }

        public string BankName
        {
            get => _bankName;
            set { if (SetField(ref _bankName, value ?? "")) Touch(nameof(BankName)); }
        }

        public string AccountNumber
        {
            get => _accountNumber;
            set { if (SetField(ref _accountNumber, value ?? "")) Touch(nameof(AccountNumber)); }
        }

        // ── Per-field messages ───────────────────────────────────────────

        public string CompanyNameError => ErrorFor(nameof(CompanyName));
        public string TrnError => ErrorFor(nameof(Trn));
        public string AddressError => ErrorFor(nameof(Address));
        public string TelephoneError => ErrorFor(nameof(Telephone));
        public string WebsiteError => ErrorFor(nameof(Website));
        public string RepresentativeNameError => ErrorFor(nameof(RepresentativeName));
        public string RepresentativeMobileError => ErrorFor(nameof(RepresentativeMobile));
        public string EmailError => ErrorFor(nameof(Email));
        public string BankNameError => ErrorFor(nameof(BankName));
        public string AccountNumberError => ErrorFor(nameof(AccountNumber));
        public string LogoLayoutError => ErrorFor(nameof(CompanyProfileDraft.LogoLayout));
        public string LogoSizeError => ErrorFor(nameof(CompanyProfileDraft.LogoSizeMm));

        public bool HasCompanyNameError => CompanyNameError.Length > 0;
        public bool HasTrnError => TrnError.Length > 0;
        public bool HasAddressError => AddressError.Length > 0;
        public bool HasTelephoneError => TelephoneError.Length > 0;
        public bool HasWebsiteError => WebsiteError.Length > 0;
        public bool HasRepresentativeNameError => RepresentativeNameError.Length > 0;
        public bool HasRepresentativeMobileError => RepresentativeMobileError.Length > 0;
        public bool HasEmailError => EmailError.Length > 0;
        public bool HasBankNameError => BankNameError.Length > 0;
        public bool HasAccountNumberError => AccountNumberError.Length > 0;
        public bool HasLogoLayoutError => LogoLayoutError.Length > 0;
        public bool HasLogoSizeError => LogoSizeError.Length > 0;

        private string ErrorFor(string field) =>
            _fieldErrors.TryGetValue(field, out string? message) ? message : "";


        // ── Feedback ─────────────────────────────────────────────────────

        public string ErrorMessage
        {
            get => _errorMessage;
            private set
            {
                if (!SetField(ref _errorMessage, value)) return;
                OnPropertyChanged(nameof(HasError));
            }
        }

        public bool HasError => _errorMessage.Length > 0;

        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (!SetField(ref _statusMessage, value)) return;
                OnPropertyChanged(nameof(HasStatus));
            }
        }

        public bool HasStatus => _statusMessage.Length > 0;

        /// <summary>True while the working copy differs from what is stored.</summary>
        public bool HasUnsavedChanges => _isDirty;

        public string UnsavedText => _isDirty ? "Unsaved changes" : "";

        public bool HasUnsavedNotice => _isDirty;

        // ── Loading ──────────────────────────────────────────────────────

        /// <summary>
        /// Reads the profile once. Safe to call repeatedly: the second call is a
        /// no-op until <see cref="ReloadAsync"/> is asked for.
        /// </summary>
        public async Task EnsureLoadedAsync()
        {
            if (_loaded) return;
            await ReloadAsync().ConfigureAwait(true);
        }

        public async Task ReloadAsync()
        {
            IsBusy = true;
            ErrorMessage = "";
            StatusMessage = "";

            try
            {
                await _repository.InitializeAsync().ConfigureAwait(true);

                CompanyProfile? profile = await _repository.GetAsync().ConfigureAwait(true);
                _stored = profile;

                if (profile == null)
                {
                    Apply(CompanyProfileRules.EmptyDraft());
                    _storedLogoFileName = "";
                }
                else
                {
                    Apply(CompanyProfileRules.ToDraft(profile));
                    _storedLogoFileName = profile.LogoFileName;
                    SavedByText = profile.UpdatedByUserId is int actor
                        ? await _repository.ResolveUserNameAsync(actor).ConfigureAwait(true)
                        : "";
                }

                _pendingLogoPath = "";
                _logoRemoved = false;
                RefreshLogoPreview();
                ClearFieldErrors();
                _isDirty = false;
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(HasUnsavedNotice));
                OnPropertyChanged(nameof(UnsavedText));
                OnPropertyChanged(nameof(CanSave));
                RaiseHeaderState();

                IsLoaded = true;
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ProfileStateText));
                OnPropertyChanged(nameof(CanEdit));
                OnPropertyChanged(nameof(IsReadOnly));
                OnPropertyChanged(nameof(ReadOnlyReason));
                OnPropertyChanged(nameof(HasReadOnlyReason));
                OnPropertyChanged(nameof(CanSave));
            }
            catch (Exception error)
            {
                IsLoaded = false;
                ErrorMessage = "The company profile could not be read ("
                    + error.GetType().Name + "): " + error.Message;
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>Discards the working copy and reloads what is stored.</summary>
        public async Task RevertAsync()
        {
            _isDirty = false;
            await ReloadAsync().ConfigureAwait(true);
            StatusMessage = "Reverted to the saved profile.";
        }


        // ── Save ─────────────────────────────────────────────────────────

        public async Task<bool> SaveAsync()
        {
            if (IsBusy) return false;

            if (!CanEdit)
            {
                ErrorMessage = ReadOnlyReason;
                return false;
            }

            IsBusy = true;
            ErrorMessage = "";
            StatusMessage = "";
            ClearFieldErrors();

            try
            {
                CompanyProfileDraft draft = BuildDraft();

                // Report per-field messages before the write, using the same rules
                // the repository will apply: the operator sees the reason on the
                // field, not a summary after a round trip.
                IReadOnlyDictionary<string, string> preview = CompanyProfileRules.Validate(draft);
                if (preview.Count > 0)
                {
                    ShowFieldErrors(preview);
                    ErrorMessage = CompanyProfileRules.Describe(preview) + " Nothing was saved.";
                    return false;
                }

                CompanyProfileSaveResult result = await _repository
                    .SaveAsync(draft).ConfigureAwait(true);

                if (!result.Succeeded)
                {
                    ShowFieldErrors(result.FieldErrors);
                    ErrorMessage = string.IsNullOrWhiteSpace(result.Message)
                        ? "The company profile was not saved."
                        : result.Message;
                    // A failed save must leave the screen honest: the working copy
                    // is still unsaved, and nothing claims otherwise.
                    return false;
                }

                _stored = result.Profile;
                _storedLogoFileName = result.Profile?.LogoFileName ?? "";
                _pendingLogoPath = "";
                _logoRemoved = false;
                _isDirty = false;

                SavedByText = result.Profile?.UpdatedByUserId is int actor
                    ? await _repository.ResolveUserNameAsync(actor).ConfigureAwait(true)
                    : "";

                RefreshLogoPreview();
                OnPropertyChanged(nameof(PendingLogoPath));
                OnPropertyChanged(nameof(HasLogo));
                OnPropertyChanged(nameof(HasUnsavedChanges));
                OnPropertyChanged(nameof(HasUnsavedNotice));
                OnPropertyChanged(nameof(UnsavedText));
                OnPropertyChanged(nameof(CanSave));
                OnPropertyChanged(nameof(ShowEmptyState));
                OnPropertyChanged(nameof(ProfileStateText));
                OnPropertyChanged(nameof(LogoDisplayName));
                RaiseHeaderState();

                StatusMessage = string.IsNullOrWhiteSpace(result.Message)
                    ? "The company profile was saved."
                    : result.Message;
                return true;
            }
            catch (Exception error)
            {
                ErrorMessage = "The company profile could not be saved ("
                    + error.GetType().Name + "): " + error.Message + " Nothing was changed.";
                return false;
            }
            finally
            {
                IsBusy = false;
            }
        }

        private CompanyProfileDraft BuildDraft() => new()
        {
            CompanyName = _companyName,
            Trn = _trn,
            Address = _address,
            Telephone = _telephone,
            Website = _website,
            RepresentativeName = _representativeName,
            RepresentativeMobile = _representativeMobile,
            Email = _email,
            BankName = _bankName,
            AccountNumber = _accountNumber,
            LogoFileName = _logoRemoved ? "" : _storedLogoFileName,
            PendingLogoSourcePath = _pendingLogoPath,
            LogoLayout = _logoRemoved ? CompanyLogoLayout.None : _logoLayout,
            LogoSizeMm = _logoSizeMm
        };

        // ── Helpers ──────────────────────────────────────────────────────

        private void Apply(CompanyProfileDraft draft)
        {
            _companyName = draft.CompanyName;
            _trn = draft.Trn;
            _address = draft.Address;
            _telephone = draft.Telephone;
            _website = draft.Website;
            _representativeName = draft.RepresentativeName;
            _representativeMobile = draft.RepresentativeMobile;
            _email = draft.Email;
            _bankName = draft.BankName;
            _accountNumber = draft.AccountNumber;
            _logoLayout = draft.LogoLayout;
            _logoSizeMm = CompanyProfileRules.ClampLogoSize(draft.LogoSizeMm);

            OnPropertyChanged(nameof(CompanyName));
            OnPropertyChanged(nameof(Trn));
            OnPropertyChanged(nameof(Address));
            OnPropertyChanged(nameof(Telephone));
            OnPropertyChanged(nameof(Website));
            OnPropertyChanged(nameof(RepresentativeName));
            OnPropertyChanged(nameof(RepresentativeMobile));
            OnPropertyChanged(nameof(Email));
            OnPropertyChanged(nameof(BankName));
            OnPropertyChanged(nameof(AccountNumber));
            OnPropertyChanged(nameof(LogoLayout));
            OnPropertyChanged(nameof(IsNoLogoLayout));
            OnPropertyChanged(nameof(IsLeftPortraitLayout));
            OnPropertyChanged(nameof(IsTopPanelLayout));
            OnPropertyChanged(nameof(LogoPlacementHint));
            OnPropertyChanged(nameof(LogoSizeMm));
            OnPropertyChanged(nameof(LogoSizeText));
            OnPropertyChanged(nameof(SelectedLogoSizeOption));
            OnPropertyChanged(nameof(PreviewCompanyName));
            OnPropertyChanged(nameof(PreviewTrnLine));
            OnPropertyChanged(nameof(PreviewContactLine));
            OnPropertyChanged(nameof(HasPreviewContactLine));
            OnPropertyChanged(nameof(PreviewRepresentativeLine));
            OnPropertyChanged(nameof(HasPreviewRepresentativeLine));
            OnPropertyChanged(nameof(PreviewAddressLine1));
            OnPropertyChanged(nameof(PreviewAddressLine2));
            OnPropertyChanged(nameof(HasPreviewAddressLine1));
            OnPropertyChanged(nameof(HasPreviewAddressLine2));
        }

        private void Touch(string field)
        {
            OnPropertyChanged(nameof(PreviewCompanyName));
            OnPropertyChanged(nameof(PreviewTrnLine));
            OnPropertyChanged(nameof(PreviewContactLine));
            OnPropertyChanged(nameof(HasPreviewContactLine));
            OnPropertyChanged(nameof(PreviewRepresentativeLine));
            OnPropertyChanged(nameof(HasPreviewRepresentativeLine));
            OnPropertyChanged(nameof(PreviewAddressLine1));
            OnPropertyChanged(nameof(PreviewAddressLine2));
            OnPropertyChanged(nameof(HasPreviewAddressLine1));
            OnPropertyChanged(nameof(HasPreviewAddressLine2));

            // Typing clears that field's message: the operator is fixing it, and a
            // stale sentence beside a corrected field is noise.
            if (_fieldErrors.Remove(field))
                RaiseFieldError(field);

            MarkDirty();
        }

        private void MarkDirty()
        {
            if (_isDirty) return;
            _isDirty = true;
            OnPropertyChanged(nameof(HasUnsavedChanges));
            OnPropertyChanged(nameof(HasUnsavedNotice));
            OnPropertyChanged(nameof(UnsavedText));
            OnPropertyChanged(nameof(CanSave));
            RaiseHeaderState();
        }

        private void ClearFieldErrors()
        {
            if (_fieldErrors.Count == 0) return;
            _fieldErrors.Clear();
            foreach (string field in new[]
                     {
                         nameof(CompanyName), nameof(Trn), nameof(Address), nameof(Telephone),
                         nameof(Website), nameof(RepresentativeName), nameof(RepresentativeMobile),
                         nameof(Email), nameof(BankName), nameof(AccountNumber),
                         nameof(CompanyProfileDraft.LogoLayout), nameof(CompanyProfileDraft.LogoSizeMm)
                     })
            {
                RaiseFieldError(field);
            }
        }

        private void ShowFieldErrors(IReadOnlyDictionary<string, string> errors)
        {
            ClearFieldErrors();
            foreach (var pair in errors) _fieldErrors[pair.Key] = pair.Value;
            foreach (var pair in errors) RaiseFieldError(pair.Key);
        }

        /// <summary>
        /// Raises the change notification for one field's message.
        /// </summary>
        /// <remarks>
        /// The draft calls the fields LogoLayout and LogoSizeMm (they are persisted
        /// verbatim), while this view model exposes them as LogoLayoutError and
        /// LogoSizeError — the two words the page actually shows. Raising
        /// "LogoSizeMmError" would notify a property that does not exist, so the
        /// message would be stored and never appear; the mapping keeps the
        /// notification and the binding on the same name.
        /// </remarks>
        private void RaiseFieldError(string field)
        {
            string property = field switch
            {
                nameof(CompanyProfileDraft.LogoSizeMm) => "LogoSize",
                _ => field
            };

            OnPropertyChanged(property + "Error");
            OnPropertyChanged("Has" + property + "Error");
        }
    }
}

namespace ProGlassAutomation.ViewModels.Company
{
    /// <summary>One offered printed logo size, bound by the size picker.</summary>
    public sealed class LogoSizeChoice
    {
        public LogoSizeChoice(double millimetres) => Millimetres = millimetres;

        public double Millimetres { get; }

        /// <summary>What the closed picker shows: the size only, never a field name.</summary>
        public string Label =>
            Millimetres.ToString("0", CultureInfo.InvariantCulture) + " mm";

        public override string ToString() => Label;
    }
}
