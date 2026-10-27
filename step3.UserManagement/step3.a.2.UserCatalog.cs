// ═══════════════════════════════════════════════════════════════════════
// FILE: step3.UserManagement/step3.a.2.UserCatalog.cs
// MODULE: step3 — Departments and roles.
//
// Two fixed starting lists, defined once and used by the DDL seed, the
// repository validation and the editor's combo boxes. There is no second copy
// of these names anywhere in the module.
//
// Why catalogues and not an enum
//   • The owner asked that a department can be added later without a redesign.
//     An enum would freeze the list into compiled code and the database would
//     reject the new value until the app was rebuilt and re-shipped.
//   • A row in Departments / Roles is data. Adding one is an insert. The
//     unique index on the normalized code keeps the list tidy.
//   • Renaming is deliberately supported (DisplayName is editable); the stored
//     value on a user row is the stable code, so a rename relabels every user
//     without rewriting them.
//
// Why a department is never a permission
//   • Department answers "which team does this person sit in". Authorization
//     is asked of Role, and only for the rules actually specified (see
//     step3.a.3.UserAuthorization). Sales and Estimation having different
//     access in some future document flow does not make department a
//     permission switch here, and there is no code that treats it as one.
//
// Role list
//   • Administrator, Accountant, Salesman, Estimator, Foreman, Cutting
//     Department, Double Glazing Department — the owner's initial set.
//   • Estimator is the default for a new user (IsDefault), which is why
//     UserDraft and the editor both start there.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ProGlassAutomation.Models.Users
{
    /// <summary>One selectable catalogue value (a department or a role).</summary>
    public sealed class CatalogEntry
    {
        public CatalogEntry(string code, string displayName, bool isDefault = false)
        {
            Code = code;
            DisplayName = displayName;
            IsDefault = isDefault;
        }

        /// <summary>Stable identifier stored on user rows. Never renamed.</summary>
        public string Code { get; }

        /// <summary>Wording shown to the operator. May be reworded without touching users.</summary>
        public string DisplayName { get; }

        /// <summary>True for the value a new user starts with.</summary>
        public bool IsDefault { get; }

        public override string ToString() => DisplayName;
    }

    /// <summary>The fixed starting departments and roles, plus the lookup rules around them.</summary>
    public static class UserCatalog
    {
        /// <summary>Shared column length for a catalogue value or code.</summary>
        public const int ValueMaxLength = 80;

        public static readonly IReadOnlyList<CatalogEntry> Departments = new[]
        {
            new CatalogEntry("ADMINISTRATION", "Administration"),
            new CatalogEntry("ACCOUNTS", "Accounts"),
            new CatalogEntry("SALES", "Sales"),
            new CatalogEntry("ESTIMATION", "Estimation"),
            new CatalogEntry("PRODUCTION", "Production"),
            new CatalogEntry("CUTTING", "Cutting Department"),
            new CatalogEntry("DOUBLE_GLAZING", "Double Glazing Department")
        };

        public static readonly IReadOnlyList<CatalogEntry> Roles = new[]
        {
            new CatalogEntry("ADMINISTRATOR", "Administrator"),
            new CatalogEntry("ACCOUNTANT", "Accountant"),
            new CatalogEntry("SALESMAN", "Salesman"),
            new CatalogEntry("ESTIMATOR", "Estimator", isDefault: true),
            new CatalogEntry("FOREMAN", "Foreman"),
            new CatalogEntry("CUTTING", "Cutting Department"),
            new CatalogEntry("DOUBLE_GLAZING", "Double Glazing Department")
        };

        /// <summary>The role a new user receives when nobody chooses otherwise.</summary>
        public const string DefaultRole = "ESTIMATOR";

        /// <summary>Roles are keyed by code; the default department is the estimation team.</summary>
        public const string DefaultDepartment = "ESTIMATION";

        /// <summary>Department a first administrator is placed in unless they choose otherwise.</summary>
        public const string AdministrationDepartment = "ADMINISTRATION";

        /// <summary>The only role with user-management authority in this stage.</summary>
        public const string AdministratorRole = "ADMINISTRATOR";

        /// <summary>
        /// The role a client's representative is drawn from. Client Management
        /// reads this constant instead of typing the code, so the one catalogue
        /// stays the single source of the role vocabulary.
        /// </summary>
        public const string SalesmanRole = "SALESMAN";

        private static readonly string[] KnownRoles =
            Roles.Select(entry => entry.Code).ToArray();

        private static readonly string[] KnownDepartments =
            Departments.Select(entry => entry.Code).ToArray();

        /// <summary>True when the code is one of the shipped departments.</summary>
        public static bool IsKnownDepartment(string? code) =>
            !string.IsNullOrWhiteSpace(code) && KnownDepartments.Contains(code.Trim(), StringComparer.Ordinal);

        /// <summary>True when the code is one of the shipped roles.</summary>
        public static bool IsKnownRole(string? code) =>
            !string.IsNullOrWhiteSpace(code) && KnownRoles.Contains(code.Trim(), StringComparer.Ordinal);

        /// <summary>True when the role is Administrator.</summary>
        public static bool IsAdministrator(string? role) =>
            string.Equals((role ?? "").Trim(), AdministratorRole, StringComparison.Ordinal);

        /// <summary>Display wording for a department code; falls back to the code itself.</summary>
        public static string DepartmentDisplayName(string? code)
        {
            string key = (code ?? "").Trim();
            var entry = Departments.FirstOrDefault(d => string.Equals(d.Code, key, StringComparison.Ordinal));
            return entry?.DisplayName ?? key;
        }

        /// <summary>Display wording for a role code; falls back to the code itself.</summary>
        public static string RoleDisplayName(string? code)
        {
            string key = (code ?? "").Trim();
            var entry = Roles.FirstOrDefault(r => string.Equals(r.Code, key, StringComparison.Ordinal));
            return entry?.DisplayName ?? key;
        }

        /// <summary>
        /// Catalogue code from operator wording, or "" when it matches nothing.
        /// Accepts either the code or the display name so the editor, a seed
        /// row and a test can all speak naturally.
        /// </summary>
        public static string DepartmentCode(string? text) => Resolve(Departments, text);

        /// <summary>Role code from operator wording, or "" when it matches nothing.</summary>
        public static string RoleCode(string? text) => Resolve(Roles, text);

        private static string Resolve(IReadOnlyList<CatalogEntry> entries, string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string key = text!.Trim();
            foreach (var entry in entries)
            {
                if (string.Equals(entry.Code, key, StringComparison.Ordinal)) return entry.Code;
                if (string.Equals(entry.DisplayName, key, StringComparison.OrdinalIgnoreCase)) return entry.Code;
            }
            return "";
        }

        /// <summary>
        /// The code to persist for a catalogue column: the resolved code when the
        /// text names a known value, otherwise the original text so an
        /// administrator-added row is not silently lost.
        /// </summary>
        public static string CoerceCode(IReadOnlyList<CatalogEntry> entries, string? text, string fallback)
        {
            if (string.IsNullOrWhiteSpace(text)) return fallback;
            string key = text!.Trim();
            foreach (var entry in entries)
            {
                if (string.Equals(entry.Code, key, StringComparison.Ordinal)) return entry.Code;
                if (string.Equals(entry.DisplayName, key, StringComparison.OrdinalIgnoreCase)) return entry.Code;
            }
            return key;
        }

        /// <summary>Uppercase comparison key for a catalogue code (matches the unique index).</summary>
        public static string CodeKey(string? text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "";
            string trimmed = text!.Trim();
            var builder = new StringBuilder(trimmed.Length);
            foreach (char character in trimmed)
            {
                if (char.IsWhiteSpace(character))
                {
                    if (builder.Length > 0 && builder[^1] != '_') builder.Append('_');
                    continue;
                }
                builder.Append(char.ToUpperInvariant(character));
            }
            return builder.ToString().Trim('_');
        }
    }
}
