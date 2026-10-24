using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Threading;
using ProGlassAutomation;
using ProGlassAutomation.Theming;
using ProGlassAutomation.ThemeBento;
using ProGlassAutomation.ThemeStudio;

namespace ProGlassAutomation.ThemeTests;

/// <summary>Dependency-free STA runtime checks. Run on Windows; nonzero exit means failure.</summary>
internal static class RuntimeChecks
{
    [STAThread]
    public static int Main(string[] args)
    {
        RenderOptions.ProcessRenderMode = RenderMode.SoftwareOnly;
        var failures = new BindingFailures();
        PresentationTraceSources.DataBindingSource.Listeners.Add(failures);
        PresentationTraceSources.DataBindingSource.Switch.Level = SourceLevels.Warning;
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Window? window = null;
        Window? dialog = null;
        try
        {
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--contracts-only"))
                throw new ArgumentException("Usage: [--contracts-only]");
            var themes = new ThemeService();
            app.Resources["UnrelatedResource"] = "preserve me";
            themes.Initialize(app.Resources);
            ContractChecks.Run();
            InquiryInboxJsonbChecks.Run();
            InquiryInboxHttpClientChecks.Run();
            InquiryInboxAuthorizationChecks.Run();
            InquiryInboxStorageChecks.Run();
            if (args.Contains("--contracts-only"))
            {
                Check(failures.Messages.Count == 0, "contract binding diagnostics");
                Console.WriteLine("PASS: compiled token/style/target contracts, package isolation and negative resource cases.");
                return 0;
            }
            ContractChecks.ExpectFailure(() => themes.Initialize(app.Resources), "double initialization");
            window = new MainWindow(themes);
            app.MainWindow = window;
            window.Show();
            Pump();
            Check(window.IsVisible, "startup main window is visible");
            var model = (ThemeStudioViewModel)window.DataContext;
            Check(model.SelectedWorkspace == 1, "business reference is the startup page");
            // AppData may restore Liquid/Clay; Bento-specific smoke (Border popup, AccentPrimary) must not depend on that.
            model.SelectedTheme = ThemeService.Bento;
            model.IsLight = true;
            Pump();
            Check(themes.ActiveTheme == ThemeService.Bento && model.IsLight, "STA smoke pins Bento Light");
            Check(model.IsMenuOpen, "shell menu starts open");
            Check(model.WorkspaceTitle == "Home" && model.WorkspaceSubtitle == "Home", "home chrome titles");
            model.ToggleMenu.Execute(null);
            Check(!model.IsMenuOpen, "toggle hides the shell chrome");
            model.ToggleMenu.Execute(null);
            Check(model.IsMenuOpen, "toggle restores the shell chrome");
            model.SelectedWorkspace = 0;
            Pump();
            var progress = Descendants(window).OfType<ProgressBar>().Single();
            Check(BindingOperations.GetBinding(progress, RangeBase.ValueProperty)?.Mode == BindingMode.OneWay,
                "display-only progress binding is explicitly one-way");
            var headerThemePicker = Descendants(window).OfType<ComboBox>().Single(c => c.Name == "ThemePicker");
            Check(headerThemePicker.IsVisible, "live theme selector is visible in startup header");
            dialog = new PreviewWindow { Owner = window, DataContext = model };
            dialog.Show();
            Pump();

            var button = Descendants(window).OfType<Button>().First(b => Equals(b.Content, "_Apply preview"));
            var input = Descendants(window).OfType<TextBox>().First(b => b.Name == "PreviewName");
            var combo = Descendants(window).OfType<ComboBox>().Single(c => c.Name == "ThemePicker");
            combo.IsDropDownOpen = true;
            Pump();
            var popup = (Popup)combo.Template.FindName("PART_Popup", combo);
            Check(popup.IsOpen, "real selection popup opens");
            var popupChrome = popup.Child as FrameworkElement
                ?? throw new InvalidOperationException("selection popup has no visual child");
            Check(popupChrome is Border, "Bento selection popup chrome is a Border");
            var popupBorder = (Border)popupChrome;
            input.SetCurrentValue(TextBox.TextProperty, "Unsaved editing state");
            input.GetBindingExpression(TextBox.TextProperty)!.UpdateSource();
            input.CaretIndex = 7;
            var existingButtonTemplate = button.Template;
            var existingWindowBrush = window.Background;
            model.IsDark = true;
            Pump();
            Check(themes.ActiveAppearance == Appearance.Dark && model.IsDark && !model.IsLight, "bound appearance state");
            Check(!ReferenceEquals(existingWindowBrush, window.Background), "existing window resources replaced");
            Check(Equals(window.Background, themes.GetToken("ApplicationBackground")), "main window background");
            Check(Equals(dialog.Background, themes.GetToken("ApplicationBackground")), "already-open dialog background");
            Check(Equals(button.Background, themes.GetToken("AccentPrimary")), "existing primary button");
            Check(Equals(input.Background, themes.GetToken("SurfacePrimary")), "existing input");
            Check(Equals(popupBorder.Background, themes.GetToken("SurfaceElevated")), "already-open popup resources");
            Check(combo.IsDropDownOpen && popup.IsOpen, "open selection survives appearance replacement");
            Check(ReferenceEquals(existingButtonTemplate, button.Template), "Bento template identity stays stable across modes");
            Check(input.Text == "Unsaved editing state" && input.CaretIndex == 7, "text and caret survive switching");
            Check(BindingOperations.IsDataBound(input, TextBox.TextProperty), "input binding survives editing and switching");
            combo.IsDropDownOpen = false;
            Check(Descendants(window).OfType<RadioButton>().Any(r => r.IsChecked == true && r.Content.ToString()!.Contains("Dark")), "appearance selector reflects actual mode");
            Check(model.Tokens.Count == ThemeContract.Required.Count, "live token completeness");
            Check(model.Tokens.Single(t => t.Name == "SurfacePrimary").Preview == themes.GetToken("SurfacePrimary"), "live token reference");
            Check(Equals(app.Resources["UnrelatedResource"], "preserve me"), "unrelated resources preserved");

            model.SampleName = "x";
            Pump();
            Check(Validation.GetHasError(input), "actual binding validation state");
            Check(!model.QuickApplyPreview.CanExecute(null), "invalid preview command disabled");
            model.SampleName = "Runtime review";
            model.QuickApplyPreview.Execute(null);
            Pump();
            Check(!Validation.GetHasError(input) && model.Feedback.Contains("Runtime review"), "valid preview action");
            model.AdvanceProgress.Execute(null);
            Pump();
            Check(model.Progress == 75 && progress.Value == 75, "progress command updates the displayed value");
            model.ResetPreview.Execute(null);
            Check(model.Progress == 50 && model.SampleName == "Bento review", "reset action");
            model.TokenFilter = "SurfacePrimary";
            Check(model.TokenView.Cast<TokenRow>().Count() == 1, "live token search");
            model.TokenFilter = "";
            model.ReferenceFilter = "no matching pattern";
            Check(model.ReferenceView.IsEmpty && model.ReferenceSummary.StartsWith("No work"), "search empty state");
            model.ReferenceFilter = "";
            Check(model.ReferenceView.Cast<ReferenceSample>().Count() == 4, "reference search reset");
            model.SelectedReference = model.ReferenceItems[1];
            model.ReviewReference.Execute(null);
            Check(model.Feedback.Contains("Lobby partitions"), "selected sample review action");
            model.ClearPreview.Execute(null);
            Check(model.SampleName == "" && !model.QuickApplyPreview.CanExecute(null), "clear preview is local and validates");
            model.ResetPreview.Execute(null);

            int packageCount = app.Resources.MergedDictionaries.Count;
            window.WindowState = WindowState.Normal;
            foreach (double width in new[] { 720d, 1024d, 1440d, 1920d, 2560d })
            {
                window.Width = width;
                window.Height = 900;
                foreach (var mode in new[] { Appearance.Light, Appearance.Dark, Appearance.Light })
                {
                    themes.Apply(ThemeService.Bento, mode);
                    Pump();
                    Check(Equals(window.Background, themes.GetToken("ApplicationBackground")), $"switch at {width}");
                    Check(app.Resources.MergedDictionaries.Count == packageCount, "switching does not accumulate resource packages");
                    Check(Equals(dialog.Background, window.Background), "dialog stays synchronized");
                    var studio = Descendants(window).OfType<ThemeStudioView>().Single();
                    var modules = (BentoGrid)studio.FindName("Modules");
                    Check((modules.EffectiveColumns < 4) == (modules.ActualWidth < 1100), "Studio responsive column mode");
                    FrameworkElement[] cards = [
                        (FrameworkElement)studio.FindName("Settings"), (FrameworkElement)studio.FindName("Controls"),
                        (FrameworkElement)studio.FindName("Signals"), (FrameworkElement)studio.FindName("DiagnosticsToggle"), (FrameworkElement)studio.FindName("Gallery")];
                    Check(input.Text == model.SampleName && BindingOperations.IsDataBound(input, TextBox.TextProperty), "input binding survives Studio reflow");
                    var bounds = new List<Rect>();
                    foreach (FrameworkElement child in cards)
                    {
                        Check(child.ActualWidth > 0 && child.ActualHeight > 0, "module remains laid out");
                        Rect box = child.TransformToAncestor(modules).TransformBounds(new Rect(child.RenderSize));
                        Check(box.Left >= -1 && box.Right <= modules.ActualWidth + 1, "module fits horizontal viewport");
                        foreach (Rect previous in bounds)
                        {
                            Rect overlap = Rect.Intersect(previous, box);
                            Check(overlap.IsEmpty || overlap.Width <= 1 || overlap.Height <= 1, "modules do not overlap");
                        }
                        bounds.Add(box);
                    }
                }
            }
            model.SelectedAccent = ProGlassAutomation.ThemeBento.BentoAccent.Teal;
            model.SelectedDensity = ProGlassAutomation.ThemeBento.BentoDensity.Comfortable;
            model.SelectedShape = ProGlassAutomation.ThemeBento.BentoShape.Rounded;
            model.SelectedTypography = ProGlassAutomation.ThemeBento.BentoType.Large;
            Pump();
            Check((double)themes.GetToken("ControlMinHeight") == 38d, "live density changes actual controls");
            Check(((CornerRadius)themes.GetToken("CardCornerRadius")).TopLeft == 18d, "live corner setting");
            Check((double)themes.GetToken("FontSizeBody") == 14d, "live typography setting");
            model.ShowBusiness.Execute(null);
            Pump();
            var business = Descendants(window).OfType<BusinessReferenceView>().Single();
            Check(!Descendants(business).OfType<CustomizationPanel>().Any(), "business reference contains no customization controls");
            Check(Descendants(business).OfType<WorklistPanel>().Any(), "business reference is a real interactive page");
            AnalyticsChecks.Runtime(themes, business, Pump);
            var businessInput = Descendants(business).OfType<TextBox>().Single(t => t.Name == "PreviewName");
            Check(businessInput.Text == model.SampleName, "both pages share actual preview state");
            foreach (double width in new[] { 720d, 1024d, 1440d, 1920d })
            {
                window.Width = width;
                model.IsDark = true;
                Pump();
                var grid = (BentoGrid)business.FindName("BusinessModules");
                Check((grid.EffectiveColumns < 4) == (grid.ActualWidth < 1100), "business responsive layout");
                Check(Equals(businessInput.Background, themes.GetToken("SurfacePrimary")), "business uses active resources");
                model.IsLight = true;
            }
            model.SelectedWorkspace = 0;
            model.ResetDesign.Execute(null);
            Pump();
            Check(model.SelectedAccent == ProGlassAutomation.ThemeBento.BentoAccent.Ocean &&
                (double)themes.GetToken("ControlMinHeight") == 32d, "reset design restores defaults without resetting sample input");
            Check(ThemeService.InstalledThemes.Contains(ThemeService.Clay) && ThemeService.InstalledThemes.Contains(ThemeService.Fluent) && ThemeService.InstalledThemes.Contains(ThemeService.Neumorphism) && ThemeService.InstalledThemes.Contains(ThemeService.LiquidGlass) && ThemeService.InstalledThemes.Contains(ThemeService.Glassmorphism) && ThemeService.InstalledThemes.Contains(ThemeService.Spatial) && ThemeService.InstalledThemes.Contains(ThemeService.Swiss) && ThemeService.InstalledThemes.Contains(ThemeService.Holographic), "Clay through Holographic are registered themes");
            foreach (string extra in ThemeService.InstalledThemes)
            {
                if (extra == ThemeService.Bento) continue;
                var previous = window.Background;
                model.SelectedTheme = extra;
                Pump();
                Check(themes.ActiveTheme == extra, extra + " activates");
                Check(!ReferenceEquals(previous, window.Background), extra + " replaces window resources");
                Check(Equals(window.Background, WindowFill(themes)), extra + " window background");
                Check(!model.CanCustomizeBento, extra + " keeps the Bento customization engine off");
                Check(model.IsFluent == (extra == ThemeService.Fluent), extra + " fluent studio flag");
                Check(model.IsNeumorph == (extra == ThemeService.Neumorphism), extra + " neumorph studio flag");
                Check(model.IsLiquid == (extra == ThemeService.LiquidGlass), extra + " liquid studio flag");
                Check(model.IsGlass == (extra == ThemeService.Glassmorphism), extra + " glass studio flag");
                Check(model.IsSpatial == (extra == ThemeService.Spatial), extra + " spatial studio flag");
                if (extra == ThemeService.Clay)
                {
                    Check(model.CanCustomizeClay && model.DesignSummary.Contains("Rose"), "clay customization is live");
                    Check(model.ResetTheme.CanExecute(null) && model.PreviewDefault.CanExecute(null) && model.ResetSection.CanExecute(null), "clay studio reset commands");
                    model.SelectedClayTypography = ProGlassAutomation.ThemeClaymorphism.ClayType.Large;
                    Pump();
                    Check((double)themes.GetToken("FontSizeBody") == 14d, "live clay typography");
                    model.SelectedClayPlumpness = ProGlassAutomation.ThemeClaymorphism.ClayPlumpness.Puffy;
                    Pump();
                    Check(((CornerRadius)themes.GetToken("CardCornerRadius")).TopLeft == 40d, "live clay plumpness");
                    model.ResetDesign.Execute(null);
                    Pump();
                    Check(model.SelectedClayAccent == ProGlassAutomation.ThemeClaymorphism.ClayAccent.Rose && (double)themes.GetToken("FontSizeBody") == 13d, "clay reset restores defaults");
                }
                else if (extra == ThemeService.Fluent)
                    Check(model.CanCustomizeGeometry && model.DesignSummary.StartsWith("Fluent"), extra + " uses shared card rounding without a Fluent material engine");
                else if (extra == ThemeService.Neumorphism)
                {
                    Check(model.CanCustomizeGeometry && model.CanCustomizeNeumorph && model.DesignSummary.StartsWith("Neumorphism"), extra + " uses shared rounding and a neumorph depth engine");
                    Check(model.ResetTheme.CanExecute(null) && model.PreviewDefault.CanExecute(null), "neumorph studio reset commands");
                    model.SelectedNeumorphDepth = ProGlassAutomation.ThemeNeumorphism.NeumorphDepth.Deep;
                    Pump();
                    Check((double)themes.GetToken("NeumorphDistance") == 10d, "live neumorph depth");
                    model.ResetDesign.Execute(null);
                    Pump();
                    Check(model.SelectedNeumorphDepth == ProGlassAutomation.ThemeNeumorphism.NeumorphDepth.Standard && (double)themes.GetToken("NeumorphDistance") == 6d, "neumorph reset restores defaults");
                }
                else if (extra == ThemeService.LiquidGlass)
                {
                    Check(model.CanCustomizeGeometry && model.CanCustomizeLiquid && model.DesignSummary.StartsWith("Liquid Glass"), extra + " uses shared rounding and a liquid frost engine");
                    Check(model.ResetTheme.CanExecute(null) && model.PreviewDefault.CanExecute(null), "liquid studio reset commands");
                    model.SelectedLiquidFrost = ProGlassAutomation.ThemeLiquidGlass.LiquidFrost.Clear;
                    Pump();
                    Check((double)themes.GetToken("LiquidFrostOpacity") == 0.22d, "live liquid frost");
                    model.ResetDesign.Execute(null);
                    Pump();
                    Check(model.SelectedLiquidFrost == ProGlassAutomation.ThemeLiquidGlass.LiquidFrost.Standard, "liquid reset restores defaults");
                }
                else if (extra == ThemeService.Glassmorphism)
                    Check(model.CanCustomizeGeometry && !model.CanCustomize && model.DesignSummary.StartsWith("Glassmorphism"), extra + " uses shared card rounding without a glass extras engine");
                else if (extra == ThemeService.Spatial)
                    Check(model.CanCustomizeGeometry && !model.CanCustomize && model.DesignSummary.StartsWith("Spatial"), extra + " uses shared card rounding without a spatial extras engine");
                else
                    throw new Exception("Unhandled extra theme: " + extra);
                model.IsDark = true;
                Pump();
                Check(themes.ActiveTheme == extra && themes.ActiveAppearance == Appearance.Dark, extra + " dark");
                Check(Equals(window.Background, WindowFill(themes)), extra + " dark window background");
                model.IsLight = true;
                Pump();
            }
            model.SelectedTheme = ThemeService.Bento;
            model.IsLight = true;
            Pump();
            Check(themes.ActiveTheme == ThemeService.Bento && themes.ActiveAppearance == Appearance.Light, "return to bento light");
            ContractChecks.ExpectFailure(() => themes.Apply("NotInstalled", Appearance.Dark), "uninstalled theme");
            ContractChecks.ExpectFailure(() => themes.Apply(ThemeService.Bento, (Appearance)100), "invalid appearance");
            Check(themes.ActiveAppearance == Appearance.Light, "invalid selection preserves active state");
            Pump();
            Check(failures.Messages.Count == 0, "binding diagnostics: " + string.Join("\n", failures.Messages));
            Console.WriteLine("PASS: contract, negative cases, commands, live tokens, existing controls/dialog, repeated switching, responsive layout and binding diagnostics.");
            Console.WriteLine("Manual review still required: visual fidelity, hover/pressed/keyboard navigation, popup/menu behavior, physical DPI, high contrast and performance.");
            return 0;
        }
        catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        finally { dialog?.Close(); window?.Close(); app.Shutdown(); }
    }

    private static void Check(bool passed, string name)
    {
        if (!passed) throw new InvalidOperationException("FAILED: " + name);
    }

    private static object WindowFill(ThemeService themes) =>
        themes.ActiveTheme == ThemeService.LiquidGlass ? themes.GetToken("LiquidCanvasBrush")
            : themes.ActiveTheme == ThemeService.Glassmorphism ? themes.GetToken("GlassCanvasBrush")
            : themes.ActiveTheme == ThemeService.Spatial ? themes.GetToken("SpatialCanvasBrush")
            : themes.GetToken("ApplicationBackground");

    private static void Pump() => Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);

    private static IEnumerable<DependencyObject> Descendants(DependencyObject root)
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            yield return child;
            foreach (var nested in Descendants(child)) yield return nested;
        }
    }

    private sealed class BindingFailures : TraceListener
    {
        public List<string> Messages { get; } = [];
        public override void Write(string? message) { if (!string.IsNullOrWhiteSpace(message)) Messages.Add(message); }
        public override void WriteLine(string? message) => Write(message);
    }
}
