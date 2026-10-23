// ═══════════════════════════════════════════════════════════════════════
// FILE: Inquiry/Services/Audit/CurrentOperator.cs
// MODULE: Audit — workstation operator identity (V3).
//
// The legacy build stamped records through an authenticated audit user.
// V3 starts single-station without the licensing/role module, so the
// operator is the workstation account. Stamping still flows through the
// same repository fields (PreparedByName / UpdatedBy / ActorName) so
// the audit columns are populated and the later role module can pick
// them up unchanged.
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.Audit
{
    /// <summary>
    /// The workstation operator for stamping created/changed-by fields.
    /// </summary>
    public static class CurrentOperator
    {
        /// <summary>
        /// Domain\user when a domain name is present, otherwise the plain
        /// user name. Falls back to "Operator" on hosts where no user is
        /// resolvable (e.g. some CI sandboxes) so stamps stay non-empty.
        /// </summary>
        public static string Username
        {
            get
            {
                try
                {
                    string domain = Environment.UserDomainName;
                    string user = Environment.UserName;
                    if (string.IsNullOrWhiteSpace(user)) return "Operator";
                    if (string.IsNullOrWhiteSpace(domain)) return user;
                    return $"{domain}\\{user}";
                }
                catch
                {
                    return "Operator";
                }
            }
        }

        /// <summary>Display name used in stamps (same as Username for now).</summary>
        public static string DisplayName => Username;
    }
}
