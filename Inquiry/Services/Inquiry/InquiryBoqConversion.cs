// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryBoqConversion.cs
// MODULE: Inquiry — the pure half of inquiry→PI scope import.
//
// Ported from the legacy build (namespace moved into Services.Inquiry
// with it). The DECISION half of the BOQ→grid mapping — which rows
// import, in which order, and with which normalised values — is pure
// data work. The Proforma wizard will keep only the materialisation
// (SpecGroup/GlassItemRow creation) so there is still exactly one piece
// of grid-touching code.
//
// Conversion contract (pinned by the legacy tests):
//   • a row imports only when IsImportable (spec name + W×H + qty);
//     partial rows deliberately stay on the inquiry as notes;
//   • stable order = SortOrder — the PI grid mirrors the BOQ grid;
//   • glass type normalises onto the PI vocabulary (SGU/DGU/LAM/
//     DGU+LAM) with the compound match checked FIRST so "DGU+LAM" is
//     never swallowed by a plain "DGU" rule;
//   • a non-positive rate means "customer didn't quote one" — the grid
//     group keeps its own pricing, the rate is not forced to zero.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Inquiry;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.Inquiry
{
    /// <summary>One fully-validated import decision for a BOQ line.</summary>
    public sealed class BoqImportRow
    {
        /// <summary>Normalised PI glass-type code; "" when unrecognised.</summary>
        public string GlassType { get; set; } = "";
        public string SpecName { get; set; } = "";
        public string OuterGlassColor { get; set; } = "";
        public string InnerGlassColor { get; set; } = "";
        public int WidthMm { get; set; }
        public int HeightMm { get; set; }
        public int Qty { get; set; }

        /// <summary>Expected rate from the BOQ row (>0 only when quoted).</summary>
        public decimal RatePerSqm { get; set; }

        /// <summary>False → the grid keeps its own pricing for this group.</summary>
        public bool HasRate { get; set; }
    }

    /// <summary>Pure inquiry-BOQ → PI-grid conversion decisions.</summary>
    public static class InquiryBoqConversion
    {
        /// <summary>
        /// Normalise an inquiry glass-type label onto the PI vocabulary.
        /// DGU+LAM is matched EXACTLY and FIRST: any starts-with check on
        /// "DGU" would otherwise swallow the compound build — the most
        /// expensive spec type must never be silently downgraded.
        /// </summary>
        public static string NormalizeGlassType(string? type)
        {
            var t = (type ?? "").Trim();
            if (t.Equals("DGU+LAM", StringComparison.OrdinalIgnoreCase)) return "DGU+LAM";
            if (t.Equals("SGU", StringComparison.OrdinalIgnoreCase)) return "SGU";
            if (t.Equals("DGU", StringComparison.OrdinalIgnoreCase)) return "DGU";
            if (t.Equals("LAM", StringComparison.OrdinalIgnoreCase)) return "LAM";
            return "";
        }

        /// <summary>
        /// Convert BOQ lines into import decisions. Only
        /// <see cref="InquiryLineItem.IsImportable"/> rows survive,
        /// ordered by SortOrder so the PI grid mirrors the BOQ grid.
        /// </summary>
        public static IReadOnlyList<BoqImportRow> ToImportRows(
            IReadOnlyList<InquiryLineItem>? items)
        {
            if (items == null || items.Count == 0)
                return Array.Empty<BoqImportRow>();

            return items
                .Where(x => x.IsImportable)
                .OrderBy(x => x.SortOrder)
                .Select(x => new { Source = x, GlassType = NormalizeGlassType(x.GlassType) })
                // Fail safe: a complete BOQ row with an unknown build must
                // not inherit whatever default type the grid group
                // currently has. The eligibility validator surfaces this
                // to the operator; the pure converter also refuses the
                // unsafe row for direct callers.
                .Where(x => !string.IsNullOrWhiteSpace(x.GlassType))
                .Select(x => new BoqImportRow
                {
                    GlassType = x.GlassType,
                    SpecName = (x.Source.SpecName ?? "").Trim(),
                    OuterGlassColor = (x.Source.OuterGlassColor ?? "").Trim(),
                    InnerGlassColor = (x.Source.InnerGlassColor ?? "").Trim(),
                    WidthMm = x.Source.WidthMm,
                    HeightMm = x.Source.HeightMm,
                    Qty = x.Source.Qty,
                    RatePerSqm = x.Source.RatePerSqm,
                    HasRate = x.Source.RatePerSqm > 0m
                })
                .ToList();
        }
    }
}
