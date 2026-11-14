using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Workflow;
using ProGlassAutomation.Models.ProformaInvoice.Common;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Audit;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.Excel;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.ProformaInvoice.Attachments;
using ProGlassAutomation.Services.ProformaInvoice.Export;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Documents;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using ProGlassAutomation.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Services.Localization;

using ProGlassAutomation.Services.Dialogs;
using ProGlassAutomation.Services.JobOrder.Repository;
using Microsoft.Extensions.DependencyInjection;
using ProGlassAutomation.Views.ProformaInvoice.SavedInvoicesList;
namespace ProGlassAutomation.ViewModels.ProformaInvoice.SavedInvoicesList
{
    /// <summary>
    /// ViewModel for the Saved Invoices list page.
    ///
    /// BUG-72 [HIGH]: ChangeStatusCommand accepts Tuple parameter matching
    ///                what the view code-behind actually passes.
    /// BUG-73 [HIGH]: Filter changes no longer trigger LoadInvoicesAsync().
    ///                Filtering is 100% in-memory over _allItems.
    /// BUG-74 [MEDIUM]: ApplyFilters uses standard clear+refill pattern.
    /// BUG-75 [MEDIUM]: Export success shows LastExportStatus feedback.
    /// BUG-76 [LOW]: Added ExportCsvFromListCommand for detail view parity.
    /// </summary>
    public partial class SavedInvoicesListViewModel : ViewModelBase, IDisposable
    {
    }
}