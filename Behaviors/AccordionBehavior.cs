// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/AccordionBehavior.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 2.5 — MVVM leakage sweep. Replaces click-accordion code-behind
// (e.g. MainWindow SidebarGroup_Click / CloseOtherSidebarGroups) with a
// declarative attached behavior: ToggleButtons sharing the same Group
// string form an accordion — checking one unchecks the others.
// ═══════════════════════════════════════════════════════════════════════

using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Attached behavior: <c>AccordionBehavior.Group="Sidebar"</c> on a
    /// ToggleButton makes all ToggleButtons with the same group value
    /// mutually exclusive (only one checked at a time — an accordion).
    /// The group is scoped per window via the ToggleButton's Window.
    /// </summary>
    public static class AccordionBehavior
    {
        public static readonly DependencyProperty GroupProperty =
            DependencyProperty.RegisterAttached(
                "Group", typeof(string), typeof(AccordionBehavior),
                new PropertyMetadata(null, OnGroupChanged));

        public static void SetGroup(DependencyObject d, string? value) => d.SetValue(GroupProperty, value);
        public static string? GetGroup(DependencyObject d) => (string?)d.GetValue(GroupProperty);

        // window -> (group -> set of members) — per-window scope, cleaned on Unloaded.
        private static readonly Dictionary<Window, Dictionary<string, HashSet<ToggleButton>>> Registry = new();

        private static void OnGroupChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ToggleButton tb) return;

            Unregister(tb, e.OldValue as string);
            Register(tb, e.NewValue as string);
        }

        private static void Register(ToggleButton tb, string? group)
        {
            if (string.IsNullOrWhiteSpace(group)) return;

            var window = Window.GetWindow(tb);
            if (window == null) return;

            if (!Registry.TryGetValue(window, out var groups))
            {
                groups = new Dictionary<string, HashSet<ToggleButton>>();
                Registry[window] = groups;
                window.Closed += (_, _) => Registry.Remove(window);
            }

            if (!groups.TryGetValue(group, out var members))
            {
                members = new HashSet<ToggleButton>();
                groups[group] = members;
            }

            members.Add(tb);
            tb.Checked -= OnChecked;
            tb.Checked += OnChecked;
            tb.Unloaded -= OnUnloaded;
            tb.Unloaded += OnUnloaded;
        }

        private static void Unregister(ToggleButton tb, string? group)
        {
            if (string.IsNullOrWhiteSpace(group)) return;
            var window = Window.GetWindow(tb);
            if (window == null || !Registry.TryGetValue(window, out var groups)) return;
            if (groups.TryGetValue(group, out var members))
                members.Remove(tb);
        }

        private static void OnUnloaded(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb) return;
            tb.Unloaded -= OnUnloaded;
            tb.Checked -= OnChecked;
            Unregister(tb, GetGroup(tb));
        }

        private static void OnChecked(object sender, RoutedEventArgs e)
        {
            if (sender is not ToggleButton tb || tb.IsChecked != true) return;
            var window = Window.GetWindow(tb);
            if (window == null) return;

            var group = GetGroup(tb);
            if (string.IsNullOrWhiteSpace(group)) return;

            if (Registry.TryGetValue(window, out var groups) &&
                groups.TryGetValue(group, out var members))
            {
                foreach (var other in members)
                {
                    if (other != tb && other.IsChecked == true)
                        other.IsChecked = false;
                }
            }
        }
    }
}
