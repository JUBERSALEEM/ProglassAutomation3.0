// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Features/ProformaFieldVisibility.cs
// PROJECT: ProGlassAutomation
// MODULE: Feature flags — Proforma Invoice field visibility
//
// PURPOSE
// ───────
// Runtime, WPF-bindable mirror of AppConfig.ProformaInvoiceFields.
// Each optional field in Steps 1-3 of the Proforma Invoice wizard has
// a bool property here that XAML binds to via {x:Static ...}. When the
// Settings ‣ Features page saves a new value, this singleton fires
// PropertyChanged and the wizard hides / shows the field WITHOUT an
// app restart.
//
// DESIGN CONTRACT
// ───────────────
//   • Singleton — the whole app reads the same instance so bindings
//     never diverge across shell / wizard / settings.
//   • Refresh() re-reads App.Config.ProformaInvoiceFields and raises
//     PropertyChanged for every property. Called from the Settings
//     view-model right after ConfigService.Save.
//   • Never throws — a null App.Config falls back to defaults (all
//     visible), matching the "opt-out" contract in
//     ProformaInvoiceFeatureFlags.
//   • Thread affinity — Refresh() marshals to the WPF Dispatcher when
//     called off-thread; XAML bindings can safely read anytime.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.AI.AIConfig;
using System;
using System.ComponentModel;
using System.Windows;

namespace ProGlassAutomation.Services.Features
{
    /// <summary>
    /// Bindable, singleton mirror of the Proforma Invoice per-field
    /// visibility settings. Bind via
    /// <c>{Binding Path=ShowXxx, Source={x:Static features:ProformaFieldVisibility.Instance},
    /// Converter={StaticResource BoolToVisibilityConverter}}</c>.
    /// </summary>
    public sealed class ProformaFieldVisibility : INotifyPropertyChanged
    {
        private static readonly Lazy<ProformaFieldVisibility> _instance =
            new(() => new ProformaFieldVisibility());

        /// <summary>Process-wide singleton.</summary>
        public static ProformaFieldVisibility Instance => _instance.Value;

        public event PropertyChangedEventHandler? PropertyChanged;

        private ProformaFieldVisibility()
        {
            // Populate from the currently loaded config (may be null
            // if this runs before App.Startup finished — the Read()
            // helper tolerates that and returns defaults).
            CopyFromConfig();
        }

        // ── Step 1: Company ────────────────────────────────────────
        public bool ShowCompanyTrn { get; private set; } = true;
        public bool ShowCompanyLocation { get; private set; } = true;
        public bool ShowRepresentative { get; private set; } = true;
        public bool ShowRepresentativeMobile { get; private set; } = true;
        public bool ShowCompanyEmail { get; private set; } = true;
        public bool ShowBankName { get; private set; } = true;
        public bool ShowAccountNumber { get; private set; } = true;
        public bool ShowIssueDate { get; private set; } = true;
        public bool ShowValidUntil { get; private set; } = true;
        public bool ShowProformaStatus { get; private set; } = true;

        // ── Step 2: Client ─────────────────────────────────────────
        public bool ShowCustomerTrn { get; private set; } = true;
        public bool ShowCustomerReferenceCode { get; private set; } = true;
        public bool ShowSalesRepresentative { get; private set; } = true;
        public bool ShowDocumentPreparedBy { get; private set; } = true;
        public bool ShowAttentionExecutive { get; private set; } = true;
        public bool ShowClientContactPhone { get; private set; } = true;
        public bool ShowBillingAddress { get; private set; } = true;

        // ── Step 3: Project ────────────────────────────────────────
        public bool ShowProjectName { get; private set; } = true;
        public bool ShowProjectReference { get; private set; } = true;
        public bool ShowContractNo { get; private set; } = true;
        public bool ShowLpoNumber { get; private set; } = true;
        public bool ShowSiteDeliveryLocation { get; private set; } = true;

