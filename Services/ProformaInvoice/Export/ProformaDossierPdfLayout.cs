// ═══════════════════════════════════════════════════════════════════════
//  FILE: Services/ProformaInvoice/Export/ProformaDossierPdfLayout.cs
//  PROJECT: ProGlassAutomation
//  CREATED: 2026-09-02 — advanced reporting, slice 4
//
//  PRINT/EMAIL FORM OF THE FULL DOSSIER
//  ---------------------------------------------------------------------
//  The Excel dossier (slice 3) is for working with; this is for sending
//  and filing. Same sections, same opt-in P&L rule, laid out to the
//  four-layer industrial structure: header → KPI band → analysis →
//  detail.
//
//  ⚠ P&L IS OPT-IN, NEVER THE DEFAULT
//  IncludeProfitLoss defaults to FALSE, and the internal variant prints a
//  red banner on every page it appears in. A PDF is the format most
//  likely to be emailed, so this is the document where an accidental
//  margin leak matters most.
//
//  A4 PORTRAIT: this is a document, not a data dump. The cut list is the
//  only wide table and it fits at caption size; landscape would make the
//  narrative sections read badly.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ProGlassAutomation.Services.ProformaInvoice.Export
{
    public class ProformaDossierPdfLayout : IPdfLayout
    {
        private readonly SavedProformaInvoice _entity;
        private readonly ProformaInvoiceSnapshot _snap;
        private readonly bool _includeProfitLoss;

        public ProformaDossierPdfLayout(
            SavedProformaInvoice entity,
            ProformaInvoiceSnapshot snapshot,
            bool includeProfitLoss = false)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snap = snapshot ?? new ProformaInvoiceSnapshot();
            _includeProfitLoss = includeProfitLoss;
        }

        // ── IDocumentLayout ──────────────────────────────────────────────

        public string DocumentPrefix => "PI";

        public string DocumentId =>
            !string.IsNullOrWhiteSpace(_snap.Company?.ProformaId)
                ? _snap.Company!.ProformaId
                : _entity.ProformaId ?? "";

        public string DocumentTitle =>
            _includeProfitLoss
                ? "Proforma Invoice — Full Dossier (Internal)"
                : "Proforma Invoice — Dossier";

        public string CompanyName =>
            !string.IsNullOrWhiteSpace(_snap.Client?.CustomerName)
                ? _snap.Client!.CustomerName
                : _entity.CustomerName ?? "";

        public string Reference => _snap.Project?.ProjectIdentifierName ?? "";

        public DateTime DocumentDate =>
            _snap.Company != null && _snap.Company.IssueDate != default
                ? _snap.Company.IssueDate
                : _entity.CreatedAt;

        public PageLayout PageLayout => PageLayout.A4Portrait;

        public LetterheadInfo? Letterhead
        {
            get
            {
                var info = new LetterheadInfo
                {
                    CompanyName = _snap.Company?.CompanyName ?? "",
                    CompanyLocation = _snap.Company?.CompanyLocation ?? "",
                    CompanyTrn = _snap.Company?.CompanyTrn ?? "",
                    DocumentTitle = DocumentTitle,
                    DocumentId = DocumentId,
                    SubtitleLine = _includeProfitLoss
                        ? "INTERNAL — contains cost and margin. Not for customer release."
                        : null,
                    LogoPath = _snap.Company?.LogoPath ?? "",
                    LogoBase64 = _snap.Company?.LogoBase64 ?? string.Empty,
                    LogoLayout = _snap.Company?.LogoLayout
                        ?? ProGlassAutomation.Models.MasterData.CompanyLogoLayout.None,
                    LogoWidthMm = _snap.Company?.LogoWidthMm ?? 22,
                    LogoHeightMm = _snap.Company?.LogoHeightMm ?? 26
                };

                // A document without its criteria printed on it is
                // unauditable; these are the fields anyone filing or
                // querying the quote will look for first.
                info.Meta.Add(("Customer", CompanyName));
                info.Meta.Add(("Project", Reference));
                info.Meta.Add(("Issue date", Date(_snap.Company?.IssueDate)));
                info.Meta.Add(("Valid until", Date(_snap.Company?.ValidUntil)));
                info.Meta.Add(("Status", _snap.Company?.ProformaStatus ?? ""));

                return info;
            }
        }

        // ── Body ─────────────────────────────────────────────────────────

        public void RenderContent(ColumnDescriptor content)
        {
            if (_includeProfitLoss) RenderInternalBanner(content);

            RenderTotalsBand(content);

            content.Item().Element(c => SectionHeaderRenderer.Render(c, "CLIENT & PROJECT"));
            RenderClientProject(content);

            content.Item().Element(c => SectionHeaderRenderer.Render(c, "SPECIFICATIONS"));
            RenderSpecifications(content);

            content.Item().Element(c => SectionHeaderRenderer.Render(c, "CUT LIST"));
            RenderCutList(content);

            if (HasCharges)
            {
                content.Item().Element(c => SectionHeaderRenderer.Render(c, "OTHER CHARGES"));
                RenderCharges(content);
            }

            if ((_snap.TermClauses?.Count ?? 0) > 0)
            {
                content.Item().Element(c => SectionHeaderRenderer.Render(c, "TERMS & CONDITIONS"));
                RenderTerms(content);
            }

            if (_includeProfitLoss)
            {
                content.Item().Element(c => SectionHeaderRenderer.Render(c, "PROFIT & LOSS — INTERNAL"));
                RenderProfitLoss(content);
            }
        }

        private void RenderInternalBanner(ColumnDescriptor content)
        {
            content.Item()
                   .Background(DocumentStyle.ColorDanger)
                   .Padding(6)
                   .Text("INTERNAL COPY — CONTAINS COST AND MARGIN. DO NOT SEND TO CUSTOMER.")
                   .FontSize(DocumentStyle.FontSizeCaption)
                   .Bold()
                   .FontColor(DocumentStyle.ColorAccentOn);
        }

        /// <summary>
        /// KPI band — layer 2. Read in three seconds by someone who reads
        /// nothing else on the page.
        /// </summary>
        private void RenderTotalsBand(ColumnDescriptor content)
        {
            int specs = _snap.SpecGroups?.Count ?? 0;
            int pieces = _snap.SpecGroups?
                .SelectMany(g => g.ItemRows ?? new List<GlassItemRowSnapshot>())
                .Sum(i => i.Qty) ?? 0;

            content.Item().PaddingVertical(8)
                   .Background(DocumentStyle.ColorTableHeader)
                   .Padding(8)
                   .Row(row =>
                   {
                       void Cell(string label, string value)
                       {
                           row.RelativeItem().Column(col =>
                           {
                               col.Item().Text(label)
                                  .FontSize(DocumentStyle.FontSizeCaption)
                                  .FontColor(DocumentStyle.ColorMuted);
                               col.Item().Text(value)
                                  .FontSize(DocumentStyle.FontSizeH2).Bold()
                                  .FontColor(DocumentStyle.ColorText);
                           });
                       }

                       Cell("SPECIFICATIONS", specs.ToString("N0"));
                       Cell("PIECES", pieces.ToString("N0"));
                       Cell("NET", Money(_entity.GlassMaterialsSubtotal + _entity.OtherChargesSubtotal));
                       Cell("VAT", Money(_entity.VatAmount));
                       Cell("GRAND TOTAL", Money(_entity.GrandTotalVatInclusive));
                   });
        }

        private void RenderClientProject(ColumnDescriptor content)
        {
            content.Item().PaddingBottom(6).Row(row =>
            {
                row.RelativeItem().Column(col =>
                {
                    Pair(col, "Customer", _snap.Client?.CustomerName);
                    Pair(col, "TRN", _snap.Client?.CustomerTrn);
                    Pair(col, "Reference", _snap.Client?.CustomerReferenceCode);
                    Pair(col, "Salesman", _snap.Client?.SalesRepresentative);
                    Pair(col, "Attention", _snap.Client?.AttentionExecutiveName);
                    Pair(col, "Contact", _snap.Client?.ClientContactPhone);
                });

                row.RelativeItem().Column(col =>
                {
                    Pair(col, "Project", _snap.Project?.ProjectIdentifierName);
                    Pair(col, "Contract no", _snap.Project?.ProjectContractNo);
                    Pair(col, "Reference", _snap.Project?.ProjectReference);
                    Pair(col, "Site / delivery", _snap.Project?.SiteDeliveryLocation);
                    Pair(col, "LPO number", _snap.Project?.LpoNumber);
                });
            });
        }

        private void RenderSpecifications(ColumnDescriptor content)
        {
            var groups = _snap.SpecGroups ?? new List<SpecGroupSnapshot>();
            if (groups.Count == 0) { Empty(content, "No specifications."); return; }

            content.Item().PaddingBottom(6).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(28);
                    c.RelativeColumn(4);
                    c.ConstantColumn(52);
                    c.RelativeColumn(1.4f);
                    c.ConstantColumn(58);
                    c.ConstantColumn(42);
                });

                t.Header(h =>
                {
                    PdfChrome.HeaderCell(h.Cell(), "#");
                    PdfChrome.HeaderCell(h.Cell(), "SPECIFICATION", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "TYPE");
                    PdfChrome.HeaderCell(h.Cell(), "COLOUR", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "RATE", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "PCS", right: true);
                });

                foreach (var g in groups)
                {
                    Cell(t, g.GroupNumber.ToString());
                    Cell(t, Pick(g.SelectedSpecName, g.GroupName));
                    Cell(t, g.SelectedGlassType ?? "");
                    Cell(t, g.OuterGlassColor ?? "");
                    Cell(t, Money(g.BaseRate), right: true);
                    Cell(t, (g.ItemRows ?? new List<GlassItemRowSnapshot>())
                              .Sum(i => i.Qty).ToString("N0"), right: true);
                }
            });
        }

        private void RenderCutList(ColumnDescriptor content)
        {
            var groups = _snap.SpecGroups ?? new List<SpecGroupSnapshot>();
            bool any = groups.Any(g => (g.ItemRows?.Count ?? 0) > 0);
            if (!any) { Empty(content, "No glass rows."); return; }

            content.Item().PaddingBottom(6).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(34);
                    c.RelativeColumn(3);
                    c.ConstantColumn(26);
                    c.RelativeColumn(1.6f);
                    c.ConstantColumn(38);
                    c.ConstantColumn(38);
                    c.ConstantColumn(34);
                    c.ConstantColumn(52);
                });

                t.Header(h =>
                {
                    PdfChrome.HeaderCell(h.Cell(), "SPEC");
                    PdfChrome.HeaderCell(h.Cell(), "SPECIFICATION", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "SR");
                    PdfChrome.HeaderCell(h.Cell(), "GLASS REF", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "W", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "H", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "QTY", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "RATE", right: true);
                });

                foreach (var g in groups)
                {
                    string spec = Pick(g.SelectedSpecName, g.GroupName);

                    foreach (var i in g.ItemRows ?? new List<GlassItemRowSnapshot>())
                    {
                        Cell(t, g.GroupNumber.ToString());
                        Cell(t, spec);
                        Cell(t, i.SerialNumber.ToString());
                        Cell(t, i.GlassRef ?? "");
                        Cell(t, i.W1.ToString(), right: true);
                        Cell(t, i.H1.ToString(), right: true);
                        Cell(t, i.Qty.ToString("N0"), right: true);
                        Cell(t, Money(i.Rate), right: true);
                    }
                }
            });
        }

        private bool HasCharges
        {
            get
            {
                var oc = _snap.OtherCharges;
                if (oc == null) return false;
                return (oc.PredefinedCharges?.Count ?? 0) > 0
                    || (oc.Wastages?.Count ?? 0) > 0
                    || (oc.CustomCharges?.Count ?? 0) > 0;
            }
        }

        private void RenderCharges(ColumnDescriptor content)
        {
            var oc = _snap.OtherCharges ?? new OtherChargesSnapshot();

            content.Item().PaddingBottom(6).Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.RelativeColumn(1.2f);
                    c.RelativeColumn(3);
                    c.ConstantColumn(46);
                    c.ConstantColumn(56);
                    c.ConstantColumn(44);
                });

                t.Header(h =>
                {
                    PdfChrome.HeaderCell(h.Cell(), "GROUP", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "ITEM", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "QTY", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "RATE", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "ACTIVE");
                });

                foreach (var c in oc.PredefinedCharges ?? new List<PredefinedChargeSnapshot>())
                {
                    Cell(t, "Predefined");
                    Cell(t, Pick(c.TemplateKey, c.SelectedSourceKey));
                    Cell(t, "", right: true);
                    Cell(t, Money(c.Rate), right: true);
                    Cell(t, c.IsActive ? "Yes" : "No");
                }

                foreach (var w in oc.Wastages ?? new List<WastageRowSnapshot>())
                {
                    Cell(t, "Wastage");
                    Cell(t, w.ColorName ?? "");
                    Cell(t, "", right: true);
                    Cell(t, Money(w.Rate), right: true);
                    Cell(t, w.IsActive ? "Yes" : "No");
                }

                foreach (var c in oc.CustomCharges ?? new List<CustomChargeSnapshot>())
                {
                    Cell(t, "Custom");
                    Cell(t, $"{c.ChargeName} {c.Unit}".Trim());
                    Cell(t, c.Qty.ToString("N2"), right: true);
                    Cell(t, Money(c.Rate), right: true);
                    Cell(t, c.IsActive ? "Yes" : "No");
                }
            });
        }

        private void RenderTerms(ColumnDescriptor content)
        {
            int n = 1;
            foreach (var c in _snap.TermClauses ?? new List<TermClauseSnapshot>())
            {
                content.Item().PaddingBottom(4).Column(col =>
                {
                    col.Item().Text($"{n}. {c.Title}")
                       .FontSize(DocumentStyle.FontSizeCaption).Bold()
                       .FontColor(DocumentStyle.ColorText);

                    if (!string.IsNullOrWhiteSpace(c.Body))
                        col.Item().Text(c.Body)
                           .FontSize(DocumentStyle.FontSizeCaption)
                           .FontColor(DocumentStyle.ColorMuted);
                });
                n++;
            }
        }

        private void RenderProfitLoss(ColumnDescriptor content)
        {
            var pl = _snap.ProfitLoss ?? new ProfitLossSnapshot();

            decimal cost = pl.TotalGlassCost + pl.TotalProductionCost;
            decimal margin = pl.NetExVat - cost;
            decimal marginPct = pl.NetExVat == 0m
                ? 0m
                : Math.Round(margin / pl.NetExVat * 100m, 1);

            content.Item().PaddingVertical(6)
                   .Background(DocumentStyle.ColorTableHeader)
                   .Padding(8)
                   .Row(row =>
                   {
                       void Cell2(string label, string value)
                       {
                           row.RelativeItem().Column(col =>
                           {
                               col.Item().Text(label)
                                  .FontSize(DocumentStyle.FontSizeCaption)
                                  .FontColor(DocumentStyle.ColorMuted);
                               col.Item().Text(value)
                                  .FontSize(DocumentStyle.FontSizeH2).Bold()
                                  .FontColor(DocumentStyle.ColorText);
                           });
                       }

                       Cell2("NET (EX VAT)", Money(pl.NetExVat));
                       Cell2("GLASS COST", Money(pl.TotalGlassCost));
                       Cell2("PRODUCTION", Money(pl.TotalProductionCost));
                       Cell2("MARGIN", Money(margin));
                       Cell2("MARGIN %", $"{marginPct:0.#}%");
                   });

            var rows = pl.Rows ?? new List<ProfitLossRowSnapshot>();
            if (rows.Count == 0) return;

            content.Item().Table(t =>
            {
                t.ColumnsDefinition(c =>
                {
                    c.ConstantColumn(52);
                    c.RelativeColumn(3);
                    c.ConstantColumn(62);
                    c.ConstantColumn(44);
                    c.ConstantColumn(52);
                    c.ConstantColumn(58);
                });

                t.Header(h =>
                {
                    PdfChrome.HeaderCell(h.Cell(), "KIND", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "DESCRIPTION", center: false);
                    PdfChrome.HeaderCell(h.Cell(), "SHEET");
                    PdfChrome.HeaderCell(h.Cell(), "QTY", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "RATE", right: true);
                    PdfChrome.HeaderCell(h.Cell(), "AMOUNT", right: true);
                });

                foreach (var r in rows)
                {
                    Cell(t, r.Kind ?? "");
                    Cell(t, r.Description ?? "");
                    Cell(t, r.SheetWidthMm > 0 && r.SheetHeightMm > 0
                              ? $"{r.SheetWidthMm}×{r.SheetHeightMm}" : "—");
                    Cell(t, r.Quantity.ToString("N2"), right: true);
                    Cell(t, Money(r.PricePerSqm), right: true);
                    Cell(t, Money(r.Amount), right: true);
                }
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static void Cell(TableDescriptor t, string text, bool right = false)
        {
            var c = t.Cell().PaddingVertical(2).PaddingHorizontal(3);
            var txt = c.Text(text ?? "")
                       .FontSize(DocumentStyle.FontSizeCaption)
                       .FontColor(DocumentStyle.ColorText);
            if (right) txt.AlignRight();
        }

        private static void Pair(ColumnDescriptor col, string label, string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            col.Item().Row(r =>
            {
                r.ConstantItem(80).Text(label)
                 .FontSize(DocumentStyle.FontSizeCaption)
                 .FontColor(DocumentStyle.ColorMuted);
                r.RelativeItem().Text(value)
                 .FontSize(DocumentStyle.FontSizeCaption)
                 .FontColor(DocumentStyle.ColorText);
            });
        }

        private static void Empty(ColumnDescriptor content, string text)
        {
            content.Item().PaddingBottom(6).Text(text)
                   .FontSize(DocumentStyle.FontSizeCaption)
                   .FontColor(DocumentStyle.ColorMuted);
        }

        private static string Money(decimal v) => v.ToString("N2");

        private static string Date(DateTime? d) =>
            d.HasValue && d.Value != default ? d.Value.ToString("dd-MMM-yyyy") : "";

        private static string Pick(string? a, string? b) =>
            !string.IsNullOrWhiteSpace(a) ? a! : (b ?? "");
    }
}
