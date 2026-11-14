using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.Export.Pdf;
using ProGlassAutomation.Services.Export.Pdf.Renderers;
using QuestPDF.Fluent;
using QuestPDF.Infrastructure;
using System;
using System.Linq;

using System.Globalization;
namespace ProGlassAutomation.Services.JobOrder.Export
{
    /// <summary>
    /// PDF layout for a Job Order factory-floor production sheet.
    ///
    /// Renders:
    /// - Letterhead (company from embedded PI snapshot, JOB ID accent)
    /// - Client + Site Delivery block (2 columns)
    /// - Production instructions block (Assigned To, Cutting, Drawing, Notes)
    /// - Cut list table per spec group (W1/H1/W2/H2, qty, SQM, TOTAL SQM — NO PRICING)
    /// - Signature block (Prepared / Verified / Received)
    ///
    /// Deliberately excludes:
    /// - All money fields (rate, line total, VAT, grand total)
    /// - Terms and conditions
    /// - Discount lines
    ///
    /// This is a production instruction, not a customer document.
    ///
    /// CHAT 20 FIX:
    ///   - Client block now uses correct Step 3 labels:
    ///     "Project Identifier Name", "Project Name / No", "Project Reference"
    ///   - ProjectReference field now displayed (was missing)
    ///   - ResolveReference prefers ProjectReference (most descriptive)
    /// </summary>
    public class JobOrderPdfLayout : IPdfLayout
    {
        private readonly SavedJobOrder _entity;
        private readonly JobOrderSnapshot _snapshot;
        private readonly ProformaInvoiceSnapshot _invoice;

        public JobOrderPdfLayout(SavedJobOrder entity, JobOrderSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _invoice = snapshot.EmbeddedInvoice ?? new ProformaInvoiceSnapshot();
        }

        // ── IDocumentLayout ──────────────────────────────────────────
        public string DocumentPrefix => "JO";
        public string DocumentId => _entity.JobOrderId ?? _snapshot.JobOrderId;
        public string DocumentTitle => "Job Order — Production Sheet";
        public string CompanyName => _invoice.Client?.CustomerName
            ?? _entity.CustomerName
            ?? string.Empty;
        public string Reference => ExportFileNamer.FirstFilled(
            _invoice.Project?.ProjectIdentifierName,
            _entity.ProjectName,
            _invoice.Project?.ProjectReference);
        public DateTime DocumentDate => _snapshot.ConvertedAt != default
                                           ? _snapshot.ConvertedAt
                                           : _entity.CreatedAt;

        // ── IPdfLayout ───────────────────────────────────────────────
        public PageLayout PageLayout => PageLayout.A4Portrait;

        public LetterheadInfo? Letterhead
        {
            get
            {
                var info = new LetterheadInfo
                {
                    CompanyName = _invoice.Company?.CompanyName ?? string.Empty,
                    CompanyLocation = _invoice.Company?.CompanyLocation ?? string.Empty,
                    CompanyTrn = _invoice.Company?.CompanyTrn ?? string.Empty,
                    DocumentTitle = DocumentTitle,
                    DocumentId = DocumentId,
                    SubtitleLine = BuildSubtitle(),
                    LogoPath = _invoice.Company?.LogoPath ?? string.Empty,
                    LogoBase64 = _invoice.Company?.LogoBase64 ?? string.Empty,
                    LogoLayout = _invoice.Company?.LogoLayout
                        ?? ProGlassAutomation.Models.MasterData.CompanyLogoLayout.None,
                    LogoWidthMm = _invoice.Company?.LogoWidthMm ?? 22,
                    LogoHeightMm = _invoice.Company?.LogoHeightMm ?? 26
                };
                info.Meta.Add(("Status", FormatStatus(_snapshot.Production?.Status)));
                info.Meta.Add(("Priority", _snapshot.Production?.Priority ?? "Medium"));
                info.Meta.Add(("Delivery",
                    _snapshot.Production?.DeliveryDate.HasValue == true
                        ? _snapshot.Production.DeliveryDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                        : "Not set"));
                info.Meta.Add(("Source PI", _snapshot.SourceProformaId ?? "—"));
                return info;
            }
        }

