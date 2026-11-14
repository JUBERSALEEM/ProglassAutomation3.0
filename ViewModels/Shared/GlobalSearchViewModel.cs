// ═══════════════════════════════════════════════════════════════════════
// FILE: ViewModels/Shared/GlobalSearchViewModel.cs
// PROJECT: ProGlassAutomation
// MODULE: Shared — top-bar global search (PHASE E)
//
// WHAT IT IS
//   One search box in the title bar that reaches across every module:
//   sheets, proformas, job orders, deliveries, tax invoices, companies
//   and clients. Picking a result navigates to the owning module.
//
// DESIGN RULES
//   • READ-ONLY. It queries the existing repositories through their
//     public GetAll/Search APIs and never writes anything.
//   • Minimum 2 characters — a one-letter query would scan every table
//     and return noise.
//   • Results capped at MaxResults (25) so a query like "a" cannot
//     freeze the UI building thousands of rows.
//   • Matching is OrdinalIgnoreCase Contains across the fields a user
//     would actually type (ids, customer, project, colour...).
//   • Navigation is INJECTED (Action<string>) rather than referencing
//     MainViewModel, so this VM stays testable and dependency-free.
//   • SearchAsync never throws: a module whose repository is unavailable
//     is skipped, the rest still return.
// ═══════════════════════════════════════════════════════════════════════
using ProGlassAutomation.Commands;
using ProGlassAutomation.Services.Logging;
using ProGlassAutomation.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Threading;

using ProGlassAutomation.Services.DeliveryNote.Repository;
using ProGlassAutomation.Services.ProformaInvoice.ProformaInvoiceRepository;
using ProGlassAutomation.Services.JobOrder.Repository;
using ProGlassAutomation.Services.MasterData.Repository;
using ProGlassAutomation.Services.TaxInvoice.Repository;
using Microsoft.Extensions.DependencyInjection;
namespace ProGlassAutomation.ViewModels.Shared
{
    public class GlobalSearchViewModel : ViewModelBase
    {
        /// <summary>Below this length the search is not run at all.</summary>
        public const int MinSearchLength = 2;

        /// <summary>Hard cap on rendered results.</summary>
        public const int MaxResults = 25;

        /// <summary>
        /// PERF-13: idle time allowed between the last keystroke and the
        /// actual query. Previously EVERY keystroke fired the full 7-module
        /// search immediately; fast typists queued overlapping queries that
        /// could land out of order and throw stale results onto the screen.
        /// </summary>
        private const int DebounceMs = 250;

        // Module filter labels — also the values shown in the ComboBox.
        public const string ModuleAll = "All Modules";
        public const string ModuleSheetStore = "Sheet Store";
        public const string ModuleProforma = "Proforma Invoice";
        public const string ModuleJobOrders = "Job Orders";
        public const string ModuleDeliveries = "Deliveries";
        public const string ModuleTaxInvoices = "Tax Invoices";
        public const string ModuleCompanies = "Companies";
        public const string ModuleClients = "Clients";

        /// <summary>Injected navigation callback (MainViewModel.Navigate).</summary>
        private readonly Action<string>? _navigate;

        /// <summary>Debounce timer — one query burst per typing pause.</summary>
        private readonly DispatcherTimer _debounceTimer;

        /// <summary>
        /// Search revision counter. Every keystroke bumps it; a search only
        /// applies its results if it is still the newest request when it
        /// finishes. Prevents out-of-order stale results.
        /// </summary>
        private int _searchGeneration;

        /// <summary>
        /// Set when a debounce fires while a search is still in flight —
        /// the freshly-typed term is re-searched as soon as the current
        /// search completes instead of being silently dropped.
        /// </summary>
        private bool _rerunOnIdle;

        /// <summary>Filter options for the module ComboBox.</summary>
        public ObservableCollection<string> ModuleOptions { get; } = new()
        {
            ModuleAll,
            ModuleSheetStore,
            ModuleProforma,
            ModuleJobOrders,
            ModuleDeliveries,
            ModuleTaxInvoices,
            ModuleCompanies,
            ModuleClients
        };

