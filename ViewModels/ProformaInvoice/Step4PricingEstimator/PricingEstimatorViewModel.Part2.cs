// ═══════════════════════════════════════════════════════════════════════
// FILE: PricingEstimatorViewModel.Part2.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Services.Localization;
using ProGlassAutomation.Services.Dialogs;
using Microsoft.Extensions.DependencyInjection;
using ProGlassAutomation.Services.Reporting;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step4PricingEstimator
{
        public partial class PricingEstimatorViewModel : ViewModelBase
    {
            private void LoadCalculationSections(
            IReadOnlyList<Step4PricingSnapshot> snapshots,
            int selectedSectionNumber)
        {
            SpecGroups.Clear();

            if (snapshots == null || snapshots.Count == 0)
            {
                InitializeCalculationSections();
                return;
            }

            for (int i = 0; i < snapshots.Count; i++)
            {
                SpecGroups.Add(new SpecTargetGroup
                {
                    SectionNumber = i + 1,
                    Name = $"Spec Group {i + 1}",
                    Description = $"Calculation section {i + 1}",
                    Snapshot = ClonePricingSnapshot(snapshots[i])
                });
            }

            int targetIndex = Math.Clamp(selectedSectionNumber - 1, 0, SpecGroups.Count - 1);
            SelectCalculationSection(SpecGroups[targetIndex], captureCurrent: false);
            OnPropertyChanged(nameof(CalculationSectionCount));
            OnPropertyChanged(nameof(CanRemoveCalculationSection));
            OnPropertyChanged(nameof(TotalSpecsActive));
            ProGlassAutomation.Utilities.AsyncUtility.InvalidateCommandsThrottled();
        }

        private void SelectGlassTypeCard(string? code)
        {
            if (string.IsNullOrWhiteSpace(code))
                return;
            var card = GlassTypes.FirstOrDefault(c =>
                string.Equals(c.Code, code, StringComparison.OrdinalIgnoreCase));
            if (card == null)
                return;
            // Never snap a live DGU/LAM/hybrid selection back to SGU unless
            // the incoming code is actually SGU (new invoice / explicit restore).
            SelectedGlassType = card;
        }

        // ==================== APPLY RATE (SAVE TO DATABASE) ====================

        /// <summary>
        /// Saves the currently configured spec (name + rate) to
        /// specifications.db for later reuse in Step 5 (Data Grid) spec
        /// dropdown.
        ///
        /// BUG-42: Returns early with a friendly warning if the current
        ///         glass type has no real spec name (DGU+LAM hybrid isn't
        ///         implemented yet — was previously writing garbage).
        /// BUG-44: Uses one-line non-blocking ApplyStatusMessage instead
        ///         of blocking MessageBox for success. Errors still use
        ///         MessageBox because they need to interrupt user attention.
        /// </summary>
        /// <summary>
        /// Saves the current spec on the way out of Step 4, so moving to
        /// Step 5 by Continue (or by clicking the step in the header) makes
        /// it available in the dropdown without the operator remembering to
        /// press APPLY.
        ///
        /// SILENT BY DESIGN. Navigation must never be interrupted, so this:
        ///   • does nothing when there is nothing valid to save -- the same
        ///     conditions ApplyRateCommand.CanExecute checks, so it cannot
        ///     write the "not implemented" garbage BUG-42 guards against;
        ///   • re-saving an unchanged spec is already a no-op update in the
        ///     repository (it just bumps UsageCount), which is what you
        ///     asked for: "if already saved then stay silent, do nothing";
        ///   • swallows everything, including a permission denial, because
        ///     an auto-save the user did not ask for must not raise a
        ///     dialog mid-navigation. An explicit APPLY still reports
        ///     normally.
        /// </summary>
        public async System.Threading.Tasks.Task AutoSaveCurrentSpecAsync()
        {
            try
            {
                if (SelectedGlassType == null) return;
                if (EstimatedRatePerSqm <= 0m) return;
                if (TotalSpecsActive <= 0) return;

                string specName = GetCurrentSpecName(SelectedGlassType.Code ?? "");
                if (string.IsNullOrWhiteSpace(specName)) return;

                await ApplyRateAsync();
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[Step4.AutoSave] {ExceptionMessages.Deep(ex)}");
            }
        }

        private async System.Threading.Tasks.Task ApplyRateAsync()
        {
            if (SelectedGlassType == null || EstimatedRatePerSqm <= 0m)
            {
                ApplyStatusMessage =
                    "⚠  Configure glass parameters and ensure estimated rate > 0.";
                return;
            }

            string glassType = SelectedGlassType.Code;
            string specName = GetCurrentSpecName(glassType);
            decimal rate = EstimatedRatePerSqm;

            // BUG-42: Guard — empty spec name means the type isn't
            // implemented (e.g. DGU+LAM). Do NOT save garbage.
            if (string.IsNullOrWhiteSpace(specName))
            {
                ApplyStatusMessage =
                    $"⚠  {glassType} pricing is not yet implemented — cannot save spec.";
                return;
            }

            // R-2: guard placed after the validation returns above so the
            // early-exit paths still surface their status message.
            if (_isApplyingRate) return;
            _isApplyingRate = true;

            try
            {
                // SPECIFICATIONS CENTER: record what this spec was built
                // from, alongside the price. Step 4 already knows the outer
                // colour/thickness and the exact sheet the operator has in
                // the boxes, so the catalogue is populated from the values
                // that were actually used -- no re-derivation, no guessing.
                //
                // Sheet size is stored for DISPLAY/traceability only. Price
                // still comes from `rate` (Step 4), so editing a sheet in
                // Sheet Store later cannot silently re-cost this spec.
                var (specColor, specThicknessMm, specW, specH) =
                    GetCatalogueDetailsForCurrentPanel(glassType);

                // Per-pane sheet matches. The single sheetWidth/Height pair
                // below describes only the OUTER pane; a DGU's inner leaf can
                // be a different stock size entirely. Derived from the spec
                // NAME so every build family works the same way, including
                // DGU+LAM which has no sheet wiring of its own.
                string? panesJson = null;
                try
                {
                    var panes = Services.Specifications.SpecPaneResolver
                        .Parse(specName);
                    panes = await Services.Specifications.SpecPaneResolver
                        .MatchSheetsAsync(panes);
                    panesJson = Services.Specifications.SpecPaneResolver
                        .Serialize(panes);
                }
                catch (Exception paneEx)
                {
                    Services.Logging.AppLogService.Debug(
                        $"[Step4.Panes] {paneEx.Message}");
                }

                // LEDGER GROUND TRUTH: this writes the reusable LIBRARY row
                // (name + rate + catalogue details) so Step 5 can offer the
                // spec. It records NO usage: an APPLY press is not a use —
                // counts and history are derived by SpecificationLedgerService
                // from SAVED invoices only, once this invoice is saved.
                // proformaId is null for the same reason: a draft PI number
                // is not a fact yet, and the ledger owns that column.
                var savedSpec = await _specs.SaveOrUpdateAsync(
                    glassType, specName, rate,
                    category: null,          // Sheet Store category: resolved
                                             // in the Center from colour+thickness
                    color: specColor,
                    thicknessMm: specThicknessMm,
                    sheetWidthMm: specW,
                    sheetHeightMm: specH,
                    sheetId: 0,
                    proformaId: null,
                    panesJson: panesJson);

                // PHASE G: remember WHICH stock sheet this spec was priced
                // against, so Step 5 can carry it and Step 9 can cost the
                // sheet the user actually chose instead of re-guessing.
                // specifications.db deliberately stays name+price only —
                // sheet choice is a per-invoice decision, not a spec property.
                RecordSheetSelection(glassType, specName);

                // BUG-44: non-blocking one-line status. The count is the
                // LEDGER-verified uses on saved invoices (healed on every
                // invoice save) — honest about what is real, which an
                // APPLY-press counter never was.
                int verifiedUses = Math.Max(0, savedSpec.UsageCount);
                ApplyStatusMessage = verifiedUses == 0
                    ? $"✓  Saved '{specName}' — AED {rate:N2}/sqm. Now available in Step 5."
                    : $"✓  Updated '{specName}' — AED {rate:N2}/sqm  (on {verifiedUses} saved group{(verifiedUses == 1 ? "" : "s")}).";
            }
            catch (Services.Audit.PermissionDeniedException denied)
            {
                // A-1: SaveOrUpdateAsync is now guarded at the service
                // layer. Without this branch the denial would fall into the
                // catch below and be reported as a "Database Error", which
                // is wrong and would send the user chasing a fault that
                // does not exist.
                _dialogs.Show(
                    denied.UserMessage, LocalizationService.Instance.T("Msg.CapPermissionDenied"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);

                ApplyStatusMessage = "⚠  " + denied.UserMessage;
            }
            catch (Exception ex)
            {
                // Errors still use MessageBox — must interrupt the user
                _dialogs.Show(
                    string.Format(LocalizationService.Instance.T("Msg.FailedToSaveSpecX"), ExceptionMessages.Deep(ex)),
                    LocalizationService.Instance.T("Msg.CapDatabaseError"),
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);

                ApplyStatusMessage = $"✗  Save failed: {ExceptionMessages.Deep(ex)}";
            }
            finally
            {
                // Must release on EVERY path, including the failure path,
                // or the Apply button stays dead for the rest of the session.
                _isApplyingRate = false;
            }
        }

        /// <summary>
        /// PHASE G — publishes the chosen stock sheet(s) for this spec into
        /// SpecSheetRegistry. SGU, DGU and LAM hold editable sheet-size
        /// fields and record here; DGU+LAM prices three panes + interlayer
        /// directly per SQM (its panel has no sheet-size inputs), so it
        /// records nothing and downstream steps use its per-SQM pane rates —
        /// that is a design choice, not a missing switch case.
        /// Never throws — a failure here must not block saving the spec.
        /// </summary>
        /// <summary>
        /// Pulls the outer pane's colour/thickness and the chosen sheet size
        /// from whichever pricing panel is active, for the Specifications
        /// Center catalogue columns.
        ///
        /// The OUTER pane is used deliberately: it is the leaf every build
        /// family has, so a DGU and the SGU carried out of it in Step 5
        /// describe the same glass.
        ///
        /// Never throws and never blocks a save -- a spec with blank
        /// catalogue details is still a perfectly valid spec.
        /// </summary>
        private (string? Color, int ThicknessMm, int SheetWidthMm, int SheetHeightMm)
            GetCatalogueDetailsForCurrentPanel(string glassType)
        {
            try
            {
                return glassType switch
                {
                    "SGU" => (SguVM.GlassColor,
                              ParseThicknessMm(SguVM.SelectedThickness),
                              SguVM.SheetWidthMm,
                              SguVM.SheetHeightMm),

                    "DGU" => (DguVM.OuterGlassColor,
                              ParseThicknessMm(DguVM.OuterThickness),
                              DguVM.OuterSheetWidthMm,
                              DguVM.OuterSheetHeightMm),

                    "LAM" => (LamVM.OuterGlassColor,
                              ParseThicknessMm(LamVM.OuterThickness),
                              LamVM.OuterSheetWidthMm,
                              LamVM.OuterSheetHeightMm),

                    "DGU+LAM" => (DguLamVM.OuterGlassColor,
                                  ParseThicknessMm(DguLamVM.OuterThickness),
                                  DguLamVM.OuterSheetWidthMm,
                                  DguLamVM.OuterSheetHeightMm),

                    _ => (null, 0, 0, 0)
                };
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[Step4.CatalogueDetails] {ExceptionMessages.Deep(ex)}");
                return (null, 0, 0, 0);
            }
        }

        /// <summary>
        /// "6mm" / "6 mm" / "6" -> 6. Returns 0 when no leading number is
        /// present, which the catalogue treats as "unknown".
        /// </summary>
        private static int ParseThicknessMm(string? thickness)
        {
            if (string.IsNullOrWhiteSpace(thickness)) return 0;

            var digits = new string(
                thickness.TrimStart().TakeWhile(char.IsDigit).ToArray());

            return int.TryParse(digits, out int mm) ? mm : 0;
        }

        private void RecordSheetSelection(string glassType, string specName)
        {
            try
            {
                switch (glassType)
                {
                    // Record the numeric width/height the user actually has
                    // in the boxes. Those are the authoritative values now
                    // that sheet size is two editable fields per side - a
                    // typed custom size has no entry in the size dropdown.
                    case "DGU":
                        ProGlassAutomation.Services.ProformaInvoice.SpecSheetRegistry
                            .Record(specName,
                                new ProGlassAutomation.Services.ProformaInvoice.SpecSheetSelection
                                {
                                    OuterWidthMm = DguVM.OuterSheetWidthMm,
                                    OuterHeightMm = DguVM.OuterSheetHeightMm,
                                    InnerWidthMm = DguVM.InnerSheetWidthMm,
                                    InnerHeightMm = DguVM.InnerSheetHeightMm,
                                    OuterPricePerSqm = DguVM.OuterBasePrice,
                                    InnerPricePerSqm = DguVM.InnerBasePrice,
                                    UInsertPricePerSqm = DguVM.HasUInsertExtra
                                        ? DguVM.UInsertPricePerSqm
                                        : 0m
                                });
                        break;

                    case "SGU":
                        ProGlassAutomation.Services.ProformaInvoice.SpecSheetRegistry
                            .Record(specName,
                                new ProGlassAutomation.Services.ProformaInvoice.SpecSheetSelection
                                {
                                    OuterWidthMm = SguVM.SheetWidthMm,
                                    OuterHeightMm = SguVM.SheetHeightMm,
                                    OuterPricePerSqm = SguVM.BasePricePerSqm
                                });
                        break;

                    case "LAM":
                        ProGlassAutomation.Services.ProformaInvoice.SpecSheetRegistry
                            .Record(specName,
                                new ProGlassAutomation.Services.ProformaInvoice.SpecSheetSelection
                                {
                                    OuterWidthMm = LamVM.OuterSheetWidthMm,
                                    OuterHeightMm = LamVM.OuterSheetHeightMm,
                                    InnerWidthMm = LamVM.InnerSheetWidthMm,
                                    InnerHeightMm = LamVM.InnerSheetHeightMm,
                                    OuterPricePerSqm = LamVM.OuterBasePrice,
                                    InnerPricePerSqm = LamVM.InnerBasePrice
                                });
                        break;

                    case "DGU+LAM":
                        ProGlassAutomation.Services.ProformaInvoice.SpecSheetRegistry
                            .Record(specName,
                                new ProGlassAutomation.Services.ProformaInvoice.SpecSheetSelection
                                {
                                    OuterWidthMm = DguLamVM.OuterSheetWidthMm,
                                    OuterHeightMm = DguLamVM.OuterSheetHeightMm,
                                    InnerWidthMm = DguLamVM.LamOuterSheetWidthMm,
                                    InnerHeightMm = DguLamVM.LamOuterSheetHeightMm,
                                    ThirdWidthMm = DguLamVM.LamInnerSheetWidthMm,
                                    ThirdHeightMm = DguLamVM.LamInnerSheetHeightMm,
                                    OuterPricePerSqm = DguLamVM.OuterBasePrice,
                                    InnerPricePerSqm = DguLamVM.LamOuterBasePrice,
                                    ThirdPricePerSqm = DguLamVM.LamInnerBasePrice
                                });
                        break;
                }
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[PricingEstimator] sheet selection not recorded: {ExceptionMessages.Deep(ex)}");
            }
        }

        /// <summary>
        /// Retrieves the current auto-generated spec name from the active panel.
        ///
        /// BUG-42 originally forced DGU+LAM to "" because the hybrid had no
        /// implementation and a placeholder string was being saved as if it
        /// were a real specification. The hybrid is implemented now, so it
        /// returns its true spec name — while "" was correct it also left
        /// APPLY RATE permanently disabled, so a hybrid could be priced on
        /// screen and never applied to a spec group.
        ///
        /// The empty-string guard in ApplyRateAsync stays: it is what stops
        /// an unimplemented type from writing garbage, and it must keep
        /// working for any type added later.
        /// </summary>
        private string GetCurrentSpecName(string glassType)
        {
            return glassType switch
            {
                "SGU" => SguVM.GeneratedSpecName ?? "",
                "DGU" => DguVM.GeneratedSpecName ?? "",
                "LAM" => LamVM.GeneratedSpecName ?? "",
                "DGU+LAM" => DguLamVM.GeneratedSpecName ?? "",
                _ => ""
            };
        }

        // ==================== LIVE CALCULATION ====================

        // BUG-45: single handler for all panel property changes.
        // Only recalculates if the currently selected glass type matches
        // the panel that fired the event.
        private void OnAnyPanelPropertyChanged(
            object? sender, PropertyChangedEventArgs e)
        {
            if (_isRestoringSnapshot) return;
            HasRestoredSnapshotState = false;

            string? currentType = SelectedGlassType?.Code;
            if (currentType == null) return;

            bool relevant = currentType switch
            {
                "SGU" => sender is SguParameters.SguParametersViewModel,
                "DGU" => sender is DguParameters.DguParametersViewModel,
                "LAM" => sender is LamParameters.LamParametersViewModel,
                "DGU+LAM" => sender is DguLamParameters.DguLamParametersViewModel,
                _ => false
            };

            if (relevant)
            {
                RecalculateEstimatedRate();
                UpdateCurrentCalculationSectionSnapshot();
                NotifySpecFingerprintChanged();
            }
        }

        private string _currentSpecFingerprint = "";

        /// <summary>
        /// Fingerprint of the current section's full Step 4 selection
        /// (spec name, panes, sheets, rate — see ComputeContentHash).
        /// Raised ONLY when it actually changes, so Step 5 can live-sync
        /// glass/pane/sheet edits even when the computed rate doesn't move
        /// (same-price glass swap fires no rate notification). The wizard
        /// coordinator subscribes to this property.
        /// </summary>
        public string CurrentSpecFingerprint => _currentSpecFingerprint;

        private void NotifySpecFingerprintChanged()
        {
            string next = SelectedCalculationSection?.Snapshot?.ComputeContentHash() ?? "";
            if (string.Equals(next, _currentSpecFingerprint, StringComparison.Ordinal))
                return;
            _currentSpecFingerprint = next;
            OnPropertyChanged(nameof(CurrentSpecFingerprint));
        }

        /// <summary>
        /// Silent cache refresh for section switches (they restore under
        /// _isRestoringSnapshot, bypassing the notifier above).
        /// </summary>
        private void ResetSpecFingerprintCache()
        {
            _currentSpecFingerprint =
                SelectedCalculationSection?.Snapshot?.ComputeContentHash() ?? "";
        }

        private void RecalculateEstimatedRate()
        {
            YieldFactorWarning = "";   // BUG-40/41: clear stale warning

            if (SelectedGlassType == null)
            {
                EstimatedRatePerSqm = 0m;
                return;
            }

            switch (SelectedGlassType.Code)
            {
                case "SGU":
                    EstimatedRatePerSqm = CalculateSguRate();
                    break;

                case "DGU":
                    EstimatedRatePerSqm = CalculateDguRate();
                    break;

                case "LAM":
                    EstimatedRatePerSqm = CalculateLamRate();
                    break;

                case "DGU+LAM":
                    // Was 0m "pending business definition", which meant a real
                    // hybrid quote priced at NOTHING and nothing on screen said
                    // so. Now costed as a THREE-pane unit: outer + air spacer +
                    // laminated outer + interlayer + laminated inner.
                    EstimatedRatePerSqm = CalculateDguLamRate();
                    break;

                default:
                    EstimatedRatePerSqm = 0m;
                    break;
            }
        }

        // ══════════════════════════════════════════════════════════════════
        //  BUG-40 [HIGH]: SGU rate — invalid yield factor no longer silent
        //
        //  Step 1: raw = Base Price per SQM
        //  Step 2: cost = raw / Yield Factor
        //  Step 3: + Cutting Cost
        //  Step 4: + Tempering Cost
        //  Step 5: * (1 + Margin/100)
        // ══════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════
        //  PRE-MARGIN COST BASIS
        //
        //  Every rate above is  basis x (1 + margin%).  The basis is the
        //  number a quoted rate has to be measured against, and until now it
        //  was a local variable inside each Calculate*Rate method — invisible
        //  to anything else.
        //
        //  Step 9's rate check needs it. It was using the Profit & Loss
        //  materials cost instead, which is a different quantity entirely:
        //  that figure is built from SHEETS PURCHASED at PURCHASE prices,
        //  while the quoted rate is built from CUT AREA at SELLING prices
        //  plus spacer, cutting, tempering and yield. Comparing a rate
        //  against it reported a margin that was never the margin.
        //
        //  Worked example, 6mm Clear FT + 12mm ASP + 6mm Clear FT:
        //      basis (25 + 25)/0.85 + 45 = 103.82 per SQM
        //      x 1.15 = 119.39   x 1.10 = 114.20
        //  So 114.20 reads as 10%, which is what the business expects.
        // ══════════════════════════════════════════════════════════════════

        /// <summary>
        /// Cost per SQM before the profit margin is applied, for the glass
        /// type currently selected. Returns 0 when no type is selected or the
        /// selection is not recognised.
        /// </summary>
        public decimal CostBasisPerSqm
        {
            get
            {
                switch (SelectedGlassType?.Code)
                {
                    case "SGU": return SguBasis();
                    case "DGU": return DguBasis();
                    case "LAM": return LamBasis();
                    case "DGU+LAM": return DguLamBasis();
                    default: return 0m;
                }
            }
        }

        /// <summary>Yield never divides by zero; mirrors the rate methods.</summary>
        private static decimal SafeYield(decimal yieldFactor) =>
            yieldFactor <= 0m ? 1m : yieldFactor;

        private decimal SguBasis()
        {
            var vm = SguVM;
            decimal basis = vm.BasePricePerSqm / SafeYield(vm.YieldFactor)
                          + vm.CuttingCostPerSqm
                          + vm.TemperingCostPerSqm;
            return MoneyRounding.Round(basis);
        }

        private decimal DguBasis()
        {
            var vm = DguVM;
            decimal basis = (vm.OuterBasePrice + vm.InnerBasePrice)
                                / SafeYield(vm.YieldFactor)
                          + vm.SpacerPrice;
            return MoneyRounding.Round(basis);
        }

        private decimal LamBasis()
        {
            var vm = LamVM;
            decimal basis = (vm.OuterBasePrice + vm.InnerBasePrice)
                                / SafeYield(vm.YieldFactor)
                          + vm.PvbMaterialRate
                          + vm.OuterCuttingCost + vm.InnerCuttingCost
                          + vm.OuterTemperingCost + vm.InnerTemperingCost;
            return MoneyRounding.Round(basis);
        }

        private decimal DguLamBasis()
        {
            var vm = DguLamVM;
            decimal basis = (vm.OuterBasePrice + vm.LamOuterBasePrice + vm.LamInnerBasePrice)
                                / SafeYield(vm.YieldFactor)
                          + vm.TotalCuttingCost
                          + vm.TotalTemperingCost
                          + vm.InterlayerSellPrice
                          + vm.SpacerPrice;
            return MoneyRounding.Round(basis);
        }

        private decimal CalculateSguRate()
        {
            var vm = SguVM;

            decimal basePrice = vm.BasePricePerSqm;
            decimal yieldFactor = vm.YieldFactor;
            decimal cuttingCost = vm.CuttingCostPerSqm;
            decimal temperingCost = vm.TemperingCostPerSqm;
            decimal marginPercent = vm.TargetMarginPercent;

            // BUG-40: warn instead of silent reset
            if (yieldFactor <= 0m)
            {
                YieldFactorWarning =
                    $"⚠  SGU yield factor must be > 0 (currently {yieldFactor:0.###}). " +
                    "Using 1.0 as fallback.";
                yieldFactor = 1m;
            }

            decimal step1 = basePrice;
            decimal step2 = step1 / yieldFactor;
            decimal step3 = step2 + cuttingCost;
            decimal step4 = step3 + temperingCost;
            decimal step5 = step4 * (1m + (marginPercent / 100m));

            return MoneyRounding.Round(step5);
        }

        // ══════════════════════════════════════════════════════════════════
        //  BUG-41 [HIGH]: DGU rate — invalid yield factor no longer silent
        // ══════════════════════════════════════════════════════════════════

        private decimal CalculateDguRate()
        {
            var vm = DguVM;

            decimal outerPrice = vm.OuterBasePrice;
            decimal innerPrice = vm.InnerBasePrice;
            decimal spacerPrice = vm.SpacerPrice;
            decimal yieldFactor = vm.YieldFactor;
            decimal marginPercent = vm.MarginProfitPercent;

            // BUG-41: warn instead of silent reset
            if (yieldFactor <= 0m)
            {
                YieldFactorWarning =
                    $"⚠  DGU yield factor must be > 0 (currently {yieldFactor:0.###}). " +
                    "Using 1.0 as fallback.";
                yieldFactor = 1m;
            }

            decimal step1 = outerPrice + innerPrice;                    // 73
            decimal step2 = step1 / yieldFactor;                        // 85.88
            decimal step3 = step2 + spacerPrice;                        // 130.88
            decimal step4 = step3 * (1m + (marginPercent / 100m));      // 150.51

            return MoneyRounding.Round(step4);
        }

        // ══════════════════════════════════════════════════════════════════
        //  BUG-41 [HIGH]: LAM rate — invalid yield factor no longer silent
        // ══════════════════════════════════════════════════════════════════

        // ══════════════════════════════════════════════════════════════════
        //  DGU+LAM — three panes of glass, one air spacer, one interlayer
        //
        //  The rule set lives on DguLamParametersViewModel.CalculateRate so
        //  there is ONE definition of the hybrid price. This wrapper only
        //  surfaces its warning through the existing YieldFactorWarning
        //  channel, the same way the SGU/DGU/LAM branches do.
        // ══════════════════════════════════════════════════════════════════

        private decimal CalculateDguLamRate()
        {
            decimal rate = DguLamVM.CalculateRate(out string warning);

            if (!string.IsNullOrWhiteSpace(warning))
                YieldFactorWarning = warning;

            return rate;
        }

        private decimal CalculateLamRate()
        {
            var vm = LamVM;

            decimal outerPrice = vm.OuterBasePrice;
            decimal innerPrice = vm.InnerBasePrice;
            decimal pvbRate = vm.PvbMaterialRate;
            decimal outerCutting = vm.OuterCuttingCost;
            decimal innerCutting = vm.InnerCuttingCost;
            decimal outerTempering = vm.OuterTemperingCost;
            decimal innerTempering = vm.InnerTemperingCost;
            decimal yieldFactor = vm.YieldFactor;
            decimal marginPercent = vm.MarginProfitPercent;

            // BUG-41: warn instead of silent reset
            if (yieldFactor <= 0m)
            {
                YieldFactorWarning =
                    $"⚠  LAM yield factor must be > 0 (currently {yieldFactor:0.###}). " +
                    "Using 1.0 as fallback.";
                yieldFactor = 1m;
            }

            decimal step1 = outerPrice + innerPrice;                              // 50
            decimal step2 = step1 / yieldFactor;                                  // 58.82
            decimal step3 = step2 + pvbRate;                                      // 168.82
            decimal step4 = step3 + outerCutting + innerCutting;                  // 178.82
            decimal step5 = step4 + outerTempering + innerTempering;              // 198.82
            decimal step6 = step5 * (1m + (marginPercent / 100m));                // 228.65

            return MoneyRounding.Round(step6);
        }
    }
}
