// ═══════════════════════════════════════════════════════════════════════
// FILE: step1.ThemeStudio/step1.b.ThemeStudioScreen/step1.b.33.ShellNavEntry.xaml.cs
// MODULE: step1 — shell navigation entry.
//
// One rail button, as a control, because MainWindow.xaml is frozen at its
// size budget (step1.d.19): a nav button is a ~45-line block once its
// per-theme selected state is spelled out, and the shell has one line of
// headroom. The shell therefore hosts this control in one line and the button
// markup lives here.
//
// The look is NOT invented here. The style body in the XAML is a copy of
// `ShellNavButton` from MainWindow.xaml plus the four selected-state triggers
// its siblings (Clients, Users, Company) use, with the flag they test
// (IsClients / IsUsers / IsCompany) replaced by this control's IsSelected.
// step5.f.1 asserts the copy still matches the original setters, so the two
// cannot drift apart silently.
//
// Why step5 needed it: the shell's visible navigation is this rail. The tab
// headers of WorkspaceTabs are collapsed by a style on purpose, so a TabItem
// alone is not a route — it is only a page. Projects had a TabItem and no rail
// entry, and SelectedWorkspace silently refused index 11.
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProGlassAutomation.Customization;

namespace ProGlassAutomation.ThemeStudio;

/// <summary>
/// A single shell navigation button: one mark, one label, one command, and the
/// active state the rail's other entries show.
/// </summary>
public sealed partial class ShellNavEntry : UserControl
{
    /// <summary>The command the entry invokes; the button disables itself when it cannot execute.</summary>
    public static readonly DependencyProperty CommandProperty =
        DependencyProperty.Register(
            nameof(Command), typeof(ICommand), typeof(ShellNavEntry),
            new PropertyMetadata(null));

    /// <summary>The word the operator reads: "Projects", "Clients", …</summary>
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(
            nameof(Label), typeof(string), typeof(ShellNavEntry),
            new PropertyMetadata(string.Empty));

    /// <summary>The rail mark drawn to the left of the label.</summary>
    public static readonly DependencyProperty MarkProperty =
        DependencyProperty.Register(
            nameof(Mark), typeof(IconKind), typeof(ShellNavEntry),
            new PropertyMetadata(IconKind.Home));

    /// <summary>True while the page this entry opens is the selected workspace.</summary>
    public static readonly DependencyProperty IsSelectedProperty =
        DependencyProperty.Register(
            nameof(IsSelected), typeof(bool), typeof(ShellNavEntry),
            new PropertyMetadata(false));

    public ShellNavEntry()
    {
        InitializeComponent();
    }

    public ICommand? Command
    {
        get => (ICommand?)GetValue(CommandProperty);
        set => SetValue(CommandProperty, value);
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    public IconKind Mark
    {
        get => (IconKind)GetValue(MarkProperty);
        set => SetValue(MarkProperty, value);
    }

    public bool IsSelected
    {
        get => (bool)GetValue(IsSelectedProperty);
        set => SetValue(IsSelectedProperty, value);
    }
}
