// ═══════════════════════════════════════════════════════════════════════
//  FILE: Services/ProformaInvoice/ActiveInvoiceContext.cs
//  PROJECT: ProGlassAutomation
//  CREATED: 2026-09-02 — Specifications Center usage history
//
//  WHY THIS EXISTS
//  ---------------------------------------------------------------------
//  Recording "which proforma used this specification" needs the PI number,
//  customer and project at the moment Step 4 saves. Step 4's view model
//  has none of them: they live in Step 1 (ProformaId), Step 2 (customer)
//  and Step 3 (project), which are sibling view models it does not
//  reference — deliberately, so the pricing panel stays independently
//  testable and does not drag the whole wizard in behind it.
//
//  Injecting three more dependencies into PricingEstimatorViewModel to
//  fetch three strings would invert that, and the constructor is already
//  DI-resolved (see the ERR-633EEB two-phase work — adding parameters
//  there is exactly the change that has broken construction before).
//
//  So this is a tiny ambient snapshot, following the SpecSheetRegistry
//  pattern already used for Step 4 → Step 5 sheet hand-off: the wizard
//  publishes the current invoice's identity, anything that needs it reads
//  it. One writer, many readers, no new constructor arguments.
//
//  DELIBERATELY NOT PERSISTED
//  This is display/traceability context for the CURRENT session. Unlike
//  SpecSheetRegistry — whose volatility caused a real bug, because Step 9
//  costing depended on it — nothing here feeds a calculation. A miss
//  means a usage row reads "(unsaved draft)", which is honest and
//  harmless.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// Ambient identity of the proforma currently open in the wizard.
    /// Written by the wizard as the user fills Steps 1–3; read when a
    /// specification usage row is recorded.
    /// </summary>
    public static class ActiveInvoiceContext
    {
        private static readonly object Gate = new();

        private static string _proformaId = string.Empty;
        private static string _customerName = string.Empty;
        private static string _projectName = string.Empty;

        public static string ProformaId
        {
            get { lock (Gate) return _proformaId; }
        }

        public static string CustomerName
        {
            get { lock (Gate) return _customerName; }
        }

        public static string ProjectName
        {
            get { lock (Gate) return _projectName; }
        }

        /// <summary>
        /// Publishes the active invoice's identity. Null arguments are
        /// stored as empty strings so readers never have to null-check.
        /// </summary>
        public static void Set(
            string? proformaId, string? customerName, string? projectName)
        {
            lock (Gate)
            {
                _proformaId = (proformaId ?? string.Empty).Trim();
                _customerName = (customerName ?? string.Empty).Trim();
                _projectName = (projectName ?? string.Empty).Trim();
            }
        }

        /// <summary>
        /// Updates only the proforma number, leaving customer/project as
        /// they are. Used when Step 1 assigns or regenerates the PI number
        /// after the client details were already entered.
        /// </summary>
        public static void SetProformaId(string? proformaId)
        {
            lock (Gate) _proformaId = (proformaId ?? string.Empty).Trim();
        }

        /// <summary>Clears the context when the wizard closes or resets.</summary>
        public static void Clear()
        {
            lock (Gate)
            {
                _proformaId = string.Empty;
                _customerName = string.Empty;
                _projectName = string.Empty;
            }
        }
    }
}