        private string _selectedModule = ModuleAll;
        /// <summary>Active module filter.</summary>
        public string SelectedModule
        {
            get => _selectedModule;
            set
            {
                if (_selectedModule == value) return;
                _selectedModule = value ?? ModuleAll;
                OnPropertyChanged();
                AsyncUtility.SafeFireAndForget(FireSearchAsync, "GlobalSearch");
            }
        }

        private string _searchText = "";
        /// <summary>Query text; searches run at MinSearchLength or more.</summary>
        public string SearchText
        {
            get => _searchText;
            set
            {
                if (_searchText == value) return;
                _searchText = value ?? "";
                OnPropertyChanged();
                AsyncUtility.SafeFireAndForget(FireSearchAsync, "GlobalSearch");
            }
        }

        /// <summary>Capped result set.</summary>
        public ObservableCollection<GlobalSearchResult> Results { get; } = new();

        private bool _isOpen;
        /// <summary>Drives the results Popup.</summary>
        public bool IsOpen
        {
            get => _isOpen;
            set
            {
                if (_isOpen == value) return;
                _isOpen = value;
                OnPropertyChanged();
            }
        }

        private bool _isBusy;
        public bool IsBusy
        {
            get => _isBusy;
            private set
            {
                if (_isBusy == value) return;
                _isBusy = value;
                OnPropertyChanged();
            }
        }

        /// <summary>True when a completed search produced nothing.</summary>
        public bool HasNoResults =>
            Results.Count == 0 &&
            (SearchText ?? "").Trim().Length >= MinSearchLength;

        private string _statusMessage = "";
        /// <summary>"12 results" / "No matches" / hint text.</summary>
        public string StatusMessage
        {
            get => _statusMessage;
            private set
            {
                if (_statusMessage == value) return;
                _statusMessage = value ?? "";
                OnPropertyChanged();
            }
        }

        private GlobalSearchResult? _selectedResult;
        /// <summary>
        /// Two-way bound to the popup ListBox. Setting it to a real hit
        /// navigates; the ListBox clears it back to null afterwards.
        /// </summary>
        public GlobalSearchResult? SelectedResult
        {
            get => _selectedResult;
            set
            {
                _selectedResult = value;
                OnPropertyChanged();

                if (value != null)
                    OpenResult(value);
            }
        }

        /// <summary>Navigates to the module owning a result.</summary>
        public ICommand SelectResultCommand { get; }

        /// <summary>Clears the query and closes the popup.</summary>
        public ICommand ClearCommand { get; }

                private readonly IDeliveryNoteRepository _deliveryNotes = null!;
        private readonly IProformaInvoiceRepository _invoices = null!;
        private readonly IJobOrderRepository _jobs = null!;
        private readonly IMasterDataRepository _masterData = null!;
        private readonly ITaxInvoiceRepository _taxInvoices = null!;

public GlobalSearchViewModel() : this(
            App.ServiceProvider?.GetService<IDeliveryNoteRepository>()!,
            App.ServiceProvider?.GetService<IProformaInvoiceRepository>()!,
            App.ServiceProvider?.GetService<IJobOrderRepository>()!,
            App.ServiceProvider?.GetService<IMasterDataRepository>()!,
            App.ServiceProvider?.GetService<ITaxInvoiceRepository>()!)
        {
        }

        public GlobalSearchViewModel(
            IDeliveryNoteRepository? deliveryNotes,
            IProformaInvoiceRepository? invoices,
            IJobOrderRepository? jobs,
            IMasterDataRepository? masterData,
            ITaxInvoiceRepository? taxInvoices,
            Action<string>? navigate = null)
        {
            _deliveryNotes = deliveryNotes!;
            _invoices = invoices!;
            _jobs = jobs!;
            _masterData = masterData!;
            _taxInvoices = taxInvoices!;
            _navigate = navigate;

            _debounceTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(DebounceMs)
            };
            _debounceTimer.Tick += OnDebounceTick;

            SelectResultCommand = new RelayCommand(param =>
            {
                if (param is GlobalSearchResult hit) OpenResult(hit);
            });

            ClearCommand = new RelayCommand(_ =>
            {
                SearchText = "";
                IsOpen = false;
            });

