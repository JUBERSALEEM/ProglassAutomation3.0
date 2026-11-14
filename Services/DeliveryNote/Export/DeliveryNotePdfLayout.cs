using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using ProGlassAutomation.Utilities;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Linq;

using System.Globalization;
namespace ProGlassAutomation.Services.DeliveryNote.Export
{
    /// <summary>
    /// PDF layout for a Delivery Note.
    ///
    /// Renders:
    /// - Letterhead (company from JO ref snapshot · DN ID · "Delivery Note")
    /// - Delivery logistics block (Customer · Driver · Vehicle · Date · Time)
    /// - Items table grouped by SpecGroupName
    ///   Columns: # · Glass Ref · W1 · H1 · W2 · H2 · Ordered · Prev · This DN · Balance · SQM · Wt
    /// - Totals strip (Ordered · Prev Delivered · This DN · Balance · Sequence)
    /// - Signature block (Dispatched By · Received By · Date)
    ///
    /// BUG-60: DocumentTitle now uses DeliveryNoteExportConstants.DocumentTitle
    ///         for consistency across PDF / Excel / CSV layouts.
    ///
    /// Phase 10.8a: Added Prev Delivered to totals strip.
    /// No pricing — delivery document only.
    /// </summary>
    public class DeliveryNotePdfLayout : IPdfLayout
    {
        private readonly SavedDeliveryNote _entity;
        private readonly DeliveryNoteSnapshot _snapshot;
        private readonly DnJobOrderRefSnapshot _joRef;

        public DeliveryNotePdfLayout(
            SavedDeliveryNote entity,
            DeliveryNoteSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _joRef = snapshot.JobOrderRef ?? new DnJobOrderRefSnapshot();
        }

        // ── IDocumentLayout ──────────────────────────────────────────────
        public string DocumentPrefix => "DN";
        public string DocumentId => _entity.DeliveryNoteId;

        // BUG-60: shared constant — single source of truth
        public string DocumentTitle => DeliveryNoteExportConstants.DocumentTitle;

        public string CompanyName => ExportFileNamer.FirstFilled(
            _joRef.CustomerName, _entity.CustomerName);
        public string Reference => ExportFileNamer.FirstFilled(
            _joRef.ProjectName, _entity.ProjectName, _joRef.ProjectReference);
        public DateTime DocumentDate =>
            _entity.DeliveryDate != default
                ? _entity.DeliveryDate
                : _entity.CreatedAt;

        // ── IPdfLayout ───────────────────────────────────────────────────
        public PageLayout PageLayout => PageLayout.A4Portrait;

        public LetterheadInfo? Letterhead
        {
            get
            {
                var info = new LetterheadInfo
                {
                    CompanyName = _joRef.CompanyName,
                    CompanyLocation = _joRef.CompanyLocation,
                    CompanyTrn = _joRef.CompanyTrn,
                    DocumentTitle = DocumentTitle,
                    DocumentId = DocumentId,
                    SubtitleLine = BuildSubtitle(),
                    LogoPath = _joRef.LogoPath ?? "",
                    LogoBase64 = _joRef.LogoBase64 ?? "",
                    LogoLayout = _joRef.LogoLayout,
                    LogoWidthMm = _joRef.LogoWidthMm,
                    LogoHeightMm = _joRef.LogoHeightMm
                };
                info.Meta.Add(("Date", _entity.DeliveryDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)));
                info.Meta.Add(("Status", _entity.Status ?? ""));
                info.Meta.Add(("Job Order", _entity.JobOrderId ?? ""));
                info.Meta.Add(("This DN", _entity.TotalDeliveredQty.ToString()));
                return info;
            }
        }

