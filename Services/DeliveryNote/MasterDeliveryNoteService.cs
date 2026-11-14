using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Services.DeliveryNote.Mapper;
using ProGlassAutomation.Services.JobOrder.Mapper;
using System;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.DeliveryNote
{
    /// <summary>
    /// Ensures a Master DN exists for a Job Order.
    /// Called after PI → JO auto-convert and before the first real DN.
    /// Master DN is the balance tracker (all JO lines, remaining qty).
    /// </summary>
    public static class MasterDeliveryNoteService
    {
        public static async Task<SavedDeliveryNote?> EnsureForJobOrderAsync(
            SavedJobOrder jobOrder)
        {
            if (jobOrder == null || App.DeliveryNotes == null)
                return null;
            if (string.IsNullOrWhiteSpace(jobOrder.JobOrderId))
                return null;

            var existing = await App.DeliveryNotes
                .GetMasterDnForJobOrderAsync(jobOrder.JobOrderId);
            if (existing != null) return existing;

            var snapshot = JobOrderMapper.DeserializeSnapshot(jobOrder);
            if (snapshot == null) return null;

            string masterId = await App.DeliveryNotes
                .BuildMasterDnIdAsync(jobOrder.JobOrderId);

            var template = DeliveryNoteMapper.FromJobOrder(
                jobOrder, snapshot, masterId, 0);

            if (template.Items == null || template.Items.Count == 0)
                return null;

            return await App.DeliveryNotes.CreateMasterDnAsync(
                jobOrder.JobOrderId,
                jobOrder.SourceProformaId ?? "",
                jobOrder.CustomerName ?? "",
                jobOrder.ProjectName ?? "",
                masterId,
                template.Items);
        }
    }
}
