// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step1CompanySetup/CompanySetupViewModel.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — ALL FIXES APPLIED
//   C-01  SavedCompany cards from _masterData.GetAllCompaniesAsync()
//   C-02  CompanyProfileCard rebuilt (ProjectName removed)
//   C-03  IsLoadingCards loading state
//   C-04  HasNoSavedCompanies empty state
//   C-05  CardsErrorMessage error state
//   C-06  LoadCardsAsync() — max 6, UsageCount ordered
//   C-07  TrnMasked on card
//   C-08  CompanyNameValidationMessage + HasCompanyNameError
//   C-09  ProformaIdValidationMessage + HasProformaIdError
//   C-10  ShowAllCompaniesCommand + HasMoreCompanies + TotalCompanyCount
//   C-11  BUG-33/34/35/36/37 preserved
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

using ProGlassAutomation.Models.ProformaInvoice.Common;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using ProGlassAutomation.Services.MasterData.Repository;
using Microsoft.Extensions.DependencyInjection;
namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step1CompanySetup
{
    public class CompanySetupViewModel : ViewModelBase
    {
        // ── Callback to parent wizard ─────────────────────────────────
        private readonly Action<int>? _navigateToStep;

        // ── BUG-37: guard while loading profile ───────────────────────
        private bool _isLoadingProfile;

        // ── Internal full list for View all toggle ────────────────────
        private List<CompanyProfileCard> _allCompanyCards = new();
        private bool _showingAll = false;

        // ═══════════════════════════════════════════════════════════════
        //  COMPANY FIELDS
        // ═══════════════════════════════════════════════════════════════

        private string _companyName = string.Empty;
        public string CompanyName
        {
            get => _companyName;
            set
            {
                if (_companyName == value) return;
                _companyName = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(CompanyNameValidationMessage));
                OnPropertyChanged(nameof(HasCompanyNameError));
                MarkUserEditedIfNotLoading();
                RebuildVisibleCards(); // Update suggestions as user types
            }
        }

        private string _companyTrn = string.Empty;
        public string CompanyTrn
        {
            get => _companyTrn;
            set
            {
                if (_companyTrn == value) return;
                _companyTrn = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(TrnValidationMessage));
                OnPropertyChanged(nameof(IsTrnValid));
                MarkUserEditedIfNotLoading();
            }
        }

        private string _companyLocation = string.Empty;
        public string CompanyLocation
        {
            get => _companyLocation;
            set
            {
                if (_companyLocation == value) return;
                _companyLocation = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  PROFORMA FIELDS
        // ═══════════════════════════════════════════════════════════════

        private int? _inquiryId;
        public int? InquiryId
        {
            get => _inquiryId;
            set { if (_inquiryId == value) return; _inquiryId = value; OnPropertyChanged(); }
        }

        private string _proformaId = "";
        public string ProformaId
        {
            get => _proformaId;
            set
            {
                if (_proformaId == value) return;
                _proformaId = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(ProformaIdValidationMessage));
                OnPropertyChanged(nameof(HasProformaIdError));
            }
        }

        private DateTime _issueDate;
        public DateTime IssueDate
        {
            get => _issueDate;
            set { _issueDate = value; OnPropertyChanged(); }
        }

        private DateTime _validUntil;
        public DateTime ValidUntil
        {
            get => _validUntil;
            set { _validUntil = value; OnPropertyChanged(); }
        }

        private string _proformaStatus = "Draft";
        public string ProformaStatus
        {
            get => _proformaStatus;
            set { _proformaStatus = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Document-status picker. Single source:
        /// <see cref="ProformaInvoiceStatus.All"/>. The saved-list filter
        /// (<c>StatusFilterOptions</c>) is a different vocabulary (lifecycle
        /// display: "PI created", "PI confirmed", …) and must not be copied
        /// here. Cancelled is a real PI status (void target / transitions).
        /// </summary>
        public IReadOnlyList<string> StatusOptions => ProformaInvoiceStatus.All;

        // ═══════════════════════════════════════════════════════════════
        //  TRN VALIDATION (BUG-35)
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// UAE TRN must be exactly 15 digits.
        /// Returns null when valid or empty. Returns message when invalid.
        /// </summary>
        public string? TrnValidationMessage
        {
            get
            {
                if (string.IsNullOrWhiteSpace(_companyTrn))
                    return null;

                var digitsOnly = new string(
                    _companyTrn.Where(char.IsDigit).ToArray());

                if (digitsOnly.Length != 15)
                    return "UAE TRN must be exactly 15 digits.";

                if (digitsOnly != _companyTrn.Trim())
                    return "TRN should contain only digits (no spaces or letters).";

                return null;
            }
        }

        /// <summary>True when TRN is empty or valid 15-digit UAE TRN.</summary>
        public bool IsTrnValid => TrnValidationMessage == null;

        // ═══════════════════════════════════════════════════════════════
        //  REQUIRED FIELD VALIDATIONS (C-08, C-09)
        // ═══════════════════════════════════════════════════════════════

        // v2 — rich company profile carried from Company Center.
        // These used to be auto-properties with no INPC. Binding them on
        // the industrial form would accept keystrokes but never refresh
        // when a saved card (or the demo walk) loaded a profile.
        private string _representative = "";
        public string Representative
        {
            get => _representative;
            set
            {
                if (_representative == value) return;
                _representative = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _representativeMobile = "";
        public string RepresentativeMobile
        {
            get => _representativeMobile;
            set
            {
                if (_representativeMobile == value) return;
                _representativeMobile = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _companyEmail = "";
        public string CompanyEmail
        {
            get => _companyEmail;
            set
            {
                if (_companyEmail == value) return;
                _companyEmail = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _accountNumber = "";
        public string AccountNumber
        {
            get => _accountNumber;
            set
            {
                if (_accountNumber == value) return;
                _accountNumber = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _bankName = "";
        public string BankName
        {
            get => _bankName;
            set
            {
                if (_bankName == value) return;
                _bankName = value;
                OnPropertyChanged();
                MarkUserEditedIfNotLoading();
            }
        }

        private string _logoPath = "";
        public string LogoPath
        {
            get => _logoPath;
            set
            {
                var normalized = value ?? string.Empty;
                if (_logoPath == normalized) return;
                _logoPath = normalized;
                if (string.IsNullOrWhiteSpace(_logoPath))
                    LogoLayout = Models.MasterData.CompanyLogoLayout.None;
                else if (LogoLayout == Models.MasterData.CompanyLogoLayout.None)
                    LogoLayout = Models.MasterData.CompanyLogoLayout.LeftPortrait;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasLogo));
                InvalidateLogoImage();
            }
        }

        /// <summary>True when a company logo is loaded.</summary>
        public bool HasLogo => !string.IsNullOrEmpty(_logoPath);

        // ── Logo for the on-screen Step 7 preview ──────────────────────
        // The exported PDF has always rendered the company logo; the preview
        // never did, so operators approved a document that did not look like
        // the one they sent. This exposes the same logo to WPF.
        //
        // Loading is deliberately defensive for the same reason the PDF
        // loader is: a moved or corrupt logo file must blank the chip, never
        // break a money document or the wizard.

        private System.Windows.Media.ImageSource? _logoImage;
        private bool _logoImageResolved;

        /// <summary>The company logo, or null when there is no usable file.</summary>
        public System.Windows.Media.ImageSource? LogoImage
        {
            get
            {
                if (_logoImageResolved) return _logoImage;
                _logoImageResolved = true;
                _logoImage = TryLoadLogoImage(_logoPath);
                return _logoImage;
            }
        }

        /// <summary>True only when a logo actually decoded (not just a path).</summary>
        public bool HasLogoImage => LogoImage != null;

        /// <summary>
        /// True when the logo renders as a full-width masthead band instead of
        /// an inline left chip. Mirrors PdfChrome.EffectiveLogoLayout exactly
        /// (explicit TopPanel choice, OR a mark too wide to sit in a portrait
        /// chip) so the preview puts the logo where the exporter will.
        /// </summary>
        public bool LogoIsTopPanel =>
            HasLogoImage
            && (_logoLayout == Models.MasterData.CompanyLogoLayout.TopPanel
                || Services.Export.PrintPreview.PrintTokenService
                    .IsTopPanelLogo(_logoWidthMm, _logoHeightMm));

        /// <summary>True when the logo sits inline at the left of the identity.</summary>
        public bool LogoIsInline => HasLogoImage && !LogoIsTopPanel;

        /// <summary>
        /// Printed logo width in screen DIPs, clamped to the same bounds the
        /// PDF uses, so the preview chip and the printed chip match.
        /// </summary>
        public double LogoWidthDip => Services.Export.PrintPreview.PrintTokenService.DipFromMm(
            Math.Clamp(_logoWidthMm > 0 ? _logoWidthMm : 22,
                Services.Export.PrintPreview.PrintTokenService.LogoMinWidthMm,
                Services.Export.PrintPreview.PrintTokenService.LogoMaxWidthMm));

        /// <summary>Printed logo height in screen DIPs (same clamps as the PDF).</summary>
        public double LogoHeightDip => Services.Export.PrintPreview.PrintTokenService.DipFromMm(
            Math.Clamp(_logoHeightMm > 0 ? _logoHeightMm : 26,
                Services.Export.PrintPreview.PrintTokenService.LogoMinHeightMm,
                Services.Export.PrintPreview.PrintTokenService.LogoMaxHeightMm));

        private static System.Windows.Media.ImageSource? TryLoadLogoImage(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;
            try
            {
                string p = path.Trim().Trim('"');
                if (!System.IO.File.Exists(p)) return null;

                var bmp = new System.Windows.Media.Imaging.BitmapImage();
                bmp.BeginInit();
                // OnLoad + Freeze: the file is not held open, and the image
                // is safe to hand to the render thread.
                bmp.CacheOption = System.Windows.Media.Imaging.BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(p, UriKind.Absolute);
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        // v3 — logo placement carried from the Company Center record so the
        // printed letterhead matches the branding the operator configured.
        private Models.MasterData.CompanyLogoLayout _logoLayout
            = Models.MasterData.CompanyLogoLayout.None;
        public Models.MasterData.CompanyLogoLayout LogoLayout
        {
            get => _logoLayout;
            set
            {
                if (_logoLayout == value) return;
                _logoLayout = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogoLayoutLabel));
                RaiseLogoPlacement();
            }
        }

        /// <summary>Human label for the chosen logo layout.</summary>
        public string LogoLayoutLabel => _logoLayout switch
        {
            Models.MasterData.CompanyLogoLayout.LeftPortrait => "Left portrait logo",
            Models.MasterData.CompanyLogoLayout.TopPanel => "Top panel logo",
            _ => "No logo"
        };

        private double _logoWidthMm = 22;
        public double LogoWidthMm
        {
            get => _logoWidthMm;
            set
            {
                if (_logoWidthMm == value) return;
                _logoWidthMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogoWidthDip));
                RaiseLogoPlacement();
            }
        }

        private double _logoHeightMm = 26;
        public double LogoHeightMm
        {
            get => _logoHeightMm;
            set
            {
                if (_logoHeightMm == value) return;
                _logoHeightMm = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(LogoHeightDip));
                RaiseLogoPlacement();
            }
        }

        /// <summary>Drops the cached decode so the next read reloads the file.</summary>
        private void InvalidateLogoImage()
        {
            _logoImageResolved = false;
            _logoImage = null;
            OnPropertyChanged(nameof(LogoImage));
            OnPropertyChanged(nameof(HasLogoImage));
            RaiseLogoPlacement();
        }

        private void RaiseLogoPlacement()
        {
            OnPropertyChanged(nameof(LogoIsTopPanel));
            OnPropertyChanged(nameof(LogoIsInline));
        }

        public int CompanyRecordId { get; set; }

        public string? CompanyNameValidationMessage =>
            string.IsNullOrWhiteSpace(_companyName)
                ? "Company name is required for invoice generation."
                : null;

        public bool HasCompanyNameError => CompanyNameValidationMessage != null;

        public string? ProformaIdValidationMessage =>
            string.IsNullOrWhiteSpace(_proformaId)
                ? "Proforma ID is required."
                : null;

        public bool HasProformaIdError => ProformaIdValidationMessage != null;

        // ═══════════════════════════════════════════════════════════════
        //  SAVED COMPANY CARDS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Cards loaded from masterdata.db.
        /// Max 6 visible by default. View all shows full list.
        /// </summary>
        public ObservableCollection<CompanyProfileCard> SavedProfiles { get; }
            = new ObservableCollection<CompanyProfileCard>();

        // Loading state
        private bool _isLoadingCards;
        public bool IsLoadingCards
        {
            get => _isLoadingCards;
            set { _isLoadingCards = value; OnPropertyChanged(); }
        }

        // Empty state
        public bool HasNoSavedCompanies =>
            !IsLoadingCards
            && string.IsNullOrEmpty(CardsErrorMessage)
            && SavedProfiles.Count == 0
            && _allCompanyCards.Count == 0;

        // Error state
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

        // View all
        public bool HasMoreCompanies =>
            !_showingAll && _allCompanyCards.Count > 6;

        public int TotalCompanyCount => _allCompanyCards.Count;

        // ═══════════════════════════════════════════════════════════════
        //  LOADED PROFILE INDICATOR (BUG-37)
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

        public ICommand LoadProfileCommand { get; }
        public ICommand GoToGridCommand { get; }
        public ICommand ShowAllCompaniesCommand { get; }
        public ICommand RefreshCardsCommand { get; }
        public ICommand PickLogoCommand { get; }
        public ICommand ClearLogoCommand { get; }

        // ═══════════════════════════════════════════════════════════════
        //  CONSTRUCTORS
        // ═══════════════════════════════════════════════════════════════

        /// <summary>Parameterless — design-time and fallback only.</summary>
        private readonly IProformaInvoiceRepository _invoices = null!;
        private readonly IMasterDataRepository _masterData = null!;

        public CompanySetupViewModel() : this(
            App.ServiceProvider?.GetService<IProformaInvoiceRepository>(),
            App.ServiceProvider?.GetService<IMasterDataRepository>(), null) { }

        public CompanySetupViewModel(IProformaInvoiceRepository? invoices,
            IMasterDataRepository? masterData,
            Action<int>? navigateToStep)
        {
            _invoices = invoices!;
            _masterData = masterData!;
            _navigateToStep = navigateToStep;

            // BUG-33: placeholder until async load completes
            ProformaId = $"PI-{DateTime.Now.Year}-000";
            IssueDate = DateTime.Now;
            ValidUntil = DateTime.Now.AddDays(30);
            ProformaStatus = "Draft";

            LoadProfileCommand = new RelayCommand(param =>
            {
                if (param is CompanyProfileCard profile)
                    LoadProfile(profile);
            });

            GoToGridCommand = new RelayCommand(_ =>
            {
                _navigateToStep?.Invoke(5);
            });

            ShowAllCompaniesCommand = new RelayCommand(_ =>
            {
                _showingAll = true;
                RebuildVisibleCards();

                // 2026-09-04: default-company prefill REMOVED — the mark
                // mechanism no longer exists anywhere. Step 1 is
                // hard-connected to the Company module: the company that
                // lands on an invoice is exactly and only the one the user
                // picks (or the one restored with the invoice's snapshot).
                OnPropertyChanged(nameof(HasMoreCompanies));
            });

            RefreshCardsCommand = new RelayCommand(
                _ => AsyncUtility.SafeFireAndForget(LoadCardsAsync, "PI Step1"));

            // v3 — Step 1 is hard-connected to the Company module: the same
            // logo picker so the operator can attach branding without leaving
            // the wizard. Picking a card later overrides with that record.
            PickLogoCommand = new RelayCommand(_ => PickLogo());
            ClearLogoCommand = new RelayCommand(_ => LogoPath = "");
        }

        private void PickLogo()
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Company logo",
                Filter = "Images|*.png;*.jpg;*.jpeg;*.bmp|All files|*.*"
            };
            if (dialog.ShowDialog() == true)
                LogoPath = dialog.FileName;
        }

        // ═══════════════════════════════════════════════════════════════
        //  LOAD CARDS FROM MASTERDATA.DB
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Loads saved companies from masterdata.db.
        /// Called by shell VM on wizard open and new invoice.
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
                var companies = await _masterData.GetAllCompaniesAsync();

                _allCompanyCards = companies
                    .Select(c => new CompanyProfileCard
                    {
                        Id = c.Id,
                        CompanyName = c.CompanyName ?? "",
                        Trn = c.CompanyTrn ?? "",
                        Location = c.CompanyLocation ?? "",
                        UsageCount = c.UsageCount,
                        LogoPath = c.LogoPath ?? "",
                        Representative = c.Representative ?? "",
                        RepresentativeMobile = c.RepresentativeMobile ?? "",
                        CompanyEmail = c.CompanyEmail ?? "",
                        AccountNumber = c.AccountNumber ?? "",
                        BankName = c.BankName ?? "",
                        IsDefault = c.IsDefault,
                        LogoLayout = !string.IsNullOrWhiteSpace(c.LogoPath) && c.LogoLayout == Models.MasterData.CompanyLogoLayout.None
                            ? Models.MasterData.CompanyLogoLayout.LeftPortrait
                            : c.LogoLayout,
                        LogoWidthMm = c.LogoWidthMm > 0 ? c.LogoWidthMm : 22,
                        LogoHeightMm = c.LogoHeightMm > 0 ? c.LogoHeightMm : 26,
                        // 2026-09-04: IsDefault no longer mapped — the
                        // default-mark mechanism is removed product-wide.
                        // E-FIX: DateTime is non-nullable — check against default
                        LastUsed = c.LastUsedAt != default(DateTime)
                                        ? c.LastUsedAt.ToString("dd MMM yyyy")
                                        : ""
                    })
                    .ToList();

                RebuildVisibleCards();

                // 2026-09-07: new proforma invoices now adopt the Company
                // module's saved default (or the most-used company when no
                // explicit default exists). This keeps Step 1 legally populated
                // by default while still letting the operator pick another card.
                if (!HasLoadedProfile && string.IsNullOrWhiteSpace(CompanyName))
                {
                    var defaultProfile = _allCompanyCards.FirstOrDefault(c => c.IsDefault)
                        ?? _allCompanyCards.FirstOrDefault();
                    if (defaultProfile != null)
                        LoadProfile(defaultProfile);
                }
            }
            catch (Exception ex)
            {
                CardsErrorMessage =
                    $"Could not load saved companies: {ExceptionMessages.Deep(ex)}";
            }
            finally
            {
                IsLoadingCards = false;
                OnPropertyChanged(nameof(HasNoSavedCompanies));
                OnPropertyChanged(nameof(HasMoreCompanies));
                OnPropertyChanged(nameof(TotalCompanyCount));
            }
        }

        private void RebuildVisibleCards()
        {
            SavedProfiles.Clear();

            // FILTER: match against the typed CompanyName.
            // AUDIT-FIX F-3: while the field is blank, offer the most-used
            // companies as tappable suggestions instead of showing nothing —
            // the historical "fresh PI shows zero cards" dead-end.
            var filtered = string.IsNullOrWhiteSpace(CompanyName)
                ? _allCompanyCards
                : _allCompanyCards
                    .Where(c => c.CompanyName.IndexOf(CompanyName, StringComparison.OrdinalIgnoreCase) >= 0)
                    .ToList();

            var visible = _showingAll
                ? filtered
                : filtered.Take(6).ToList();

            foreach (var card in visible)
                SavedProfiles.Add(card);

            OnPropertyChanged(nameof(HasNoSavedCompanies));
            OnPropertyChanged(nameof(HasMoreCompanies));
        }

        // ═══════════════════════════════════════════════════════════════
        //  BUG-33: Async unique Proforma ID generator
        // ═══════════════════════════════════════════════════════════════

        /// <summary>
        /// Generates a DB-verified unique Proforma ID.
        /// Called by shell VM on new invoice start.
        /// </summary>
        public async Task GenerateUniqueProformaIdAsync()
        {
            try
            {
                if (_invoices == null)
                {
                    ProformaId =
                        $"PI-{DateTime.Now.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
                    return;
                }

                string seedId = $"PI-{DateTime.Now.Year}-000";
                ProformaId = await _invoices.SuggestNextIdAsync(seedId);
            }
            catch (Exception ex)
            {
                // 2026-09-05 hardening: this catch was SILENT. A repo
                // failure used to degrade the document number from
                // DB-verified-unique to a 6-hex random fallback without a
                // trace — the operator saw nothing and no log line
                // existed to explain a later duplicate-id report. The
                // fallback itself stays (a wizard must never die on an
                // auxiliary lookup), but the degradation is now logged.
                Services.Logging.AppLogService.Warning(
                    "[CompanySetupViewModel] SuggestNextIdAsync failed; " +
                    "falling back to a random 6-hex proforma id: " +
                    ExceptionMessages.Deep(ex));
                ProformaId =
                    $"PI-{DateTime.Now.Year}-{Guid.NewGuid().ToString("N").Substring(0, 6).ToUpper()}";
            }
        }

        // ═══════════════════════════════════════════════════════════════
        //  BUG-37: Loaded profile lifecycle
        // ═══════════════════════════════════════════════════════════════

        private void LoadProfile(CompanyProfileCard profile)
        {
            _isLoadingProfile = true;
            try
            {
                CompanyName = profile.CompanyName;
                CompanyTrn = profile.Trn;
                CompanyLocation = profile.Location;
                LoadedProfileName = profile.CompanyName;
                Representative = profile.Representative;
                LogoPath = profile.LogoPath;
                // AUDIT-FIX F-4: the card carries these from the store and
                // the XAML binds them, but they were never copied — picking
                // a company card left mobile/e-mail/bank blank.
                RepresentativeMobile = profile.RepresentativeMobile;
                CompanyEmail = profile.CompanyEmail;
                AccountNumber = profile.AccountNumber;
                BankName = profile.BankName;
                LogoLayout = !string.IsNullOrWhiteSpace(profile.LogoPath) && profile.LogoLayout == Models.MasterData.CompanyLogoLayout.None
                    ? Models.MasterData.CompanyLogoLayout.LeftPortrait
                    : profile.LogoLayout;
                LogoWidthMm = profile.LogoWidthMm;
                LogoHeightMm = profile.LogoHeightMm;
                CompanyRecordId = profile.Id;
            }
            finally
            {
                _isLoadingProfile = false;
            }
        }

        /// <summary>
        /// BUG-37: Clears LoadedProfileName when user manually edits
        /// after loading a profile.
        /// </summary>
        private void MarkUserEditedIfNotLoading()
        {
            if (_isLoadingProfile) return;
            if (!string.IsNullOrEmpty(_loadedProfileName))
                LoadedProfileName = "";
        }

        // ── SafeFireAsync — avoids async void crashes ─────────────────
        
    }
}
