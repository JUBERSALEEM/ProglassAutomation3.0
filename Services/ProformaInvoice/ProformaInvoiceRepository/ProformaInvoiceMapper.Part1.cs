// ═══════════════════════════════════════════════════════════════════════
// FILE: ProformaInvoiceMapper.Part1.cs (Phase 3.8 split — member census verified)
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.MasterData;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.ProformaInvoice.Specifications;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.CustomCharges;
using ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.Wastages;
using ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step1CompanySetup;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step2ClientProfile;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step3ProjectSpecs;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step5DataGrid;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step6OtherCharges;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.InvoiceMainSummary;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.PrintableInvoice;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository
{
        public static partial class ProformaInvoiceMapper
    {
            // BUG-20: shared static — allocated once, reused everywhere
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            WriteIndented = false,
            PropertyNameCaseInsensitive = true
        };

        private static string FirstNonEmpty(params string?[] values)
        {
            foreach (var v in values)
            {
                if (!string.IsNullOrWhiteSpace(v))
                    return v.Trim();
            }
            return "";
        }

        private static string TryEmbedLogoBase64(string? path)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path) || !System.IO.File.Exists(path))
                    return string.Empty;

                var bytes = System.IO.File.ReadAllBytes(path);
                return bytes.Length > 0 ? Convert.ToBase64String(bytes) : string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        private static CompanyLogoLayout EffectiveLogoLayout(
            string? logoPath,
            CompanyLogoLayout layout,
            double widthMm = 22,
            double heightMm = 26)
        {
            if (string.IsNullOrWhiteSpace(logoPath))
                return CompanyLogoLayout.None;

            bool looksWide = widthMm >= 70 || (heightMm > 0 && widthMm / heightMm >= 2.4);
            if (layout == CompanyLogoLayout.TopPanel || looksWide)
                return CompanyLogoLayout.TopPanel;

            return CompanyLogoLayout.LeftPortrait;
        }

        // ══════════════════════════════════════════════════════════════
        //  SAVE PATH: VMs → Snapshot → Entity
        // ══════════════════════════════════════════════════════════════

        public static SavedProformaInvoice BuildEntityFromWizard(
            CompanySetupViewModel company,
            ClientProfileViewModel client,
            ProjectSpecsViewModel project,
            DataGridViewModel dataGrid,
            OtherChargesViewModel otherCharges,
            InvoiceMainSummaryViewModel summary,
            TermsAndConditionsViewModel terms,
            PrintableInvoiceViewModel printable)
        {
            var snapshot = BuildSnapshot(
                company, client, project,
                dataGrid, otherCharges,
                summary, terms, printable);

            string json = JsonSerializer.Serialize(snapshot, _jsonOptions);

            // Senior workflow hardening (Aug 2026): Step 7 print-preview
            // fields must NEVER be able to confirm an invoice. Stored PI
            // workflow status comes from the real wizard status only; Step 8
            // approval is the only path that may move it to Approved.
            string effectiveStatus = company.ProformaStatus ?? "Draft";

            var entity = new SavedProformaInvoice
            {
                ProformaId = company.ProformaId ?? "",
                InquiryId = company.InquiryId,
                CompanyName = company.CompanyName ?? "",
                CustomerName = client.CustomerName ?? "",
                // Indexed list column is Project Name. Reference stays
                // in the snapshot so JO / DN / TI can print both.
                ProjectName = FirstNonEmpty(
                    project.ProjectIdentifierName,
                    project.ProjectReference),
                Status = effectiveStatus,
                IssueDate = company.IssueDate,
                ValidUntil = company.ValidUntil,

                GlassMaterialsSubtotal = summary.GlassMaterialsSubtotal,
                OtherChargesSubtotal = summary.OtherChargesSubtotal,
                DiscountAmount = summary.DiscountAmount,
                VatAmount = summary.VatAmount,
                GrandTotalVatInclusive = summary.GrandTotalVatInclusive,

                FullSnapshotJson = json
            };

            // PHASE 14 — Master Data auto-save (fire-and-forget)
            if (App.MasterData != null)
            {
                _ = SafeAutoSaveMasterDataAsync(company, client, project);
            }

            return entity;
        }

        /// <summary>
        /// Phase 14: Fire-and-forget master data auto-save.
        /// CHAT 15: projectReference now passed to AutoSaveFromInvoiceAsync.
        /// All exceptions swallowed — must never block invoice save.
        /// </summary>
        private static async Task SafeAutoSaveMasterDataAsync(
            CompanySetupViewModel company,
            ClientProfileViewModel client,
            ProjectSpecsViewModel project)
        {
            try
            {
                await App.MasterData.AutoSaveFromInvoiceAsync(
                    companyName: company.CompanyName ?? "",
                    companyTrn: company.CompanyTrn ?? "",
                    companyLocation: company.CompanyLocation ?? "",

                    // AUDIT-FIX F-2: Step 1 profile fields enrich the saved
                    // company record. The repository preserves any stored
                    // value when the invoice field is blank.
                    companyLogoPath: company.LogoPath ?? "",
                    companyRepresentative: company.Representative ?? "",
                    companyRepresentativeMobile: company.RepresentativeMobile ?? "",
                    companyEmail: company.CompanyEmail ?? "",
                    companyAccountNumber: company.AccountNumber ?? "",
                    companyBankName: company.BankName ?? "",
                    companyLogoLayout: EffectiveLogoLayout(company.LogoPath, company.LogoLayout, company.LogoWidthMm, company.LogoHeightMm),
                    companyLogoWidthMm: company.LogoWidthMm,
                    companyLogoHeightMm: company.LogoHeightMm,

                    customerName: client.CustomerName ?? "",
                    customerTrn: client.CustomerTrn ?? "",
                    customerReferenceCode: client.CustomerReferenceCode ?? "",
                    salesRepresentative: client.SalesRepresentative ?? "",
                    documentPreparedBy: client.DocumentPreparedBy ?? "",
                    billingAddress: client.BillingAddress ?? "",
                    attentionExecutiveName: client.AttentionExecutiveName ?? "",
                    clientContactPhone: client.ClientContactPhone ?? "",

                    projectIdentifierName: project.ProjectIdentifierName ?? "",
                    projectContractNo: project.ProjectContractNo ?? "",
                    projectReference: project.ProjectReference ?? "",
                    siteDeliveryLocation: project.SiteDeliveryLocation ?? "",
                    lpoNumber: project.LpoNumber ?? "");
            }
            catch (Exception ex)
            {
                AppLogService.Debug(
                    $"[ProformaInvoiceMapper] Master data auto-save failed: {ex.Message}");
            }
        }

        private static ProformaInvoiceSnapshot BuildSnapshot(
            CompanySetupViewModel company,
            ClientProfileViewModel client,
            ProjectSpecsViewModel project,
            DataGridViewModel dataGrid,
            OtherChargesViewModel otherCharges,
            InvoiceMainSummaryViewModel summary,
            TermsAndConditionsViewModel terms,
            PrintableInvoiceViewModel printable)
        {
            var snap = new ProformaInvoiceSnapshot
            {
                FurthestStepReached = printable?.FurthestStepReached ?? 0,
                Company = new CompanySnapshot
                {
                    CompanyName = company.CompanyName ?? "",
                    CompanyTrn = company.CompanyTrn ?? "",
                    CompanyLocation = company.CompanyLocation ?? "",
                    ProformaId = company.ProformaId ?? "",
                    InquiryId = company.InquiryId,
                    IssueDate = company.IssueDate,
                    ValidUntil = company.ValidUntil,
                    ProformaStatus = company.ProformaStatus ?? "Draft",
                    Representative = company.Representative ?? "",
                    RepresentativeMobile = company.RepresentativeMobile ?? "",
                    CompanyEmail = company.CompanyEmail ?? "",
                    AccountNumber = company.AccountNumber ?? "",
                    BankName = company.BankName ?? "",
                    LogoPath = company.LogoPath ?? "",
                    LogoBase64 = TryEmbedLogoBase64(company.LogoPath),
                    CompanyRecordId = company.CompanyRecordId,
                    LogoLayout = EffectiveLogoLayout(company.LogoPath, company.LogoLayout, company.LogoWidthMm, company.LogoHeightMm),
                    LogoWidthMm = company.LogoWidthMm,
                    LogoHeightMm = company.LogoHeightMm
                },

                Client = new ClientSnapshot
                {
                    CustomerName = client.CustomerName ?? "",
                    CustomerTrn = client.CustomerTrn ?? "",
                    CustomerReferenceCode = client.CustomerReferenceCode ?? "",
                    SalesRepresentative = client.SalesRepresentative ?? "",
                    DocumentPreparedBy = client.DocumentPreparedBy ?? "",
                    BillingAddress = client.BillingAddress ?? "",
                    AttentionExecutiveName = client.AttentionExecutiveName ?? "",
                    ClientContactPhone = client.ClientContactPhone ?? ""
                },

                Project = new ProjectSnapshot
                {
                    ProjectIdentifierName = project.ProjectIdentifierName ?? "",
                    ProjectContractNo = project.ProjectContractNo ?? "",
                    ProjectReference = project.ProjectReference ?? "",
                    SiteDeliveryLocation = project.SiteDeliveryLocation ?? "",
                    LpoNumber = project.LpoNumber ?? ""
                },

                Summary = new InvoiceSummarySnapshot
                {
                    DiscountType = summary.SelectedDiscountType.ToString(),
                    DiscountValue = summary.DiscountValue,
                    IsVatApplicable = summary.IsVatApplicable,
                    VatPercent = summary.VatPercent
                },

                PreparedBy = new PreparedBySnapshot
                {
                    PreparedByName = printable.PreparedByName ?? "",
                    PreparedByDept = printable.PreparedByDept ?? "",
                    PreparedByStatus = company.ProformaStatus ?? "",
                    TermsText = printable.TermsText ?? "",
                    FooterDisclaimer = printable.FooterDisclaimer ?? ""
                }
            };

            // ── PHASE A/B: Step 9 Profit & Loss ────────────────────────
            // The printable VM carries a nullable link to the Step 9 VM.
            // When it is present we persist the costed breakdown; when it
            // is not (older wiring, or a VM built outside the wizard) the
            // snapshot keeps its empty default and nothing breaks.
            if (printable?.Step4Pricing != null)
            {
                var step4 = printable.Step4Pricing;
                snap.Step4Pricing = new Step4PricingSnapshot
                {
                    SelectedGlassTypeCode = step4.SelectedGlassType?.Code ?? "",
                    GeneratedSpecName = step4.CaptureSectionSnapshots()
                        .ElementAtOrDefault(Math.Max(0, step4.SelectedCalculationSectionNumber - 1))?.GeneratedSpecName ?? "",
                    EstimatedRatePerSqm = step4.EstimatedRatePerSqm,
                    Sgu = new SguPricingPanelSnapshot
                    {
                        SelectedThickness = step4.SguVM.SelectedThickness ?? "",
                        GlassColor = step4.SguVM.GlassColor ?? "",
                        SelectedTreatment = step4.SguVM.SelectedTreatment ?? "",
                        BasePricePerSqm = step4.SguVM.BasePricePerSqm,
                        CuttingCostPerSqm = step4.SguVM.CuttingCostPerSqm,
                        TemperingCostPerSqm = step4.SguVM.TemperingCostPerSqm,
                        YieldFactor = step4.SguVM.YieldFactor,
                        TargetMarginPercent = step4.SguVM.TargetMarginPercent,
                        SelectedPolishOption = step4.SguVM.SelectedPolishOption ?? "",
                        PanelExtra = step4.SguVM.PanelExtra,
                        UseCustomPanelExtra = step4.SguVM.UseCustomPanelExtra,
                        CustomPanelExtra = step4.SguVM.CustomPanelExtra,
                        SheetWidthMm = step4.SguVM.SheetWidthMm,
                        SheetHeightMm = step4.SguVM.SheetHeightMm
                    },
                    Dgu = new DguPricingPanelSnapshot
                    {
                        OuterThickness = step4.DguVM.OuterThickness ?? "",
                        OuterGlassColor = step4.DguVM.OuterGlassColor ?? "",
                        OuterTreatment = step4.DguVM.OuterTreatment ?? "",
                        OuterBasePrice = step4.DguVM.OuterBasePrice,
                        OuterSheetWidthMm = step4.DguVM.OuterSheetWidthMm,
                        OuterSheetHeightMm = step4.DguVM.OuterSheetHeightMm,
                        SpacerWidth = step4.DguVM.SpacerWidth ?? "",
                        SpacerMaterial = step4.DguVM.SpacerMaterial ?? "",
                        SpacerPrice = step4.DguVM.SpacerPrice,
                        SpacerExtra = step4.DguVM.SpacerExtra ?? "",
                        UInsertPricePerSqm = step4.DguVM.UInsertPricePerSqm,
                        PanelExtra = step4.DguVM.PanelExtra ?? "",
                        InnerThickness = step4.DguVM.InnerThickness ?? "",
                        InnerGlassColor = step4.DguVM.InnerGlassColor ?? "",
                        InnerTreatment = step4.DguVM.InnerTreatment ?? "",
                        InnerBasePrice = step4.DguVM.InnerBasePrice,
                        InnerSheetWidthMm = step4.DguVM.InnerSheetWidthMm,
                        InnerSheetHeightMm = step4.DguVM.InnerSheetHeightMm,
                        YieldFactor = step4.DguVM.YieldFactor,
                        MarginProfitPercent = step4.DguVM.MarginProfitPercent
                    },
                    Lam = new LamPricingPanelSnapshot
                    {
                        OuterThickness = step4.LamVM.OuterThickness ?? "",
                        OuterGlassColor = step4.LamVM.OuterGlassColor ?? "",
                        OuterTreatment = step4.LamVM.OuterTreatment ?? "",
                        OuterBasePrice = step4.LamVM.OuterBasePrice,
                        OuterSheetWidthMm = step4.LamVM.OuterSheetWidthMm,
                        OuterSheetHeightMm = step4.LamVM.OuterSheetHeightMm,
                        InnerThickness = step4.LamVM.InnerThickness ?? "",
                        InnerGlassColor = step4.LamVM.InnerGlassColor ?? "",
                        InnerTreatment = step4.LamVM.InnerTreatment ?? "",
                        InnerBasePrice = step4.LamVM.InnerBasePrice,
                        InnerSheetWidthMm = step4.LamVM.InnerSheetWidthMm,
                        InnerSheetHeightMm = step4.LamVM.InnerSheetHeightMm,
                        PvbThickness = step4.LamVM.PvbThickness ?? "",
                        PvbColor = step4.LamVM.PvbColor ?? "",
                        PvbMaterialRate = step4.LamVM.PvbMaterialRate,
                        LaminationPolish = step4.LamVM.LaminationPolish ?? "",
                        OuterCuttingCost = step4.LamVM.OuterCuttingCost,
                        InnerCuttingCost = step4.LamVM.InnerCuttingCost,
                        OuterTemperingCost = step4.LamVM.OuterTemperingCost,
                        InnerTemperingCost = step4.LamVM.InnerTemperingCost,
                        YieldFactor = step4.LamVM.YieldFactor,
                        MarginProfitPercent = step4.LamVM.MarginProfitPercent
                    },
                    DguLam = new DguLamPricingPanelSnapshot
                    {
                        OuterThickness = step4.DguLamVM.OuterThickness ?? "",
                        OuterGlassColor = step4.DguLamVM.OuterGlassColor ?? "",
                        OuterTreatment = step4.DguLamVM.OuterTreatment ?? "",
                        OuterBasePrice = step4.DguLamVM.OuterBasePrice,
                        OuterSheetWidthMm = step4.DguLamVM.OuterSheetWidthMm,
                        OuterSheetHeightMm = step4.DguLamVM.OuterSheetHeightMm,
                        SpacerThickness = step4.DguLamVM.SpacerThickness ?? "",
                        SpacerPrice = step4.DguLamVM.SpacerPrice,
                        SpacerExtra = step4.DguLamVM.SpacerExtra ?? "",
                        LamOuterThickness = step4.DguLamVM.LamOuterThickness ?? "",
                        LamOuterColor = step4.DguLamVM.LamOuterColor ?? "",
                        LamOuterTreatment = step4.DguLamVM.LamOuterTreatment ?? "",
                        LamOuterBasePrice = step4.DguLamVM.LamOuterBasePrice,
                        LamOuterSheetWidthMm = step4.DguLamVM.LamOuterSheetWidthMm,
                        LamOuterSheetHeightMm = step4.DguLamVM.LamOuterSheetHeightMm,
                        InterlayerType = step4.DguLamVM.InterlayerType ?? "",
                        InterlayerThickness = step4.DguLamVM.InterlayerThickness ?? "",
                        InterlayerColor = step4.DguLamVM.InterlayerColor ?? "",
                        InterlayerBuyPrice = step4.DguLamVM.InterlayerBuyPrice,
                        InterlayerSellPrice = step4.DguLamVM.InterlayerSellPrice,
                        LamInnerThickness = step4.DguLamVM.LamInnerThickness ?? "",
                        LamInnerColor = step4.DguLamVM.LamInnerColor ?? "",
                        LamInnerTreatment = step4.DguLamVM.LamInnerTreatment ?? "",
                        LamInnerBasePrice = step4.DguLamVM.LamInnerBasePrice,
                        LamInnerSheetWidthMm = step4.DguLamVM.LamInnerSheetWidthMm,
                        LamInnerSheetHeightMm = step4.DguLamVM.LamInnerSheetHeightMm,
                        PanelExtra = step4.DguLamVM.PanelExtra ?? "",
                        EdgePolish = step4.DguLamVM.EdgePolish ?? "",
                        OuterCuttingCost = step4.DguLamVM.OuterCuttingCost,
                        OuterTemperingCost = step4.DguLamVM.OuterTemperingCost,
                        LamOuterCuttingCost = step4.DguLamVM.LamOuterCuttingCost,
                        LamOuterTemperingCost = step4.DguLamVM.LamOuterTemperingCost,
                        LamInnerCuttingCost = step4.DguLamVM.LamInnerCuttingCost,
                        LamInnerTemperingCost = step4.DguLamVM.LamInnerTemperingCost,
                        YieldFactor = step4.DguLamVM.YieldFactor,
                        MarginProfitPercent = step4.DguLamVM.MarginProfitPercent
                    }
                };
                var step4Verdict = ProGlassAutomation.Services.ProformaInvoice
                    .Step4MaterialIntegrity.Evaluate(snap.Step4Pricing);
                snap.Step4Pricing.MaterialIsValid = step4Verdict.IsValid;
                snap.Step4Pricing.MaterialInvalidReason = step4Verdict.Reason;
                snap.Step4PricingSections = step4.CaptureSectionSnapshots().ToList();
                snap.Step4SelectedSectionNumber = step4.SelectedCalculationSectionNumber;
            }

            if (printable?.ProfitLoss != null)
            {
                snap.ProfitLoss = printable.ProfitLoss.BuildSnapshot();
                // PHASE 3: pin the price basis the P&L was computed
                // against. Must never block or fail a save — the stamper
                // degrades to an empty signature on any store hiccup.
                snap.ProfitLoss.ComputedAtUtc = DateTime.UtcNow;
                snap.ProfitLoss.GlassPriceStamp = GlassPriceStamper.Current();
            }

            // Step 5 — spec groups + rows
            foreach (var group in dataGrid.SpecGroups)
            {
                var g = new SpecGroupSnapshot
                {
                    GroupNumber = group.GroupNumber,
                    GroupName = group.GroupName ?? "",
                    SelectedGlassType = group.SelectedGlassType ?? "",
                    SelectedSpecName = group.SelectedSpecName ?? "",
                    LastSyncedStep4SpecName = group.LastSyncedStep4SpecName ?? "",
                    LastSyncedStep4GlassType = group.LastSyncedStep4GlassType ?? "",
                    LastSyncedStep4ContentHash = group.LastSyncedStep4ContentHash ?? "",
                    HasManualStep5SpecOverride = group.HasManualStep5SpecOverride,
                    HasManualStep5GlassTypeOverride = group.HasManualStep5GlassTypeOverride,
                    HasManualStep5BaseRateOverride = group.HasManualStep5BaseRateOverride,
                    ImportedPrice = group.ImportedPrice,
                    Step4EstimatedRatePerSqm = group.Step4EstimatedRatePerSqm,
                    OuterGlassColor = group.OuterGlassColor ?? "",
                    InnerGlassColor = group.InnerGlassColor ?? "",
                    ThirdGlassColor = group.ThirdGlassColor ?? "",
                    MaterialIsValid = group.MaterialIsValid,
                    MaterialInvalidReason = group.MaterialInvalidReason ?? "",
                    BaseRate = group.BaseRate,
                    SurchargePercent = group.SurchargePercent,

                    // PHASE G: persist the Step 4 sheet choice with the
                    // invoice so re-costing later uses the quoted sheet.
                    OuterSheetWidthMm = group.OuterSheetWidthMm,
                    OuterSheetHeightMm = group.OuterSheetHeightMm,
                    InnerSheetWidthMm = group.InnerSheetWidthMm,
                    InnerSheetHeightMm = group.InnerSheetHeightMm,
                    OuterSheetPricePerSqm = group.OuterSheetPricePerSqm,
                    InnerSheetPricePerSqm = group.InnerSheetPricePerSqm,
                    ThirdSheetWidthMm = group.ThirdSheetWidthMm,
                    ThirdSheetHeightMm = group.ThirdSheetHeightMm,
                    ThirdSheetPricePerSqm = group.ThirdSheetPricePerSqm,
                    UInsertPricePerSqm = group.UInsertPricePerSqm,

                    // PHASE 1-AUDIT: persist the production rates this group
                    // was QUOTED with, so Step 9 re-costs a restored or
                    // multi-section invoice against each group's own inputs.
                    Step4SpacerPricePerSqm = group.Step4SpacerPricePerSqm,
                    Step4InterlayerCostPerSqm = group.Step4InterlayerCostPerSqm,
                    Step4CuttingCostPerSqm = group.Step4CuttingCostPerSqm,
                    Step4TemperingCostPerSqm = group.Step4TemperingCostPerSqm
                };

                foreach (var row in group.ItemRows)
                {
                    g.ItemRows.Add(new GlassItemRowSnapshot
                    {
                        SerialNumber = row.SerialNumber,
                        GlassRef = row.GlassRef ?? "",
                        W1 = row.W1,
                        H1 = row.H1,
                        W2 = row.W2,
                        H2 = row.H2,
                        Qty = row.Qty,
                        Rate = row.Rate,
                        PerimeterFactor = row.PerimeterFactor
                    });
                }

                snap.SpecGroups.Add(g);
            }

            // Step 6 — automation mode + predefined charges
            snap.OtherCharges.UseAdvancedAutomation =
                otherCharges.UseAdvancedAutomation;

            // CHAT 20: SelectedMetricKey and CheckedSpecGroupNumbers are
            // written from row.TargetSelection. Delivery rows and rows
            // without a TargetSelection get empty defaults.
            foreach (var charge in otherCharges.ChargeRows)
            {
                var chargeSnap = new PredefinedChargeSnapshot
                {
                    TemplateKey = charge.TemplateKey ?? "",
                    IsActive = charge.IsActive,
                    SelectedSourceKey = "",   // legacy field — new saves leave empty
                    Rate = charge.Rate,
                    MetricQty = charge.MetricQty,
                    ResultAmount = charge.ResultAmount,
                    PolishSubType = charge.SelectedPolishSubType?.Type.ToString() ?? "",
                    CustomPolishDescription = charge.CustomPolishDescription ?? "",
                    CustomPolishQty = charge.CustomPolishQty
                };

                // CHAT 20: Save per-spec targeting state
                if (charge.TargetSelection != null)
                {
                    chargeSnap.SelectedMetricKey =
                        charge.TargetSelection.SelectedMetric?.Key ?? "";

                    chargeSnap.CheckedSpecGroupNumbers =
                        charge.TargetSelection.AvailableSpecs
                            .Where(s => s.IsChecked)
                            .Select(s => s.SpecGroupNumber)
                            .ToList();
                }

                snap.OtherCharges.PredefinedCharges.Add(chargeSnap);
            }

            // Step 6 — wastages
            foreach (var wastage in otherCharges.WastagesSection.WastageRows)
            {
                var w = new WastageRowSnapshot
                {
                    IsActive = wastage.IsActive,
                    ColorName = wastage.ColorName ?? "",
                    Rate = wastage.Rate,
                    TotalSqm = wastage.TotalSqm,
                    ResultAmount = wastage.ResultAmount,
                    IsManuallyAdded = wastage.IsManuallyAdded,
                    UseOptimizationFormula = wastage.UseOptimizationFormula,
                    FormulaPricePerSqm = wastage.FormulaPricePerSqm,
                    OptimizationPercent = wastage.OptimizationPercent,
                    SheetQuantity = wastage.SheetQuantity
                };
                foreach (var src in wastage.AvailableSources.Where(s => s.IsChecked))
                    w.CheckedSourceKeys.Add($"{src.SpecNumber}-{src.SideLabel}");

                snap.OtherCharges.Wastages.Add(w);
            }

            // Step 6 — custom charges
            foreach (var custom in otherCharges.CustomChargesSection.AddedCharges)
            {
                snap.OtherCharges.CustomCharges.Add(new CustomChargeSnapshot
                {
                    IsActive = custom.IsActive,
                    ChargeName = custom.ChargeName ?? "",
                    Qty = custom.Qty,
                    Unit = custom.Unit.ToString(),
                    Rate = custom.Rate
                });
            }

            // Phase 7.14 — Terms & Conditions
            foreach (var clause in terms.Clauses)
            {
                var c = new TermClauseSnapshot
                {
                    Type = clause.Type.ToString(),
                    Order = clause.Order,
                    IsActive = clause.IsActive,
                    Title = clause.Title ?? "",
                    Body = clause.Body ?? "",
                    ValidityDays = clause.ValidityDays
                };
                foreach (var line in clause.SubLines)
                    c.SubLines.Add(line);

                snap.TermClauses.Add(c);
            }

            return snap;
        }

        // ══════════════════════════════════════════════════════════════
        //  RESTORE PATH: Entity → Snapshot → VMs
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// BUG-18: catches only JsonException.
        /// </summary>
        public static ProformaInvoiceSnapshot DeserializeSnapshot(
            SavedProformaInvoice entity)
        {
            if (entity == null
                || string.IsNullOrWhiteSpace(entity.FullSnapshotJson))
                return new ProformaInvoiceSnapshot();

            try
            {
                return JsonSerializer.Deserialize<ProformaInvoiceSnapshot>(
                    entity.FullSnapshotJson, _jsonOptions)
                    ?? new ProformaInvoiceSnapshot();
            }
            catch (JsonException ex)
            {
                AppLogService.Debug(
                    $"[ProformaInvoiceMapper] Snapshot deserialize failed " +
                    $"for PI '{entity.ProformaId}': {ex.Message}");
                return new ProformaInvoiceSnapshot();
            }
        }

        /// <summary>
        /// Serializes a snapshot back to JSON using the SAME options the rest
        /// of the mapper uses, so a snapshot modified in memory (e.g. an LPO
        /// number added at approval time) round-trips exactly like a normal
        /// save. Used when a business action edits snapshot state outside the
        /// wizard (e.g. approving an invoice and storing the LPO).
        /// </summary>
        public static string SerializeSnapshot(ProformaInvoiceSnapshot snapshot)
            => JsonSerializer.Serialize(snapshot ?? new ProformaInvoiceSnapshot(), _jsonOptions);

        public static void RestoreWizardFromSnapshot(
            ProformaInvoiceSnapshot snap,
            CompanySetupViewModel company,
            ClientProfileViewModel client,
            ProjectSpecsViewModel project,
            DataGridViewModel dataGrid,
            OtherChargesViewModel otherCharges,
            InvoiceMainSummaryViewModel summary,
            TermsAndConditionsViewModel terms,
            PrintableInvoiceViewModel printable)
        {
            if (snap == null) return;

            ProGlassAutomation.Services.ProformaInvoice.SpecSheetRegistry.Clear();

            // ── Wizard progress ─────────────────────────────────────────
            // Restore the furthest step reached so the step-completion
            // indicators and the operator's ability to jump directly to
            // a previously visited step survive an app close or reload.
            if (printable != null)
                printable.FurthestStepReached = snap.FurthestStepReached;

            // ── Step 1: Company ────────────────────────────────────────
            if (snap.Company != null)
            {
                company.CompanyName = snap.Company.CompanyName ?? "";
                company.CompanyTrn = snap.Company.CompanyTrn ?? "";
                company.CompanyLocation = snap.Company.CompanyLocation ?? "";
                company.ProformaId = snap.Company.ProformaId ?? "";
                company.InquiryId = snap.Company.InquiryId;
                company.IssueDate = snap.Company.IssueDate;
                company.ValidUntil = snap.Company.ValidUntil;
                company.ProformaStatus = snap.Company.ProformaStatus ?? "Draft";
                company.Representative = snap.Company.Representative ?? "";
                company.RepresentativeMobile = snap.Company.RepresentativeMobile ?? "";
                company.CompanyEmail = snap.Company.CompanyEmail ?? "";
                company.AccountNumber = snap.Company.AccountNumber ?? "";
                company.BankName = snap.Company.BankName ?? "";
                company.LogoPath = snap.Company.LogoPath ?? "";
                company.CompanyRecordId = snap.Company.CompanyRecordId;
                company.LogoLayout = snap.Company.LogoLayout;
                company.LogoWidthMm = snap.Company.LogoWidthMm;
                company.LogoHeightMm = snap.Company.LogoHeightMm;
            }

            // ── Step 2: Client ─────────────────────────────────────────
            if (snap.Client != null)
            {
                client.CustomerName = snap.Client.CustomerName ?? "";
                client.CustomerTrn = snap.Client.CustomerTrn ?? "";
                client.CustomerReferenceCode = snap.Client.CustomerReferenceCode ?? "";
                client.SalesRepresentative = snap.Client.SalesRepresentative ?? "";
                client.DocumentPreparedBy = snap.Client.DocumentPreparedBy ?? "";
                client.BillingAddress = snap.Client.BillingAddress ?? "";
                client.AttentionExecutiveName = snap.Client.AttentionExecutiveName ?? "";
                client.ClientContactPhone = snap.Client.ClientContactPhone ?? "";
            }

            // ── Step 3: Project ────────────────────────────────────────
            if (snap.Project != null)
            {
                project.ProjectIdentifierName = snap.Project.ProjectIdentifierName ?? "";
                project.ProjectContractNo = snap.Project.ProjectContractNo ?? "";
                project.ProjectReference = snap.Project.ProjectReference ?? "";  // CHAT 15: NEW
                project.SiteDeliveryLocation = snap.Project.SiteDeliveryLocation ?? "";
                project.LpoNumber = snap.Project.LpoNumber ?? "";
            }

            if (printable?.Step4Pricing != null)
            {
                printable.Step4Pricing.RestoreFromSnapshot(
                    snap.Step4Pricing,
                    snap.SpecGroups,
                    snap.Step4PricingSections,
                    snap.Step4SelectedSectionNumber);
            }

            // ── Step 5: DataGrid ───────────────────────────────────────
            // Restoring Step 5 populates SpecGroups → triggers MetricHub
            // refresh → SyncTargetSelectionsFromHub creates AvailableSpecs
            // with all-checked defaults. Step 6 restore below then
            // overrides those defaults with saved per-spec state.
            RestoreDataGrid(snap, dataGrid);

            // ── Step 6: Other Charges ──────────────────────────────────
            RestoreOtherCharges(snap, otherCharges);

            // ── Financial Summary ──────────────────────────────────────
            if (snap.Summary != null)
            {
                summary.BeginBatchUpdate();
                if (System.Enum.TryParse<
                    ProGlassAutomation.Models.ProformaInvoice
                        .Step7PrintPreview.InvoiceMainSummary.DiscountType>(
                        snap.Summary.DiscountType, out var dt))
                {
                    summary.SelectedDiscountType = dt;
                }
                summary.DiscountValue = snap.Summary.DiscountValue;
                summary.IsVatApplicable = snap.Summary.IsVatApplicable;
                summary.VatPercent = snap.Summary.VatPercent;
                summary.EndBatchUpdate();
            }

            // ── Terms & Conditions ─────────────────────────────────────
            RestoreTerms(snap, terms);

            // ── Prepared By ────────────────────────────────────────────
            if (snap.PreparedBy != null)
            {
                printable.PreparedByName = snap.PreparedBy.PreparedByName ?? "";
                printable.PreparedByDept = snap.PreparedBy.PreparedByDept ?? "";
                printable.PreparedByStatus = snap.Company?.ProformaStatus ?? "Draft";
                printable.TermsText = snap.PreparedBy.TermsText ?? "";
                printable.FooterDisclaimer = snap.PreparedBy.FooterDisclaimer ?? "";
            }
        }

        // ── Restore helpers ────────────────────────────────────────────

        private static void RestoreDataGrid(
            ProformaInvoiceSnapshot snap, DataGridViewModel dataGrid)
        {
            if (snap.SpecGroups == null || snap.SpecGroups.Count == 0)
            {
                dataGrid.ResetForNewInvoice();
                return;
            }

            dataGrid.SpecGroups.Clear();

            foreach (var gSnap in snap.SpecGroups)
            {
                if (!string.IsNullOrWhiteSpace(gSnap.SelectedSpecName))
                {
                    ProGlassAutomation.Services.ProformaInvoice.SpecSheetRegistry.Record(
                        gSnap.SelectedSpecName,
                        new ProGlassAutomation.Services.ProformaInvoice.SpecSheetSelection
                        {
                            OuterWidthMm = gSnap.OuterSheetWidthMm,
                            OuterHeightMm = gSnap.OuterSheetHeightMm,
                            InnerWidthMm = gSnap.InnerSheetWidthMm,
                            InnerHeightMm = gSnap.InnerSheetHeightMm,
                            ThirdWidthMm = gSnap.ThirdSheetWidthMm,
                            ThirdHeightMm = gSnap.ThirdSheetHeightMm,
                            OuterPricePerSqm = gSnap.OuterSheetPricePerSqm,
                            InnerPricePerSqm = gSnap.InnerSheetPricePerSqm,
                            ThirdPricePerSqm = gSnap.ThirdSheetPricePerSqm,
                            UInsertPricePerSqm = gSnap.UInsertPricePerSqm
                        });
                }

                var group = new SpecGroup
                {
                    GroupNumber = gSnap.GroupNumber,
                    GroupName = gSnap.GroupName ?? "",
                    // Repository first: setting a type triggers the async
                    // type-filtered catalogue load immediately.
                    Specifications = dataGrid.SpecificationsRepository,
                    SelectedGlassType = gSnap.SelectedGlassType ?? "",
                    SelectedSpecName = gSnap.SelectedSpecName ?? "",
                    LastSyncedStep4SpecName = gSnap.LastSyncedStep4SpecName ?? "",
                    LastSyncedStep4GlassType = gSnap.LastSyncedStep4GlassType ?? "",
                    LastSyncedStep4ContentHash = gSnap.LastSyncedStep4ContentHash ?? "",
                    HasManualStep5SpecOverride = gSnap.HasManualStep5SpecOverride,
                    HasManualStep5GlassTypeOverride = gSnap.HasManualStep5GlassTypeOverride,
                    ImportedPrice = gSnap.ImportedPrice,
                    Step4EstimatedRatePerSqm = gSnap.Step4EstimatedRatePerSqm,
                    OuterGlassColor = gSnap.OuterGlassColor ?? "",
                    InnerGlassColor = gSnap.InnerGlassColor ?? "",
                    BaseRate = gSnap.BaseRate,
                    HasManualStep5BaseRateOverride = gSnap.HasManualStep5BaseRateOverride,
                    SurchargePercent = gSnap.SurchargePercent,

                    // PHASE G: restore the quoted sheet. Old invoices have
                    // zeros here, so Step 9 falls back and reports it.
                    OuterSheetWidthMm = gSnap.OuterSheetWidthMm,
                    OuterSheetHeightMm = gSnap.OuterSheetHeightMm,
                    InnerSheetWidthMm = gSnap.InnerSheetWidthMm,
                    InnerSheetHeightMm = gSnap.InnerSheetHeightMm,
                    OuterSheetPricePerSqm = gSnap.OuterSheetPricePerSqm,
                    InnerSheetPricePerSqm = gSnap.InnerSheetPricePerSqm,
                    ThirdSheetWidthMm = gSnap.ThirdSheetWidthMm,
                    ThirdSheetHeightMm = gSnap.ThirdSheetHeightMm,
                    ThirdSheetPricePerSqm = gSnap.ThirdSheetPricePerSqm,
                    UInsertPricePerSqm = gSnap.UInsertPricePerSqm,

                    // PHASE 1-AUDIT: restore production-rate provenance.
                    // Old invoices have nulls here; Step 9 then reads the
                    // live Step 4 panel exactly as it did before this change.
                    Step4SpacerPricePerSqm = gSnap.Step4SpacerPricePerSqm,
                    Step4InterlayerCostPerSqm = gSnap.Step4InterlayerCostPerSqm,
                    Step4CuttingCostPerSqm = gSnap.Step4CuttingCostPerSqm,
                    Step4TemperingCostPerSqm = gSnap.Step4TemperingCostPerSqm

                };

                if (gSnap.ItemRows != null && gSnap.ItemRows.Count > 0)
                {
                    foreach (var rSnap in gSnap.ItemRows)
                    {
                        var row = new GlassItemRow();
                        row.SetAllSilent(
                            rSnap.GlassRef ?? "",
                            rSnap.W1, rSnap.H1, rSnap.W2, rSnap.H2,
                            rSnap.Qty,
                            rSnap.PerimeterFactor,
                            rSnap.Rate);
                        row.SerialNumber = rSnap.SerialNumber;
                        group.ItemRows.Add(row);
                    }
                }

                dataGrid.SpecGroups.Add(group);
            }

            dataGrid.EnsureDefaultWorkspace();
        }

        private static void RestoreOtherCharges(
            ProformaInvoiceSnapshot snap, OtherChargesViewModel otherCharges)
        {
            if (snap.OtherCharges == null) return;

            // Begin restore mode to prevent UseAdvancedAutomation setter from
            // triggering ApplyAdvancedSuggestions. We want to preserve the
            // saved charge selections, not re-run automation.
            otherCharges.BeginRestore();
            try
            {
                otherCharges.UseAdvancedAutomation =
                    snap.OtherCharges.UseAdvancedAutomation;

                if (snap.OtherCharges.PredefinedCharges != null)
                {
                    foreach (var cSnap in snap.OtherCharges.PredefinedCharges)
                    {
                        var row = otherCharges.ChargeRows.FirstOrDefault(
                            r => r.TemplateKey == cSnap.TemplateKey);
                        if (row == null) continue;

                        row.RestoreIsActive(cSnap.IsActive);
                        row.RestoreRate(cSnap.Rate);

                    // Legacy: SelectedSource restore (pre-Chat 17 invoices)
                    if (!row.IsPolishRow
                        && !string.IsNullOrWhiteSpace(cSnap.SelectedSourceKey))
                    {
                        var src = row.AvailableSources.FirstOrDefault(
                            s => s.Key == cSnap.SelectedSourceKey);
                        if (src != null) row.SelectedSource = src;
                    }

                    if (row.IsPolishRow
                        && !string.IsNullOrWhiteSpace(cSnap.PolishSubType))
                    {
                        if (System.Enum.TryParse<PolishSubType>(
                            cSnap.PolishSubType, out var pst))
                        {
                            var meta = row.AvailablePolishSubTypes
                                .FirstOrDefault(p => p.Type == pst);
                            if (meta != null) row.SelectedPolishSubType = meta;
                        }
                        row.CustomPolishDescription = cSnap.CustomPolishDescription ?? "";
                        row.CustomPolishQty = cSnap.CustomPolishQty;
                    }

                    // ══════════════════════════════════════════════════════
                    // CHAT 20: Restore per-spec targeting state.
                    //
                    // By this point row.TargetSelection.AvailableSpecs is
                    // already populated (with all-checked defaults) because
                    // RestoreDataGrid triggered MetricHub → sync above.
                    //
                    // We override:
                    //   • SelectedMetric — if a saved metric key exists
                    //   • IsChecked      — apply saved checked list;
                    //                      uncheck everything else
                    //
                    // Backward compatibility:
                    //   • Empty SelectedMetricKey → skip metric restore
                    //     (row keeps whatever sync set as default)
                    //   • Null/empty CheckedSpecGroupNumbers → skip checkbox
                    //     restore (row keeps default all-checked state).
                    //     This is exactly how old invoices used to behave.
                    // ══════════════════════════════════════════════════════
                    if (row.TargetSelection != null)
                    {
                        // Restore selected metric
                        string metricKey = !string.IsNullOrWhiteSpace(cSnap.SelectedMetricKey)
                            ? cSnap.SelectedMetricKey
                            : cSnap.SelectedSourceKey;
                        if (!string.IsNullOrWhiteSpace(metricKey))
                        {
                            string normalized = MetricOption.NormalizeKey(metricKey);
                            var metric = row.TargetSelection.AvailableMetrics
                                .FirstOrDefault(m => m.Key == normalized);
                            if (metric != null)
                                row.TargetSelection.SelectedMetric = metric;
                        }

                        // Restore per-spec checked state — only if saved list exists
                        if (cSnap.CheckedSpecGroupNumbers != null
                            && cSnap.CheckedSpecGroupNumbers.Count > 0)
                        {
                            // Build lookup for O(1) membership test
                            var checkedSet = new HashSet<int>(
                                cSnap.CheckedSpecGroupNumbers);

                            // Apply saved state — uncheck any not in saved list
                            foreach (var spec in row.TargetSelection.AvailableSpecs)
                            {
                                spec.IsChecked = checkedSet.Contains(
                                    spec.SpecGroupNumber);
                            }
                        }
                        // else: leave TargetSelection with default all-checked
                        //       state (matches pre-Chat 20 behaviour for
                        //       legacy invoices without saved spec state).
                    }
                }
            }

            // BUG-16: Wastages restore — no UI command, create directly.
            // Clear through the VM helper so restored rows never leave the
            // previous invoice's row subscriptions behind.
            otherCharges.WastagesSection.ClearWastageRows();
            if (snap.OtherCharges.Wastages != null)
            {
                foreach (var wSnap in snap.OtherCharges.Wastages)
                {
                    var newRow = new WastageRow
                    {
                        IsActive = wSnap.IsActive,
                        ColorName = wSnap.ColorName ?? "",
                        Rate = wSnap.Rate,
                        // Legacy snapshots predate the flag and default
                        // false → generated rows, matching the rebuild
                        // behaviour those invoices always had.
                        IsManuallyAdded = wSnap.IsManuallyAdded,
                        UseOptimizationFormula = wSnap.UseOptimizationFormula,
                        FormulaPricePerSqm = wSnap.FormulaPricePerSqm,
                        OptimizationPercent = wSnap.OptimizationPercent,
                        SheetQuantity = wSnap.SheetQuantity > 0
                            ? wSnap.SheetQuantity
                            : 1
                    };

                    otherCharges.WastagesSection.PopulateSourcesForRestoredRow(newRow);

                    if (wSnap.CheckedSourceKeys != null)
                    {
                        foreach (var src in newRow.AvailableSources)
                        {
                            string key = $"{src.SpecNumber}-{src.SideLabel}";
                            src.IsChecked = wSnap.CheckedSourceKeys.Contains(key);
                        }
                    }

                    otherCharges.WastagesSection.WastageRows.Add(newRow);
                }
            }
            otherCharges.WastagesSection.RefreshTotal();

            otherCharges.CustomChargesSection.ClearCharges();
            if (snap.OtherCharges.CustomCharges != null)
            {
                foreach (var cSnap in snap.OtherCharges.CustomCharges)
                {
                    var custom = new CustomChargeRow
                    {
                        IsActive = cSnap.IsActive,
                        ChargeName = cSnap.ChargeName ?? "",
                        Qty = cSnap.Qty,
                        Rate = cSnap.Rate
                    };
                    if (System.Enum.TryParse<OtherChargeUnit>(cSnap.Unit, out var unit))
                        custom.Unit = unit;

                    otherCharges.CustomChargesSection.AttachCharge(custom);
                    otherCharges.CustomChargesSection.AddedCharges.Add(custom);
                }
            }
            otherCharges.CustomChargesSection.RefreshTotal();
            }
            finally
            {
                otherCharges.EndRestore();
            }
        }

        private static void RestoreTerms(
            ProformaInvoiceSnapshot snap, TermsAndConditionsViewModel terms)
        {
            if (snap.TermClauses == null) return;

            terms.Clauses.Clear();
            foreach (var cSnap in snap.TermClauses)
            {
                var clause = new TermClause
                {
                    Order = cSnap.Order,
                    IsActive = cSnap.IsActive,
                    Title = cSnap.Title ?? "",
                    Body = cSnap.Body ?? "",
                    ValidityDays = cSnap.ValidityDays
                };
                if (System.Enum.TryParse<TermClauseType>(cSnap.Type, out var t))
                    clause.Type = t;

                if (cSnap.SubLines != null)
                    foreach (var line in cSnap.SubLines)
                        clause.SubLines.Add(line);

                terms.Clauses.Add(clause);
            }
        }
    }
}
