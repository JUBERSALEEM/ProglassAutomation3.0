using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Effects;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using ProGlassAutomation.Services.Users;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeStudio;
using ProGlassAutomation.ViewModels.Users;

namespace ProGlassAutomation;

public partial class MainWindow : Window
{
    private readonly ThemeStudioViewModel viewModel;
    private FirstRunViewModel? firstRun;
    private SignInViewModel? signIn;
    private readonly ShellSessionViewModel sessionBanner;
    private PreviewWindow? preview;
    private readonly BlurEffect spotlightBlur = new() { Radius = 16, KernelType = KernelType.Gaussian, RenderingBias = RenderingBias.Performance };

    public MainWindow(ThemeService themes)
    {
        InitializeComponent();
        if (FindResource("AppMark") is ImageSource mark) Icon = mark;
        DataContext = viewModel = new ThemeStudioViewModel(themes);
        viewModel.DialogRequested += OpenPreview;

        // Round 30: the shell shows the session itself, so the way in is never a
        // page that happens to be open. Created here because it cannot fail —
        // it is strings and a read of AuthenticationContext, no database, no disk.
        sessionBanner = new ShellSessionViewModel();
        SessionBanner.DataContext = sessionBanner;
        sessionBanner.ActionRequested += OnSessionActionRequested;
        sessionBanner.Refresh();
        OverflowSpotlight.Changed += OnSpotlight;
        SpotlightScrim.PreviewMouseLeftButtonDown += (_, _) => OverflowSpotlight.Set(false);
        Closed += (_, _) =>
        {
            OverflowSpotlight.Changed -= OnSpotlight;
            viewModel.DialogRequested -= OpenPreview;
            sessionBanner.ActionRequested -= OnSessionActionRequested;
            AuthenticationContext.SignedOut -= OnSignedOut;
            AuthenticationContext.SignInRequested -= OnSignInRequested;
            viewModel.Dispose();
        };

        // Signing out (from the Users page) must bring the gate straight back.
        AuthenticationContext.SignedOut += OnSignedOut;

        // ... and so must setting the first password from the page: the page is
        // reachable with no session when the first-run gate was dismissed, and
        // after that write the next step is to sign in, not to stay on a page
        // that cannot act.
        AuthenticationContext.SignInRequested += OnSignInRequested;

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
        sessionBanner.Refresh();

        try
        {
            var repository = new UserRepository();
            firstRun = new FirstRunViewModel(new FirstRunService(repository));
            FirstRunGate.DataContext = firstRun;
            firstRun.Completed += OnFirstRunCompleted;
            firstRun.Dismissed += OnFirstRunDismissed;

            bool show = await firstRun.InitializeAsync();
            FirstRunGate.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (show)
            {
                FirstRunGate.FocusFirstField();
                return;
            }
        }
        catch (Exception error)
        {
            // Round 30: "is this a first run" could not be answered. This used to
            // collapse BOTH gates, which left the shell with no session, no gate
            // and no control that could ask for one — read-only pages until the
            // application was restarted. The sign-in gate is the way in, so it is
            // opened with the reason on it; the Users page needs no rescue plan.
            FirstRunGate.Visibility = Visibility.Collapsed;
            System.Diagnostics.Debug.WriteLine("First-run gate skipped: " + error);
            await BeginSignInAsync(
                problem: "The user directory could not be read, so the first-run check could not be "
                       + "answered: " + error.Message + " Signing in may fail until that is fixed.");
            return;
        }

        // A directory with users but no verified session: the gate is the way in.
        // Nothing else is reachable until it hands the session over.
        await BeginSignInAsync();
    }

    /// <summary>
    /// Setup finished: close the gate and put the new administrator in front of
    /// the register, which is the page they now need — adding the rest of the team.
    /// </summary>
    private async void OnFirstRunCompleted(object? sender, EventArgs e)
    {
        FirstRunGate.Visibility = Visibility.Collapsed;

        // The administrator was just created with a password of their own; ask for
        // it once, so the session is established by a real sign-in rather than by
        // the act of creating the row.
        await BeginSignInAsync(firstRun?.OperatorUsername);
    }

    /// <summary>
    /// Shows the sign-in gate and waits for a verified password.
    /// </summary>
    /// <remarks>
    /// The session object is shared with the Users page (same repository, same
    /// process-wide authentication context), so once this returns a session the
    /// whole shell is operating as that person.
    ///
    /// Round 30: the gate is shown BEFORE the card is prepared and no failure
    /// below collapses it. Until now a failure here collapsed the only way into a
    /// session, and the shell sat there read-only with nothing to click — the
    /// defect the owner reported. A failure now lands on the card's own error
    /// surface, and if even the card cannot be built the banner says so.
    /// </remarks>
    private async Task BeginSignInAsync(string? prefillUsername = null, string? problem = null)
    {
        if (signIn == null)
        {
            try
            {
                var repository = new UserRepository();
                signIn = new SignInViewModel(repository, new UserSession(repository));
                signIn.SignedIn += OnSignedIn;
                SignInGate.DataContext = signIn;
            }
            catch (Exception error)
            {
                // Nothing exists to ask with. The banner still offers the step and
                // carries the reason; a blank card would say less and cannot be
                // pressed.
                sessionBanner.ReportProblem(
                    "The sign-in screen could not be created: " + error.Message);
                System.Diagnostics.Debug.WriteLine("Sign-in gate could not be created: " + error);
                return;
            }
        }

        // The door is open from here on: everything below can only add a message.
        SignInGate.Visibility = Visibility.Visible;
        sessionBanner.ClearProblem();

        try
        {
            await signIn.InitializeAsync(prefillUsername);
            if (!string.IsNullOrWhiteSpace(problem)) signIn.ReportProblem(problem!);
        }
        catch (Exception error)
        {
            signIn.ReportProblem(
                "The sign-in screen could not be prepared: " + error.Message);
            System.Diagnostics.Debug.WriteLine("Sign-in gate could not be prepared: " + error);
        }

        SignInGate.FocusFirstField();
    }

    private void OnSignedIn(object? sender, EventArgs e)
    {
        SignInGate.Visibility = Visibility.Collapsed;
        sessionBanner.ClearProblem();
        sessionBanner.Refresh();

        // Land on the register: that is the page a person needs first, and it is
        // the only page that depends on who is signed in.
        viewModel.SelectedWorkspace = 9;   // Users (MainWindow tab order)
    }

    private void OnSignInRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnSignInRequested(sender, e));
            return;
        }

        // Prefill the account: it is the one that just set its own password, and the
        // sign-in screen is asking for a password, not for a name to be retyped.
        _ = BeginSignInAsync(ProGlassAutomation.Services.Audit.CurrentOperator.Username);
    }

    private void OnSignedOut(object? sender, EventArgs e)
    {
        // Raised from the Users page; marshal to the UI thread before touching
        // any control.
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnSignedOut(sender, e));
            return;
        }

        sessionBanner.Refresh();
        _ = BeginSignInAsync();
    }

    /// <summary>
    /// The banner's one session action. It is the shell's decision, not the view
    /// model's: show the gate when the way in is needed, end the session when the
    /// person is finished (signing out raises SignedOut, which opens the gate).
    /// </summary>
    private void OnSessionActionRequested(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnSessionActionRequested(sender, e));
            return;
        }

        if (AuthenticationContext.IsSignedIn)
        {
            AuthenticationContext.SignOut();
            return;
        }

        _ = BeginSignInAsync();
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
