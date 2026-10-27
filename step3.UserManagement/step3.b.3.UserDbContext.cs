// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.b.3.UserDbContext.cs
// MODULE: step3 — EF Core mapping for the Users table.
//
// This context maps ONE table of the existing inquiries.db. The file path is
// taken from the established owner (InquiryDbContext.DatabaseFilePath) rather
// than recomputed, so there is a single source of truth for the ERP file.
//
// It deliberately does NOT call EnsureCreated: the ERP file's creation and
// version chain are owned by the Inquiry initializer (see step3.b.4). Two
// EnsureCreated owners on one file would race, because EF skips creation as
// soon as any table exists.
//
// The Departments and Roles catalogues are read with plain SQL in the
// repository rather than mapped: they are lookup lists with a string key and
// no entity behaviour, and mapping them would add change tracking the module
// never needs. The Users table remains the only tracked entity.
// ═══════════════════════════════════════════════════════════════════════

using Microsoft.EntityFrameworkCore;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Database;
using ProGlassAutomation.Services.Inquiry;

namespace ProGlassAutomation.Services.Users
{
    /// <summary>EF Core model for the user directory.</summary>
    public sealed class UserDbContext : DbContext
    {
        public DbSet<User> Users => Set<User>();

        /// <summary>Same SQLite file as the rest of the ERP.</summary>
        public static string DatabaseFilePath => InquiryDbContext.DatabaseFilePath;

        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            AppDataRoot.EnsureCurrentFolder();
            SqliteResilience.ConfigureFor(options, DatabaseFilePath);
        }

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.Entity<User>(e =>
            {
                e.ToTable("Users");
                e.HasKey(x => x.Id);
                e.Property(x => x.Id).ValueGeneratedOnAdd();

                // Mirrors step3.b.1.UserSchema so a database created by either
                // path behaves identically. NormalizedUsername is the only
                // unique key: one workstation account must not map to two rows.
                e.HasIndex(x => x.FullName);
                e.HasIndex(x => x.Department);
                e.HasIndex(x => x.Role);
                e.HasIndex(x => new { x.IsActive, x.FullName });
                e.HasIndex(x => x.NormalizedUsername).IsUnique();
            });
        }
    }
}
