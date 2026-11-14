// ═══════════════════════════════════════════════════════════════
// FILE: OptimizationViewModel.Jobs.cs
// MODULE: Optimization — JOBS tab — job picker, Step 4/5 pane breakdown and pane selection (partial of OptimizationViewModel)
// ═══════════════════════════════════════════════════════════════

using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using ProGlassAutomation.Commands;
using ProGlassAutomation.Models.JobOrder.Common.JobOrderStatus;
using ProGlassAutomation.Models.Production;
using ProGlassAutomation.Models.ProformaInvoice.SavedInvoice;
using ProGlassAutomation.Services.JobOrder.Mapper;
using ProGlassAutomation.Services.JobOrder.Repository;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Services.Optimization;
using ProGlassAutomation.Services.Production;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using ProGlassAutomation.Utilities;
using ProGlassAutomation.ViewModels.ProformaInvoice.Step9ProfitLoss;

namespace ProGlassAutomation.ViewModels.Optimization
{
    public partial class OptimizationViewModel : ViewModels.ViewModelBase
    {

        // ── JOBS tab: confirmed Job-Order picker (source lock) ─────────
        // Production runs COMPLETELY from the confirmed PI (Job Order).
        // The picker lists every non-draft, non-void JO; loading one expands
        // its cutting pieces through the production distribution engine so
        // the W2/H2 size semantics (equal-size vs overlap) match the floor.

        public sealed class JobOptionVm : ViewModels.ViewModelBase
        {
            private bool _isSelected;
            public string JobOrderId { get; set; } = string.Empty;
            /// <summary>The source Proforma Invoice this job order was cut from —
            /// shown on the card so the operator always knows which Step 5 is
            /// being imported (a voided PI can never be mistaken for a live one).</summary>
            public string SourceProformaId { get; set; } = string.Empty;

            /// <summary>Display label for the source PI ("from PI-2026-001"),
            /// empty when the job carries no source reference.</summary>
            public string SourceLabel => string.IsNullOrWhiteSpace(SourceProformaId)
                ? string.Empty
                : "from " + SourceProformaId;
            public string CustomerName { get; set; } = string.Empty;
            public string Summary { get; set; } = string.Empty;
            public int PieceRows { get; set; }

            /// <summary>Quick stats shown on the job card: pieces / area / spec count.</summary>
            public string PiecesLabel { get; set; } = "0 pcs";
            public string AreaLabel { get; set; } = "0 m²";
            public string SpecCountLabel { get; set; } = "0 specs";

            /// <summary>Distribution-engine import warnings for this job
            /// ("row N has no quantity — produced as 1 piece", skipped panes,
            /// unparsed spec names…). Set once when the option is built,
            /// before the card is bound, so no change notification needed.</summary>
            public List<string> Warnings { get; } = new();
            public bool HasWarnings => Warnings.Count > 0;
            public string WarningLabel => string.Format(System.Globalization.CultureInfo.InvariantCulture,
                "{0} warning(s)", Warnings.Count);
            public string WarningSummary => Warnings.Count == 0
                ? string.Empty
                : string.Join(" ", Warnings.Distinct());

            /// <summary>Card badge visibility — the app has no
            /// BooleanToVisibility resource, so the VM exposes Visibility
            /// directly (it already references System.Windows).</summary>
            public System.Windows.Visibility WarningVisibility =>
                Warnings.Count > 0 ? System.Windows.Visibility.Visible : System.Windows.Visibility.Collapsed;

            /// <summary>Multi-select: tick any number of jobs and combine
            /// their panes (e.g. two jobs whose low-e outers merge into ONE
            /// cutting group).</summary>
            public bool IsSelected
            {
                get => _isSelected;
                set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
            }

            /// <summary>Step 4/5 spec build-up of this job, each spec expanded
            /// into its physical layer boxes (outer pane → ASP spacer → inner
            /// pane).</summary>
            public List<SpecOptionVm> Specs { get; } = new();

            /// <summary>Stock sheet sizes picked in Step 4 (per spec × pane),
            /// tagged with the pane's glass composition so the STOCKS tab can
            /// route each cutting group to its OWN rack (never the whole store).
            /// Tuple = (W, H, glass composition, provenance).</summary>
            /// <summary>Step 4 stock sheets for this job. PricePerSqm is the
            /// rate Step 4 itself used for that sheet, carried alongside the
            /// size so the REPORT tab can cost the plan without re-deriving a
            /// colour match against the Sheet Store.</summary>
            /// <summary>SpecNumber/Side are the Step 5 pane identity this rack
            /// came from, so Step 6 and Step 9 can join a run back to a spec
            /// pane structurally instead of guessing from a colour name.</summary>
            public List<(int W, int H, string Composition, string Source, decimal PricePerSqm, int SpecNumber, string Side)> Step4Sheets { get; } = new();

