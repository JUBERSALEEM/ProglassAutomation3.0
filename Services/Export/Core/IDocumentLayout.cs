using System;

namespace ProGlassAutomation.Services.Export.Core
{
    /// <summary>
    /// Base contract every module layout implements.
    /// Provides identity info the engine needs to build filenames and
    /// dialog titles without knowing anything about the document content.
    ///
    /// Filename convention (built by ExportFileNamer):
    ///   {DocumentId}_{CompanyName}_{Reference}_{DocumentDate:dd-MM-yyyy}.{ext}
    ///
    /// Example:
    ///   PI-2026-001_EMAAR-PROPERTIES_TWR-A-L15_22-07-2026.pdf
    ///
    /// Empty CompanyName or Reference segments are skipped automatically.
    /// </summary>
    public interface IDocumentLayout
    {
        /// <summary>
        /// Short prefix used in dialog titles. e.g. "PI", "JO", "DN", "TAX".
        /// NOT used in filename (DocumentId already carries the prefix).
        /// </summary>
        string DocumentPrefix { get; }

        /// <summary>
        /// User-facing document ID. First segment of filename.
        /// e.g. "PI-2026-001", "JOB-2026-001", "DN-2026-001".
        /// </summary>
        string DocumentId { get; }

        /// <summary>
        /// Human-readable document type. Used in save dialog title.
        /// e.g. "Proforma Invoice", "Job Order — Production Sheet".
        /// </summary>
        string DocumentTitle { get; }

        /// <summary>
        /// Company / customer name. Second filename segment.
        /// Sanitized to UPPERCASE-WITH-HYPHENS. Skipped if empty.
        /// e.g. "Emaar Properties LLC" → "EMAAR-PROPERTIES-LLC"
        /// </summary>
        string CompanyName { get; }

        /// <summary>
        /// Project / contract / LPO reference. Third filename segment.
        /// For PI/JO: falls back ProjectName → ContractNo → LPO → empty.
        /// Sanitized to UPPERCASE-WITH-HYPHENS. Skipped if empty.
        /// e.g. "Tower A / Level 15" → "TOWER-A-LEVEL-15"
        /// </summary>
        string Reference { get; }

        /// <summary>
        /// Document date. Fourth filename segment formatted "dd-MM-yyyy".
        /// Use document's created/updated date, NOT export date, so
        /// exporting 5 old docs today produces 5 different filenames.
        /// </summary>
        DateTime DocumentDate { get; }
    }
}