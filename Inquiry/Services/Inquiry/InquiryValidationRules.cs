// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryValidationRules.cs
// MODULE: Inquiry — industrial intake hardening.
//
// PURPOSE
//   Inquiry is the first commercial record. If impossible dimensions,
//   negative expected rates, or an unknown glass build pass this
//   boundary, the downstream Proforma Invoice can inherit a wrong
//   customer quote. These pure rules are intentionally shared by the
//   editor, repository, and inquiry-to-PI conversion guard so UI
//   bypasses cannot weaken them.
//
// Ported unchanged from the legacy build — do NOT weaken the limits.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Inquiry
{
    /// <summary>Pure validation rules for inquiry header + BOQ rows.</summary>
    public static class InquiryValidationRules
    {
        // Wide industrial limits: block typos, not legitimate work.
        public const int MaxPanelDimensionMm = 20000;
        public const int MaxQuantityPerLine = 100000;
        public const decimal MaxRatePerSqm = 100000m;
        public const decimal MaxInquiryValue = 999999999m;

        /// <summary>
        /// Returns blocking errors for an inquiry before it is saved or
        /// converted. Empty result means the record is safe to persist.
        /// Partial BOQ rows are allowed as intake notes, but any row that
        /// is complete enough to become PI pricing must name an exact PI
        /// build.
        /// </summary>
        public static IReadOnlyList<string> ValidateForSave(
            InquiryRecord? record,
            IReadOnlyList<InquiryLineItem>? lineItems = null)
        {
            var errors = new List<string>();
            if (record == null)
                return new[] { "Inquiry record is missing." };

            if (string.IsNullOrWhiteSpace(record.CustomerName))
                errors.Add("Customer is required before saving the inquiry.");
            if (string.IsNullOrWhiteSpace(record.ProjectName))
                errors.Add("Project is required before saving the inquiry.");
            if (record.InquiryValue < 0m)
                errors.Add("Inquiry value cannot be negative.");
            if (record.InquiryValue > MaxInquiryValue)
                errors.Add($"Inquiry value exceeds AED {MaxInquiryValue:N0}; verify the amount.");

            IReadOnlyList<InquiryLineItem> rows = lineItems
                ?? (IReadOnlyList<InquiryLineItem>?)record.LineItems
                ?? Array.Empty<InquiryLineItem>();
            errors.AddRange(ValidateLineItems(rows));
            return errors;
        }

        /// <summary>
        /// Validates the scope-of-supply rows independently. Used by the
        /// replace-all persistence method and by inquiry-to-PI
        /// eligibility.
        /// </summary>
        public static IReadOnlyList<string> ValidateLineItems(
            IReadOnlyList<InquiryLineItem>? rows)
        {
            if (rows == null || rows.Count == 0)
                return Array.Empty<string>();

            var errors = new List<string>();
            for (int i = 0; i < rows.Count; i++)
            {
                var row = rows[i];
                if (row == null) continue;
                int lineNo = i + 1;
                bool hasAnyCommercialValue =
                    !string.IsNullOrWhiteSpace(row.SpecName)
                    || !string.IsNullOrWhiteSpace(row.GlassType)
                    || !string.IsNullOrWhiteSpace(row.OuterGlassColor)
                    || !string.IsNullOrWhiteSpace(row.InnerGlassColor)
                    || row.WidthMm != 0 || row.HeightMm != 0
                    || row.Qty != 0 || row.RatePerSqm != 0m;

                if (!hasAnyCommercialValue) continue;

                if (row.WidthMm < 0 || row.HeightMm < 0 || row.Qty < 0)
                    errors.Add($"BOQ line {lineNo}: width, height and quantity cannot be negative.");
                if (row.WidthMm > MaxPanelDimensionMm || row.HeightMm > MaxPanelDimensionMm)
                    errors.Add($"BOQ line {lineNo}: panel dimension exceeds {MaxPanelDimensionMm:N0} mm; verify units.");
                if (row.Qty > MaxQuantityPerLine)
                    errors.Add($"BOQ line {lineNo}: quantity exceeds {MaxQuantityPerLine:N0}; split or verify the row.");
                if (row.RatePerSqm < 0m)
                    errors.Add($"BOQ line {lineNo}: expected rate cannot be negative.");
                if (row.RatePerSqm > MaxRatePerSqm)
                    errors.Add($"BOQ line {lineNo}: expected rate exceeds AED {MaxRatePerSqm:N0}/sqm; verify the rate.");

                bool completeForPi = !string.IsNullOrWhiteSpace(row.SpecName)
                    && row.WidthMm > 0 && row.HeightMm > 0 && row.Qty > 0;
                if (completeForPi &&
                    string.IsNullOrWhiteSpace(InquiryBoqConversion.NormalizeGlassType(row.GlassType)))
                {
                    errors.Add($"BOQ line {lineNo}: choose SGU, DGU, LAM or DGU+LAM before converting to PI. Unknown glass builds are never guessed.");
                }
            }
            return errors;
        }
    }
}
