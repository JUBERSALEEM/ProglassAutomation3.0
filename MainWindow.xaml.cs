using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Users;

namespace ProGlassAutomation;

public partial class MainWindow : Window
{
    private readonly ThemeStudioViewModel viewModel;
    private FirstRunViewModel? firstRun;
    private PreviewWindow? preview;
    private readonly BlurEffect spotlightBlur = new() { Radius = 16, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };

    public MainWindow(ThemeService themes)
    {
        InitializeComponent();
        if (FindResource("AppMark") is ImageSource mark) Icon = mark;
        DataContext = viewModel = new ThemeStudioViewModel(themes);
        viewModel.DialogRequested += OpenPreview;
        OverflowSpotlight.Changed += OnSpotlight;
        SpotlightScrim.PreviewMouseLeftButtonDown += (_, _) => OverflowSpotlight.Set(false);
        Closed += (_, _) =>
        {
            OverflowSpotlight.Changed -= OnSpotlight;
            viewModel.DialogRequested -= OpenPreview;
            viewModel.Dispose();
        };

        // First run is decided once the shell is up: the check reads the
        // database, and doing that before the window exists would delay startup
        // for every launch.
        Loaded += OnShellLoaded;
    }

    /// <summary>
    /// First run. A directory with no users cannot be administered: nobody can be
    /// identified, so no administrator can exist, and "ask an administrator" is
    /// circular. The gate is shown before the shell is usable and registers the
    /// Windows account this workstation already runs under as the first
    /// Administrator. Every decision behind it lives in IFirstRunService.
    /// </summary>
    private async void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnShellLoaded;

        try
        {
            var repository = new UserRepository();
            firstRun = new FirstRunViewModel(new FirstRunService(repository));
            FirstRunGate.DataContext = firstRun;
            firstRun.Completed += OnFirstRunCompleted;
            firstRun.Dismissed += OnFirstRunDismissed;

            bool show = await firstRun.InitializeAsync();
            FirstRunGate.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            if (show) FirstRunGate.FocusFirstField();
        }
        catch (Exception error)
        {
            // The gate must never take the shell down. If it cannot even be
            // prepared, the Users page still offers the same step and reports
            // the same failure in its own error surface.
            FirstRunGate.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine("First-run gate skipped: " + error);
        }
    }

    /// <summary>
    /// Setup finished: close the gate and put the new administrator in front of
    /// the register, which is the page they now need — adding the rest of the team.
    /// </summary>
    private void OnFirstRunCompleted(object? sender, EventArgs e)
    {
        FirstRunGate.Visibility = Visibility.Collapsed;
        viewModel.SelectedWorkspace = 9;   // Users (MainWindow tab order)
    }

    /// <summary>
    /// "Set up later": close the gate and land on the Users page, where the same
    /// step is offered as a card. Deferring must never hide the only way in.
    /// </summary>
    private void OnFirstRunDismissed(object? sender, EventArgs e)
    {
        FirstRunGate.Visibility = Visibility.Collapsed;
        viewModel.SelectedWorkspace = 9;   // Users
    }

    private void OnSpotlight(bool on)
    {
        if (!on)
        {
            SpotlightSnap.Effect = null;
            SpotlightSnap.Background = null;
            SpotlightScrim.Visibility = Visibility.Collapsed;
            return;
        }
        SpotlightSnap.Effect = null;
        try
        {
            var dpi = VisualTreeHelper.GetDpi(ShellBody);
            int width = Math.Max(1, (int)Math.Ceiling(ShellBody.ActualWidth * dpi.DpiScaleX));
            int height = Math.Max(1, (int)Math.Ceiling(ShellBody.ActualHeight * dpi.DpiScaleY));
            var bitmap = new RenderTargetBitmap(width, height, 96 * dpi.DpiScaleX, 96 * dpi.DpiScaleY, PixelFormats.Pbgra32);
            bitmap.Render(ShellBody);
            bitmap.Freeze();
            SpotlightSnap.Background = new ImageBrush(bitmap) { Stretch = Stretch.Fill, AlignmentX = AlignmentX.Left, AlignmentY = AlignmentY.Top };
            SpotlightSnap.Effect = spotlightBlur;
        }
        catch (InvalidOperationException)
        {
            SpotlightSnap.SetResourceReference(Border.BackgroundProperty, "ApplicationBackground");
        }
        SpotlightScrim.Visibility = Visibility.Visible;
    }

    protected override void OnManipulationBoundaryFeedback(ManipulationBoundaryFeedbackEventArgs e)
    {
        e.Handled = true;
    }

    private void OpenPreview(object? sender, EventArgs e)
    {
        if (preview is not null) { preview.Activate(); return; }
        preview = new PreviewWindow { Owner = this, DataContext = viewModel };
        preview.Closed += (_, _) => preview = null;
        preview.Show();
    }
}
