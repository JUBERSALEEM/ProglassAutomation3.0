// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.b.6.InvoiceAccess.cs
// MODULE: step6 — who may write a document, and what the row records.
//
// Attribution follows the model that resolves G1-06 for a transaction
// document: a STABLE USER ID plus the NAME AS IT READ WHEN THE DOCUMENT WAS
// SAVED. The id links and authorises; the name prints. The workstation login
// (CurrentOperator) is never identity — a shared "Reception" account cannot be
// the author of every quotation.
//
// Authorization is in the repository, not in a disabled button. The minimum
// this build enforces is a signed-in user: the session gate already stands
// before any page opens. A per-role table (who may issue, who may cancel) is
// an owner decision that has not been made, and docs/11 §4 forbids inventing
// one — so this file deliberately does not pretend to have it.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Users;
using System;

namespace ProGlassAutomation.Services.Proforma
{
    /// <summary>The signed-in person a write is attributed to.</summary>
    public sealed class InvoiceActor
    {
        public int? UserId { get; init; }
        public string Name { get; init; } = "";
        public bool IsAdministrator { get; init; }
    }

    /// <summary>Session gate and failure wording for every invoice write.</summary>
    public static class InvoiceAccess
    {
        /// <summary>
        /// The current user, or null with the sentence explaining why not.
        /// A session with no resolvable name is refused: a row that cannot say
        /// who wrote it is not an auditable record.
        /// </summary>
        public static InvoiceActor? Current(out string refusal)
        {
            refusal = "";
            var user = AuthenticationContext.Current;
            if (user == null)
            {
                refusal = "Sign in before creating or changing a proforma invoice.";
                return null;
            }

            string name = (user.FullName ?? "").Trim();
            if (name.Length == 0) name = (user.Username ?? "").Trim();
            if (name.Length == 0)
            {
                refusal = "The signed-in user has no name, so this document cannot record who wrote it. "
                    + "Complete the user's name in User Management and try again.";
                return null;
            }

            return new InvoiceActor
            {
                UserId = user.Id,
                Name = name,
                IsAdministrator = AuthenticationContext.IsAdministrator
            };
        }
    }
}
