// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.d.2.CompanyProfileView.xaml.cs
// MODULE: step4 — the Company Profile page's composition code.
//
// Code-behind here does three things and nothing else:
//   1. builds the view model and reads the profile the first time the tab is
//      shown (and again on return, but only when there is nothing unsaved);
//   2. shows the file picker, because a view model cannot open a dialog and a
//      test must not be able to — and it attaches that handler on EVERY load,
//      because this view lives in a TabControl: the WPF tab template hosts one
//      content presenter, so leaving the Company tab UNLOADS this view and
//      coming back LOADS it again. Round 32: the handler was attached once in
//      the constructor and removed on Unloaded, so after the first visit to
//      another tab "Choose image" opened nothing at all — a dead button with no
//      message, which is exactly what the owner reported;
//   3. contains every failure. A database that will not open must report on the
//      page's error surface, never take the shell down from an async void.
//   4. reflows the BODY between two columns and one. Above 940 px of content
//      width the mockup's arrangement is used — form left, 300 px logo panel
//      right. Below it the panel moves UNDER the form instead of squeezing the
//      form into 100 px columns. WPF has no media queries, and a fixed two-column
//      layout is simply not usable at WindowMinWidth with the sidebar expanded,
//      so this is the one piece of layout that must be code.
//
// Every rule, every query and every write lives in step4.a / step4.b / step4.c.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.Win32;
using ProGlassAutomation.ViewModels.Company;
using System;
using System.Windows;
using System.Windows.Controls;
using ProGlassAutomation.Diagnostics;

namespace ProGlassAutomation.Company
{
    /// <summary>
    /// step4 Company Profile: the one page that owns the installation's own
    /// identity, and the logo that prints on every document.
    /// </summary>
    public partial class CompanyProfileView : UserControl
    {
        /// <summary>
        /// Below this much content width the two-column arrangement stops being
        /// usable: the form would be squeezed under ~480 px while the panel keeps
        /// its 300. Measured on the BODY, so the sidebar's state is already
        /// accounted for — collapsing the sidebar buys width back automatically.
        /// </summary>
        private const double TwoColumnMinimumWidth = 940;

        /// <summary>The approved mockup fixes the panel's width at 300 px.</summary>
        private const double PanelWidth = 300;

        private readonly CompanyProfileViewModel _viewModel;
        private bool _loadedOnce;
        private bool _twoColumn = true;
        private bool _logoPickAttached;

        public CompanyProfileView()
        {
            InitializeComponent();

            _viewModel = new CompanyProfileViewModel();
            DataContext = _viewModel;

            Loaded += OnLoaded;
            IsVisibleChanged += OnVisibilityChanged;
            Unloaded += OnUnloaded;
        }

        private async void OnLoaded(object sender, RoutedEventArgs e)
        {
            _loadedOnce = true;

            // Every load, not just the first: see the file header. Attaching twice
            // would open two dialogs, so the flag is what makes this idempotent.
            AttachLogoPick();

            // Who may edit is a process-wide fact, and this view may be shown long
            // after the session changed. Re-read it on every load so "Choose image"
            // and the fields are never left disabled by a stale answer.
            _viewModel.RefreshAuthorization();
            try
            {
                // EnsureLoadedAsync keeps its own failures on the page's error
                // surface, so there is nothing to rethrow here.
                await _viewModel.EnsureLoadedAsync();
            }
            catch (Exception error)
            {
                // Defensive: an async void handler that lets an exception escape
                // terminates the process.
                AppLog.Error("step4.company.load", error, "the Company page showed the failure on its own surface");
            }
        }

        /// <summary>
        /// Coming back to the tab re-reads the profile, so a change made by
        /// another window is not shown stale — but ONLY when this page holds no
        /// unsaved edits. Reloading over an edit in progress would silently
        /// discard the operator's typing, which is exactly the behaviour a
        /// settings page must not have.
        /// </summary>
        private async void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if (e.NewValue is not true) return;

            _viewModel.RefreshAuthorization();
            if (!_loadedOnce || !IsLoaded) return;
            if (_viewModel.HasUnsavedChanges) return;

            try
            {
                await _viewModel.ReloadAsync();
            }
            catch (Exception error)
            {
                AppLog.Error("step4.company.refresh", error, "returning to the Company page could not reload the profile");
            }
        }

        /// <summary>
        /// Reflows the body when the content width crosses the threshold.
        /// </summary>
        /// <remarks>
        /// Only a CROSSING does any work, and XAML already declares the wide
        /// arrangement, so the first measurement at a wide window costs nothing.
        /// Measured on the grid itself, which is already inside the page padding
        /// and excludes the scrollbar: no arithmetic on someone else's size.
        /// </remarks>
        private void OnBodySizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (e.WidthChanged) ApplyArrangement(e.NewSize.Width >= TwoColumnMinimumWidth);
        }

        private void ApplyArrangement(bool twoColumn)
        {
            if (twoColumn == _twoColumn) return;
            _twoColumn = twoColumn;

            // Move first, then rebuild the columns: an index beyond the current
            // column count is clamped, and clamping is not an error worth having.
            Grid.SetColumn(LogoPanel, twoColumn ? 1 : 0);
            Grid.SetRow(LogoPanel, twoColumn ? 0 : 1);

            BodyGrid.ColumnDefinitions.Clear();
            BodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            if (twoColumn)
                BodyGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            // The panel keeps its 300 px only while it has a column of its own;
            // stacked, it takes the card width instead of leaving a dead gutter.
            LogoPanel.Width = twoColumn ? PanelWidth : double.NaN;

            // The gap between the two cards is the contract's direction token:
            // side by side it is a RIGHT gap, stacked it is a BOTTOM gap. Swapping
            // which token the card carries keeps one 16 px gutter either way —
            // giving both cards a margin would double it to 32.
            FormCard.SetResourceReference(MarginProperty, twoColumn ? "UiGapCluster" : "UiGapGroup");
        }

        private void OnUnloaded(object sender, RoutedEventArgs e)
        {
            // The view model is owned by this view; unsubscribing keeps the
            // handler from outliving it. OnLoaded attaches it again — the pair is
            // what makes leaving and returning to the tab safe in both directions.
            DetachLogoPick();
        }

        private void AttachLogoPick()
        {
            if (_logoPickAttached) return;
            _viewModel.RequestLogoPick += OnRequestLogoPick;
            _logoPickAttached = true;
        }

        private void DetachLogoPick()
        {
            if (!_logoPickAttached) return;
            _viewModel.RequestLogoPick -= OnRequestLogoPick;
            _logoPickAttached = false;
        }

        /// <summary>
        /// The file picker for the logo. Filtering here is a convenience only:
        /// step4.a.3 refuses anything that is not a PNG or JPEG by its extension
        /// and step4.c.2 refuses to decode a file that is not really an image, so
        /// a renamed file cannot get past the dialog.
        /// </summary>
        private void OnRequestLogoPick(object? sender, EventArgs e)
        {
            var dialog = new OpenFileDialog
            {
                Title = "Choose the company logo",
                Filter = "PNG or JPEG image (*.png;*.jpg;*.jpeg)|*.png;*.jpg;*.jpeg",
                CheckFileExists = true,
                Multiselect = false
            };

            bool? chosen = dialog.ShowDialog(Window.GetWindow(this));
            if (chosen != true) return;

            // Picking does not save. The image is previewed and the stored logo
            // stays untouched until Save succeeds.
            _viewModel.SetPickedLogo(dialog.FileName);
        }
    }
}
