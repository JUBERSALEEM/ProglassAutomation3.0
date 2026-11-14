// ═══════════════════════════════════════════════════════════════════════
// FILE: Models/ProformaInvoice/SavedInvoice/ProformaInvoiceSnapshot.cs
// ═══════════════════════════════════════════════════════════════════════
// CHAT 20 — PER-SPEC TARGETING PERSISTENCE
//
//   PredefinedChargeSnapshot gains 2 new fields:
//     • SelectedMetricKey        — which metric radio was selected
//     • CheckedSpecGroupNumbers  — which specs were checked
//
//   Old JSON without these fields deserialize to defaults:
//     SelectedMetricKey        → "" (System.Text.Json string default)
//     CheckedSpecGroupNumbers  → null (mapper handles null gracefully)
//
//   Backward compatibility: 100% — old invoices load unchanged.
//
// PREVIOUS: CHAT 15 — PROJECT REFERENCE FIELD ADDED
//   ProjectSnapshot.ProjectReference field
//   All other snapshot classes preserved EXACTLY.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.MasterData;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.Json.Serialization;

namespace ProGlassAutomation.Models.ProformaInvoice.SavedInvoice
{
    /// <summary>
    /// Pure POCO representation of the entire wizard state, ready for
    /// JSON serialization. Never contains WPF types or ObservableCollections.
    /// Populated by ProformaInvoiceMapper.ToSnapshot(),
    /// consumed by ProformaInvoiceMapper.RestoreFromSnapshot().
    ///
    /// BUG-01: CompanySnapshot.IssueDate / ValidUntil no longer default
    ///         to DateTime.Now at property level.
    /// BUG-02: TermClauseSnapshot.Type stores enum name as string.
    /// BUG-03: PolishSubType documented.
    ///
    /// CHAT 15: ProjectSnapshot.ProjectReference added.
    /// CHAT 20: PredefinedChargeSnapshot per-spec targeting fields added.
    /// </summary>
    public class ProformaInvoiceSnapshot
    {
        public int SchemaVersion { get; set; } = 2;

        /// <summary>
        /// Furthest wizard step the operator reached before the last save.
        /// Restored on load so the step-completion indicators (and the
        /// operator's ability to jump directly to a previously visited
        /// step) survive an app close or a back-to-list navigation.
        /// Additive: old snapshots deserialise to 0 (step 1 only).
        /// </summary>
        public int FurthestStepReached { get; set; }

        // Step 1: Company
        public CompanySnapshot Company { get; set; } = new();

        // Step 2 in snapshot = Project data (structure unchanged for JSON stability)
        public ProjectSnapshot Project { get; set; } = new();

        // Step 3 in snapshot = Client data (structure unchanged for JSON stability)
        public ClientSnapshot Client { get; set; } = new();

        // Step 4: pricing estimator panel state
        public Step4PricingSnapshot Step4Pricing { get; set; } = new();
        public List<Step4PricingSnapshot> Step4PricingSections { get; set; } = new();
        public int Step4SelectedSectionNumber { get; set; } = 1;

        // Step 5: DataGrid spec groups
        public List<SpecGroupSnapshot> SpecGroups { get; set; } = new();

        // Step 6: Other Charges (predefined + wastages + custom)
        public OtherChargesSnapshot OtherCharges { get; set; } = new();

        // Step 7 / Phase 7.12: Financial summary settings
        public InvoiceSummarySnapshot Summary { get; set; } = new();

        // Phase 7.14: Terms & Conditions
        public List<TermClauseSnapshot> TermClauses { get; set; } = new();

        // Phase 7.13: Prepared By fields
        public PreparedBySnapshot PreparedBy { get; set; } = new();

        // ── PHASE A/B: Step 9 Profit & Loss ─────────────────────────────
        // ADDITIVE — invoices saved before this field existed deserialize
        // with the property initializer intact (System.Text.Json only
        // overwrites members present in the JSON), so every historical
        // snapshot still loads with an empty, harmless P&L block.
        public ProfitLossSnapshot ProfitLoss { get; set; } = new();
    }

    // ── Step 1 ──────────────────────────────────────────────────────────

    public class CompanySnapshot
    {
        public string CompanyName { get; set; } = "";
        public string CompanyTrn { get; set; } = "";
        public string CompanyLocation { get; set; } = "";
        public string ProformaId { get; set; } = "";
        public int? InquiryId { get; set; }

        /// <summary>BUG-01: default(DateTime). Mapper sets explicitly.</summary>
        public DateTime IssueDate { get; set; } = default;

        /// <summary>BUG-01: default(DateTime). Mapper sets explicitly.</summary>
        public DateTime ValidUntil { get; set; } = default;

        public string ProformaStatus { get; set; } = "Draft";

        // v2 — richer company identity (Company Center module).
        // Additive: older invoice JSON deserializes with empty values.
        public string Representative { get; set; } = "";
        public string RepresentativeMobile { get; set; } = "";
        public string CompanyEmail { get; set; } = "";
        public string AccountNumber { get; set; } = "";
        public string BankName { get; set; } = "";
        public string LogoPath { get; set; } = "";
        /// <summary>Embedded logo copy so saved/exported documents survive if the original file moves.</summary>
        public string LogoBase64 { get; set; } = "";
        /// <summary>Id of the Company Center record this invoice uses.</summary>
        public int CompanyRecordId { get; set; }

