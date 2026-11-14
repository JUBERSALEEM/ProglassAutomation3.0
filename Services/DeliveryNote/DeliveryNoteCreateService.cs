using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Services.DeliveryNote.Mapper;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.Logging;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.DeliveryNote
{
    /// <summary>
    /// Workspace NEW DN / CREATE DN — one path for every entry point.
    ///
    /// Rules:
    ///   1. Never open a Dispatched (or later) DN. Those open from God View.
    ///   2. If a Dispatching/Draft DN already exists for the JO, open it.
    ///   3. Otherwise create a new Dispatching DN with remaining balance only.
    ///   4. Line SerialNumber is copied from Master/JO — never reassigned.
    ///   5. Remaining qty is confirmed deliveries only (not in-progress drafts).
    /// </summary>
    public sealed class DnCreateResult
    {
        public enum Kind
        {
            OpenExisting,
            Created,
            FullyDelivered,
            Failed
        }

        public Kind Outcome { get; init; }
        public int DeliveryNoteDbId { get; init; }
        public string DeliveryNoteId { get; init; } = "";
        public string Message { get; init; } = "";

        public static DnCreateResult OpenExisting(SavedDeliveryNote dn) => new()
        {
            Outcome = Kind.OpenExisting,
            DeliveryNoteDbId = dn.Id,
            DeliveryNoteId = dn.DeliveryNoteId ?? ""
        };

        public static DnCreateResult Created(SavedDeliveryNote dn) => new()
        {
            Outcome = Kind.Created,
            DeliveryNoteDbId = dn.Id,
            DeliveryNoteId = dn.DeliveryNoteId ?? ""
        };

        public static DnCreateResult FullyDelivered(
            string jobOrderId, int ordered, int delivered) => new()
        {
            Outcome = Kind.FullyDelivered,
            Message =
                "All confirmed deliveries for this job order are complete.\n\n" +
                $"Job Order : {jobOrderId}\n" +
                $"Ordered   : {ordered} pcs\n" +
                $"Delivered : {delivered} pcs\n" +
                "Balance   : 0 pcs\n\n" +
                "Open God View to inspect dispatched delivery notes."
        };

        public static DnCreateResult Fail(string message) => new()
        {
            Outcome = Kind.Failed,
            Message = message
        };
    }

    public static class DeliveryNoteCreateService
    {
        public static async Task<DnCreateResult> CreateOrOpenDispatchingAsync(
            SavedJobOrder jobOrder)
        {
            if (jobOrder == null || string.IsNullOrWhiteSpace(jobOrder.JobOrderId))
                return DnCreateResult.Fail("No job order selected.");

            if (App.DeliveryNotes == null)
                return DnCreateResult.Fail("Delivery Notes database not initialized.");

            try
            {
                try
                {
                    await MasterDeliveryNoteService.EnsureForJobOrderAsync(jobOrder);
                }
                catch (Exception ex)
                {
                    AppLogService.Debug("[DN Create] Ensure master failed: " + ex.Message);
                }

                try
                {
                    await App.DeliveryNotes.UpdateMasterDnAsync(jobOrder.JobOrderId, "");
                }
                catch (Exception ex)
                {
                    AppLogService.Debug("[DN Create] Update master failed: " + ex.Message);
                }

                var existing = await App.DeliveryNotes
                    .GetByJobOrderIdAsync(jobOrder.JobOrderId);

                var openRow = existing
                    .Where(d => !d.IsMasterDn
                             && !string.Equals(d.DeliveryType, "Dummy",
                                 StringComparison.OrdinalIgnoreCase)
                             && DeliveryNoteWorkflow.IsOpenForDispatch(d.Status))
                    .OrderByDescending(d => d.SequenceNumber)
                    .FirstOrDefault();

                if (openRow != null)
                {
                    var openEntity = await App.DeliveryNotes.GetByIdAsync(openRow.Id);
                    if (openEntity != null &&
                        !openEntity.IsMasterDn &&
                        !string.Equals(openEntity.DeliveryType, "Dummy",
                            StringComparison.OrdinalIgnoreCase) &&
                        DeliveryNoteWorkflow.IsOpenForDispatch(openEntity.Status))
                    {
                        return DnCreateResult.OpenExisting(openEntity);
                    }
                }

                var master = await App.DeliveryNotes
                    .GetMasterDnForJobOrderAsync(jobOrder.JobOrderId);

                if (master == null ||
                    master.Items == null ||
                    master.Items.Count == 0)
                    return await CreateFirstFromJobOrderAsync(jobOrder);

                var remaining = (master.Items ?? new System.Collections.Generic.List<DeliveryNoteItem>())
                    .Where(i => i.ComputedBalanceQty > 0)
                    .ToList();

                if (remaining.Count == 0)
                {
                    return DnCreateResult.FullyDelivered(
                        jobOrder.JobOrderId,
                        master.TotalOrderedQty,
                        master.TotalDeliveredQty);
                }

                return await InsertFromMasterAsync(master);
            }
            catch (Exception ex)
            {
                return DnCreateResult.Fail(
                    "Failed to create delivery note:\n\n" + ex.Message);
            }
        }

        private static async Task<DnCreateResult> InsertFromMasterAsync(
            SavedDeliveryNote master)
        {
            int nextSeq = await App.DeliveryNotes!
                .GetNextSequenceAsync(master.JobOrderId);

            string dnId = await App.DeliveryNotes
                .BuildDeliveryNoteIdAsync(master.JobOrderId, nextSeq);

            if (await App.DeliveryNotes.ExistsByDeliveryNoteIdAsync(dnId))
            {
                return DnCreateResult.Fail(
                    $"Delivery note '{dnId}' already exists.\n\n" +
                    "Please refresh and try again.");
            }

            var newDn = DeliveryNoteMapper.FromMasterBalance(master, dnId, nextSeq);
            if (newDn.Items == null || newDn.Items.Count == 0)
            {
                return DnCreateResult.FullyDelivered(
                    master.JobOrderId,
                    master.TotalOrderedQty,
                    master.TotalDeliveredQty);
            }

            var saved = await App.DeliveryNotes.InsertAsync(newDn);
            return DnCreateResult.Created(saved);
        }

        private static async Task<DnCreateResult> CreateFirstFromJobOrderAsync(
            SavedJobOrder jobOrder)
        {
            var snapshot = JobOrderMapper.DeserializeSnapshot(jobOrder);
            if (snapshot == null)
            {
                return DnCreateResult.Fail(
                    $"Could not read Job Order data for '{jobOrder.JobOrderId}'.\n\n" +
                    "The snapshot may be missing or corrupted.");
            }

            int nextSeq = await App.DeliveryNotes!
                .GetNextSequenceAsync(jobOrder.JobOrderId);

            string dnId = await App.DeliveryNotes
                .BuildDeliveryNoteIdAsync(jobOrder.JobOrderId, nextSeq);

            if (await App.DeliveryNotes.ExistsByDeliveryNoteIdAsync(dnId))
                return DnCreateResult.Fail($"Delivery note '{dnId}' already exists.");

            var dnEntity = DeliveryNoteMapper.FromJobOrder(
                jobOrder, snapshot, dnId, nextSeq);

            foreach (var item in dnEntity.Items)
            {
                int previouslyDelivered = await App.DeliveryNotes
                    .GetPreviouslyDeliveredQtyAsync(
                        jobOrder.JobOrderId,
                        item.SpecGroupNumber,
                        item.SerialNumber);

                item.PreviouslyDeliveredQty = previouslyDelivered;
                item.BalanceQty = Math.Max(0,
                    item.OrderedQty - previouslyDelivered - item.DeliveredQty);
            }

            dnEntity.Items = dnEntity.Items
                .Where(i => i.ComputedBalanceQty > 0)
                .ToList();
            dnEntity.TotalPreviouslyDeliveredQty =
                dnEntity.Items.Sum(i => i.PreviouslyDeliveredQty);
            dnEntity.TotalBalanceQty = dnEntity.Items.Sum(i => i.BalanceQty);
            dnEntity.TotalOrderedQty = dnEntity.Items.Sum(i => i.OrderedQty);

            if (dnEntity.Items.Count == 0)
            {
                return DnCreateResult.FullyDelivered(
                    jobOrder.JobOrderId, 0, 0);
            }

            var saved = await App.DeliveryNotes.InsertAsync(dnEntity);
            return DnCreateResult.Created(saved);
        }
    }
}
