// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.5.ProformaInvoiceDbContext.cs
// MODULE: step6 — EF Core mapping for the invoice tables.
//
// Same file, same rules as step2–step5: one database (inquiries.db), path
// taken from the established owner (InquiryDbContext), and NO EnsureCreated —
// the file's creation and version chain belong to the Inquiry initializer and
// the migration registry, which now carries 1018.
//
// The attachment entity is mapped here too but lives in its own table, so an
// invoice's evidence can be listed without reading its body.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;
using System;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>EF Core model for saved proforma invoices.</summary>
    public sealed class ProformaInvoiceDbContext : DbContext
    {
        public DbSet<ProformaInvoiceRecord> Invoices => Set<ProformaInvoiceRecord>();
        public DbSet<ProformaInvoiceAttachmentRecord> Attachments =>
            Set<ProformaInvoiceAttachmentRecord>();

        /// <summary>Same SQLite file as the rest of the ERP.</summary>
        public static string DatabaseFilePath => InquiryDbContext.DatabaseFilePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            AppDataRoot.EnsureCurrentFolder();
            SqliteResilience.ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<ProformaInvoiceRecord>(entity =>
            {
                entity.ToTable(ProformaInvoiceSchema.TableName);
                entity.HasKey(invoice => invoice.Id);
                entity.HasIndex(invoice => invoice.ProformaNumber).IsUnique();
                entity.HasIndex(invoice => invoice.Status);
                entity.HasIndex(invoice => invoice.ClientId);
                entity.HasIndex(invoice => invoice.InquiryId);
                entity.HasIndex(invoice => invoice.IssueDate);
            });

            model.Entity<ProformaInvoiceAttachmentRecord>(entity =>
            {
                entity.ToTable(ProformaInvoiceSchema.AttachmentTableName);
                entity.HasKey(attachment => attachment.Id);
                entity.HasIndex(attachment => attachment.ProformaInvoiceId);
            });
        }
    }

    /// <summary>One stored attachment. The file itself lives in the app data folder.</summary>
    public sealed class ProformaInvoiceAttachmentRecord
    {
        public long Id { get; set; }
        public int ProformaInvoiceId { get; set; }
        public string FileName { get; set; } = "";
        public string StoredFileName { get; set; } = "";
        public string Extension { get; set; } = "";
        public long FileSize { get; set; }
        public string AttachmentType { get; set; } = "Document";
        public string Note { get; set; } = "";
        public DateTime UploadedAtUtc { get; set; }
        public int? UploadedByUserId { get; set; }
        public string UploadedByName { get; set; } = "";
    }
}
