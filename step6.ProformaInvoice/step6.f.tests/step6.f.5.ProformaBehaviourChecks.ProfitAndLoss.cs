using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.Step6Tests
{
    internal static partial class Program
    {
        private static void ProfitAndLoss()
        {
            var draft = new ProformaInvoiceDraft();
            draft.EnsureStructure();
            draft.Parties.Company.CompanyName = "ProGlass Automation";
            draft.Parties.Client.ClientName = "Al Noor Glass LLC";
            PricingSection section = draft.PricingSections[0];
            section.Outer.Thickness = "6"; section.Outer.Colour = "Clear"; section.Outer.Treatment = "FT";
            section.Outer.BasePricePerSqm = 100m;
            section.Outer.PurchasePricePerSqm = 80m;
            section.Outer.SheetWidthMm = 3210; section.Outer.SheetHeightMm = 2250;
            section.Outer.CuttingCostPerSqm = 10m;
            section.Outer.TemperingCostPerSqm = 20m;
            section.YieldFactor = 1m;
            section.MarginPercent = 20m;
            draft.AddGroupFromSection(1);
            draft.Groups[0].Rows[0].W1 = 1000; draft.Groups[0].Rows[0].H1 = 1000;
            draft.Groups[0].Rows[0].Qty = 1;
            draft.Recalculate();

            ProfitLossResult loss = draft.ProfitLoss;
            Check("the P&L costed something", loss.TotalCost > 0m, loss.TotalCost.ToString());
            Check("glass and production costs are separated",
                loss.TotalGlassCost > 0m && loss.TotalProductionCost > 0m);
            Check("a glass cost row exists",
                loss.Rows.Any(row => row.Kind == ProfitLossRowKind.Glass));
            Eq("net revenue is the total less VAT", draft.Summary.SubtotalBeforeVat, loss.NetExVat);
            Check("profit is net revenue less cost",
                Math.Abs(loss.Profit - (loss.NetExVat - loss.TotalCost)) < 0.02m,
                loss.Profit.ToString());
            Check("margin is measurable when there is revenue",
                loss.MarginPercent.HasValue);
            Check("the costing basis is stated", loss.BasisNote.Length > 0);
            Check("the pricing inputs carry a fingerprint", loss.PricingInputsFingerprint.Length > 0);

            draft.ProfitLossSettings.WastagePercent = 25m;
            draft.Recalculate();
            Check("the wastage allowance is carried into the result",
                draft.ProfitLoss.WastagePercent == 25m);

            // The provenance rule: changing the SELECTED section must not re-cost
            // an existing group.
            decimal before = draft.ProfitLoss.TotalCost;
            PricingSection second = PricingEstimator.NewSection(GlassBuilds.Sgu, 2);
            second.Outer.BasePricePerSqm = 999m;
            second.Outer.Thickness = "10";
            draft.PricingSections.Add(second);
            draft.SelectedSectionNumber = 2;
            draft.Recalculate();
            Check("selecting another section does not re-cost an existing group",
                draft.ProfitLoss.TotalCost == before,
                before + " → " + draft.ProfitLoss.TotalCost);
        }

    }
}
