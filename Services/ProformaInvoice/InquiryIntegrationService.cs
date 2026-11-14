// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/ProformaInvoice/InquiryIntegrationService.cs
// PROJECT: ProGlassAutomation
// MODULE: Proforma Invoice — Inquiry integration logic
//
// THE PROBLEM THIS SOLVES
//   ProformaInvoiceViewModel had inquiry integration logic mixed with
//   navigation and persistence. This service extracts the inquiry-to-PI
//   flow so it can be tested independently and reused by other modules.
//
// CONTRACT
//   • Validates inquiry eligibility for PI creation
//   • Pre-fills wizard steps from inquiry data
//   • Handles the "awaiting inquiry selection" state
//   • No WPF dependencies — pure business logic
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Inquiry;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice
{
    /// <summary>
    /// Callback interface for wizard operations that the service needs
    /// but shouldn't own (e.g., resetting wizard state, showing dialogs).
    /// </summary>
    public interface IInquiryIntegrationCallbacks
    {
        /// <summary>
        /// Resets the wizard to a blank state. Called before pre-filling
        /// from inquiry data.
        /// </summary>
        Task ResetWizardAsync();

        /// <summary>
        /// Sets the inquiry ID on Step 1.
        /// </summary>
        void SetInquiryId(int? inquiryId);

        /// <summary>
        /// Sets customer information on Step 2.
        /// </summary>
        void SetCustomerInfo(string customerName, string? customerLocation, string? salesmanName, string? preparedByName);

        /// <summary>
        /// Sets project information on Step 3.
        /// </summary>
        void SetProjectInfo(string projectName, string? projectDescription, string inquiryNumber);

        /// <summary>
        /// Adopts external customer selection (triggers client card selection).
        /// </summary>
        void AdoptExternalCustomerSelection(string customerName);

        /// <summary>
        /// Sets the client filter for project lookup.
        /// </summary>
        void SetClientFilter(string customerName);
    }

    /// <summary>
    /// Handles the inquiry-to-proforma-invoice integration flow.
    /// Extracted from ProformaInvoiceViewModel to improve testability
    /// and separation of concerns.
    /// </summary>
    public sealed class InquiryIntegrationService
    {
        /// <summary>
        /// Validates that an inquiry is eligible for PI creation.
        /// Throws InvalidOperationException if validation fails.
        /// </summary>
        public void ValidateInquiryEligibility(InquiryRecord inquiry)
        {
            if (inquiry == null)
                throw new ArgumentNullException(nameof(inquiry));

            // Allow PI creation from Prepared or Confirmed inquiries.
            // The inquiry status is automatically advanced to "Sent"
            // when the PI is created (via InquiryLinkSync), and to
            // "Confirmed" when the PI is approved.
            if (!string.Equals(inquiry.Status, InquiryStatuses.Confirmed, StringComparison.OrdinalIgnoreCase)
                && !string.Equals(inquiry.Status, InquiryStatuses.Prepared, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    $"Only a Prepared or Confirmed Inquiry is eligible for Proforma creation. " +
                    $"Current status: {inquiry.Status}");
            }

            // Bug #7 protection — refuses to start a new PI when
            // the inquiry already has a linked PI. The Inquiry list
            // already guards this in the UI, but the service must guard
            // too because other entry points (Agent, tests, dashboard
            // quick-actions) reach here without going through the list.
            if (inquiry.ProformaInvoiceId.HasValue ||
                !string.IsNullOrWhiteSpace(inquiry.ProformaNumber))
            {
                throw new InvalidOperationException(
                    $"Inquiry {inquiry.InquiryNumber} already has a Proforma Invoice " +
                    $"({inquiry.ProformaNumber}). Delete the existing PI first or " +
                    $"open it via Load Invoice.");
            }

            // Industrial hard gate: complete BOQ rows must be safe to price.
            // An unknown glass build is never imported as a default SGU/DGU,
            // and negative/implausible dimensions or rates must be corrected
            // before the PI wizard can inherit them.
            var boqErrors = InquiryValidationRules.ValidateLineItems(
                (IReadOnlyList<InquiryLineItem>?)inquiry.LineItems ?? Array.Empty<InquiryLineItem>());
            if (boqErrors.Count > 0)
            {
                throw new InvalidOperationException(
                    "Inquiry BOQ is not safe for Proforma creation: " +
                    string.Join(" ", boqErrors.Take(5)));
            }
        }

        /// <summary>
        /// Pre-fills the wizard from an inquiry record. Called after
        /// the wizard has been reset to a blank state.
        /// </summary>
        public async Task PreFillFromInquiryAsync(
            InquiryRecord inquiry,
            IInquiryIntegrationCallbacks callbacks)
        {
            if (inquiry == null) throw new ArgumentNullException(nameof(inquiry));
            if (callbacks == null) throw new ArgumentNullException(nameof(callbacks));

            // Reset wizard to blank state first
            await callbacks.ResetWizardAsync();

            // Company / commercial context
            callbacks.SetInquiryId(inquiry.Id);

            // Client identity — Customer Name is the DB-required field
            // and also the trigger for the Step 3 project filter.
            // Setting CustomerName alone does NOT wake the SelectedClientName
            // property; the wizard's saved-clients list normally does that
            // when the operator clicks a card. We therefore also assign
            // BillingAddress from the inquiry so the operator can see a
            // starting point in Step 2 without re-typing.
            callbacks.SetCustomerInfo(
                inquiry.CustomerName,
                inquiry.CustomerLocation,
                inquiry.SalesmanName,
                inquiry.PreparedByName);

            callbacks.AdoptExternalCustomerSelection(inquiry.CustomerName);
            callbacks.SetClientFilter(inquiry.CustomerName);

            // Project reference. ProjectDescription is the fuller
            // free-text field on the inquiry; when empty we fall back
            // to InquiryNumber so the PI always carries something
            // linking back to the origin.
            callbacks.SetProjectInfo(
                inquiry.ProjectName,
                inquiry.ProjectDescription,
                inquiry.InquiryNumber);
        }
    }
}
