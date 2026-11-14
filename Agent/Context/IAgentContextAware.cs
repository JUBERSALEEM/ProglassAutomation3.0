// ═══════════════════════════════════════════════════════════════════════
// FILE: Agent/Context/IAgentContextAware.cs
// PROJECT: ProGlassAutomation — ProGlass Agent
//
// MASTER PROMPT §14/§15 (context layer, structured not scraped),
//                §4/§51 (reuse, minimal footprint), §45 (no screen scraping)
//
// ─────────────────────────────────────────────────────────────────────
//  WHY AN OPT-IN INTERFACE RATHER THAN 20 EDITED VIEW-MODELS
// ─────────────────────────────────────────────────────────────────────
//  The obvious way to tell the agent WHICH document is open is to add an
//  AiScreenContext.SetDetail(...) call to every module view-model. That is
//  ~20 files touched, 20 chances to break a working module, and 20 places
//  a future developer must remember to update.
//
//  Instead a view-model OPTS IN by implementing this one-property
//  interface. MainViewModel already funnels every navigation through
//  UpdateAiScreenContext(), so it asks the incoming view-model for its
//  detail there — one edit, in code that already runs on every navigation.
//
//  CONSEQUENCES OF THIS SHAPE, ALL DELIBERATE
//    • A module that does not implement it simply reports no detail. No
//      reflection, no property-name guessing, no scraping (§45).
//    • Each module decides EXACTLY what it reveals. The agent cannot pull
//      anything a view-model did not choose to publish.
//    • The contract is a short label, not an object graph. There is no way
//      to hand the agent a live entity through this seam.
//
//  WHAT BELONGS IN THE STRING
//    A short human label that identifies the record: "PI-2026-014",
//    "editing JO-2201". NOT customer names, prices, balances or totals —
//    those are ERP data and must be fetched through an audited read tool
//    with a permission check, never leaked through a context label.
// ═══════════════════════════════════════════════════════════════════════

namespace ProGlassAutomation.Agent.Context
{
    /// <summary>
    /// Implemented by a module view-model that wants the agent to know
    /// which record is on screen. Entirely optional.
    /// </summary>
    public interface IAgentContextAware
    {
        /// <summary>
        /// Short label for the record currently open, e.g. "PI-2026-014".
        /// Return an empty string when nothing specific is open — an empty
        /// string is honest, a stale label is not.
        ///
        /// Must not include customer names, prices or totals. Keep it to an
        /// identifier the user would recognise.
        /// </summary>
        string AgentContextDetail { get; }
    }
}
