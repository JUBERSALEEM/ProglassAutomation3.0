// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareRecipient.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — recipient model (Stage 1)
//
// One selectable TO entry. Recipients are RESOLVED from ERP data by
// IContactResolver — never typed by the operator except through the
// explicit manual fallback. Only kinds that exist in the data model are
// offered (there is deliberately no Boss/Supplier: no such entities).
//
// A recipient may carry a phone, an email, both, or (Salesman) neither —
// providers enable themselves from what is present, so a name-only
// salesman is still selectable for a future provider that needs no phone.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Services.Sharing
{
    /// <summary>
    /// Where the recipient came from. Order = default preference order.
    /// </summary>
    public enum ShareRecipientKind
    {
        /// <summary>Customer attention contact (name + customer phone).</summary>
        CustomerAttention,

        /// <summary>Customer primary contact.</summary>
        Customer,

        /// <summary>Own-company representative (internal forwarding).</summary>
        CompanyRepresentative,

        /// <summary>Salesman on the document (name; phone rarely known).</summary>
        Salesman,

        /// <summary>Operator-typed fallback (explicit user choice).</summary>
        Manual
    }

    /// <summary>One resolved TO candidate.</summary>
    public sealed class ShareRecipient
    {
        public ShareRecipientKind Kind { get; }

        /// <summary>Person or contact name (Ahmed Khan).</summary>
        public string Name { get; }

        /// <summary>Organisation context (ABC Glass).</summary>
        public string Organisation { get; }

        /// <summary>Phone as stored (may need normalization).</summary>
        public string Phone { get; }

        /// <summary>Email as stored (often blank — ERP stores few).</summary>
        public string Email { get; }

        public ShareRecipient(
            ShareRecipientKind kind,
            string name,
            string organisation = "",
            string phone = "",
            string email = "")
        {
            Kind = kind;
            Name = (name ?? "").Trim();
            Organisation = (organisation ?? "").Trim();
            Phone = (phone ?? "").Trim();
            Email = (email ?? "").Trim();
        }

        public bool HasPhone => !string.IsNullOrWhiteSpace(Phone);

        public bool HasEmail => !string.IsNullOrWhiteSpace(Email);

        /// <summary>"Customer Attention — Ahmed Khan". Localize at display.</summary>
        public string KindDisplay => Kind switch
        {
            ShareRecipientKind.CustomerAttention => "Customer Attention",
            ShareRecipientKind.Customer => "Customer",
            ShareRecipientKind.CompanyRepresentative => "Company Representative",
            ShareRecipientKind.Salesman => "Salesman",
            _ => "Other Contact"
        };

        /// <summary>Best single contact line for display (phone, else email).</summary>
        public string ContactLine =>
            HasPhone ? Phone : HasEmail ? Email : "—";

        /// <summary>
        /// Audit-safe identity: name + kind + masked phone. Full numbers
        /// are PII and do not belong in the trail; last-4 is enough to
        /// distinguish "which number" when it matters.
        /// </summary>
        public string AuditIdentity
        {
            get
            {
                string masked = MaskPhone(Phone);
                return string.IsNullOrWhiteSpace(masked)
                    ? $"{KindDisplay}: {Name}"
                    : $"{KindDisplay}: {Name} ({masked})";
            }
        }

        public static string MaskPhone(string? phone)
        {
            if (string.IsNullOrWhiteSpace(phone)) return "";
            string digits = "";
            foreach (char c in phone)
                if (char.IsDigit(c)) digits += c;
            if (digits.Length <= 4) return "••••";
            return "••••" + digits.Substring(digits.Length - 4);
        }
    }
}
