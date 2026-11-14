// ═══════════════════════════════════════════════════════════════════════
// FILE: Behaviors/FocusOnCheckedBehavior.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.7 — replaces view-only "focus the field when this radio gets
// checked" code-behind (InvoiceApprovalDialog's LPO radio). Attached
// Target is focused (and its text selected) when the owning
// ToggleButton becomes checked. The focus is deferred to layout time,
// mirroring the old Dispatcher.BeginInvoke so the target is measurable.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Attached behavior: <c>FocusOnCheckedBehavior.Target</c> is focused
    /// (with select-all for a TextBox) when the element it is attached to
    /// becomes checked.
    /// Usage:
    ///   <RadioButton behaviors:FocusOnCheckedBehavior.Target=
    ///                         "{Binding ElementName=LpoBox}" .../>
    /// </summary>
    public static class FocusOnCheckedBehavior
    {
        public static readonly DependencyProperty TargetProperty =
            DependencyProperty.RegisterAttached(
                "Target", typeof(FrameworkElement), typeof(FocusOnCheckedBehavior),
                new PropertyMetadata(null, OnTargetChanged));

        public static void SetTarget(DependencyObject d, FrameworkElement? value) =>
            d.SetValue(TargetProperty, value);
        public static FrameworkElement? GetTarget(DependencyObject d) =>
            (FrameworkElement?)d.GetValue(TargetProperty);

        private static void OnTargetChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            if (d is not ToggleButton button) return;

            var descriptor = DependencyPropertyDescriptor.FromProperty(
                ToggleButton.IsCheckedProperty, typeof(ToggleButton));
            descriptor.RemoveValueChanged(button, OnIsCheckedChanged);

            if (e.NewValue is FrameworkElement)
                descriptor.AddValueChanged(button, OnIsCheckedChanged);
        }

        private static void OnIsCheckedChanged(object? sender, EventArgs e)
        {
            if (sender is not ToggleButton { IsChecked: true } button) return;
            if (GetTarget(button) is not FrameworkElement target) return;

            // Defer until layout completes so the target is measurable
            // (mirrors the old Dispatcher.BeginInvoke focus code).
            target.Dispatcher.BeginInvoke(new Action(() =>
            {
                target.Focus();
                if (target is TextBox textBox)
                    textBox.SelectAll();
            }));
        }
    }
}
