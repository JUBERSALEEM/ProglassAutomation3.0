// ═══════════════════════════════════════════════════════════════════════
// FILE: step5.ProjectRecords/step5.b.2.ProjectDbContext.cs
// MODULE: step5 — EF Core mapping for the Projects table.
//
// This context maps ONE table of the existing inquiries.db. The file path is
// taken from the established owner (InquiryDbContext.DatabaseFilePath) rather
// than recomputed, so there is a single source of truth for the ERP file.
//
// It deliberately does NOT call EnsureCreated: the ERP file's creation and
// version chain are owned by the Inquiry initializer (see step5.b.4). Two
// EnsureCreated owners on one file would race, because EF skips creation as
// soon as any table exists.
//
// The inquiry link is NOT mapped as an entity here. `Inquiries` belongs to the
// Inquiry module, and mapping a second, partial entity over it would let this
// context insert an inquiry with every column but the one being written. The
// link goes through a parameterised UPDATE instead (step5.b.4), which touches
// exactly one column and cannot create a row.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Projects;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;

namespace ProGlassAutomation.Services.Projects
{
    /// <summary>EF Core model for project records.</summary>
    public sealed class ProjectDbContext : DbContext
    {
        public DbSet<Project> Projects => Set<Project>();

        /// <summary>Same SQLite file as the rest of the ERP.</summary>
        public static string DatabaseFilePath => InquiryDbContext.DatabaseFilePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            AppDataRoot.EnsureCurrentFolder();
            SqliteResilience.ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Project>(e =>
            {
                e.ToTable(ProjectSchema.TableName);
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();

                // Indexes mirror step5.b.1.ProjectSchema so a database created by
                // either path has the same shape. The number index is unique: the
                // database refuses a duplicate, it does not merely warn.
                e.HasIndex(x => x.ProjectNumber).IsUnique();
                e.HasIndex(x => x.ClientId);
                e.HasIndex(x => new { x.Status, x.ProjectName });
            });
        }
    }
}
