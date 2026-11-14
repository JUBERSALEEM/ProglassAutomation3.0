using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.ViewModels;

namespace ProGlassAutomation.ViewModels.Shared
{
    /// <summary>One selectable print layout, as the picker shows it.</summary>
    public sealed class PrintLayoutOption
    {
        public PrintLayoutOption(string id, string name, string description, string kind)
        {
            Id = id;
            Name = name;
            Description = description;
            Kind = kind;
        }

        public string Id { get; }
        public string Name { get; }
        public string Description { get; }

        /// <summary>FormalBand / SplitConsole / Editorial / Workshop / QuietForm.</summary>
        public string Kind { get; }

        /// <summary>
        /// Defence in depth against the "layout shows as a type name" bug
        /// (owner-reported: 'ProGlassAutomationViewModelsSharedPrintLayoutOption').
        /// WPF renders item.ToString() whenever DisplayMemberPath cannot
        /// resolve in a given context (template edge cases, selection echo,
        /// logging, agent screen-readers). The default implementation
        /// returns the fully-qualified type name — a debug string leaking
        /// onto a customer screen. Overriding it means NO rendering path
        /// can ever show the type name again: the human-readable layout
        /// name wins everywhere, always.
        /// </summary>
        public override string ToString() => Name;
    }

    /// <summary>
    /// Drives the live PDF-layout switcher on the Step 7 preview.
    ///
    /// WHY THIS EXISTS
    /// ───────────────
    /// The five print layouts were only reachable from Settings → Appearance,
    /// so an operator could never see what a layout did to the document they
    /// were about to send. This puts the choice on the preview itself: pick a
    /// layout, watch the paper re-skin, then export — and the export is
    /// guaranteed to match, because both sides read DocumentStyle.
    ///
    /// WHY IT PERSISTS
    /// ───────────────
    /// PdfExportEngine calls PdfThemeCatalog.ApplySaved() before rendering,
    /// which re-reads the saved id. A switcher that only changed the
    /// in-memory theme would therefore be silently reverted at export time —
    /// the exact preview/export divergence this whole change exists to kill.
    /// So selecting a layout writes the same setting Settings writes.
    /// </summary>
    public class PrintLayoutViewModel : ViewModelBase
    {
        private string _selectedId;
        private bool _suppress;

        public PrintLayoutViewModel()
        {
            Layouts = new ObservableCollection<PrintLayoutOption>(
                PdfThemeCatalog.All.Select(t => new PrintLayoutOption(
                    t.Id, t.DisplayName, t.Description, t.Kind.ToString())));

            _selectedId = ResolveSavedId();

            SelectLayoutCommand = new RelayCommand(
                p => SelectedId = p as string ?? string.Empty);

            // Follow changes made anywhere else (Settings → Appearance), so
            // the picker never shows a layout the document is not using.
            DocumentStyle.Changed += OnDocumentLayoutChanged;
        }

        public ObservableCollection<PrintLayoutOption> Layouts { get; }

        public RelayCommand SelectLayoutCommand { get; }

        /// <summary>Id of the active layout, e.g. "ClassicNavy".</summary>
        public string SelectedId
        {
            get => _selectedId;
            set
            {
                var theme = PdfThemeCatalog.Resolve(value);
                if (string.Equals(_selectedId, theme.Id, StringComparison.OrdinalIgnoreCase))
                    return;

                _selectedId = theme.Id;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedName));
                OnPropertyChanged(nameof(SelectedDescription));
                OnPropertyChanged(nameof(Kind));
                OnPropertyChanged(nameof(SelectedStyleLabel));

                ApplyAndPersist(theme);
            }
        }

        /// <summary>
        /// The active layout's shape family (FormalBand / SplitConsole /
        /// Editorial / Workshop / QuietForm). Drives derived rule weights in
        /// PdfMetrics and the shared chrome used by the non-PI modules.
        /// Kept for compatibility only — the Step 7 UI surfaces
        /// <see cref="SelectedStyleLabel"/>, never this raw enum name.
        /// </summary>
        public string Kind => DocumentStyle.Kind.ToString();

        public string SelectedName => DocumentStyle.Current.DisplayName;

        public string SelectedDescription => DocumentStyle.Current.Description;

        /// <summary>
        /// Curated style family shown on the preview badge — EXECUTIVE,
        /// MODERN, EDITORIAL, INDUSTRIAL or MINIMAL. Never a type name and
        /// never the raw PdfLayoutKind enum member ("FormalBand",
        /// "SplitConsole"…), which operators read as a leaked debug string
        /// (owner-reported issue 1).
        /// </summary>
        public string SelectedStyleLabel => DocumentStyle.Current.StyleLabel;

        private void ApplyAndPersist(PdfTheme theme)
        {
            if (_suppress) return;

            DocumentStyle.Apply(theme);

            var config = App.Config;
            if (config?.Appearance != null
                && !string.Equals(config.Appearance.PdfLayoutId, theme.Id,
                                  StringComparison.OrdinalIgnoreCase))
            {
                config.Appearance.PdfLayoutId = theme.Id;
                try
                {
                    Services.AI.AIConfig.ConfigService.SaveDebounced(config);
                    Services.Audit.AuditService.Log(
                        Services.Audit.AuditSections.Settings,
                        "PDF layout changed (preview switcher)",
                        theme.DisplayName);
                }
                catch (Exception ex)
                {
                    // A failed save must not block the on-screen change.
                    Services.Logging.AppLogService.Warning(
                        "[PrintLayoutViewModel] persist failed: " + ex.Message);
                }
            }
        }

        private void OnDocumentLayoutChanged(PdfTheme theme)
        {
            if (theme == null) return;
            if (string.Equals(_selectedId, theme.Id, StringComparison.OrdinalIgnoreCase))
                return;

            _suppress = true;
            try
            {
                _selectedId = theme.Id;
            }
            finally
            {
                _suppress = false;
            }

            OnPropertyChanged(nameof(SelectedId));
            OnPropertyChanged(nameof(SelectedName));
            OnPropertyChanged(nameof(SelectedDescription));
            OnPropertyChanged(nameof(Kind));
            OnPropertyChanged(nameof(SelectedStyleLabel));
        }

        private static string ResolveSavedId()
        {
            try
            {
                return PdfThemeCatalog
                    .Resolve(App.Config?.Appearance?.PdfLayoutId).Id;
            }
            catch
            {
                // Design-time / config not ready — fall back to the catalog
                // default rather than failing the preview.
                return PdfThemeCatalog.Resolve(null).Id;
            }
        }
    }
}
