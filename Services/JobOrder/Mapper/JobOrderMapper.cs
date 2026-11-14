using ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus;
using ProGlassAutomation.Models.JobOrder.Common.JobPriority;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using System;
using System.Text.Json;

namespace ProGlassAutomation.Services.JobOrder.Mapper
{
    /// <summary>
    /// Handles conversion between:
    ///   - SavedProformaInvoice → JobOrderSnapshot (BuildFromProformaInvoice)
    ///   - JobOrderSnapshot     → SavedJobOrder entity (BuildEntityFromSnapshot)
    ///   - JobOrderSnapshot     → JSON blob (SerializeSnapshot)
    ///   - SavedJobOrder JSON   → JobOrderSnapshot (DeserializeSnapshot)
    ///
    /// The core "convert PI to Job Order" method deep-copies the invoice
    /// snapshot via JSON round-trip so subsequent PI edits don't mutate
    /// the historical record preserved in the job order.
    ///
    /// Note on GrandTotal:
    /// The ProformaInvoiceSnapshot POCO does NOT contain a computed grand
    /// total field (only discount + VAT % inputs). We take the grand total
    /// directly from SavedProformaInvoice.GrandTotalVatInclusive (indexed
    /// column) and pass it through to SavedJobOrder for search + sort.
    /// </summary>
    public static class JobOrderMapper
    {
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = false,
            PropertyNamingPolicy = null,
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.Never
        };

        // ═════════════════════════════════════════════════════════════════
        //  CONVERSION FROM PROFORMA INVOICE
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Converts a saved Proforma Invoice into a new Job Order snapshot.
        /// Deep-copies the invoice via JSON round-trip so future PI edits
        /// don't mutate the embedded snapshot.
        ///
        /// The returned snapshot has:
        ///   - JobOrderId          → derived from proformaId (PI-... → JOB-...)
        ///   - SourceProformaId    → the source PI's ProformaId
        ///   - ConvertedAt         → DateTime.Now
        ///   - EmbeddedInvoice     → deep clone of PI snapshot
        ///   - Production          → initial status derived from the source
        ///                           PI: Approved → Released (sent to the
        ///                           factory floor), otherwise Draft.
        ///                           Priority defaults to Medium.
        /// </summary>
        public static JobOrderSnapshot BuildFromProformaInvoice(SavedProformaInvoice savedInvoice)
        {
            if (savedInvoice == null)
                throw new ArgumentNullException(nameof(savedInvoice));

            // Deserialize the PI snapshot from JSON blob
            var invoiceSnapshot = ProformaInvoiceMapper.DeserializeSnapshot(savedInvoice);
            if (invoiceSnapshot == null)
                throw new InvalidOperationException(
                    $"Cannot convert PI '{savedInvoice.ProformaId}': " +
                    $"snapshot JSON is empty or invalid.");

            // Deep clone via JSON round-trip to guarantee no shared references
            string cloneJson = JsonSerializer.Serialize(invoiceSnapshot, JsonOptions);
            var clonedInvoice = JsonSerializer.Deserialize<ProformaInvoiceSnapshot>(
                cloneJson, JsonOptions) ?? new ProformaInvoiceSnapshot();

            // Derive JOB-YYYY-NNN from PI-YYYY-NNN (replace PI prefix with JOB)
            string jobOrderId = DeriveJobOrderId(savedInvoice.ProformaId);

            // A CONFIRMED quote (status Approved) produces a Job Order that is
            // already released to the factory floor — the customer confirmed
            // it, so there is no reason to hold the job in Draft. A quote that
            // was never confirmed stays Draft until the factory releases it.
            bool confirmed = string.Equals(
                savedInvoice.Status, "Approved", StringComparison.OrdinalIgnoreCase);

            return new JobOrderSnapshot
            {
                JobOrderId = jobOrderId,
                SourceProformaId = savedInvoice.ProformaId ?? string.Empty,
                ConvertedAt = DateTime.Now,
                EmbeddedInvoice = clonedInvoice,
                Production = new Models.JobOrder.ProductionDetails.ProductionDetails
                {
                    Status = confirmed
                        ? JobOrderStatus.Released.ToString()
                        : JobOrderStatus.Draft.ToString(),
                    Priority = JobPriority.Medium.ToString(),
                    DeliveryDate = null,
                    LastEditedAt = DateTime.Now,
                    LastEditedBy = Environment.UserName
                }
            };
        }

        /// <summary>
        /// Job-order name copied from the embedded project: identifier, then
        /// the descriptive reference, then the contract number. Never a type name.
        /// </summary>
        public static string ResolveProjectName(ProjectSnapshot? project)
        {
            if (project == null) return string.Empty;
            if (!string.IsNullOrWhiteSpace(project.ProjectIdentifierName))
                return project.ProjectIdentifierName.Trim();
            if (!string.IsNullOrWhiteSpace(project.ProjectReference))
                return project.ProjectReference.Trim();
            return project.ProjectContractNo?.Trim() ?? string.Empty;
        }

