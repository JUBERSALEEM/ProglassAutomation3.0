// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/Audit/AppUser.cs
// PROJECT: ProGlassAutomation
// MODULE: Audit / User Management
//
// Represents one application user. Users are stored in the dedicated
// audit.db SQLite database (see Services/Audit/AuditDbContext.cs).
//
// Passwords are never stored in plain text — AuditService hashes them
// with a random per-user salt (SHA-256). This is a local desktop app,
// so the hash protects against casual inspection of the DB file.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.Audit
{
    /// <summary>
    /// One application user (Admin / Manager / Operator roles).
    /// Created from System Center → Users tab.
    /// </summary>
    [Table("AppUsers")]
    public class AppUser
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Login name. Unique (enforced by index in DbContext).</summary>
        [Required, MaxLength(60)]
        public string Username { get; set; } = "";

        /// <summary>Display name shown in the UI and audit trail.</summary>
        [Required, MaxLength(120)]
        public string DisplayName { get; set; } = "";

        /// <summary>"Admin" | "Manager" | "Operator".</summary>
        [Required, MaxLength(30)]
        public string Role { get; set; } = "Operator";

        /// <summary>
        /// Mobile number for the Sharing Center FROM identity
        /// ("Juber Saleem · Estimator · +971…"). Optional — blank means
        /// the composer shows name + role only. Added Phase 17; existing
        /// rows hydrate to "" via the audit.db migration.
        /// </summary>
        [MaxLength(40)]
        public string Mobile { get; set; } = "";

        /// <summary>
        /// Email for the Sharing Center FROM identity and mailto handoff.
        /// Optional — blank is normal. Added Phase 17.
        /// </summary>
        [MaxLength(200)]
        public string Email { get; set; } = "";

        /// <summary>SHA-256(salt + password) as hex. Set by AuditService.</summary>
        [Required, MaxLength(128)]
        public string PasswordHash { get; set; } = "";

        /// <summary>Random salt (hex) generated per user.</summary>
        [Required, MaxLength(64)]
        public string PasswordSalt { get; set; } = "";

        /// <summary>
        /// Inactive users stay in the audit history but cannot be
        /// selected as the active user. Never delete users — that would
        /// orphan their audit entries.
        /// </summary>
        public bool IsActive { get; set; } = true;

        /// <summary>Set explicitly by AuditService on insert.</summary>
        public DateTime CreatedAtUtc { get; set; }

        /// <summary>Last time this user was made the active user.</summary>
        public DateTime? LastActiveAtUtc { get; set; }

        /// <summary>
        /// Consecutive failed sign-in attempts. Persisted so the lockout
        /// survives an app restart (Phase 13). Reset to 0 on a successful
        /// sign-in and when a lockout window is triggered.
        /// </summary>
        public int FailedAttemptCount { get; set; }

        /// <summary>
        /// UTC instant before which this account rejects sign-ins because of
        /// too many consecutive failed attempts (Phase 13). Null = not locked.
        /// </summary>
        public DateTime? LockedUntilUtc { get; set; }
    }
}
