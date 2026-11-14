// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.QuoteBasis.cs
// PROJECT: ProGlassAutomation
// ─────────────────────────────────────────────────────────────────────────
// Phase 3.4 — partial split. This part owns the ViewModels/ProformaInvoice/Step9ProfitLoss/ProfitLossStepViewModel.QuoteBasis members.
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
        //  QUOTE BASIS PER SPECIFICATION
        //
        //  Mirrors Step 4's rate formulas, stopping one step short: every
        //  Calculate*Rate is  basis x (1 + margin%),  and this returns the
        //  basis. That is the number a quoted rate has to be measured
        //  against.
        //
        //  Worked example, 6mm Clear FT + 12mm ASP + 6mm Clear FT:
        //      (25 + 25) / 0.85 + 45 = 103.82
        //      x 1.15 = 119.39     x 1.10 = 114.20
        //  So entering 114.20 reads as 10%, which is what the shop expects.
        //
        //  The build is identified from the SPEC NAME first (isDgu / isLam /
        //  isDguLam were already parsed out of it) and only falls back to the
        //  group's glass-type text. A proforma can hold several different
        //  builds at once, so using the live Step 4 selection would score
        //  every specification against whichever tab was last open.
        //
        //  Returns 0 when the owning panel is not bound, which makes the
        //  caller fall back to materials cost and say so on screen rather
        //  than silently quoting a wrong percentage.
        // ══════════════════════════════════════════════════════════════════

        private decimal QuoteBasisFor(
            SpecGroup group, bool isDgu, bool isLam, bool isDguLam,
            int spacerCavities = 1)
        {
            // Authoritative model for Step 9 rate checking:
            //   Step 5 group metadata copied from Step 4 wins per spec.
            //   Live Step 4 VM fields are only legacy fallback when the group
            //   has no pane identity/rate at all. A multi-spec PI must never
            //   score SPEC 2 with the currently visible Step 4 tab.
            spacerCavities = Math.Max(1, spacerCavities);
            if (group == null) return 0m;

            string type = (group.SelectedGlassType ?? "").Trim().ToUpperInvariant();
            bool hasOuterHandoff = HasOuterPaneHandoff(group);
            bool hasInnerHandoff = HasInnerPaneHandoff(group);
            decimal yield = EffectiveYieldFactor();

            if (isDguLam || type == "DGU+LAM")
            {
                // DGU+LAM still lacks full physical sheet fields on SpecGroup
                // for all three panes. Use stored laminate pair rates where
                // present, otherwise fall back only for legacy groups without
                // handoff. This branch is intentionally conservative.
                var vm = _dguLamVm;
                decimal outer = RateFromGroupOrLegacy(
                    group.OuterSheetPricePerSqm, hasOuterHandoff,
                    vm?.OuterBasePrice ?? 0m);
                decimal lamInner = RateFromGroupOrLegacy(
                    group.InnerSheetPricePerSqm, hasInnerHandoff,
                    vm?.LamInnerBasePrice ?? 0m);
                decimal lamOuter = vm?.LamOuterBasePrice ?? 0m;
                decimal spacer = group.Step4SpacerPricePerSqm
                    ?? (hasOuterHandoff || hasInnerHandoff ? 0m : (vm?.SpacerPrice ?? 0m));
                decimal processing = (group.Step4CuttingCostPerSqm ?? (hasOuterHandoff || hasInnerHandoff ? 0m : (vm?.TotalCuttingCost ?? 0m)))
                    + (group.Step4TemperingCostPerSqm ?? (hasOuterHandoff || hasInnerHandoff ? 0m : (vm?.TotalTemperingCost ?? 0m)))
                    + (group.Step4InterlayerCostPerSqm ?? (hasOuterHandoff || hasInnerHandoff ? 0m : (vm?.InterlayerSellPrice ?? 0m)));

                if (outer <= 0m || lamOuter <= 0m || lamInner <= 0m || spacer <= 0m || yield <= 0m)
                    return 0m;

                return Math.Round((outer + lamOuter + lamInner) / yield
                    + processing
                    + spacer * spacerCavities, 2);
            }

            if (isLam || type == "LAM")
            {
                var vm = _lamVm;
                decimal outer = RateFromGroupOrLegacy(
                    group.OuterSheetPricePerSqm, hasOuterHandoff,
                    vm?.OuterBasePrice ?? 0m);
                decimal inner = RateFromGroupOrLegacy(
                    group.InnerSheetPricePerSqm, hasInnerHandoff,
                    vm?.InnerBasePrice ?? 0m);
                decimal processing = (group.Step4InterlayerCostPerSqm ?? (hasOuterHandoff || hasInnerHandoff ? 0m : (vm?.PvbMaterialRate ?? 0m)))
                    + (group.Step4CuttingCostPerSqm ?? (hasOuterHandoff || hasInnerHandoff ? 0m : ((vm?.OuterCuttingCost ?? 0m) + (vm?.InnerCuttingCost ?? 0m))))
                    + (group.Step4TemperingCostPerSqm ?? (hasOuterHandoff || hasInnerHandoff ? 0m : ((vm?.OuterTemperingCost ?? 0m) + (vm?.InnerTemperingCost ?? 0m))));

                if (outer <= 0m || inner <= 0m || yield <= 0m)
                    return 0m;

                return Math.Round((outer + inner) / yield + processing, 2);
            }

            if (isDgu || type == "DGU")
            {
                var vm = _dguVm;
                decimal outer = RateFromGroupOrLegacy(
                    group.OuterSheetPricePerSqm, hasOuterHandoff,
                    vm?.OuterBasePrice ?? 0m);
                decimal inner = RateFromGroupOrLegacy(
                    group.InnerSheetPricePerSqm, hasInnerHandoff,
                    vm?.InnerBasePrice ?? 0m);
                decimal spacer = group.Step4SpacerPricePerSqm
                    ?? (hasOuterHandoff || hasInnerHandoff ? 0m : (vm?.SpacerPrice ?? 0m));

                if (outer <= 0m || inner <= 0m || spacer <= 0m || yield <= 0m)
                    return 0m;

                return Math.Round((outer + inner) / yield
                    + spacer * spacerCavities, 2);
            }

            if (type == "SGU" || type.Length == 0 || type.Contains("SINGLE"))
            {
                var vm = _sguVm;
                decimal outer = RateFromGroupOrLegacy(
                    group.OuterSheetPricePerSqm, hasOuterHandoff,
                    vm?.BasePricePerSqm ?? 0m);
                decimal processing = (group.Step4CuttingCostPerSqm ?? (hasOuterHandoff ? 0m : (vm?.CuttingCostPerSqm ?? 0m)))
                    + (group.Step4TemperingCostPerSqm ?? (hasOuterHandoff ? 0m : (vm?.TemperingCostPerSqm ?? 0m)));

                if (outer <= 0m || yield <= 0m)
                    return 0m;

                return Math.Round(outer / yield + processing, 2);
            }

            return 0m;
        }

        private static bool HasOuterPaneHandoff(SpecGroup group) =>
            !string.IsNullOrWhiteSpace(group?.OuterGlassColor)
            || (group?.OuterSheetPricePerSqm ?? 0m) > 0m
            || (group?.HasOuterSheet ?? false);

        private static bool HasInnerPaneHandoff(SpecGroup group) =>
            !string.IsNullOrWhiteSpace(group?.InnerGlassColor)
            || (group?.InnerSheetPricePerSqm ?? 0m) > 0m
            || (group?.HasInnerSheet ?? false);

        private static decimal RateFromGroupOrLegacy(
            decimal groupRate, bool hasAuthoritativeHandoff, decimal liveFallback)
        {
            if (groupRate > 0m) return groupRate;
            return hasAuthoritativeHandoff ? 0m : liveFallback;
        }


        /// <summary>
        /// Before Step 9 generates quote basis/target rows, re-apply the
        /// latest Step 4 section snapshot to every Step 5 group that is still
        /// Step-4-owned. This closes the real stale-data gap: changing a pane
        /// from Clear to Obscure Clear Ramly can leave the Step 4 estimated
        /// price numerically unchanged, so the old Step 5 sync was not always
        /// triggered even though the pane/sheet identity changed.
        /// </summary>
        private void RefreshOwnedStep5GroupsFromStep4Snapshots()
        {
            if (_step4SnapshotProvider == null || _dataGrid?.SpecGroups == null)
                return;

            IReadOnlyList<Step4PricingSnapshot> sections;
            try
            {
                sections = _step4SnapshotProvider.Invoke()
                    ?? Array.Empty<Step4PricingSnapshot>();
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[Step9] Step4 snapshot refresh failed: {ExceptionMessages.Deep(ex)}");
                return;
            }

            int count = Math.Min(sections.Count, _dataGrid.SpecGroups.Count);
            for (int i = 0; i < count; i++)
            {
                var section = sections[i];
                var group = _dataGrid.SpecGroups[i];
                if (section == null || group == null) continue;
                if (string.IsNullOrWhiteSpace(section.GeneratedSpecName)) continue;

                bool hasNoStep5Spec = string.IsNullOrWhiteSpace(group.SelectedSpecName);
                bool isFreshBootstrap = hasNoStep5Spec
                    && !group.HasManualStep5SpecOverride
                    && !group.HasManualStep5GlassTypeOverride;

                bool markersConfirmCurrentStep4Selection =
                    !string.IsNullOrWhiteSpace(group.LastSyncedStep4SpecName)
                    && !string.IsNullOrWhiteSpace(group.LastSyncedStep4GlassType)
                    && string.Equals(group.LastSyncedStep4SpecName,
                        section.GeneratedSpecName,
                        StringComparison.OrdinalIgnoreCase)
                    && string.Equals(group.LastSyncedStep4GlassType,
                        section.SelectedGlassTypeCode,
                        StringComparison.OrdinalIgnoreCase);

                bool selectedSpecMatchesStep4 =
                    string.Equals(group.SelectedSpecName,
                        section.GeneratedSpecName,
                        StringComparison.OrdinalIgnoreCase);

                bool specIsStep4Owned = isFreshBootstrap
                    || selectedSpecMatchesStep4
                    || (!group.HasManualStep5SpecOverride
                        && !string.IsNullOrWhiteSpace(group.LastSyncedStep4SpecName)
                        && string.Equals(group.SelectedSpecName,
                            group.LastSyncedStep4SpecName,
                            StringComparison.OrdinalIgnoreCase));

                bool selectedGlassTypeMatchesStep4 =
                    string.Equals(group.SelectedGlassType,
                        section.SelectedGlassTypeCode,
                        StringComparison.OrdinalIgnoreCase);

                bool glassTypeIsStep4Owned = isFreshBootstrap
                    || selectedGlassTypeMatchesStep4
                    || (!group.HasManualStep5GlassTypeOverride
                        && string.IsNullOrWhiteSpace(group.SelectedGlassType))
                    || (!group.HasManualStep5GlassTypeOverride
                        && markersConfirmCurrentStep4Selection)
                    || (!group.HasManualStep5GlassTypeOverride
                        && !string.IsNullOrWhiteSpace(group.LastSyncedStep4GlassType)
                        && string.Equals(group.SelectedGlassType,
                            group.LastSyncedStep4GlassType,
                            StringComparison.OrdinalIgnoreCase));

                if (specIsStep4Owned && glassTypeIsStep4Owned)
                    group.ApplyStep4PricingSnapshot(section);
            }
        }

        /// <summary>
        /// Display/carry the Step 9 specification using Step 4/Step 5 pane
        /// identity, not blindly the generated text. If Step 4 says SPEC 2
        /// inner is Obscure Clear Ramly, every Step 9 overview/inspector row
        /// must show that even when the saved human spec string still says
        /// Clear from an older/fallback generation.
        /// </summary>
        private static string BuildStep9SpecDisplay(SpecGroup group)
        {
            string specName = group?.SelectedSpecName ?? string.Empty;
            if (group == null || string.IsNullOrWhiteSpace(specName))
                return specName;

            var matches = PnLSpecParser.GlassMatches(specName).ToList();
            if (matches.Count == 0)
                return specName;

            var replacements = new List<(int Index, int Length, string Value)>();
            void AddColourReplacement(Match match, string colour)
            {
                if (string.IsNullOrWhiteSpace(colour)) return;
                var g = match.Groups["color"];
                if (!g.Success || g.Index < 0 || g.Length <= 0) return;
                replacements.Add((g.Index, g.Length, colour.Trim()));
            }

            AddColourReplacement(matches[0], group.OuterGlassColor ?? string.Empty);
            if (matches.Count > 1)
                AddColourReplacement(matches[^1], group.InnerGlassColor ?? string.Empty);

            if (replacements.Count == 0)
                return specName;

            var sb = new System.Text.StringBuilder(specName);
            foreach (var r in replacements.OrderByDescending(r => r.Index))
                sb.Remove(r.Index, r.Length).Insert(r.Index, r.Value);

            return sb.ToString();
        }

        /// <summary>
        /// Physical panes this specification must show. Parsed from the
        /// spec name first; missing DGU/LAM panes are synthesised from the
        /// live Step 4 panel so a Clear/Clear unit cannot collapse to
        /// "spacer only" just because a regex or the Sheet Store missed.
        ///
        /// FIX-2026-09-05 (3+ pane builds): a triple-glazed DGU carries
        /// THREE sheets of glass. The old outer+inner path took the spec's
        /// panes from ResolveTwoPanes, so the MIDDLE sheet never became a
        /// material row — the P&L silently costed a third of the glass
        /// away on every triple unit (the very mistake this method's own
        /// header says it was built to stop). Panes now come from
        /// ResolveAllPanes: outer, every middle (in spec order), inner.
        /// </summary>
        private List<PnLPane> CollectPanes(
            SpecGroup group,
            string specName, bool isDgu, bool isLam, bool isDguLam, bool hasInnerPane)
        {
            var panes = new List<PnLPane>();
            var matches = PnLSpecParser.GlassMatches(specName);

            if (isDguLam)
            {
                string[] labels = { "Outer", "Lam Outer", "Lam Inner" };
                if (matches.Count > 0)
                {
                    for (int i = 0; i < matches.Count; i++)
                    {
                        string label = i < labels.Length ? labels[i] : $"Pane {i + 1}";
                        panes.Add(PnLSpecParser.FromMatch(matches[i], label));
                    }
                }
                else if (_dguLamVm != null)
                {
                    panes.Add(PnLSpecParser.FromParts(
                        "Outer",
                        SheetStorePricingService.ParseMm(_dguLamVm.OuterThickness),
                        _dguLamVm.OuterGlassColor));
                    panes.Add(PnLSpecParser.FromParts(
                        "Lam Outer",
                        SheetStorePricingService.ParseMm(_dguLamVm.LamOuterThickness),
                        _dguLamVm.LamOuterColor));
                    panes.Add(PnLSpecParser.FromParts(
                        "Lam Inner",
                        SheetStorePricingService.ParseMm(_dguLamVm.LamInnerThickness),
                        _dguLamVm.LamInnerColor));
                }

                return EnforceStep4PaneIdentity(group, panes, isDguLam);
            }

            // FIX-2026-09-05: full ordered pane list (Outer/Middle…/Inner).
            // Two-pane builds keep the classic pair; the middle panes of
            // a 3+ pane build are material rows now, in spec order.
            var allPanes = PnLSpecParser.ResolveAllPanes(specName);
            for (int i = 0; i < allPanes.Count; i++)
                panes.Add(allPanes[i]);

            // Synthesis gaps, never synthesis replacement: when the spec
            // parse gives NOTHING for a side that the build type implies,
            // fall back to the live Step 4 panel (the Clear/Clear guard).
            if (panes.Count == 0)
            {
                if (isDgu && _dguVm != null)
                    panes.Add(PnLSpecParser.FromParts(
                        "Outer",
                        SheetStorePricingService.ParseMm(_dguVm.OuterThickness),
                        _dguVm.OuterGlassColor));
                else if (isLam && _lamVm != null)
                    panes.Add(PnLSpecParser.FromParts(
                        "Outer",
                        SheetStorePricingService.ParseMm(_lamVm.OuterThickness),
                        _lamVm.OuterGlassColor));
                else if (!hasInnerPane && _sguVm != null && matches.Count == 0)
                    panes.Add(PnLSpecParser.FromParts(
                        "Outer",
                        SheetStorePricingService.ParseMm(_sguVm.SelectedThickness),
                        _sguVm.GlassColor));
            }

            // A two-pane build with only the outer pane named must still
            // gain its synthesised inner — parsing one colour is NOT proof
            // the inner sheet is absent (FUZZY part-colours exist in the
            // wild). Middle panes are never synthesised: the spec is the
            // only source that can name a middle.
            if (hasInnerPane)
            {
                bool innerPresent = false;
                for (int i = 0; i < panes.Count; i++)
                {
                    if (string.Equals(panes[i].Side, "Inner",
                            StringComparison.OrdinalIgnoreCase))
                    {
                        innerPresent = true;
                        break;
                    }
                }

                if (!innerPresent)
                {
                    if (isDgu && _dguVm != null)
                        panes.Add(PnLSpecParser.FromParts(
                            "Inner",
                            SheetStorePricingService.ParseMm(_dguVm.InnerThickness),
                            _dguVm.InnerGlassColor));
                    else if (isLam && _lamVm != null)
                        panes.Add(PnLSpecParser.FromParts(
                            "Inner",
                            SheetStorePricingService.ParseMm(_lamVm.InnerThickness),
                            _lamVm.InnerGlassColor));
                }
            }

            return EnforceStep4PaneIdentity(group, panes, isDguLam);
        }

        /// <summary>
        /// Step 5/Step 4 pane provenance is the authoritative identity for
        /// normal two-pane DGU/LAM costing. The human spec string remains a
        /// useful parser source, but it can be affected by wording extras or
        /// legacy imported text. Never let a parsed/fallback "Clear" replace
        /// an explicit Step 4 inner colour such as "Obscure Clear Ramly" —
        /// that also changes the Sheet Store lookup and can move the row from
        /// the 2500 × 1875 sheet to the outer 3210 × 2250 sheet.
        /// </summary>
        private static List<PnLPane> EnforceStep4PaneIdentity(
            SpecGroup group,
            List<PnLPane> panes,
            bool isDguLam)
        {
            if (group == null || panes.Count == 0 || isDguLam)
                return panes;

            string outerColor = (group.OuterGlassColor ?? string.Empty).Trim();
            string innerColor = (group.InnerGlassColor ?? string.Empty).Trim();
            if (outerColor.Length == 0 && innerColor.Length == 0)
                return panes;

            var fixedPanes = new List<PnLPane>(panes.Count);
            foreach (var pane in panes)
            {
                bool isInner = pane.Side.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0;
                bool isOuter = pane.Side.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0
                    && pane.Side.IndexOf("Lam", StringComparison.OrdinalIgnoreCase) < 0;

                string authoritative = isInner
                    ? innerColor
                    : isOuter
                        ? outerColor
                        : string.Empty;

                fixedPanes.Add(!string.IsNullOrWhiteSpace(authoritative)
                    ? PnLSpecParser.FromParts(pane.Side, pane.ThicknessMm, authoritative)
                    : pane);
            }

            return fixedPanes;
        }

        /// <summary>
        /// Step 4 already holds a typed rate for every pane. That is what
        /// the spacer uses, and it is what glass must fall back to when
        /// Sheet Store has no matching colour/thickness — otherwise the
        /// sandwich collapses to "air spacer" on screen.
        /// </summary>
        private decimal FallbackPaneRate(
            SpecGroup group, string side, bool isDgu, bool isLam, bool isDguLam)
        {
            // Step 4 → Step 5 stores the authoritative pane box prices per
            // specification. Prefer those stored prices over the live Step 4
            // tab, because a PI can contain multiple specs and the visible tab
            // may belong to a different section.
            bool inner = side.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0
                      || side.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0
                      || side.IndexOf("Lam", StringComparison.OrdinalIgnoreCase) >= 0;

            if (isDguLam)
            {
                if (side.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                    return group.InnerSheetPricePerSqm > 0m
                        ? group.InnerSheetPricePerSqm
                        : (_dguLamVm?.LamInnerBasePrice ?? 0m);
                if (side.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                    return _dguLamVm?.LamOuterBasePrice ?? 0m;
                return group.OuterSheetPricePerSqm > 0m
                    ? group.OuterSheetPricePerSqm
                    : (_dguLamVm?.OuterBasePrice ?? 0m);
            }

            decimal grouped = inner
                ? group.InnerSheetPricePerSqm
                : group.OuterSheetPricePerSqm;
            if (grouped > 0m)
                return grouped;

            if (isDgu)
                return inner ? (_dguVm?.InnerBasePrice ?? 0m) : (_dguVm?.OuterBasePrice ?? 0m);
            if (isLam)
                return inner ? (_lamVm?.InnerBasePrice ?? 0m) : (_lamVm?.OuterBasePrice ?? 0m);
            return grouped > 0m ? grouped : (_sguVm?.BasePricePerSqm ?? 0m);
        }

        /// <summary>
        /// Prices one pane. Step 4 pane rate wins; Sheet Store is only the
        /// fallback; if both are empty the row is STILL emitted (amount 0)
        /// so the physical build stays visible. Returns null only when the
        /// pane itself cannot be identified.
        /// </summary>
        private async Task<ProfitLossRow?> BuildGlassRowAsync(
            SpecGroup group,
            string side,
            int thicknessMm,
            string color,
            decimal cutSqm,
            decimal fallbackRate,
            bool priceByRequiredArea)
        {
            int specNumber = group.GroupNumber;
            color = (color ?? "").Trim();

            if (thicknessMm <= 0 || color.Length == 0) return null;

            // FIX-2026-09-05 (3+ pane builds): a middle sheet is measured
            // against the INNER pane sheet geometry chosen in Step 4 —
            // treating it as outer picked a differently-sized sheet for
            // an identical build size.
            bool isInner = side.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0
                        || side.IndexOf("Middle", StringComparison.OrdinalIgnoreCase) >= 0;

            int sheetW = 0;
            int sheetH = 0;
            string source;
            bool missingAuthoritativeStep4Sheet = false;

            bool hasAuthoritativePaneHandoff = isInner
                ? !string.IsNullOrWhiteSpace(group?.InnerGlassColor) || group?.InnerSheetPricePerSqm > 0m
                : !string.IsNullOrWhiteSpace(group?.OuterGlassColor) || group?.OuterSheetPricePerSqm > 0m;

            if (isInner && group.HasInnerSheet)
            {
                sheetW = group.InnerSheetWidthMm;
                sheetH = group.InnerSheetHeightMm;
                source = "chosen in Step 4";
            }
            else if (!isInner && group.HasOuterSheet)
            {
                sheetW = group.OuterSheetWidthMm;
                sheetH = group.OuterSheetHeightMm;
                source = "chosen in Step 4";
            }
            else if (hasAuthoritativePaneHandoff)
            {
                // Industrial safety: once Step 4/Step 5 identifies this pane,
                // a missing sheet handoff is a data gap, not permission to
                // invent the biggest/first Sheet Store size. Showing a zero
                // row is safer than costing Obscure Clear Ramly on Clear's
                // 3210 × 2250 catalogue default.
                missingAuthoritativeStep4Sheet = true;
                source = "MISSING Step 4 selected sheet — re-apply Step 4 for this spec";
            }
            else
            {
                var sizes = await SheetStorePricingService
                    .GetSizesAsync(color, thicknessMm).ConfigureAwait(true);

                if (sizes.Count > 0)
                {
                    sheetW = sizes[0].W;
                    sheetH = sizes[0].H;
                    source = "from Sheet Store";
                }
                else
                {
                    sheetW = DefaultSheetWidthMm;
                    sheetH = DefaultSheetHeightMm;
                    source = "default sheet — not in Sheet Store";
                }
            }

            decimal sheetSqm = Math.Round((sheetW * (decimal)sheetH) / 1_000_000m, 4);
            if (sheetSqm <= 0m && !missingAuthoritativeStep4Sheet) return null;

            var price = missingAuthoritativeStep4Sheet
                ? null
                : await SheetStorePricingService
                    .TryGetExactPriceForSizeAsync(color, thicknessMm, sheetW, sheetH)
                    .ConfigureAwait(true);

            decimal purchaseRate = price?.PurchasePerSqm ?? 0m;
            decimal sellRate = price?.SellPerSqm ?? 0m;

            // Industrial rule: when Step 4 supplied an outer/inner pane price,
            // that box is authoritative. Sheet Store is only a fallback for
            // legacy/manual rows with no Step 4 handoff; fuzzy catalogue matches
            // must never turn "Obscure Clear Ramly" into generic Clear.
            decimal unitRate = 0m;
            if (fallbackRate > 0m)
            {
                unitRate = fallbackRate;
                if (!missingAuthoritativeStep4Sheet)
                    source = $"Step 4 {CostBasisLabel}";
            }
            else
            {
                unitRate = CostAtSellingPrice
                    ? (sellRate > 0m ? sellRate : purchaseRate)
                    : (purchaseRate > 0m ? purchaseRate : sellRate);
            }

            decimal needed = PnLMeasurement.AreaWithWastage(cutSqm, WastagePercent);
            int sheets = missingAuthoritativeStep4Sheet
                ? 0
                : PnLMeasurement.WholeSheetsRequired(needed, sheetSqm);
            bool fromStep6Setup = false;

            // ══════════════════════════════════════════════════════════════
            //  QTY COMES FROM THE STEP 6 WASTAGE SETUP, NOT FROM SHEET STORE
            //
            //  The operator's SHEET QTY box on the matching wastage row is
            //  authoritative for this pane's quantity (and therefore its
            //  glass cost) for EVERY pane type — SGU included. SGU panes
            //  keep the "priced by required area" basis only when no wastage
            //  row claims them; once the operator has set a sheet quantity
            //  for the pane, the row's number wins exactly as it does for
            //  DGU / LAM / DGU+LAM panes. A DISABLED wastage row still owns
            //  the setup quantity — IsActive only controls whether the
            //  wastage CHARGE is added to production cost. No match keeps
            //  the existing Sheet Store / SQM round-up as fallback.
            // ══════════════════════════════════════════════════════════════
            int setupQty = ResolveStep6WastageSheetQuantity(specNumber, side);
            if (setupQty > 0)
            {
                sheets = setupQty;
                fromStep6Setup = true;
            }

            decimal quantity = fromStep6Setup
                ? sheets
                : priceByRequiredArea
                    ? Math.Round(needed, 2)
                    : sheets;

            decimal amount = 0m;
            if (unitRate > 0m)
            {
                amount = fromStep6Setup
                    ? Math.Round(sheetSqm * sheets * unitRate, 2)
                    : priceByRequiredArea
                        ? Math.Round(needed * unitRate, 2)
                        : Math.Round(sheetSqm * sheets * unitRate, 2);
            }

            string detail = missingAuthoritativeStep4Sheet
                ? $"{cutSqm:0.##} sqm · MISSING AUTHORITATIVE STEP 4 SHEET SIZE for {side} pane · not costed from Sheet Store"
                : unitRate > 0m
                    ? fromStep6Setup
                        ? $"{cutSqm:0.##} sqm → {sheets} sheet(s) · SHEET QTY from Step 6 wastage setup · {source}"
                        : priceByRequiredArea
                            ? $"{cutSqm:0.##} + {WastagePercent:0.#}% = {needed:0.##} sqm · priced by required area · {source}"
                            : $"{cutSqm:0.##} + {WastagePercent:0.#}% = {needed:0.##} sqm → {sheets} sheet(s) · {source}"
                    : "UNPRICED — not in Sheet Store and no Step 4 rate · not included in profit";

            return new ProfitLossRow
            {
                Kind = "Glass",
                SpecNumber = specNumber,
                SpecName = BuildStep9SpecDisplay(group),
                Description = PnLMeasurement.BuildRowDescription(
                    group,
                    $"{side} pane · {thicknessMm}mm {color}",
                    detail),
                SheetSource = unitRate > 0m ? source : "unpriced",
                SheetWidthMm = sheetW,
                SheetHeightMm = sheetH,
                SheetSqm = sheetSqm,
                Quantity = quantity,
                PricePerSqm = unitRate,
                Amount = amount
            };
        }

        /// <summary>
        /// Maps a Step 9 pane side to its Step 6 wastage source label so the
        /// setup SHEET QTY is read for the EXACT pane — outer, inner, lam
        /// outer, lam inner — instead of applying one number to the build.
        /// </summary>
        private static string? WastageSideLabelForPane(string paneSide)
        {
            if (paneSide.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Lam Inner (W2/H2)";
            if (paneSide.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Lam Outer (W2/H2)";
            if (paneSide.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Inner (W2/H2)";
            if (paneSide.IndexOf("Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                return "Outer (W1/H1)";
            return null;
        }

        /// <summary>
        /// The sheet quantity the operator set in Step 6's wastage setup for
        /// THIS specification's pane side. A DISABLED wastage row still owns
        /// the setup quantity — IsActive only controls whether the wastage
        /// charge is added to production cost. 0 means no wastage row targets
        /// this pane, so the existing Sheet Store calculation remains the
        /// fallback.
        /// </summary>
        private int ResolveStep6WastageSheetQuantity(int specNumber, string paneSide)
        {
            string? sideLabel = WastageSideLabelForPane(paneSide);
            if (sideLabel == null) return 0;

            var rows = _otherChargesVm?.WastagesSection?.WastageRows;
            if (rows == null) return 0;

            foreach (var row in rows.OrderByDescending(r => r.IsActive))
            {
                if (row == null || row.SheetQuantity <= 0) continue;

                var sources = row.AvailableSources
                    .Where(s => s != null && s.IsChecked)
                    .ToList();
                if (sources.Count == 0) continue;

                // A single Step 6 wastage row may intentionally combine the
                // same glass across two specs. In that case the setup's sheet
                // quantity is TOTAL shared sheets, not the quantity for each
                // spec. Use the same allocator as Sheet Details/Target so the
                // actual-cost rows split 17 shared sheets into e.g. 16 + 1,
                // instead of showing 17 on every related spec.
                var allocation = Step6SheetAllocation.Allocate(row, sources)
                    .FirstOrDefault(a =>
                        a.Source.SpecNumber == specNumber
                        && string.Equals(
                            a.Source.SideLabel,
                            sideLabel,
                            StringComparison.OrdinalIgnoreCase));

                if (allocation != null)
                    return allocation.SheetQuantity;
            }

            return 0;
        }

    }
}
