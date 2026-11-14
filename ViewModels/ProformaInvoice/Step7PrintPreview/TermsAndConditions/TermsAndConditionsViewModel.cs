using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.TermsAndConditions;
using ProGlassAutomation.ViewModels;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using ProGlassAutomation.Services.Localization;

using ProGlassAutomation.Services.Dialogs;
using Microsoft.Extensions.DependencyInjection;
namespace ProGlassAutomation.ViewModels.ProformaInvoice.Step7PrintPreview.TermsAndConditions
{
    /// <summary>
    /// Manages the Terms &amp; Conditions section shown on the printable invoice.
    /// Ships with 6 default UAE glass industry clauses.
    ///
    /// BUG-69 [HIGH]: RemovePaymentSubLineCommand now accepts a Tuple
    ///                (TermClause, string) so the exact clause is known.
    ///                Previously loop-matched by string, which removed the
    ///                wrong line when two clauses had identical sub-line text.
    /// BUG-70 [MEDIUM]: ResetToDefaultsCommand shows YES/NO confirmation
    ///                  before overwriting user's custom clauses.
    /// BUG-71 [LOW]: New-line placeholder extracted to constant for
    ///               future localization.
    /// </summary>
    public class TermsAndConditionsViewModel : ViewModelBase
    {
        private readonly IDialogService _dialogs = null!;

        // ERR-633EEB hardening: this DI overload used to leave every
        // command and collection NULL (all initialization lived in the
        // parameterless ctor). The container always picks the GREEDIEST
        // resolvable ctor, so DI-built instances were half-constructed.
        // It now chains into the fully-initializing ctor.
        public TermsAndConditionsViewModel(IDialogService? dialogs) : this(dialogs, initialize: true)
        {
        }

        // BUG-71: extracted for future localization
        private const string NEW_SUBLINE_PLACEHOLDER = "New payment condition...";

        public string Title { get; } = "TERMS & CONDITIONS";
        public string Subtitle { get; } = "Editable clauses printed on the proforma invoice.";

        public ObservableCollection<TermClause> Clauses { get; } = new ObservableCollection<TermClause>();

        /// <summary>Validity dropdown values (2-7 days).</summary>
        public List<int> ValidityDayOptions { get; } = new List<int> { 2, 3, 4, 5, 6, 7 };

        // ── Commands ────────────────────────────────────────────────────────

        public ICommand AddCustomClauseCommand { get; }
        public ICommand RemoveClauseCommand { get; }
        public ICommand ResetToDefaultsCommand { get; }
        public ICommand MoveUpCommand { get; }
        public ICommand MoveDownCommand { get; }

        // Payment Terms sub-line management
        public ICommand AddPaymentSubLineCommand { get; }
        public ICommand RemovePaymentSubLineCommand { get; }

        public TermsAndConditionsViewModel() : this(null, initialize: true)
        {
        }

        private TermsAndConditionsViewModel(IDialogService? dialogs, bool initialize)
        {
            _dialogs = dialogs
                ?? App.ServiceProvider?.GetService<IDialogService>()
                ?? new DialogService();
            _ = initialize; // discriminator only — keeps the two ctors distinct

            LoadDefaults();

            AddCustomClauseCommand = new RelayCommand(_ => AddCustomClause());
            RemoveClauseCommand = new RelayCommand(p => { if (p is TermClause c) RemoveClause(c); });

            // BUG-70: confirmation before destructive reset
            ResetToDefaultsCommand = new RelayCommand(_ =>
            {
                if (Clauses.Count > 0)
                {
                    var result = _dialogs.Show(
                        LocalizationService.Instance.T("Msg.ResetAllTermsToDefaults"),
                        LocalizationService.Instance.T("Msg.CapResetToDefaults"),
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning);

                    if (result != MessageBoxResult.Yes) return;
                }

                LoadDefaults();
            });

            MoveUpCommand = new RelayCommand(p => { if (p is TermClause c) MoveClause(c, -1); });
            MoveDownCommand = new RelayCommand(p => { if (p is TermClause c) MoveClause(c, +1); });

            AddPaymentSubLineCommand = new RelayCommand(p =>
            {
                if (p is TermClause c) c.SubLines.Add(NEW_SUBLINE_PLACEHOLDER);
            });

            // BUG-69 [HIGH]: accept Tuple<TermClause, string> so we know
            // exactly which clause owns the line to be removed.
            // Backward-compat: if a plain string is passed, fall back to
            // the old "first match wins" behavior (deprecated).
            RemovePaymentSubLineCommand = new RelayCommand(RemoveSubLine);
        }

        // ── Public API used by PrintableInvoice preview ────────────────────

        /// <summary>Returns only active clauses in display order.</summary>
        public IEnumerable<TermClause> GetActivePrintableClauses()
            => Clauses.Where(c => c.IsActive).OrderBy(c => c.Order);

        // ── Sub-line removal (BUG-69 fix) ──────────────────────────────────

        /// <summary>
        /// BUG-69 FIX: Removes a sub-line from a SPECIFIC clause instead of
        /// looping through all clauses. XAML passes a Tuple(clause, line)
        /// so we always target the correct one.
        ///
        /// Backward-compat: if XAML still passes only a string, we fall back
        /// to the old "first clause containing this text" behavior with a
        /// diagnostic warning.
        /// </summary>
        private void RemoveSubLine(object p)
        {
            // Preferred path: Tuple carries both clause + line
            if (p is Tuple<TermClause, string> tuple
                && tuple.Item1 is TermClause targetClause
                && tuple.Item2 is string targetLine)
            {
                targetClause.SubLines.Remove(targetLine);
                return;
            }

            // Also support ValueTuple form (some XAML behaviors emit these)
            if (p is ValueTuple<TermClause, string> vtuple)
            {
                vtuple.Item1.SubLines.Remove(vtuple.Item2);
                return;
            }

            // Backward-compat fallback (deprecated) — string only
            if (p is string line)
            {
                AppLogService.Debug(
                    "[TermsAndConditionsVM] RemovePaymentSubLineCommand " +
                    "was called with a plain string. Update XAML to pass " +
                    "Tuple<TermClause,string> for correct removal.");

                foreach (var clause in Clauses)
                {
                    if (clause.SubLines.Contains(line))
                    {
                        clause.SubLines.Remove(line);
                        break;
                    }
                }
            }
        }

        // ── Internal helpers ───────────────────────────────────────────────

        private void LoadDefaults()
        {
            Clauses.Clear();
            foreach (var c in TermsAndConditionsDefaults.BuildDefaults())
                Clauses.Add(c);
            Renumber();
        }

        private void AddCustomClause()
        {
            var next = Clauses.Count + 1;
            Clauses.Add(new TermClause
            {
                Type = TermClauseType.Custom,
                Order = next,
                Title = $"Custom Clause {next}",
                Body = "Enter your custom term text here..."
            });
            Renumber();
        }

        private void RemoveClause(TermClause clause)
        {
            Clauses.Remove(clause);
            Renumber();
        }

        private void MoveClause(TermClause clause, int direction)
        {
            int oldIndex = Clauses.IndexOf(clause);
            if (oldIndex < 0) return;
            int newIndex = oldIndex + direction;
            if (newIndex < 0 || newIndex >= Clauses.Count) return;

            Clauses.Move(oldIndex, newIndex);
            Renumber();
        }

        private void Renumber()
        {
            for (int i = 0; i < Clauses.Count; i++)
                Clauses[i].Order = i + 1;
        }
    }
}