        // v3 — logo placement on printed documents (Company Center branding).
        // Additive: older invoice JSON deserializes with defaults (no logo).
        public CompanyLogoLayout LogoLayout { get; set; } = CompanyLogoLayout.None;
        public double LogoWidthMm { get; set; } = 22;
        public double LogoHeightMm { get; set; } = 26;
    }

    // ── Project data ────────────────────────────────────────────────────

    /// <summary>
    /// CHAT 15: ProjectReference field added.
    /// Old JSON snapshots without this field deserialize to "" — safe.
    /// </summary>
    public class ProjectSnapshot
    {
        public string ProjectIdentifierName { get; set; } = "";
        public string ProjectContractNo { get; set; } = "";

        /// <summary>
        /// CHAT 15: NEW — Descriptive project reference.
        /// e.g. "HOUSE FOR IMAD HABIB" — full descriptive text.
        /// Separate from ProjectContractNo which is a short code.
        /// </summary>
        public string ProjectReference { get; set; } = "";

        public string SiteDeliveryLocation { get; set; } = "";
        public string LpoNumber { get; set; } = "";
    }

    // ── Client data ─────────────────────────────────────────────────────

    public class ClientSnapshot
    {
        public string CustomerName { get; set; } = "";
        public string CustomerTrn { get; set; } = "";
        public string CustomerReferenceCode { get; set; } = "";
        public string SalesRepresentative { get; set; } = "";
        public string DocumentPreparedBy { get; set; } = "";
        public string BillingAddress { get; set; } = "";
        public string AttentionExecutiveName { get; set; } = "";
        public string ClientContactPhone { get; set; } = "";
    }

    // ── Step 4: Pricing estimator ───────────────────────────────────────

    public class Step4PricingSnapshot
    {
        public string SelectedGlassTypeCode { get; set; } = "";
        public string GeneratedSpecName { get; set; } = "";
        public decimal EstimatedRatePerSqm { get; set; }
        /// <summary>
        /// PHASE A: true only when every required pane has colour, thickness,
        /// processing, sheet W/H and a store-resolved price. Null on invoices
        /// saved before this field existed.
        /// </summary>
        public bool? MaterialIsValid { get; set; }
        public string MaterialInvalidReason { get; set; } = "";
        public SguPricingPanelSnapshot Sgu { get; set; } = new();
        public DguPricingPanelSnapshot Dgu { get; set; } = new();
        public LamPricingPanelSnapshot Lam { get; set; } = new();
        public DguLamPricingPanelSnapshot DguLam { get; set; } = new();

