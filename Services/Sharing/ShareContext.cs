// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Sharing/ShareContext.cs
// PROJECT: ProGlassAutomation
// MODULE: Sharing Center — universal sharing context (Stage 1)
//
// Everything the Sharing Center knows about ONE share, in one object:
// the document, the customer, the money, the company, and who prepared it.
// Built by the calling module from data it ALREADY HAS (saved entity +
// snapshot). Every property maps to a real ERP field — see the remarks;
// nothing here is invented.
//
// The context feeds three consumers: the contact resolver (TO candidates),
// the template service (message variables), and the composer (display).
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;

namespace ProGlassAutomation.Services.Sharing
{
    /// <summary>
    /// Document identity + customer + money + company + prepared-by for one
    /// share. All strings default to "" — consumers treat blank as unknown,
    /// never as an error.
    /// </summary>
    public sealed class ShareContext
    {
        // ── Document ──────────────────────────────────────────────
        // From the saved entity (ProformaId, JobOrderId, …) and the module
        // name. DocumentTypeKey is the short key used by document rules
        // and templates: PI, JO, DN, TI, Statement, OrderReport, Report,
        // Specifications, SheetStore, Inquiry.

        /// <summary>Short key: PI, JO, DN, TI, Statement, Report…</summary>
        public string DocumentTypeKey { get; set; } = "";

        /// <summary>Display name: Proforma Invoice, Job Order…</summary>
        public string DocumentTypeDisplay { get; set; } = "";

        /// <summary>
        /// Explicit display title (saved statement/report titles, list
        /// screens). When set, DisplayTitle shows it verbatim instead of
        /// composing type + number.
        /// </summary>
        public string Title { get; set; } = "";

        /// <summary>PI-2026-001, JO-2026-014… ("" for ad-hoc reports).</summary>
        public string DocumentNumber { get; set; } = "";

        /// <summary>Module that produced it (Proforma Invoice…).</summary>
        public string SourceModule { get; set; } = "";

        /// <summary>Issue / generation date of the document.</summary>
        public DateTime DocumentDate { get; set; }

        // ── Money ─────────────────────────────────────────────────
        // From the saved totals where the document HAS totals; reports
        // leave Amount null and templates skip the line.

        public decimal? Amount { get; set; }

        public string Currency { get; set; } = "";

        // ── Customer (TO side) ────────────────────────────────────
        // From ClientSnapshot / SavedClient. ClientContactPhone is the only
        // customer phone the ERP stores — there is no customer email field.

        public string CustomerName { get; set; } = "";

        public string AttentionName { get; set; } = "";

        public string CustomerPhone { get; set; } = "";

        /// <summary>
        /// Direct customer email (Tax Invoice snapshot carries one; most
        /// documents leave this blank and recipients resolve phone-only).
        /// </summary>
        public string CustomerEmail { get; set; } = "";

        public string SalesmanName { get; set; } = "";

        // ── Company (FROM organisation side) ──────────────────────
        // From CompanySnapshot / SavedCompany (Company Center).

        public string CompanyName { get; set; } = "";

        public string CompanyRepresentative { get; set; } = "";

        public string CompanyRepMobile { get; set; } = "";

        public string CompanyEmail { get; set; } = "";

        // ── Prepared by (FROM user side) ──────────────────────────
        // Preferred: the logged-in AppUser. Fallback: the document's
        // PreparedBySnapshot / DocumentPreparedBy string (older docs, or a
        // share opened by someone other than the preparer).

        /// <summary>Logged-in user id when known, else 0.</summary>
        public int PreparedByUserId { get; set; }

        public string PreparedByName { get; set; } = "";

        /// <summary>Role (AppUser) or department (snapshot).</summary>
        public string PreparedByRole { get; set; } = "";

        public string PreparedByMobile { get; set; } = "";

        public string PreparedByEmail { get; set; } = "";

        // ── Artifacts ─────────────────────────────────────────────
        // One or more validated artifacts. Providers that accept a single
        // attachment use the first and say so; the architecture always
        // carries the full list.

        public List<ShareableArtifact> Artifacts { get; } = new();

        /// <summary>
        /// Display title for the composer header, e.g.
        /// "Proforma Invoice · PI-2026-001".
        /// </summary>
        public string DisplayTitle
        {
            get
            {
                if (!string.IsNullOrWhiteSpace(Title))
                    return Title.Trim();
                if (!string.IsNullOrWhiteSpace(DocumentTypeDisplay)
                    && !string.IsNullOrWhiteSpace(DocumentNumber))
                    return $"{DocumentTypeDisplay} · {DocumentNumber}";
                if (!string.IsNullOrWhiteSpace(DocumentTypeDisplay))
                    return DocumentTypeDisplay;
                if (!string.IsNullOrWhiteSpace(DocumentNumber))
                    return DocumentNumber;
                return "Document";
            }
        }
    }
}
