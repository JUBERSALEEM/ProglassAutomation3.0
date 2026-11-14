using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList;
using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ProGlassAutomation.Views.ProformaInvoice.SavedInvoicesList
{
    /// <summary>
    /// SavedInvoicesListView code-behind.
    ///
    /// BUG-72 SUPPORT: Status menu now passes Tuple(item, status) matching
    ///                 updated ChangeStatusCommand signature in the VM.
    /// APPROVAL FLOW: The manual "Convert to Job Order" item is REMOVED.
    ///                Approving now runs a gate (Signed PI or LPO) and, on
    ///                confirmation, the invoice is auto-converted to a Job
    ///                Order inside the ViewModel (ApproveAsync), so this view
    ///                no longer holds any conversion logic.
    /// BUG-86 [HIGH]: Dynamic MenuItem generation stays in code-behind by
    ///                WPF design — XAML cannot cleanly bind nested
    ///                MenuItem.Command with per-item CommandParameter for
    ///                a status list. Documented as WPF limitation, not bug.
    /// BUG-87 [MEDIUM]: menu.PlacementTarget = element set explicitly so
    ///                  the menu anchors to the row that was right-clicked.
    /// BUG-88 [MEDIUM]: Loading spinner glyph documented — animation is
    ///                  a UI polish concern deferred to a future phase.
    /// </summary>
    public partial class SavedInvoicesListView : UserControl
    {
        public SavedInvoicesListView()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Builds a styled right-click context menu dynamically.
        /// BUG-86: This uses code-behind by design — WPF cannot bind
        /// nested MenuItem.Command with per-item CommandParameter for
        /// dynamic lists of status values in pure XAML.
        /// </summary>
        private void OnRowRightClick(object sender, MouseButtonEventArgs e)
        {
            ShowRowMenu(sender as FrameworkElement, atPointer: true);
            e.Handled = true;
        }

        // The visible More button and right-click share every original menu action.
        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Border is not a Control, so it has no MouseDoubleClick event.
            // PreviewMouseLeftButtonDown is on UIElement; only the second click opens.
            if (e.ClickCount != 2) return;
            if (sender is not FrameworkElement element) return;
            if (element.DataContext is not SavedInvoiceListItem item) return;
            if (DataContext is not SavedInvoicesListViewModel vm) return;
            // Action buttons keep their own click. A double-click on Open,
            // versions, or More must not also fire the row open.
            if (e.OriginalSource is DependencyObject source)
            {
                var current = source;
                while (current != null && !ReferenceEquals(current, element))
                {
                    if (current is Button)
                        return;
                    current = System.Windows.Media.VisualTreeHelper.GetParent(current);
                }
            }
            if (vm.LoadInvoiceCommand?.CanExecute(item) == true)
                vm.LoadInvoiceCommand.Execute(item);
            e.Handled = true;
        }

        private void OnRowMoreClick(object sender, RoutedEventArgs e)
        {
            ShowRowMenu(sender as FrameworkElement, atPointer: false);
            e.Handled = true;
        }

        private void ShowRowMenu(FrameworkElement? element, bool atPointer)
        {
            if (element == null) return;
            if (element.DataContext is not SavedInvoiceListItem item) return;
            if (DataContext is not SavedInvoicesListViewModel vm) return;

            var menu = new ContextMenu
            {
                // BUG-87: anchor menu to the clicked row explicitly
                PlacementTarget = element,
                Placement = atPointer ? System.Windows.Controls.Primitives.PlacementMode.MousePoint
                    : System.Windows.Controls.Primitives.PlacementMode.Bottom,
                Style = (Style)FindResource("Sl.ContextMenu")
            };

            // ── Primary actions ──────────────────────────────────────────
            menu.Items.Add(CreateMenuItem("📂  Open & Edit Invoice",
                vm.LoadInvoiceCommand, item, bold: true));
            menu.Items.Add(CreateMenuItem("📋  Duplicate Invoice",
                vm.DuplicateInvoiceCommand, item));
            menu.Items.Add(CreateMenuItem("🧬  Create Revision (original preserved)",
                vm.CreateRevisionCommand, item));

            menu.Items.Add(new Separator
            {
                Style = (Style)FindResource("Sl.MenuSeparator")
            });

            // ── Export sub-menu ──────────────────────────────────────────
            var exportMenu = new MenuItem
            {
                Header = "📤  Export",
                Style = (Style)FindResource("Sl.MenuItem")
            };
            exportMenu.Items.Add(CreateMenuItem("PDF — Full Invoice",
                vm.ExportPdfFromListCommand, item));
            exportMenu.Items.Add(CreateMenuItem("Excel — Workbook",
                vm.ExportExcelFromListCommand, item));
            exportMenu.Items.Add(CreateMenuItem("CSV — Comma Separated",
                vm.ExportCsvFromListCommand, item));
            menu.Items.Add(exportMenu);

            menu.Items.Add(new Separator
            {
                Style = (Style)FindResource("Sl.MenuSeparator")
            });

            // ── Change Status sub-menu ───────────────────────────────────
            // Friendly business labels map onto the stored status values the
            // ViewModel already understands (Approved still routes through the
            // Step 8 approval flow; Rejected displays as "Cancelled").
            var statusMenu = new MenuItem
            {
                Header = "🔄  Change Status",
                Style = (Style)FindResource("Sl.MenuItem")
            };
            statusMenu.Items.Add(CreateStatusItem("PI Created", "Draft", item, vm));
            statusMenu.Items.Add(CreateStatusItem("PI Sent", "Sent", item, vm));
            statusMenu.Items.Add(CreateStatusItem("PI Confirmed", "Approved", item, vm));
            statusMenu.Items.Add(CreateStatusItem("Cancelled", "Rejected", item, vm));
            statusMenu.Items.Add(CreateStatusItem("Expired", "Expired", item, vm));
            menu.Items.Add(statusMenu);

            menu.Items.Add(new Separator
            {
                Style = (Style)FindResource("Sl.MenuSeparator")
            });

            var deleteItem = CreateMenuItem("🗑  Delete Invoice",
                vm.DeleteInvoiceCommand, item);
            deleteItem.Foreground = new System.Windows.Media.SolidColorBrush(
                (System.Windows.Media.Color)FindResource("DangerColor"));
            menu.Items.Add(deleteItem);

            element.ContextMenu = menu;
            menu.IsOpen = true;

        }

        private MenuItem CreateMenuItem(
            string header, ICommand command, object parameter, bool bold = false)
        {
            var item = new MenuItem
            {
                Header = header,
                Command = command,
                CommandParameter = parameter,
                Style = (Style)FindResource("Sl.MenuItem"),
                Padding = new Thickness(8, 6, 20, 6)
            };
            if (bold) item.FontWeight = FontWeights.SemiBold;
            return item;
        }

        /// <summary>
        /// BUG-72 SUPPORT: Now passes Tuple(item, status) as command parameter
        /// matching updated ChangeStatusCommand signature in the VM.
        ///
        /// Previously used a direct event handler that called API directly —
        /// this bypassed the VM's error handling. Now goes through the
        /// command's SafeFireAsync path.
        /// </summary>
        private MenuItem CreateStatusItem(
            string displayLabel, string storedStatus,
            SavedInvoiceListItem item, SavedInvoicesListViewModel vm)
        {
            var menuItem = new MenuItem
            {
                Header = displayLabel,
                Tag = storedStatus,
                IsCheckable = true,
                IsChecked = string.Equals(item.Status, storedStatus, StringComparison.OrdinalIgnoreCase),
                Style = (Style)FindResource("Sl.CheckableMenuItem"),
                Padding = new Thickness(8, 6, 20, 6),

                // BUG-72: use command with Tuple parameter — VM handles errors
                Command = vm.ChangeStatusCommand,
                CommandParameter = Tuple.Create(item, storedStatus)
            };

            return menuItem;
        }
    }
}