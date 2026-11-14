using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.MasterData;
using System;
using System.IO;

namespace ProGlassAutomation.Services.MasterData.Database
{
    /// <summary>
    /// EF Core DbContext for the Master Data SQLite database: masterdata.db
    ///
    /// Location: %AppData%\ProGlassAutomation\masterdata.db
    /// (same folder as proformainvoices.db, joborders.db, deliverynotes.db)
    ///
    /// Tables:
    ///   Companies  — company profiles (name, TRN, location)
    ///   Clients    — customer profiles (name, TRN, billing, contact)
    ///   Projects   — project profiles (name, contract#, site, LPO)
    ///   Salesmen   — salesman lookup (name, department)
    ///
    /// Initialized in App.xaml.cs alongside other DbContexts:
    ///   App.MasterData = new MasterDataRepository();
    ///   await App.MasterData.InitializeAsync();
    ///
    /// Uniqueness enforced via composite indexes:
    ///   Companies: CompanyName + CompanyTrn (case-insensitive via COLLATE NOCASE)
    ///   Clients:   CustomerName + CustomerTrn
    ///   Projects:  ProjectIdentifierName + CustomerName
    ///   Salesmen:  Name
    ///
    /// Pattern matches ProformaInvoicesDbContext, JobOrdersDbContext,
    /// DeliveryNotesDbContext — DbPath pure computation, directory
    /// creation in OnConfiguring only.
    /// </summary>
    public class MasterDataDbContext : DbContext
    {
        public DbSet<SavedCompany> Companies { get; set; }
        public DbSet<SavedClient> Clients { get; set; }
        public DbSet<SavedProject> Projects { get; set; }
        public DbSet<SavedSalesman> Salesmen { get; set; }

        /// <summary>Production path — self-configures to masterdata.db.</summary>
        public MasterDataDbContext() { }

        /// <summary>
        /// PHASE 4 additive seam: externally-configured context so the
        /// repository's update semantics can be exercised against an
        /// isolated database (SQLite in-memory) in the regression suite.
        /// Production behaviour is untouched — the app only ever uses the
        /// parameterless constructor.
        /// </summary>
        public MasterDataDbContext(DbContextOptions<MasterDataDbContext> options)
            : base(options) { }

        // Pure computation — no side effects (matches Chat 12 pattern)
        private static string DbPath
        {
            get
            {
                return Path.Combine(
                    ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder,
                    "masterdata.db");
            }
        }

        /// <summary>Public accessor used by the schema migration runner.</summary>
        public static string DatabaseFilePath => DbPath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // Externally-configured context (tests inject SQLite in-memory):
            // options are already complete — never override them.
            if (options.IsConfigured) return;

            // Directory creation runs once per context creation —
            // shared-aware (2026-09-24): see AppDataRoot.
            ProGlassAutomation.Services.Database.AppDataRoot.EnsureCurrentFolder();

            // Senior audit (Aug 2026): WAL + busy_timeout + foreign_keys=ON
            // via SqliteResilience. See SqliteResilience.cs.
            ProGlassAutomation.Services.Database.SqliteResilience
                .ConfigureFor(options, DbPath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            // ── Companies table ──────────────────────────────────────────
            modelBuilder.Entity<SavedCompany>(e =>
            {
                // Composite unique: CompanyName + CompanyTrn
                e.HasIndex(x => new { x.CompanyName, x.CompanyTrn })
                    .IsUnique()
                    .HasDatabaseName("IX_Companies_Name_Trn");

                // Fast search by name alone
                e.HasIndex(x => x.CompanyName)
                    .HasDatabaseName("IX_Companies_Name");

                // Sort by usage (most-used cards first)
                e.HasIndex(x => x.UsageCount)
                    .HasDatabaseName("IX_Companies_UsageCount");

                e.HasIndex(x => x.LastUsedAt)
                    .HasDatabaseName("IX_Companies_LastUsedAt");
            });

            // ── Clients table ────────────────────────────────────────────
            modelBuilder.Entity<SavedClient>(e =>
            {
                // Composite unique: CustomerName + CustomerTrn
                e.HasIndex(x => new { x.CustomerName, x.CustomerTrn })
                    .IsUnique()
                    .HasDatabaseName("IX_Clients_Name_Trn");

                e.HasIndex(x => x.CustomerName)
                    .HasDatabaseName("IX_Clients_Name");

                e.HasIndex(x => x.UsageCount)
                    .HasDatabaseName("IX_Clients_UsageCount");

                e.HasIndex(x => x.LastUsedAt)
                    .HasDatabaseName("IX_Clients_LastUsedAt");

                // Search by salesman (future: salesman analytics)
                e.HasIndex(x => x.SalesRepresentative)
                    .HasDatabaseName("IX_Clients_SalesRep");
            });

            // ── Projects table ───────────────────────────────────────────
            modelBuilder.Entity<SavedProject>(e =>
            {
                // Composite unique: ProjectName + CustomerName
                // "Tower A" for "Apex LLC" ≠ "Tower A" for "Emirates LLC"
                e.HasIndex(x => new { x.ProjectIdentifierName, x.CustomerName })
                    .IsUnique()
                    .HasDatabaseName("IX_Projects_Name_Customer");

                e.HasIndex(x => x.ProjectIdentifierName)
                    .HasDatabaseName("IX_Projects_Name");

                e.HasIndex(x => x.CustomerName)
                    .HasDatabaseName("IX_Projects_Customer");

                e.HasIndex(x => x.UsageCount)
                    .HasDatabaseName("IX_Projects_UsageCount");

                e.HasIndex(x => x.LastUsedAt)
                    .HasDatabaseName("IX_Projects_LastUsedAt");
            });

            // ── Salesmen table ───────────────────────────────────────────
            modelBuilder.Entity<SavedSalesman>(e =>
            {
                // Unique by name alone
                e.HasIndex(x => x.Name)
                    .IsUnique()
                    .HasDatabaseName("IX_Salesmen_Name");

                e.HasIndex(x => x.UsageCount)
                    .HasDatabaseName("IX_Salesmen_UsageCount");
            });

            base.OnModelCreating(modelBuilder);
        }
    }
}