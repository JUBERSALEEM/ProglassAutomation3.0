using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Views.Shared.Dialogs;
using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;

namespace ProGlassAutomation.Views.ProformaInvoice.SavedInvoicesList
{
    /// <summary>Display wrapper for one version row.</summary>
    public class VersionRowDisplay
    {
        public int VersionId { get; set; }
        public int VersionNo { get; set; }
        public string Summary { get; set; } = "";
        public string Username { get; set; } = "";
        public string TimestampLocal { get; set; } = "";

        public static VersionRowDisplay From(RecordVersion v) => new()
        {
            VersionId = v.Id,
            VersionNo = v.VersionNo,
            Summary = string.IsNullOrEmpty(v.Summary) ? "Saved" : v.Summary,
            Username = v.Username,
            TimestampLocal = v.TimestampUtc.ToLocalTime()
                .ToString("dd MMM yyyy  HH:mm")
        };
    }

    /// <summary>
    /// Version history for one invoice. Restore hands the chosen
    /// snapshot back to the caller (opens it in the wizard).
    /// </summary>
    public partial class VersionsDialog : ThemedDialogWindow
    {
        /// <summary>Set when the user clicks Restore on a version.</summary>
        public int? RestoreVersionId { get; private set; }

        public bool CanRestore { get; private set; } = true;

        private VersionsDialog()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Shows the dialog. Returns the version id to restore, or null.
        /// </summary>
        public static int? Show(
            Window? owner,
            string invoiceId,
            List<RecordVersion> versions,
            bool canRestore = true)
        {
            var dialog = new VersionsDialog { Owner = owner, CanRestore = canRestore };
            dialog.DialogSubtitle =
                $"{invoiceId} — {versions.Count} version(s). " +
                "Restoring opens that version in the editor; saving it " +
                "creates a new version.";
            if (!canRestore)
                dialog.DialogSubtitle = $"{invoiceId} — {versions.Count} version(s). " +
                    "Read-only history in this load picker. Restore from the main Proforma Invoice list.";

            var rows = new List<VersionRowDisplay>();
            foreach (var v in versions)
                rows.Add(VersionRowDisplay.From(v));

            dialog.VersionsList.ItemsSource = rows;
            dialog.EmptyText.Visibility = rows.Count == 0
                ? Visibility.Visible
                : Visibility.Collapsed;

            dialog.ShowDialog();
            return dialog.RestoreVersionId;
        }

        private async void OnRestoreClick(object sender, RoutedEventArgs e)
        {
            if (!CanRestore) return;
            if (sender is not Button button ||
                button.Tag is not VersionRowDisplay row)
                return;

            var confirm = ThemedMessageBox.Show(
                this,
                $"Open Version {row.VersionNo} " +
                $"({row.TimestampLocal}) in the editor?\n\n" +
                "Your current wizard content will be replaced. " +
                "Save afterwards to store it as a new version.",
                "Restore Version",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (confirm != MessageBoxResult.Yes) return;

            // Fetch the full snapshot for this version.
            var full = await VersionHistoryService.GetByIdAsync(row.VersionId);
            if (full == null || string.IsNullOrEmpty(full.SnapshotJson))
            {
                ThemedMessageBox.Show(
                    this,
                    "This version has no restorable snapshot.",
                    "Restore Version",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            RestoreVersionId = row.VersionId;
            DialogResult = true;
            Close();
        }
    }
}
