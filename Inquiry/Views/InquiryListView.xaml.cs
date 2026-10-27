using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProGlassAutomation.ViewModels.Inquiry;

namespace ProGlassAutomation.Inquiry;

/// <summary>
/// Inquiry register (list page). Code-behind is navigation only:
/// double-click / Enter open the selected row in the module's editor
/// page. All business logic lives in InquiryListViewModel.
/// </summary>
public partial class InquiryListView : UserControl
{
    public InquiryListView()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Opens the command bar's overflow menu on a left click.
    ///
    /// A Button.ContextMenu only opens on right-click by its own wiring, so the
    /// menu a user can see needs one line of code-behind to show it. The menu is
    /// itself a themed ContextMenu with MenuItem children - no Popup, ToolBar or
    /// Menu, none of which is in the theme contract.
    /// </summary>
    private void OnMoreActionsClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        OpenSelectedRow();
        e.Handled = true;
    }

    private void OnGridKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            OpenSelectedRow();
            e.Handled = true;
        }
    }

    private void OpenSelectedRow()
    {
        if (DataContext is not InquiryListViewModel vm) return;
        if (vm.SelectedInquiry is { } row && vm.OpenInquiryRowCommand.CanExecute(row))
            vm.OpenInquiryRowCommand.Execute(row);
    }
}
