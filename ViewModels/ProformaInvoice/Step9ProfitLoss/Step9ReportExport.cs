// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/ProformaInvoice/Step9ProfitLoss/Step9ReportExport.cs
// PROJECT: ProGlassAutomation
// MODULE: Step 9 Profit & Loss — report export (CSV / Excel / PDF)
//
// WHY THIS FILE
//   The four Step 9 analysis reports (Service, Sheet, Glass-type and the
//   per-spec Target comparison) had rich on-screen output but NO way to
//   export — so a profitable analysis could never leave the screen.
//   This static helper maps each row type to column arrays and hands them
//   to the SAME central ReportingExportService used by every Reports-hub
//   screen, so Step 9 exports behave and look identical to the rest of
//   the product while retaining PI identity in the save name and body.
//
//   A synthetic "TOTAL" row is appended by the owning ViewModels (their
//   totals are recomputed from the source rows, never summed margins) so
//   the exported artifact carries the same column-aligned totals as the
//   on-screen footer.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.Export.Core;
using ProGlassAutomation.Services.Export.FileSystem;
using ProGlassAutomation.Services.Reporting;
using ProGlassAutomation.Services.ProformaInvoice.TargetPricing;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;

namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss
{
    /// <summary>
    /// Shared CSV / Excel / PDF export for the Step 9 Profit &amp; Loss
    /// analysis reports. Row types live in this same namespace, so the
    /// mappers are typed against the real bindable rows.
    /// </summary>
    public static class Step9ReportExport
    {
        /// <summary>Professional report codes shared by every Step 9 export surface.</summary>
        public const string ReportCodeOverview = "OVERVIEW";
        public const string ReportCodeCostLedger = "COST-LEDGER";
        public const string ReportCodePriceDecision = "PRICE-DECISION";
        public const string ReportCodeServices = "SERVICES";
        public const string ReportCodeSheetPricing = "SHEET-PRICING";
        public const string ReportCodeSpecStrategy = "SPEC-STRATEGY";

        /// <summary>
        /// Legacy generic-report overload retained for source compatibility.
        /// Step 9 surfaces use the PI-aware overload below.
        /// </summary>
        public static async Task<ExportResult> RunAsync<T>(
            ExportFormat format,
            IReadOnlyList<T> rows,
            string reportTitle,
            string[] headers,
            Func<T, string[]> textMapper,
            Func<T, object[]> excelMapper,
            string suggestedFileName,
            ExportRequest? requestOverride = null)
        {
            var request = requestOverride ?? new ExportRequest
            {
                PromptForLocation = true,
                OpenAfterSave = true,
                SuggestedFileName = suggestedFileName
            };

            return format switch
            {
                ExportFormat.Csv => await ReportingExportService.ExportCsvAsync(
                    rows, reportTitle, headers, textMapper, request),
                ExportFormat.Excel => await ReportingExportService.ExportExcelAsync(
                    rows, reportTitle, headers, excelMapper, request),
                _ => await ReportingExportService.ExportPdfAsync(
                    rows, reportTitle, headers, textMapper, request)
            };
        }

        /// <summary>
        /// Exports a Step 9 report with a live proforma identity in both its
        /// filename and visible CSV/Excel/PDF header.
        /// </summary>
        public static async Task<ExportResult> RunAsync<T>(
            ExportFormat format,
            IReadOnlyList<T> rows,
            string reportTitle,
            string[] headers,
            Func<T, string[]> textMapper,
            Func<T, object[]> excelMapper,
            string reportCode,
            ReportDocumentContext? reportIdentity,
            ExportRequest? requestOverride = null)
        {
            var documentContext = BuildDocumentContext(
                reportCode,
                reportIdentity,
                DateTime.Now);
            var request = requestOverride ?? new ExportRequest
            {
                PromptForLocation = true,
                OpenAfterSave = true,
                SuggestedFileName = BuildSuggestedFileName(format, documentContext)
            };

            return format switch
            {
                ExportFormat.Csv => await ReportingExportService.ExportCsvAsync(
                    rows, reportTitle, headers, textMapper, request, documentContext),
                ExportFormat.Excel => await ReportingExportService.ExportExcelAsync(
                    rows, reportTitle, headers, excelMapper, request, documentContext),
                _ => await ReportingExportService.ExportPdfAsync(
                    rows, reportTitle, headers, textMapper, request, documentContext)
            };
        }

        /// <summary>
        /// Applies one report code to a live PI identity without mutating the
        /// binding object. A fresh context also guarantees one generated-time
        /// value across the filename and all three rendered formats.
        /// </summary>
        internal static ReportDocumentContext BuildDocumentContext(
            string reportCode,
            ReportDocumentContext? reportIdentity,
            DateTime generatedAt)
        {
            var identity = reportIdentity ?? new ReportDocumentContext();
            string proformaId = string.IsNullOrWhiteSpace(identity.ProformaId)
                ? "PI-DRAFT"
                : identity.ProformaId.Trim();
            string normalizedReportCode = string.IsNullOrWhiteSpace(reportCode)
                ? "REPORT"
                : reportCode.Trim();

            return new ReportDocumentContext
            {
                DocumentPrefix = "PI",
                DocumentId = $"{proformaId} STEP9 {normalizedReportCode}",
                ProformaId = identity.ProformaId,
                CompanyName = identity.CompanyName,
                CustomerName = identity.CustomerName,
                ProjectName = identity.ProjectName,
                IssueDate = identity.IssueDate,
                Status = identity.Status,
                GeneratedAt = generatedAt
            };
        }

        /// <summary>
        /// Builds a no-dialog staged request for the share temp folder,
        /// using the SAME filename logic as the interactive save dialog.
        /// Additive Stage 3 seam — existing callers are unaffected.
        /// </summary>
        internal static ExportRequest StagedRequest(
            ExportFormat format,
            string reportCode,
            ReportDocumentContext? reportIdentity,
            string folder)
        {
            var documentContext = BuildDocumentContext(
                reportCode, reportIdentity, DateTime.Now);
            return new ExportRequest
            {
                OutputPath = System.IO.Path.Combine(
                    folder,
                    BuildSuggestedFileName(format, documentContext)),
                PromptForLocation = false,
                OpenAfterSave = false
            };
        }

        /// <summary>Builds the standard safe PI-oriented filename for a Step 9 artifact.</summary>
        internal static string BuildSuggestedFileName(
            ExportFormat format,
            ReportDocumentContext documentContext) =>
            ExportFileNamer.Build(
                documentContext.DocumentId,
                documentContext.FilenameCustomer,
                documentContext.FilenameReference,
                documentContext.FilenameDate,
                format);

        // ── Formatting helpers ──────────────────────────────────────────
        private static string N(decimal v) => v.ToString("N2", CultureInfo.InvariantCulture);

        private static bool IsSummaryKind(string? kind) =>
            string.Equals(kind, "TOTAL", StringComparison.OrdinalIgnoreCase)
            || (kind ?? "").IndexOf("TOTAL", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool IsSpacerKind(string? kind) =>
            string.Equals(kind, "Spacer", StringComparison.OrdinalIgnoreCase);

        private static bool IsProductionKind(string? kind) =>
            string.Equals(kind, "Production", StringComparison.OrdinalIgnoreCase)
            || (kind ?? "").IndexOf("Production", StringComparison.OrdinalIgnoreCase) >= 0;

        private static bool Mentions(string? value, string token) =>
            (value ?? "").IndexOf(token, StringComparison.OrdinalIgnoreCase) >= 0;

        private static string CostClass(ProfitLossRow r)
        {
            if (IsSummaryKind(r.Kind)) return "SUMMARY";
            if (IsSpacerKind(r.Kind)) return "PRODUCTION SERVICE · SPACER / ASP";
            if (Mentions(r.Kind, "Argon") || Mentions(r.Description, "Argon")) return "PRODUCTION ADD-ON · ARGON GAS";
            if (IsProductionKind(r.Kind)) return "PRODUCTION SERVICE · CUTTING / TEMPERING / LAMINATION";
            if (string.Equals(r.Kind, "Glass", StringComparison.OrdinalIgnoreCase)) return "MATERIAL · GLASS SHEET";
            return "COST LINE";
        }

        private static string ServiceClass(ServiceProfitRow r)
        {
            var name = r.ServiceName ?? "";
            if (string.Equals(r.SpecName, "TOTAL", StringComparison.OrdinalIgnoreCase)) return "SUMMARY";
            if (name.IndexOf("Argon", StringComparison.OrdinalIgnoreCase) >= 0)
                return "ARGON GAS DGU CAVITY ADD-ON";
            if (name.IndexOf("Spacer", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("ASP", StringComparison.OrdinalIgnoreCase) >= 0)
                return "SPACER / ASP PRODUCTION SERVICE";
            if (name.IndexOf("Cutting", StringComparison.OrdinalIgnoreCase) >= 0)
                return "CUTTING SERVICE";
            if (name.IndexOf("Tempering", StringComparison.OrdinalIgnoreCase) >= 0)
                return "TEMPERING SERVICE";
            if (name.IndexOf("PVB", StringComparison.OrdinalIgnoreCase) >= 0
                || name.IndexOf("Interlayer", StringComparison.OrdinalIgnoreCase) >= 0)
                return "LAMINATION / INTERLAYER SERVICE";
            if (name.IndexOf("U-Insert", StringComparison.OrdinalIgnoreCase) >= 0)
                return "U-INSERT SERVICE";
            if (name.IndexOf("Glass", StringComparison.OrdinalIgnoreCase) >= 0)
                return "GLASS MARGIN";
            return "SERVICE";
        }

        private static decimal ExtendedRate(ProfitLossRow r)
        {
            if (r.PricePerSqm <= 0m) return 0m;
            if (string.Equals(r.Kind, "Glass", StringComparison.OrdinalIgnoreCase)
                && r.SheetSqm > 0m
                && r.Quantity == Math.Round(r.Quantity, 0)
                && Math.Abs(r.Amount - Math.Round(r.SheetSqm * r.Quantity * r.PricePerSqm, 2)) <= 0.02m)
                return Math.Round(r.SheetSqm * r.PricePerSqm, 2);
            return r.PricePerSqm;
        }

        private static string CostBasis(ProfitLossRow r)
        {
            if (IsSummaryKind(r.Kind)) return "Subtotal / control total";
            if (IsSpacerKind(r.Kind))
                return $"Spacer service quantity {r.Quantity:0.##} SQM × AED {r.PricePerSqm:N2}/SQM";
            if (Mentions(r.Kind, "Argon") || Mentions(r.Description, "Argon"))
                return $"Argon gas quantity {r.Quantity:0.##} {r.UnitLabel} × AED {r.PricePerSqm:N2}/{r.UnitLabel}";
            if (IsProductionKind(r.Kind))
                return $"Production service quantity {r.Quantity:0.##} {r.UnitLabel} × AED {r.PricePerSqm:N2}/{r.UnitLabel}";
            if (string.Equals(r.Kind, "Glass", StringComparison.OrdinalIgnoreCase) && r.SheetSqm > 0m)
            {
                bool sheetBased = r.Quantity == Math.Round(r.Quantity, 0)
                    && Math.Abs(r.Amount - Math.Round(r.SheetSqm * r.Quantity * r.PricePerSqm, 2)) <= 0.02m;
                return sheetBased
                    ? $"{r.SheetSqm:0.####} sheet SQM × {r.Quantity:0.##} sheet(s) × AED {r.PricePerSqm:N2}/SQM"
                    : $"{r.Quantity:0.##} required SQM × AED {r.PricePerSqm:N2}/SQM";
            }
            return $"{r.Quantity:0.##} {r.UnitLabel} × AED {r.PricePerSqm:N2}/{r.UnitLabel}";
        }

        private static string ExtendedCalculation(ProfitLossRow r)
        {
            if (IsSummaryKind(r.Kind)) return N(r.Amount);
            if (string.Equals(r.Kind, "Glass", StringComparison.OrdinalIgnoreCase)
                && r.SheetSqm > 0m
                && r.Quantity == Math.Round(r.Quantity, 0)
                && Math.Abs(r.Amount - Math.Round(r.SheetSqm * r.Quantity * r.PricePerSqm, 2)) <= 0.02m)
                return $"{r.SheetSqm:0.####} × {r.Quantity:0.##} × {r.PricePerSqm:N2} = {N(r.Amount)}";
            return $"{r.Quantity:0.##} × {r.PricePerSqm:N2} = {N(r.Amount)}";
        }

        private static string ServiceNote(ProfitLossRow r)
        {
            if (IsSpacerKind(r.Kind))
                return "SPACER / ASP production cost — includes DGU spacer work and related cutting/tempering/assembly where configured in Step 4.";
            if (Mentions(r.Kind, "Argon") || Mentions(r.Description, "Argon"))
                return "ARGON GAS add-on — DGU cavity gas filling. Shown separately from glass sheet price and spacer/ASP service cost.";
            if (IsProductionKind(r.Kind))
                return "Production/service charge — cutting, tempering, lamination/interlayer, U-insert or Step 6 production charge.";
            if (string.Equals(r.Kind, "Glass", StringComparison.OrdinalIgnoreCase))
                return "Glass material line — sheet/area basis shown separately from production service charges.";
            return "";
        }

        // ── SERVICE PROFITABILITY ───────────────────────────────────────
        public static readonly string[] ServiceHeaders =
        {
            "CATEGORY", "SPEC", "SERVICE / ADD-ON", "QTY BASIS", "RATE", "CHARGE",
            "COST / PROFIT", "STATUS / NOTE"
        };

        public static string[] ServiceMap(ServiceProfitRow r) => new[]
        {
            ServiceClass(r),
            r.SpecName,
            $"{r.ServiceName} — {r.Description}",
            r.Area > 0m ? $"{r.Area:0.##} SQM" : "—",
            r.Area > 0m ? $"AED {(r.Charge / r.Area):N2}/SQM" : "—",
            $"AED {r.Charge:N2}",
            $"Cost AED {r.Cost:N2} · Profit AED {r.Profit:N2} · Margin {r.MarginPercent:0.#}%",
            r.SpecName == "TOTAL" ? "Control total"
                : r.Cost <= 0m && r.Charge > 0m ? "Charge visible; detailed cost model not allocated"
                : r.IsProfitable ? "Profit" : "Loss"
        };

        public static object[] ServiceMapExcel(ServiceProfitRow r) => new object[]
        {
            ServiceClass(r),
            r.SpecName,
            $"{r.ServiceName} — {r.Description}",
            r.Area > 0m ? $"{r.Area:0.##} SQM" : "—",
            r.Area > 0m ? Math.Round(r.Charge / r.Area, 2) : 0m,
            r.Charge,
            $"Cost {r.Cost:N2} / Profit {r.Profit:N2} / Margin {r.MarginPercent:0.#}%",
            r.SpecName == "TOTAL" ? "Control total"
                : r.Cost <= 0m && r.Charge > 0m ? "Charge visible; detailed cost model not allocated"
                : r.IsProfitable ? "Profit" : "Loss"
        };

        // ── SHEET PROFITABILITY ─────────────────────────────────────────
        public static readonly string[] SheetHeaders =
        {
            "CATEGORY", "SPEC", "PANE / GLASS", "SHEET SIZE", "SHEET QTY / AREA",
            "RATE / SQM", "EFFECTIVE RATE / SQM", "SHEET PRICE CALC", "STEP 6 / WASTAGE", "EFFICIENCY", "STATUS / NOTE"
        };

        /// <summary>True on ⚑ FOCUS highlight rows (2026-09-11): they carry
        /// a message, not money — every numeric column prints blank so the
        /// reader's eye lands on the note.</summary>
        private static bool IsSheetHighlight(SheetProfitRow r) =>
            r.GlassTypeLabel == "HIGHLIGHT";

        public static string[] SheetMap(SheetProfitRow r)
        {
            if (IsSheetHighlight(r))
            {
                return new[]
                {
                    "⚑ FOCUS", r.SpecName, r.PaneSide, "", "", "", "", "", "", "",
                    r.DiagnosticMessage
                };
            }
            return new[]
            {
            r.GlassTypeLabel == "TOTAL" ? "TOTAL" : "MATERIAL SHEET PRICE",
            r.SpecName,
            $"{r.GlassTypeLabel} · {r.PaneGlassDisplay}",
            r.SheetSize,
            $"Sheet {r.SheetSqm:0.####} SQM · buy {r.BuySheets:0.##} · used {r.UsedSheets:0.##} · waste {r.WastageSheets:0.##}",
            $"AED {r.PricePerSqm:N2}/SQM",
            r.EffectivePricePerSqm > 0m
                ? $"AED {r.EffectivePricePerSqm:N2}/SQM{(r.YieldPremiumPercent > 0m ? $" (+{r.YieldPremiumPercent:0.#}% yield)" : "")}"
                : "—",
            $"Buy AED {r.BuySheetPrice:N2} · Used AED {r.UsedSheetPrice:N2} · Waste AED {r.WastageValue:N2}",
            r.Step6Charge > 0m
                ? $"Step 6 AED {r.Step6Charge:N2} · {r.Step6SourceLabel} · {r.Step6SourceSqm:0.###} SQM · sheets {r.Step6SheetQuantityDisplay} · rate {r.Step6RateDisplay}"
                : "No active Step 6 charge on this sheet line",
            $"{r.UsageEfficiencyPercent:N2}% used · {r.UsedPriceSharePercent:0.#}% used value · {r.WastageValueSharePercent:0.#}% waste value",
            $"{SheetStatus(r)} · {r.DiagnosticMessage}"
            };
        }

        public static object[] SheetMapExcel(SheetProfitRow r)
        {
            if (IsSheetHighlight(r))
            {
                return new object[]
                {
                    "⚑ FOCUS", r.SpecName, r.PaneSide, "", "", "", "", "", "", "",
                    r.DiagnosticMessage
                };
            }
            return new object[]
            {
            r.GlassTypeLabel == "TOTAL" ? "TOTAL" : "MATERIAL SHEET PRICE",
            r.SpecName,
            $"{r.GlassTypeLabel} · {r.PaneGlassDisplay}",
            r.SheetSize,
            $"Sheet {r.SheetSqm:0.####} SQM / buy {r.BuySheets:0.##} / used {r.UsedSheets:0.##} / waste {r.WastageSheets:0.##}",
            r.PricePerSqm,
            r.EffectivePricePerSqm,
            $"Buy {r.BuySheetPrice:N2} / Used {r.UsedSheetPrice:N2} / Waste {r.WastageValue:N2}",
            r.Step6Charge > 0m
                ? $"Step 6 {r.Step6Charge:N2} / {r.Step6SourceLabel} / {r.Step6SourceSqm:0.###} SQM / sheets {r.Step6SheetQuantityDisplay} / rate {r.Step6RateDisplay}"
                : "No active Step 6 charge",
            $"{r.UsageEfficiencyPercent:N2}% used / {r.UsedPriceSharePercent:0.#}% used value / {r.WastageValueSharePercent:0.#}% waste value",
            $"{SheetStatus(r)} · {r.DiagnosticMessage}"
            };
        }

        // The total row is marked by GlassTypeLabel == "TOTAL"; its computed
        // StatusLabel ("NO CHARGE"/"OK") is meaningless, so it prints "—".
        private static string SheetStatus(SheetProfitRow r) =>
            r.GlassTypeLabel == "TOTAL" || r.GlassTypeLabel == "HIGHLIGHT"
                ? "—"
                : r.StatusLabel;

        // ── GLASS TYPE COMPARISON ───────────────────────────────────────
        public static readonly string[] GlassTypeHeaders =
        {
            "CATEGORY", "BUILD / SPEC TYPE", "VOLUME", "VALUE SUMMARY", "MARGIN RESULT", "STATUS / NOTE"
        };

        public static string[] GlassTypeMap(GlassTypeComparisonRow r) => new[]
        {
            r.GlassType == "TOTAL" ? "OVERALL SPEC STRATEGY TOTAL" : $"{r.GlassType} SPEC STRATEGY",
            r.GlassType,
            $"{r.JobCount:N0} job(s) · {r.TotalArea:0.##} SQM",
            $"Revenue AED {r.TotalRevenue:N2} · Cost AED {r.TotalCost:N2}",
            $"Profit AED {r.TotalProfit:N2} · Margin {r.MarginPercent:0.#}%",
            GlassStatus(r)
        };

        public static object[] GlassTypeMapExcel(GlassTypeComparisonRow r) => new object[]
        {
            r.GlassType == "TOTAL" ? "OVERALL SPEC STRATEGY TOTAL" : $"{r.GlassType} SPEC STRATEGY",
            r.GlassType,
            $"{r.JobCount:N0} job(s) / {r.TotalArea:0.##} SQM",
            $"Revenue {r.TotalRevenue:N2} / Cost {r.TotalCost:N2}",
            $"Profit {r.TotalProfit:N2} / Margin {r.MarginPercent:0.#}%",
            GlassStatus(r)
        };

        private static string GlassStatus(GlassTypeComparisonRow r) =>
            r.GlassType == "TOTAL" ? "—"
            : r.IsBestPerformer ? "BEST"
            : r.IsWorstPerformer ? "WORST"
            : r.IsProfitable ? "PROFIT"
            : "LOSS";

        // ── MAIN P&L COST BREAKDOWN ────────────────────────────────────
        public static readonly string[] PnlHeaders =
        {
            "CATEGORY", "SPEC", "ITEM", "QTY × RATE BASIS", "SHEET / SOURCE",
            "AMOUNT", "READING NOTE"
        };

        public static string[] PnlMap(ProfitLossRow r) => new[]
        {
            CostClass(r),
            r.SpecDisplay,
            $"{r.Kind}: {r.Description}",
            CostBasis(r),
            IsSummaryKind(r.Kind) ? "—" : $"{r.SizeDisplay} · {r.SheetSource}",
            $"AED {r.Amount:N2}",
            $"{ExtendedCalculation(r)}. {ServiceNote(r)}"
        };

        public static object[] PnlMapExcel(ProfitLossRow r) => new object[]
        {
            CostClass(r),
            r.SpecDisplay,
            $"{r.Kind}: {r.Description}",
            CostBasis(r),
            IsSummaryKind(r.Kind) ? "—" : $"{r.SizeDisplay} · {r.SheetSource}",
            r.Amount,
            $"{ExtendedCalculation(r)}. {ServiceNote(r)}"
        };

        // ── TARGET COMPARISON / SPEC DETAIL ───────────────────────────
        //
        // IMPORTANT TERMINOLOGY
        //   • "Step 5 given / quoted price" is the customer's rate held in
        //     Step 5 (the source control calls it Base Price).
        //   • "Normal formula base" is the pre-profit, pre-uplift DGU/SGU/
        //     LAM formula result. It is never the Step 5 given price.
        //
        // Keeping these in separately named columns/rows is intentional:
        // estimators must be able to audit both price bases without treating
        // the Step 5 label as the DGU normal-price formula.
        public static readonly string[] TargetHeaders =
        {
            "SECTION", "SPECIFICATION", "FORMULA INPUTS",
            "NORMAL FORMULA BASE (NO PROFIT)", "STEP 5 GIVEN / QUOTED PRICE",
            "BASE RESULT", "REQUIRED TARGET (+ PROFIT)",
            "TARGET RESULT / STATUS", "AUDIT / WARNINGS"
        };

        /// <summary>
        /// Maps one specification to a self-contained price-decision record.
        /// The first field is deliberately a unique SECTION value, so the
        /// shared CSV, Excel and PDF exporter creates one visible block per
        /// specification instead of compressing all specifications together.
        /// All values are projected from the canonical calculator result;
        /// this exporter never recalculates a target or a margin.
        /// </summary>
        public static string[] TargetMap(SpecTargetRowViewModel r) => new[]
        {
            PriceDecisionSection(r),
            SpecificationIdentity(r),
            FormulaInputSummary(r),
            FormulaBaseSummary(r),
            Step5GivenSummary(r),
            BaseResultSummary(r),
            RequiredTargetSummary(r),
            TargetResultSummary(r),
            AuditSummary(r)
        };

        /// <summary>
        /// Uses exactly the same textual cells as CSV/PDF. This prevents an
        /// Excel-specific projection from quietly losing a BASE or TARGET
        /// field when this report evolves.
        /// </summary>
        public static object[] TargetMapExcel(SpecTargetRowViewModel r) =>
            TargetMap(r).Cast<object>().ToArray();

        // ── PER-SPECIFICATION PRICE DECISION PROJECTION ────────────────

        /// <summary>
        /// Builds the discrete rows that mirror Overview card 3's BASE and
        /// TARGET decision trail for one specification. The section name is
        /// spec-scoped so every export format yields a separate block for
        /// every specification group.
        /// </summary>
        internal static IReadOnlyList<OverviewReportRow> BuildPriceDecisionRows(
            SpecTargetRowViewModel? spec)
        {
            if (spec == null) return Array.Empty<OverviewReportRow>();

            string section = PriceDecisionSection(spec);
            var rows = new List<OverviewReportRow>();

            void Add(string item, string value, string detail = "") =>
                rows.Add(new OverviewReportRow
                {
                    Section = section,
                    Item = item,
                    Value = value,
                    Detail = detail
                });

            Add("Specification", SpecificationIdentity(spec),
                $"Step 5 dimensions: {OneLine(spec.DimensionsSummary)}");

            Add("Formula inputs", FormulaInputSummary(spec),
                $"Step 4 pane/sheet handoff: {OneLine(spec.Step4PaneSheetSummary)}");

            Add("Normal formula base price — no profit", FormulaBaseRate(spec),
                FormulaBaseDetail(spec));

            Add("Step 5 given / quoted price", GivenRate(spec),
                Step5GivenDetail(spec));

            Add("BASE result — against normal formula base", BaseResultSummary(spec),
                BaseResultDetail(spec));

            Add("Required target price — formula base + profit", RequiredTargetRate(spec),
                RequiredTargetDetail(spec));

            Add("TARGET result — against required target", TargetResultSummary(spec),
                TargetResultDetail(spec));

            Add("Decision / validation", spec.StatusLabel, AuditSummary(spec));

            return rows;
        }

        private static string PriceDecisionSection(SpecTargetRowViewModel? r)
        {
            if (r == null) return "PRICE DECISION";
            return r.SpecNumber > 0
                ? $"{r.SpecNumberDisplay} · PRICE DECISION"
                : "INVOICE-WIDE · PRICE DECISION";
        }

        private static string SpecificationIdentity(SpecTargetRowViewModel? r)
        {
            if (r == null) return "—";
            return $"{r.SpecDisplayName} · {r.BuildTypeLabel} · Area {r.AreaSqmDisplay}";
        }

        private static string FormulaInputSummary(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null) return "Formula inputs unavailable";

            var parts = new List<string>();
            switch (result.BuildType)
            {
                case GlassBuildType.Sgu:
                    parts.Add($"Glass {FormulaRate(result.OuterGlassRatePerSqm)}");
                    break;

                case GlassBuildType.Dgu:
                case GlassBuildType.Lam:
                    parts.Add($"Outer {FormulaRate(result.OuterGlassRatePerSqm)}");
                    parts.Add($"Inner {FormulaRate(result.InnerGlassRatePerSqm)}");
                    break;

                case GlassBuildType.DguLam:
                    parts.Add($"Outer {FormulaRate(result.OuterGlassRatePerSqm)}");
                    parts.Add($"Inner {FormulaRate(result.InnerGlassRatePerSqm)}");
                    parts.Add($"Third {FormulaRate(result.ThirdGlassRatePerSqm)}");
                    break;

                default:
                    parts.Add($"Outer {FormulaRate(result.OuterGlassRatePerSqm)}");
                    if (result.InnerGlassRatePerSqm != 0m)
                        parts.Add($"Inner {FormulaRate(result.InnerGlassRatePerSqm)}");
                    if (result.ThirdGlassRatePerSqm != 0m)
                        parts.Add($"Third {FormulaRate(result.ThirdGlassRatePerSqm)}");
                    break;
            }

            if (result.BuildType is GlassBuildType.Dgu or GlassBuildType.DguLam
                || result.AspRatePerSqm != 0m)
            {
                parts.Add($"ASP {FormulaRate(result.AspRatePerSqm)}");
            }

            if (result.ProcessingRatePerSqm != 0m)
                parts.Add($"Processing {FormulaRate(result.ProcessingRatePerSqm)}");

            parts.Add(result.YieldFactor > 0m
                ? $"Yield {Compact(result.YieldFactor)}"
                : "Yield missing");
            parts.Add($"Profit uplift {Compact(result.TargetUpliftPercent)}%");

            return string.Join(" · ", parts);
        }

        private static string FormulaBaseRate(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            return result != null && result.BasePricePerSqm > 0m
                ? MoneyRate(result.BasePricePerSqm)
                : "—";
        }

        private static string GivenRate(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            return result != null && result.GivenPricePerSqm > 0m
                ? MoneyRate(result.GivenPricePerSqm)
                : "—";
        }

        private static string RequiredTargetRate(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            return result != null && result.FinalRequiredTargetPerSqm > 0m
                ? MoneyRate(result.FinalRequiredTargetPerSqm)
                : "—";
        }

        private static string FormulaBaseSummary(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            return result != null && result.BasePricePerSqm > 0m
                ? $"{BaseFormulaExpression(r)} = {MoneyRate(result.BasePricePerSqm)} (normal formula base; no profit)"
                : $"Normal formula base unavailable · {BaseFormulaExpression(r)}";
        }

        private static string Step5GivenSummary(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null) return "—";

            string value = result.GivenPricePerSqm > 0m
                ? $"{MoneyRate(result.GivenPricePerSqm)} · Step 5 value {Money(result.GivenValue)}"
                : "— (Step 5 quote missing)";

            return result.ExtraProfitPerSqm.HasValue
                ? $"{value} · judged {MoneyRate(result.GivenPriceJudgedPerSqm)}"
                : value;
        }

        private static string BaseResultSummary(SpecTargetRowViewModel? r)
        {
            if (r == null || r.Result?.ExtraProfitPerSqm == null)
                return "— (normal formula base or given price incomplete)";

            return $"Balance {r.BaseDifferencePerSqmDisplay} · value {r.BaseDifferenceValueDisplay} · margin {r.BaseMarginDisplay}";
        }

        private static string RequiredTargetSummary(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null || result.FinalRequiredTargetPerSqm <= 0m)
                return "— (target formula incomplete)";

            decimal upliftFactor = 1m + result.TargetUpliftPercent / 100m;
            string calculation = result.BasePricePerSqm > 0m
                ? $"Normal formula base {MoneyRate(result.BasePricePerSqm)} × {Compact(upliftFactor)} = required target {MoneyRate(result.FinalRequiredTargetPerSqm)}"
                : $"Required target {MoneyRate(result.FinalRequiredTargetPerSqm)}";
            return $"{calculation} · uplift {Compact(result.TargetUpliftPercent)}% · value {Money(result.RequiredTargetValue)}";
        }

        private static string TargetResultSummary(SpecTargetRowViewModel? r)
        {
            if (r == null || r.Result == null || !r.Result.MarginAgainstTargetPercent.HasValue)
                return $"— · {r?.StatusLabel ?? "CALCULATION INCOMPLETE"}";

            return $"Quoted balance {r.DifferencePerSqmDisplay} · value {r.DifferenceValueDisplay} · margin {r.MarginAgainstTargetDisplay} · {r.StatusLabel}";
        }

        private static string FormulaBaseDetail(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null) return "No canonical Step 9 formula result is available.";
            if (result.BasePricePerSqm <= 0m)
                return $"Normal formula base is unavailable. Formula basis: {BaseFormulaExpression(r)}.";

            return "Normal formula base before profit: " +
                   $"{BaseFormulaExpression(r)} = {MoneyRate(result.BasePricePerSqm)}. " +
                   "The pre-uplift formula is rounded to 2 decimals before the required target is calculated. " +
                   "This is not the Step 5 given / quoted price.";
        }

        private static string Step5GivenDetail(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null) return "No Step 5 price is available.";
            if (result.GivenPricePerSqm <= 0m)
                return "The Step 5 given / quoted price is missing; no margin is invented.";

            string detail = "This is the customer quoted rate from Step 5 (the source control calls it Base Price), " +
                            "not the normal formula base price.";

            if (result.ExtraProfitPerSqm.HasValue)
                detail += $" Given judged for the BASE and TARGET margin results: {MoneyRate(result.GivenPriceJudgedPerSqm)}.";

            return detail + " " + (r?.UInsertTreatmentDetail ?? "");
        }

        private static string BaseResultDetail(SpecTargetRowViewModel? r)
        {
            if (r == null || r.Result?.ExtraProfitPerSqm == null)
                return "BASE result is unavailable because a required formula input or the Step 5 quote is missing.";

            var result = r.Result!;
            return $"Given judged {MoneyRate(result.GivenPriceJudgedPerSqm)} − normal formula base {MoneyRate(result.BasePricePerSqm)} = {r.BaseDifferencePerSqmDisplay}. " +
                   "BASE margin = (given judged − normal formula base) ÷ normal formula base × 100.";
        }

        private static string RequiredTargetDetail(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null || result.BasePricePerSqm <= 0m || result.FinalRequiredTargetPerSqm <= 0m)
                return "Required target is unavailable because the normal formula base is incomplete.";

            decimal upliftFactor = 1m + result.TargetUpliftPercent / 100m;
            return $"{MoneyRate(result.BasePricePerSqm)} × {Compact(upliftFactor)} " +
                   $"({Compact(result.TargetUpliftPercent)}% profit) = {MoneyRate(result.FinalRequiredTargetPerSqm)}. " +
                   "The target is based on the rounded normal formula base; it is not derived from the Step 5 quote.";
        }

        private static string TargetResultDetail(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null || !result.MarginAgainstTargetPercent.HasValue)
                return "TARGET result is unavailable because a required formula input or the Step 5 quote is missing.";

            return $"TARGET margin/status use given judged {MoneyRate(result.GivenPriceJudgedPerSqm)} against required target {MoneyRate(result.FinalRequiredTargetPerSqm)}. " +
                   "Quoted balance and quoted balance value preserve the raw Step 5 given / quoted price.";
        }

        private static string AuditSummary(SpecTargetRowViewModel? r)
        {
            if (r == null || r.Result == null) return "No canonical Step 9 result is available.";

            string warning = string.IsNullOrWhiteSpace(r.WarningSummary)
                ? string.Empty
                : " Warning: " + r.WarningSummary;
            return $"{r.StatusLabel}. Formula: {r.FormulaText}. U-insert: {r.UInsertTreatmentLabel}; " +
                   $"rate {r.UInsertRateDisplay}; target treatment {r.UInsertAppliedValueDisplay}.{warning}";
        }

        private static string BaseFormulaExpression(SpecTargetRowViewModel? r)
        {
            var result = r?.Result;
            if (result == null) return "Formula unavailable";

            string panes = result.BuildType switch
            {
                GlassBuildType.Sgu => N(result.OuterGlassRatePerSqm),
                GlassBuildType.Dgu or GlassBuildType.Lam =>
                    $"({N(result.OuterGlassRatePerSqm)} + {N(result.InnerGlassRatePerSqm)})",
                GlassBuildType.DguLam =>
                    $"({N(result.OuterGlassRatePerSqm)} + {N(result.InnerGlassRatePerSqm)} + {N(result.ThirdGlassRatePerSqm)})",
                _ => $"({N(result.OuterGlassRatePerSqm)} + {N(result.InnerGlassRatePerSqm)} + {N(result.ThirdGlassRatePerSqm)})"
            };

            var terms = new List<string>
            {
                result.YieldFactor > 0m
                    ? $"{panes} ÷ {Compact(result.YieldFactor)}"
                    : $"{panes} ÷ missing yield"
            };

            if (result.BuildType is GlassBuildType.Dgu or GlassBuildType.DguLam
                || result.AspRatePerSqm != 0m)
            {
                terms.Add(N(result.AspRatePerSqm));
            }

            if (result.ProcessingRatePerSqm != 0m)
                terms.Add(N(result.ProcessingRatePerSqm));

            return string.Join(" + ", terms);
        }

        private static string MoneyRate(decimal value) => $"AED {N(value)}/SQM";
        private static string FormulaRate(decimal value) => $"AED {N(value)}/SQM";
        private static string Money(decimal value) => $"AED {N(value)}";
        private static string Compact(decimal value) => value.ToString("0.##", CultureInfo.InvariantCulture);

        // ── OVERVIEW DETAILED EXPORT ───────────────────────────────────
        public sealed class OverviewReportRow
        {
            public string Section { get; init; } = "";
            public string Item { get; init; } = "";
            public string Value { get; init; } = "";
            public string Detail { get; init; } = "";
        }

        public static readonly string[] OverviewHeaders =
        {
            "SECTION", "ITEM", "VALUE", "DETAIL"
        };

        public static string[] OverviewMap(OverviewReportRow r) => new[]
        {
            r.Section, r.Item, r.Value, r.Detail
        };

        public static object[] OverviewMapExcel(OverviewReportRow r) => new object[]
        {
            r.Section, r.Item, r.Value, r.Detail
        };


        private sealed class Step6ExportCharge
        {
            public string Key { get; init; } = "";
            public string Name { get; init; } = "";
            public string Category { get; init; } = "";
            public decimal Qty { get; init; }
            public string Unit { get; init; } = "";
            public decimal Rate { get; init; }
            public decimal Amount { get; init; }
            public string Formula { get; init; } = "";
            public string TargetSummary { get; init; } = "";
        }

        private static IReadOnlyList<Step6ExportCharge> ActiveStep6Charges(ProfitLossStepViewModel vm)
        {
            var result = new List<Step6ExportCharge>();
            var charges = vm?._otherChargesVm?.ChargeRows;
            if (charges == null) return result;

            foreach (var c in charges.Where(x => x.IsActive && x.ResultAmount > 0m))
            {
                var key = (c.TemplateKey ?? "").Trim().ToUpperInvariant();
                result.Add(new Step6ExportCharge
                {
                    Key = key,
                    Name = c.ChargeName ?? key,
                    Category = key == "ARGON_GAS" ? "ARGON GAS DGU CAVITY ADD-ON" : "STEP 6 PRODUCTION / ADD-ON",
                    Qty = c.MetricQty,
                    Unit = c.Unit.ToString().ToUpperInvariant(),
                    Rate = c.Rate,
                    Amount = c.ResultAmount,
                    Formula = c.FormulaDisplay,
                    TargetSummary = c.TargetSelection?.SummaryText ?? ""
                });
            }

            return result;
        }

        /// <summary>
        /// Projects the authoritative Step 9 glass cost rows before any
        /// summary totals. This is intentionally a projection only: it never
        /// recomputes sheet quantity, rate, amount or cost basis.
        /// </summary>
        private static IReadOnlyList<OverviewReportRow> BuildOperationalSheetRows(ProfitLossStepViewModel vm)
        {
            var result = new List<OverviewReportRow>();
            var glassRows = (vm.Rows ?? Enumerable.Empty<ProfitLossRow>())
                .Where(row => string.Equals(row.Kind, "Glass", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var row in glassRows)
            {
                bool wholeSheetBasis = row.SheetSqm > 0m
                    && row.Quantity == Math.Round(row.Quantity, 0)
                    && Math.Abs(row.Amount - Math.Round(
                        row.SheetSqm * row.Quantity * row.PricePerSqm, 2)) <= 0.02m;
                string size = row.SheetWidthMm > 0 && row.SheetHeightMm > 0
                    ? $"{row.SheetWidthMm} × {row.SheetHeightMm} mm · {Compact(row.SheetSqm)} SQM"
                    : "—";
                string quantityLabel = wholeSheetBasis ? "sheet quantity" : "required quantity";
                string quantityUnit = wholeSheetBasis ? "sheet(s)" : (row.UnitLabel ?? "SQM");
                string source = string.IsNullOrWhiteSpace(row.SheetSource)
                    ? "not recorded"
                    : OneLine(row.SheetSource);

                result.Add(new OverviewReportRow
                {
                    Section = "SHEET OPERATIONAL DETAIL",
                    Item = $"{row.SpecDisplay} — {OneLine(row.Description)}",
                    Value = Money(row.Amount),
                    Detail = $"Sheet size {size}; {quantityLabel} {Compact(row.Quantity)} {quantityUnit}; " +
                             $"rate {MoneyRate(row.PricePerSqm)}; amount {Money(row.Amount)}; " +
                             $"source {source}; cost basis {CostBasis(row)}."
                });
            }

            return result;
        }

        /// <summary>
        /// Gives spacer/ASP its own production-service block immediately after
        /// the sheet operation rows. The values are carried directly from the
        /// existing Step 9 rows; no production costing is recalculated here.
        /// </summary>
        private static IReadOnlyList<OverviewReportRow> BuildSpacerOperationalRows(ProfitLossStepViewModel vm)
        {
            var result = new List<OverviewReportRow>();
            var spacerRows = (vm.Rows ?? Enumerable.Empty<ProfitLossRow>())
                .Where(row => IsSpacerKind(row.Kind))
                .ToList();

            foreach (var row in spacerRows)
            {
                result.Add(new OverviewReportRow
                {
                    Section = "SPACER / ASP PRODUCTION DETAIL",
                    Item = $"{row.SpecDisplay} — {OneLine(row.Description)}",
                    Value = Money(row.Amount),
                    Detail = $"Spacer / ASP production service; quantity {Compact(row.Quantity)} " +
                             $"{row.UnitLabel}; rate {MoneyRate(row.PricePerSqm)}; " +
                             $"amount {Money(row.Amount)}; cost basis {CostBasis(row)}."
                });
            }

            if (spacerRows.Count == 0 && (vm.Rows ?? Enumerable.Empty<ProfitLossRow>())
                .Any(row => string.Equals(row.Kind, "Glass", StringComparison.OrdinalIgnoreCase)))
            {
                result.Add(new OverviewReportRow
                {
                    Section = "SPACER / ASP PRODUCTION DETAIL",
                    Item = "No spacer / ASP production service",
                    Value = "—",
                    Detail = "No spacer / ASP production row applies to the current glass build."
                });
            }

            return result;
        }

        /// <summary>
        /// Adds the richer buy/used/wastage view already calculated by the
        /// Sheet Pricing tab. It is supplemental to the direct cost-row block
        /// above and preserves the tab's existing Step 6 allocation logic.
        /// </summary>
        private static IReadOnlyList<OverviewReportRow> BuildSheetPricingRows(ProfitLossStepViewModel vm)
        {
            var result = new List<OverviewReportRow>();
            var analysis = vm.SheetProfitability;
            var sheetRows = analysis.SheetBreakdown
                .Where(row => !string.Equals(row.GlassTypeLabel, "TOTAL", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (sheetRows.Count == 0)
            {
                var directGlassRows = (vm.Rows ?? Enumerable.Empty<ProfitLossRow>())
                    .Where(row => string.Equals(row.Kind, "Glass", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                if (directGlassRows.Count > 0)
                {
                    result.Add(new OverviewReportRow
                    {
                        Section = "SHEET PRICING SUMMARY",
                        Item = "Step 9 glass sheet cost",
                        Value = Money(directGlassRows.Sum(row => row.Amount)),
                        Detail = "Bought/used/wastage allocation is not currently available; " +
                                 "the exact sheet size, quantity, rate, amount, source and cost basis " +
                                 "remain visible in SHEET OPERATIONAL DETAIL."
                    });
                }

                return result;
            }

            result.Add(new OverviewReportRow
            {
                Section = "SHEET PRICING SUMMARY",
                Item = "Purchased sheets / area",
                Value = $"{Compact(analysis.TotalBuySheets)} sheet(s) · {Compact(analysis.TotalBuySheetArea)} SQM",
                Detail = $"Purchased sheet value {Money(analysis.TotalBuyPrice)}."
            });
            result.Add(new OverviewReportRow
            {
                Section = "SHEET PRICING SUMMARY",
                Item = "Used sheets / area",
                Value = $"{Compact(analysis.TotalUsedSheets)} sheet(s) · {Compact(analysis.TotalUsedArea)} SQM",
                Detail = $"Used material value {Money(analysis.TotalUsedPrice)}."
            });
            result.Add(new OverviewReportRow
            {
                Section = "SHEET PRICING SUMMARY",
                Item = "Wastage / leftover",
                Value = $"{Compact(analysis.TotalWastageSheets)} sheet(s)",
                Detail = $"Wastage material value {Money(analysis.TotalWastageValue)}; usage efficiency {Compact(analysis.UsageEfficiencyPercent)}%."
            });
            result.Add(new OverviewReportRow
            {
                Section = "SHEET PRICING SUMMARY",
                Item = "Step 6 wastage charge",
                Value = Money(analysis.TotalStep6Charge),
                Detail = $"Buy − used − wastage reconciliation: AED {N(analysis.ReconciliationBalance)}."
            });

            foreach (var row in sheetRows)
            {
                string source = string.IsNullOrWhiteSpace(row.Step6SourceLabel)
                    ? "Step 9 cost-row fallback"
                    : $"Step 6 source {OneLine(row.Step6SourceLabel)}";
                result.Add(new OverviewReportRow
                {
                    Section = "SHEET PRICING DETAIL",
                    Item = $"{row.SpecPaneGlassDisplay} — {row.SheetSize}",
                    Value = Money(row.BuySheetPrice),
                    Detail = $"Sheet size {row.SheetSize}; buy {Compact(row.BuySheets)} sheet(s) " +
                             $"({Compact(row.BuySheetArea)} SQM); rate {MoneyRate(row.PricePerSqm)}; " +
                             $"amount {Money(row.BuySheetPrice)}; used {Compact(row.UsedSheets)} sheet(s) / " +
                             $"{Compact(row.UsedArea)} SQM = {Money(row.UsedSheetPrice)}; leftover " +
                             $"{Compact(row.WastageSheets)} sheet(s) / {Compact(row.WastageArea)} SQM = " +
                             $"{Money(row.WastageValue)}; {source}."
                });
            }

            return result;
        }

        public static IReadOnlyList<OverviewReportRow> BuildOverviewRows(ProfitLossStepViewModel vm)
        {
            var rows = new List<OverviewReportRow>();
            if (vm == null) return rows;

            void Add(string section, string item, string value, string detail = "") =>
                rows.Add(new OverviewReportRow { Section = section, Item = item, Value = value, Detail = detail });

            // Report order is deliberate: operations first, the spacer/ASP
            // production separator second, then sheet pricing and management
            // totals/decisions.
            rows.AddRange(BuildOperationalSheetRows(vm));
            rows.AddRange(BuildSpacerOperationalRows(vm));
            rows.AddRange(BuildSheetPricingRows(vm));

            Add("OVERVIEW", "Step 9 rating", vm.Step9MaintenanceRating, vm.Step9ImprovementSummary);
            Add("OVERVIEW", "Target status", vm.TargetJobStatusLabel, vm.MarginAgainstTargetDisplay);
            Add("OVERVIEW", "Required target price", vm.RequiredTargetPriceDisplay, vm.TargetFormulaPreview);
            Add("OVERVIEW", "Expected profit margin", vm.ExpectedProfitMarginDisplay, vm.ExpectedProfitMarginSourceLabel);
            Add("OVERVIEW", "Calculation status", vm.CalculationStateLabel, vm.LastCalculatedAtDisplay);

            Add("SHEET PRICE / MATERIAL", "Gross total including VAT", vm.GrossDisplay, "From Step 7 grand total.");
            Add("SHEET PRICE / MATERIAL", "Net revenue excluding VAT", vm.NetDisplay, "Uses Step 7 subtotal-before-VAT when available.");
            Add("SHEET PRICE / MATERIAL", "Glass sheet cost", vm.GlassCostDisplay, vm.CostBasisLabel);
            Add("PRODUCTION SERVICES", "Production / service cost", vm.ProductionCostDisplay, "Spacer/ASP, cutting, tempering, doubling/lamination and active Step 6 production rows.");

            var spacerRows = (vm.Rows ?? Enumerable.Empty<ProfitLossRow>())
                .Where(r => IsSpacerKind(r.Kind))
                .ToList();
            if (spacerRows.Count > 0)
            {
                Add("PRODUCTION SERVICES", "Spacer / ASP production total",
                    $"AED {spacerRows.Sum(r => r.Amount):N2}",
                    $"{spacerRows.Sum(r => r.Quantity):0.##} SQM service quantity across {spacerRows.Count} spacer line(s). Formula: service SQM × Step 4 ASP rate.");

                foreach (var specSpacer in spacerRows.GroupBy(r => r.SpecDisplay))
                {
                    decimal amount = specSpacer.Sum(r => r.Amount);
                    decimal qty = specSpacer.Sum(r => r.Quantity);
                    decimal weightedRate = qty > 0m ? amount / qty : 0m;
                    Add("SPACER / ASP BY SPECIFICATION", specSpacer.Key,
                        $"AED {amount:N2}",
                        $"Spacer/ASP service: {qty:0.##} SQM × AED {weightedRate:N2}/SQM. This is production/service cost, not a normal glass sheet line.");
                }
            }

            foreach (var charge in ActiveStep6Charges(vm))
            {
                Add(charge.Key == "ARGON_GAS" ? "ARGON GAS / STEP 6 ADD-ONS" : "STEP 6 ADD-ONS",
                    charge.Name,
                    $"AED {charge.Amount:N2}",
                    $"{charge.Category}: {charge.Qty:0.##} {charge.Unit} × AED {charge.Rate:N2} = AED {charge.Amount:N2}. {charge.TargetSummary}. {charge.Formula}");
            }

            Add("PROFIT RESULT", "Total cost", vm.TotalCostDisplay, "Glass + production cost.");
            Add("PROFIT RESULT", vm.ProfitLabel, vm.ProfitDisplay, vm.MarginDisplay);
            Add("PROFIT RESULT", "Overall actual margin", vm.OverallMarginDisplay, vm.PriceBasedMarginDisplay);

            // Job-level card-3 mirror. The wording deliberately separates
            // the Step 5 quote from the normal, pre-profit formula base.
            Add("JOB PRICE DECISION SUMMARY", "Step 5 quoted-price source", vm.GivenPriceSourceLabel, vm.TargetScopeNote);
            Add("JOB PRICE DECISION SUMMARY", "Normal formula base — no profit (area-weighted)",
                vm.BasePriceAverageDisplay,
                "Pre-uplift formula result. This is not the Step 5 given / quoted price.");
            Add("JOB PRICE DECISION SUMMARY", "Step 5 given / quoted price (area-weighted)",
                vm.GivenPricePerSqmDisplay,
                "Customer quoted rate from Step 5; source control label is Base Price.");
            Add("JOB PRICE DECISION SUMMARY", "Given judged for BASE and TARGET margins (area-weighted)",
                vm.GivenJudgedAverageDisplay,
                "Nets a U-insert only when it is included inside the Step 5 quote.");
            Add("JOB PRICE DECISION SUMMARY", "Total Step 5 quoted value", vm.TotalGivenValueDisplay,
                "Area-weighted Step 5 given / quoted price; not the normal formula base price.");
            Add("JOB PRICE DECISION SUMMARY", "Required target price (area-weighted)",
                vm.RequiredTargetPriceDisplay,
                "Rounded normal formula base plus the configured profit uplift.");
            Add("JOB PRICE DECISION SUMMARY", "Total required target value", vm.TotalRequiredTargetValueDisplay,
                "Formula-base target after the configured profit uplift.");
            Add("JOB PRICE DECISION SUMMARY", "Total quoted difference", vm.TotalDifferenceValueDisplay,
                "Positive means the raw Step 5 quote is above the required target.");
            Add("JOB PRICE DECISION SUMMARY", "BASE result — margin against normal formula base",
                vm.BaseMarginPercentDisplay,
                "BASE margin uses given judged for the margin, not the raw Step 5 quote where a U-insert is included.");
            Add("JOB PRICE DECISION SUMMARY", "BASE balance (area-weighted)",
                vm.BaseBalancePerSqmDisplay,
                $"BASE balance value {vm.BaseBalanceValueDisplay}.");
            Add("JOB PRICE DECISION SUMMARY", "TARGET result — margin against required target",
                vm.TargetMarginPercentDisplay,
                "TARGET margin/status use given judged for the margin.");
            Add("JOB PRICE DECISION SUMMARY", "TARGET balance (margin-rate basis, area-weighted)",
                vm.TargetBalancePerSqmDisplay,
                $"Uses given judged when margin is calculable (otherwise the raw quote fallback) − required target. Raw Step 5 quoted-balance value is {vm.TargetBalanceValueDisplay}; it is distinct when a U-insert is included in the quote.");
            Add("JOB PRICE DECISION SUMMARY", "U-insert treatment", vm.UInsertSummaryDisplay, vm.UInsertSummaryDetail);

            Add("CALCULATION FORMULA AUDIT", "All spec formulas", vm.TargetFormulaAllSpecsText, vm.CalculationVersionLabel);
            foreach (var warning in vm.CalculationWarnings ?? Array.Empty<string>())
                Add("CALCULATION FORMULA AUDIT", "Warning", warning, "Resolve before approval/output if blocking.");

            // One complete BASE/TARGET decision section for every Step 5
            // specification group. Do not collapse this to a summary line:
            // the shared exporter treats each unique SECTION as its own
            // CSV/Excel/PDF block, so two specs yield two decision sections.
            foreach (var spec in vm.TargetSpecsView ?? Enumerable.Empty<SpecTargetRowViewModel>())
                rows.AddRange(BuildPriceDecisionRows(spec));

            foreach (var line in vm.Rows ?? Enumerable.Empty<ProfitLossRow>())
                Add("ACTUAL COST LEDGER", line.SpecDisplay, line.AmountDisplay,
                    $"{CostClass(line)}; {line.Description}; basis {CostBasis(line)}; calc {ExtendedCalculation(line)}; sheet {line.SizeDisplay}; source {line.SheetSource}; {ServiceNote(line)}");

            return rows;
        }

        private static string OneLine(string? value) =>
            string.IsNullOrWhiteSpace(value)
                ? ""
                : value.Replace("\r", " ").Replace("\n", " ").Trim();
    }
}
