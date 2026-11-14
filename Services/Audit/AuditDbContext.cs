// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Audit/AuditDbContext.cs
// PROJECT: ProGlassAutomation
// MODULE: Audit / Activity Logging
//
// Dedicated SQLite DbContext for users + activity log.
// Database file: AppDataRoot.CurrentFolder\audit.db (local or shared LAN root)
//
// Kept isolated from all business databases so the audit trail can be
// backed up / inspected independently (same pattern as
// ProformaInvoicesDbContext).
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Audit;
using System;
using System.IO;

namespace ProGlassAutomation.Services.Audit
{
    /// <summary>
    /// EF Core context for audit.db (users + activity log).
    /// Create inside a using-block — contexts are short-lived by design.
    /// </summary>
    public class AuditDbContext : DbContext
    {
        public DbSet<AppUser> Users { get; set; }
        public DbSet<ActivityLogEntry> ActivityLog { get; set; }

        /// <summary>Pure path computation — no side effects.</summary>
        public static string DatabaseFilePath => Path.Combine(
            ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
            "audit.db");

        protected override void OnConfiguring(
            DbContextOptionsBuilder optionsBuilder)
        {
            // Shared-aware folder ensure (2026-09-24) — see AppDataRoot.
            ProGlassAutomation.Services.Database.AppDataRoot.EnsureCurrentFolder();

            // Senior audit (Aug 2026): audit.db is security-critical — WAL
            // + foreign_keys=ON + busy_timeout=5s guarantee no audit write is
            // lost to a transient lock, and cascade behaviour matches the model.
            ProGlassAutomation.Services.Database.SqliteResilience
                .ConfigureFor(optionsBuilder, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<AppUser>(entity =>
            {
                entity.HasIndex(e => e.Username).IsUnique();
            });

            modelBuilder.Entity<ActivityLogEntry>(entity =>
            {
                // Activity views always query by recency + section.
                entity.HasIndex(e => e.TimestampUtc)
                    .HasDatabaseName("IX_ActivityLog_TimestampUtc");
                entity.HasIndex(e => e.Section)
                    .HasDatabaseName("IX_ActivityLog_Section");
                entity.HasIndex(e => e.Username)
                    .HasDatabaseName("IX_ActivityLog_Username");
                entity.HasIndex(e => new { e.EntityType, e.EntityId })
                    .HasDatabaseName("IX_ActivityLog_Entity");
                entity.HasIndex(e => e.OperationId)
                    .HasDatabaseName("IX_ActivityLog_OperationId");
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}
