// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.a.1.User.cs
// MODULE: step3 — User Management, domain model.
//
// One row of the desktop application's user directory. Persisted in the
// existing inquiries.db (step3.b), created by migration 1013.
//
// Deliberate design decisions (owner-approved 2026-10-27):
//   • Id is the only identifier other modules may reference. The name and
//     the username are data, never keys. Documents will store the id.
//   • Username is the login identifier as entered. NormalizedUsername is the
//     uppercase, trimmed comparison key that carries the unique index, so
//     "A.Salam" and "a.salam" cannot both exist.
//   • Department and Role are separate concepts and are stored separately.
//     A department is the team a person belongs to; a role is what they are
//     authorized to do. Two people in Sales can hold different roles, and a
//     role never implies a department.
//   • Every user signs in with a password. What is stored is a PBKDF2 hash, a
//     per-user salt and the work factor — never the password itself, and never
//     anything reversible (step3.a.5). Migration 1015 adds those columns; a row
//     created before it exists has an empty credential and cannot sign in until
//     an administrator sets one.
//     (Until 2026-10-27 identity was the Windows workstation account alone.
//     The owner rejected that: "user should have password so user should be able
//     to read the data related to them only".)
//   • IsActive replaces deletion: a person who leaves the company is
//     deactivated, never deleted, so historical attribution keeps a target.
//   • Timestamps are stored in UTC and displayed locally, matching the rest of
//     the application.
//   • CreatedByUserId / UpdatedByUserId record which administrator granted the
//     access, as a stable id. This is the module's own working example of the
//     Prepared-By rule: an id is persisted, the display name is looked up, and
//     no typed name ever enters the field.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace ProGlassAutomation.Models.Users
{
    /// <summary>One person who uses the ProGlass desktop application.</summary>
    [Table("Users")]
    public sealed class User
    {
        [Key]
        public int Id { get; set; }

        /// <summary>Full name of the person. Required.</summary>
        [Required, MaxLength(UserRules.FullNameMaxLength)]
        public string FullName { get; set; } = "";

        /// <summary>
        /// Login identifier as entered (display casing retained). Required and
        /// unique. On a single-station install this is normally the Windows
        /// account name, which is what links the row to the operator.
        /// </summary>
        [Required, MaxLength(UserRules.UsernameMaxLength)]
        public string Username { get; set; } = "";

        /// <summary>
        /// Uppercase, whitespace-trimmed comparison key for <see cref="Username"/>.
        /// Unique index lives here so case variants cannot duplicate a person.
        /// </summary>
        [Required, MaxLength(UserRules.UsernameMaxLength)]
        public string NormalizedUsername { get; set; } = "";

        /// <summary>Organisational team, from <see cref="UserCatalog.Departments"/>.</summary>
        [Required, MaxLength(UserCatalog.ValueMaxLength)]
        public string Department { get; set; } = UserCatalog.DefaultDepartment;

        /// <summary>Business role, from <see cref="UserCatalog.Roles"/>. Authorizes nothing by itself.</summary>
        [Required, MaxLength(UserCatalog.ValueMaxLength)]
        public string Role { get; set; } = UserCatalog.DefaultRole;

        /// <summary>False for a person who has left. Inactive users cannot act.</summary>
        public bool IsActive { get; set; } = true;

        public DateTime CreatedAt { get; set; }

        public DateTime UpdatedAt { get; set; }

        /// <summary>
        /// Stable id of the administrator who added this row. Null only for the
        /// first-run bootstrap row, which had no earlier administrator to name.
        /// Stored as an id, never as a name: renaming a person must not rewrite
        /// history, and a name is not an identifier.
        /// </summary>
        public int? CreatedByUserId { get; set; }

        /// <summary>Stable id of the administrator who last changed this row.</summary>
        public int? UpdatedByUserId { get; set; }

        // ── Credentials (migration 1015) ─────────────────────────────────
        // Only the hash, its salt and the work factor are stored. See
        // step3.a.5.UserPassword for how they are produced and checked.

        /// <summary>PBKDF2-HMAC-SHA256 hash, base64. Empty = no credential yet.</summary>
        [MaxLength(128)]
        public string PasswordHash { get; set; } = "";

        /// <summary>Per-user random salt, base64.</summary>
        [MaxLength(64)]
        public string PasswordSalt { get; set; } = "";

        /// <summary>Work factor used for <see cref="PasswordHash"/>.</summary>
        public int PasswordIterations { get; set; }

        /// <summary>When the password was last set, UTC.</summary>
        public DateTime? PasswordUpdatedAt { get; set; }

        /// <summary>
        /// True when an administrator set the password (or reset it): the person
        /// must choose their own before the directory treats them as fully set up.
        /// </summary>
        public bool MustChangePassword { get; set; }

        /// <summary>Consecutive failed sign-ins; reset on success.</summary>
        public int FailedSignInCount { get; set; }

        /// <summary>No sign-in is accepted before this instant, UTC.</summary>
        public DateTime? LockedUntilUtc { get; set; }

        /// <summary>True when a credential exists, so the editor can say so.</summary>
        [NotMapped]
        public bool HasPassword => !string.IsNullOrWhiteSpace(PasswordHash) &&
                                   !string.IsNullOrWhiteSpace(PasswordSalt) &&
                                   PasswordIterations > 0;

        /// <summary>"Active" / "Inactive" for the register and status badge.</summary>
        [NotMapped]
        public string StatusText => IsActive ? "Active" : "Inactive";

        /// <summary>"Estimator · Estimation" — role first, because role decides what may be done.</summary>
        [NotMapped]
        public string RoleDepartmentText => $"{Role} · {Department}";
    }

    /// <summary>
    /// Editable values of a user, independent of persistence. The editor works
    /// on a draft so a cancelled edit can never leak half-applied values into
    /// the tracked entity.
    /// </summary>
    public sealed class UserDraft
    {
        public int Id { get; init; }

        public string FullName { get; set; } = "";

        public string Username { get; set; } = "";

        public string Department { get; set; } = UserCatalog.DefaultDepartment;

        public string Role { get; set; } = UserCatalog.DefaultRole;

        public bool IsActive { get; set; } = true;

        /// <summary>Set when the row is loaded for editing; enables "last active administrator" checks.</summary>
        public string OriginalRole { get; init; } = "";

        public bool OriginalIsActive { get; init; } = true;

        // ── Passwords ────────────────────────────────────────────────────
        // The draft carries a password only when one is being set; it is never
        // read back from a stored row, so an edit cannot leak or re-save a hash.

        /// <summary>A new password to set, or "" to leave the credential alone.</summary>
        public string Password { get; set; } = "";

        /// <summary>Confirmation of <see cref="Password"/>; never stored.</summary>
        public string PasswordConfirm { get; set; } = "";

        /// <summary>
        /// The current password, required when a person changes their own
        /// credential (an administrator resetting someone else's does not need it).
        /// </summary>
        public string CurrentPassword { get; set; } = "";

        public static UserDraft From(User user) => new()
        {
            Id = user.Id,
            FullName = user.FullName,
            Username = user.Username,
            Department = user.Department,
            Role = user.Role,
            IsActive = user.IsActive,
            OriginalRole = user.Role,
            OriginalIsActive = user.IsActive
        };

        public UserDraft Clone() => new()
        {
            Id = Id,
            FullName = FullName,
            Username = Username,
            Department = Department,
            Role = Role,
            IsActive = IsActive,
            OriginalRole = OriginalRole,
            OriginalIsActive = OriginalIsActive,
            Password = Password,
            PasswordConfirm = PasswordConfirm,
            CurrentPassword = CurrentPassword
        };
    }
}
