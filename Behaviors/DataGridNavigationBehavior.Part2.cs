// ═══════════════════════════════════════════════════════════════════════
// FILE: DataGridNavigationBehavior.Part2.cs (Phase 3.8 split — member census verified)
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
        public static partial class DataGridNavigationBehavior
    {
            private static void AutoAddRowAndFocus(
            FrameworkElement container, int targetColumn)
        {
            var pasteable = FindViewModelInTree(container);
            if (pasteable == null) return;

            pasteable.AddRows(1);

            var itemsCtrl = FindItemsControl(container);
            if (itemsCtrl is not ListView listView) return;

            int newRowIndex = listView.Items.Count - 1;
            if (newRowIndex < 0) return;

            var newItem = listView.Items[newRowIndex];
            listView.ScrollIntoView(newItem);

            container.Dispatcher.BeginInvoke(new Action(() =>
            {
                InvalidateTextBoxCache(container);

                var newContainer = listView.ItemContainerGenerator
                    .ContainerFromItem(newItem) as FrameworkElement;

                if (newContainer == null) return;

                var boxes = FindAllTextBoxes(newContainer);
                int col = targetColumn >= 0 && targetColumn < boxes.Count
                    ? targetColumn : 0;

                if (boxes.Count > 0)
                    FocusAndSelect(boxes[col]);

            }), DispatcherPriority.Loaded);
        }

        // ══════════════════════════════════════════════════════════════════
        // SCROLL ROW INTO VIEW AND FOCUS
        // ══════════════════════════════════════════════════════════════════

        private static void ScrollRowIntoViewAndFocus(
            FrameworkElement container,
            int targetRow,
            int targetColumn)
        {
            var itemsCtrl = FindItemsControl(container);
            if (itemsCtrl == null) return;

            var items = itemsCtrl.Items;
            if (targetRow < 0 || targetRow >= items.Count) return;

            var targetItem = items[targetRow];

            if (itemsCtrl is ListBox listBox)
                listBox.ScrollIntoView(targetItem);
            else if (itemsCtrl is DataGrid dataGrid)
                dataGrid.ScrollIntoView(targetItem);
            else
            {
                var fe = itemsCtrl.ItemContainerGenerator
                    .ContainerFromItem(targetItem) as FrameworkElement;
                fe?.BringIntoView();
            }

            container.Dispatcher.BeginInvoke(new Action(() =>
            {
                var container2 = itemsCtrl.ItemContainerGenerator
                    .ContainerFromItem(targetItem) as FrameworkElement;

                if (container2 == null) return;

                var textBoxes = FindAllTextBoxes(container2);
                if (textBoxes.Count == 0) return;

                int col = targetColumn >= 0 && targetColumn < textBoxes.Count
                    ? targetColumn : 0;

                FocusAndSelect(textBoxes[col]);

            }), DispatcherPriority.Loaded);
        }

        // ══════════════════════════════════════════════════════════════════
        // COLUMN INDEX DETECTION
        // ══════════════════════════════════════════════════════════════════

        private static int FindColumnIndex(TextBox focusedBox)
        {
            var rowContainer = FindRowContainer(focusedBox);
            if (rowContainer == null) return -1;

            var rowBoxes = FindAllTextBoxes(rowContainer);
            if (rowBoxes.Count == 0) return -1;

            return rowBoxes.IndexOf(focusedBox);
        }

        // ══════════════════════════════════════════════════════════════════
        // VISUAL TREE HELPERS
        // ══════════════════════════════════════════════════════════════════

        private static object FindRowDataContext(DependencyObject element)
        {
            var current = VisualTreeHelper.GetParent(element);
            while (current != null)
            {
                if (current is ListViewItem lvi)
                    return lvi.DataContext;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static IExcelPasteable FindViewModelInTree(DependencyObject start)
        {
            var current = start;
            while (current != null)
            {
                if (current is FrameworkElement fe &&
                    fe.DataContext is IExcelPasteable pasteable)
                    return pasteable;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        private static ItemsControl FindItemsControl(DependencyObject parent)
        {
            if (parent is ItemsControl ic) return ic;

            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is ItemsControl found) return found;
                var deeper = FindItemsControl(child);
                if (deeper != null) return deeper;
            }
            return null;
        }

        private static DependencyObject FindRowContainer(DependencyObject element)
        {
            var current = VisualTreeHelper.GetParent(element);
            while (current != null)
            {
                if (current is ListViewItem) return current;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        /// <summary>
        /// Generic visual tree ancestor finder.
        /// Walks UP from the element until it finds an ancestor of type T,
        /// or returns null if not found.
        /// </summary>
        private static T FindAncestor<T>(DependencyObject element)
            where T : DependencyObject
        {
            var current = element;
            while (current != null)
            {
                if (current is T match) return match;
                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }

        // ══════════════════════════════════════════════════════════════════
        // TEXTBOX DISCOVERY
        // ══════════════════════════════════════════════════════════════════

        private static List<TextBox> FindAllTextBoxes(DependencyObject parent)
        {
            var result = new List<TextBox>();
            if (parent == null) return result;
            CollectTextBoxes(parent, result);
            return result;
        }

        private static void CollectTextBoxes(
            DependencyObject parent, List<TextBox> result)
        {
            int count = VisualTreeHelper.GetChildrenCount(parent);
            for (int i = 0; i < count; i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is TextBox tb)
                    result.Add(tb);
                else
                    CollectTextBoxes(child, result);
            }
        }

        // ══════════════════════════════════════════════════════════════════
        // UTILITY
        // ══════════════════════════════════════════════════════════════════

        private static void FocusAndSelect(TextBox tb)
        {
            tb.Focus();
            tb.SelectAll();
        }

        /// <summary>
        /// Excel rule: stay in the cell while the user is moving the caret.
        /// Leave when the whole value is selected, or the caret is already
        /// at the edge in the travel direction.
        /// </summary>
        private static bool ShouldLeaveCell(TextBox tb, bool goingRight)
        {
            string text = tb.Text ?? string.Empty;
            if (text.Length == 0) return true;
            if (tb.SelectionLength == text.Length) return true;

            int caret = tb.CaretIndex;
            if (tb.SelectionLength > 0)
                caret = goingRight
                    ? tb.SelectionStart + tb.SelectionLength
                    : tb.SelectionStart;

            return goingRight ? caret >= text.Length : caret <= 0;
        }

        private static int GetColumnsPerRow(TextBox current)
        {
            var rowContainer = FindRowContainer(current);
            if (rowContainer == null) return 7;

            var boxes = FindAllTextBoxes(rowContainer);
            return boxes.Count > 0 ? boxes.Count : 7;
        }
    }
}
