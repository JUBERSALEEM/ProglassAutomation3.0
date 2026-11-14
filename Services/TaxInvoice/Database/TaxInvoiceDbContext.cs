using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.TaxInvoice.Attachment;
using ProGlassAutomation.Models.TaxInvoice.SavedTaxInvoice;
using System;
using System.IO;

namespace ProGlassAutomation.Services.TaxInvoice.Database
{
    /// <summary>
    /// EF Core DbContext for the sixth SQLite database: taxinvoices.db
    ///
    /// Location: %AppData%\ProGlassAutomation\taxinvoices.db
    ///
    /// Tables:
    ///   TaxInvoices           — main invoice header + JSON snapshot
    ///   TaxInvoiceItems       — normalized line items per invoice
    ///   TaxInvoiceAttachments — byte[] blobs (signed PDFs + docs)
    ///
    /// PATTERNS CARRIED FROM DeliveryNotesDbContext:
    ///   • DbPath pure computation — no side effects in getter
    ///   • Directory creation in OnConfiguring only
    ///   • HasPrincipalKey on string FK (InvoiceNumber)
    ///   • Cascade delete on Items + Attachments
    ///   • Explicit TEXT column type on decimal + FullSnapshotJson
    ///   • Indexes on all searchable + filterable columns
    ///
    /// DECIMAL STORAGE:
    ///   SQLite has no native decimal type. Using TEXT column type
    ///   with EF Core value conversion ensures precision is preserved.
    ///   All financial amounts stored as TEXT (same as JO + PI modules).
    ///
    /// ATTACHMENT BLOB:
    ///   FileBytes stored as BLOB. SQLite handles this natively.
    ///   No special configuration needed beyond EF defaults.
    ///
    /// SCHEMA CHANGE NOTE:
    ///   Delete taxinvoices.db before first run after any model change.
    ///   EF Core EnsureCreated() does not run migrations — it creates
    ///   the schema from scratch if the DB file does not exist.
    /// </summary>
    public class TaxInvoiceDbContext : DbContext
    {
        public DbSet<SavedTaxInvoice> TaxInvoices { get; set; } = null!;
        public DbSet<TaxInvoiceItem> TaxInvoiceItems { get; set; } = null!;
        public DbSet<TaxInvoiceAttachment> TaxInvoiceAttachments { get; set; } = null!;

        // ── DB path (pure computation — zero side effects) ────────────────

        private static string DbPath
        {
            get
            {
                return Path.Combine(
                    ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
                    "taxinvoices.db");
            }
        }

        /// <summary>Public accessor used by the schema migration runner.</summary>
        public static string DatabaseFilePath => DbPath;

        // ── Configuration ─────────────────────────────────────────────────

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // Directory creation here — runs once per context creation,
            // not on every DbPath access (BUG-12 pattern from DN).
            // Shared-aware (2026-09-24): see AppDataRoot.EnsureCurrentFolder.
            ProGlassAutomation.Services.Database.AppDataRoot.EnsureCurrentFolder();

            // Senior audit (Aug 2026): WAL + busy_timeout + foreign_keys=ON
            // via SqliteResilience. See SqliteResilience.cs.
            ProGlassAutomation.Services.Database.SqliteResilience
                .ConfigureFor(options, DbPath);
        }

