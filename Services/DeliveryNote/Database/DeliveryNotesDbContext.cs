using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.DeliveryNote.SavedDeliveryNote;
using System;
using System.IO;

namespace ProGlassAutomation.Services.DeliveryNote.Database
{
    /// <summary>
    /// EF Core DbContext for the 5th SQLite database: deliverynotes.db
    ///
    /// Location: %AppData%\ProGlassAutomation\deliverynotes.db
    ///
    /// Tables:
    ///   DeliveryNotes           — main DN header + JSON snapshot
    ///   DeliveryNoteItems       — normalized line items per DN
    ///   DeliveryNoteAttachments — file references (images + PDFs)
    ///
    /// BUG-12: DbPath no longer calls Directory.CreateDirectory in getter.
    ///         Side effect moved to OnConfiguring where it belongs.
    /// BUG-13: Added index on HasTaxInvoice for Phase 19 queries.
    /// BUG-14: Added HasColumnType on FullSnapshotJson for migration compat.
    /// </summary>
    public class DeliveryNotesDbContext : DbContext
    {
        public DbSet<SavedDeliveryNote> DeliveryNotes { get; set; }
        public DbSet<DeliveryNoteItem> DeliveryNoteItems { get; set; }
        public DbSet<DeliveryNoteAttachment> DeliveryNoteAttachments { get; set; }

        // ── DB path (pure computation — no side effects) ─────────────────

        private static string DbPath
        {
            get
            {
                return Path.Combine(
                    ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
                    "deliverynotes.db");
            }
        }

        /// <summary>Public accessor used by the schema migration runner.</summary>
        public static string DatabaseFilePath => DbPath;

        // ── Configuration ────────────────────────────────────────────────

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // BUG-12: Directory creation moved here — runs once per context
            // creation, not on every DbPath property access.
            // Shared-aware (2026-09-24): see AppDataRoot.EnsureCurrentFolder.
            ProGlassAutomation.Services.Database.AppDataRoot.EnsureCurrentFolder();

            // Senior audit (Aug 2026): SqliteResilience applies WAL,
            // busy_timeout=5s, foreign_keys=ON on every connection so the
            // DN detail auto-save + list-refresh don't collide with a lock.
            ProGlassAutomation.Services.Database.SqliteResilience
                .ConfigureFor(options, DbPath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ── DeliveryNotes table ──────────────────────────────────────
            modelBuilder.Entity<SavedDeliveryNote>(e =>
            {
                e.HasIndex(x => x.DeliveryNoteId).IsUnique();
                e.HasIndex(x => x.JobOrderId);
                e.HasIndex(x => x.ProformaId);
                e.HasIndex(x => x.CustomerName);
                e.HasIndex(x => x.Status);
                e.HasIndex(x => x.DeliveryType);
                e.HasIndex(x => x.DeliveryDate);

                // BUG-13: Index for Phase 19 Tax Invoice queries
                // WHERE HasTaxInvoice = false — without this = full table scan
                e.HasIndex(x => x.HasTaxInvoice)
                    .HasDatabaseName("IX_DeliveryNotes_HasTaxInvoice");

                // BUG-14: Explicit column type for migration compatibility
                e.Property(x => x.FullSnapshotJson)
                    .HasColumnType("TEXT");

                // One-to-many: DN → Items (cascade delete)
                e.HasMany(x => x.Items)
                    .WithOne(i => i.DeliveryNote)
                    .HasForeignKey(i => i.DeliveryNoteId)
                    .HasPrincipalKey(x => x.DeliveryNoteId)
                    .OnDelete(DeleteBehavior.Cascade);

                // One-to-many: DN → Attachments (cascade delete)
                e.HasMany(x => x.Attachments)
                    .WithOne(a => a.DeliveryNote)
                    .HasForeignKey(a => a.DeliveryNoteId)
                    .HasPrincipalKey(x => x.DeliveryNoteId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // ── DeliveryNoteItems table ──────────────────────────────────
            modelBuilder.Entity<DeliveryNoteItem>(e =>
            {
                e.HasIndex(x => x.DeliveryNoteId);
                e.HasIndex(x => x.JobOrderId);
                e.HasIndex(x => new
                {
                    x.JobOrderId,
                    x.SpecGroupNumber,
                    x.SerialNumber
                });
            });

            // ── DeliveryNoteAttachments table ────────────────────────────
            modelBuilder.Entity<DeliveryNoteAttachment>(e =>
            {
                e.HasIndex(x => x.DeliveryNoteId);
            });
        }
    }
}