        /// <summary>
        /// Stable content fingerprint of the whole Step 4 selection
        /// (type, generated name, rate and every panel field — panes,
        /// treatments, spacers, sheets, prices). The Step 4 → Step 5
        /// coordinator compares it against the group's last-synced hash:
        /// any Step 4 edit — even one that leaves the spec name and the
        /// rate unchanged (same glass, different stock sheet) — forces a
        /// re-sync instead of leaving Step 5 on stale specs/sheets.
        /// Invariant-culture, separator-safe (unit separator), never null.
        /// </summary>
        public string ComputeContentHash()
        {
            var sb = new StringBuilder(512);
            void Put(string? v) { sb.Append(v ?? "").Append('\u001F'); }
            void PutM(decimal v) { sb.Append(v.ToString("0.########", CultureInfo.InvariantCulture)).Append('\u001F'); }
            void PutI(int v) { sb.Append(v.ToString(CultureInfo.InvariantCulture)).Append('\u001F'); }

            Put(SelectedGlassTypeCode);
            Put(GeneratedSpecName);
            PutM(EstimatedRatePerSqm);

            var sgu = Sgu ?? new SguPricingPanelSnapshot();
            Put(sgu.SelectedThickness); Put(sgu.GlassColor); Put(sgu.SelectedTreatment);
            PutM(sgu.BasePricePerSqm); PutM(sgu.CuttingCostPerSqm); PutM(sgu.TemperingCostPerSqm);
            PutM(sgu.YieldFactor); PutM(sgu.TargetMarginPercent); Put(sgu.SelectedPolishOption);
            Put(sgu.PanelExtra); Put(sgu.UseCustomPanelExtra ? "1" : "0"); Put(sgu.CustomPanelExtra);
            PutI(sgu.SheetWidthMm); PutI(sgu.SheetHeightMm);
            Put(sgu.PriceResolvedFromStore.HasValue ? (sgu.PriceResolvedFromStore.Value ? "1" : "0") : "");

            var dgu = Dgu ?? new DguPricingPanelSnapshot();
            Put(dgu.OuterThickness); Put(dgu.OuterGlassColor); Put(dgu.OuterTreatment);
            PutM(dgu.OuterBasePrice); PutI(dgu.OuterSheetWidthMm); PutI(dgu.OuterSheetHeightMm);
            Put(dgu.SpacerWidth); Put(dgu.SpacerMaterial); PutM(dgu.SpacerPrice);
            Put(dgu.SpacerExtra); PutM(dgu.UInsertPricePerSqm); Put(dgu.PanelExtra);
            Put(dgu.InnerThickness); Put(dgu.InnerGlassColor); Put(dgu.InnerTreatment);
            PutM(dgu.InnerBasePrice); PutI(dgu.InnerSheetWidthMm); PutI(dgu.InnerSheetHeightMm);
            PutM(dgu.YieldFactor); PutM(dgu.MarginProfitPercent);
            Put(dgu.OuterPriceResolvedFromStore.HasValue ? (dgu.OuterPriceResolvedFromStore.Value ? "1" : "0") : "");
            Put(dgu.InnerPriceResolvedFromStore.HasValue ? (dgu.InnerPriceResolvedFromStore.Value ? "1" : "0") : "");

            var lam = Lam ?? new LamPricingPanelSnapshot();
            Put(lam.OuterThickness); Put(lam.OuterGlassColor); Put(lam.OuterTreatment);
            PutM(lam.OuterBasePrice); PutI(lam.OuterSheetWidthMm); PutI(lam.OuterSheetHeightMm);
            Put(lam.InnerThickness); Put(lam.InnerGlassColor); Put(lam.InnerTreatment);
            PutM(lam.InnerBasePrice); PutI(lam.InnerSheetWidthMm); PutI(lam.InnerSheetHeightMm);
            Put(lam.PvbThickness); Put(lam.PvbColor); PutM(lam.PvbMaterialRate);
            Put(lam.LaminationPolish);
            PutM(lam.OuterCuttingCost); PutM(lam.InnerCuttingCost);
            PutM(lam.OuterTemperingCost); PutM(lam.InnerTemperingCost);
            PutM(lam.YieldFactor); PutM(lam.MarginProfitPercent);
            Put(lam.OuterPriceResolvedFromStore.HasValue ? (lam.OuterPriceResolvedFromStore.Value ? "1" : "0") : "");
            Put(lam.InnerPriceResolvedFromStore.HasValue ? (lam.InnerPriceResolvedFromStore.Value ? "1" : "0") : "");

            var dl = DguLam ?? new DguLamPricingPanelSnapshot();
            Put(dl.OuterThickness); Put(dl.OuterGlassColor); Put(dl.OuterTreatment);
            PutM(dl.OuterBasePrice);
            PutI(dl.OuterSheetWidthMm); PutI(dl.OuterSheetHeightMm);
            Put(dl.SpacerThickness); PutM(dl.SpacerPrice); Put(dl.SpacerExtra);
            Put(dl.LamOuterThickness); Put(dl.LamOuterColor); Put(dl.LamOuterTreatment);
            PutM(dl.LamOuterBasePrice);
            PutI(dl.LamOuterSheetWidthMm); PutI(dl.LamOuterSheetHeightMm);
            Put(dl.InterlayerType); Put(dl.InterlayerThickness); Put(dl.InterlayerColor);
            PutM(dl.InterlayerBuyPrice); PutM(dl.InterlayerSellPrice);
            Put(dl.LamInnerThickness); Put(dl.LamInnerColor); Put(dl.LamInnerTreatment);
            PutM(dl.LamInnerBasePrice);
            PutI(dl.LamInnerSheetWidthMm); PutI(dl.LamInnerSheetHeightMm);
            Put(dl.PanelExtra); Put(dl.EdgePolish);
            PutM(dl.OuterCuttingCost); PutM(dl.OuterTemperingCost);
            PutM(dl.LamOuterCuttingCost); PutM(dl.LamOuterTemperingCost);
            PutM(dl.LamInnerCuttingCost); PutM(dl.LamInnerTemperingCost);
            PutM(dl.YieldFactor); PutM(dl.MarginProfitPercent);
            Put(dl.OuterPriceResolvedFromStore.HasValue ? (dl.OuterPriceResolvedFromStore.Value ? "1" : "0") : "");
            Put(dl.LamOuterPriceResolvedFromStore.HasValue ? (dl.LamOuterPriceResolvedFromStore.Value ? "1" : "0") : "");
            Put(dl.LamInnerPriceResolvedFromStore.HasValue ? (dl.LamInnerPriceResolvedFromStore.Value ? "1" : "0") : "");
            Put(MaterialIsValid.HasValue ? (MaterialIsValid.Value ? "1" : "0") : "");
            Put(MaterialInvalidReason);

            return sb.ToString();
        }
    }

