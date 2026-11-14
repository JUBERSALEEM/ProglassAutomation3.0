// ═══════════════════════════════════════════════════════════════════════
// FILE: step6.ProformaInvoice/step6.c.Presentation/step6.c.8.ProformaDiscountOption.cs
// MODULE: step6 — the discount-method list the summary stage binds.
//
// One entry: the engine's value, and the words the operator reads. It lives in
// its own file because it is a screen vocabulary, not part of the draft — the
// document stores `DiscountType` and never this class, so nothing here reaches
// the stored JSON.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Models.Proforma;

namespace ProGlassAutomation.ViewModels.Proforma
{
    /// <summary>A choice in the discount-method list: engine value plus its wording.</summary>
    public sealed class DiscountOption
    {
        /// <summary>What the pricing engine stores.</summary>
        public DiscountType Value { get; init; }

        /// <summary>What the operator reads in the list.</summary>
        public string Label { get; init; } = "";

        /// <summary>So a list that forgets an ItemTemplate still reads as words.</summary>
        public override string ToString() => Label;
    }
}