            /// <summary>Raw Step 5 grid rows (GlassRef, W1, H1, W2, H2, Qty)
            /// copied from the JO's embedded invoice so CUT LIST can show the
            /// same columns as Step 5 instead of only exploded panes.</summary>
            public List<GlassItemRowSnapshot> Step5Rows { get; } = new();

            /// <summary>
            /// The JO embedded PI spec groups (source of truth for W1/H1/W2/H2/Qty).
            /// Kept so JOBS cutting list can rebuild Pieces from saved ItemRows
            /// instead of only exploded pane layers.
            /// </summary>
            public List<SpecGroupSnapshot> InvoiceSpecGroups { get; } = new();

            /// <summary>
            /// False when Step 4 material is unresolved or invalid.
            /// The job still appears in the picker and may import Step 5
            /// sizes; Run / TryBuildInputs must refuse packing.
            /// </summary>
            public bool CanOptimizeMaterial { get; set; } = true;
            public string MaterialInvalidReason { get; set; } = "";

            /// <summary>
            /// Step 4/5 identity of the first spec group on the embedded PI.
            /// Selecting the job must push these onto the studio (glass type,
            /// spec name, colours, sheet sizes) — not only exploded panes.
            /// </summary>
            public string GlassType { get; set; } = string.Empty;
            public string SpecName { get; set; } = string.Empty;
            public string OuterGlassColor { get; set; } = string.Empty;
            public string InnerGlassColor { get; set; } = string.Empty;
            public string ThirdGlassColor { get; set; } = string.Empty;
            public int OuterSheetWidthMm { get; set; }
            public int OuterSheetHeightMm { get; set; }
            public int InnerSheetWidthMm { get; set; }
            public int InnerSheetHeightMm { get; set; }
            public int ThirdSheetWidthMm { get; set; }
            public int ThirdSheetHeightMm { get; set; }
            public decimal OuterSheetPricePerSqm { get; set; }
            public decimal InnerSheetPricePerSqm { get; set; }

            /// <summary>
            /// False when Distribute produced no selectable pane layers.
            /// The job still lists; Sizes/Cut List stay empty until panes exist.
            /// </summary>
            public bool HasOptimizablePanes { get; set; } = true;

            public int TotalPcs => Specs.Sum(s => s.Layers.Where(l => l.Kind == "Pane").Sum(l => l.TotalPcs));
        }

        /// <summary>
        /// One Step 5 spec group rendered as its build-up strip: the glass
        /// layers the factory actually stacks — outer pane, air spacer /
        /// interlayer, inner pane. Each PANE layer is selectable; spacer and
        /// interlayer boxes are informational (nothing is cut from them).
        /// </summary>
        public sealed class SpecOptionVm
        {
            public string JobOrderId { get; set; } = string.Empty;
            public int SpecGroupNumber { get; set; }
            public string SpecName { get; set; } = string.Empty;
            public string BuildType { get; set; } = string.Empty;
            public List<BuildUpLayerVm> Layers { get; } = new();

            public string SpecLabel => "Spec " + SpecGroupNumber.ToString(CultureInfo.InvariantCulture)
                + " · " + BuildType.ToUpperInvariant();
        }

        /// <summary>One box in a spec's build-up strip: a cut pane, an air
        /// spacer, or an interlayer.</summary>
        public sealed class BuildUpLayerVm : ViewModels.ViewModelBase
        {
            private bool _isSelected;

            /// <summary>"Pane" (selectable), "Spacer" or "Interlayer" (informational).</summary>
            public string Kind { get; set; } = "Pane";

            /// <summary>"Outer" / "Inner" / "Lam Outer" / … for panes.</summary>
            public string Position { get; set; } = string.Empty;

            /// <summary>"Spec 1" — the Step 5 spec group this layer belongs to.</summary>
            public string SpecLabel { get; set; } = string.Empty;

            /// <summary>Glass composition ("6mm clear FT glass") or "12mm ASP".</summary>
            public string Title { get; set; } = string.Empty;

            /// <summary>"OUTER · FT" / "air spacer" / "interlayer (not cut)".</summary>
            public string Sub { get; set; } = string.Empty;

            public string Thickness { get; set; } = string.Empty;
            public int TotalPcs { get; set; }
            public string CountLabel { get; set; } = string.Empty;
            public string AreaLabel { get; set; } = string.Empty;

            public bool IsPane => Kind == "Pane";

            public string JobOrderId { get; set; } = string.Empty;
            public string ProformaId { get; set; } = string.Empty;
            public int SpecGroupNumber { get; set; }
            public string Color { get; set; } = string.Empty;
            public int ThicknessMm { get; set; }
            public string Processing { get; set; } = string.Empty;
            public string MaterialKey { get; set; } = string.Empty;

