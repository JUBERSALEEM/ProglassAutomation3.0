// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/ClickToCommandBehavior.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 2.5 — MVVM leakage sweep. Lets a non-button element (logo, dim
// overlay, row, …) invoke an ICommand on MouseLeftButtonDown without
// code-behind. Optional <c>ExcludeAncestorTypes</c> skips the command when
// the click originated on/inside one of the named element types (e.g.
// "TextBox" — an editable cell must receive the click itself).
// ═══════════════════════════════════════════════════════════════════════

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Attached behavior: <c>ClickToCommandBehavior.Command</c> (optionally
    /// with <c>CommandParameter</c>) executes the command when the element
    /// receives MouseLeftButtonDown. Replaces view-only click code-behind.
    /// </summary>
    public static class ClickToCommandBehavior
    {
        public static readonly DependencyProperty CommandProperty =
            DependencyProperty.RegisterAttached(
                "Command", typeof(ICommand), typeof(ClickToCommandBehavior),
                new PropertyMetadata(null, OnCommandChanged));

        public static readonly DependencyProperty CommandParameterProperty =
            DependencyProperty.RegisterAttached(
                "CommandParameter", typeof(object), typeof(ClickToCommandBehavior),
                new PropertyMetadata(null));

        /// <summary>
        /// Comma-separated element type names (simple names, e.g. "TextBox").
        /// When the click's original source is — or is inside — an element of
        /// one of these types, the command is NOT executed.
        /// </summary>
        public static readonly DependencyProperty ExcludeAncestorTypesProperty =
            DependencyProperty.RegisterAttached(
                "ExcludeAncestorTypes", typeof(string), typeof(ClickToCommandBehavior),
                new PropertyMetadata(null));

        public static void SetCommand(DependencyObject d, ICommand? value) => d.SetValue(CommandProperty, value);
        public static ICommand? GetCommand(DependencyObject d) => (ICommand?)d.GetValue(CommandProperty);

        public static void SetCommandParameter(DependencyObject d, object? value) => d.SetValue(CommandParameterProperty, value);
        public static object? GetCommandParameter(DependencyObject d) => d.GetValue(CommandParameterProperty);

        public static void SetExcludeAncestorTypes(DependencyObject d, string? value) => d.SetValue(ExcludeAncestorTypesProperty, value);
        public static string? GetExcludeAncestorTypes(DependencyObject d) => (string?)d.GetValue(ExcludeAncestorTypesProperty);

        private static void OnCommandChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not UIElement element) return;

            element.RemoveHandler(UIElement.MouseLeftButtonDownEvent,
                new MouseButtonEventHandler(OnMouseLeftButtonDown));

            if (e.NewValue is ICommand)
            {
                element.AddHandler(UIElement.MouseLeftButtonDownEvent,
                    new MouseButtonEventHandler(OnMouseLeftButtonDown), true);
            }
        }

        private static void OnMouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (sender is not UIElement element) return;

            var excludes = GetExcludeAncestorTypes(element);
            if (!string.IsNullOrWhiteSpace(excludes) &&
                IsInsideExcludedType(e.OriginalSource as DependencyObject, excludes))
                return;

            var command = GetCommand(element);
            var parameter = GetCommandParameter(element);
            if (command != null && command.CanExecute(parameter))
                command.Execute(parameter);
        }

        private static bool IsInsideExcludedType(DependencyObject? source, string excludes)
        {
            var names = excludes.Split(',');
            while (source != null)
            {
                var typeName = source.GetType().Name;
                foreach (var n in names)
                {
                    if (string.Equals(n.Trim(), typeName, System.StringComparison.Ordinal))
                        return true;
                }
                source = VisualTreeHelper.GetParent(source);
            }
            return false;
        }
    }
}