            Results.CollectionChanged += (_, __) =>
                OnPropertyChanged(nameof(HasNoResults));
        }

        private void OpenResult(GlobalSearchResult hit)
        {
            IsOpen = false;

            try
            {
                if (!string.IsNullOrWhiteSpace(hit.NavigateTarget))
                    _navigate?.Invoke(hit.NavigateTarget);
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[GlobalSearch] navigate failed: {ExceptionMessages.Deep(ex)}");
            }

            // Reset so re-picking the same row navigates again.
            _selectedResult = null;
            OnPropertyChanged(nameof(SelectedResult));
        }

        /// <summary>
        /// Debounced entry point. Coalesces a typing burst into ONE query
        /// after the user pauses (PERF-13), and invalidates any search that
        /// is still in flight from an older keystroke.
        /// </summary>
        private Task FireSearchAsync()
        {
            _debounceTimer.Stop();

            string term = (SearchText ?? "").Trim();
            if (term.Length < MinSearchLength)
            {
                // Bump the generation so an in-flight search cannot paint
                // results that belong to the previous/cleared text.
                Interlocked.Increment(ref _searchGeneration);
                _rerunOnIdle = false;
                Results.Clear();
                IsOpen = false;
                StatusMessage = $"Type at least {MinSearchLength} characters.";
                OnPropertyChanged(nameof(HasNoResults));
                return Task.CompletedTask;
            }

            _debounceTimer.Start();
            return Task.CompletedTask;
        }

        private void OnDebounceTick(object? sender, EventArgs e)
        {
            _debounceTimer.Stop();

            if (IsBusy)
            {
                // Current search still in flight — remember that a newer
                // term is waiting instead of dropping it (the old code
                // returned here and never searched the new text).
                _rerunOnIdle = true;
                return;
            }

            AsyncUtility.SafeFireAndForget(SearchCoreAsync, "GlobalSearch");
        }

        // ══════════════════════════════════════════════════════════════
        //  SEARCH
        // ══════════════════════════════════════════════════════════════

        /// <summary>
        /// Runs the query across the selected modules (debounced). Never
        /// throws; a module whose repository is unavailable is skipped.
        /// </summary>
        public Task SearchAsync() => FireSearchAsync();

        private async Task SearchCoreAsync()
        {
            string term = (SearchText ?? "").Trim();

            if (term.Length < MinSearchLength)
            {
                Results.Clear();
                IsOpen = false;
                StatusMessage = $"Type at least {MinSearchLength} characters.";
                OnPropertyChanged(nameof(HasNoResults));
                return;
            }

            if (IsBusy)
            {
                _rerunOnIdle = true;
                return;
            }

            // This search may only apply its results if it is still the
            // latest request when it completes.
            int generation = Interlocked.Increment(ref _searchGeneration);
            IsBusy = true;

            try
            {
                var hits = new List<GlobalSearchResult>();

                if (Wants(ModuleSheetStore)) await AddSheetsAsync(hits, term);
                if (Wants(ModuleProforma)) await AddProformasAsync(hits, term);
                if (Wants(ModuleJobOrders)) await AddJobOrdersAsync(hits, term);
                if (Wants(ModuleDeliveries)) await AddDeliveriesAsync(hits, term);
                if (Wants(ModuleTaxInvoices)) await AddTaxInvoicesAsync(hits, term);
                if (Wants(ModuleCompanies)) await AddCompaniesAsync(hits, term);
                if (Wants(ModuleClients)) await AddClientsAsync(hits, term);

                // Stale results (user typed again while this ran) are
                // discarded — the debounce fires a fresh search instead.
                if (generation != Volatile.Read(ref _searchGeneration))
                    return;

                Results.Clear();
                foreach (var hit in hits.Take(MaxResults)) Results.Add(hit);

                StatusMessage = Results.Count == 0
                    ? "No matches."
                    : hits.Count > MaxResults
                        ? $"Showing first {MaxResults} of {hits.Count} matches."
                        : $"{Results.Count} result(s).";

                IsOpen = true;
                OnPropertyChanged(nameof(HasNoResults));
            }
            catch (Exception ex)
            {
                AppLogService.Debug($"[GlobalSearch] search failed: {ExceptionMessages.Deep(ex)}");
                StatusMessage = "Search failed — some modules may be unavailable.";
            }
            finally
            {
                IsBusy = false;

                // A newer keystroke arrived while this search was running —
                // re-run for the latest term instead of dropping it.
                if (Volatile.Read(ref _rerunOnIdle) /* read */)
                {
                    Volatile.Write(ref _rerunOnIdle, false);
                    _debounceTimer.Start();
                }
            }
        }