    public class SguPricingPanelSnapshot
    {
        public string SelectedThickness { get; set; } = "";
        public string GlassColor { get; set; } = "";
        public string SelectedTreatment { get; set; } = "";
        public decimal BasePricePerSqm { get; set; }
        public decimal CuttingCostPerSqm { get; set; }
        public decimal TemperingCostPerSqm { get; set; }
        public decimal YieldFactor { get; set; }
        public decimal TargetMarginPercent { get; set; }
        public string SelectedPolishOption { get; set; } = "";
        public string PanelExtra { get; set; } = "None";
        public bool UseCustomPanelExtra { get; set; }
        public string CustomPanelExtra { get; set; } = "";
        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }
        /// <summary>Null on invoices saved before Phase A.</summary>
        public bool? PriceResolvedFromStore { get; set; }
    }

    public class DguPricingPanelSnapshot
    {
        public string OuterThickness { get; set; } = "";
        public string OuterGlassColor { get; set; } = "";
        public string OuterTreatment { get; set; } = "";
        public decimal OuterBasePrice { get; set; }
        public int OuterSheetWidthMm { get; set; }
        public int OuterSheetHeightMm { get; set; }
        public string SpacerWidth { get; set; } = "";
        public string SpacerMaterial { get; set; } = "";
        public decimal SpacerPrice { get; set; }
        public string SpacerExtra { get; set; } = "";
        public decimal UInsertPricePerSqm { get; set; }
        public string PanelExtra { get; set; } = "";
        public string InnerThickness { get; set; } = "";
        public string InnerGlassColor { get; set; } = "";
        public string InnerTreatment { get; set; } = "";
        public decimal InnerBasePrice { get; set; }
        public int InnerSheetWidthMm { get; set; }
        public int InnerSheetHeightMm { get; set; }
        public decimal YieldFactor { get; set; }
        public decimal MarginProfitPercent { get; set; }
        /// <summary>Null on invoices saved before Phase A.</summary>
        public bool? OuterPriceResolvedFromStore { get; set; }
        public bool? InnerPriceResolvedFromStore { get; set; }
    }

    public class LamPricingPanelSnapshot
    {
        public string OuterThickness { get; set; } = "";
        public string OuterGlassColor { get; set; } = "";
        public string OuterTreatment { get; set; } = "";
        public decimal OuterBasePrice { get; set; }
        public int OuterSheetWidthMm { get; set; }
        public int OuterSheetHeightMm { get; set; }
        public string InnerThickness { get; set; } = "";
        public string InnerGlassColor { get; set; } = "";
        public string InnerTreatment { get; set; } = "";
        public decimal InnerBasePrice { get; set; }
        public int InnerSheetWidthMm { get; set; }
        public int InnerSheetHeightMm { get; set; }
        public string PvbThickness { get; set; } = "";
        public string PvbColor { get; set; } = "";
        public decimal PvbMaterialRate { get; set; }
        public string LaminationPolish { get; set; } = "";
        public decimal OuterCuttingCost { get; set; }
        public decimal InnerCuttingCost { get; set; }
        public decimal OuterTemperingCost { get; set; }
        public decimal InnerTemperingCost { get; set; }
        public decimal YieldFactor { get; set; }
        public decimal MarginProfitPercent { get; set; }
        /// <summary>Null on invoices saved before Phase A.</summary>
        public bool? OuterPriceResolvedFromStore { get; set; }
        public bool? InnerPriceResolvedFromStore { get; set; }
    }

    public class DguLamPricingPanelSnapshot
    {
        public string OuterThickness { get; set; } = "";
        public string OuterGlassColor { get; set; } = "";
        public string OuterTreatment { get; set; } = "";
        public decimal OuterBasePrice { get; set; }
        public int OuterSheetWidthMm { get; set; }
        public int OuterSheetHeightMm { get; set; }
        public string SpacerThickness { get; set; } = "";
        public decimal SpacerPrice { get; set; }
        public string SpacerExtra { get; set; } = "";
        public string LamOuterThickness { get; set; } = "";
        public string LamOuterColor { get; set; } = "";
        public string LamOuterTreatment { get; set; } = "";
        public decimal LamOuterBasePrice { get; set; }
        public int LamOuterSheetWidthMm { get; set; }
        public int LamOuterSheetHeightMm { get; set; }
        public string InterlayerType { get; set; } = "";
        public string InterlayerThickness { get; set; } = "";
        public string InterlayerColor { get; set; } = "";
        public decimal InterlayerBuyPrice { get; set; }
        public decimal InterlayerSellPrice { get; set; }
        public string LamInnerThickness { get; set; } = "";
        public string LamInnerColor { get; set; } = "";
        public string LamInnerTreatment { get; set; } = "";
        public decimal LamInnerBasePrice { get; set; }
        public int LamInnerSheetWidthMm { get; set; }
        public int LamInnerSheetHeightMm { get; set; }
        public string PanelExtra { get; set; } = "";
        public string EdgePolish { get; set; } = "";
        public decimal OuterCuttingCost { get; set; }
        public decimal OuterTemperingCost { get; set; }
        public decimal LamOuterCuttingCost { get; set; }
        public decimal LamOuterTemperingCost { get; set; }
        public decimal LamInnerCuttingCost { get; set; }
        public decimal LamInnerTemperingCost { get; set; }
        public decimal YieldFactor { get; set; }
        public decimal MarginProfitPercent { get; set; }
        /// <summary>Null on invoices saved before Phase A.</summary>
        public bool? OuterPriceResolvedFromStore { get; set; }
        public bool? LamOuterPriceResolvedFromStore { get; set; }
        public bool? LamInnerPriceResolvedFromStore { get; set; }
    }

    // ── Step 5: DataGrid spec groups ────────────────────────────────────

    public class SpecGroupSnapshot
    {
        public int GroupNumber { get; set; }
        public string GroupName { get; set; } = "";
        public string SelectedGlassType { get; set; } = "";
        public string SelectedSpecName { get; set; } = "";

        // Synchronization provenance is persisted so reopening an invoice
        // cannot turn a deliberate Step 5 edit back into a Step 4-owned row.
        // Old snapshots naturally deserialize these additive fields as empty.
        public string LastSyncedStep4SpecName { get; set; } = "";
        public string LastSyncedStep4GlassType { get; set; } = "";
        /// <summary>
        /// ADDITIVE (2026-09-17). Content fingerprint of the Step 4 section
        /// at the last sync (see Step4PricingSnapshot.ComputeContentHash).
        /// Lets the coordinator tell "Step 4 moved to a NEW selection —
        /// force-update Step 5" from "Step 4 untouched — respect operator
        /// overrides". Old snapshots deserialize as "" (no hash baseline).
        /// </summary>
        public string LastSyncedStep4ContentHash { get; set; } = "";
        public bool HasManualStep5SpecOverride { get; set; }
        public bool HasManualStep5GlassTypeOverride { get; set; }
        public bool HasManualStep5BaseRateOverride { get; set; }

        public string OuterGlassColor { get; set; } = "";
        public string InnerGlassColor { get; set; } = "";
        /// <summary>DGU+LAM laminated-inner colour. Empty on other builds.</summary>
        public string ThirdGlassColor { get; set; } = "";
        /// <summary>Null on invoices saved before Phase A.</summary>
        public bool? MaterialIsValid { get; set; }
        public string MaterialInvalidReason { get; set; } = "";

        /// <summary>
        /// Original Step 4 reference rate shown in Step 5. This is separate
        /// from BaseRate because operators may subsequently edit BaseRate.
        /// </summary>
        public decimal ImportedPrice { get; set; }
        public decimal Step4EstimatedRatePerSqm { get; set; }
        public decimal BaseRate { get; set; }
        public decimal SurchargePercent { get; set; }

        // ── PHASE G: stock sheet chosen in Step 4 ───────────────────────
        // ADDITIVE. Persisting the sheet means a saved invoice always
        // re-costs against the sheet it was QUOTED on, even months later
        // after Sheet Store sizes or prices have changed. Zero on invoices
        // saved before this field existed — Step 9 then falls back and
        // says so, rather than silently using a different sheet.
        public int OuterSheetWidthMm { get; set; }
        public int OuterSheetHeightMm { get; set; }
        public int InnerSheetWidthMm { get; set; }
        public int InnerSheetHeightMm { get; set; }
        public decimal OuterSheetPricePerSqm { get; set; }
        public decimal InnerSheetPricePerSqm { get; set; }
        /// <summary>DGU+LAM third (laminated inner) stock sheet. Zero on other builds.</summary>
        public int ThirdSheetWidthMm { get; set; }
        public int ThirdSheetHeightMm { get; set; }
        public decimal ThirdSheetPricePerSqm { get; set; }

        /// <summary>
        /// Step 4 seeded U-Insert rate for DGU-type specs. Additive: older
        /// snapshots deserialize to 0 and Step 6 falls back to its template.
        /// </summary>
        public decimal UInsertPricePerSqm { get; set; }

        // ══════════════════════════════════════════════════════════════
        //  PHASE 1-AUDIT — per-group production-cost provenance.
        //
        //  These are the production rates Step 4 quoted for THIS group at
        //  handoff time (spacer, interlayer, cutting, tempering). Before
        //  they existed, Step 9 read them from the LIVE Step 4 panel —
        //  which holds only the currently selected calculation section —
        //  so a multi-section PI could silently cost every group against
        //  section 1's production inputs (and a restored PI against
        //  whatever section the wizard happens to show).
        //
        //  Nullable on purpose: groups on invoices saved before these
        //  fields existed deserialize to null, and Step 9 falls back to
        //  the live panel exactly as it does today. A group that was
        //  manually re-spec'd in Step 5 keeps its last-synced values.
        //
        //  Semantics by build:
        //    SGU     → cutting/tempering per-SQM rates
        //    DGU     → spacer (air-spacer production row)
        //    LAM     → interlayer = PVB material rate; cutting/tempering
        //              = the pane-pair rates summed
        //    DGU+LAM → spacer; interlayer = laminator BUY price
        //              (InterlayerBuyPrice, never the sell price);
        //              cutting/tempering = all three panes summed
        // ══════════════════════════════════════════════════════════════
        public decimal? Step4SpacerPricePerSqm { get; set; }
        public decimal? Step4InterlayerCostPerSqm { get; set; }
        public decimal? Step4CuttingCostPerSqm { get; set; }
        public decimal? Step4TemperingCostPerSqm { get; set; }

        public List<GlassItemRowSnapshot> ItemRows { get; set; } = new();
    }

    public class GlassItemRowSnapshot
    {
        public int SerialNumber { get; set; }
        public string GlassRef { get; set; } = "";
        public int W1 { get; set; }
        public int H1 { get; set; }
        public int W2 { get; set; }
        public int H2 { get; set; }
        public int Qty { get; set; }
        public decimal Rate { get; set; }
        public int PerimeterFactor { get; set; }

        /// <summary>
        /// Unit panel area (W1 × H1). Computed — not persisted.
        /// Old snapshots without this field still bind correctly.
        /// </summary>
        [JsonIgnore]
        public decimal Sqm1 => SqmCalculator.CalculatePanel(W1, H1);

        /// <summary>
        /// Total area (unit SQM × Qty). Computed — not persisted.
        /// </summary>
        [JsonIgnore]
        public decimal TotalSqm => SqmCalculator.CalculateTotal(W1, H1, Qty);
    }

    // ── Step 6: Other Charges ───────────────────────────────────────────

    public class OtherChargesSnapshot
    {
        /// <summary>
        /// True = Step 6 advanced mode auto-detects charges from Step 4 /
        /// Step 5. False = manual mode.
        /// </summary>
        public bool UseAdvancedAutomation { get; set; } = true;

        public List<PredefinedChargeSnapshot> PredefinedCharges { get; set; } = new();
        public List<WastageRowSnapshot> Wastages { get; set; } = new();
        public List<CustomChargeSnapshot> CustomCharges { get; set; } = new();
    }

    /// <summary>
    /// Predefined charge snapshot.
    ///
    /// CHAT 20 additions for per-spec targeting persistence:
    ///   • SelectedMetricKey        — which metric radio was selected
    ///   • CheckedSpecGroupNumbers  — which specs were ticked
    ///
    /// Old JSON files without these fields will deserialize to:
    ///   • SelectedMetricKey       → "" (skip metric restore)
    ///   • CheckedSpecGroupNumbers → null (skip checkbox restore — defaults apply)
    /// </summary>
    public class PredefinedChargeSnapshot
    {
        public string TemplateKey { get; set; } = "";
        public bool IsActive { get; set; }

        /// <summary>
        /// Legacy field from pre-Chat 17 single-source system.
        /// Preserved for backward compatibility. New saves may leave empty.
        /// </summary>
        public string SelectedSourceKey { get; set; } = "";

        public decimal Rate { get; set; }

        /// <summary>
        /// Live Step 6 quantity at save/export time. Null on legacy snapshots
        /// so PDF/Excel fall back to recomputing from specs.
        /// </summary>
        public decimal? MetricQty { get; set; }

        /// <summary>
        /// Live Step 6 result at save/export time. Null on legacy snapshots.
        /// When present, Step 7 PDF must print this instead of recomputing
        /// a different amount from the charge calculator.
        /// </summary>
        public decimal? ResultAmount { get; set; }

        /// <summary>
        /// BUG-03: Valid values: "AllSidePolish", "AllSidePolishAsPerDwg",
        ///                       "BothHeightSide", "BothWidthSide", "Custom".
        /// </summary>
        public string PolishSubType { get; set; } = "";
        public string CustomPolishDescription { get; set; } = "";
        public decimal CustomPolishQty { get; set; }

        /// <summary>
        /// CHAT 20: The metric key selected in the row's spec-target popup.
        /// Values match SpecTargetSelection.AvailableMetrics[].Key
        /// (e.g. "LM1", "SQM1", "QTY", "LM_COMBINED", etc.)
        /// Empty on legacy invoices — restore skips metric selection.
        /// </summary>
        public string SelectedMetricKey { get; set; } = "";

        /// <summary>
        /// CHAT 20: SpecGroupNumbers of specs that were CHECKED in the
        /// row's spec-target popup. Others were unchecked.
        /// Null on legacy invoices — restore skips checkbox state
        /// and TargetSelection retains its default all-checked state.
        /// </summary>
        public List<int> CheckedSpecGroupNumbers { get; set; } = new();
    }

    public class WastageRowSnapshot
    {
        public bool IsActive { get; set; }
        public string ColorName { get; set; } = "";
        public decimal Rate { get; set; }

        /// <summary>
        /// Live Step 6 quantity at save/export time. Null on legacy snapshots
        /// so PDF/Excel exports can fall back to recomputing from sources.
        /// </summary>
        public decimal? TotalSqm { get; set; }

        /// <summary>
        /// Live Step 6 result at save/export time. Null on legacy snapshots.
        /// When present, exports must retain the amount calculated by the
        /// wastage row, including optimization-formula results.
        /// </summary>
        public decimal? ResultAmount { get; set; }

        /// <summary>
        /// True when the operator attached this row by hand instead of the
        /// advanced engine generating it. Manual rows survive advanced
        /// refreshes (which rebuild generated rows on every Step 5 edit).
        /// Absent on legacy snapshots → false → row is treated as generated,
        /// preserving the historical rebuild behaviour for old invoices.
        /// </summary>
        public bool IsManuallyAdded { get; set; }

        /// <summary>
        /// True when the wastage row derives its effective AED/SQM rate from
        /// the optimization formula instead of using the manual Rate field.
        /// </summary>
        public bool UseOptimizationFormula { get; set; }

        /// <summary>Glass price fed into the optimization formula.</summary>
        public decimal FormulaPricePerSqm { get; set; }

        /// <summary>Third-party optimization percentage, e.g. 70 means 70%.</summary>
        public decimal OptimizationPercent { get; set; }

        /// <summary>
        /// Sheet quantity from the wastage setup box. Step 9 reads this for
        /// the matching pane's QTY (outer AND inner) even when the wastage
        /// charge row itself is disabled. Old snapshots default to 1.
        /// </summary>
        public int SheetQuantity { get; set; } = 1;

        /// <summary>Format: "SpecNumber-SideLabel" (e.g. "1-Outer (W1/H1)").</summary>
        public List<string> CheckedSourceKeys { get; set; } = new();
    }

    public class CustomChargeSnapshot
    {
        public bool IsActive { get; set; }
        public string ChargeName { get; set; } = "";
        public decimal Qty { get; set; }
        public string Unit { get; set; } = "";
        public decimal Rate { get; set; }
    }

    // ── Phase 7.12: Invoice Summary ─────────────────────────────────────

    public class InvoiceSummarySnapshot
    {
        /// <summary>"Percentage" or "FixedAmount"</summary>
        public string DiscountType { get; set; } = "Percentage";
        public decimal DiscountValue { get; set; }
        public bool IsVatApplicable { get; set; } = true;
        public decimal VatPercent { get; set; } = 5m;
    }

    // ── Phase 7.14: Terms & Conditions ──────────────────────────────────

    public class TermClauseSnapshot
    {
        /// <summary>
        /// BUG-02: Valid values: "PaymentTerms", "Delivery", "Warranty",
        ///                       "Validity", "Taxes", "Installation", "Custom".
        /// </summary>
        public string Type { get; set; } = "Custom";

        public int Order { get; set; }
        public bool IsActive { get; set; } = true;
        public string Title { get; set; } = "";
        public string Body { get; set; } = "";
        public List<string> SubLines { get; set; } = new();
        public int ValidityDays { get; set; } = 7;
    }

    // ── Phase 7.13: Prepared By ─────────────────────────────────────────

    public class PreparedBySnapshot
    {
        public string PreparedByName { get; set; } = "";
        public string PreparedByDept { get; set; } = "";
        // Default empty so the printable VM falls back to the workflow
        // status from Company.ProformaStatus. A hard-coded APPROVED here
        // promoted old/blank snapshots into a fake confirmed state.
        public string PreparedByStatus { get; set; } = "";
        public string TermsText { get; set; } = "";
        public string FooterDisclaimer { get; set; } = "";
    }

    // ── PHASE A/B: Step 9 Profit & Loss ─────────────────────────────────

    /// <summary>
    /// Persisted result of the Step 9 Profit &amp; Loss calculation.
    /// Pure POCO, JSON friendly. Every field is a plain value so old and
    /// new snapshots round-trip without custom converters.
    /// </summary>
    public class ProfitLossSnapshot
    {
        /// <summary>Line items — one per glass pane / spacer / production charge.</summary>
        public List<ProfitLossRowSnapshot> Rows { get; set; } = new();

        /// <summary>Invoice grand total as quoted to the customer (VAT inclusive).</summary>
        public decimal GrossVatInclusive { get; set; }

        /// <summary>Revenue excluding VAT (gross ÷ 1.05).</summary>
        public decimal NetExVat { get; set; }

        /// <summary>Sum of the glass (sheet purchase) rows.</summary>
        public decimal TotalGlassCost { get; set; }

        /// <summary>Sum of the production rows (ASP spacer, cutting, tempering).</summary>
        public decimal TotalProductionCost { get; set; }

        /// <summary>NetExVat − TotalGlassCost − TotalProductionCost. Negative = loss.</summary>
        public decimal Profit { get; set; }

        /// <summary>Profit as a percentage of NetExVat, one decimal.</summary>
        public decimal MarginPercent { get; set; }

        /// <summary>
        /// Overall margin including U-insert cost:
        /// (Gross Revenue − Total Cost with U-insert) / Total Cost × 100.
        /// This is the REAL job profitability for decision-making.
        /// Defaults to 0 for snapshots saved before this field existed;
        /// consumers should treat 0 as "unknown" for old snapshots.
        /// </summary>
        public decimal OverallMarginPercent { get; set; }

        /// <summary>
        /// Net margin (accounting perspective):
        /// (Gross Revenue − Total Cost) / Gross Revenue × 100.
        /// Defaults to 0 for snapshots saved before this field existed.
        /// </summary>
        public decimal NetMarginPercent { get; set; }

        /// <summary>Wastage allowance used when sheet counts were computed.</summary>
        public decimal WastagePercent { get; set; }

        /// <summary>
        /// PHASE 3 — sheet-store price-basis signature at compute time
        /// (see Services/ProformaInvoice/GlassPriceStamper.cs). "" on
        /// snapshots saved before this field existed OR when the stamp
        /// could not be read; consumers report that as UNKNOWN, never as
        /// fresh. Reporting flags a P&L when the store's current stamp
        /// differs — it does NOT re-cost (single engine, single truth).
        /// </summary>
        public string GlassPriceStamp { get; set; } = "";

        /// <summary>PHASE 3 — when this P&L was last computed.</summary>
        public DateTime? ComputedAtUtc { get; set; }

        /// <summary>
        /// Which Sheet Store column priced the glass when this snapshot was
        /// taken: true = SELLING, false = PURCHASE.
        ///
        /// Recorded because the two produce materially different profit for
        /// the same job — around 20% versus 5% on a typical DGU — and the
        /// default changed from purchase to selling. Without this flag,
        /// invoices saved either side of that change would be indistinguishable
        /// and no one could tell which question a stored figure answered.
        ///
        /// Defaults to false so snapshots written before this field existed
        /// deserialise as purchase-costed, which is what they were.
        /// </summary>
        public bool CostAtSellingPrice { get; set; }

        // ────────────────────────────────────────────────────────────────
        //  TARGET-PRICE MODEL (approved KPI) — ALL ADDITIVE.
        //
        //  Invoices saved before this model existed deserialise with empty
        //  SpecTargets and zero target totals. Consumers must treat that as
        //  "no target data" (INCOMPLETE / STALE), never as a 0% margin.
        // ────────────────────────────────────────────────────────────────

        /// <summary>One target-price result per Step 5 specification group.</summary>
        public List<SpecTargetSnapshot> SpecTargets { get; set; } = new();

        /// <summary>Σ Step 5 given price × area across the job's specs.</summary>
        public decimal TargetTotalGivenValue { get; set; }

        /// <summary>Σ final required target × area across the job's specs.</summary>
        public decimal TargetTotalRequiredValue { get; set; }

        public decimal TargetTotalDifferenceValue { get; set; }

        /// <summary>Area-weighted average Step 5 given price (AED/m²).</summary>
        public decimal TargetAverageGivenPricePerSqm { get; set; }

        /// <summary>Area-weighted average specification target before U-insert (AED/m²).</summary>
        public decimal TargetAverageSpecificationTargetPerSqm { get; set; }

        /// <summary>Area-weighted average FINAL required target (AED/m²).</summary>
        public decimal TargetAverageFinalRequiredTargetPerSqm { get; set; }

        /// <summary>Area-weighted average applied U-insert (AED/m²).</summary>
        public decimal TargetAverageAppliedUInsertPerSqm { get; set; }

        // ── Base-price judgement (the KPI trigger) ──────────────────────
        /// <summary>Area-weighted average BASE price (pre-uplift formula, AED/m²).</summary>
        public decimal TargetAverageBasePricePerSqm { get; set; }

        /// <summary>Area-weighted average GIVEN JUDGED price (AED/m²).</summary>
        public decimal TargetAverageGivenJudgedPerSqm { get; set; }

        /// <summary>Area-weighted average extra profit over base (AED/m²); null when not calculable.</summary>
        public decimal? TargetAverageExtraProfitPerSqm { get; set; }

        /// <summary>Job margin over base %; null when not calculable.</summary>
        public decimal? TargetExtraProfitMarginPercent { get; set; }

        /// <summary>Null when the margin is not calculable.</summary>
        public decimal? TargetMarginAgainstTargetPercent { get; set; }

        /// <summary>Job-level status (enum name of TargetStatus).</summary>
        public string TargetStatus { get; set; } = "";

        /// <summary>"Additional", "Included in Step 5 price", "Mixed", …</summary>
        public string TargetUInsertTreatmentSummary { get; set; } = "";

        public DateTime? TargetCalculatedAtUtc { get; set; }

        public string TargetCalculationVersion { get; set; } = "";

        public int TargetValidSpecCount { get; set; }
        public int TargetIncompleteSpecCount { get; set; }

        public List<string> TargetWarnings { get; set; } = new();
    }

    /// <summary>
    /// Persisted target-price result for ONE specification group. Additive —
    /// old snapshots simply have none.
    /// </summary>
    public class SpecTargetSnapshot
    {
        public int SpecNumber { get; set; }
        public string SpecName { get; set; } = "";
        public string GlassTypeCode { get; set; } = "";

        /// <summary>Enum name of GlassBuildType ("Dgu", "Sgu", "Lam", "DguLam").</summary>
        public string BuildType { get; set; } = "";

        public decimal GivenPricePerSqm { get; set; }
        public decimal AreaSqm { get; set; }
        public decimal GivenValue { get; set; }

        public decimal OuterGlassRatePerSqm { get; set; }
        public decimal InnerGlassRatePerSqm { get; set; }
        public decimal ThirdGlassRatePerSqm { get; set; }
        public decimal AspRatePerSqm { get; set; }
        public decimal ProcessingRatePerSqm { get; set; }
        public decimal YieldFactor { get; set; }
        public decimal TargetUpliftPercent { get; set; }

        public string FormulaText { get; set; } = "";
        public decimal SpecificationTargetPerSqm { get; set; }

        /// <summary>Enum name of UInsertTreatment.</summary>
        public string UInsertTreatment { get; set; } = "";
        public decimal UInsertRatePerSqm { get; set; }
        public string UInsertSourceLabel { get; set; } = "";
        public decimal AppliedUInsertPerSqm { get; set; }
        public decimal FinalRequiredTargetPerSqm { get; set; }

        // ── Base-price judgement (the KPI trigger) ──────────────────────
        /// <summary>Per-spec BASE price (pre-uplift formula, AED/m²), e.g. 103.82.</summary>
        public decimal BasePricePerSqm { get; set; }

        /// <summary>Per-spec GIVEN JUDGED price (AED/m²) used for the margin.</summary>
        public decimal GivenPriceJudgedPerSqm { get; set; }

        /// <summary>Per-spec extra profit over base (AED/m²); null when not calculable.</summary>
        public decimal? ExtraProfitPerSqm { get; set; }

        /// <summary>Per-spec margin over base %; null when not calculable.</summary>
        public decimal? ExtraProfitMarginPercent { get; set; }

        public decimal DifferencePerSqm { get; set; }
        public decimal RequiredTargetValue { get; set; }
        public decimal DifferenceValue { get; set; }

        public decimal? MarginAgainstTargetPercent { get; set; }

        /// <summary>Enum name of TargetStatus.</summary>
        public string Status { get; set; } = "";

        public List<string> Warnings { get; set; } = new();
    }

    /// <summary>One line of the Profit &amp; Loss breakdown.</summary>
    public class ProfitLossRowSnapshot
    {
        /// <summary>"Glass", "Spacer" or "Production".</summary>
        public string Kind { get; set; } = "";

        /// <summary>Originating Step 5 spec group number.</summary>
        public int SpecNumber { get; set; }

        /// <summary>Human-readable line description.</summary>
        public string Description { get; set; } = "";

        public int SheetWidthMm { get; set; }
        public int SheetHeightMm { get; set; }

        /// <summary>Area of one stock sheet in SQM.</summary>
        public decimal SheetSqm { get; set; }

        /// <summary>Sheets consumed (glass) or units of the charge.</summary>
        public decimal Quantity { get; set; }

        /// <summary>Applied unit price.</summary>
        public decimal PricePerSqm { get; set; }

        /// <summary>Display unit for the price (SQM / LM / QTY / TRIP).</summary>
        public string UnitLabel { get; set; } = "SQM";

        /// <summary>Extended line cost in AED.</summary>
        public decimal Amount { get; set; }

        /// <summary>
        /// PHASE G — provenance of the sheet size used for this line
        /// ("chosen in Step 4" / "from Sheet Store" / default fallback).
        /// </summary>
        public string SheetSource { get; set; } = "";

        /// <summary>
        /// Full specification the row was costed against, so a reopened
        /// invoice still shows what was quoted rather than "SPEC 2".
        /// Additive — invoices saved before this field load with "".
        /// </summary>
        public string SpecName { get; set; } = "";
    }
}
