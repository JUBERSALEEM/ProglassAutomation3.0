using ProGlassAutomation.Models.DeliveryNote.Common.DeliveryNoteStatus;
using System;

namespace ProGlassAutomation.Services.DeliveryNote
{
    /// <summary>
    /// Single source of truth for Delivery Note status meaning.
    ///
    /// Workspace NEW DN / CREATE DN may only open Dispatching (or legacy Draft).
    /// Confirmed deliveries (Dispatched / Delivered / Signed / Received) consume
    /// remaining qty and are opened from God View — never from NEW DN.
    /// </summary>
    public static class DeliveryNoteWorkflow
    {
        public static bool IsOpenForDispatch(string? status) =>
            string.Equals(status, DeliveryNoteStatusValues.Dispatching, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, DeliveryNoteStatusValues.Draft, StringComparison.OrdinalIgnoreCase);

        public static bool IsConfirmedDelivery(string? status) =>
            string.Equals(status, DeliveryNoteStatusValues.Dispatched, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, DeliveryNoteStatusValues.Delivered, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, DeliveryNoteStatusValues.Signed, StringComparison.OrdinalIgnoreCase) ||
            string.Equals(status, DeliveryNoteStatusValues.Received, StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// Stable line identity. SerialNumber is the JO/Master serial and must
        /// never be reassigned when creating a subsequent DN — remapping is
        /// what made remaining lines look fully delivered.
        /// </summary>
        public static string LineKey(int specGroupNumber, int serialNumber) =>
            $"{specGroupNumber}|{serialNumber}";
    }
}
