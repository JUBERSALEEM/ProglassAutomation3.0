// ═══════════════════════════════════════════════════════════════════════
//  FILE: Services/ProformaInvoice/SpecificationsRepository/SpecCatalogNotifier.cs
//  PROJECT: ProGlassAutomation
//  CREATED: 2026-09-02
//
//  WHY THIS EXISTS
//  ---------------------------------------------------------------------
//  Saving a specification in Step 4 wrote it to specifications.db, but
//  nothing told Step 5 about it. SpecGroup.LoadSpecsAsync() ran in exactly
//  ONE place -- the SelectedGlassType setter -- so a spec you had just
//  created did not appear in the Step 5 dropdown until you switched the
//  glass type away and back, or reopened the invoice.
//
//  That is the "no delay, no switch, no refresh -- auto sync" gap: the
//  data was correct on disk and stale on screen.
//
//  This is a tiny process-wide announcement channel. The repository raises
//  Changed after every successful write; every live SpecGroup listens and
//  reloads its own dropdown. One writer, many independent listeners, no
//  coupling between Step 4 and Step 5 -- which matters because the
//  Specifications Center will become a THIRD writer and must trigger the
//  same refresh without either step knowing it exists.
//
//  THREADING
//  Writes happen on a background thread (async repository calls) while
//  listeners touch ObservableCollections bound to the UI. Each listener is
//  therefore responsible for marshalling to the dispatcher; SpecGroup does
//  this. The event itself is raised on whatever thread completed the save.
//
//  LIFETIME
//  Subscribers MUST unsubscribe when disposed, otherwise this static event
//  roots them and every closed invoice leaks its SpecGroups. SpecGroup
//  unsubscribes in Dispose().
// ═══════════════════════════════════════════════════════════════════════

using System;

namespace ProGlassAutomation.Services.ProformaInvoice.SpecificationsRepository
{
    /// <summary>
    /// Process-wide "the saved-specification catalogue changed" signal.
    /// Raised by the repository after any insert, update or delete.
    /// </summary>
    public static class SpecCatalogNotifier
    {
        /// <summary>
        /// Fired after the specification catalogue has been modified.
        /// May be raised on a background thread — marshal before touching UI.
        /// </summary>
        public static event EventHandler? Changed;

        /// <summary>
        /// Announces a catalogue change. Never throws: a listener that blows
        /// up must not roll back or mask a database write that already
        /// succeeded, so failures are logged and swallowed.
        /// </summary>
        public static void NotifyChanged()
        {
            try
            {
                Changed?.Invoke(null, EventArgs.Empty);
            }
            catch (Exception ex)
            {
                Services.Logging.AppLogService.Debug(
                    $"[SpecCatalogNotifier] listener threw: {ex.Message}");
            }
        }
    }
}
