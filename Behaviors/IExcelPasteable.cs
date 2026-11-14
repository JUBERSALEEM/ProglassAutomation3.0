// ════════════════════════════════════════════════════════════════════════
// FILE: Behaviors/IExcelPasteable.cs
// PROJECT: ProGlassAutomation
//
// INTERFACE: IExcelPasteable
//
// Implemented by SpecGroup. Called by DataGridNavigationBehavior.
// All methods must be safe to call on the UI thread.
//
// NOTE: This interface was originally declared at the top of
// DataGridNavigationBehavior.cs and was accidentally lost during a
// Phase 11/12 refactor (extract FindReplaceDialog). It is restored here as
// a dedicated file so SpecGroup + DataGridNavigationBehavior can reference
// it (fixes CS0246).
// ════════════════════════════════════════════════════════════════════════

using System.Collections.Generic;

namespace ProGlassAutomation.Behaviors
{
    /// <summary>
    /// Contract for grid view-models that support Excel-style paste,
    /// find/replace and multi-row selection.
    /// </summary>
    public interface IExcelPasteable
    {
        /// <summary>
        /// Pastes clipboard text at cursor position.
        /// startColumnIndex = -1 → no cell focused, show warning, do not paste.
        /// </summary>
        void PasteFromText(
            string clipboardText,
            object startRowContext = null,
            int startColumnIndex = 0);

        /// <summary>
        /// Adds empty rows. Calls RaiseReset() internally.
        /// </summary>
        void AddRows(int count);

        /// <summary>
        /// Removes the specified row.
        /// </summary>
        void RemoveRow(object rowContext);

        /// <summary>
        /// Finds next row containing searchText, starting AFTER fromRowContext.
        /// Returns null if not found.
        /// </summary>
        object FindNext(string searchText, object fromRowContext);

        /// <summary>
        /// Replaces all occurrences across all columns.
        /// Returns count of replacements.
        /// </summary>
        int FindAndReplace(string findText, string replaceText);

        /// <summary>
        /// Single-column fill down — copies columnIndex value from row above
        /// targetRowContext into targetRowContext.
        /// Used when 0 or 1 row is selected.
        /// </summary>
        void FillDown(object targetRowContext, int columnIndex);

        // ── Multi-row selection support ──────────────────────────────────

        /// <summary>Clears IsSelected on all rows.</summary>
        void ClearRowSelection();

        /// <summary>Sets IsSelected=true on all rows.</summary>
        void SelectAllRows();

        /// <summary>Sets IsSelected on a specific row.</summary>
        void SetRowSelection(object rowContext, bool isSelected);

        /// <summary>
        /// Selects range from anchorRow to targetRow (inclusive).
        /// Clears selection outside this range.
        /// </summary>
        void SelectRowRange(object anchorRow, object targetRow);

        /// <summary>
        /// Returns currently selected rows in original collection order.
        /// </summary>
        List<object> GetSelectedRows();

        /// <summary>
        /// Multi-row fill down. Source row's writable columns (GlassRef,
        /// W1, H1, W2, H2, Qty) are copied to ALL target rows.
        /// Rate column excluded — auto-calculated by row logic.
        /// </summary>
        void FillDownAcrossRows(object sourceRow, List<object> targetRows);
    }
}
