// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/ProformaInvoiceRepository/ProformaInvoiceMapper.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 20 — PER-SPEC TARGETING PERSISTENCE
//
//   BuildSnapshot: writes TargetSelection.SelectedMetric.Key and
//                  TargetSelection.AvailableSpecs.Where(IsChecked).
//                  Select(SpecGroupNumber) to snapshot.
//
//   RestoreOtherCharges: after existing restore of row properties,
//                        apply saved metric key and checked spec numbers
//                        to row.TargetSelection.
//
//   Restore order preserved: Step 5 SpecGroups → Step 6 charges.
//   By the time charges restore runs, TargetSelection.AvailableSpecs
//   is populated (via SyncTargetSelectionsFromHub triggered by Step 5).
//   We just override defaults with saved state.
//
//   Backward compatibility: null/empty saved fields → defaults apply.
//
// PREVIOUS: CHAT 15 — PROJECT REFERENCE FIELD ADDED
//   3 precise changes:
//     1. BuildSnapshot: ProjectSnapshot gains ProjectReference
//     2. RestoreWizardFromSnapshot: project.ProjectReference restored
//     3. SafeAutoSaveMasterDataAsync: projectReference passed to auto-save
//   All BUG-15/16/17/18/20 + PHASE 14 + step swap fixes preserved.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.CustomCharges;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.Wastages;
using ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step1CompanySetup;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step2ClientProfile;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step3ProjectSpecs;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.InvoiceMainSummary;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.PrintableInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository
{
    /// <summary>
    /// Converts between live wizard ViewModels and the JSON-serializable
    /// ProformaInvoiceSnapshot / SavedProformaInvoice entity.
    ///
    /// BUG-15 [CRITICAL]: Stored PI workflow status no longer trusts the
    ///                     printable PreparedByStatus field.
    /// BUG-16: Wastages restore — no UI command called from mapper.
    /// BUG-17: Spec name mismatch check removed.
    /// BUG-18: DeserializeSnapshot catches only JsonException.
    /// BUG-20: Shared static JsonSerializerOptions.
    /// PHASE 14: Auto-save hook — fire-and-forget to masterdata.db.
    /// CHAT 15:  Step swap (Step2=Client, Step3=Project).
    ///           ProjectReference field added.
    /// CHAT 20:  Per-spec targeting persistence (SelectedMetricKey +
    ///           CheckedSpecGroupNumbers on PredefinedChargeSnapshot).
    /// </summary>
    public static partial class ProformaInvoiceMapper
    {
    }
}