        // ── Model configuration ───────────────────────────────────────────

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ── TaxInvoices table ────────────────────────────────────────
            modelBuilder.Entity<SavedTaxInvoice>(e =>
            {
                // Unique constraint on user-facing invoice number
                e.HasIndex(x => x.InvoiceNumber).IsUnique();

                // Family queries — most common filter
                e.HasIndex(x => x.ProformaId)
                    .HasDatabaseName("IX_TaxInvoices_ProformaId");

                // Cross-module reference queries
                e.HasIndex(x => x.JobOrderId)
                    .HasDatabaseName("IX_TaxInvoices_JobOrderId");

                // List search columns
                e.HasIndex(x => x.CustomerName)
                    .HasDatabaseName("IX_TaxInvoices_CustomerName");
                e.HasIndex(x => x.Status)
                    .HasDatabaseName("IX_TaxInvoices_Status");
                e.HasIndex(x => x.InvoiceType)
                    .HasDatabaseName("IX_TaxInvoices_InvoiceType");
                e.HasIndex(x => x.IssueDate)
                    .HasDatabaseName("IX_TaxInvoices_IssueDate");
                e.HasIndex(x => x.DueDate)
                    .HasDatabaseName("IX_TaxInvoices_DueDate");

                // Composite index for family ordering queries:
                // WHERE ProformaId = ? ORDER BY SequenceNumber
                e.HasIndex(x => new { x.ProformaId, x.SequenceNumber })
                    .HasDatabaseName("IX_TaxInvoices_Family_Sequence");

                // Explicit TEXT storage for JSON blob
                e.Property(x => x.FullSnapshotJson)
                    .HasColumnType("TEXT");

                // Explicit TEXT storage for decimal financial columns
                // (SQLite has no native decimal — TEXT preserves precision)
                e.Property(x => x.Subtotal).HasColumnType("TEXT");
                e.Property(x => x.DiscountAmount).HasColumnType("TEXT");
                e.Property(x => x.DiscountPercent).HasColumnType("TEXT");
                e.Property(x => x.AfterDiscount).HasColumnType("TEXT");
                e.Property(x => x.VatAmount).HasColumnType("TEXT");
                e.Property(x => x.GrandTotal).HasColumnType("TEXT");
                e.Property(x => x.TotalSqm).HasColumnType("TEXT");

                // One-to-many: Invoice → Items (cascade delete)
                e.HasMany(x => x.Items)
                    .WithOne(i => i.TaxInvoice)
                    .HasForeignKey(i => i.InvoiceNumber)
                    .HasPrincipalKey(x => x.InvoiceNumber)
                    .OnDelete(DeleteBehavior.Cascade);

                // One-to-many: Invoice → Attachments (cascade delete)
                e.HasMany(x => x.Attachments)
                    .WithOne(a => a.TaxInvoice)
                    .HasForeignKey(a => a.InvoiceNumber)
                    .HasPrincipalKey(x => x.InvoiceNumber)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ── TaxInvoiceItems table ────────────────────────────────────
            modelBuilder.Entity<TaxInvoiceItem>(e =>
            {
                e.HasIndex(x => x.InvoiceNumber)
                    .HasDatabaseName("IX_TaxInvoiceItems_InvoiceNumber");
                e.HasIndex(x => x.ProformaId)
                    .HasDatabaseName("IX_TaxInvoiceItems_ProformaId");

                // Composite for per-invoice item lookup
                e.HasIndex(x => new
                {
                    x.InvoiceNumber,
                    x.SpecGroupNumber,
                    x.SerialNumber
                }).HasDatabaseName("IX_TaxInvoiceItems_Composite");

                // Decimal columns as TEXT
                e.Property(x => x.Sqm).HasColumnType("TEXT");
                e.Property(x => x.TotalSqm).HasColumnType("TEXT");
                e.Property(x => x.Rate).HasColumnType("TEXT");
                e.Property(x => x.Value).HasColumnType("TEXT");
            });

            // ── TaxInvoiceAttachments table ──────────────────────────────
            modelBuilder.Entity<TaxInvoiceAttachment>(e =>
            {
                e.HasIndex(x => x.InvoiceNumber)
                    .HasDatabaseName("IX_TaxInvoiceAttachments_InvoiceNumber");

                // For Statement PDF ordering: WHERE InvoiceNumber = ?
                // ORDER BY AttachmentType (SignedPI first, then SignedDN, then Other)
                e.HasIndex(x => new { x.InvoiceNumber, x.AttachmentType })
                    .HasDatabaseName("IX_TaxInvoiceAttachments_Type");
            });
        }
    }
}