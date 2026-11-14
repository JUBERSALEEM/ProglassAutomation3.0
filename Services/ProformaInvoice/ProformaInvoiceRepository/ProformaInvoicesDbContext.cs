using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using System;
using System.IO;

namespace ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository
{
    /// <summary>
    /// Dedicated SQLite DbContext for saved proforma invoices.
    /// Database file: %AppData%\ProGlassAutomation\proformainvoices.db
    ///
    /// Isolated from main proglass.db and specifications.db so invoices
    /// can be backed up, exported, or reset independently.
    ///
    /// BUG-13: DatabaseFilePath no longer calls Directory.CreateDirectory
    ///         in its getter. Side effect moved to OnConfiguring where
    ///         it runs once per context creation.
    /// BUG-14: Added indexes on ValidUntil + IssueDate for fast date-range
    ///         filtering in the Load List UI.
    /// </summary>
    public class ProformaInvoicesDbContext : DbContext
    {
        public DbSet<SavedProformaInvoice> ProformaInvoices { get; set; }

        // BUG-13: pure computation — no side effects
        public static string DatabaseFilePath
        {
            get
            {
                return Path.Combine(
                    ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
                    "proformainvoices.db");
            }
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // BUG-13: Directory creation moved here — runs once per
            // context instance, not on every property access.
            // Shared-aware (2026-09-24): see AppDataRoot.EnsureCurrentFolder.
            ProGlassAutomation.Services.Database.AppDataRoot.EnsureCurrentFolder();

            // Senior audit (Aug 2026): WAL + busy_timeout + foreign_keys=ON
            // via SqliteResilience. Prevents lock contention between the PI
            // wizard save and the InquiryLifecycleService background reads.
            ProGlassAutomation.Services.Database.SqliteResilience
                .ConfigureFor(optionsBuilder, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SavedProformaInvoice>(entity =>
            {
                // Enforce ProformaId uniqueness (Phase 7.15 Q3 = Option A + C hybrid)
                entity.HasIndex(e => e.ProformaId).IsUnique();

                // Additional indexes for Load List UI (Phase 7.17)
                entity.HasIndex(e => e.InquiryId);
                entity.HasIndex(e => e.CompanyName);
                entity.HasIndex(e => e.CustomerName);
                entity.HasIndex(e => e.Status);
                entity.HasIndex(e => e.CreatedAt);

                // BUG-14: date-range indexes for filter queries
                entity.HasIndex(e => e.IssueDate)
                    .HasDatabaseName("IX_ProformaInvoices_IssueDate");
                entity.HasIndex(e => e.ValidUntil)
                    .HasDatabaseName("IX_ProformaInvoices_ValidUntil");
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}