        public void RenderContent(ColumnDescriptor content)
        {
            content.Item().Element(RenderIndustrialControlPanel);

            // ── Client + Site Delivery ────────────────────────────────
            content.Item().Element(c =>
                SectionHeaderRenderer.Render(c, "CLIENT & DELIVERY"));
            content.Item().Element(RenderClientAndDelivery);

            // ── Production instructions ───────────────────────────────
            content.Item().Element(c =>
                SectionHeaderRenderer.Render(c, "PRODUCTION INSTRUCTIONS"));
            content.Item().Element(RenderProductionInstructions);

            // ── Cut lists per spec group ──────────────────────────────
            content.Item().Element(c =>
                SectionHeaderRenderer.Render(c, "CUT LIST"));

            if (_invoice.SpecGroups == null || _invoice.SpecGroups.Count == 0)
            {
                content.Item().Text("No glass items in source invoice.")
                    .Italic()
                    .FontColor(DocumentStyle.ColorMuted);
            }
            else
            {
                foreach (var group in _invoice.SpecGroups
                                              .OrderBy(g => g.GroupNumber))
                {
                    content.Item().Element(c => RenderSpecGroupTable(c, group));
                }

                content.Item().Element(RenderProductionTotals);
            }

            // ── Signature block ───────────────────────────────────────
            content.Item().Element(c =>
                SectionHeaderRenderer.Render(c, "SIGN-OFF"));
            content.Item().Element(RenderSignatureBlock);
        }


        private void RenderIndustrialControlPanel(IContainer container)
        {
            var rows = (_invoice.SpecGroups ?? Enumerable.Empty<SpecGroupSnapshot>())
                .SelectMany(g => g.ItemRows ?? Enumerable.Empty<GlassItemRowSnapshot>())
                .ToList();
            PdfChrome.IndustrialDocumentPanel(container,
                "Production release control",
                "Workshop-ready ERP document for cutting, fabrication, QA and delivery planning. Commercial prices stay out; production quantities and risks stay visible.",
                ("Status", FormatStatus(_snapshot.Production?.Status), DocumentStyle.ColorAccent),
                ("Priority", _snapshot.Production?.Priority ?? "Medium", ResolvePriorityColor(_snapshot.Production?.Priority)),
                ("Spec Groups", (_invoice.SpecGroups?.Count ?? 0).ToString(CultureInfo.InvariantCulture), (string?)null),
                ("Pieces", rows.Sum(r => r.Qty).ToString(CultureInfo.InvariantCulture), (string?)null),
                ("Total SQM", rows.Sum(r => r.TotalSqm).ToString("0.000", CultureInfo.InvariantCulture), DocumentStyle.ColorAccent));
        }

        // ═════════════════════════════════════════════════════════════
        //  SECTION RENDERERS
        // ═════════════════════════════════════════════════════════════

        private void RenderStatusStrip(IContainer container)
        {
            PdfChrome.KpiStrip(container,
                ("Status", FormatStatus(_snapshot.Production?.Status), DocumentStyle.ColorAccent),
                ("Priority", _snapshot.Production?.Priority ?? "Medium",
                    ResolvePriorityColor(_snapshot.Production?.Priority)),
                ("Delivery",
                    _snapshot.Production?.DeliveryDate.HasValue == true
                        ? _snapshot.Production.DeliveryDate.Value.ToString("dd MMM yyyy", CultureInfo.InvariantCulture)
                        : "Not set", null),
                ("Source PI", _snapshot.SourceProformaId ?? "—", null));
        }

        private void RenderClientAndDelivery(IContainer container)
        {
            container.Row(row =>
            {
                row.RelativeItem().Element(c =>
                    PdfChrome.InfoCard(c, "Client", col =>
                    {
                        col.Item().Text(_invoice.Client?.CustomerName ?? "—")
                            .FontSize(DocumentStyle.FontSizeH2).Bold();

                        if (!string.IsNullOrWhiteSpace(_invoice.Client?.CustomerTrn))
                            PdfChrome.LabelValue(col, "TRN", _invoice.Client.CustomerTrn);
                        if (!string.IsNullOrWhiteSpace(_invoice.Project?.ProjectIdentifierName))
                            PdfChrome.LabelValue(col, "Project Name",
                                _invoice.Project.ProjectIdentifierName);
                        if (!string.IsNullOrWhiteSpace(_invoice.Project?.ProjectReference))
                            PdfChrome.LabelValue(col, "Project Reference",
                                _invoice.Project.ProjectReference);
                        if (!string.IsNullOrWhiteSpace(_invoice.Project?.ProjectContractNo))
                            PdfChrome.LabelValue(col, "Contract No",
                                _invoice.Project.ProjectContractNo);
                        if (!string.IsNullOrWhiteSpace(_invoice.Project?.LpoNumber))
                            PdfChrome.LabelValue(col, "LPO", _invoice.Project.LpoNumber);
                    }));

                row.ConstantItem(10);

                row.RelativeItem().Element(c =>
                    PdfChrome.InfoCard(c, "Site delivery", col =>
                    {
                        var prod = _snapshot.Production;
                        string contact = prod?.SiteDeliveryContact ?? string.Empty;
                        string phone = prod?.SiteDeliveryPhone ?? string.Empty;
                        string addr = prod?.SiteDeliveryAddress ?? string.Empty;

                        col.Item().Text(
                            string.IsNullOrWhiteSpace(contact) ? "No contact set" : contact)
                            .FontSize(DocumentStyle.FontSizeH2).Bold();

                        if (!string.IsNullOrWhiteSpace(phone))
                            PdfChrome.LabelValue(col, "Phone", phone);
                        if (!string.IsNullOrWhiteSpace(addr))
                            PdfChrome.LabelValue(col, "Address", addr);
                        else if (!string.IsNullOrWhiteSpace(_invoice.Project?.SiteDeliveryLocation))
                            PdfChrome.LabelValue(col, "From PI",
                                _invoice.Project.SiteDeliveryLocation);
                    }));
            });
        }

