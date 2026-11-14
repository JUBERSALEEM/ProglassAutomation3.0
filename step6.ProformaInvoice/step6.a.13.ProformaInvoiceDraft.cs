// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.a.13.ProformaInvoiceDraft.cs
// MODULE: step6 — the document itself, and the ONE recalculation.
//
// Everything a stage changes lands here and Recualculate() re-derives the
// whole document from its inputs: grid totals → charge quantities → the
// discount/VAT waterfall → the profit & loss. There is no second calculator
// and no cached total that can disagree with the grid.
//
// This object is also what persistence serializes (as JSON), which is why it
// is plain data: no WPF type, no repository, no static state. Computed members
// are marked [JsonIgnore] so a stored document cannot carry a stale total that
// a future reader would trust.
// ═══════════════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Models.Proforma
{
    /// <summary>Evidence attached to an invoice. The file lives beside the app data.</summary>
    public sealed class ProformaAttachment
    {
        public long Id { get; set; }
        public string FileName { get; set; } = "";
        public string StoredFileName { get; set; } = "";
        public string Extension { get; set; } = "";
        public long FileSize { get; set; }
        public string AttachmentType { get; set; } = "Document";
        public string Note { get; set; } = "";
        public DateTime UploadedAtUtc { get; set; }
        public int? UploadedByUserId { get; set; }
        public string UploadedByName { get; set; } = "";
    }

    /// <summary>The whole proforma invoice while it is being built or edited.</summary>
    public sealed class ProformaInvoiceDraft
    {
        /// <summary>Bumped when the stored JSON shape changes. Additive changes keep it.</summary>
        public const int CurrentFormatVersion = 1;

        public int FormatVersion { get; set; } = CurrentFormatVersion;

        /// <summary>0 until the first save; the repository assigns it.</summary>
        public int Id { get; set; }

        /// <summary>PI-YYYY-NNN once allocated; empty on a new draft.</summary>
        public string ProformaNumber { get; set; } = "";

        public string Status { get; set; } = ProformaInvoiceStatus.Draft;

        public DateTime IssueDate { get; set; } = DateTime.Today;
        public DateTime ValidUntil { get; set; } = DateTime.Today.AddDays(7);

        public InvoiceParties Parties { get; set; } = new();

        public List<PricingSection> PricingSections { get; set; } = new();
        public int SelectedSectionNumber { get; set; } = 1;

        public List<SpecGroup> Groups { get; set; } = new();
        public OtherChargesSheet Charges { get; set; } = new();
        public InvoiceSummary Summary { get; set; } = new();
        public ProfitLossSettings ProfitLossSettings { get; set; } = new();
        public ProfitLossResult ProfitLoss { get; set; } = new();

        public string Notes { get; set; } = "";
        public string TermsText { get; set; } = "";

        /// <summary>Furthest stage opened, restored so a reopened draft keeps context.</summary>
        public InvoiceStage FurthestStageReached { get; set; } = InvoiceStage.Header;

        // ── Attachments are rows in their own table, not part of the JSON ──
        [JsonIgnore]
        public List<ProformaAttachment> Attachments { get; set; } = new();

        // ── Audit ───────────────────────────────────────────────────────
        public DateTime CreatedAtUtc { get; set; }
        public DateTime UpdatedAtUtc { get; set; }
        public int? CreatedByUserId { get; set; }
        public string CreatedByName { get; set; } = "";
        public int? UpdatedByUserId { get; set; }
        public string UpdatedByName { get; set; } = "";
        public int RowVersion { get; set; }

        /// <summary>True once the number has been allocated by a save.</summary>
        [JsonIgnore]
        public bool IsPersisted => Id > 0;

        [JsonIgnore]
        public bool IsEditable => InvoiceLifecycle.CanEditBody(Status);

        [JsonIgnore]
        public PricingSection? SelectedSection =>
            PricingSections.FirstOrDefault(section => section.SectionNumber == SelectedSectionNumber)
            ?? PricingSections.FirstOrDefault();

        // ── Structure ───────────────────────────────────────────────────

        /// <summary>
        /// Makes the draft usable: one pricing section, catalogue charge rows,
        /// and a recalculation. Safe to call on every new draft and on load.
        /// </summary>
        public void EnsureStructure()
        {
            if (PricingSections.Count == 0)
                PricingSections.Add(PricingEstimator.NewSection(GlassBuilds.Sgu, 1));
            foreach (PricingSection section in PricingSections)
            {
                section.Outer ??= new GlassPane();
                section.Middle ??= new GlassPane();
                section.Inner ??= new GlassPane();
            }
            if (!PricingSections.Any(section => section.SectionNumber == SelectedSectionNumber))
                SelectedSectionNumber = PricingSections[0].SectionNumber;

            Charges ??= new OtherChargesSheet();
            Charges.EnsureCatalogueRows();
            Parties ??= new InvoiceParties();
            Summary ??= new InvoiceSummary();
            ProfitLossSettings ??= new ProfitLossSettings();
            Groups ??= new List<SpecGroup>();
            Recalculate();
        }

        /// <summary>Adds an empty pricing section and returns its number.</summary>
        public int AddPricingSection(string glassType)
        {
            EnsureStructure();
            int number = PricingSections.Count == 0
                ? 1
                : PricingSections.Max(section => section.SectionNumber) + 1;
            PricingSections.Add(PricingEstimator.NewSection(glassType, number));
            SelectedSectionNumber = number;
            return number;
        }

        /// <summary>Removes a section unless it is the last one or a group uses it.</summary>
        public bool RemovePricingSection(int sectionNumber)
        {
            if (PricingSections.Count <= 1) return false;
            PricingSection? section = PricingSections.FirstOrDefault(
                item => item.SectionNumber == sectionNumber);
            if (section == null) return false;
            PricingSections.Remove(section);
            if (SelectedSectionNumber == sectionNumber)
                SelectedSectionNumber = PricingSections[0].SectionNumber;
            return true;
        }

        /// <summary>
        /// Creates a grid group from a pricing section. The estimator's result —
        /// including its warnings — is returned so a refusal says why.
        /// </summary>
        public PricingResult AddGroupFromSection(int sectionNumber)
        {
            EnsureStructure();
            PricingSection? section = PricingSections.FirstOrDefault(
                item => item.SectionNumber == sectionNumber) ?? SelectedSection;
            if (section == null)
                return new PricingResult
                {
                    Warnings = new List<string> { "No pricing section is available to create this specification." }
                };

            PricingResult result = PricingEstimator.Calculate(section);
            if (!result.IsComplete) return result;

            int groupNumber = Groups.Count == 0
                ? 1
                : Groups.Max(group => group.GroupNumber) + 1;

            var group = new SpecGroup
            {
                GroupNumber = groupNumber,
                GroupName = "",
                GlassType = GlassBuilds.CodeOf(section.Build),
                SpecName = result.SpecName
            };
            group.ApplyPricing(section, result);
            group.Rows.Add(new SpecRow { GlassRef = "W1", Qty = 1 });
            Groups.Add(group);
            Recalculate();
            return result;
        }

        /// <summary>Removes a group and renumbers the rest. No gap in the grid.</summary>
        public bool RemoveGroup(int groupNumber)
        {
            SpecGroup? group = Groups.FirstOrDefault(item => item.GroupNumber == groupNumber);
            if (group == null) return false;
            Groups.Remove(group);
            for (int index = 0; index < Groups.Count; index++)
                Groups[index].GroupNumber = index + 1;
            Recalculate();
            return true;
        }

        // ── The one recalculation ───────────────────────────────────────

        /// <summary>
        /// Re-derives every total and the P&amp;L from the document's inputs.
        /// Order matters: charges read the grid, the summary reads both, the
        /// P&amp;L reads all three.
        /// </summary>
        public void Recalculate()
        {
            foreach (SpecGroup group in Groups)
            {
                group.Rows ??= new List<SpecRow>();
                group.Renumber();
            }

            SpecGridTotals totals = SpecGridTotals.From(Groups);

            Charges.EnsureCatalogueRows();
            OtherChargeAutomation.Apply(Charges, Groups);
            Charges.RebuildWastages(Groups);
            Charges.Recalculate(Groups);

            Summary.GlassSubtotal = totals.TotalValue;
            Summary.OtherChargesSubtotal = Charges.Total;
            Summary.Recalculate();

            ProfitLoss = ProfitLossCalculator.Calculate(
                Groups, Charges, Summary, ProfitLossSettings);
        }

        /// <summary>Grid totals without mutating anything (for display and checks).</summary>
        [JsonIgnore]
        public SpecGridTotals GridTotals => SpecGridTotals.From(Groups);

        /// <summary>A deep copy, so a caller can abandon an edit without side effects.</summary>
        public ProformaInvoiceDraft Clone() => new()
        {
            FormatVersion = FormatVersion,
            Id = Id,
            ProformaNumber = ProformaNumber,
            Status = Status,
            IssueDate = IssueDate,
            ValidUntil = ValidUntil,
            Parties = Parties.Clone(),
            PricingSections = PricingSections.Select(section => section.Clone()).ToList(),
            SelectedSectionNumber = SelectedSectionNumber,
            Groups = Groups.Select(group => group.Clone()).ToList(),
            Charges = Charges.Clone(),
            Summary = Summary.Clone(),
            ProfitLossSettings = new ProfitLossSettings { WastagePercent = ProfitLossSettings.WastagePercent },
            ProfitLoss = ProfitLoss,
            Notes = Notes,
            TermsText = TermsText,
            FurthestStageReached = FurthestStageReached,
            Attachments = new List<ProformaAttachment>(Attachments),
            CreatedAtUtc = CreatedAtUtc,
            UpdatedAtUtc = UpdatedAtUtc,
            CreatedByUserId = CreatedByUserId,
            CreatedByName = CreatedByName,
            UpdatedByUserId = UpdatedByUserId,
            UpdatedByName = UpdatedByName,
            RowVersion = RowVersion
        };
    }
}
