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
//   • There is NO password, hash, salt or token column. The application has no
//     credentialed login to support, and inventing one would be a fabricated
//     security feature. Identity is the Windows workstation account
//     (Inquiry/Services/Audit/CurrentOperator.cs) matched to a user row;
//     a password-based login is a separate, unapproved decision.
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
            OriginalIsActive = OriginalIsActive
        };
    }
}