            public List<(string Glass, string Label, int W, int H, int Qty)> Pieces { get; } = new();

            public bool IsSelected
            {
                get => _isSelected;
                set { if (_isSelected == value) return; _isSelected = value; OnPropertyChanged(); }
            }
        }

        public bool IsHeadlessHost { get; set; }
        public bool HasAvailableJobs => AvailableJobs.Count > 0;
        public void AttachJobOption(JobOptionVm job)
        {
            job.PropertyChanged -= OnJobSelectionChanged;
            job.PropertyChanged += OnJobSelectionChanged;
        }

        public ObservableCollection<JobOptionVm> AvailableJobs { get; } = new();

        /// <summary>Job picker filtered by <see cref="JobSearchText"/> (id / customer).</summary>
        public ObservableCollection<JobOptionVm> FilteredJobs { get; } = new();

        private string _jobSearchText = string.Empty;
        public string JobSearchText
        {
            get => _jobSearchText;
            set
            {
                if (_jobSearchText == value) return;
                _jobSearchText = value;
                OnPropertyChanged();
                RefreshJobsFilter();
            }
        }

        private JobOptionVm? _selectedJob;
        public JobOptionVm? SelectedJob
        {
            get => _selectedJob;
            set
            {
                if (IsDisposed || IsRunning || IsHeadlessHost) return;
                if (_selectedJob != value)
                {
                    _selectedJob = value;
                    OnPropertyChanged();
                }
                // Clicking/keyboard-selecting a job card must behave like ticking
                // it. Keep other ticked jobs: the picker is a combined-job workflow.
                if (value != null && AvailableJobs.Contains(value))
                {
                    AttachJobOption(value);
                    value.IsSelected = true;
                }
            }
        }

        /// <summary>Currently ticked jobs (multi-select — combine panes across jobs).</summary>
        public IEnumerable<JobOptionVm> SelectedJobs => AvailableJobs.Where(j => j.IsSelected);

        /// <summary>Build-up strips of every ticked job's specs (outer → ASP → inner).</summary>
        public ObservableCollection<SpecOptionVm> JobSpecBreakdown { get; } = new();

        private bool _hasSpecBreakdown;
        public bool HasSpecBreakdown { get => _hasSpecBreakdown; private set { _hasSpecBreakdown = value; OnPropertyChanged(); } }

        /// <summary>Flat checklist of every pane across the selected jobs —
        /// checkbox · position · spec · glass composition · pcs · m².</summary>
        public ObservableCollection<BuildUpLayerVm> PaneChecklist { get; } = new();

        private bool _hasPaneChecklist;
        public bool HasPaneChecklist { get => _hasPaneChecklist; private set { _hasPaneChecklist = value; OnPropertyChanged(); } }

        // ── KPI strip (dusk-navy stat cards) ─────────────────────────
        private string _kpiJobs = "0";
        public string KpiJobs { get => _kpiJobs; private set { if (_kpiJobs == value) return; _kpiJobs = value; OnPropertyChanged(); } }

        private string _kpiPieces = "0";
        public string KpiPieces { get => _kpiPieces; private set { if (_kpiPieces == value) return; _kpiPieces = value; OnPropertyChanged(); } }

        private string _kpiGlass = "0 m²";
        public string KpiGlass { get => _kpiGlass; private set { if (_kpiGlass == value) return; _kpiGlass = value; OnPropertyChanged(); } }

        private string _kpiGroups = "0";
        public string KpiGroups
        {
            get => _kpiGroups;
            private set
            {
                if (_kpiGroups == value) return;
                _kpiGroups = value;
                OnPropertyChanged();
                NotifyHeroStatusChanged();
            }
        }

