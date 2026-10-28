// ═══════════════════════════════════════════════════════════════════════
// FILE: step4.CompanyProfile/step4.b.3.CompanyProfileDbContext.cs
// MODULE: step4 — EF Core mapping for the CompanyProfile table.
//
// Same file, same rules as step2 and step3: one table of the existing
// inquiries.db, path taken from the established owner (InquiryDbContext), and
// NO EnsureCreated — the file's creation and version chain belong to the
// Inquiry initializer and the migration registry (step4.b.1 is 1016 on it).
//
// The key is ValueGeneratedNever: the id is the constant 1, not an identity.
// Letting EF treat it as generated is how a second row would appear.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Company;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;

namespace ProGlassAutomation.Services.Company
{
    /// <summary>EF Core model for the single company profile row.</summary>
    public sealed class CompanyProfileDbContext : DbContext
    {
        public DbSet<CompanyProfile> CompanyProfiles => Set<CompanyProfile>();

        /// <summary>Same SQLite file as the rest of the ERP.</summary>
        public static string DatabaseFilePath => InquiryDbContext.DatabaseFilePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            AppDataRoot.EnsureCurrentFolder();
            SqliteResilience.ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<CompanyProfile>(e =>
            {
                e.ToTable(CompanyProfileSchema.TableName);
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedNever();

                // The computed members are projections, not columns.
                e.Ignore(x => x.HasLogo);
            });
        }
    }
}
