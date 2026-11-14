// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.14.ProformaInvoiceRules.cs
// MODULE: step6 — what the document accepts and what each stage still needs.
//
// One home for the rules, called by the workspace, the repository (so a caller
// cannot bypass them) and the verification executable. Errors block a save;
// warnings are advisory and the operator may proceed. Validation NEVER throws:
// a document that is wrong is a result, not an exception.
//
// A warning is not proof the document is fine; it is a statement that a person
// should look. That distinction is what keeps a legitimate loss-making quote
// possible while making it impossible to issue one by accident.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Errors block; warnings advise.</summary>
    public sealed class InvoiceValidationResult
    {
        public IReadOnlyList<string> Errors { get; }
        public IReadOnlyList<string> Warnings { get; }
        public bool IsValid => Errors.Count == 0;

        public InvoiceValidationResult(
            IReadOnlyList<string>? errors = null, IReadOnlyList<string>? warnings = null)
        {
            Errors = errors ?? Array.Empty<string>();
            Warnings = warnings ?? Array.Empty<string>();
        }

        public string Summary => Errors.Count == 0
            ? "The document is ready to save."
            : Errors.Count + " problem(s) must be fixed: " + string.Join(" ", Errors);
    }

    /// <summary>Whether one stage has everything the next stage needs.</summary>
    public sealed class StageStatus
    {
        public InvoiceStage Stage { get; init; }
        public bool IsComplete { get; init; }
        public IReadOnlyList<string> Problems { get; init; } = Array.Empty<string>();
    }

    /// <summary>Normalisation, validation and stage completeness.</summary>
    public static class ProformaInvoiceRules
    {
        public const int ClientNameMaxLength = 200;
        public const int SpecNameMaxLength = 300;
        public const int ColourMaxLength = 120;
        public const int GroupNameMaxLength = 200;
        public const int ChargeNameMaxLength = 160;
        public const int NotesMaxLength = 2000;
        public const int TermsMaxLength = 4000;
        public const int MaxValidityDays = 365;
        public const int MaxRowsPerGroup = 500;
        public const int MaxGroups = 200;
        public const int MaxSections = 50;

        /// <summary>Below this margin the quotation is worth a second look.</summary>
        public const decimal WarningMarginPercent = 5m;

        /// <summary>Above this discount the document is worth a second look.</summary>
        public const decimal WarningDiscountPercent = 50m;

        private static readonly Regex NumberPattern =
            new(@"^PI-\d{4}-\d{3,}$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

        /// <summary>Trims and collapses internal whitespace runs to one space.</summary>
        public static string NormalizeText(string? value, int maxLength = 0)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            var builder = new StringBuilder(value.Length);
            bool space = false;
            foreach (char character in value.Trim())
            {
                if (char.IsWhiteSpace(character))
                {
                    space = true;
                    continue;
                }
                if (space) builder.Append(' ');
                space = false;
                builder.Append(character);
            }
            string text = builder.ToString();
            if (maxLength > 0 && text.Length > maxLength) text = text.Substring(0, maxLength);
            return text;
        }

        /// <summary>True when a value looks like an allocated document number.</summary>
        public static bool IsAllocatedNumber(string? value) =>
            !string.IsNullOrWhiteSpace(value) && NumberPattern.IsMatch(value.Trim());

        /// <summary>Normalizes the draft in place (text fields only).</summary>
        public static void Normalize(ProformaInvoiceDraft draft)
        {
            if (draft == null) return;
            draft.Notes = NormalizeText(draft.Notes, NotesMaxLength);
            draft.TermsText = NormalizeText(draft.TermsText, TermsMaxLength);
            draft.Parties.Client.ClientName =
                NormalizeText(draft.Parties.Client.ClientName, ClientNameMaxLength);
            draft.Parties.Client.AttentionName =
                NormalizeText(draft.Parties.Client.AttentionName, ClientNameMaxLength);

            foreach (SpecGroup group in draft.Groups)
            {
                group.SpecName = NormalizeText(group.SpecName, SpecNameMaxLength);
                group.GroupName = NormalizeText(group.GroupName, GroupNameMaxLength);
                group.OuterGlassColor = NormalizeText(group.OuterGlassColor, ColourMaxLength);
                group.InnerGlassColor = NormalizeText(group.InnerGlassColor, ColourMaxLength);
                group.ThirdGlassColor = NormalizeText(group.ThirdGlassColor, ColourMaxLength);
                group.GlassType = GlassBuilds.Normalize(group.GlassType);
            }

            foreach (CustomChargeRow row in draft.Charges.Custom)
                row.ChargeName = NormalizeText(row.ChargeName, ChargeNameMaxLength);
        }

        /// <summary>The full document check: errors block the save.</summary>
        public static InvoiceValidationResult Validate(ProformaInvoiceDraft draft)
        {
            var errors = new List<string>();
            var warnings = new List<string>();
            if (draft == null)
                return new InvoiceValidationResult(new[] { "No document was supplied." });

            // ── Stage 1: header ─────────────────────────────────────────
            if (draft.Parties.Company.IsEmpty)
                errors.Add("The company identity is missing — fill in the Company Profile first.");
            if (!draft.Parties.Client.HasCustomer)
                errors.Add("The customer name is required.");
            if (draft.Parties.Client.ClientName.Length > ClientNameMaxLength)
                errors.Add("The customer name is longer than " + ClientNameMaxLength + " characters.");
            if (!string.IsNullOrWhiteSpace(draft.Parties.Client.Trn) &&
                !draft.Parties.Client.Trn.All(char.IsDigit))
                errors.Add("The customer TRN must contain digits only (separators are stripped by the client record).");

            if (draft.IssueDate == default)
                errors.Add("The issue date is required.");
            if (draft.ValidUntil == default)
                errors.Add("The valid-until date is required.");
            if (draft.IssueDate != default && draft.ValidUntil != default)
            {
                if (draft.ValidUntil < draft.IssueDate)
                    errors.Add("The valid-until date cannot be before the issue date.");
                else if ((draft.ValidUntil - draft.IssueDate).Days > MaxValidityDays)
                    warnings.Add("The quotation is valid for more than a year — check the date.");
            }

            // ── Status and number ───────────────────────────────────────
            if (!ProformaInvoiceStatus.IsDefined(draft.Status))
                errors.Add("The status \"" + draft.Status + "\" is not a known invoice status.");
            if (!string.IsNullOrWhiteSpace(draft.ProformaNumber) &&
                !IsAllocatedNumber(draft.ProformaNumber))
                errors.Add("The document number must read PI-YYYY-NNN.");

            // ── Stage 3: grid ───────────────────────────────────────────
            if (draft.Groups.Count == 0)
                errors.Add("At least one specification is required.");
            if (draft.Groups.Count > MaxGroups)
                errors.Add("This document has more than " + MaxGroups + " specifications; split it.");
            if (draft.PricingSections.Count > MaxSections)
                errors.Add("This document has more than " + MaxSections + " pricing sections; split it.");

            foreach (SpecGroup group in draft.Groups)
            {
                string label = "Specification " + group.GroupNumber.ToString(CultureInfo.InvariantCulture);
                if (GlassBuilds.Normalize(group.GlassType).Length == 0)
                    errors.Add(label + ": the glass type is required (SGU, DGU, LAM or DGU+LAM).");
                if (group.SpecName.Length == 0)
                    errors.Add(label + ": the specification name is required.");
                if (group.BaseRatePerSqm < 0m)
                    errors.Add(label + ": the rate cannot be negative.");
                if (group.Rows.Count == 0)
                    errors.Add(label + ": at least one measured row is required.");
                if (group.Rows.Count > MaxRowsPerGroup)
                    errors.Add(label + ": more than " + MaxRowsPerGroup + " rows; split the specification.");
                if (group.Rows.Count > 0 && !group.Rows.Any(row => row.IsMeasurable))
                    errors.Add(label + ": no row has a width, a height and a quantity.");
                if (group.Rows.Any(row => row.W1 < 0 || row.H1 < 0 || row.W2 < 0 || row.H2 < 0))
                    errors.Add(label + ": a dimension cannot be negative.");
                if (group.CostCard != null && group.CostCard.CostBasisPerSqm <= 0m)
                    warnings.Add(label + ": no cost basis is recorded, so its profit cannot be measured.");
            }

            // ── Stage 4: charges ────────────────────────────────────────
            foreach (PredefinedChargeRow row in draft.Charges.Predefined.Where(row => row.IsActive))
                if (row.Rate < 0m)
                    errors.Add(row.ChargeName + ": the rate cannot be negative.");
            foreach (WastageChargeRow row in draft.Charges.Wastages.Where(row => row.IsActive))
            {
                if (row.Rate < 0m) errors.Add("Wastage (" + row.ColorName + "): the rate cannot be negative.");
                if (row.UseOptimizationFormula && row.OptimizationPercent < 0m)
                    errors.Add("Wastage (" + row.ColorName + "): the optimization percentage cannot be negative.");
            }
            foreach (CustomChargeRow row in draft.Charges.Custom.Where(row => row.IsActive &&
                         (row.Qty != 0m || row.Rate != 0m)))
            {
                if (row.ChargeName.Length == 0)
                    errors.Add("A custom charge needs a name.");
                if (row.Qty < 0m || row.Rate < 0m)
                    errors.Add("Custom charge \"" + row.ChargeName + "\": quantity and rate cannot be negative.");
            }

            // ── Stage 5: summary ────────────────────────────────────────
            if (draft.Summary.VatPercent < 0m || draft.Summary.VatPercent > 100m)
                errors.Add("The VAT percentage must be between 0 and 100.");
            if (draft.Summary.DiscountValue < 0m)
                errors.Add("The discount cannot be negative.");
            if (draft.Summary.GrandTotal < 0m)
                errors.Add("The grand total cannot be negative.");
            if (draft.Summary.EffectiveDiscountPercent > WarningDiscountPercent)
                warnings.Add("The discount is more than " + WarningDiscountPercent + "% of the net subtotal.");
            if (!draft.Summary.IsVatApplicable)
                warnings.Add("VAT is switched off for this document — confirm that is intended.");
            if (draft.Summary.NetSubtotal > 0m && draft.GridTotals.TotalValue <= 0m)
                warnings.Add("The glass subtotal is zero — the quotation carries charges only.");

            // ── Stage 7: margin ─────────────────────────────────────────
            if (draft.ProfitLoss?.MarginPercent is decimal margin && margin < WarningMarginPercent)
                warnings.Add("The margin is " + margin.ToString("0.#", CultureInfo.InvariantCulture) +
                    "% — below the " + WarningMarginPercent.ToString("0.#", CultureInfo.InvariantCulture) +
                    "% review level.");
            if (draft.GridTotals.TotalSqm > 10m && draft.Charges.Total == 0m)
                warnings.Add("There are no other charges on a large order — check this is intentional.");

            return new InvoiceValidationResult(errors, warnings);
        }

        /// <summary>
        /// What each stage still needs. Drives the wizard's stage markers without
        /// duplicating a single rule: the problems come from the same checks.
        /// </summary>
        public static IReadOnlyList<StageStatus> EvaluateStages(ProformaInvoiceDraft draft)
        {
            var statuses = new List<StageStatus>();
            if (draft == null) return statuses;

            var headerProblems = new List<string>();
            if (draft.Parties.Company.IsEmpty) headerProblems.Add("Company identity is missing.");
            if (!draft.Parties.Client.HasCustomer) headerProblems.Add("Customer name is required.");
            if (draft.IssueDate == default || draft.ValidUntil == default) headerProblems.Add("Issue and validity dates are required.");
            else if (draft.ValidUntil < draft.IssueDate) headerProblems.Add("Valid-until is before the issue date.");

            statuses.Add(new StageStatus
            {
                Stage = InvoiceStage.Header,
                IsComplete = headerProblems.Count == 0,
                Problems = headerProblems
            });

            var pricingProblems = new List<string>();
            if (draft.PricingSections.Count == 0) pricingProblems.Add("No pricing section exists.");
            foreach (PricingSection section in draft.PricingSections)
            {
                PricingResult result = PricingEstimator.Calculate(section);
                if (!result.IsComplete)
                    pricingProblems.Add("Section " + section.SectionNumber + ": " +
                        string.Join(" ", result.Warnings));
            }
            statuses.Add(new StageStatus
            {
                Stage = InvoiceStage.Pricing,
                IsComplete = pricingProblems.Count == 0,
                Problems = pricingProblems
            });

            var gridProblems = new List<string>();
            if (draft.Groups.Count == 0) gridProblems.Add("No specification has been added.");
            foreach (SpecGroup group in draft.Groups.Where(group => !group.IsPriced))
                gridProblems.Add("Specification " + group.GroupNumber + " is not priced.");
            statuses.Add(new StageStatus
            {
                Stage = InvoiceStage.SpecGrid,
                IsComplete = gridProblems.Count == 0,
                Problems = gridProblems
            });

            var chargeProblems = new List<string>();
            if (draft.Charges.Wastages.Count > 0 &&
                draft.Charges.Wastages.Any(row => row.Rate < 0m))
                chargeProblems.Add("A wastage rate is negative.");
            statuses.Add(new StageStatus
            {
                Stage = InvoiceStage.OtherCharges,
                IsComplete = chargeProblems.Count == 0,
                Problems = chargeProblems
            });

            var summaryProblems = new List<string>();
            if (draft.Summary.GrandTotal < 0m) summaryProblems.Add("The grand total is negative.");
            if (draft.Summary.GrandTotal <= 0m) summaryProblems.Add("The document total is zero.");
            statuses.Add(new StageStatus
            {
                Stage = InvoiceStage.Summary,
                IsComplete = summaryProblems.Count == 0,
                Problems = summaryProblems
            });

            statuses.Add(new StageStatus
            {
                Stage = InvoiceStage.Attachments,
                IsComplete = true,
                Problems = Array.Empty<string>()
            });

            var profitProblems = new List<string>();
            if (draft.ProfitLoss == null || draft.ProfitLoss.Rows.Count == 0)
                profitProblems.Add("Nothing is costed yet.");
            statuses.Add(new StageStatus
            {
                Stage = InvoiceStage.ProfitLoss,
                IsComplete = profitProblems.Count == 0,
                Problems = profitProblems
            });

            return statuses;
        }
    }
}
