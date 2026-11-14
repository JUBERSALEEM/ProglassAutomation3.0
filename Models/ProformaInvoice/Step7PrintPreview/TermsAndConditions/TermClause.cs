using ProGlassAutomation.ViewModels;
using System;
using System.Collections.ObjectModel;

namespace ProGlassAutomation.Models.ProformaInvoice.Step7PrintPreview.TermsAndConditions
{
    /// <summary>
    /// Predefined categories of proforma invoice terms.
    /// Custom = user-added free-text clause.
    /// </summary>
    public enum TermClauseType
    {
        PaymentTerms,
        Delivery,
        Warranty,
        Validity,
        Taxes,
        Installation,
        Custom
    }

    /// <summary>
    /// A single clause on the proforma invoice's Terms &amp; Conditions section.
    /// Supports:
    ///   - Single-line body text (most clauses)
    ///   - Multiple sub-lines (Payment Terms with multiple installment lines)
    ///   - Validity days integer picker (2..7)
    ///
    /// BUG-31: ValidityDays no longer overwrites Body when user has
    ///         manually edited it. Tracked via _bodyManuallyEdited flag.
    /// </summary>
    public class TermClause : ViewModelBase
    {
        public Guid Id { get; } = Guid.NewGuid();

        public TermClauseType Type { get; set; } = TermClauseType.Custom;

        private int _order;
        public int Order
        {
            get => _order;
            set
            {
                if (_order == value) return;
                _order = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedNumberedTitle));
            }
        }

        private bool _isActive = true;
        public bool IsActive
        {
            get => _isActive;
            set
            {
                if (_isActive == value) return;
                _isActive = value;
                OnPropertyChanged();
            }
        }

        private string _title = "";
        public string Title
        {
            get => _title;
            set
            {
                if (_title == value) return;
                _title = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(FormattedNumberedTitle));
            }
        }

        // ── BUG-31 tracking ───────────────────────────────────────────────
        // True if the user typed into Body themselves (not auto-generated
        // from ValidityDays). Prevents ValidityDays setter from clobbering
        // the user's custom text.
        private bool _bodyManuallyEdited;

        /// <summary>Main body text of the clause (single line for most types).</summary>
        private string _body = "";
        public string Body
        {
            get => _body;
            set
            {
                if (_body == value) return;
                _body = value;
                _bodyManuallyEdited = true;   // BUG-31 fix
                OnPropertyChanged();
            }
        }

        // ── Multi-line sub-items (used by Payment Terms) ────────────────────

        /// <summary>
        /// Extra bullet lines shown beneath the main body.
        /// Populated for PaymentTerms (advance %, dispatch %, etc.).
        /// </summary>
        public ObservableCollection<string> SubLines { get; set; } = new ObservableCollection<string>();

        public bool HasSubLines => SubLines != null && SubLines.Count > 0;

        // ── Validity days picker (used by Validity only) ────────────────────

        private int _validityDays = 7;
        public int ValidityDays
        {
            get => _validityDays;
            set
            {
                if (_validityDays == value) return;
                _validityDays = value;
                OnPropertyChanged();

                // BUG-31: only auto-update Body when validity clause days
                // change AND the user hasn't manually edited Body yet.
                if (Type == TermClauseType.Validity && !_bodyManuallyEdited)
                {
                    // Set _body directly to avoid marking as manually edited
                    _body = $"This quotation is valid for {value} day{(value == 1 ? "" : "s")} from issue date.";
                    OnPropertyChanged(nameof(Body));
                }
            }
        }

        /// <summary>
        /// BUG-31 helper: Called by factories (like TermsAndConditionsDefaults)
        /// to set the initial Body without marking it as manually edited.
        /// Also useful when programmatically restoring a clause from a snapshot.
        /// </summary>
        public void SetBodyFromDefaults(string body)
        {
            _body = body ?? "";
            _bodyManuallyEdited = false;
            OnPropertyChanged(nameof(Body));
        }

        // ── Display helpers ────────────────────────────────────────────────

        /// <summary>"1. PAYMENT TERMS" — used in preview rendering.</summary>
        public string FormattedNumberedTitle => $"{Order}. {Title?.ToUpper() ?? ""}";
    }
}