using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using System;
using System.IO;

namespace ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository
{
    /// <summary>
    /// Separate SQLite database context for saved specifications.
    /// Stores all glass specs generated in Step 4 for reuse in Step 5.
    ///
    /// Database file: %AppData%\ProGlassAutomation\specifications.db
    /// Isolated from main proglass.db to keep spec library portable
    /// (can be exported/imported/shared between installations).
    /// </summary>
    public class SpecificationsDbContext : DbContext
    {
        public DbSet<SavedSpecification> Specifications { get; set; }

        /// <summary>
        /// Append-only record of every proforma that used a specification.
        /// See Models/Specifications/SpecificationUsage.cs.
        /// </summary>
        public DbSet<SpecificationUsage> SpecificationUsages { get; set; }

        /// <summary>Public accessor used by the schema migration runner.</summary>
        public static string DatabaseFilePath => Path.Combine(
            ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
            "specifications.db");

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            // Shared-aware folder ensure (2026-09-24) — see AppDataRoot.
            ProGlassAutomation.Services.Database.AppDataRoot.EnsureCurrentFolder();
            // Senior audit (Aug 2026): WAL + busy_timeout + foreign_keys=ON
            // via SqliteResilience. See SqliteResilience.cs.
            ProGlassAutomation.Services.Database.SqliteResilience
                .ConfigureFor(optionsBuilder, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<SavedSpecification>(entity =>
            {
                entity.HasKey(s => s.Id);

                entity.Property(s => s.GlassType)
                      .IsRequired()
                      .HasMaxLength(20);

                entity.Property(s => s.SpecName)
                      .IsRequired()
                      .HasMaxLength(500);

                // D-6: TEXT affinity for exact decimal storage (SQLite has no
                // native decimal; "decimal(18,2)" gives NUMERIC -> REAL float
                // storage, losing precision under SQL-side SUM). TEXT matches
                // the rest of the codebase. EnsureCreated means existing
                // databases are untouched; new ones get the corrected column.
                entity.Property(s => s.PricePerSqm)
                      .HasColumnType("TEXT");

                entity.Property(s => s.CreatedAt)
                      .IsRequired();

                entity.Property(s => s.LastUsedAt)
                      .IsRequired();

                // Index for fast filtering by GlassType (SGU/DGU/LAM lookups)
                entity.HasIndex(s => s.GlassType);

                // Index for spec name searches
                entity.HasIndex(s => s.SpecName);
            });

            modelBuilder.Entity<SpecificationUsage>(entity =>
            {
                entity.HasKey(u => u.Id);

                entity.Property(u => u.SpecName).IsRequired().HasMaxLength(500);
                entity.Property(u => u.GlassType).HasMaxLength(20);
                entity.Property(u => u.ProformaId).HasMaxLength(100);
                entity.Property(u => u.CompanyName).HasMaxLength(300);
                entity.Property(u => u.CustomerName).HasMaxLength(300);
                entity.Property(u => u.ProjectName).HasMaxLength(300);
                entity.Property(u => u.UserName).HasMaxLength(150);

                // Same TEXT affinity rationale as SavedSpecification.PricePerSqm:
                // SQLite has no native decimal and NUMERIC degrades to REAL,
                // losing precision on SQL-side aggregates.
                entity.Property(u => u.PricePerSqm).HasColumnType("TEXT");

                entity.Property(u => u.UsedAt).IsRequired();

                // "show me every use of this spec, newest first"
                entity.HasIndex(u => u.SpecificationId);
                entity.HasIndex(u => u.ProformaId);
            });
        }
    }
}