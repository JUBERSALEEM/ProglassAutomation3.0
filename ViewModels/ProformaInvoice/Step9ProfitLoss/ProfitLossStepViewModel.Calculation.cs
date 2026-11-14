// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Calculation.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.4 — partial split. This part owns the ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.Calculation members.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.ProformaInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguLamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.DguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.LamParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator.SguParameters;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Threading;
using System.Text.RegularExpressions; // Match still used while production rows read spacer/pvb groups
using System.Threading.Tasks;
using System.Windows.Input;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    public partial class ProfitLossStepViewModel : ViewModelBase
    {
        // ══════════════════════════════════════════════════════════════════

        public async Task RecalculateAsync()
        {
            if (IsBusy) return;
            IsBusy = true;

            try
            {
                // Applying Step 4 snapshots writes SpecGroup properties that
                // the live-recalc watcher would otherwise treat as a new
                // operator edit and queue another run — that loop rebuilt
                // the overview and killed the Check Rate / SQM box.
                _suppressLiveRecalc = true;
                try { RefreshOwnedStep5GroupsFromStep4Snapshots(); }
                finally { _suppressLiveRecalc = false; }

                var rows = new List<ProfitLossRow>();
                var specAreas = new Dictionary<int, decimal>();
                var specBases = new Dictionary<int, decimal>();
                decimal glassCost = 0m;
                decimal productionCost = 0m;
                decimal uInsertInProduction = 0m;
                int skipped = 0;

                var groups = _dataGrid?.SpecGroups?.ToList()
                             ?? new List<SpecGroup>();

                foreach (var group in groups)
                {
                    if (group == null) continue;

                    // Perimeter is still measured by MeasureGroup (a future
                    // edge-work charge will need it) but is deliberately
                    // discarded here: nothing in the P&L is priced per linear
                    // metre any more.
                    var (cutSqm, _) = PnLMeasurement.MeasureGroup(group);
                    if (cutSqm <= 0m) continue;

                    // Kept for the per-specification view: cost per SQM needs
                    // the area this specification actually covers, and the
                    // row list alone cannot supply it (sheet counts are
                    // rounded up, so summing rows would overstate the area).
                    specAreas[group.GroupNumber] = cutSqm;

                    string specName = BuildStep9SpecDisplay(group);

                    // ── Parse the build out of the spec name ──────────────
                    var spacerMatch = PnLSpecParser.Spacer(specName);
                    bool spacerDetected = spacerMatch.Success;

                    // BUSINESS DECISION 2026-09-05 (owner-confirmed): EVERY
                    // air-spacer cavity pays the spacer field. A triple-
                    // glazed unit carries two ASP charges, a quad three.
                    // Classic two-pane builds report one token, so their
                    // multiplier is 1 and this changes nothing for them.
                    // A legacy DGU string with no spacer in the name
                    // clamps to 1 cavity (its price was already quoted
                    // against one).
                    int spacerCavities =
                        Math.Max(1, PnLSpecParser.SpacerCount(specName));

                    bool isDgu =
                        (group.SelectedGlassType ?? "")
                            .IndexOf("DGU", StringComparison.OrdinalIgnoreCase) >= 0
                        || spacerDetected;

                    // PHASE J — laminated: two panes bonded by PVB. Detected
                    // from the spec name or the glass type. A LAM unit that
                    // is not a DGU still has an INNER pane, which the old
                    // code missed entirely (it costed only the outer).
                    var pvbMatch = PnLSpecParser.Interlayer(specName);
                    bool pvbDetected = pvbMatch.Success;

                    bool isLam =
                        (group.SelectedGlassType ?? "")
                            .IndexOf("LAM", StringComparison.OrdinalIgnoreCase) >= 0
                        || pvbDetected;

                    // ══════════════════════════════════════════════════════
                    //  DGU+LAM — a THREE-pane hybrid:
                    //     outer + ASP + lam-outer + interlayer + lam-inner
                    //
                    //  It shows BOTH a spacer and an interlayer, which is the
                    //  only build that does. The outer/inner assumption below
                    //  costed just two of its three sheets and silently threw
                    //  a  of glass away, so panes are now taken
                    //  from the spec itself rather than assumed.
                    // ══════════════════════════════════════════════════════
                    bool isDguLam = spacerDetected && pvbDetected;

                    // Pre-margin cost basis for THIS specification's own glass
                    // type — the number a quoted rate must be measured
                    // against.
                    //
                    // Placed here deliberately: it needs isDgu / isLam /
                    // isDguLam, which are parsed out of the spec name just
                    // above. An earlier version computed it before those
                    // flags existed, which is a use-before-declaration and
                    // would not compile.
                    //
                    // The build is read from the SPEC NAME rather than from
                    // whichever Step 4 tab is open: one proforma can mix an
                    // SGU and a DGU, and using the live selection would score
                    // both against the same panel.
                    specBases[group.GroupNumber] =
                        QuoteBasisFor(group, isDgu, isLam,
                            isDguLam, spacerCavities);

                    // Either construction has a second pane to cost.
                    bool hasInnerPane = isDgu || isLam;

                    // Physical layers the UI must show. Glass used to be
                    // dropped when Sheet Store had no price, so a Clear/Clear
                    // DGU rendered as ONLY the air spacer (which is priced
                    // from Step 4, not the store). Every pane we can name is
                    // now emitted; unpriced panes stay visible and are not
                    // added into the profit total.
                    var panesToCost = CollectPanes(
                        group, specName, isDgu, isLam, isDguLam, hasInnerPane);

                    int spacerInsertAt = -1;
                    int interlayerInsertAt = -1;

                    for (int paneIndex = 0; paneIndex < panesToCost.Count; paneIndex++)
                    {
                        var pane = panesToCost[paneIndex];
                        decimal fallback = FallbackPaneRate(
                            group, pane.Side, isDgu, isLam, isDguLam);

                        var paneRow = await BuildGlassRowAsync(
                            group, pane.Side, pane.ThicknessMm, pane.Color,
                            cutSqm, fallback,
                            priceByRequiredArea: !isDgu && !isLam && !isDguLam)
                            .ConfigureAwait(true);

                        if (paneRow != null)
                        {
                            rows.Add(paneRow);
                            if (paneRow.Amount > 0m)
                                glassCost += paneRow.Amount;
                            else
                                skipped++;
                        }
                        else skipped++;

                        if (isDguLam)
                        {
                            if (paneIndex == 0) spacerInsertAt = rows.Count;
                            if (paneIndex == 1) interlayerInsertAt = rows.Count + 1;
                        }
                    }

                    // Two-pane builds: the middle layer belongs immediately
                    // after the first glass pane.
                    if (!isDguLam)
                        spacerInsertAt = rows.Count > 0 ? 1 : rows.Count;

                    // Splice a layer row into build order rather than
                    // appending it, returning the next insert position.
                    //
                    // Takes and returns the index by value: a local function
                    // cannot take `ref` to a captured outer variable, and the
                    // two call sites need different counters anyway.
                    //
                    // Falls back to appending when the position was never set,
                    // so an unrecognised shape still reports its costs instead
                    // of silently dropping them.
                    int AddLayerRow(ProfitLossRow layerRow, int at)
                    {
                        if (at >= 0 && at <= rows.Count)
                        {
                            rows.Insert(at, layerRow);
                            return at + 1;
                        }

                        rows.Add(layerRow);
                        return at;
                    }

                    // ── Production rows ───────────────────────────────────
                    // DGU+LAM is tested FIRST: it satisfies both isDgu and
                    // isLam, so either of those branches would claim it and
                    // charge only half its production.
                    if (isDguLam)
                    {
                        // ══════════════════════════════════════════════════
                        //  DGU+LAM production has THREE parts:
                        //
                        //   1. Air spacer, per SQM, which the business says
                        //      already covers cutting, tempering and
                        //      production on the DGU side.
                        //   2. The outsourced interlayer, costed at what we
                        //      PAY the laminator (InterlayerBuyPrice). The
                        //      customer-facing 110 belongs in Step 4's sell
                        //      rate; costing the job at 110 would erase the
                        //      spread the business actually earns and report
                        //      a profit lower than reality.
                        //   3. Cutting and tempering on ALL THREE panes,
                        //      each at its own rate. The rates are not
                        //      equal — tinted glass tempers dearer than
                        //      clear — so they are summed individually
                        //      rather than doubled from one figure.
                        // ══════════════════════════════════════════════════
                        // Zero unless this job was given a spacer charge in
                        // Step 4, in which case no spacer row is produced.
                        // PHASE 1-AUDIT: prefer THIS group's quoted rate
                        // (captured at the Step 4 → 5 handoff). The live
                        // panel is only a fallback for legacy groups —
                        // reading it first would silently cost a restored
                        // or multi-section invoice against whichever
                        // calculation section Step 4 happens to display.
                        decimal spacerPrice =
                            group.Step4SpacerPricePerSqm
                            ?? _dguLamVm?.SpacerPrice ?? 0m;

                        if (spacerPrice > 0m)
                        {
                            int spacerMm = TextParsing.ParseInt(spacerMatch.Groups["mm"].Value);
                            // PER-CAVITY charging (2026-09-05 decision):
                            // quantity kept identical to amount math so
                            // row integrity Quantity x Price = Amount.
                            decimal spacerAmount = Math.Round(
                                cutSqm * spacerPrice * spacerCavities, 2);

                            spacerInsertAt = AddLayerRow(new ProfitLossRow
                            {
                                Kind = "Spacer",
                                SpecNumber = group.GroupNumber,
                                SpecName = BuildStep9SpecDisplay(group),
                                Description = PnLMeasurement.BuildRowDescription(
                                    group,
                                    spacerCavities > 1
                                        ? $"{spacerMm}mm ASP x {spacerCavities} cavities (includes cutting, tempering and production)"
                                        : $"{spacerMm}mm ASP (includes cutting, tempering and production)",
                                    $"{cutSqm:0.##} SQM"),
                                Quantity = Math.Round(cutSqm * spacerCavities, 2),
                                PricePerSqm = spacerPrice,
                                Amount = spacerAmount
                            }, spacerInsertAt);

                            // Inserting the spacer pushed every later row one
                            // place right, including the boundary the
                            // interlayer was going to use. Without this the
                            // hybrid came out as outer, spacer, interlayer,
                            // lam-outer, lam-inner -- the interlayer floating
                            // above the pane it bonds.
                            if (interlayerInsertAt >= spacerInsertAt - 1)
                                interlayerInsertAt++;

                            productionCost += spacerAmount;
                        }

                        // PHASE 1-AUDIT: group-quoted buy cost first.
                        decimal interlayerBuy =
                            group.Step4InterlayerCostPerSqm
                            ?? _dguLamVm?.InterlayerBuyPrice ?? 0m;

                        // All three panes, each at its own rate. The group
                        // provenance stores the same pane-summed totals the
                        // panel computes (TotalCuttingCost/TotalTemperingCost).
                        decimal lamProcessing =
                            (group.Step4CuttingCostPerSqm
                                ?? _dguLamVm?.TotalCuttingCost ?? 0m) +
                            (group.Step4TemperingCostPerSqm
                                ?? _dguLamVm?.TotalTemperingCost ?? 0m);

                        decimal lamRate = interlayerBuy + lamProcessing;

                        if (lamRate > 0m)
                        {
                            decimal lamAmount = Math.Round(cutSqm * lamRate, 2);

                            string interlayerType = _dguLamVm?.InterlayerType ?? "PVB";
                            string interlayerMm = pvbMatch.Groups["mm"].Value;

                            string lamLabel = interlayerMm.Length > 0
                                ? $"{interlayerMm}mm {interlayerType} interlayer (outsourced) + cutting/tempering (all three panes)"
                                : $"{interlayerType} interlayer (outsourced) + cutting/tempering (all three panes)";

                            interlayerInsertAt = AddLayerRow(new ProfitLossRow
                            {
                                Kind = "Production",
                                SpecNumber = group.GroupNumber,
                                SpecName = BuildStep9SpecDisplay(group),
                                Description = PnLMeasurement.BuildRowDescription(
                                    group, lamLabel, $"{cutSqm:0.##} SQM"),
                                Quantity = Math.Round(cutSqm, 2),
                                PricePerSqm = lamRate,
                                Amount = lamAmount
                            }, interlayerInsertAt);

                            productionCost += lamAmount;
                        }
                    }
                    else if (isDgu)
                    {
                        // ══════════════════════════════════════════════════
                        //  ASP IS PRICED PER SQM, NOT PER LINEAR METRE.
                        //
                        //  Step 4's CalculateDguRate builds the sell rate as
                        //      (outer + inner) / yield + spacerPrice
                        //  where every term is AED per SQM. The ASP table
                        //  37.5 + ((w-6)/2)*2.5 therefore yields 45.00 PER SQM
                        //  at 12mm - the figure the business quotes.
                        //
                        //  This code multiplied that same 45.00 by the
                        //  PERIMETER in linear metres. On 20 panes of
                        //  1.5 x 1.2 m that is 108 LM against 36 SQM, so the
                        //  spacer cost came out 4,860 instead of 1,620 -
                        //  overstated by 3,240 AED, three times too high, and
                        //  the error grows with every small pane because
                        //  perimeter-to-area rises as panes shrink.
                        //
                        //  Costing must use the same unit the price was
                        //  defined in, so this is now cut area x rate.
                        //
                        //  The stated business rule is also that the ASP
                        //  price INCLUDES cutting, tempering and production
                        //  for DGU, which is why there is no separate
                        //  production line in this branch.
                        // ══════════════════════════════════════════════════
                        // PHASE 1-AUDIT: THIS group's quoted spacer rate
                        // first; live panel is the legacy fallback only.
                        decimal spacerPrice =
                            group.Step4SpacerPricePerSqm
                            ?? _dguVm?.SpacerPrice ?? 0m;

                        if (spacerPrice > 0m && cutSqm > 0m)
                        {
                            int spacerMm = spacerDetected
                                ? TextParsing.ParseInt(spacerMatch.Groups["mm"].Value)
                                : SheetStorePricingService.ParseMm(_dguVm?.SpacerWidth);

                            // PER-CAVITY charging (2026-09-05 decision):
                            // one cavity = x1 (identical to before);
                            // two spacer tokens in a triple build = x2.
                            decimal amount = Math.Round(
                                cutSqm * spacerPrice * spacerCavities, 2);

                            spacerInsertAt = AddLayerRow(new ProfitLossRow
                            {
                                Kind = "Spacer",
                                SpecNumber = group.GroupNumber,
                                SpecName = BuildStep9SpecDisplay(group),
                                Description = PnLMeasurement.BuildRowDescription(
                                    group,
                                    spacerCavities > 1
                                        ? $"{spacerMm}mm ASP x {spacerCavities} cavities (includes cutting, tempering and production)"
                                        : $"{spacerMm}mm ASP (includes cutting, tempering and production)",
                                    $"{cutSqm:0.##} SQM"),
                                Quantity = Math.Round(cutSqm * spacerCavities, 2),
                                PricePerSqm = spacerPrice,
                                Amount = amount
                            }, spacerInsertAt);

                            productionCost += amount;
                        }
                    }
                    else if (isLam)
                    {
                        // PHASE J — laminated production: PVB interlayer
                        // material plus cutting and tempering on BOTH panes.
                        // Priced per SQM of laminated area (unlike the DGU
                        // spacer, which is per linear metre of perimeter).
                        // PHASE 1-AUDIT: THIS group's quoted rates first.
                        decimal pvbRate =
                            group.Step4InterlayerCostPerSqm
                            ?? _lamVm?.PvbMaterialRate ?? 0m;

                        decimal processing =
                            (group.Step4CuttingCostPerSqm
                                ?? ((_lamVm?.OuterCuttingCost ?? 0m) +
                                    (_lamVm?.InnerCuttingCost ?? 0m))) +
                            (group.Step4TemperingCostPerSqm
                                ?? ((_lamVm?.OuterTemperingCost ?? 0m) +
                                    (_lamVm?.InnerTemperingCost ?? 0m)));

                        decimal lamRate = pvbRate + processing;

                        if (lamRate > 0m)
                        {
                            decimal amount = Math.Round(cutSqm * lamRate, 2);

                            string pvbMm = pvbDetected
                                ? pvbMatch.Groups["mm"].Value
                                : "";

                            // Report the interlayer the spec actually names.
                            // Now that SGP and EVA are matched, hardcoding
                            // "PVB" would print the wrong material on a real
                            // cost line.
                            string pvbType = pvbDetected
                                ? pvbMatch.Groups["type"].Value.ToUpperInvariant()
                                : "PVB";

                            string label = pvbMm.Length > 0
                                ? $"{pvbMm}mm {pvbType} interlayer + cutting/tempering (both panes)"
                                : $"{pvbType} interlayer + cutting/tempering (both panes)";

                            spacerInsertAt = AddLayerRow(new ProfitLossRow
                            {
                                Kind = "Production",
                                SpecNumber = group.GroupNumber,
                                SpecName = BuildStep9SpecDisplay(group),
                                Description = PnLMeasurement.BuildRowDescription(
                                    group, label, $"{cutSqm:0.##} SQM"),
                                Quantity = Math.Round(cutSqm, 2),
                                PricePerSqm = lamRate,
                                Amount = amount
                            }, spacerInsertAt);

                            productionCost += amount;
                        }
                    }
                    else
                    {
                        // Single glazing: the business wants glass priced by
                        // required SQM, with cutting and tempering broken out
                        // as separate SQM-based charges rather than one merged
                        // production line.
                        // PHASE 1-AUDIT: THIS group's quoted rates first.
                        decimal cuttingRate =
                            group.Step4CuttingCostPerSqm
                            ?? _sguVm?.CuttingCostPerSqm ?? 0m;
                        decimal temperingRate =
                            group.Step4TemperingCostPerSqm
                            ?? _sguVm?.TemperingCostPerSqm ?? 0m;

                        if (cuttingRate > 0m)
                        {
                            decimal amount = Math.Round(cutSqm * cuttingRate, 2);
                            rows.Add(new ProfitLossRow
                            {
                                Kind = "Production",
                                SpecNumber = group.GroupNumber,
                                SpecName = BuildStep9SpecDisplay(group),
                                Description = PnLMeasurement.BuildRowDescription(
                                    group, "Cutting", $"{cutSqm:0.##} SQM"),
                                Quantity = Math.Round(cutSqm, 2),
                                PricePerSqm = cuttingRate,
                                Amount = amount
                            });
                            productionCost += amount;
                        }

                        if (temperingRate > 0m)
                        {
                            decimal amount = Math.Round(cutSqm * temperingRate, 2);
                            rows.Add(new ProfitLossRow
                            {
                                Kind = "Production",
                                SpecNumber = group.GroupNumber,
                                SpecName = BuildStep9SpecDisplay(group),
                                Description = PnLMeasurement.BuildRowDescription(
                                    group, "Tempering", $"{cutSqm:0.##} SQM"),
                                Quantity = Math.Round(cutSqm, 2),
                                PricePerSqm = temperingRate,
                                Amount = amount
                            });
                            productionCost += amount;
                        }
                    }
                }

                AppendStep6Costs(rows, ref productionCost, ref uInsertInProduction);

                // ── Publish rows ──────────────────────────────────────────
                Rows.Clear();
                foreach (var r in rows) Rows.Add(r);
                OnPropertyChanged(nameof(HasRows));

                // ── Publish the same rows grouped by specification ────────
                //
                // Built from the finished flat list rather than inside the
                // costing loop: the loop already splices layer rows into
                // build order, and rebuilding that ordering a second time
                // would be two places to keep in step. Grouping afterwards
                // inherits the order for free.
                //
                // SpecCostGroup instances are reused in place. The Check
                // Rate / SQM box is bound to QuotedRatePerSqm on that
                // object; Clear()+new would destroy the TextBox on every
                // live recalc and make the field unusable.
                PublishSpecGroups(rows, specAreas, specBases);

                // ── Target profitability (approved KPI model) ─────────────
                // Runs AFTER the actual-cost rows publish: the two models are
                // independent but share the same Step 5 measurement pass.
                // The target model must never be able to break the wizard, so
                // it lives inside this try block and reports through
                // CalculationWarnings.
                try
                {
                    RebuildTargetResults();
                }
                catch (Exception targetEx)
                {
                    AppLogService.Debug($"[ProfitLossStepViewModel] Target model failed: {ExceptionMessages.Deep(targetEx)}");
                }

                // ── Totals ────────────────────────────────────────────────
                decimal gross = 0m;
                try { gross = _grossProvider?.Invoke() ?? 0m; }
                catch (Exception ex)
                {
                    AppLogService.Debug($"[ProfitLossStepViewModel] gross provider: {ExceptionMessages.Deep(ex)}");
                }

                GrossVatInclusive = gross;
                NetExVat = Math.Round(gross / VatDivisor, 2);
                TotalGlassCost = Math.Round(glassCost, 2);
                TotalProductionCost = Math.Round(productionCost, 2);

                decimal profit = NetExVat - TotalGlassCost - TotalProductionCost;
                Profit = Math.Round(profit, 2);

                // Only call it a loss when there is revenue to lose against.
                // With gross at zero the negative figure is simply
                // "cost known, revenue not entered yet" - painting that red
                // tells the user they are losing money when they are not.
                IsLoss = gross > 0m && Profit < 0m;

                // ══════════════════════════════════════════════════════════
                //  COST-BASED MARGIN (Option A - Main KPI)
                //
                //  Formula: (Gross Revenue - Base Cost) / Base Cost × 100
                //
                //  Example: Base 160.59, Selling 160, Area 20 SQM
                //    Total Base Cost = 160.59 × 20 = 3,211.80
                //    Gross Revenue = 160 × 20 = 3,200
                //    Margin = (3,200 - 3,211.80) / 3,211.80 × 100 = -0.4%
                //
                //  Uses GROSS revenue (including VAT), not net.
                //  Uses BASE COST (with yield factor), not actual P&L rows.
                // ══════════════════════════════════════════════════════════
                decimal basisTotal = 0m;
                foreach (var group in groups)
                {
                    if (!specBases.TryGetValue(group.GroupNumber, out decimal basis))
                        continue;
                    if (!specAreas.TryGetValue(group.GroupNumber, out decimal area))
                        continue;
                    if (basis <= 0m || area <= 0m) continue;
                    basisTotal += Math.Round(basis * area, 2);
                }

                decimal computedUInsert = ComputePriceBasedUInsertCost();
                PriceBasedUInsertCost = computedUInsert > 0m
                    ? Math.Round(computedUInsert, 2)
                    : Math.Round(uInsertInProduction, 2);

                PriceBasedBasisCost = basisTotal > 0m
                    ? Math.Round(basisTotal, 2)
                    : Math.Max(0m, Math.Round(
                        TotalGlassCost + TotalProductionCost
                        - Math.Round(uInsertInProduction, 2), 2));

                PriceBasedTotalActualCost = Math.Round(
                    PriceBasedBasisCost + PriceBasedUInsertCost, 2);

                // Cost-based profit using GROSS revenue (not net)
                PriceBasedActualProfit = Math.Round(GrossVatInclusive - PriceBasedTotalActualCost, 2);

                // ══════════════════════════════════════════════════════════
                //  THREE MARGIN CALCULATIONS
                // ══════════════════════════════════════════════════════════

                // 1. COST-BASED MARGIN (Base Cost Only - no U-insert)
                //    Formula: (Revenue - Base Cost) / Base Cost × 100
                //    Shows: Glass + ASP margin (ignoring U-insert)
                //    Use: Compare to Step 4's margin setting
                decimal baseCostProfit = Math.Round(GrossVatInclusive - PriceBasedBasisCost, 2);
                MarginPercent = PriceBasedBasisCost > 0m
                    ? Math.Round(baseCostProfit / PriceBasedBasisCost * 100m, 2)
                    : 0m;

                // 2. OVERALL MARGIN (Actual Total Cost) ← MOST IMPORTANT
                //    Formula: (Revenue - Actual Cost) / Actual Cost × 100
                //    Uses ACTUAL costs: glass rows + production (spacer, cutting,
                //    tempering, wastage, U-insert) — NOT the Step 4 basis.
                //    This is the REAL job profitability for decision making.
                decimal actualTotalCost = TotalGlassCost + TotalProductionCost;
                decimal actualProfit = Math.Round(NetExVat - actualTotalCost, 2);
                OverallMarginPercent = actualTotalCost > 0m
                    ? Math.Round(actualProfit / actualTotalCost * 100m, 2)
                    : 0m;

                // 3. NET MARGIN (Accounting perspective)
                //    Formula: (Revenue - Actual Cost) / Revenue × 100
                //    Shows: Profit as % of revenue using actual costs
                PriceBasedMarginPercent = NetExVat > 0m
                    ? Math.Round(actualProfit / NetExVat * 100m, 2)
                    : 0m;

                StatusMessage = BuildStatus(
                    groups.Count, rows.Count, skipped, gross);
                RaiseStep9HealthNotifications();

                // ══════════════════════════════════════════════════════════
                //  AUTO-SYNC: Refresh all 3 analysis sub-tabs after recalc
                //  so they always show current data without manual refresh.
                // ══════════════════════════════════════════════════════════
                try
                {
                    ServiceProfitability?.RefreshAnalysis();
                    SheetProfitability?.RefreshAnalysis();
                    GlassTypeComparison?.RefreshAnalysis();
                }
                catch (Exception syncEx)
                {
                    AppLogService.Debug($"[ProfitLossStepViewModel] Sub-tab sync failed: {ExceptionMessages.Deep(syncEx)}");
                }
            }
            catch (Exception ex)
            {
                // A P&L failure must never break the wizard.
                AppLogService.Debug($"[ProfitLossStepViewModel] Recalculate failed: {ExceptionMessages.Deep(ex)}");
                StatusMessage =
                    "Could not complete the calculation — check Sheet Store data and try again.";
                RaiseStep9HealthNotifications();
            }
            finally
            {
                IsBusy = false;
            }
        }

        /// <summary>
        /// Keep existing SpecCostGroup objects so the Check Rate / SQM
        /// TextBox stays focused and keeps whatever the operator typed.
        /// Only add/remove/reorder when the spec set itself changed.
        /// </summary>
        private void PublishSpecGroups(
            List<ProfitLossRow> rows,
            Dictionary<int, decimal> specAreas,
            Dictionary<int, decimal> specBases)
        {
            var existingByNumber = SpecGroupsView
                .Where(g => g.SpecNumber > 0)
                .GroupBy(g => g.SpecNumber)
                .ToDictionary(g => g.Key, g => g.Last());
            var existingInvoiceWide = SpecGroupsView
                .FirstOrDefault(g => g.SpecNumber <= 0);

            var desired = new List<SpecCostGroup>();
            foreach (var bySpec in rows
                         .GroupBy(r => r.SpecNumber)
                         .OrderBy(g => g.Key <= 0 ? int.MaxValue : g.Key))
            {
                var first = bySpec.First();
                int specNumber = bySpec.Key;
                string specName = first.SpecName ?? "";
                decimal totalCost = Math.Round(bySpec.Sum(r => r.Amount), 2);
                decimal cutSqm = specAreas.TryGetValue(specNumber, out decimal area)
                    ? Math.Round(area, 2)
                    : 0m;
                decimal quoteBasis = specBases.TryGetValue(specNumber, out decimal basis)
                    ? basis
                    : 0m;

                SpecCostGroup group;
                if (specNumber > 0 && existingByNumber.TryGetValue(specNumber, out var keep))
                    group = keep;
                else if (specNumber <= 0 && existingInvoiceWide != null)
                    group = existingInvoiceWide;
                else
                    group = new SpecCostGroup { SpecNumber = specNumber };

                group.SpecNumber = specNumber;
                group.ApplyCalculatedState(specName, totalCost, cutSqm, quoteBasis, bySpec);
                desired.Add(group);
            }

            SyncSpecGroupsView(desired);
            OnPropertyChanged(nameof(HasSpecGroups));
        }

        private void SyncSpecGroupsView(List<SpecCostGroup> desired)
        {
            for (int i = SpecGroupsView.Count - 1; i >= 0; i--)
            {
                if (!desired.Contains(SpecGroupsView[i]))
                    SpecGroupsView.RemoveAt(i);
            }

            for (int i = 0; i < desired.Count; i++)
            {
                var item = desired[i];
                int current = SpecGroupsView.IndexOf(item);
                if (current == i) continue;

                if (current >= 0)
                    SpecGroupsView.Move(current, i);
                else if (i < SpecGroupsView.Count)
                    SpecGroupsView.Insert(i, item);
                else
                    SpecGroupsView.Add(item);
            }
        }


    }
}
