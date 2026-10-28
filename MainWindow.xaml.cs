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
using ProGlassAutomation.Diagnostics;

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
            AuthenticationContext.SignedIn -= OnSessionStarted;
            AuthenticationContext.SignInRequested -= OnSignInRequested;
            viewModel.Dispose();
        };

        // Signing out (from the Users page) must bring the gate straight back.
        AuthenticationContext.SignedOut += OnSignedOut;

        // ... and while a session lasts, the banner says who it is, and this
        // device remembers the sign-in so the next launch does not ask again
        // (owner instruction, round 32). One handler, whichever way the session
        // was established — a verified password or the remembered record.
        AuthenticationContext.SignedIn += OnSessionStarted;

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

        // Declared outside the guard so the remembered-sign-in step below can use
        // it; assigned inside it so nothing here can take the shell down.
        UserRepository? repository = null;

        try
        {
            repository = new UserRepository();
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
            AppLog.Error("shell.first-run-check", error,
                "the first-run check could not be answered, so the sign-in gate was opened");
            await BeginSignInAsync(
                problem: "The user directory could not be read, so the first-run check could not be "
                       + "answered: " + error.Message + " Signing in may fail until that is fixed.");
            return;
        }

        // The directory is readable and not new. Before asking for a password,
        // ask this device whether it already knows who is operating: the owner's
        // instruction is that the password is typed once, not once per launch
        // (round 32). Every reason a remembered session stops counting is
        // decided in step3.b.9, not here; this only reports the outcome.
        RememberedSignInResult remembered;
        try
        {
            remembered = await RememberedSignIn.TryRestoreAsync(repository!);
        }
        catch (Exception error)
        {
            remembered = RememberedSignInResult.None(
                "The remembered sign-in could not be checked: " + error.Message);
            AppLog.Error("shell.remembered-signin", error,
                "the remembered sign-in could not be checked");
        }

        if (remembered.Restored)
        {
            // OnSessionStarted has already refreshed the banner and rewritten the
            // record; the shell opens on its normal page with a live session.
            // Nothing is logged here: a resumed session is the normal case, and
            // this log records failures only.
            return;
        }

        // Nobody is signed in: the gate is the way in, and it carries the reason
        // the remembered session did not apply, when there was one.
        await BeginSignInAsync(remembered.Username,
            problem: remembered.Message.Length > 0 ? remembered.Message : null);
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
                AppLog.Error("shell.sign-in-gate.create", error,
                    "the sign-in card could not be built");
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
            AppLog.Error("shell.sign-in-gate.prepare", error,
                "the sign-in card could not be prepared");
        }

        SignInGate.FocusFirstField();
    }

    /// <summary>
    /// A session now exists — from a verified password or from this device's
    /// remembered record. Two things follow, and both belong here so there is one
    /// place that knows them: the banner says who is operating, and the sign-in
    /// is written down for the next launch. There is no password in that record
    /// (step3.b.8); it is cleared the moment the session ends.
    /// </summary>
    private void OnSessionStarted(object? sender, EventArgs e)
    {
        if (!Dispatcher.CheckAccess())
        {
            Dispatcher.Invoke(() => OnSessionStarted(sender, e));
            return;
        }

        sessionBanner.ClearProblem();
        sessionBanner.Refresh();
        DeviceSessionStore.Remember(
            AuthenticationContext.Current,
            ProGlassAutomation.Services.Audit.CurrentOperator.Username);
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
        // Signing out is the one explicit "do not remember me" (owner instruction,
        // round 32), so the record goes first and goes on whatever thread raised
        // the event — before any marshalling that could be lost with the window.
        DeviceSessionStore.Forget();

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
