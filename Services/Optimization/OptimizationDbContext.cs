// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Optimization/OptimizationDbContext.cs
// PROJECT: ProGlassAutomation
// MODULE: Optimization — persistence (optimization.db)
//
// Follows the house DbContext pattern (see InquiryDbContext): SQLite
// under %AppData%\ProGlassAutomation, every connection hardened via
// SqliteResilience (WAL, busy_timeout, foreign_keys).
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Optimization;
using ProGlassAutomation.Services.Database;
using System.IO;

namespace ProGlassAutomation.Services.Optimization
{
    public sealed class OptimizationDbContext : DbContext
    {
        public DbSet<SavedOptimizationSession> Sessions => Set<SavedOptimizationSession>();

        public static string DatabaseFilePath =>
            Path.Combine(AppDataRoot.CurrentFolder, "optimization.db");

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // Shared-aware folder ensure (2026-09-24) — see AppDataRoot.
            AppDataRoot.EnsureCurrentFolder();
            SqliteResilience.ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<SavedOptimizationSession>(e =>
            {
                e.HasKey(x => x.Id);
                e.HasIndex(x => x.CreatedUtc);
                e.Property(x => x.Name).IsRequired();
            });
        }
    }
}