        private bool Wants(string module) =>
            SelectedModule == ModuleAll || SelectedModule == module;

        /// <summary>Case-insensitive substring test used by every query.</summary>
        private static bool Hit(string? value, string term) =>
            (value ?? "").IndexOf(term, StringComparison.OrdinalIgnoreCase) >= 0;

        // ── Per-module queries ────────────────────────────────────────────

        private async Task AddSheetsAsync(
            List<GlobalSearchResult> hits, string term)
        {
            try
            {
                var repo = new Services.SheetStore.SheetStoreRepository();

                // Deliberately NOT pushed into SQL, unlike the proforma, job
                // order, delivery note and tax invoice modules.
                //
                // GlassSheet is master data — one row per
                // Category+Thickness+Colour+Size+Supplier combination — so the
                // table is bounded by the variety stocked, not by transaction
                // volume. It holds tens to low hundreds of rows, not tens of
                // thousands.
                //
                // It also matches on ThicknessDisplay ("6mm") and SizeDisplay
                // ("1000 × 2000"), both computed from integer columns. Making
                // those searchable in SQL means reproducing that string
                // formatting in the query, and doing it wrong here fails
                // silently — a sheet stops appearing in search with no error.
                // Not worth that risk for a table this size.
                var sheets = await repo.GetAllAsync().ConfigureAwait(true);

                foreach (var s in sheets.Where(s =>
                             Hit(s.Color, term) ||
                             Hit(s.Category, term) ||
                             Hit(s.Supplier, term) ||
                             Hit(s.ThicknessDisplay, term) ||
                             Hit(s.SizeDisplay, term))
                         .Take(MaxResults))
                {
                    hits.Add(new GlobalSearchResult
                    {
                        ModuleLabel = ModuleSheetStore,
                        NavigateTarget = "SheetStore",
                        Glyph = "\uE7B8",
                        Title = $"{s.ThicknessDisplay} {s.Color} · {s.SizeDisplay}",
                        Subtitle = $"{s.Category} · {s.Supplier} · balance {s.Balance}"
                    });
                }
            }
            catch (Exception ex) { Skip(ModuleSheetStore, ex); }
        }

        private async Task AddProformasAsync(
            List<GlobalSearchResult> hits, string term)
        {
            try
            {
                if (_invoices == null) return;

                // PERF: bounded, DB-side search - only the top matches are
                // returned, so the FullSnapshotJson blobs are never loaded.
                var rows = await _invoices
                    .SearchAsync(term, MaxResults).ConfigureAwait(true);

                foreach (var pi in rows)
                {
                    hits.Add(new GlobalSearchResult
                    {
                        ModuleLabel = ModuleProforma,
                        NavigateTarget = "ProformaInvoice",
                        Glyph = "\uE8A5",
                        Title = pi.ProformaId,
                        Subtitle = $"{pi.CustomerName} · {pi.ProjectName} · {pi.Status}"
                    });
                }
            }
            catch (Exception ex) { Skip(ModuleProforma, ex); }
        }

        private async Task AddJobOrdersAsync(
            List<GlobalSearchResult> hits, string term)
        {
            try
            {
                if (_jobs == null) return;

                // PERF: bounded, DB-side search - only the top matches are
                // returned, so the FullSnapshotJson blobs are never loaded.
                var rows = await _jobs
                    .SearchAsync(term, MaxResults).ConfigureAwait(true);

                foreach (var jo in rows)
                {
                    hits.Add(new GlobalSearchResult
                    {
                        ModuleLabel = ModuleJobOrders,
                        NavigateTarget = "JobOrder",
                        Glyph = "\uE8C8",
                        Title = jo.JobOrderId,
                        Subtitle = $"{jo.CustomerName} · {jo.ProjectName} · {jo.Status}"
                    });
                }
            }
            catch (Exception ex) { Skip(ModuleJobOrders, ex); }
        }

