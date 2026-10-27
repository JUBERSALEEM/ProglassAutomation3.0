using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeStudio;

namespace ProGlassAutomation;

public partial class MainWindow : Window
{
    private readonly ThemeStudioViewModel viewModel;
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
    /// First run: if the user directory is still empty, open on the Users page.
    /// An empty directory means nobody can be identified and no administrator
    /// exists, so the shell must present the one thing that can be done —
    /// registering the workstation account — instead of dropping the operator on
    /// a home page whose every action is refused. It is a navigation decision
    /// only; the setup card, its validation and the write all belong to the
    /// Users module, so there is exactly one implementation of the step.
    /// </summary>
    private async void OnShellLoaded(object sender, RoutedEventArgs e)
    {
        Loaded -= OnShellLoaded;

        try
        {
            var repository = new UserRepository();
            await repository.InitializeAsync();
            if (await repository.IsFirstRunRequiredAsync())
                viewModel.SelectedWorkspace = 9;   // Users (MainWindow tab order)
        }
        catch (Exception error)
        {
            // Landing on the home page is a graceful fallback: the Users page
            // reports the same failure with its own error surface, and no setup
            // gate may bring the shell down.
            System.Diagnostics.Debug.WriteLine("First-run check skipped: " + error);
        }
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