        public void RenderContent(ColumnDescriptor content)
        {
            content.Item().Element(RenderIndustrialControlPanel);

            // ── Delivery logistics ────────────────────────────────────────
            content.Item().Element(c =>
                SectionHeaderRenderer.Render(c, "DELIVERY DETAILS"));
            content.Item().Element(RenderDeliveryDetails);

            // ── Items table ───────────────────────────────────────────────
            content.Item().Element(c =>
                SectionHeaderRenderer.Render(c, "GLASS ITEMS"));

            var groups = (_entity.Items ?? new())
                .GroupBy(i => new { i.SpecGroupNumber, i.SpecGroupName })
                .OrderBy(g => g.Key.SpecGroupNumber)
                .ToList();

            if (groups.Count == 0)
            {
                content.Item()
                    .Text("No items on this delivery note.")
                    .Italic()
                    .FontColor(DocumentStyle.ColorMuted);
            }
            else
            {
                foreach (var group in groups)
                    content.Item().Element(c =>
                        RenderSpecGroupTable(c,
                            group.Key.SpecGroupName,
                            group.ToList()));

                content.Item().Element(RenderTotalsStrip);
            }

            // ── Signature block ───────────────────────────────────────────
            content.Item().Element(c =>
                SectionHeaderRenderer.Render(c, "SIGN-OFF"));
            content.Item().Element(RenderSignatureBlock);
        }


        private void RenderIndustrialControlPanel(IContainer container)
        {
            decimal totalWeight = (_entity.Items ?? Enumerable.Empty<DeliveryNoteItem>()).Sum(i => i.TotalWeight);
            PdfChrome.IndustrialDocumentPanel(container,
                "Dispatch and receiving control",
                "Industrial ERP delivery note for warehouse release, driver handover, receiver confirmation, delivered balance and glass weight traceability.",
                ("Status", _entity.Status ?? "", DocumentStyle.ColorAccent),
                ("This DN", _entity.TotalDeliveredQty.ToString(CultureInfo.InvariantCulture), DocumentStyle.ColorSuccess),
                ("Balance", _entity.TotalBalanceQty.ToString(CultureInfo.InvariantCulture), _entity.TotalBalanceQty > 0 ? DocumentStyle.ColorWarning : DocumentStyle.ColorSuccess),
                ("Sequence", _entity.SequenceNumber > 0 ? _entity.SequenceNumber.ToString(CultureInfo.InvariantCulture) : "—", null),
                ("Weight", totalWeight > 0 ? $"{totalWeight:N2} kg" : "—", null));
        }

        // ═════════════════════════════════════════════════════════════════
        //  SECTION RENDERERS
        // ═════════════════════════════════════════════════════════════════

