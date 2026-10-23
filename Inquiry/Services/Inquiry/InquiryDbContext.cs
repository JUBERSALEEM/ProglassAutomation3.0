// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Inquiry/InquiryDbContext.cs
// MODULE: Inquiry — EF Core model for inquiries.db (SQLite).
//
// Schema owner for the Inquiry tables. Fresh databases are created by
// EnsureCreated at the current model; existing legacy databases are
// converged by the AppSchemaMigrations.Inquiries registry (see
// InquiryRepository.InitializeAsync).
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Inquiry;
using ProGlassAutomation.Services.Database;
using System.IO;

namespace ProGlassAutomation.Services.Inquiry
{
    public sealed class InquiryDbContext : DbContext
    {
        public DbSet<InquiryRecord> Inquiries => Set<InquiryRecord>();
        public DbSet<InquiryAttachment> Attachments => Set<InquiryAttachment>();
        public DbSet<InquiryCommunication> Communications => Set<InquiryCommunication>();
        public DbSet<InquiryLineItem> LineItems => Set<InquiryLineItem>();
        public static string DatabaseFilePath => Path.Combine(AppDataRoot.CurrentFolder, "inquiries.db");

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // Shared-aware folder ensure.
            AppDataRoot.EnsureCurrentFolder();
            // Route every SQLite connection through SqliteResilience so
            // WAL, busy_timeout=5s, foreign_keys=ON and the other
            // hardening pragmas are applied on every physical connection.
            // Prevents "database is locked" during concurrent Inquiry
            // save + lifecycle hydration, and enforces the cascade
            // deletes declared in OnModelCreating below.
            SqliteResilience.ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<InquiryRecord>(e =>
            {
                e.Property(x => x.RowVersion)
                    .IsConcurrencyToken()
                    .ValueGeneratedNever();
                e.HasIndex(x => x.InquiryNumber).IsUnique();
                e.HasIndex(x => x.ReceivedAt); e.HasIndex(x => x.UpdatedAt);
                e.HasIndex(x => x.Status); e.HasIndex(x => x.CustomerId);
                e.HasIndex(x => x.SalesmanId); e.HasIndex(x => x.PreparedByUserId);
                e.HasMany(x => x.Attachments).WithOne(x => x.Inquiry)
                    .HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.Communications).WithOne(x => x.Inquiry)
                    .HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Cascade);
                e.HasMany(x => x.LineItems).WithOne(x => x.Inquiry)
                    .HasForeignKey(x => x.InquiryId).OnDelete(DeleteBehavior.Cascade);
            });
            model.Entity<InquiryAttachment>(e =>
            {
                e.HasIndex(x => x.InquiryId); e.HasIndex(x => x.UploadedAt);
            });
            model.Entity<InquiryCommunication>(e =>
            {
                e.HasIndex(x => new { x.InquiryId, x.OccurredAt });
            });
            model.Entity<InquiryLineItem>(e =>
            {
                e.HasIndex(x => x.InquiryId);
                e.HasIndex(x => new { x.InquiryId, x.SortOrder });
            });
        }
    }
}
