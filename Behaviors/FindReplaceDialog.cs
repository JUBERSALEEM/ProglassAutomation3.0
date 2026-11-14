// ═══════════════════════════════════════════════════════════════════════
// FILE: FindReplaceDialog.cs
// PROJECT: ProGlassAutomation
// MODULE: Data grid — Find / Replace
//
// FindReplaceDialog split out of DataGridNavigationBehavior (Phase 11/12
// God-VM split) — behaviour unchanged.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace ProGlassAutomation.Behaviors
{
    internal static class FindReplaceDialog
    {
        /// <summary>
        /// Shows a persistent Find or Find+Replace dialog.
        /// Dialog stays open across multiple button clicks.
        /// User closes with Close button or Escape key.
        /// </summary>
        /// <param name="isReplace">
        /// True = show Find + Replace fields with Replace All button.
        /// False = show Find field only with Find Next button.
        /// </param>
        /// <param name="onFindNext">
        /// Fired each time user clicks Find Next (or presses Enter in Find field).
        /// Parameter = search text.
        /// </param>
        /// <param name="onReplaceAll">
        /// Fired when user clicks Replace All.
        /// Parameters = find text, replace text.
        /// Only used when isReplace is true.
        /// </param>
        public static void Show(
            bool isReplace,
            Action<string> onFindNext,
            Action<string, string> onReplaceAll = null)
        {
            var window = new Window
            {
                Title = isReplace ? "Find and Replace" : "Find",
                Width = 440,
                Height = isReplace ? 240 : 180,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                ResizeMode = ResizeMode.NoResize,
                ShowInTaskbar = false,
                Background = SystemColors.WindowBrush,
                FontFamily = new FontFamily("Segoe UI"),
                FontSize = 13
            };

            if (Application.Current?.MainWindow != null)
                window.Owner = Application.Current.MainWindow;

            var grid = new Grid { Margin = new Thickness(16) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(
                new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(90) });
            grid.ColumnDefinitions.Add(
                new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            // Find field
            var findLabel = new TextBlock
            {
                Text = "Find:",
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(0, 0, 8, 8)
            };
            Grid.SetRow(findLabel, 0);
            Grid.SetColumn(findLabel, 0);

            var findBox = new TextBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(6, 4, 6, 4),
                BorderBrush = SystemColors.ControlDarkBrush,
                BorderThickness = new Thickness(1)
            };
            Grid.SetRow(findBox, 0);
            Grid.SetColumn(findBox, 1);

            grid.Children.Add(findLabel);
            grid.Children.Add(findBox);

            // Replace field (only when isReplace)
            TextBox replaceBox = null;
            if (isReplace)
            {
                var replaceLabel = new TextBlock
                {
                    Text = "Replace:",
                    VerticalAlignment = VerticalAlignment.Center,
                    Margin = new Thickness(0, 0, 8, 8)
                };
                Grid.SetRow(replaceLabel, 1);
                Grid.SetColumn(replaceLabel, 0);

                replaceBox = new TextBox
                {
                    Margin = new Thickness(0, 0, 0, 8),
                    Padding = new Thickness(6, 4, 6, 4),
                    BorderBrush = SystemColors.ControlDarkBrush,
                    BorderThickness = new Thickness(1)
                };
                Grid.SetRow(replaceBox, 1);
                Grid.SetColumn(replaceBox, 1);

                grid.Children.Add(replaceLabel);
                grid.Children.Add(replaceBox);
            }

            // Buttons
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 8, 0, 0)
            };
            Grid.SetRow(buttonPanel, 4);
            Grid.SetColumnSpan(buttonPanel, 2);

            // Primary action — Find Next or Replace All
            var primaryButton = new Button
            {
                Content = isReplace ? "Replace All" : "Find Next",
                Width = 110,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                IsDefault = true
            };

            // Close button — always closes dialog
            var closeButton = new Button
            {
                Content = "Close",
                Width = 80,
                Height = 32,
                IsCancel = true
            };
            closeButton.Click += (s, e) => window.Close();

            buttonPanel.Children.Add(primaryButton);
            buttonPanel.Children.Add(closeButton);
            grid.Children.Add(buttonPanel);

            window.Content = grid;

            // Primary button click — fires callback, dialog STAYS OPEN
            primaryButton.Click += (s, e) =>
            {
                var searchText = findBox.Text?.Trim() ?? "";
                if (string.IsNullOrEmpty(searchText)) return;

                if (isReplace)
                {
                    var replaceText = replaceBox?.Text ?? "";
                    onReplaceAll?.Invoke(searchText, replaceText);
                }
                else
                {
                    onFindNext?.Invoke(searchText);
                }
                // Dialog remains open — user can repeat action
            };

            // Enter key in Find or Replace TextBox = trigger primary action
            findBox.KeyDown += (s, e) =>
            {
                if (e.Key == Key.Return)
                {
                    e.Handled = true;
                    primaryButton.RaiseEvent(
                        new RoutedEventArgs(Button.ClickEvent));
                }
            };

            if (replaceBox != null)
            {
                replaceBox.KeyDown += (s, e) =>
                {
                    if (e.Key == Key.Return)
                    {
                        e.Handled = true;
                        primaryButton.RaiseEvent(
                            new RoutedEventArgs(Button.ClickEvent));
                    }
                };
            }

            // Focus find box when window opens
            window.Loaded += (s, e) =>
            {
                findBox.Focus();
                findBox.SelectAll();
            };

            window.ShowDialog();
        }
    }
}
