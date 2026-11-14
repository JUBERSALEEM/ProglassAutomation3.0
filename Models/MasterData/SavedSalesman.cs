using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.MasterData
{
    /// <summary>
    /// Master Data entity — one salesman.
    /// Auto-saved from Proforma Invoice Step 3 (SalesRepresentative field).
    ///
    /// Uniqueness: Name (case-insensitive).
    /// Simple lookup table — name only for now.
    /// Future: add department, phone, email, commission rate.
    ///
    /// Used by:
    ///   - Step 2 (future) Client Profile: salesman ComboBox dropdown
    ///   - Reports: sales analytics by salesman
    ///
    /// DB: masterdata.db → Salesmen table
    /// </summary>
    [Table("Salesmen")]
    public class SavedSalesman
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Salesman full name, e.g. "Ahmed Al Mansouri".</summary>
        [Required, MaxLength(200)]
        public string Name { get; set; } = "";

        /// <summary>Department (future use).</summary>
        [MaxLength(100)]
        public string Department { get; set; } = "";

        /// <summary>How many invoices this salesman has been assigned to.</summary>
        public int UsageCount { get; set; } = 1;

        /// <summary>Last time this salesman was selected or saved.</summary>
        public DateTime LastUsedAt { get; set; }

        /// <summary>When this record was first created.</summary>
        public DateTime CreatedAt { get; set; }
    }
}