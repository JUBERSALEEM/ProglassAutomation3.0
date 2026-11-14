using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using ProGlassAutomation.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

using System.Globalization;
using ProGlassAutomation.Services.Reporting;
namespace ProGlassAutomation.Services.ProformaInvoice.Export
{
    /// <summary>
    /// IPdfLayout implementation for Proforma Invoice.
    ///
    /// 2026-09-11 — WYSIWYG REDESIGN
    /// ─────────────────────────────
    /// The PDF is now rendered by <see cref="PiPreviewParityRenderer"/>,
    /// a faithful port of the Step 7 on-screen paper. The old five
    /// PdfLayoutKind page architectures (rainbow spec headers, misaligned
    /// charge rows, subtotal bars that ignored the columns above them) are
    /// gone from this document: one professional design, styled by the five
    /// theme variants, identical on screen and in export.
    ///
    /// BUG-21 [CRITICAL]: metric math delegates to
    ///         ProformaChargeMetricsCalculator (no duplication).
    /// BUG-22: Uses SqmCalculator.CalculatePanel() (single source of truth).
    /// BUG-23: DELIVERY hardcoded to 1 TRIP — documented technical debt.
    /// BUG-24 [HIGH]: IsVatApplicable sourced from snapshot.Summary,
    ///         not derived from VatAmount > 0 (broke on FOC invoices).
    /// BUG-26: FooterDisclaimer taken from snapshot only.
    /// </summary>
    public class ProformaInvoicePdfLayout : IPdfLayout
    {
        private readonly SavedProformaInvoice _entity;
        private readonly ProformaInvoiceSnapshot _snapshot;
        private readonly PiDoc _doc;
        private readonly PiPreviewLetterhead _letterheadStyle = new();

        public ProformaInvoicePdfLayout(
            SavedProformaInvoice entity,
            ProformaInvoiceSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _doc = BuildDocument(entity, snapshot);
        }

        // ── IDocumentLayout ──────────────────────────────────────────────────

        public string DocumentPrefix => "PI";
        public string DocumentId => _entity.ProformaId;
        public string DocumentTitle => "PROFORMA INVOICE";
        public string CompanyName => _entity.CustomerName;
        public string Reference => ExportFileNamer.FirstFilled(
            _entity.ProjectName,
            _snapshot.Project?.ProjectIdentifierName,
            _snapshot.Project?.ProjectReference);
        public DateTime DocumentDate => _entity.IssueDate;

        // ── IPdfLayout ───────────────────────────────────────────────────────

        public PageLayout PageLayout => PageLayout.A4Portrait;

        /// <summary>
        /// The preview-parity letterhead: the exported page opens exactly
        /// like the Step 7 paper (masthead or company header + reference
        /// box), with the company logo placed by the same rule the preview
        /// uses. The five divergent PdfChrome letterheads are no longer
        /// applied to this document.
        /// </summary>
        public IPdfLetterheadStyle? LetterheadStyle => _letterheadStyle;

        public LetterheadInfo? Letterhead
        {
            get
            {
                var info = new LetterheadInfo
                {
                    CompanyName = _doc.CompanyName,
                    CompanyLocation = _doc.CompanyLocation,
                    CompanyTrn = _doc.CompanyTrn,
                    DocumentTitle = DocumentTitle,
                    DocumentId = _doc.ProformaId,
                    SubtitleLine = _doc.ProformaStatus,
                    IssueDateText = _doc.IssueDate,
                    LogoPath = _snapshot.Company?.LogoPath ?? "",
                    LogoBase64 = _snapshot.Company?.LogoBase64 ?? string.Empty,
                    LogoLayout = _snapshot.Company?.LogoLayout
                        ?? ProGlassAutomation.Models.MasterData.CompanyLogoLayout.None,
                    LogoWidthMm = _snapshot.Company?.LogoWidthMm ?? 22,
                    LogoHeightMm = _snapshot.Company?.LogoHeightMm ?? 26
                };
                return info;
            }
        }

        /// <summary>
        /// Body = the Step 7 paper, section by section. The exported PDF and
        /// the on-screen preview read the same PdfTheme/PdfMetrics tokens,
        /// so they cannot drift.
        /// </summary>
        public void RenderContent(ColumnDescriptor content)
        {
            PiPreviewParityRenderer.RenderBody(content, _doc);
        }

