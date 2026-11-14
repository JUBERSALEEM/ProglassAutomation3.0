using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.MasterData
{
    /// <summary>
    /// Master Data entity — one saved company profile.
    /// Auto-saved from Proforma Invoice Step 1 on invoice save.
    ///
    /// Uniqueness: CompanyName + CompanyTrn (case-insensitive).
    /// Two companies with same name but different TRN = different entities.
    ///
    /// Used by:
    ///   - Step 1 Company Setup: card auto-fill
    ///   - Future modules: DN letterhead, JO company ref
    ///
    /// DB: masterdata.db → Companies table
    /// </summary>
    [Table("Companies")]
    public class SavedCompany
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Company registered name, e.g. "Apex Glazing LLC".</summary>
        [Required, MaxLength(200)]
        public string CompanyName { get; set; } = "";

        /// <summary>UAE Federal Tax Authority TRN (15 digits).</summary>
        [MaxLength(20)]
        public string CompanyTrn { get; set; } = "";

        /// <summary>Headquarters location, e.g. "Dubai, UAE".</summary>
        [MaxLength(300)]
        public string CompanyLocation { get; set; } = "";

        /// <summary>How many times this company has been used in invoices.</summary>
        public int UsageCount { get; set; } = 1;

        /// <summary>Last time this company was selected or saved.</summary>
        public DateTime LastUsedAt { get; set; }

        /// <summary>When this record was first created.</summary>
        public DateTime CreatedAt { get; set; }

        // ═══ v2 — full company profile (Company Center module) ═══
        // All optional; older rows simply carry empty values. Schema
        // evolution is handled by MasterDataRepository.InitializeAsync
        // (ALTER TABLE ADD COLUMN for existing databases).

        /// <summary>Path to the company logo image (empty = none).</summary>
        [MaxLength(500)]
        public string LogoPath { get; set; } = "";

        /// <summary>
        /// How the logo is placed on printed documents:
        /// 0 = None (text), 1 = LeftPortrait, 2 = TopPanel.
        /// </summary>
        public CompanyLogoLayout LogoLayout { get; set; } = CompanyLogoLayout.None;

        /// <summary>
        /// Printed logo width in millimetres (portrait layout). Clamped to
        /// 8–60 mm by the renderer; the stored value is the operator's intent.
        /// </summary>
        public double LogoWidthMm { get; set; } = 22;

        /// <summary>
        /// Printed logo height in millimetres (portrait layout). Clamped to
        /// 8–60 mm by the renderer; the stored value is the operator's intent.
        /// </summary>
        public double LogoHeightMm { get; set; } = 26;

        /// <summary>Authorized representative / contact person.</summary>
        [MaxLength(120)]
        public string Representative { get; set; } = "";

        /// <summary>Representative mobile number.</summary>
        [MaxLength(30)]
        public string RepresentativeMobile { get; set; } = "";

        /// <summary>Company e-mail shown on documents.</summary>
        [MaxLength(120)]
        public string CompanyEmail { get; set; } = "";

        /// <summary>Bank account number / IBAN for invoice footers.</summary>
        [MaxLength(80)]
        public string AccountNumber { get; set; } = "";

        /// <summary>Bank name.</summary>
        [MaxLength(120)]
        public string BankName { get; set; } = "";

        /// <summary>The primary company pre-selected in Step 1.</summary>
        public bool IsDefault { get; set; }
    }
}