        private async Task AddDeliveriesAsync(
            List<GlobalSearchResult> hits, string term)
        {
            try
            {
                if (_deliveryNotes == null) return;

                // PERF: bounded, DB-side search — mirrors what the proforma and
                // job-order modules already did. This used to call GetAllAsync
                // and filter in memory, so every keystroke materialised the
                // whole delivery-note table to return at most 25 rows.
                var rows = await _deliveryNotes
                    .SearchAsync(term, MaxResults).ConfigureAwait(true);

                foreach (var dn in rows)
                {
                    hits.Add(new GlobalSearchResult
                    {
                        ModuleLabel = ModuleDeliveries,
                        NavigateTarget = "DeliveryNote",
                        Glyph = "\uE7F4",
                        Title = dn.DeliveryNoteId,
                        Subtitle = $"{dn.CustomerName} · {dn.ProjectName} · {dn.Status}"
                    });
                }
            }
            catch (Exception ex) { Skip(ModuleDeliveries, ex); }
        }

        private async Task AddTaxInvoicesAsync(
            List<GlobalSearchResult> hits, string term)
        {
            try
            {
                if (_taxInvoices == null) return;

                // PERF: bounded, DB-side search — see AddDeliveriesAsync.
                var rows = await _taxInvoices
                    .SearchAsync(term, MaxResults).ConfigureAwait(true);

                foreach (var ti in rows)
                {
                    hits.Add(new GlobalSearchResult
                    {
                        ModuleLabel = ModuleTaxInvoices,
                        NavigateTarget = "TaxInvoice",
                        Glyph = "\uE7BF",
                        Title = ti.InvoiceNumber,
                        Subtitle = $"{ti.CustomerName} · {ti.ProjectName} · {ti.Status}"
                    });
                }
            }
            catch (Exception ex) { Skip(ModuleTaxInvoices, ex); }
        }

        private async Task AddCompaniesAsync(
            List<GlobalSearchResult> hits, string term)
        {
            try
            {
                if (_masterData == null) return;
                var list = await _masterData.GetAllCompaniesAsync()
                    .ConfigureAwait(true);

                foreach (var c in list.Where(c =>
                             Hit(c.CompanyName, term) ||
                             Hit(c.CompanyTrn, term) ||
                             Hit(c.CompanyLocation, term))
                         .Take(MaxResults))
                {
                    hits.Add(new GlobalSearchResult
                    {
                        ModuleLabel = ModuleCompanies,
                        NavigateTarget = "CompanyCenter",
                        Glyph = "\uE822",
                        Title = c.CompanyName,
                        Subtitle = $"TRN {c.CompanyTrn} · {c.CompanyLocation}"
                    });
                }
            }
            catch (Exception ex) { Skip(ModuleCompanies, ex); }
        }

        private async Task AddClientsAsync(
            List<GlobalSearchResult> hits, string term)
        {
            try
            {
                if (_masterData == null) return;
                var list = await _masterData.GetAllClientsAsync()
                    .ConfigureAwait(true);

                foreach (var c in list.Where(c =>
                             Hit(c.CustomerName, term) ||
                             Hit(c.CustomerTrn, term) ||
                             Hit(c.CustomerReferenceCode, term) ||
                             Hit(c.SalesRepresentative, term))
                         .Take(MaxResults))
                {
                    hits.Add(new GlobalSearchResult
                    {
                        ModuleLabel = ModuleClients,
                        NavigateTarget = "ProformaInvoice",
                        Glyph = "\uE716",
                        Title = c.CustomerName,
                        Subtitle = $"TRN {c.CustomerTrn} · rep {c.SalesRepresentative}"
                    });
                }
            }
            catch (Exception ex) { Skip(ModuleClients, ex); }
        }

        private static void Skip(string module, Exception ex) =>
            AppLogService.Debug($"[GlobalSearch] {module} skipped: {ExceptionMessages.Deep(ex)}");
    }
}
