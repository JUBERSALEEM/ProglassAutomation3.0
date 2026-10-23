using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ProGlassAutomation.ViewModels.Inquiry;

namespace ProGlassAutomation.Inquiry;

/// <summary>
/// The inquiry inbox page — deliveries from the customer website.
/// A separate page from the register (owner spec); the register keeps
/// an inbox card at its top for one-click access.
/// </summary>
public partial class InquiryInboxView : UserControl
{
    public InquiryInboxView()
    {
        InitializeComponent();

        // The PasswordBox cannot bind, so the VM pulls its value through
        // a view-provided function; the box clears after a successful save.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is InquiryInboxViewModel vm)
            {
                vm.GetPasswordFunc = () => PasswordBox.Password;
                vm.SettingsSaved += OnSettingsSaved;
            }
        };
        Unloaded += (_, _) =>
        {
            if (DataContext is InquiryInboxViewModel vm)
                vm.SettingsSaved -= OnSettingsSaved;
        };
    }

    private void OnSettingsSaved(object? sender, EventArgs e) => PasswordBox.Clear();

    /// <summary>
    /// Double-click primary action: a downloaded delivery creates its
    /// local inquiry; an ingested one opens the inquiry in the editor.
    /// </summary>
    private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (DataContext is not InquiryInboxViewModel vm) return;
        var row = vm.SelectedItem;
        if (row == null) return;
        if (row.CanIngest) _ = vm.IngestSelectedAsync();
        else if (row.CanOpenInquiry && row.LocalInquiryId != null)
            vm.OpenInquiryRequested?.Invoke(row.LocalInquiryId.Value);
    }
}
