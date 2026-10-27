// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.b.3.ClientDbContext.cs
// MODULE: step2 — EF Core mapping for the Clients table.
//
// This context maps ONE table of the existing inquiries.db. The file path is
// taken from the established owner (InquiryDbContext.DatabaseFilePath) rather
// than recomputed, so there is a single source of truth for the ERP file.
//
// It deliberately does NOT call EnsureCreated: the ERP file's creation and
// version chain are owned by the Inquiry initializer (see step2.b.4). Two
// EnsureCreated owners on one file would race, because EF skips creation as
// soon as any table exists.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Clients;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;

namespace ProGlassAutomation.Services.Clients
{
    /// <summary>EF Core model for the client directory.</summary>
    public sealed class ClientDbContext : DbContext
    {
        public DbSet<Client> Clients => Set<Client>();

        /// <summary>Same SQLite file as the rest of the ERP.</summary>
        public static string DatabaseFilePath => InquiryDbContext.DatabaseFilePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            AppDataRoot.EnsureCurrentFolder();
            SqliteResilience.ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<Client>(e =>
            {
                e.ToTable("Clients");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();

                // Search/lookup indexes mirror step2.b.1.ClientSchema so a
                // database created by either path behaves identically. None of
                // these is unique: duplicate clients are warned about, not
                // blocked (owner decision D9).
                e.HasIndex(x => x.ClientName);
                e.HasIndex(x => x.Phone);
                e.HasIndex(x => x.Email);
                e.HasIndex(x => x.Trn);
                e.HasIndex(x => new { x.IsActive, x.ClientName });

                // 1014's reference into Users, indexed to mirror the migration so
                // a database created by either path has the same shape.
                e.HasIndex(x => x.RepresentativeUserId);
            });
        }
    }
}
