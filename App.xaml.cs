using System.Windows;
using ProGlassAutomation.Customization;
using ProGlassAutomation.Diagnostics;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.ThemeLiquidGlass;
using ProGlassAutomation.ThemeNeumorphism;
using ProGlassAutomation.Theming;

namespace ProGlassAutomation;

public partial class App : Application
{
    /// <summary>
    /// The earliest code this application owns. The generated entry point creates
    /// this object, so its field initialisers run BEFORE <see cref="OnStartup"/>:
    /// without a trap here, a fault while the theme service is being constructed
    /// would end the process with no record at all. The dispatcher trap cannot
    /// attach yet (there is no Application object), so <see cref="OnStartup"/>
    /// attaches again — both calls are idempotent by design.
    /// </summary>
    static App() => AppLog.AttachProcessHandlers();

    public ThemeService Themes { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        // Second call: the static constructor already took the AppDomain and task
        // traps, and this one adds the dispatcher trap now that the Application
        // object exists. Idempotent; failure-only, so nothing is written unless a
        // handler is actually entered.
        AppLog.AttachProcessHandlers();

        try
        {
            Themes.Initialize(Resources);
            // Restore persisted customization before any window shows — validate, never crash on corrupt file.
            try
            {
                var saved = CustomizationStore.Load();
                var theme = ThemeService.InstalledThemes.Contains(saved.Theme) ? saved.Theme : ThemeService.Bento;
                Themes.Apply(theme, saved.Appearance, SharedGeometry.Compose(
                    theme,
                    saved.Preferences ?? new BentoCustomizationState(),
                    saved.Clay ?? new ClayCustomizationState(),
                    saved.Geometry ?? new SharedGeometryState(),
                    saved.Neumorph ?? new NeumorphCustomizationState(),
                    saved.Font ?? new FontCustomizationState(),
                    saved.Colors ?? new ColorPlateState(),
                    saved.Liquid ?? new LiquidCustomizationState(),
                    saved.Pages));
                IconService.Apply(saved.Icons ?? new IconCustomizationState());
            }
            catch (Exception error)
            {
                // The store falls back to the built-in theme; the application is
                // usable, so this is ERROR, not FATAL. Logged because a saved
                // customisation that silently stops applying is otherwise
                // undiagnosable: the owner just sees the old look.
                AppLog.Error("app.startup.customization-restore", error,
                    "default theme shown instead of the saved customisation");
            }

            MainWindow = new MainWindow(Themes);
            MainWindow.Show();
        }
        catch (Exception error)
        {
            // Startup cannot continue on this run. Record first, then show the same
            // message the owner already had, then shut down: the record is what
            // survives because the message box does not.
            AppLog.Fatal("app.startup", error,
                "the shell could not be created; the application is shutting down");

            MessageBox.Show(error.ToString(), "ProGlass Automation could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