        /// <summary>
        /// Derives a Job Order ID from a Proforma Invoice ID.
        /// e.g. "PI-2026-001" → "JOB-2026-001"
        ///
        /// If the source ID doesn't start with "PI-", falls back to
        /// "JOB-{sourceId}".
        /// </summary>
        public static string DeriveJobOrderId(string? proformaId)
        {
            if (string.IsNullOrWhiteSpace(proformaId))
                return $"JOB-{DateTime.Now.Year}-001";

            if (proformaId.StartsWith("PI-", StringComparison.OrdinalIgnoreCase))
                return "JOB-" + proformaId.Substring(3);

            return "JOB-" + proformaId;
        }

        // ═════════════════════════════════════════════════════════════════
        //  ENTITY ↔ SNAPSHOT
        // ═════════════════════════════════════════════════════════════════

        /// <summary>
        /// Builds a SavedJobOrder EF entity from a JobOrderSnapshot.
        /// Populates indexed columns from snapshot data + serializes full
        /// snapshot to JSON blob.
        ///
        /// grandTotalOverride: pass the source PI's
        /// SavedProformaInvoice.GrandTotalVatInclusive value here — the
        /// PI snapshot itself does not contain the computed grand total
        /// (only the discount + VAT % inputs). If omitted, defaults to 0.
        /// </summary>
        public static SavedJobOrder BuildEntityFromSnapshot(
            JobOrderSnapshot snapshot,
            decimal grandTotalOverride = 0m)
        {
            if (snapshot == null)
                throw new ArgumentNullException(nameof(snapshot));

            // Extract searchable fields from embedded invoice for indexing
            string customerName = snapshot.EmbeddedInvoice?.Client?.CustomerName ?? string.Empty;
            string projectName = ResolveProjectName(snapshot.EmbeddedInvoice?.Project);

            return new SavedJobOrder
            {
                JobOrderId = snapshot.JobOrderId,
                SourceProformaId = snapshot.SourceProformaId,
                CustomerName = customerName,
                ProjectName = projectName,
                GrandTotalVatInclusive = grandTotalOverride,
                Status = snapshot.Production?.Status ?? "Draft",
                Priority = snapshot.Production?.Priority ?? "Medium",
                DeliveryDate = snapshot.Production?.DeliveryDate,
                CreatedAt = snapshot.ConvertedAt,
                UpdatedAt = DateTime.Now,
                FullSnapshotJson = SerializeSnapshot(snapshot)
            };
        }

        /// <summary>
        /// Updates an existing SavedJobOrder entity from a mutated
        /// JobOrderSnapshot. Refreshes indexed columns + JSON blob.
        /// Preserves Id, CreatedAt, and GrandTotalVatInclusive from the
        /// existing entity (the grand total shouldn't change after
        /// conversion — the invoice snapshot is immutable).
        /// </summary>
        public static void UpdateEntityFromSnapshot(SavedJobOrder entity, JobOrderSnapshot snapshot)
        {
            if (entity == null) throw new ArgumentNullException(nameof(entity));
            if (snapshot == null) throw new ArgumentNullException(nameof(snapshot));

            string customerName = snapshot.EmbeddedInvoice?.Client?.CustomerName ?? string.Empty;
            string projectName = ResolveProjectName(snapshot.EmbeddedInvoice?.Project);

            entity.JobOrderId = snapshot.JobOrderId;
            entity.SourceProformaId = snapshot.SourceProformaId;
            entity.CustomerName = customerName;
            entity.ProjectName = projectName;
            entity.Status = snapshot.Production?.Status ?? "Draft";
            entity.Priority = snapshot.Production?.Priority ?? "Medium";
            entity.DeliveryDate = snapshot.Production?.DeliveryDate;
            entity.UpdatedAt = DateTime.Now;
            entity.FullSnapshotJson = SerializeSnapshot(snapshot);
            // Note: GrandTotalVatInclusive intentionally NOT updated —
            // preserves the original conversion value.
        }

        // ═════════════════════════════════════════════════════════════════
        //  JSON SERIALIZATION
        // ═════════════════════════════════════════════════════════════════

        public static string SerializeSnapshot(JobOrderSnapshot snapshot)
        {
            if (snapshot == null) return string.Empty;
            return JsonSerializer.Serialize(snapshot, JsonOptions);
        }

        public static JobOrderSnapshot? DeserializeSnapshot(SavedJobOrder entity)
        {
            if (entity == null || string.IsNullOrWhiteSpace(entity.FullSnapshotJson))
                return null;

            try
            {
                return JsonSerializer.Deserialize<JobOrderSnapshot>(
                    entity.FullSnapshotJson, JsonOptions);
            }
            catch
            {
                // corrupt JSON — caller should handle null
                return null;
            }
        }
    }
}