        private void RenderProductionInstructions(IContainer container)
        {
            var prod = _snapshot.Production;
            container.Column(col =>
            {
                col.Spacing(4);

                col.Item().Row(row =>
                {
                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("ASSIGNED TO")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        c.Item().Text(string.IsNullOrWhiteSpace(prod?.AssignedTo)
                                      ? "Not assigned" : prod.AssignedTo)
                            .FontSize(DocumentStyle.FontSizeBody).SemiBold();
                    });

                    row.RelativeItem().Column(c =>
                    {
                        c.Item().Text("DRAWING REFERENCE")
                            .FontSize(DocumentStyle.FontSizeCaption)
                            .FontColor(DocumentStyle.ColorMuted);
                        c.Item().Text(string.IsNullOrWhiteSpace(prod?.DrawingReference)
                                      ? "—" : prod.DrawingReference)
                            .FontSize(DocumentStyle.FontSizeBody).SemiBold();
                    });
                });

                if (!string.IsNullOrWhiteSpace(prod?.CuttingInstructions))
                {
                    col.Item().PaddingTop(6).Text("CUTTING INSTRUCTIONS")
                        .FontSize(DocumentStyle.FontSizeCaption)
                        .FontColor(DocumentStyle.ColorMuted);
                    col.Item().Border(0.5f).BorderColor(DocumentStyle.ColorBorder)
                        .Padding(6).Text(prod.CuttingInstructions)
                        .FontSize(DocumentStyle.FontSizeSmall);
                }

                if (!string.IsNullOrWhiteSpace(prod?.InternalNotes))
                {
                    col.Item().PaddingTop(6).Text("INTERNAL NOTES")
                        .FontSize(DocumentStyle.FontSizeCaption)
                        .FontColor(DocumentStyle.ColorMuted);
                    col.Item().Border(0.5f).BorderColor(DocumentStyle.ColorBorder)
                        .Padding(6).Text(prod.InternalNotes)
                        .FontSize(DocumentStyle.FontSizeSmall);
                }
            });
        }

        private void RenderSpecGroupTable(IContainer container, SpecGroupSnapshot group)
        {
            container.PaddingBottom(6).Column(col =>
            {
                // Group header
                col.Item().Element(c =>
                    PdfChrome.GroupBanner(c,
                        $"Spec {group.GroupNumber}: {group.SelectedSpecName}",
                        string.IsNullOrWhiteSpace(group.SelectedGlassType)
                            ? null : $"Type: {group.SelectedGlassType}"));

                // Table
                col.Item().Table(t =>
                {
                    t.ColumnsDefinition(cols =>
                    {
                        cols.ConstantColumn(28);   // #
                        cols.RelativeColumn(2);    // Glass Ref
                        cols.ConstantColumn(38);   // W1
                        cols.ConstantColumn(38);   // H1
                        cols.ConstantColumn(38);   // W2
                        cols.ConstantColumn(38);   // H2
                        cols.ConstantColumn(36);   // Qty
                        cols.ConstantColumn(48);   // SQM
                        cols.ConstantColumn(58);   // TOTAL SQM
                    });

                    // Header row
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
                        PdfChrome.HeaderCell(h.Cell(), "QTY",
                            background: ExportColumnPalette.Qty);
                        PdfChrome.HeaderCell(h.Cell(), "SQM",
                            background: ExportColumnPalette.Area);
                        PdfChrome.HeaderCell(h.Cell(), "TOTAL SQM",
                            background: ExportColumnPalette.Area);
                    });

                    if (group.ItemRows != null)
                    {
                        bool alt = false;
                        foreach (var r in group.ItemRows)
                        {
                            PdfChrome.DataCell(t.Cell(), r.SerialNumber.ToString(), alt: alt, center: true);
                            PdfChrome.DataCell(t.Cell(), r.GlassRef ?? "—", alt: alt, center: true);
                            PdfChrome.DataCell(t.Cell(), r.W1 > 0 ? r.W1.ToString() : "—", alt: alt, center: true,
                                background: ExportColumnPalette.Pane1Fill);
                            PdfChrome.DataCell(t.Cell(), r.H1 > 0 ? r.H1.ToString() : "—", alt: alt, center: true,
                                background: ExportColumnPalette.Pane1Fill);
                            PdfChrome.DataCell(t.Cell(), r.W2 > 0 ? r.W2.ToString() : "—", alt: alt, center: true,
                                background: ExportColumnPalette.Pane2Fill);
                            PdfChrome.DataCell(t.Cell(), r.H2 > 0 ? r.H2.ToString() : "—", alt: alt, center: true,
                                background: ExportColumnPalette.Pane2Fill);
                            PdfChrome.DataCell(t.Cell(), r.Qty.ToString(), alt: alt, center: true, bold: true,
                                background: ExportColumnPalette.QtyFill);
                            PdfChrome.DataCell(t.Cell(), r.Sqm1.ToString("0.000", CultureInfo.InvariantCulture), alt: alt, center: true,
                                background: ExportColumnPalette.AreaFill);
                            PdfChrome.DataCell(t.Cell(), r.TotalSqm.ToString("0.000", CultureInfo.InvariantCulture), alt: alt, center: true, bold: true,
                                background: ExportColumnPalette.AreaFill);
                            alt = !alt;
                        }
                    }
                });

                // Group total pieces
                if (group.ItemRows != null && group.ItemRows.Count > 0)
                {
                    int groupPieces = group.ItemRows.Sum(r => r.Qty);
                    decimal groupSqm = group.ItemRows.Sum(r => r.TotalSqm);
                    col.Item().PaddingTop(2).AlignRight()
                        .Text($"Group total: {groupPieces} piece(s)  ·  {groupSqm:0.000} TOTAL SQM")
                        .FontSize(DocumentStyle.FontSizeCaption)
                        .FontColor(DocumentStyle.ColorMuted);
                }
            });
        }

        private void RenderProductionTotals(IContainer container)
        {
            var allRows = _invoice.SpecGroups?
                .SelectMany(g => g.ItemRows ?? Enumerable.Empty<GlassItemRowSnapshot>())
                .ToList() ?? new System.Collections.Generic.List<GlassItemRowSnapshot>();

            int totalPieces = allRows.Sum(r => r.Qty);
            decimal totalSqm = allRows.Sum(r => r.TotalSqm);
            int totalSpecs = _invoice.SpecGroups?.Count ?? 0;

            PdfChrome.KpiStrip(container,
                ("Total specs", totalSpecs.ToString(), null),
                ("Total pieces", totalPieces.ToString(), DocumentStyle.ColorAccent),
                ("TOTAL SQM", totalSqm.ToString("0.000", CultureInfo.InvariantCulture), null));
        }

        private void RenderSignatureBlock(IContainer container)
        {
            PdfChrome.Signatures(container,
                new SignatureSlot { Title = "Prepared by" },
                new SignatureSlot { Title = "Verified by" },
                new SignatureSlot { Title = "Received by" });
        }

        // ── Helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Resolves the reference text used in the PDF letterhead subtitle.
        /// CHAT 20 FIX: Prefer ProjectReference (most descriptive) first.
        /// Falls back through the other project fields in decreasing order
        /// of descriptiveness.
        /// </summary>
        private string ResolveReference()
        {
            var proj = _invoice.Project;
            if (proj == null) return string.Empty;

            // CHAT 20: ProjectReference first — most descriptive text
            // like "HOUSE FOR IMAD HABIB".
            if (!string.IsNullOrWhiteSpace(proj.ProjectReference))
                return proj.ProjectReference;

            if (!string.IsNullOrWhiteSpace(proj.ProjectIdentifierName))
                return proj.ProjectIdentifierName;
            if (!string.IsNullOrWhiteSpace(proj.ProjectContractNo))
                return proj.ProjectContractNo;
            if (!string.IsNullOrWhiteSpace(proj.LpoNumber))
                return proj.LpoNumber;

            return string.Empty;
        }

        private string BuildSubtitle()
        {
            var parts = new System.Collections.Generic.List<string>();

            if (!string.IsNullOrWhiteSpace(_snapshot.SourceProformaId))
                parts.Add($"From: {_snapshot.SourceProformaId}");

            if (_snapshot.Production?.DeliveryDate.HasValue == true)
                parts.Add($"Due: {_snapshot.Production.DeliveryDate.Value:dd MMM yyyy}");

            return string.Join("  ·  ", parts);
        }

        private static string FormatStatus(string? status) =>
            status switch
            {
                "InProduction" => "IN PRODUCTION",
                null or "" => "DRAFT",
                _ => status.ToUpperInvariant()
            };

        private static string ResolvePriorityColor(string? priority) =>
            priority switch
            {
                "High" => DocumentStyle.ColorDanger,
                "Medium" => DocumentStyle.ColorWarning,
                "Low" => DocumentStyle.ColorSuccess,
                _ => DocumentStyle.ColorText
            };
    }
}