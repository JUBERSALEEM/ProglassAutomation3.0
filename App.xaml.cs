using System.Windows;
using ProGlassAutomation.Customization;
using ProGlassAutomation.ThemeClaymorphism;
using ProGlassAutomation.ThemeNeumorphism;
using ProGlassAutomation.Theming;

namespace ProGlassAutomation;

public partial class App : Application
{
    public ThemeService Themes { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
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
            catch { /* safe fallback already handled in store */ }
            MainWindow = new MainWindow(Themes);
            MainWindow.Show();
        }
        catch (Exception error)
        {
            MessageBox.Show(error.ToString(), "ProGlass Automation could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }
}