        // ════════════════════════════════════════════════════════════════════
        //  SNAPSHOT → DOCUMENT MODEL
        //  BUG-21: uses ProformaChargeMetricsCalculator (no more duplication)
        //  BUG-22: uses SqmCalculator for panel area
        //  BUG-24: IsVatApplicable from snapshot.Summary (not VatAmount > 0)
        //  BUG-26: FooterDisclaimer from snapshot only
        // ════════════════════════════════════════════════════════════════════

        private static PiDoc BuildDocument(
            SavedProformaInvoice entity,
            ProformaInvoiceSnapshot snapshot)
        {
            // BUG-24 [HIGH]: use snapshot.Summary.IsVatApplicable directly
            // Was: entity.VatAmount > 0 — broke on FOC invoices (VAT applicable
            // but amount = 0 for zero-value line items)
            bool isVatApplicable = snapshot.Summary?.IsVatApplicable ?? true;

            var doc = new PiDoc
            {
                CompanyName = snapshot.Company?.CompanyName ?? "",
                CompanyTrn = snapshot.Company?.CompanyTrn ?? "",
                CompanyLocation = snapshot.Company?.CompanyLocation ?? "",
                ProformaId = snapshot.Company?.ProformaId ?? entity.ProformaId,
                IssueDate = snapshot.Company?.IssueDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "",
                ValidUntil = snapshot.Company?.ValidUntil.ToString("dd MMM yyyy", CultureInfo.InvariantCulture) ?? "",
                ProformaStatus = snapshot.Company?.ProformaStatus ?? entity.Status,

                ProjectName = snapshot.Project?.ProjectIdentifierName
                    ?? entity.ProjectName,
                ProjectReference = snapshot.Project?.ProjectReference ?? "",
                ProjectContractNo = snapshot.Project?.ProjectContractNo ?? "",
                SiteDelivery = snapshot.Project?.SiteDeliveryLocation ?? "",
                LpoNumber = snapshot.Project?.LpoNumber ?? "",

                CustomerName = snapshot.Client?.CustomerName ?? entity.CustomerName,
                CustomerTrn = snapshot.Client?.CustomerTrn ?? "",
                AttentionName = snapshot.Client?.AttentionExecutiveName ?? "",
                ContactPhone = snapshot.Client?.ClientContactPhone ?? "",
                BillingAddress = snapshot.Client?.BillingAddress ?? "",
                CustomerReference = snapshot.Client?.CustomerReferenceCode ?? "",
                SalesRepresentative = snapshot.Client?.SalesRepresentative ?? "",

                GlassMaterialsTotal = entity.GlassMaterialsSubtotal,
                OtherChargesTotal = entity.OtherChargesSubtotal,
                NetSubtotal = entity.GlassMaterialsSubtotal
                                    + entity.OtherChargesSubtotal
                                    - entity.DiscountAmount,
                DiscountAmount = entity.DiscountAmount,
                IsVatApplicable = isVatApplicable,          // BUG-24 fix
                VatAmount = entity.VatAmount,
                GrandTotal = entity.GrandTotalVatInclusive,
                VatPercent = snapshot.Summary?.VatPercent ?? Reporting.VatCalculator.VAT_RATE_PERCENT,

                PreparedByName = snapshot.PreparedBy?.PreparedByName ?? "",
                PreparedByDept = snapshot.PreparedBy?.PreparedByDept ?? "",
                PreparedByStatus = snapshot.PreparedBy?.PreparedByStatus ?? "",
                TermsText = snapshot.PreparedBy?.TermsText ?? "",
                // BUG-26: sourced from snapshot only, no hardcoded fallback
                FooterDisclaimer = snapshot.PreparedBy?.FooterDisclaimer ?? ""
            };

            // ── Spec groups ─────────────────────────────────────────────────
            if (snapshot.SpecGroups != null)
            {
                foreach (var g in snapshot.SpecGroups)
                {
                    var group = new PiSpecGroupModel
                    {
                        GroupNumber = g.GroupNumber,
                        GroupName = !string.IsNullOrWhiteSpace(g.SelectedSpecName)
                                          ? g.SelectedSpecName
                                          : (!string.IsNullOrWhiteSpace(g.GroupName)
                                              ? g.GroupName
                                              : $"Spec Group {g.GroupNumber.ToString(CultureInfo.InvariantCulture)}")
                    };

                    if (g.ItemRows != null)
                    {
                        foreach (var r in g.ItemRows)
                        {
                            // BUG-22: shared SqmCalculator (0.5 m² floor)
                            decimal unitArea = SqmCalculator.CalculatePanel(r.W1, r.H1);
                            decimal totalArea = Math.Round(unitArea * r.Qty, 3);
                            decimal total = MoneyRounding.Round(totalArea * r.Rate);

                            group.Rows.Add(new PiGlassRowModel
                            {
                                Serial = r.SerialNumber,
                                GlassRef = r.GlassRef ?? "",
                                W1 = r.W1,
                                H1 = r.H1,
                                W2 = r.W2,
                                H2 = r.H2,
                                Qty = r.Qty,
                                UnitArea = unitArea,
                                TotalArea = totalArea,
                                Rate = r.Rate,
                                Total = total
                            });

                            group.TotalQty += r.Qty;
                            group.TotalArea += totalArea;
                            group.TotalValue += total;
                        }
                    }

                    doc.SpecGroups.Add(group);
                }
            }

            // ── Other charges — uses shared calculator ──────────────────────
            if (snapshot.OtherCharges != null)
            {
                var templatesByKey = PredefinedChargeTemplates.GetAll()
                    .ToDictionary(t => t.Key, t => t);

                // Predefined charges — prefer the live Step 6 amounts captured
                // at export time so the PDF matches the on-screen preview.
                // Legacy snapshots without MetricQty/ResultAmount still
                // recompute (previous behaviour).
                foreach (var c in snapshot.OtherCharges.PredefinedCharges
                    .Where(c => c.IsActive))
                {
                    templatesByKey.TryGetValue(c.TemplateKey ?? "", out var template);
                    string friendlyName = template?.ChargeName ?? c.TemplateKey ?? "";
                    string unitLabel = template?.Unit.ToString().ToUpper() ?? "QTY";

                    decimal metricQty;
                    decimal amount;

                    if (c.MetricQty.HasValue && c.ResultAmount.HasValue)
                    {
                        metricQty = c.MetricQty.Value;
                        amount = c.ResultAmount.Value;
                        if (c.TemplateKey == "DELIVERY")
                            unitLabel = "TRIP";
                        else if (c.TemplateKey == "POLISH_MATCHED_LAM")
                            friendlyName = "Polish — same-glass laminate (2×)";
                        else if (c.TemplateKey == "POLISH"
                              && !string.IsNullOrWhiteSpace(c.CustomPolishDescription)
                              && c.PolishSubType == "Custom")
                            friendlyName = $"Polish — {c.CustomPolishDescription}";
                        else if (c.TemplateKey == "POLISH"
                              && !string.IsNullOrWhiteSpace(c.PolishSubType))
                            friendlyName = $"Polish — {c.PolishSubType}";
                    }
                    else if (c.TemplateKey == "DELIVERY")
                    {
                        // BUG-23: DELIVERY quick-hack preserved — documented tech debt.
                        metricQty = 1m;
                        unitLabel = "TRIP";
                        amount = MoneyRounding.Round(metricQty * c.Rate);
                    }
                    else if (c.TemplateKey == "POLISH"
                          || c.TemplateKey == "POLISH_MATCHED_LAM")
                    {
                        if (c.PolishSubType == "Custom")
                        {
                            metricQty = c.CustomPolishQty;
                            if (!string.IsNullOrWhiteSpace(c.CustomPolishDescription))
                                friendlyName = $"Polish — {c.CustomPolishDescription}";
                        }
                        else
                        {
                            var polishGroups = ProformaChargeMetricsCalculator.FilterSpecs(
                                snapshot.SpecGroups, c.CheckedSpecGroupNumbers);
                            metricQty = ProformaChargeMetricsCalculator
                                .ComputePolishLm(
                                    polishGroups,
                                    c.PolishSubType,
                                    matchedSameGlassOnly: c.TemplateKey == "POLISH_MATCHED_LAM");
                            if (c.TemplateKey == "POLISH_MATCHED_LAM")
                                friendlyName = "Polish — same-glass laminate (2×)";
                            else if (!string.IsNullOrWhiteSpace(c.PolishSubType))
                                friendlyName = $"Polish — {c.PolishSubType}";
                        }
                        amount = MoneyRounding.Round(metricQty * c.Rate);
                    }
                    else
                    {
                        string sourceKey = ProformaChargeMetricsCalculator.FirstChargeSourceKey(
                            c.SelectedMetricKey,
                            c.SelectedSourceKey,
                            template?.DefaultSourceKey);
                        metricQty = ProformaChargeMetricsCalculator
                            .ComputeTargetedMetric(
                                snapshot.SpecGroups,
                                sourceKey,
                                c.CheckedSpecGroupNumbers);
                        amount = MoneyRounding.Round(metricQty * c.Rate);
                    }

                    doc.OtherCharges.Add(new PiChargeModel
                    {
                        Name = friendlyName,
                        QtyLabel = metricQty > 0
                                        ? $"{metricQty.ToString("0.###", CultureInfo.InvariantCulture)} {unitLabel}"
                                        : "—",
                        RateLabel = c.Rate.ToString("N2", CultureInfo.InvariantCulture),
                        Amount = amount
                    });
                }

                // Wastages — prefer the live Step 6 result (covers the
                // optimization formula). Legacy snapshots recompute.
                foreach (var w in snapshot.OtherCharges.Wastages
                    .Where(w => w.IsActive))
                {
                    decimal wastageQty;
                    decimal wastageAmount;

                    if (w.TotalSqm.HasValue && w.ResultAmount.HasValue)
                    {
                        wastageQty = w.TotalSqm.Value;
                        wastageAmount = w.ResultAmount.Value;
                    }
                    else
                    {
                        wastageQty = ProformaChargeMetricsCalculator
                            .ComputeWastageQty(snapshot.SpecGroups, w.CheckedSourceKeys);
                        wastageAmount = MoneyRounding.Round(wastageQty * w.Rate);
                    }

                    doc.OtherCharges.Add(new PiChargeModel
                    {
                        Name = $"Wastage — {w.ColorName}",
                        QtyLabel = wastageQty > 0
                                        ? $"{wastageQty.ToString("0.###", CultureInfo.InvariantCulture)} SQM"
                                        : "—",
                        RateLabel = w.Rate.ToString("N2", CultureInfo.InvariantCulture),
                        Amount = wastageAmount
                    });
                }

                // Custom charges
                foreach (var c in snapshot.OtherCharges.CustomCharges
                    .Where(c => c.IsActive))
                {
                    decimal amount = MoneyRounding.Round(c.Qty * c.Rate);
                    string unitStr = string.IsNullOrWhiteSpace(c.Unit)
                                          ? "QTY" : c.Unit.ToUpper();

                    doc.OtherCharges.Add(new PiChargeModel
                    {
                        Name = c.ChargeName ?? "",
                        QtyLabel = c.Qty > 0
                                        ? $"{c.Qty.ToString("0.###", CultureInfo.InvariantCulture)} {unitStr}"
                                        : "—",
                        RateLabel = c.Rate.ToString("N2", CultureInfo.InvariantCulture),
                        Amount = amount
                    });
                }
            }

            // ── Terms & conditions ──────────────────────────────────────────
            if (snapshot.TermClauses != null)
            {
                foreach (var c in snapshot.TermClauses
                    .Where(c => c.IsActive)
                    .OrderBy(c => c.Order))
                {
                    var clause = new PiClauseModel
                    {
                        Order = c.Order,
                        Title = c.Title ?? "",
                        Body = c.Body ?? ""
                    };

                    if (c.SubLines != null)
                        foreach (var l in c.SubLines.Where(l =>
                            !string.IsNullOrWhiteSpace(l)))
                            clause.SubLines.Add(l);

                    doc.TermClauses.Add(clause);
                }
            }

            return doc;
        }
    }
}
