using CsvHelper;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.Csv;
using ProGlassAutomation.Services.Export.FileSystem;
using System;
using System.Linq;

using System.Globalization;
namespace ProGlassAutomation.Services.JobOrder.Export
{
    /// <summary>
    /// CSV layout for a Job Order — flat cut list only per Round 3
    /// design decision Q3. No header sections, no notes — just one
    /// row per glass item with all identifying context columns so
    /// the file is machine-parseable.
    ///
    /// Header row includes the Overdue flag column per Q2.
    ///
    /// CHAT 20 FIX:
    ///   - Ambiguous "Project" column renamed to precise
    ///     "ProjectIdentifierName" (matches Step 3 field name).
    ///   - Added missing project fields:
    ///       ProjectNameNo    (was: ProjectContractNo — not exported)
    ///       ProjectReference (new field from Chat 15 — not exported)
    ///       LpoNumber        (was not exported)
    ///   - ResolveReference prefers ProjectReference (most descriptive).
    ///
    /// SCHEMA CHANGE NOTE (Option A):
    ///   The CSV column previously named "Project" has been renamed to
    ///   "ProjectIdentifierName" for precision. External systems
    ///   consuming this CSV will need to update their column mappings.
    ///   Three new columns added at the same position for full project
    ///   context: ProjectNameNo, ProjectReference, LpoNumber.
    /// </summary>
    public class JobOrderCsvLayout : ICsvLayout
    {
        private readonly SavedJobOrder _entity;
        private readonly JobOrderSnapshot _snapshot;
        private readonly ProformaInvoiceSnapshot _invoice;

        public JobOrderCsvLayout(SavedJobOrder entity, JobOrderSnapshot snapshot)
        {
            _entity = entity ?? throw new ArgumentNullException(nameof(entity));
            _snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
            _invoice = snapshot.EmbeddedInvoice ?? new ProformaInvoiceSnapshot();
        }

        // ── IDocumentLayout ──────────────────────────────────────────
        public string DocumentPrefix => "JO";
        public string DocumentId => _entity.JobOrderId ?? _snapshot.JobOrderId;
        public string DocumentTitle => "Job Order — Cut List";
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

        public void WriteRows(CsvWriter writer)
        {
            // Header row
            // CHAT 20: Column names are now precise and match Step 3 fields.
            writer.WriteField("JobOrderId");
            writer.WriteField("SourceProformaId");
            writer.WriteField("Customer");
            writer.WriteField("ProjectName");
            writer.WriteField("ProjectReference");
            writer.WriteField("ContractNo");
            writer.WriteField("LpoNumber");               // CHAT 20: NEW
            writer.WriteField("Status");
            writer.WriteField("Priority");
            writer.WriteField("DeliveryDate");
            writer.WriteField("Overdue");
            writer.WriteField("SpecNumber");
            writer.WriteField("SpecName");
            writer.WriteField("GlassType");
            writer.WriteField("RowNumber");
            writer.WriteField("GlassRef");
            writer.WriteField("W1");
            writer.WriteField("H1");
            writer.WriteField("W2");
            writer.WriteField("H2");
            writer.WriteField("Qty");
            writer.WriteField("SQM");
            writer.WriteField("TOTAL SQM");
            writer.NextRecord();

            // Read all project fields — one per invoice, replicated per row
            string jobId = _entity.JobOrderId ?? "";
            string sourcePi = _snapshot.SourceProformaId ?? "";
            string customer = _invoice.Client?.CustomerName ?? "";

            // CHAT 20: All three project fields captured now.
            string projectIdentifier = _invoice.Project?.ProjectIdentifierName ?? "";
            string projectNameNo = _invoice.Project?.ProjectContractNo ?? "";
            string projectReference = _invoice.Project?.ProjectReference ?? "";
            string lpoNumber = _invoice.Project?.LpoNumber ?? "";

            string status = _snapshot.Production?.Status ?? "Draft";
            string priority = _snapshot.Production?.Priority ?? "Medium";
            string deliveryStr = _snapshot.Production?.DeliveryDate.HasValue == true
                                        ? _snapshot.Production.DeliveryDate.Value.ToString("yyyy-MM-dd")
                                        : "";
            string overdueFlag = IsOverdue() ? "YES" : "NO";

            if (_invoice.SpecGroups == null || _invoice.SpecGroups.Count == 0)
                return;

            foreach (var group in _invoice.SpecGroups.OrderBy(g => g.GroupNumber))
            {
                if (group.ItemRows == null) continue;

                foreach (var row in group.ItemRows)
                {
                    writer.WriteField(jobId);
                    writer.WriteField(sourcePi);
                    writer.WriteField(customer);
                    writer.WriteField(projectIdentifier);
                    writer.WriteField(projectReference);
                    writer.WriteField(projectNameNo);
                    writer.WriteField(lpoNumber);
                    writer.WriteField(status);
                    writer.WriteField(priority);
                    writer.WriteField(deliveryStr);
                    writer.WriteField(overdueFlag);
                    writer.WriteField(group.GroupNumber);
                    writer.WriteField(group.SelectedSpecName ?? "");
                    writer.WriteField(group.SelectedGlassType ?? "");
                    writer.WriteField(row.SerialNumber);
                    writer.WriteField(row.GlassRef ?? "");
                    writer.WriteField(row.W1);
                    writer.WriteField(row.H1);
                    writer.WriteField(row.W2);
                    writer.WriteField(row.H2);
                    writer.WriteField(row.Qty);
                    writer.WriteField(row.Sqm1.ToString("0.000", CultureInfo.InvariantCulture));
                    writer.WriteField(row.TotalSqm.ToString("0.000", CultureInfo.InvariantCulture));
                    writer.NextRecord();
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────

        private bool IsOverdue()
        {
            var status = _snapshot.Production?.Status ?? "";
            var delivery = _snapshot.Production?.DeliveryDate;
            if (!delivery.HasValue) return false;
            if (string.Equals(status, "Completed", StringComparison.OrdinalIgnoreCase)) return false;
            if (string.Equals(status, "Delivered", StringComparison.OrdinalIgnoreCase)) return false;
            return delivery.Value.Date < DateTime.Today;
        }

        /// <summary>
        /// Resolves the reference text used in export metadata.
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
    }
}