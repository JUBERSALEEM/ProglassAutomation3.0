// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.f.tests/step6.f.2.ProformaBehaviourChecks.cs
// MODULE: step6 — behaviour of the proforma invoice module.
//
// NOT EXECUTED BY THE AUTHOR. There is no .NET SDK on the machine that wrote
// this file, so it is written, reviewed and committed unrun. Treat it as the
// specification of what must be true, not as evidence that it is.
//
// Run on Windows / .NET 8:
//     cd step6.ProformaInvoice/step6.f.tests
//     dotnet run
//
// Storage checks are in step6.f.3, PDF render checks in step6.f.4, and the
// profit-and-loss checks in step6.f.5. Parts stay below rule K's 400-line cap.
//
// The suite uses a disposable database in the temp folder, never production.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Proforma;
using ProGlassAutomation.Models.Users;
using ProGlassAutomation.Services.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static int _failures;
        private static int _passes;

        private static async Task<int> Main()
        {
            Console.WriteLine("step6.f.2 — Proforma Invoice behaviour checks");
            Console.WriteLine();

            Section("Lifecycle");
            Lifecycle();

            Section("Money");
            Money();

            Section("Pricing engine");
            Pricing();

            Section("Spec grid and charges");
            GridAndCharges();

            Section("Other-charge automation parity");
            ChargeAutomation();

            Section("Per-edge polish subtype formulas");
            PolishSubtypeChecks();

            Section("Summary");
            Summary();

            Section("Profit and loss");
            ProfitAndLoss();

            Section("PDF styles and rendering");
            PdfExport();

            Section("Cut-list import and file exports");
            ImportExport();

            Section("Stored body");
            StoredBody();

            Section("Numbering, status and the inquiry link");
            await WithTempFolder("invoice", RepositoryAsync);

            Section("Attachments");
            await WithTempFolder("attachments", AttachmentsAsync);

            Console.WriteLine();
            Console.WriteLine($"{_passes} passed, {_failures} failed.");
            return _failures == 0 ? 0 : 1;
        }

        // ── Lifecycle ───────────────────────────────────────────────────
        private static void Lifecycle()
        {
            Check("draft can be edited", InvoiceLifecycle.CanEditBody(ProformaInvoiceStatus.Draft));
            Check("a sent invoice cannot be edited",
                !InvoiceLifecycle.CanEditBody(ProformaInvoiceStatus.Sent));
            Check("a cancelled invoice cannot be edited",
                !InvoiceLifecycle.CanEditBody(ProformaInvoiceStatus.Cancelled));
            Check("draft → sent is allowed",
                InvoiceLifecycle.CanTransition(ProformaInvoiceStatus.Draft, ProformaInvoiceStatus.Sent));
            Check("sent → approved is allowed",
                InvoiceLifecycle.CanTransition(ProformaInvoiceStatus.Sent, ProformaInvoiceStatus.Approved));
            Check("cancelled is terminal",
                InvoiceLifecycle.IsTerminal(ProformaInvoiceStatus.Cancelled));
            Check("no status leaves cancelled",
                ProformaInvoiceStatus.All
                    .Where(status => !string.Equals(status, ProformaInvoiceStatus.Cancelled, StringComparison.Ordinal))
                    .All(status => !InvoiceLifecycle.CanTransition(ProformaInvoiceStatus.Cancelled, status)));
            Check("approved → draft is refused (a revision is a new document)",
                !InvoiceLifecycle.CanTransition(ProformaInvoiceStatus.Approved, ProformaInvoiceStatus.Draft));
            Check("an unknown status is not \"defined\"",
                !ProformaInvoiceStatus.IsDefined("Closed"));
            Check("all nine legacy stages are numbered in order",
                (int)InvoiceStage.Company == 1 && (int)InvoiceStage.Client == 2 && (int)InvoiceStage.Project == 3
                && (int)InvoiceStage.Pricing == 4 && (int)InvoiceStage.SpecGrid == 5
                && (int)InvoiceStage.OtherCharges == 6 && (int)InvoiceStage.Summary == 7
                && (int)InvoiceStage.Attachments == 8 && (int)InvoiceStage.ProfitLoss == 9);
            Check("stage list has exactly nine entries", Enum.GetValues<InvoiceStage>().Length == 9);
        }

        // ── Money ───────────────────────────────────────────────────────
        private static void Money()
        {
            Eq("2.345 rounds away from zero to 2.35", 2.35m, MoneyRounding.Round(2.345m));
            Eq("2.344 rounds to 2.34", 2.34m, MoneyRounding.Round(2.344m));
            Eq("-2.345 rounds away from zero to -2.35", -2.35m, MoneyRounding.Round(-2.345m));
            Eq("an area keeps four places", 1.2345m, MoneyRounding.RoundArea(1.23449m));
            Check("clamp keeps a value inside its bounds",
                MoneyRounding.ClampPercent(150m, 0m, 100m) == 100m);
            Check("parse never throws on nonsense", MoneyRounding.Parse("not a number") == 0m);
            Check("parse reads a decimal point", MoneyRounding.Parse("12.50") == 12.50m);
            Check("panel area is never below half a square metre",
                GlassMeasure.PanelSqm(500, 500) >= GlassMeasure.MinimumPanelSqm);
            Check("a tiny requirement still buys one sheet",
                GlassMeasure.SheetsRequired(0.01m, GlassMeasure.SheetSqm(3210, 2250)) == 1);
            Check("wastage is clamped, never compounded past 95%",
                GlassMeasure.WithWastage(10m, 500m) == 19.5m);
        }

        // ── Pricing engine ──────────────────────────────────────────────
        private static void Pricing()
        {
            PricingSection sgu = PricingEstimator.NewSection(GlassBuilds.Sgu, 1);
            sgu.Outer.Thickness = "6"; sgu.Outer.Colour = "Clear"; sgu.Outer.Treatment = "FT";
            sgu.Outer.BasePricePerSqm = 100m;
            sgu.Outer.CuttingCostPerSqm = 10m;
            sgu.Outer.TemperingCostPerSqm = 20m;
            sgu.YieldFactor = 0.8m;
            sgu.MarginPercent = 15m;
            PricingResult sguResult = PricingEstimator.Calculate(sgu);
            Check("SGU is complete", sguResult.IsComplete, sguResult.Warnings.Count > 0 ? sguResult.Warnings[0] : "");
            Eq("SGU basis = 100 ÷ 0.8 + 30", 155m, sguResult.CostBasisPerSqm);
            Eq("SGU rate = 155 × 1.15", 178.25m, sguResult.RatePerSqm);
            Check("SGU specification names its single pane",
                sguResult.SpecName == "6mm Clear FT", sguResult.SpecName);

            PricingSection dgu = TwoPane(GlassBuilds.Dgu, 100m, 80m, 12m, 0m, 1m, 10m);
            // ADV v2.0.15 parity: DGU's quoted formula includes the spacer;
            // cutting and tempering are not charged again in the DGU rate.
            // Keep non-zero pane processing inputs here so a future refactor
            // cannot accidentally double-charge them.
            dgu.Outer.CuttingCostPerSqm = 7m;
            dgu.Outer.TemperingCostPerSqm = 11m;
            dgu.Inner.CuttingCostPerSqm = 13m;
            dgu.Inner.TemperingCostPerSqm = 17m;
            PricingResult dguResult = PricingEstimator.Calculate(dgu);
            Eq("DGU basis = (100 + 80) ÷ 1 + spacer 12", 192m, dguResult.CostBasisPerSqm);
            Eq("DGU rate = 192 × 1.10", 211.20m, dguResult.RatePerSqm);
            Check("DGU specification names two panes and the spacer",
                dguResult.SpecName.Contains("ASP", StringComparison.Ordinal)
                && dguResult.SpecName.Split('+').Length == 3, dguResult.SpecName);
            Check("DGU cost card labels its panes Outer and Inner",
                dguResult.CostCard.Panes.Select(pane => pane.PaneLabel).SequenceEqual(new[] { "Outer", "Inner" }),
                string.Join(",", dguResult.CostCard.Panes.Select(pane => pane.PaneLabel)));
            Check("a DGU cost card carries no interlayer",
                dguResult.CostCard.InterlayerCostPerSqm == 0m);

            PricingSection lam = TwoPane(GlassBuilds.Lam, 100m, 80m, 0m, 15m, 1m, 0m);
            lam.Outer.CuttingCostPerSqm = 5m; lam.Inner.CuttingCostPerSqm = 5m;
            PricingResult lamResult = PricingEstimator.Calculate(lam);
            Eq("LAM basis = 180 + PVB 15 + cutting 10", 205m, lamResult.CostBasisPerSqm);
            Check("LAM specification names the interlayer",
                lamResult.SpecName.Contains("PVB", StringComparison.Ordinal), lamResult.SpecName);
            Check("a LAM cost card carries no spacer", lamResult.CostCard.SpacerCostPerSqm == 0m);

            PricingSection dguLam = TwoPane(GlassBuilds.DguLam, 100m, 60m, 12m, 20m, 1m, 20m);
            dguLam.Middle.BasePricePerSqm = 80m;
            dguLam.Middle.Thickness = "6"; dguLam.Middle.Colour = "Clear"; dguLam.Middle.Treatment = "FT";
            PricingResult compound = PricingEstimator.Calculate(dguLam);
            Eq("DGU+LAM basis = 240 + spacer 12 + PVB 20", 272m, compound.CostBasisPerSqm);
            Eq("DGU+LAM rate = 272 × 1.20", 326.40m, compound.RatePerSqm);
            Check("DGU+LAM names three panes, a spacer and an interlayer",
                compound.SpecName.Contains("ASP", StringComparison.Ordinal)
                && compound.SpecName.Contains("PVB", StringComparison.Ordinal)
                && compound.SpecName.Split('+').Length == 5, compound.SpecName);
            Check("DGU+LAM labels its middle pane", compound.CostCard.Panes.Count == 3
                && compound.CostCard.Panes[1].PaneLabel == "Middle");

            PricingSection badYield = TwoPane(GlassBuilds.Dgu, 100m, 80m, 12m, 0m, 0.5m, 0m);
            PricingResult fallback = PricingEstimator.Calculate(badYield);
            Check("an impossible yield is reported", fallback.Warnings.Count > 0);
            Check("the fallback yield is stated as 1.00", fallback.YieldFactorUsed == 1m);
            Eq("the fallback price uses 180 + 12", 192m, fallback.CostBasisPerSqm);

            PricingSection missing = TwoPane(GlassBuilds.Dgu, 100m, 0m, 12m, 0m, 1m, 0m);
            PricingResult incomplete = PricingEstimator.Calculate(missing);
            Check("a missing pane rate makes the section incomplete", !incomplete.IsComplete);
            Check("the missing rate is named", incomplete.Warnings.Count > 0
                && incomplete.Warnings[0].Contains("pane 2", StringComparison.OrdinalIgnoreCase));
            Check("an unknown build is refused rather than priced",
                !PricingEstimator.Calculate(new PricingSection { GlassType = "Triple" }).IsComplete);
        }

        // ── Grid and charges ────────────────────────────────────────────
        private static void GridAndCharges()
        {
            var group = new SpecGroup { GroupNumber = 1, GlassType = GlassBuilds.Sgu, SpecName = "6mm Clear FT" };
            group.Rows.Add(new SpecRow { W1 = 500, H1 = 500, Qty = 2 });
            Eq("two half-metre panels bill one square metre", 1m, group.TotalSqm);
            Eq("two panels are two pieces", 2, group.TotalPieces);
            Check("edge length is measured", group.TotalLm > 0m);
            group.OuterGlassColor = "Clear"; group.InnerGlassColor = "Clear";
            group.ThirdGlassColor = "";
            Eq("a colour used on two panes counts twice", 2m, group.ColorSqm("Clear"));
            group.InnerGlassColor = "Grey";
            Eq("a colour used once counts once", 1m, group.ColorSqm("Clear"));
            Eq("an unused colour has no area", 0m, group.ColorSqm("Bronze"));

            var priced = new ProformaInvoiceDraft();
            priced.EnsureStructure();
            priced.Parties.Company.CompanyName = "ProGlass Automation";
            priced.Parties.Client.ClientName = "Al Noor Glass LLC";
            PricingSection section = priced.PricingSections[0];
            section.Outer.Thickness = "6"; section.Outer.Colour = "Clear"; section.Outer.Treatment = "FT";
            section.Outer.BasePricePerSqm = 100m;
            section.Outer.CuttingCostPerSqm = 10m;
            section.Outer.TemperingCostPerSqm = 20m;
            section.YieldFactor = 1m;
            section.MarginPercent = 0m;
            Check("a priced section becomes a group",
                priced.AddGroupFromSection(1).IsComplete && priced.Groups.Count == 1);
            SpecGroup built = priced.Groups[0];
            built.Rows[0].W1 = 1000; built.Rows[0].H1 = 1000; built.Rows[0].Qty = 1;
            priced.Recalculate();
            Eq("glass subtotal is area × rate", 130m, priced.Summary.GlassSubtotal);
            Check("the group carries its provenance", built.CostCard.Panes.Count == 1
                && built.CostCard.Panes[0].PaneLabel == "Outer");
            Check("the catalogue rows were created once",
                priced.Charges.Predefined.Count == ChargeCatalog.Templates.Count);

            Check("a wastage row follows the grid colour",
                priced.Charges.Wastages.Any(row => row.ColorName == "Clear"));
            priced.Charges.Wastages.Add(new WastageChargeRow
            {
                ColorName = "Manual Bronze",
                IsManuallyAdded = true,
                IsActive = true,
                Rate = 50m,
                SheetQuantity = 2
            });
            priced.Recalculate();
            Check("a manual wastage row survives a rebuild",
                priced.Charges.Wastages.Any(row => row.ColorName == "Manual Bronze"));
            Check("a manual sheet quantity is kept",
                priced.Charges.Wastages.First(row => row.ColorName == "Manual Bronze").SheetQuantity == 2);

        }

        // ── Summary ─────────────────────────────────────────────────────
        private static void Summary()
        {
            var summary = new InvoiceSummary { GlassSubtotal = 1000m, OtherChargesSubtotal = 0m };
            summary.DiscountMethod = DiscountType.Percentage;
            summary.DiscountValue = 10m;
            summary.Recalculate();
            Eq("discount is 10% of 1000", 100m, summary.DiscountAmount);
            Eq("VAT is 5% of 900", 45m, summary.VatAmount);
            Eq("grand total is 945", 945m, summary.GrandTotal);

            var capped = new InvoiceSummary { GlassSubtotal = 100m };
            capped.DiscountMethod = DiscountType.FixedAmount;
            capped.DiscountValue = 500m;
            capped.Recalculate();
            Eq("a discount larger than the net is capped", 100m, capped.DiscountAmount);
            Eq("the grand total cannot go negative", 0m, capped.GrandTotal);

            var noVat = new InvoiceSummary { GlassSubtotal = 100m, IsVatApplicable = false };
            noVat.Recalculate();
            Eq("VAT can be switched off explicitly", 0m, noVat.VatAmount);

            var target = new InvoiceSummary { GlassSubtotal = 1000m };
            bool reached = target.SetTargetGrandTotal(997.50m);
            Check("a reachable target grand total is solved", reached);
            Eq("the solved total matches the target", 997.50m, target.GrandTotal);
            Check("an unreachable target is refused, not faked",
                !new InvoiceSummary { GlassSubtotal = 100m }.SetTargetGrandTotal(900m));
        }

        // ── Profit and loss ─────────────────────────────────────────────
        // ── Helpers ─────────────────────────────────────────────────────
        private static ProformaInvoiceDraft SavableDraft()
        {
            var draft = new ProformaInvoiceDraft
            {
                IssueDate = DateTime.Today,
                ValidUntil = DateTime.Today.AddDays(7)
            };
            draft.EnsureStructure();
            draft.Parties.Company.CompanyName = "ProGlass Automation";
            draft.Parties.Company.Trn = "100123456700003";
            draft.Parties.Client.ClientName = "Al Noor Glass LLC";
            draft.Parties.PreparedBy.FullName = "Juber Saleem";
            PricingSection section = draft.PricingSections[0];
            section.Outer.Thickness = "6"; section.Outer.Colour = "Clear"; section.Outer.Treatment = "FT";
            section.Outer.BasePricePerSqm = 100m;
            section.Outer.PurchasePricePerSqm = 80m;
            section.Outer.CuttingCostPerSqm = 10m;
            section.Outer.TemperingCostPerSqm = 20m;
            section.YieldFactor = 1m;
            section.MarginPercent = 20m;
            draft.AddGroupFromSection(1);
            draft.Groups[0].Rows[0].W1 = 1000;
            draft.Groups[0].Rows[0].H1 = 1000;
            draft.Groups[0].Rows[0].Qty = 1;
            draft.Recalculate();
            return draft;
        }

        private static PricingSection TwoPane(
            string build, decimal outer, decimal inner, decimal spacer, decimal interlayer,
            decimal yield, decimal margin)
        {
            PricingSection section = PricingEstimator.NewSection(build, 1);
            section.Outer.Thickness = "6"; section.Outer.Colour = "Clear"; section.Outer.Treatment = "FT";
            section.Outer.BasePricePerSqm = outer;
            section.Inner.Thickness = "6"; section.Inner.Colour = "Clear"; section.Inner.Treatment = "FT";
            section.Inner.BasePricePerSqm = inner;
            section.SpacerPricePerSqm = spacer;
            section.InterlayerSellPerSqm = interlayer;
            section.YieldFactor = yield;
            section.MarginPercent = margin;
            return section;
        }

        private static User Administrator() => new()
        {
            Id = 1,
            FullName = "Juber Saleem",
            Username = "juber",
            NormalizedUsername = "JUBER",
            Department = "Administration",
            Role = UserCatalog.AdministratorRole,
            IsActive = true
        };

        private static void Section(string title)
        {
            Console.WriteLine("── " + title);
        }

        private static void Check(string name, bool condition, string detail = "")
        {
            if (condition)
            {
                _passes++;
                Console.WriteLine("   PASS  " + name);
            }
            else
            {
                _failures++;
                Console.WriteLine("   FAIL  " + name + (detail.Length == 0 ? "" : " — " + detail));
            }
        }

        private static void Eq(string name, decimal expected, decimal actual) =>
            Check(name, Math.Abs(expected - actual) < 0.005m, $"expected {expected}, got {actual}");
    }
}