        private int _selectedPaneCount;
        public int SelectedPaneCount
        {
            get => _selectedPaneCount;
            private set
            {
                if (_selectedPaneCount == value) return;
                _selectedPaneCount = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SelectedPaneSummary));
                OnPropertyChanged(nameof(HasSelectedPanes));
                OnPropertyChanged(nameof(AddPanesButtonLabel));
                if (AddSelectedPanesCommand is RelayCommand command) command.RaiseCanExecuteChanged();
            }
        }
        public string SelectedPaneSummary => SelectedPaneCount == 0
            ? "No panes selected"
            : SelectedPaneCount.ToString(CultureInfo.InvariantCulture) + " pane(s) selected";
        public bool HasSelectedPanes => SelectedPaneCount > 0;
        public string AddPanesButtonLabel => SelectedPaneCount == 0
            ? "Add selected panes → cutting list"
            : "Add " + SelectedPaneCount.ToString(CultureInfo.InvariantCulture) + " selected pane(s) → cutting list";

        private string _jobsStatusText = "Loading confirmed job orders…";
        public string JobsStatusText
        {
            get => _jobsStatusText;
            set { _jobsStatusText = value; OnPropertyChanged(); }
        }

        private bool _hasPieces;
        public bool HasPieces
        {
            get => _hasPieces;
            private set { _hasPieces = value; OnPropertyChanged(); OnPropertyChanged(nameof(PiecesEmptyHint)); }
        }

        /// <summary>Shown on JOBS/SIZES tabs while the cutting list is empty.</summary>
        public string PiecesEmptyHint => HasPieces
            ? string.Empty
            : "Select a job, tick its panes, then press Add selected panes. Double-click an imported row to open its sizes.";

        public ICommand RefreshJobsCommand { get; }
        public ICommand AddSelectedPanesCommand { get; }
        public ICommand SelectAllPanesCommand { get; }
        public ICommand ClearPaneSelectionCommand { get; }


        private async void LoadJobsAsync()
        {
            if (IsHeadlessHost || IsDisposed || IsRunning) return;
            JobsStatusText = "Loading confirmed job orders…";
            try
            {
                var repo = App.ServiceProvider?.GetService(typeof(IJobOrderRepository)) as IJobOrderRepository;
                if (repo == null)
                {
                    JobsStatusText = "Job store is not available yet — use Refresh.";
                    return;
                }

                // Source-lock guard: a Job Order converted from a VOIDED
                // Proforma Invoice must never appear in the picker. The JO's
                // own IsVoid flag tracks voiding the JO itself — it says
                // nothing about the PI it was cut from, so we read the PI
                // void flags and exclude any JO whose source PI is voided.
                // This is what stopped a live PI-2026-001 from silently
                // importing the Step 5 of its voided twin PI-2026-002.
                var voidedPis = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    var invoiceRepo = App.ServiceProvider?.GetService(typeof(IProformaInvoiceRepository))
                        as IProformaInvoiceRepository;
                    if (invoiceRepo != null)
                    {
                        var piRows = await invoiceRepo.GetListRowsAsync().ConfigureAwait(false);
                        foreach (var row in piRows)
                        {
                            if (row != null && row.IsVoid && !string.IsNullOrWhiteSpace(row.ProformaId))
                                voidedPis.Add(row.ProformaId.Trim());
                        }
                    }
                }
                catch (Exception ex)
                {
                    // The source-lock must not break the picker if the PI
                    // store is unavailable — degrade to the old behaviour
                    // and say so on the status line.
                    AppLogService.Warning($"[Optimization] could not read voided PI list: {ex.Message}");
                }

                var all = await repo.GetAllAsync().ConfigureAwait(false);
                var options = new List<JobOptionVm>();

                foreach (var jo in all
                    .Where(j => j != null && !j.IsVoid
                        && !JobOrderStatusValues.Is(j.Status, JobOrderStatusValues.Draft)
                        && !voidedPis.Contains(j.SourceProformaId ?? string.Empty))
                    .OrderByDescending(j => j.UpdatedAt))
                {
                    var snapshot = JobOrderMapper.DeserializeSnapshot(jo);
                    var invoice = snapshot?.EmbeddedInvoice;
                    options.Add(BuildJobOption(jo.JobOrderId, jo.SourceProformaId ?? string.Empty,
                        jo.CustomerName, invoice));
                }

                var dispatcher = System.Windows.Application.Current?.Dispatcher;
                if (dispatcher != null)
                {
                    dispatcher.Invoke(() =>
                    {
                        if (IsHeadlessHost || IsDisposed || IsRunning) return;
                        var selectedIds = new HashSet<string>(SelectedJobs.Select(j => j.JobOrderId), StringComparer.OrdinalIgnoreCase);
                        foreach (var old in AvailableJobs)
                            old.PropertyChanged -= OnJobSelectionChanged;

                        AvailableJobs.Clear();
                        foreach (var o in options)
                        {
                            o.IsSelected = selectedIds.Contains(o.JobOrderId);
                            o.PropertyChanged += OnJobSelectionChanged;
                            AvailableJobs.Add(o);
                        }
                        SelectedJob = null;
                        RefreshJobsFilter();
                        if (!_sessionInputsActive) RebuildSelectedJobBreakdown();
                        RefreshKpis();
                        JobsStatusText = options.Count == 0
                            ? "No confirmed job orders yet — confirm a Proforma Invoice to create a Job Order with Step 5 cutting sizes."
                            : string.Format(CultureInfo.InvariantCulture,
                                "{0} confirmed job order(s) — select a job, review its panes, then Add selected panes. Jobs with missing sizes show a warning.", options.Count);
                        // Calibration 152-piece auto-load removed 2026-09-21:
                        // optimization no longer invents data. Pieces/Stocks
                        // are populated only from the selected approved JO(s).
                    });
                }
            }
            catch (Exception ex)
            {
                JobsStatusText = "Job list unavailable: " + ex.Message;
            }
        }

        /// <summary>
        /// The source branch's invoice → production panes → size rows projection.
        /// Shared by the database picker and tests; never synthesizes missing sizes.
        /// </summary>
        internal static JobOptionVm BuildJobOption(string jobOrderId, string sourceProformaId,
            string customerName, ProformaInvoiceSnapshot? invoice)
        {
            var plan = ProductionDistributionEngine.Distribute(jobOrderId, invoice);

            var cut = plan.Items
                .Where(i => i.Stage == ProductionStage.Cutting && i.WidthMm > 0 && i.HeightMm > 0)
                .ToList();

            var opt = new JobOptionVm
            {
                JobOrderId = jobOrderId,
                SourceProformaId = sourceProformaId ?? string.Empty,
                CustomerName = customerName
            };

            // ── Step 4/5 spec build-up (outer → ASP → inner) ─────
            // Each spec group becomes a strip of layer boxes using
            // the SAME pane routing the production floor uses
            // (W1/H1 = outer, W2/H2 = inner, equal-size fallback).
            // Two specs sharing one outer pane (low-e on a clear DGU
            // and on an obscure DGU) expose that pane once per spec —
            // the operator ticks both and the SIZES merge folds them
            // into ONE cutting group by composition.
            var groups = (invoice?.SpecGroups ?? new List<SpecGroupSnapshot>())
                .Where(g => g != null)
                .OrderBy(g => g.GroupNumber)
                .ToList();

            foreach (var group in groups)
            {
                string buildType = ProductionDistributionEngine.ResolveBuildType(group, invoice);
                var occurrences = ProductionDistributionEngine.ResolvePaneOccurrences(buildType, group);
                var spec = new SpecOptionVm
                {
                    JobOrderId = jobOrderId,
                    SpecGroupNumber = group.GroupNumber,
                    SpecName = group.SelectedSpecName ?? string.Empty,
                    BuildType = buildType
                };

                int paneIndex = 0;
                foreach (var occ in occurrences)
                {
                    var paneItems = cut
                        .Where(i => i.SpecGroupNumber == group.GroupNumber
                            && string.Equals(i.Position, occ.Position, StringComparison.OrdinalIgnoreCase))
                        .ToList();
                    if (paneItems.Count == 0) continue;

                    string composition = string.IsNullOrWhiteSpace(occ.Composition)
                        ? group.SelectedSpecName ?? string.Empty
                        : occ.Composition;

                    var layer = new BuildUpLayerVm
                    {
                        Kind = "Pane",
                        JobOrderId = jobOrderId,
                        ProformaId = sourceProformaId ?? string.Empty,
                        SpecGroupNumber = group.GroupNumber,
                        Color = occ.Pane?.Color ?? string.Empty,
                        ThicknessMm = occ.ThicknessMm,
                        Processing = occ.Treatment ?? string.Empty,
                        Position = occ.Position,
                        SpecLabel = "Spec " + group.GroupNumber.ToString(CultureInfo.InvariantCulture),
                        Title = composition,
                        Thickness = occ.ThicknessMm > 0
                            ? occ.ThicknessMm.ToString(CultureInfo.InvariantCulture) + "mm"
                            : ParseThicknessLabel(composition),
                        Sub = occ.Position.ToUpperInvariant()
                            + (string.IsNullOrWhiteSpace(occ.Treatment)
                                ? string.Empty : " · " + occ.Treatment),
                        // Panes are pre-ticked so a job's sizes import
                        // with a single click on "Add selected panes";
                        // the operator can still untick any pane.
                        IsSelected = true
                    };

                    // merge identical sizes; the row refs ride along
                    // as the label so the 2D view and cut plan stay
                    // traceable back to Step 5.
                    foreach (var sizeGroup in paneItems
                        .GroupBy(i => new { i.WidthMm, i.HeightMm })
                        .OrderBy(g => g.Key.WidthMm).ThenBy(g => g.Key.HeightMm))
                    {
                        int qty = sizeGroup.Sum(i => i.Qty);
                        string label = string.Join(", ",
                            sizeGroup.Select(i => i.GlassRef)
                                     .Where(r => !string.IsNullOrWhiteSpace(r))
                                     .Distinct());
                        layer.Pieces.Add((
                            composition,
                            string.IsNullOrWhiteSpace(label) ? "—" : label,
                            sizeGroup.Key.WidthMm, sizeGroup.Key.HeightMm, qty));
                    }

                    layer.TotalPcs = layer.Pieces.Sum(p => p.Qty);
                    double paneArea = layer.Pieces.Sum(p => p.W / 1000.0 * p.H / 1000.0 * p.Qty);
                    layer.AreaLabel = paneArea.ToString("0.00", CultureInfo.InvariantCulture) + " m²";
                    layer.CountLabel = layer.TotalPcs.ToString(CultureInfo.InvariantCulture) + " pcs";

                    spec.Layers.Add(layer);
                    paneIndex++;

                    // Insert the informational spacer/interlayer box
                    // exactly where it sits in the stack (nothing is
                    // cut from it — it shows the build-up only).
                    AddBuildUpInfoBox(spec, buildType, paneIndex, group.SelectedSpecName);
                }

                if (spec.Layers.Count > 0) opt.Specs.Add(spec);

                // Step 4 stock sheets (the rack the job was quoted on),
                // tagged with the pane's glass composition so the STOCKS
                // tab routes each cutting group to its own rack. The
                // composition here is the SAME canonical label used for
                // the cutting list, so group→rack matching is exact.
                if (spec.Layers.Count > 0)
                {
                    string ComposeForSlot(PaneSlot slot)
                    {
                        var o = occurrences.FirstOrDefault(x => x.Slot == slot);
                        if (o == null) return string.Empty;
                        return string.IsNullOrWhiteSpace(o.Composition)
                            ? (group.SelectedSpecName ?? string.Empty)
                            : o.Composition;
                    }

                    if (group.OuterSheetWidthMm > 0 && group.OuterSheetHeightMm > 0)
                        opt.Step4Sheets.Add((group.OuterSheetWidthMm, group.OuterSheetHeightMm,
                            ComposeForSlot(PaneSlot.Pane1),
                            "Spec " + group.GroupNumber.ToString(CultureInfo.InvariantCulture) + " outer",
                            group.OuterSheetPricePerSqm, group.GroupNumber, "Outer"));

                    if (group.InnerSheetWidthMm > 0 && group.InnerSheetHeightMm > 0)
                        opt.Step4Sheets.Add((group.InnerSheetWidthMm, group.InnerSheetHeightMm,
                            ComposeForSlot(PaneSlot.Pane2),
                            "Spec " + group.GroupNumber.ToString(CultureInfo.InvariantCulture) + " inner",
                            group.InnerSheetPricePerSqm, group.GroupNumber, "Inner"));
                    var third = occurrences.FirstOrDefault(o => o.Position == "Lam Inner");
                    if (third != null && group.ThirdSheetWidthMm > 0 && group.ThirdSheetHeightMm > 0)
                        opt.Step4Sheets.Add((group.ThirdSheetWidthMm, group.ThirdSheetHeightMm,
                            third.Composition, "Spec " + group.GroupNumber + " lam inner",
                            group.ThirdSheetPricePerSqm, group.GroupNumber, "LamInner"));
                }
            }

            int pcs = opt.TotalPcs;
            double m2 = opt.Specs.Sum(sp => sp.Layers.Where(l => l.Kind == "Pane")
                .Sum(l => l.Pieces.Sum(x => x.W / 1000.0 * x.H / 1000.0 * x.Qty)));
            // Spec count = the PI's ACTUAL specification groups, not
            // just the groups that produced selectable panes. A Job
            // Order with two specs must read "2 specs" even when one
            // spec has no cutting rows yet (previously the card read
            // "1 spec" because that group contributed no pane layers
            // and never reached opt.Specs).
            int specCount = groups.Count;
            opt.PieceRows = opt.Specs.Sum(sp => sp.Layers.Count(l => l.Kind == "Pane"));
            opt.Summary = string.Format(CultureInfo.InvariantCulture,
                "{0} · {1} pcs · {2:0.0} m² · {3} spec(s)",
                customerName, pcs, m2, specCount);
            opt.PiecesLabel = pcs.ToString(CultureInfo.InvariantCulture) + " pcs";
            opt.AreaLabel = m2.ToString("0.0", CultureInfo.InvariantCulture) + " m²";
            opt.SpecCountLabel = specCount.ToString(CultureInfo.InvariantCulture)
                + (specCount == 1 ? " spec" : " specs");

            opt.Warnings.AddRange(plan.Warnings);
            opt.HasOptimizablePanes = opt.Specs.Any(spec => spec.Layers.Any(layer => layer.IsPane && layer.Pieces.Count > 0));
            if (!opt.HasOptimizablePanes)
                opt.Warnings.Add("No usable Step 5 cutting sizes in the saved job snapshot. Open the source invoice and check its sizes.");
            return opt;
        }

        private void OnJobSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(JobOptionVm.IsSelected)) return;
            _sessionInputsActive = false;
            if (sender is JobOptionVm job && !job.IsSelected && ReferenceEquals(_selectedJob, job))
            {
                _selectedJob = null;
                OnPropertyChanged(nameof(SelectedJob));
            }
            RebuildSelectedJobBreakdown();
        }

        /// <summary>
        /// Inserts the informational spacer / interlayer box at its correct
        /// place in a spec's build-up strip (after the outer pane, and after
        /// the first lam pane on hybrids). Nothing is cut from these boxes.
        /// </summary>
        private static void AddBuildUpInfoBox(SpecOptionVm spec, string buildType, int paneIndex, string specName)
        {
            bool needSpacer = (buildType == "Dgu" && paneIndex == 1)
                || (buildType == "DguLam" && paneIndex == 1);
            bool needInterlayer = (buildType == "Lam" && paneIndex == 1)
                || (buildType == "DguLam" && paneIndex == 2);

            if (needSpacer)
            {
                string mm = PnLSpecParser.Spacer(specName).Groups["mm"].Value;
                spec.Layers.Add(new BuildUpLayerVm
                {
                    Kind = "Spacer",
                    Title = (string.IsNullOrWhiteSpace(mm) ? "" : mm + "mm ") + "ASP",
                    Sub = "air spacer · not cut",
                    Thickness = string.Empty
                });
            }
            else if (needInterlayer)
            {
                var interlayer = PnLSpecParser.Interlayer(specName);
                string mm = interlayer.Success ? interlayer.Groups["mm"].Value : "";
                string type = interlayer.Success ? interlayer.Groups["type"].Value.ToUpperInvariant() : "PVB";
                spec.Layers.Add(new BuildUpLayerVm
                {
                    Kind = "Interlayer",
                    Title = (string.IsNullOrWhiteSpace(mm) ? "" : mm + "mm ") + type,
                    Sub = "interlayer · not cut",
                    Thickness = string.Empty
                });
            }
        }

        /// <summary>
        /// Rebuilds <see cref="FilteredJobs"/> from <see cref="AvailableJobs"/>
        /// using <see cref="JobSearchText"/> (job id / customer / summary).
        /// Runs on the UI thread — callers marshal beforehand.
        /// </summary>
        private void RefreshJobsFilter()
        {
            string q = (_jobSearchText ?? string.Empty).Trim();
            IEnumerable<JobOptionVm> items = string.IsNullOrEmpty(q)
                ? AvailableJobs
                : AvailableJobs.Where(j =>
                    (j.JobOrderId ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (j.CustomerName ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (j.Summary ?? string.Empty).Contains(q, StringComparison.OrdinalIgnoreCase));

            FilteredJobs.Clear();
            foreach (var item in items) FilteredJobs.Add(item);
        }

        // ── composition helpers ─────────────────────────────────────────

        /// <summary>Leading thickness of a glass composition, e.g. "6mm clear …" → "6mm".</summary>
        private static string ParseThicknessLabel(string? composition)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                composition ?? string.Empty, @"^\s*(\d+(?:\.\d+)?)\s*mm");
            return m.Success ? m.Groups[1].Value + "mm" : string.Empty;
        }

        /// <summary>Leading thickness in mm (0 when unreadable).</summary>
        private static int ParseThicknessMm(string? composition)
        {
            var m = System.Text.RegularExpressions.Regex.Match(
                composition ?? string.Empty, @"^\s*(\d+(?:\.\d+)?)\s*mm");
            return m.Success && int.TryParse(m.Groups[1].Value, NumberStyles.Integer,
                CultureInfo.InvariantCulture, out int t) ? t : 0;
        }

        /// <summary>Rebuilds the spec build-up strips from every ticked job.</summary>
        private void RebuildSelectedJobBreakdown()
        {
            if (IsHeadlessHost || IsDisposed || IsRunning) return;
            foreach (var spec in JobSpecBreakdown)
                foreach (var layer in spec.Layers)
                    layer.PropertyChanged -= OnLayerSelectionChanged;

            var selectedIds = new HashSet<string>(SelectedJobs.Select(j => j.JobOrderId), StringComparer.OrdinalIgnoreCase);
            foreach (var row in Pieces.Where(p => !string.IsNullOrWhiteSpace(p.JobOrderId)
                         && !selectedIds.Contains(p.JobOrderId)).ToList()) Pieces.Remove(row);
            var documents = SelectedJobs.Select(j => j.SourceProformaId).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            DocumentKey = documents.Count == 1 ? documents[0] : string.Empty;
            JobSpecBreakdown.Clear();
            foreach (var job in AvailableJobs.Where(j => j.IsSelected))
            {
                foreach (var spec in job.Specs)
                {
                    foreach (var layer in spec.Layers)
                        layer.PropertyChanged += OnLayerSelectionChanged;
                    JobSpecBreakdown.Add(spec);
                }
            }

            HasSpecBreakdown = JobSpecBreakdown.Count > 0;

            PaneChecklist.Clear();
            foreach (var spec in JobSpecBreakdown)
                foreach (var layer in spec.Layers)
                    if (layer.IsPane) PaneChecklist.Add(layer);
            HasPaneChecklist = PaneChecklist.Count > 0;

            RefreshPaneSelectionCount();

            // STOCKS follows the selection: the pool becomes exactly the
            // ticked jobs' Step 4 sheet sizes (per pane × composition).
            SyncStocksFromSelectedJobs();
            RefreshDerived();
        }

        private void OnLayerSelectionChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(BuildUpLayerVm.IsSelected)) return;
            RefreshPaneSelectionCount();
        }

        private void RefreshPaneSelectionCount()
        {
            SelectedPaneCount = JobSpecBreakdown
                .SelectMany(s => s.Layers)
                .Count(l => l.IsPane && l.IsSelected);
        }

        private void SetAllPanesSelected(bool selected)
        {
            foreach (var layer in JobSpecBreakdown.SelectMany(s => s.Layers).Where(l => l.IsPane))
                layer.IsSelected = selected;
            RefreshPaneSelectionCount();
        }

        /// <summary>
        /// Adds the checked pane layers' cutting pieces to the run. Each piece
        /// keeps its per-pane glass composition (so SIZES merges matching
        /// compositions from multiple specs into single cutting groups) and
        /// its pane position for the 2D view.
        /// </summary>
        private void AddSelectedPanes()
        {
            if (IsRunning || IsDisposed || IsHeadlessHost) return;
            ImportPaneLayers(PaneChecklist.Where(layer => layer.IsSelected).ToList());
        }

        private void ImportPaneLayers(IReadOnlyList<BuildUpLayerVm> selected)
        {
            if (IsRunning || IsDisposed || IsHeadlessHost) return;
            var imported = new HashSet<(string Job, int Spec, string Position, string Glass, string Label, string W, string H)>(
                Pieces.Select(p => (p.JobOrderId, p.SpecGroupNumber, p.Position, p.GlassRef, p.PieceLabel, p.WidthText, p.HeightText)));
            if (selected.Count == 0)
            {
                Blocked("Tick at least one pane box (outer / inner) to add.", "Nothing to add");
                return;
            }

            int addedPieces = 0;
            int skipped = 0;
            foreach (var layer in selected)
            {
                foreach (var (glass, label, w, h, qty) in layer.Pieces)
                {
                    string wText = w.ToString(CultureInfo.InvariantCulture);
                    string hText = h.ToString(CultureInfo.InvariantCulture);

                    // Source row identity includes job/spec/pane: two jobs can
                    // legitimately have the same label and dimensions.
                    if (!imported.Add((layer.JobOrderId, layer.SpecGroupNumber, layer.Position,
                        glass, label, wText, hText))) { skipped++; continue; }

                    var row = NewPieceRow(glass, label, wText, hText,
                        qty.ToString(CultureInfo.InvariantCulture));
                    row.Position = layer.Position;
                    row.JobOrderId = layer.JobOrderId;
                    row.ProformaId = layer.ProformaId;
                    row.SpecGroupNumber = layer.SpecGroupNumber;
                    row.Color = layer.Color;
                    row.ThicknessMm = layer.ThicknessMm;
                    row.Processing = layer.Processing;
                    row.MaterialKey = layer.MaterialKey;
                    Pieces.Add(row);
                    addedPieces += qty;
                }
            }

            // An earlier one-glass filter must not hide newly imported panes.
            _selectedCuttingGroup = AllCuttingGroups;
            RefreshDerived();

            var compositions = selected
                .Select(l => l.Title)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
            RunMessage = string.Format(CultureInfo.InvariantCulture,
                "Added {0} pane layer(s) → {1} pieces in {2} cutting group(s): {3}.{4}",
                selected.Count, addedPieces, compositions.Count,
                string.Join(" · ", compositions),
                skipped > 0
                    ? " " + skipped.ToString(CultureInfo.InvariantCulture) + " already-imported row(s) skipped."
                    : string.Empty);
        }

        /// <summary>
        /// Calibration job removed 2026-09-21 — see <see cref="LoadReferenceJob"/>
        /// in OptimizationViewModel.Parts.cs. Former 152-piece benchmark
        /// (76 low-e outer / 64 clear inner / 12 ramly) no longer injected.
        /// All cutting data now comes exclusively from approved Proforma
        /// Invoices via their Job Orders.
        /// </summary>
    }
}
