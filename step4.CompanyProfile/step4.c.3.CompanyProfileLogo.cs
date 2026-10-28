// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.c.3.CompanyProfileLogo.cs
// MODULE: step4 — the logo half of the Company Profile view model.
//
// Why this is its own file (owner instruction, round 30b: "never keep any file
// long like god files, keep them small separate parts for the same module")
//   step4.c.1 was 999 lines and is grandfathered by step1.d.19, which means any
//   new line added to it had to go into a part instead. Round 32 adds the logo's
//   document preview and the session-aware authorisation refresh, so the whole
//   logo concern moves here: which file is shown, how big it prints, where it
//   sits on the page, and the preview text the sheet is built from.
//
//   It is a partial class of the same view model — same instance, same state,
//   no new contract, no property moved behind a wrapper. C# has no "module" unit
//   other than the file, and this is the file's job: one concern, one place.
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
    /// <summary>Logo and document preview — the same view model, its own file.</summary>
    public sealed partial class CompanyProfileViewModel
    {
        // ── Logo ─────────────────────────────────────────────────────────

        /// <summary>The stored asset's name, "" when none.</summary>
        public string StoredLogoFileName => _storedLogoFileName;

        /// <summary>A file the operator picked and has not saved yet.</summary>
        public string PendingLogoPath => _pendingLogoPath;

        /// <summary>True when a logo is in the working copy (stored or picked).</summary>
        public bool HasLogo => !_logoRemoved
            && (_pendingLogoPath.Length > 0 || _storedLogoFileName.Length > 0);

        public ImageSource? LogoPreview
        {
            get => _logoPreview;
            private set => SetField(ref _logoPreview, value);
        }

        public bool HasLogoPreview => _logoPreview != null;

        /// <summary>Why there is no preview, when that needs saying.</summary>
        public string LogoPreviewProblem
        {
            get => _logoPreviewProblem;
            private set
            {
                if (!SetField(ref _logoPreviewProblem, value)) return;
                OnPropertyChanged(nameof(HasLogoPreviewProblem));
            }
        }

        public bool HasLogoPreviewProblem => _logoPreviewProblem.Length > 0;

        /// <summary>"1240 × 480 px" — the source image's own pixel size.</summary>
        public string LogoImageInfo
        {
            get => _logoImageInfo;
            private set => SetField(ref _logoImageInfo, value);
        }

        public bool HasLogoImageInfo => _logoImageInfo.Length > 0;

        /// <summary>
        /// What the file chip shows: the picked file's own name before a save, the
        /// stored asset's generated name after it. Never a full path — the path is
        /// how the file got here, not what the profile stores.
        /// </summary>
        public string LogoDisplayName
        {
            get
            {
                if (_pendingLogoPath.Length > 0)
                {
                    try { return Path.GetFileName(_pendingLogoPath); }
                    catch (ArgumentException) { return ""; }
                }

                return _storedLogoFileName;
            }
        }

        /// <summary>The picked image's width ÷ height, used by the preview panel.</summary>
        public double LogoAspectRatio
        {
            get => _logoAspectRatio;
            private set => SetField(ref _logoAspectRatio, value);
        }

        public string LogoPlacementHint => CompanyLogoLayouts.Description(_logoLayout);

        public CompanyLogoLayout LogoLayout
        {
            get => _logoLayout;
            set
            {
                if (!SetField(ref _logoLayout, value)) return;
                OnPropertyChanged(nameof(IsNoLogoLayout));
                OnPropertyChanged(nameof(IsLeftPortraitLayout));
                OnPropertyChanged(nameof(IsTopPanelLayout));
                OnPropertyChanged(nameof(LogoPlacementHint));
                Touch(nameof(CompanyProfileDraft.LogoLayout));
            }
        }

        public bool IsNoLogoLayout
        {
            get => _logoLayout == CompanyLogoLayout.None;
            set { if (value) LogoLayout = CompanyLogoLayout.None; }
        }

        public bool IsLeftPortraitLayout
        {
            get => _logoLayout == CompanyLogoLayout.LeftPortrait;
            set { if (value) LogoLayout = CompanyLogoLayout.LeftPortrait; }
        }

        public bool IsTopPanelLayout
        {
            get => _logoLayout == CompanyLogoLayout.TopPanel;
            set { if (value) LogoLayout = CompanyLogoLayout.TopPanel; }
        }

        /// <summary>
        /// Printed size of the logo's long edge, in millimetres.
        /// </summary>
        /// <remarks>
        /// One dimension, not two: the other one follows the image's own aspect
        /// ratio, so no stored value can stretch a logo. The size picker and the
        /// rules layer clamp it to the range the renderer uses.
        /// </remarks>
        public double LogoSizeMm
        {
            get => _logoSizeMm;
            set
            {
                double clamped = CompanyProfileRules.ClampLogoSize(value);
                if (!SetField(ref _logoSizeMm, clamped)) return;
                OnPropertyChanged(nameof(LogoSizeText));
                OnPropertyChanged(nameof(SelectedLogoSizeOption));
                Touch(nameof(CompanyProfileDraft.LogoSizeMm));
            }
        }

        public string LogoSizeText => _logoSizeMm.ToString("0.#", CultureInfo.InvariantCulture) + " mm";

        /// <summary>
        /// The sizes offered for the printed logo. A picker rather than a slider:
        /// every installed theme templates a ComboBox, none templates a Slider, and
        /// an untemplated control would look foreign in eight of the nine.
        /// </summary>
        public IReadOnlyList<LogoSizeChoice> LogoSizeOptions { get; } = new[]
        {
            new LogoSizeChoice(16),
            new LogoSizeChoice(24),
            new LogoSizeChoice(32),
            new LogoSizeChoice(40),
            new LogoSizeChoice(56)
        };

        /// <summary>The offered size closest to the stored one.</summary>
        public LogoSizeChoice SelectedLogoSizeOption
        {
            get
            {
                LogoSizeChoice? closest = null;
                double best = double.MaxValue;
                foreach (LogoSizeChoice choice in LogoSizeOptions)
                {
                    double distance = Math.Abs(choice.Millimetres - _logoSizeMm);
                    if (distance >= best) continue;
                    best = distance;
                    closest = choice;
                }

                return closest ?? LogoSizeOptions[1];
            }
            set
            {
                if (value == null) return;
                LogoSizeMm = value.Millimetres;
                OnPropertyChanged(nameof(SelectedLogoSizeOption));
            }
        }

        // ── Document preview ─────────────────────────────────────────────
        // The preview beside the fields is the point of the logo section: it shows
        // the SAME identity in the chosen composition, so "portrait" and "top
        // masthead" are visible decisions rather than two words in a list.

        public string PreviewCompanyName =>
            _companyName.Trim().Length > 0 ? _companyName.Trim() : "Your company name";

        public string PreviewTrnLine =>
            _trn.Trim().Length > 0 ? "TRN " + _trn.Trim() : "TRN not set";

        /// <summary>Address first line, telephone and e-mail — whatever is filled in.</summary>
        public string PreviewContactLine
        {
            get
            {
                var parts = new List<string>(4);
                string firstAddressLine = _address.Replace("\r\n", "\n").Split('\n')[0].Trim();
                if (firstAddressLine.Length > 0) parts.Add(firstAddressLine);
                if (_telephone.Trim().Length > 0) parts.Add(_telephone.Trim());
                if (_email.Trim().Length > 0) parts.Add(_email.Trim());
                if (_website.Trim().Length > 0) parts.Add(_website.Trim());
                return parts.Count == 0 ? "Address, telephone and e-mail appear here" : string.Join("  ·  ", parts);
            }
        }

        public string PreviewRepresentativeLine
        {
            get
            {
                var parts = new List<string>(2);
                if (_representativeName.Trim().Length > 0) parts.Add(_representativeName.Trim());
                if (_representativeMobile.Trim().Length > 0) parts.Add(_representativeMobile.Trim());
                return string.Join("  ·  ", parts);
            }
        }

        public bool HasPreviewRepresentativeLine => PreviewRepresentativeLine.Length > 0;

        /// <summary>True when there is something to print in the page footer.</summary>
        public bool HasPreviewContactLine => PreviewContactLine.Length > 0;

        /// <summary>The address, split for the letterhead mock: two lines is what it prints.</summary>
        public string PreviewAddressLine1 => AddressLines().Count > 0 ? AddressLines()[0] : "";

        public string PreviewAddressLine2 => AddressLines().Count > 1 ? AddressLines()[1] : "";

        public bool HasPreviewAddressLine1 => PreviewAddressLine1.Length > 0;

        public bool HasPreviewAddressLine2 => PreviewAddressLine2.Length > 0;

        /// <summary>
        /// The address as the letterhead prints it. Blank lines are skipped here —
        /// which is why NormalizeAddress must drop them too: the stored value and
        /// the page it feeds have to be the same three lines (round 34.5).
        /// </summary>
        private List<string> AddressLines()
        {
            var lines = new List<string>(3);
            foreach (string line in _address.Replace("\r\n", "\n").Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length > 0) lines.Add(trimmed);
            }

            return lines;
        }

        public double LogoSizeMinimum => CompanyProfileRules.MinLogoSizeMm;

        public double LogoSizeMaximum => CompanyProfileRules.MaxLogoSizeMm;
        // ── Logo actions ─────────────────────────────────────────────────

        /// <summary>
        /// Records a picked image. Nothing is copied and nothing is saved until
        /// <see cref="SaveAsync"/> runs.
        /// </summary>
        public void SetPickedLogo(string? path)
        {
            string? problem = CompanyLogoStore.ValidateSource(path);
            if (problem != null)
            {
                ErrorMessage = problem;
                return;
            }

            _pendingLogoPath = path!.Trim();
            _logoRemoved = false;
            if (_logoLayout == CompanyLogoLayout.None)
                _logoLayout = CompanyLogoLayout.LeftPortrait;

            OnPropertyChanged(nameof(PendingLogoPath));
            OnPropertyChanged(nameof(LogoDisplayName));
            OnPropertyChanged(nameof(HasLogo));
            OnPropertyChanged(nameof(LogoLayout));
            OnPropertyChanged(nameof(IsNoLogoLayout));
            OnPropertyChanged(nameof(IsLeftPortraitLayout));
            OnPropertyChanged(nameof(IsTopPanelLayout));
            OnPropertyChanged(nameof(LogoPlacementHint));
            RefreshLogoPreview();
            ErrorMessage = "";
            MarkDirty();
        }

        /// <summary>
        /// Removes the logo from the working copy. The stored asset is deleted
        /// only when the save succeeds.
        /// </summary>
        public void RemoveLogo()
        {
            if (!HasLogo && _pendingLogoPath.Length == 0)
            {
                return;
            }

            _pendingLogoPath = "";
            _logoRemoved = true;
            _logoLayout = CompanyLogoLayout.None;

            OnPropertyChanged(nameof(PendingLogoPath));
            OnPropertyChanged(nameof(LogoDisplayName));
            OnPropertyChanged(nameof(HasLogo));
            OnPropertyChanged(nameof(LogoLayout));
            OnPropertyChanged(nameof(IsNoLogoLayout));
            OnPropertyChanged(nameof(IsLeftPortraitLayout));
            OnPropertyChanged(nameof(IsTopPanelLayout));
            OnPropertyChanged(nameof(LogoPlacementHint));
            RefreshLogoPreview();
            MarkDirty();
        }

        /// <summary>
        /// Which file the preview should show right now. The stored name resolves
        /// to its path even when the file has gone: the loader then reports "the
        /// image file is not on disk", which is the only way the operator learns
        /// that a logo they still see referenced in the database is missing. An
        /// existence check here would turn that into silence (round 34.3).
        /// </summary>
        private string? PreviewSourcePath =>
            _pendingLogoPath.Length > 0
                ? _pendingLogoPath
                : _logoRemoved
                    ? null
                    : CompanyLogoStore.PathForStoredName(_storedLogoFileName);

        private void RefreshLogoPreview()
        {
            string? path = PreviewSourcePath;

            if (path == null)
            {
                LogoPreview = null;
                LogoImageInfo = "";
                LogoPreviewProblem = "";
                LogoAspectRatio = 1;
                OnPropertyChanged(nameof(HasLogoPreview));
                return;
            }

            CompanyLogoPreview preview = CompanyLogoPreviewLoader.Load(path);
            LogoPreview = preview.Image;
            LogoAspectRatio = preview.AspectRatio;
            LogoPreviewProblem = preview.Available
                ? ""
                : (preview.Problem.Length > 0
                    ? preview.Problem
                    : "The stored logo file is missing. Choose the image again, or remove the logo.");
            LogoImageInfo = preview.Available
                ? preview.PixelWidth.ToString(CultureInfo.InvariantCulture) + " × "
                  + preview.PixelHeight.ToString(CultureInfo.InvariantCulture) + " px"
                : "";
            OnPropertyChanged(nameof(HasLogoPreview));
        }
    }
}
