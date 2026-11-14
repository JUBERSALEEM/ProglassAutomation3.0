// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/MasterData/SavedProject.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 15 — PROJECT REFERENCE FIELD ADDED
//   NEW: ProjectReference column (descriptive, e.g. "HOUSE FOR IMAD HABIB")
//   All other fields preserved exactly.
//
// MIGRATION NOTE:
//   You MUST delete %AppData%\ProGlassAutomation\masterdata.db before
//   running. EF Core EnsureCreated does not ALTER existing tables.
//   You lose saved company/client/project cards (proforma invoices
//   in proformainvoice.db are untouched and safe).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.MasterData
{
    /// <summary>
    /// Master Data entity — one saved project profile.
    /// Auto-saved from Proforma Invoice Step 3 (Project Specs) on invoice save.
    ///
    /// Uniqueness: ProjectIdentifierName + CustomerName (case-insensitive).
    /// Same project name for different clients = different entity.
    ///
    /// Field distinction:
    ///   ProjectIdentifierName — short designation, e.g. "IMAD HABIB"
    ///   ProjectContractNo     — project code/number, e.g. "428 IMAD HABIB"
    ///   ProjectReference      — descriptive reference, e.g. "HOUSE FOR IMAD HABIB"
    ///   SiteDeliveryLocation  — physical address, e.g. "AJMAN"
    ///   LpoNumber             — client PO reference, e.g. "2680"
    ///
    /// DB: masterdata.db → Projects table
    /// </summary>
    [Table("Projects")]
    public class SavedProject
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Project designation name, e.g. "IMAD HABIB" or "SZR Tower Phase 2".</summary>
        [Required, MaxLength(200)]
        public string ProjectIdentifierName { get; set; } = "";

        /// <summary>
        /// Project name + number combined, e.g. "428 IMAD HABIB".
        /// Short internal project code/identifier.
        /// </summary>
        [MaxLength(100)]
        public string ProjectContractNo { get; set; } = "";

        /// <summary>
        /// NEW: Descriptive project reference, e.g. "HOUSE FOR IMAD HABIB".
        /// Full descriptive text explaining what the project is (site/building/purpose).
        /// Different from ProjectContractNo which is a short code.
        /// </summary>
        [MaxLength(300)]
        public string ProjectReference { get; set; } = "";

        /// <summary>Site delivery location, e.g. "AJMAN".</summary>
        [MaxLength(300)]
        public string SiteDeliveryLocation { get; set; } = "";

        /// <summary>LPO / PO reference number from client, e.g. "2680".</summary>
        [MaxLength(100)]
        public string LpoNumber { get; set; } = "";

        /// <summary>
        /// Client name this project belongs to.
        /// Used as part of uniqueness key:
        /// "Tower A" for "Apex LLC" ≠ "Tower A" for "Emirates LLC".
        /// </summary>
        [MaxLength(200)]
        public string CustomerName { get; set; } = "";

        /// <summary>How many times this project has been used.</summary>
        public int UsageCount { get; set; } = 1;

        /// <summary>Last time this project was selected or saved.</summary>
        public DateTime LastUsedAt { get; set; }

        /// <summary>When this record was first created.</summary>
        public DateTime CreatedAt { get; set; }
    }
}