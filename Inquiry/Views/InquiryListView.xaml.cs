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
