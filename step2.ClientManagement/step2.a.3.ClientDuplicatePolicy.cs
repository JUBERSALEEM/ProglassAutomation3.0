// ═══════════════════════════════════════════════════════════════════════
// FILE: step2.ClientManagement/step2.a.3.ClientDuplicatePolicy.cs
// MODULE: step2 — duplicate WARNINGS for the client directory.
//
// A duplicate warning is information for the operator, never an action:
//   • it never blocks a save,
//   • it never merges rows,
//   • it never rewrites an existing client,
//   • it never touches Inquiry history.
//
// There is deliberately no unique database index on the client name: two
// genuine clients can share a trading name (different legal entities, same
// group). See docs/05 — owner decision D9.
//
// Pure functions: no EF, no WPF, no I/O, so the policy is directly testable.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace ProGlassAutomation.Models.Clients
{
    /// <summary>A field on which a draft resembles an existing client.</summary>
    public enum ClientMatchField
    {
        Name = 0,
        Phone = 1,
        Email = 2,
        Trn = 3
    }

    /// <summary>Existing client data used for duplicate comparison (no tracking).</summary>
    public sealed class ClientDuplicateCandidate
    {
        public int Id { get; init; }
        public string ClientName { get; init; } = "";
        public string Phone { get; init; } = "";
        public string Email { get; init; } = "";
        public string Trn { get; init; } = "";
        public bool IsActive { get; init; } = true;
    }

    /// <summary>One existing client the draft resembles, with the reasons why.</summary>
    public sealed class ClientDuplicateMatch
    {
        public int ClientId { get; init; }
        public string ClientName { get; init; } = "";
        public bool IsActive { get; init; } = true;
        /// <summary>Field labels, e.g. "name", "phone", "e-mail", "TRN".</summary>
        public IReadOnlyList<string> Fields { get; init; } = Array.Empty<string>();
    }

    /// <summary>Duplicate detection rules.</summary>
    public static class ClientDuplicatePolicy
    {
        /// <summary>
        /// Compares a draft against known clients. <paramref name="excludeClientId"/>
        /// omits the row being edited so a client never matches itself.
        ///
        /// Matching is key-based (normalized name, phone digits, e-mail key,
        /// TRN digits); a blank draft field never matches anything.
        /// </summary>
        public static IReadOnlyList<ClientDuplicateMatch> Find(
            IEnumerable<ClientDuplicateCandidate> existing,
            ClientDraft draft,
            int? excludeClientId = null)
        {
            if (existing == null) return Array.Empty<ClientDuplicateMatch>();
            if (draft == null) throw new ArgumentNullException(nameof(draft));

            string nameKey = ClientRules.ComparisonKey(draft.ClientName);
            string phoneKey = ClientRules.PhoneKey(draft.Phone);
            string emailKey = ClientRules.EmailKey(draft.Email);
            string trnKey = ClientRules.TrnKey(draft.Trn);

            var matches = new List<ClientDuplicateMatch>();
            foreach (ClientDuplicateCandidate candidate in existing)
            {
                if (candidate == null) continue;
                if (excludeClientId.HasValue && candidate.Id == excludeClientId.Value) continue;

                var fields = new List<string>();
                if (nameKey.Length > 0 &&
                    string.Equals(nameKey, ClientRules.ComparisonKey(candidate.ClientName), StringComparison.Ordinal))
                    fields.Add("name");
                if (phoneKey.Length > 0 &&
                    string.Equals(phoneKey, ClientRules.PhoneKey(candidate.Phone), StringComparison.Ordinal))
                    fields.Add("phone");
                if (emailKey.Length > 0 &&
                    string.Equals(emailKey, ClientRules.EmailKey(candidate.Email), StringComparison.Ordinal))
                    fields.Add("e-mail");
                if (trnKey.Length > 0 &&
                    string.Equals(trnKey, ClientRules.TrnKey(candidate.Trn), StringComparison.Ordinal))
                    fields.Add("TRN");

                if (fields.Count == 0) continue;

                matches.Add(new ClientDuplicateMatch
                {
                    ClientId = candidate.Id,
                    ClientName = candidate.ClientName,
                    IsActive = candidate.IsActive,
                    Fields = fields
                });
            }

            // Strongest evidence first: a shared TRN/e-mail/phone is a firmer
            // signal than a shared name, then alphabetical for stability.
            return matches
                .OrderByDescending(match => match.Fields.Count)
                .ThenByDescending(match => match.Fields.Contains("TRN") ? 1 : 0)
                .ThenBy(match => match.ClientName, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>
        /// One operator-facing sentence listing the resemblance(s), or an empty
        /// string when there is nothing to warn about. The wording makes clear
        /// that saving is still the operator's decision.
        /// </summary>
        public static string DescribeWarning(IReadOnlyList<ClientDuplicateMatch> matches)
        {
            if (matches == null || matches.Count == 0) return "";

            var builder = new StringBuilder();
            builder.Append(matches.Count == 1
                ? "Possible duplicate: an existing client shares "
                : $"Possible duplicates: {matches.Count} existing clients share ");

            int shown = 0;
            foreach (ClientDuplicateMatch match in matches)
            {
                if (shown == 3) break;
                if (shown > 0) builder.Append("; ");
                builder.Append('"').Append(match.ClientName).Append("\" (")
                       .Append(string.Join(", ", match.Fields)).Append(')');
                if (!match.IsActive) builder.Append(" — inactive");
                shown++;
            }
            if (matches.Count > shown) builder.Append($"; and {matches.Count - shown} more");

            builder.Append(". This is a warning only — nothing was merged. Save again to keep this client separate.");
            return builder.ToString();
        }
    }
}
