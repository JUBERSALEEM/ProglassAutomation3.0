using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Database.SchemaMigrations;
using ProGlassAutomation.Services.DeliveryNote.Database;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;

namespace ProGlassAutomation.Services.DeliveryNote.Repository
{
    /// <summary>
    /// EF Core implementation of IDeliveryNoteRepository.
    ///
    /// CHAT 19: Master DN system added.
    /// CHAT 19 INVOICED:
    ///   MarkAsInvoicedAsync         — marks single DN invoiced after TI saved
    ///   MarkMultipleAsInvoicedAsync — marks multiple DNs invoiced (Master Invoice)
    ///   GetDeliveryHistoryAsync     — now maps HasTaxInvoice + TaxInvoiceId
    ///                                 onto each DeliveryHistoryEntry so the
    ///                                 history sidebar can show invoiced badges.
    /// </summary>
    public partial class DeliveryNoteRepository : IDeliveryNoteRepository
    {
    }
}