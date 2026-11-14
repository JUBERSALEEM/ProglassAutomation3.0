// After a Job Order, Delivery Note or Tax Invoice is removed, the source
// Proforma Invoice must match what is still on the lists.
//
// Display status (Fully invoiced / delivered / Full job created) is derived
// from remaining related documents. When NONE remain, the PI is stored as
// Rejected so the list shows Cancelled.
//
// This sync is ONLY invoked from downstream DELETE operations, so a PI that
// reaches it demonstrably had a Job Order / Delivery Note / Tax Invoice — it
// entered the workflow. Its stored status therefore must NOT gate the
// cancellation: a Job Order can be built from a Draft quote via the picker
// ("include non-approved" checkbox), and deleting that Job Order must still
// cancel the PI instead of leaving it showing "PI created".
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    public static class ProformaLifecycleSync
    {
        public static async Task AfterDownstreamChangeAsync(string? proformaId)
        {
            if (string.IsNullOrWhiteSpace(proformaId) || App.Invoices == null)
                return;

            try
            {
                bool anyJob = false;
                bool anyDelivery = false;
                bool anyTaxInvoice = false;

                if (App.Jobs != null)
                {
                    var jobs = await App.Jobs.GetBySourceProformaIdAsync(proformaId);
                    anyJob = jobs != null && jobs.Count > 0;
                }

                if (App.DeliveryNotes != null)
                {
                    var notes = await App.DeliveryNotes.GetAllAsync();
                    anyDelivery = notes.Any(d =>
                        string.Equals(d.ProformaId, proformaId,
                            StringComparison.OrdinalIgnoreCase));
                }

                if (App.TaxInvoices != null)
                {
                    var invoices = await App.TaxInvoices.GetByProformaIdAsync(proformaId);
                    anyTaxInvoice = invoices != null && invoices.Count > 0;
                }

                if (anyJob || anyDelivery || anyTaxInvoice)
                    return;

                // MarkCancelledAsync is a dedicated lifecycle write: it
                // persists status + snapshot without demanding ProformaEdit
                // (authorization is the originating downstream delete), so
                // e.g. the Production role deleting a Job Order still gets
                // the PI cancelled. It also raises the change signal so the
                // saved-invoices list refreshes to "Cancelled" immediately.
                bool updated = await App.Invoices
                    .MarkCancelledAsync(proformaId);
                if (!updated)
                    return;

                AuditService.Log(
                    AuditSections.Proforma, "Invoice cancelled",
                    $"{proformaId} — related job order, delivery note and tax invoice removed",
                    "PI", proformaId);

                // NOTE: attachments are intentionally NOT cleared here.
                // Whether the cancelled PI keeps its Step 8 attachments is
                // an explicit user decision in the job-order delete flow
                // (Delete / Keep gate). This sync only fixes status.
            }
            catch (Exception ex)
            {
                AppLogService.Error(
                    "Proforma lifecycle sync failed.", ex,
                    new LogContext
                    {
                        Module = "Proforma",
                        Class = nameof(ProformaLifecycleSync),
                        Operation = "AfterDownstreamChange"
                    });
            }
        }
    }
}
