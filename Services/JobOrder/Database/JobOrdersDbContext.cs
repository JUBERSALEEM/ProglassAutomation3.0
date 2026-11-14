using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.JobOrder.SavedJobOrder;
using System;
using System.IO;

namespace ProGlassAutomation.Services.JobOrder.Database
{
    /// <summary>
    /// EF Core DbContext for the joborders.db SQLite database.
    /// Isolated from proglass.db and proformainvoices.db per project
    /// architecture (one database per domain).
    ///
    /// Location: %AppData%\ProGlassAutomation\joborders.db
    ///
    /// Following project rule: new DbContext per operation via `using`,
    /// never held as a field. See JobOrderRepository.
    /// </summary>
    public class JobOrdersDbContext : DbContext
    {
        public DbSet<SavedJobOrder> JobOrders { get; set; }

        public static string DatabaseFilePath =>
            Path.Combine(ProGlassAutomation.Services.Database.AppDataRoot.CurrentFolder, "joborders.db");

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            // Shared-aware folder ensure (2026-09-24) — see AppDataRoot.
            ProGlassAutomation.Services.Database.AppDataRoot.EnsureCurrentFolder();
            // Senior audit (Aug 2026): WAL + busy_timeout + foreign_keys=ON
            // via SqliteResilience. See SqliteResilience.cs.
            ProGlassAutomation.Services.Database.SqliteResilience
                .ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            var entity = modelBuilder.Entity<SavedJobOrder>();

            // UNIQUE index on user-facing ID for duplicate detection
            entity.HasIndex(j => j.JobOrderId)
                  .IsUnique()
                  .HasDatabaseName("IX_JobOrder_JobOrderId_Unique");

            // Indexes for search + filter + sort performance
            entity.HasIndex(j => j.SourceProformaId)
                  .HasDatabaseName("IX_JobOrder_SourceProformaId");

            entity.HasIndex(j => j.CustomerName)
                  .HasDatabaseName("IX_JobOrder_CustomerName");

            entity.HasIndex(j => j.Status)
                  .HasDatabaseName("IX_JobOrder_Status");

            entity.HasIndex(j => j.Priority)
                  .HasDatabaseName("IX_JobOrder_Priority");

            entity.HasIndex(j => j.DeliveryDate)
                  .HasDatabaseName("IX_JobOrder_DeliveryDate");

            entity.HasIndex(j => j.CreatedAt)
                  .HasDatabaseName("IX_JobOrder_CreatedAt");

            entity.HasIndex(j => j.UpdatedAt)
                  .HasDatabaseName("IX_JobOrder_UpdatedAt");
        }
    }
}