        private void RenderDeliveryDetails(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Element(c =>
                    PdfChrome.InfoCard(c, "Customer", col =>
                    {
                        col.Item().Text(_joRef.CustomerName)
                            .FontSize(DocumentStyle.FontSizeH2).Bold();
                        if (!string.IsNullOrWhiteSpace(_joRef.ProjectName))
                            PdfChrome.LabelValue(col, "Project Name", _joRef.ProjectName);
                        if (!string.IsNullOrWhiteSpace(_joRef.ProjectReference))
                            PdfChrome.LabelValue(col, "Project Reference", _joRef.ProjectReference);
                        if (!string.IsNullOrWhiteSpace(_joRef.CompanyTrn))
                            PdfChrome.LabelValue(col, "TRN", _joRef.CompanyTrn);
                        PdfChrome.LabelValue(col, "Job order", _entity.JobOrderId);
                        if (!string.IsNullOrWhiteSpace(_entity.ProformaId))
                            PdfChrome.LabelValue(col, "PI", _entity.ProformaId);
                    }));

                row.ConstantItem(10);

                row.RelativeItem().Element(c =>
                    PdfChrome.InfoCard(c, "Logistics", col =>
                    {
                        PdfChrome.LabelValue(col, "Date",
                            _entity.DeliveryDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));
                        PdfChrome.LabelValue(col, "Time", _entity.DeliveryTime);
                        PdfChrome.LabelValue(col, "Driver",
                            string.IsNullOrWhiteSpace(_entity.DriverName)
                                ? "—" : _entity.DriverName);
                        PdfChrome.LabelValue(col, "Vehicle",
                            string.IsNullOrWhiteSpace(_entity.VehicleNumber)
                                ? "—" : _entity.VehicleNumber);
                        PdfChrome.LabelValue(col, "Receiver",
                            string.IsNullOrWhiteSpace(_entity.ReceiverName)
                                ? "—" : _entity.ReceiverName);
                        PdfChrome.LabelValue(col, "Status", _entity.Status);
                        PdfChrome.LabelValue(col, "Type", _entity.DeliveryType);

                        if (_entity.IsFoc)
                        {
                            col.Item().PaddingTop(4)
                                .Background(DocumentStyle.ColorWarning)
                                .PaddingHorizontal(6).PaddingVertical(3)
                                .Text("FREE OF CHARGE (FOC)")
                                .FontSize(DocumentStyle.FontSizeSmall)
                                .Bold()
                                .FontColor(DocumentStyle.ColorWhite);
                        }

                        if (!string.IsNullOrWhiteSpace(_entity.Notes))
                            PdfChrome.LabelValue(col, "Notes", _entity.Notes);
                    }));
            });
        }

        private void RenderSpecGroupTable(
            IContainer container,
            string specGroupName,
            System.Collections.Generic.List<DeliveryNoteItem> items)
        {
            container.PaddingBottom(6).Column(col =>
            {
                col.Item().Element(c =>
                    PdfChrome.GroupBanner(c, specGroupName));

                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(20);   // #
                        cols.RelativeColumn(1.6f); // Glass Ref
                        cols.ConstantColumn(30);   // W1
                        cols.ConstantColumn(30);   // H1
                        cols.ConstantColumn(30);   // W2
                        cols.ConstantColumn(30);   // H2
                        cols.ConstantColumn(34);   // Ordered
                        cols.ConstantColumn(30);   // Prev
                        cols.ConstantColumn(36);   // This DN
                        cols.ConstantColumn(36);   // Balance
                        cols.ConstantColumn(38);   // SQM
                        cols.ConstantColumn(38);   // Wt/pc
                        cols.ConstantColumn(42);   // Total Wt
                    });

                    t.Header(h =>
                    {
                        PdfChrome.HeaderCell(h.Cell(), "#",
                            background: ExportColumnPalette.Identity);
                        PdfChrome.HeaderCell(h.Cell(), "GLASS REF",
                            background: ExportColumnPalette.Identity);
                        PdfChrome.HeaderCell(h.Cell(), "W1",
                            background: ExportColumnPalette.Pane1);
                        PdfChrome.HeaderCell(h.Cell(), "H1",
                            background: ExportColumnPalette.Pane1);
                        PdfChrome.HeaderCell(h.Cell(), "W2",
                            background: ExportColumnPalette.Pane2);
                        PdfChrome.HeaderCell(h.Cell(), "H2",
                            background: ExportColumnPalette.Pane2);
                        PdfChrome.HeaderCell(h.Cell(), "ORD",
                            background: ExportColumnPalette.Qty);
                        PdfChrome.HeaderCell(h.Cell(), "PREV",
                            background: ExportColumnPalette.Qty);
                        PdfChrome.HeaderCell(h.Cell(), "THIS",
                            background: ExportColumnPalette.Qty);
                        PdfChrome.HeaderCell(h.Cell(), "BAL",
                            background: ExportColumnPalette.Qty);
                        PdfChrome.HeaderCell(h.Cell(), "SQM",
                            background: ExportColumnPalette.Area);
                        PdfChrome.HeaderCell(h.Cell(), "WT/PC",
                            background: ExportColumnPalette.Weight);
                        PdfChrome.HeaderCell(h.Cell(), "TOT WT",
                            background: ExportColumnPalette.Weight);
                    });

                    bool alt = false;
                    foreach (var item in items.OrderBy(i => i.SerialNumber))
                    {
                        decimal sqm = SqmCalculator.CalculatePanel(item.W1, item.H1);
                        PdfChrome.DataCell(t.Cell(), item.SerialNumber.ToString(),
                            alt: alt, center: true);
                        PdfChrome.DataCell(t.Cell(), item.GlassRef ?? "—", alt: alt);
                        PdfChrome.DataCell(t.Cell(), Mm(item.W1), alt: alt, center: true,
                            background: ExportColumnPalette.Pane1Fill);
                        PdfChrome.DataCell(t.Cell(), Mm(item.H1), alt: alt, center: true,
                            background: ExportColumnPalette.Pane1Fill);
                        PdfChrome.DataCell(t.Cell(), Mm(item.W2), alt: alt, center: true,
                            background: ExportColumnPalette.Pane2Fill);
                        PdfChrome.DataCell(t.Cell(), Mm(item.H2), alt: alt, center: true,
                            background: ExportColumnPalette.Pane2Fill);
                        PdfChrome.DataCell(t.Cell(), item.OrderedQty.ToString(),
                            alt: alt, center: true,
                            background: ExportColumnPalette.QtyFill);
                        PdfChrome.DataCell(t.Cell(), item.PreviouslyDeliveredQty.ToString(),
                            alt: alt, center: true,
                            background: ExportColumnPalette.QtyFill);
                        PdfChrome.DataCell(t.Cell(), item.DeliveredQty.ToString(),
                            alt: alt, center: true, bold: item.DeliveredQty > 0,
                            color: item.DeliveredQty > 0 ? DocumentStyle.ColorAccent : null,
                            background: ExportColumnPalette.QtyFill);
                        PdfChrome.DataCell(t.Cell(), item.BalanceQty.ToString(),
                            alt: alt, center: true, bold: item.BalanceQty > 0,
                            color: item.BalanceQty > 0 ? DocumentStyle.ColorDanger : null,
                            background: ExportColumnPalette.QtyFill);
                        PdfChrome.DataCell(t.Cell(), sqm.ToString("0.000", CultureInfo.InvariantCulture),
                            alt: alt, center: true,
                            background: ExportColumnPalette.AreaFill);
                        PdfChrome.DataCell(t.Cell(),
                            item.WeightPerPiece > 0 ? item.WeightPerPiece.ToString("F2") : "—",
                            alt: alt, center: true,
                            background: ExportColumnPalette.WeightFill);
                        PdfChrome.DataCell(t.Cell(),
                            item.TotalWeight > 0 ? item.TotalWeight.ToString("F2") : "—",
                            alt: alt, center: true, bold: item.TotalWeight > 0,
                            background: ExportColumnPalette.WeightFill);
                        alt = !alt;
                    }
                });

                int groupDelivered = items.Sum(i => i.DeliveredQty);
                int groupBalance = items.Sum(i => i.BalanceQty);
                decimal groupWeight = items.Sum(i => i.TotalWeight);
                decimal groupSqm = items.Sum(i =>
                    SqmCalculator.CalculatePanel(i.W1, i.H1) * i.DeliveredQty);
                col.Item().AlignRight().PaddingTop(2)
                    .Text($"Group: {groupDelivered} delivered · " +
                          $"{groupBalance} balance · {groupSqm:0.000} sqm" +
                          (groupWeight > 0 ? $" · {groupWeight:F2} kg" : ""))
                    .FontSize(DocumentStyle.FontSizeCaption)
                    .FontColor(DocumentStyle.ColorMuted);
            });
        }

        private void RenderTotalsStrip(IContainer container)
        {
            container
                .Background(DocumentStyle.ColorTableHeader)
                .Padding(8)
                .Row(row =>
                {
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("TOTAL ORDERED")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        col.Item().Text(_entity.TotalOrderedQty.ToString())
                            .FontSize(DocumentStyle.FontSizeH1).Bold()
                            .FontColor(DocumentStyle.ColorText);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("PREV DELIVERED")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        col.Item()
                            .Text(_entity.TotalPreviouslyDeliveredQty.ToString())
                            .FontSize(DocumentStyle.FontSizeH1).Bold()
                            .FontColor(DocumentStyle.ColorMuted);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("THIS DN")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        col.Item().Text(_entity.TotalDeliveredQty.ToString())
                            .FontSize(DocumentStyle.FontSizeH1).Bold()
                            .FontColor(DocumentStyle.ColorAccent);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("BALANCE")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        col.Item().Text(_entity.TotalBalanceQty.ToString())
                            .FontSize(DocumentStyle.FontSizeH1).Bold()
                            .FontColor(_entity.TotalBalanceQty > 0
                                ? DocumentStyle.ColorDanger
                                : DocumentStyle.ColorSuccess);
                    });

                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("DN SEQUENCE")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        col.Item().Text($"#{_entity.SequenceNumber} of job")
                            .FontSize(DocumentStyle.FontSizeH1).Bold()
                            .FontColor(DocumentStyle.ColorText);
                    });

                    decimal totalSqm = (_entity.Items ?? new())
                        .Sum(i => SqmCalculator.CalculatePanel(i.W1, i.H1) * i.DeliveredQty);
                    row.RelativeItem().Column(col =>
                    {
                        col.Item().Text("THIS DN SQM")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        col.Item().Text(totalSqm.ToString("0.000", CultureInfo.InvariantCulture))
                            .FontSize(DocumentStyle.FontSizeH1).Bold()
                            .FontColor(DocumentStyle.ColorAccent);
                    });

                    decimal totalWeight = (_entity.Items ?? new())
                        .Sum(i => i.TotalWeight);
                    if (totalWeight > 0)
                    {
                        row.RelativeItem().Column(col =>
                        {
                            col.Item().Text("TOTAL WEIGHT")
                                .FontSize(DocumentStyle.FontSizeCaption)
                                .FontColor(DocumentStyle.ColorMuted);
                            col.Item().Text($"{totalWeight:F2} kg")
                                .FontSize(DocumentStyle.FontSizeH1).Bold()
                                .FontColor(DocumentStyle.ColorAccent);
                        });
                    }
                });
        }

        private static void RenderSignatureBlock(IContainer container)
        {
            container.Row(row =>
            {
                SignatureCell(row, "Dispatched By");
                row.ConstantItem(8);
                SignatureCell(row, "Received By");
                row.ConstantItem(8);
                SignatureCell(row, "Date");
            });
        }

        private static void SignatureCell(RowDescriptor row, string label)
        {
            row.RelativeItem().Column(col =>
            {
                col.Item().PaddingTop(30)
                    .BorderBottom(0.75f)
                    .BorderColor(DocumentStyle.ColorText);
                col.Item().PaddingTop(3).Text(label)
                    .FontSize(DocumentStyle.FontSizeCaption)
                    .FontColor(DocumentStyle.ColorMuted);
                col.Item().Text("Name: _____________________")
                    .FontSize(DocumentStyle.FontSizeCaption)
                    .FontColor(DocumentStyle.ColorMuted);
                col.Item().Text("Date: _______________________")
                    .FontSize(DocumentStyle.FontSizeCaption)
                    .FontColor(DocumentStyle.ColorMuted);
            });
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static string Mm(int mm) => mm > 0 ? mm.ToString() : "—";

        private string BuildSubtitle()
        {
            var parts = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrWhiteSpace(_entity.JobOrderId))
                parts.Add($"JO: {_entity.JobOrderId}");

            parts.Add(_entity.DeliveryDate.ToString("dd MMM yyyy", CultureInfo.InvariantCulture));

            if (!string.IsNullOrWhiteSpace(_entity.Status))
                parts.Add(_entity.Status.ToUpperInvariant());

            return string.Join("  ·  ", parts);
        }
    }
}