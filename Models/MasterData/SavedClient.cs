using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.MasterData
{
    /// <summary>
    /// Master Data entity — one saved client profile.
    /// Auto-saved from Proforma Invoice Step 3 on invoice save.
    ///
    /// Uniqueness: CustomerName + CustomerTrn (case-insensitive).
    /// Same customer with different TRN = different entity
    /// (e.g. same group company, different legal entity).
    ///
    /// Used by:
    ///   - Step 2 (future) Client Profile: card auto-fill
    ///   - Salesman dropdown population
    ///   - DN / JO customer reference
    ///
    /// DB: masterdata.db → Clients table
    /// </summary>
    [Table("Clients")]
    public class SavedClient
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Customer company name.</summary>
        [Required, MaxLength(200)]
        public string CustomerName { get; set; } = "";

        /// <summary>Customer TRN (15 digits UAE).</summary>
        [MaxLength(20)]
        public string CustomerTrn { get; set; } = "";

        /// <summary>Customer internal reference code.</summary>
        [MaxLength(100)]
        public string CustomerReferenceCode { get; set; } = "";

        /// <summary>Assigned salesman name (also saved to Salesmen table).</summary>
        [MaxLength(200)]
        public string SalesRepresentative { get; set; } = "";

        /// <summary>Person who prepared the document.</summary>
        [MaxLength(200)]
        public string DocumentPreparedBy { get; set; } = "";

        /// <summary>Customer billing address.</summary>
        [MaxLength(500)]
        public string BillingAddress { get; set; } = "";

        /// <summary>Attention / executive name at customer side.</summary>
        [MaxLength(200)]
        public string AttentionExecutiveName { get; set; } = "";

        /// <summary>Customer contact phone number.</summary>
        [MaxLength(50)]
        public string ClientContactPhone { get; set; } = "";

        /// <summary>How many times this client has been used.</summary>
        public int UsageCount { get; set; } = 1;

        /// <summary>Last time this client was selected or saved.</summary>
        public DateTime LastUsedAt { get; set; }

        /// <summary>When this record was first created.</summary>
        public DateTime CreatedAt { get; set; }
    }
}