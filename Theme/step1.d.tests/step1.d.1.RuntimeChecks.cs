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
            InquiryInboxDeliveryOrderingChecks.Run();
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
            // The startup page is the inquiry register: step1.b.17 initialises
            // selectedWorkspace = 2, and the rule recorded in docs/05 and enforced by
            // step1.d.3 is "the shell must not boot into Theme Studio (workspace 0)",
            // not "boot into page 1". This check used to pin the literal page 1, which
            // no source sets, so it could only ever fail. Assert the rule and the page
            // the model actually names — never a second literal.
            Check(model.SelectedWorkspace != 0 && model.SelectedWorkspace <= 10,
                "business reference is the startup page, not Theme Studio (observed workspace " + model.SelectedWorkspace + ")");
            Check(model.IsInquiry, "startup page is the inquiry register");
            Check(model.WorkspaceTitle == "Inquiries" && model.WorkspaceSubtitle == "Business · Inquiry register",
                "startup chrome names the business reference");
            // AppData may restore Liquid/Clay; Bento-specific smoke (Border popup, AccentPrimary) must not depend on that.
            model.SelectedTheme = ThemeService.Bento;
            model.IsLight = true;
            // The chrome-title checks below are about page 1, so the page is set
            // explicitly instead of relying on the startup page happening to be it.
            model.SelectedWorkspace = 1;
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

            // Round 36: the owner reports the Light/Dark switch is slow, so the run
            // reports numbers instead of a recollection — the engine's package rebuild on
            // its own, then three complete switches of the bound button. The first switch
            // after startup also pays for one-time work, so the repeats are what the owner
            // actually feels. Printed, not asserted: a slow machine must not fail a
            // fidelity run. Render mode is SoftwareOnly here, which is the pessimistic case.
            var engineClock = Stopwatch.StartNew();
            themes.Apply(ThemeService.Bento, Appearance.Light);
            engineClock.Stop();
            var switchTimes = new List<long>();
            for (int pass = 0; pass < 3; pass++)
            {
                var clock = Stopwatch.StartNew();
                if (themes.ActiveAppearance == Appearance.Light) model.IsDark = true; else model.IsLight = true;
                Pump();
                clock.Stop();
                switchTimes.Add(clock.ElapsedMilliseconds);
            }
            if (themes.ActiveAppearance != Appearance.Dark) model.IsDark = true;
            Pump();
            Check(themes.ActiveAppearance == Appearance.Dark && model.IsDark, "measured appearance switch lands in dark");
            Console.WriteLine($"MEASURED: package rebuild {engineClock.ElapsedMilliseconds} ms; bound switches {string.Join(", ", switchTimes)} ms for {switchTimes.Count} complete Light<->Dark toggles, median {switchTimes.OrderBy(t => t).ElementAt(switchTimes.Count / 2)} ms (studio realized, software render).");

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
                    // The grid's own rule, not a remembered one: all columns at or above
                    // its CompactWidth, two between 720 and it, one below. The old assertion
                    // compared EffectiveColumns with 4 and the width with 1100 — the Studio
                    // grid declares three columns, so "< 4" was true at every width and
                    // CompactWidth is a themed resource that need not be 1100 either.
                    Check(modules.EffectiveColumns == ExpectedColumns(modules),
                        $"Studio columns follow the grid's compact rule at {width}: effective {modules.EffectiveColumns}, expected {ExpectedColumns(modules)} (declared {modules.Columns}, compact at {modules.CompactWidth:0})");
                    Check(input.Text == model.SampleName && BindingOperations.IsDataBound(input, TextBox.TextProperty), "input binding survives Studio reflow");
                    // The page is two columns: a scrolling BentoGrid of modules and the fixed
                    // Settings sidebar beside it. Cards are therefore measured against the
                    // page, not against the grid — the sidebar is the grid's sibling, and
                    // asking the grid to transform it threw "The specified Visual is not an
                    // ancestor of this Visual" the first time this ran. The names are read
                    // and asserted, not assumed: this list used to name "Signals" and
                    // "DiagnosticsToggle", which no page in this repository declares.
                    var bounds = new List<(string Name, Rect Box)>();
                    foreach (string moduleName in new[] { "Preview", "Gallery", "StudioMix", "Controls", "Tokens", "Settings" })
                    {
                        object? found = studio.FindName(moduleName);
                        Check(found is FrameworkElement, $"Studio page declares {moduleName}");
                        var card = (FrameworkElement)found;
                        Check(IsInside(card, studio), $"{moduleName} is part of the Studio page");
                        Check(card.ActualWidth > 0 && card.ActualHeight > 0, $"{moduleName} remains laid out");
                        Rect box = card.TransformToAncestor(studio).TransformBounds(new Rect(card.RenderSize));
                        Check(box.Left >= -1 && box.Right <= studio.ActualWidth + 1, $"{moduleName} fits the page width");
                        foreach (var previous in bounds)
                        {
                            Rect overlap = Rect.Intersect(previous.Box, box);
                            Check(overlap.IsEmpty || overlap.Width <= 1 || overlap.Height <= 1,
                                $"{moduleName} does not overlap {previous.Name}");
                        }
                        bounds.Add((moduleName, box));
                    }
                }
            }
            // This workstation's saved preferences are restored on construction, and a
            // pinned page size rewrites the very tokens asserted below (PageSizeService
            // sets ControlMinHeight and FontSizeBody). Clearing it means the numbers are
            // the theme's own, not whatever page size was last saved here.
            model.ResetPages.Execute(null);
            Pump();
            // The Studio restores this workstation's saved design, including a layout
            // preset that rewrites the card radius asserted below. Reset first, so the
            // numbers are the engine's and the owner's saved file cannot decide the run.
            model.ResetDesign.Execute(null);
            Pump();
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
                Check(grid.EffectiveColumns == ExpectedColumns(grid),
                    $"business columns follow the grid's compact rule at {width}: effective {grid.EffectiveColumns}, expected {ExpectedColumns(grid)} (declared {grid.Columns}, compact at {grid.CompactWidth:0})");
                Check(Equals(businessInput.Background, themes.GetToken("SurfacePrimary")), "business uses active resources");
                model.IsLight = true;
            }
            model.SelectedWorkspace = 0;
            model.ResetDesign.Execute(null);
            Pump();
            Check(model.SelectedAccent == ProGlassAutomation.ThemeBento.BentoAccent.Ocean &&
                (double)themes.GetToken("ControlMinHeight") == 32d, "reset design restores defaults without resetting sample input");
            // Every installed theme is accounted for by name, and that list is checked
            // against the catalogue BEFORE anything is switched. The chain this replaces
            // covered the six themes that existed when it was written and then threw
            // "Unhandled extra theme: Swiss International" — the check was behind the
            // product, not the other way round. A tenth theme now fails immediately, by
            // name, before a single package is built.
            var summaryPrefix = new Dictionary<string, string>
            {
                [ThemeService.Bento] = "Ocean",
                [ThemeService.Clay] = "Rose",
                [ThemeService.Fluent] = "Fluent",
                [ThemeService.Neumorphism] = "Neumorphism",
                [ThemeService.LiquidGlass] = "Liquid Glass",
                [ThemeService.Glassmorphism] = "Glassmorphism",
                [ThemeService.Spatial] = "Spatial",
                [ThemeService.Swiss] = "Swiss",
                [ThemeService.Holographic] = "Holographic",
            };
            Check(summaryPrefix.Keys.OrderBy(name => name).SequenceEqual(ThemeService.InstalledThemes.OrderBy(name => name)),
                "this check accounts for every installed theme; unaccounted: "
                + string.Join(", ", ThemeService.InstalledThemes.Where(name => !summaryPrefix.ContainsKey(name))));
            foreach (string extra in ThemeService.InstalledThemes)
            {
                if (extra == ThemeService.Bento) continue;
                var previous = window.Background;
                var expectedRounding = ThemeCatalog.Find(extra).Geometry == ThemeGeometry.SharedTokens;
                model.SelectedTheme = extra;
                Pump();
                Check(themes.ActiveTheme == extra, extra + " activates");
                // Saved preferences are restored on construction, so a theme's "defaults"
                // are only defaults after a reset — otherwise the assertions below describe
                // whatever was last saved on this workstation.
                model.ResetDesign.Execute(null);
                Pump();
                Check(!ReferenceEquals(previous, window.Background), extra + " replaces window resources");
                Check(Equals(window.Background, WindowFill(themes)), extra + " window background");
                Check(!model.CanCustomizeBento, extra + " keeps the Bento customization engine off");
                Check(model.IsFluent == (extra == ThemeService.Fluent), extra + " fluent studio flag");
                Check(model.IsNeumorph == (extra == ThemeService.Neumorphism), extra + " neumorph studio flag");
                Check(model.IsLiquid == (extra == ThemeService.LiquidGlass), extra + " liquid studio flag");
                Check(model.IsGlass == (extra == ThemeService.Glassmorphism), extra + " glass studio flag");
                Check(model.IsSpatial == (extra == ThemeService.Spatial), extra + " spatial studio flag");
                Check(model.IsSwiss == (extra == ThemeService.Swiss), extra + " swiss studio flag");
                Check(model.IsHolographic == (extra == ThemeService.Holographic), extra + " holographic studio flag");
                // Shared card rounding is a property of the theme's declared geometry, not
                // of a list kept here: Clay owns its geometry, the other eight share it.
                Check(model.CanCustomizeGeometry == expectedRounding, extra + " shared card rounding matches its geometry");
                Check(model.DesignSummary.StartsWith(summaryPrefix[extra]),
                    extra + " names itself in the design summary: " + model.DesignSummary);
                Check(model.CanCustomize == (extra is ThemeService.Clay or ThemeService.Neumorphism or ThemeService.LiquidGlass),
                    extra + " material engine flag");
                if (extra == ThemeService.Clay)
                {
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
                else if (extra == ThemeService.Neumorphism)
                {
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
                    Check(model.ResetTheme.CanExecute(null) && model.PreviewDefault.CanExecute(null), "liquid studio reset commands");
                    model.SelectedLiquidFrost = ProGlassAutomation.ThemeLiquidGlass.LiquidFrost.Clear;
                    Pump();
                    Check((double)themes.GetToken("LiquidFrostOpacity") == 0.22d, "live liquid frost");
                    model.ResetDesign.Execute(null);
                    Pump();
                    Check(model.SelectedLiquidFrost == ProGlassAutomation.ThemeLiquidGlass.LiquidFrost.Standard, "liquid reset restores defaults");
                }
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

    /// <summary>
    /// What BentoGrid.MeasureOverride must produce for the width the grid was given:
    /// every column at or above its CompactWidth, two between 720 and it, one below.
    /// Read from the grid itself — Columns and CompactWidth are both themed values.
    /// </summary>
    private static int ExpectedColumns(BentoGrid grid)
    {
        int declared = Math.Max(1, grid.Columns);
        if (grid.ActualWidth >= grid.CompactWidth) return declared;
        return grid.ActualWidth >= 720 ? Math.Min(2, declared) : 1;
    }

    /// <summary>
    /// True when <paramref name="node"/> is the ancestor's descendant in the visual tree.
    /// TransformToAncestor throws an opaque exception when it is not, so the relationship
    /// is proved before anything is measured through it.
    /// </summary>
    private static bool IsInside(DependencyObject node, DependencyObject ancestor)
    {
        for (DependencyObject? cursor = node; cursor is not null; cursor = VisualTreeHelper.GetParent(cursor))
            if (ReferenceEquals(cursor, ancestor)) return true;
        return false;
    }

    /// <summary>
    /// The token each theme's own ThemedWindow style paints the window with. Four of the
    /// nine declare a material canvas of their own; the rest use ApplicationBackground.
    /// Holographic was missing here and cost a run — step1.d.6 now parses every theme's
    /// Foundation.xaml and fails if this table and those styles disagree.
    /// </summary>
    private static string WindowFillToken(string theme) => theme switch
    {
        ThemeService.LiquidGlass => "LiquidCanvasBrush",
        ThemeService.Glassmorphism => "GlassCanvasBrush",
        ThemeService.Spatial => "SpatialCanvasBrush",
        ThemeService.Holographic => "HoloCanvasBrush",
        _ => "ApplicationBackground"
    };

    private static object WindowFill(ThemeService themes) =>
        themes.GetToken(WindowFillToken(themes.ActiveTheme));

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