        /// <summary>
        /// True when at least one field is hidden. The wizard uses
        /// this to hint "Some fields are hidden — enable them in
        /// Settings → Features → Proforma Invoice."
        /// </summary>
        public bool HasAnyHiddenField
        {
            get
            {
                return !(ShowCompanyTrn && ShowCompanyLocation
                    && ShowRepresentative && ShowRepresentativeMobile
                    && ShowCompanyEmail && ShowBankName && ShowAccountNumber
                    && ShowIssueDate && ShowValidUntil && ShowProformaStatus
                    && ShowCustomerTrn && ShowCustomerReferenceCode
                    && ShowSalesRepresentative && ShowDocumentPreparedBy
                    && ShowAttentionExecutive && ShowClientContactPhone
                    && ShowBillingAddress
                    && ShowProjectName && ShowProjectReference
                    && ShowContractNo && ShowLpoNumber
                    && ShowSiteDeliveryLocation);
            }
        }

        /// <summary>
        /// Re-reads <see cref="AppConfig.ProformaInvoiceFields"/> and
        /// raises PropertyChanged for every bindable property. Safe to
        /// call from any thread — marshals to the WPF Dispatcher when
        /// necessary. Called by SettingsViewModel right after each
        /// ConfigService.Save so the wizard reacts live.
        /// </summary>
        public void Refresh()
        {
            var dispatcher = Application.Current?.Dispatcher;
            if (dispatcher != null && !dispatcher.CheckAccess())
            {
                dispatcher.BeginInvoke(new Action(RefreshCore));
                return;
            }

            RefreshCore();
        }

        private void RefreshCore()
        {
            CopyFromConfig();
            RaiseAllChanged();
        }

        private void CopyFromConfig()
        {
            var src = App.Config?.ProformaInvoiceFields
                ?? new ProformaInvoiceFeatureFlags();

            ShowCompanyTrn = src.ShowCompanyTrn;
            ShowCompanyLocation = src.ShowCompanyLocation;
            ShowRepresentative = src.ShowRepresentative;
            ShowRepresentativeMobile = src.ShowRepresentativeMobile;
            ShowCompanyEmail = src.ShowCompanyEmail;
            ShowBankName = src.ShowBankName;
            ShowAccountNumber = src.ShowAccountNumber;
            ShowIssueDate = src.ShowIssueDate;
            ShowValidUntil = src.ShowValidUntil;
            ShowProformaStatus = src.ShowProformaStatus;

            ShowCustomerTrn = src.ShowCustomerTrn;
            ShowCustomerReferenceCode = src.ShowCustomerReferenceCode;
            ShowSalesRepresentative = src.ShowSalesRepresentative;
            ShowDocumentPreparedBy = src.ShowDocumentPreparedBy;
            ShowAttentionExecutive = src.ShowAttentionExecutive;
            ShowClientContactPhone = src.ShowClientContactPhone;
            ShowBillingAddress = src.ShowBillingAddress;

            ShowProjectName = src.ShowProjectName;
            ShowProjectReference = src.ShowProjectReference;
            ShowContractNo = src.ShowContractNo;
            ShowLpoNumber = src.ShowLpoNumber;
            ShowSiteDeliveryLocation = src.ShowSiteDeliveryLocation;
        }

        private void RaiseAllChanged()
        {
            var handler = PropertyChanged;
            if (handler == null) return;

            // Explicit list keeps the intent visible and avoids any
            // reflection cost on every save. If a new flag is added,
            // adding a line here is the reminder to keep bindings
            // in sync.
            handler(this, new PropertyChangedEventArgs(nameof(ShowCompanyTrn)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowCompanyLocation)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowRepresentative)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowRepresentativeMobile)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowCompanyEmail)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowBankName)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowAccountNumber)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowIssueDate)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowValidUntil)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowProformaStatus)));

            handler(this, new PropertyChangedEventArgs(nameof(ShowCustomerTrn)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowCustomerReferenceCode)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowSalesRepresentative)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowDocumentPreparedBy)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowAttentionExecutive)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowClientContactPhone)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowBillingAddress)));

            handler(this, new PropertyChangedEventArgs(nameof(ShowProjectName)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowProjectReference)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowContractNo)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowLpoNumber)));
            handler(this, new PropertyChangedEventArgs(nameof(ShowSiteDeliveryLocation)));

            handler(this, new PropertyChangedEventArgs(nameof(HasAnyHiddenField)));
        }
    }
}
