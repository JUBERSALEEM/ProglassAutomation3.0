using System;
using ProGlassAutomation.ViewModels;

namespace ProGlassAutomation.Models.ProformaInvoice.Step6OtherCharges.Wastages
{
    /// <summary>
    /// One selectable physical glass pane in Step 6 wastage. SideLabel is a
    /// stable persistence identity (for example "Outer (W1/H1)") and must not
    /// be repurposed for presentation; PaneDisplayLabel is the concise
    /// operator-facing side + colour label.
    /// </summary>
    public class SpecGroupColorSide : ViewModelBase
    {
        private int _specNumber;
        public int SpecNumber
        {
            get => _specNumber;
            set
            {
                if (_specNumber == value) return;
                _specNumber = value;
                OnPropertyChanged();
                RaiseDisplayLabelsChanged();
            }
        }

        private string _specName = "";
        public string SpecName
        {
            get => _specName;
            set
            {
                string next = value ?? "";
                if (_specName == next) return;
                _specName = next;
                OnPropertyChanged();
                RaiseDisplayLabelsChanged();
            }
        }

        // Stable source key: "Outer (W1/H1)", "Inner (W2/H2)",
        // "Lam Outer (W2/H2)", or "Lam Inner (W2/H2)".
        private string _sideLabel = "";
        public string SideLabel
        {
            get => _sideLabel;
            set
            {
                string next = value ?? "";
                if (_sideLabel == next) return;
                _sideLabel = next;
                OnPropertyChanged();
                RaiseDisplayLabelsChanged();
            }
        }

        private string _colorName = "";
        public string ColorName
        {
            get => _colorName;
            set
            {
                string next = value ?? "";
                if (_colorName == next) return;
                _colorName = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(HasColorIdentity));
                OnPropertyChanged(nameof(MatchesRowColor));
                OnPropertyChanged(nameof(ConflictsRowColor));
                RaiseDisplayLabelsChanged();
            }
        }

        /// <summary>
        /// The colour of the wastage row that owns this source. The section
        /// view-model pushes it down so the picker can flag panes that match
        /// or contradict the row colour BEFORE the operator charges the
        /// wrong glass.
        /// </summary>
        private string _rowColorName = "";
        public string RowColorName
        {
            get => _rowColorName;
            set
            {
                string next = value ?? "";
                if (_rowColorName == next) return;
                _rowColorName = next;
                OnPropertyChanged();
                OnPropertyChanged(nameof(MatchesRowColor));
                OnPropertyChanged(nameof(ConflictsRowColor));
            }
        }

        /// <summary>True when a real colour was detected for this pane.</summary>
        public bool HasColorIdentity => !string.IsNullOrWhiteSpace(ColorName);

        /// <summary>This pane's colour matches the owning row's colour.</summary>
        public bool MatchesRowColor =>
            !string.IsNullOrWhiteSpace(RowColorName)
            && !string.IsNullOrWhiteSpace(ColorName)
            && string.Equals(ColorName.Trim(), RowColorName.Trim(),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// This pane HAS a detected colour and it contradicts the owning
        /// row's colour — the classic wrong-charge trap when a bill carries
        /// two specs in different colours.
        /// </summary>
        public bool ConflictsRowColor =>
            !string.IsNullOrWhiteSpace(RowColorName)
            && !string.IsNullOrWhiteSpace(ColorName)
            && !string.Equals(ColorName.Trim(), RowColorName.Trim(),
                StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// True on the first source of a specification inside the picker so
        /// the view can render a per-spec group header ("SPEC 1", "SPEC 2")
        /// above its pane entries.
        /// </summary>
        private bool _isFirstOfSpec;
        public bool IsFirstOfSpec
        {
            get => _isFirstOfSpec;
            set
            {
                if (_isFirstOfSpec == value) return;
                _isFirstOfSpec = value;
                OnPropertyChanged();
            }
        }

        private string _specHeaderText = "";
        public string SpecHeaderText
        {
            get => _specHeaderText;
            set
            {
                string next = value ?? "";
                if (_specHeaderText == next) return;
                _specHeaderText = next;
                OnPropertyChanged();
            }
        }

        private int _sheetWidthMm;
        public int SheetWidthMm
        {
            get => _sheetWidthMm;
            set
            {
                if (_sheetWidthMm == value) return;
                _sheetWidthMm = value;
                OnPropertyChanged();
            }
        }

        private int _sheetHeightMm;
        public int SheetHeightMm
        {
            get => _sheetHeightMm;
            set
            {
                if (_sheetHeightMm == value) return;
                _sheetHeightMm = value;
                OnPropertyChanged();
            }
        }

        private decimal _pricePerSqm;
        public decimal PricePerSqm
        {
            get => _pricePerSqm;
            set
            {
                if (_pricePerSqm == value) return;
                _pricePerSqm = value;
                OnPropertyChanged();
            }
        }

        private decimal _sqmValue;
        public decimal SqmValue
        {
            get => _sqmValue;
            set { if (_sqmValue == value) return; _sqmValue = value; OnPropertyChanged(); }
        }

        private bool _isChecked;
        public bool IsChecked
        {
            get => _isChecked;
            set { if (_isChecked == value) return; _isChecked = value; OnPropertyChanged(); }
        }

        /// <summary>
        /// Semantic pane name without its persisted coordinate key.
        /// Examples: "Outer", "Inner", "Lam Outer", "Lam Inner".
        /// </summary>
        public string SemanticSideLabel
        {
            get
            {
                if (SideLabel.IndexOf("Lam Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Lam Inner";
                if (SideLabel.IndexOf("Lam Outer", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Lam Outer";
                if (SideLabel.IndexOf("Inner", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "Inner";
                return "Outer";
            }
        }

        /// <summary>
        /// Concise, colour-bearing pane identity shown first in the source
        /// picker: "Outer (HD Grey)" / "Inner (Clear)".
        /// </summary>
        public string PaneDisplayLabel => string.IsNullOrWhiteSpace(ColorName)
            ? SemanticSideLabel
            : $"{SemanticSideLabel} ({ColorName.Trim()})";

        /// <summary>
        /// Primary visible picker label. The colour is intentionally adjacent
        /// to the side instead of appended after a long spec name, where it
        /// was routinely clipped by the popup width.
        /// </summary>
        public string SourceDisplayLabel => $"Spec {SpecNumber} · {PaneDisplayLabel}";

        /// <summary>
        /// Full accessibility/tooltip text. The canonical SideLabel stays in
        /// the detail so operators can still see W1/H1 versus W2/H2 without
        /// changing the persisted source key.
        /// </summary>
        public string DisplayLabel => string.IsNullOrWhiteSpace(SpecName)
            ? $"{SourceDisplayLabel} · {SideLabel}"
            : $"{SourceDisplayLabel} · {SideLabel} — {SpecName}";

        /// <summary>Compact summary used by WastageRow after selection.</summary>
        public string ShortLabel => SourceDisplayLabel;

        private void RaiseDisplayLabelsChanged()
        {
            OnPropertyChanged(nameof(SemanticSideLabel));
            OnPropertyChanged(nameof(PaneDisplayLabel));
            OnPropertyChanged(nameof(SourceDisplayLabel));
            OnPropertyChanged(nameof(DisplayLabel));
            OnPropertyChanged(nameof(ShortLabel));
        }
    }
}
