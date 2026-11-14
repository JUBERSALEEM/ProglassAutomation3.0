// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/TaxInvoice/TaxInvoiceValidationService.cs
// PROJECT: ProGlassAutomation
// MODULE: Tax Invoice — Validation Service (Phase 6b)
// VERSION: Phase 6b — 2026-08-21 — Extracted from TaxInvoiceDetailViewModel 2131 LOC

using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using ProGlassAutomation.Services.Common;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Workflow;
using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Services.TaxInvoice
{
    public sealed class TaxInvoiceValidationService
    {
        public static Result ValidateSave(SavedTaxInvoice? entity, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            if (entity == null)
                return Result.Fail("No tax invoice loaded", corrId);

            if (string.IsNullOrWhiteSpace(entity.InvoiceNumber))
                return Result.Fail("Invoice number is required", corrId);

            if (string.IsNullOrWhiteSpace(entity.ProformaId))
                return Result.Fail("Proforma reference is required", corrId);

            if (entity.Items == null || entity.Items.Count == 0)
                return Result.Fail("Invoice must have at least one line item", corrId);

            if (entity.Items.Any(i => i.Qty <= 0))
                return Result.Fail("All items must have qty > 0", corrId);

            if (entity.GrandTotal <= 0)
                return Result.Fail("Grand total must be > 0", corrId);

            return Result.Ok(corrId);
        }

        public static Result ValidateStatusTransition(string currentStatus, string newStatus, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.NewId();
            var transition = StatusTransitions.Validate(StatusTransitions.Kind.TaxInvoice, currentStatus, newStatus);

            if (!transition.IsValid)
            {
                AppLogService.Warning(
                    $"TI status transition invalid: {currentStatus} → {newStatus}: {transition.Reason}",
                    new LogContext { Module = "TaxInvoice", Operation = "ValidateStatus", CorrelationId = corrId });
                return Result.Fail(transition.Reason ?? $"Invalid status {currentStatus} → {newStatus}", corrId);
            }

            return Result.Ok(corrId);
        }

        public static Result ValidateDelete(SavedTaxInvoice? entity, bool hasDependents, string dependentMessage, string? correlationId = null)
        {
            var corrId = correlationId ?? CorrelationContext.CurrentId ?? CorrelationContext.NewId();

            if (entity == null)
                return Result.Fail("Invoice not found", corrId);

            if (hasDependents)
                return Result.Fail(dependentMessage, corrId);

            return Result.Ok(corrId);
        }
    }
}
