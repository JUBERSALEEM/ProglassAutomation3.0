// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/CurrencyService.cs
// PROJECT: ProGlassAutomation
// MODULE: Global currency (2026-09-01) — LIVE rate conversion
//
// CONTRACT
//   • The company's BOOKS stay in the base currency (AED) — accounting
//     integrity is never risked by display conversion. CurrencyService
//     converts AT DISPLAY / EXPORT / PRINT time only.
//   • Rates come from the internet when available (primary
//     https://open.er-api.com/v6/latest/AED, fallback
//     https://api.frankfurter.app/latest?from=AED — both free, no key).
//     Every successful fetch is cached to
//     %AppData%\ProGlassAutomation\fx-cache.json WITH ITS TIMESTAMP.
//   • ONLINE → live rates (auto-refresh every 4 h; while offline it
//     retries every 2 min so the app comes alive the moment
//     connectivity returns).
//   • OFFLINE (or fetch failure) → the LAST FETCHED cache is used.
//   • First run with no cache → SeededFxRates seed table (approximate,
//     labelled Seed) so the switch still works — the status line tells
//     the user the rate is a seeded approximation until a fetch succeeds.
//   • The UI is NEVER blocked: RefreshAsync is fire-and-forget and
//     Convert() is a pure in-memory lookup.
//
// EXAMPLE:
//   PI amount 1 AED → switch language/currency to India → ≈ ₹22.7 INR at
//   the current internet rate (AED is pegged to USD at 3.6725, so
//   1 AED ≈ 22.7 INR while USD/INR ≈ 83.5); with no internet the last
//   fetched rate (or seeded fallback) is used.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.AI.AIConfig;
using ProGlassAutomation.Services.Logging;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;

namespace ProGlassAutomation.Services.Localization
{
    public sealed class CurrencyService : System.ComponentModel.INotifyPropertyChanged
    {
        public static CurrencyService Instance { get; } = new();

        /// <summary>Where the currency token sits relative to the number.</summary>
        public enum CurrencyLayout { Prefix, Suffix }

        /// <summary>The currency amounts are DISPLAYED in (ISO 4217).</summary>
        public string DisplayCurrency { get; private set; } = "AED";

        /// <summary>Base currency of the books (always AED).</summary>
        public const string BaseCurrency = "AED";

        /// <summary>True when DisplayCurrency == BaseCurrency (no conversion).</summary>
        public bool IsBase => string.Equals(DisplayCurrency, BaseCurrency,
            StringComparison.OrdinalIgnoreCase);

        private DateTime _ratesFetchedUtc;
        private string _rateSource = "seed";

        /// <summary>Timestamp of the last SUCCESSFUL internet fetch.</summary>
        public DateTime RatesFetchedUtc => _ratesFetchedUtc;

        /// <summary>seed | cache | live — shown to the user on the page.</summary>
        public string RateSource => _rateSource;

        public bool HasLiveRates => _rateSource == "live";

        /// <summary>Rate snapshot (1 BASE → X). Replaced atomically on
        /// every fetch/seed so readers never see a half-written table.
        /// Reads are lock-free and always consistent.</summary>
        private Dictionary<string, decimal> _baseToTarget = new(StringComparer.OrdinalIgnoreCase);

        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProGlassAutomation", "fx-cache.json");

        // ── Live-fetch policy ─────────────────────────────────────────────
        // Primary + fallback endpoints (both free, no key, base AED).
        // The LAST-FETCHED cache is the offline answer; the seed table is
        // only ever used before the very first successful fetch.
        private static readonly string[] RateEndpoints =
        [
            "https://open.er-api.com/v6/latest/{BASE}",
            "https://api.frankfurter.app/latest?from={BASE}",
        ];

        /// <summary>Re-fetch while offline so the app comes alive the
        /// moment connectivity returns.</summary>
        private static readonly TimeSpan OfflineRetryInterval = TimeSpan.FromMinutes(2);

        /// <summary>Normal cadence once we have live rates.</summary>
        private static readonly TimeSpan LiveRefreshInterval = TimeSpan.FromHours(4);

        private static readonly object RefreshLock = new();
        private bool _refreshing;
        private Timer? _autoRefreshTimer;

        /// <summary>Raised on the UI thread when rates or currency change.</summary>
        public event Action? RatesChanged;
        public event Action? CurrencyChanged;

        private CurrencyService() { }

        /// <summary>Loads cache + applies the persisted display currency.
        /// Called once at startup — never blocks (cache read only).</summary>
        public void Initialize()
        {
            LoadCache();
            ApplyCurrency(App.Config?.Localization?.CurrencyCode ?? "AED",
                persist: false);

            // One-time geo suggestion (India → INR, China → CNY…):
            // applies ONLY when the user never picked a currency. A
            // manual pick (settings) is persisted and always wins.
            GeoService.Instance.LocationResolved += OnGeoLocation;

            StartAutoRefresh();
        }

        /// <summary>
        /// Background refresh loop: retries every 2 minutes while the
        /// source isn't live (offline/blocked), then settles into a
        /// 4-hour cadence. All callbacks are fire-and-forget and never
        /// throw — the UI is never blocked and offline always shows the
        /// last-fetched cache.
        /// </summary>
        public void StartAutoRefresh()
        {
            if (_autoRefreshTimer != null) return;
            _autoRefreshTimer = new Timer(_ => TickAutoRefresh(), null,
                TimeSpan.FromSeconds(30), OfflineRetryInterval);
        }

        private void TickAutoRefresh()
        {
            bool stale = _rateSource != "live"
                         || DateTime.UtcNow - _ratesFetchedUtc > LiveRefreshInterval;
            if (stale)
                _ = RefreshAsync();
        }

        private void OnGeoLocation(GeoResult geo)
        {
            if (App.Config?.Localization == null) return;
            if (App.Config.Localization.CurrencyCode.Length > 0) return; // user picked
            if (!App.Config.Localization.AutoSuggestByRegion) return;

            var suggested = CurrencyCatalog.Find(geo.CurrencyCode);
            if (suggested != null && !string.Equals(suggested.Code, DisplayCurrency,
                    StringComparison.OrdinalIgnoreCase))
                ApplyCurrency(suggested.Code);
        }

        /// <summary>Refresh from the internet, then notify. Call with
        /// `_ =` (fire-and-forget); never throws — failures fall back to
        /// cache/seed. Re-entrancy safe: concurrent callers coalesce.</summary>
        public async Task RefreshAsync()
        {
            lock (RefreshLock)
            {
                if (_refreshing) return;
                _refreshing = true;
            }
            try
            {
                var rates = await FetchLiveRatesAsync().ConfigureAwait(false);
                if (rates != null && rates.Count > 0)
                {
                    var fresh = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    foreach (var kv in rates)
                        fresh[kv.Key] = kv.Value;
                    fresh[BaseCurrency] = 1m;
                    _baseToTarget = fresh;   // atomic snapshot swap
                    _ratesFetchedUtc = DateTime.UtcNow;
                    _rateSource = "live";
                    WriteCache();
                    NotifyRates();
                }
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Currency refresh failed: {ex.Message}",
                    new LogContext { Module = "Localization", Operation = "FxRefresh" });
            }
            finally
            {
                lock (RefreshLock)
                {
                    _refreshing = false;
                }
            }
        }

        /// <summary>Applies the display currency (pinned or geo-suggested).
        /// No conversion happens here — conversion is per amount.</summary>
        public void ApplyCurrency(string code, bool persist = true)
        {
            var info = CurrencyCatalog.Find(code) ?? CurrencyCatalog.Find(BaseCurrency)!;
            if (string.Equals(DisplayCurrency, info.Code, StringComparison.OrdinalIgnoreCase))
                return;

            DisplayCurrency = info.Code;
            OnPropertyChanged(nameof(DisplayCurrency));
            OnPropertyChanged(nameof(IsBase));
            CurrencyChanged?.Invoke();
            RefreshAmountBindings();

            if (persist && App.Config != null)
            {
                App.Config.Localization.CurrencyCode = info.Code;
                try { ConfigService.Save(App.Config); } catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("if suppressed");
            }
            }
        }

        /// <summary>Convert an amount from the BASE currency into the
        /// DISPLAY currency (pure, in-memory, never throws).</summary>
        public decimal Convert(decimal baseAmount)
        {
            if (IsBase) return baseAmount;
            if (!_baseToTarget.TryGetValue(DisplayCurrency, out var rate))
            {
                if (_baseToTarget.Count == 0) SeedRates();
                if (!_baseToTarget.TryGetValue(DisplayCurrency, out rate))
                    return baseAmount; // unknown currency → identity
            }
            return decimal.Round(baseAmount * rate, 4);
        }

        /// <summary>Format an amount for display in the active display
        /// currency, e.g. "AED 12,500.00", "₹ 26.00", "¥ 1,250".</summary>
        public string Format(decimal baseAmount, bool includeCode = true,
            CurrencyLayout layout = CurrencyLayout.Prefix)
        {
            var info = CurrencyCatalog.Find(DisplayCurrency)
                       ?? CurrencyCatalog.Find(BaseCurrency)!;
            decimal value = Convert(baseAmount);
            string digits = value.ToString($"N{info.Decimals}",
                System.Globalization.CultureInfo.InvariantCulture);

            string symbol = info.Symbol;
            if (includeCode && info.Code == "AED")
                symbol = "AED"; // the books' base reads as code for clarity
            bool prefix = layout == CurrencyLayout.Prefix;
            return (info.SymbolFirst ? prefix : !prefix)
                ? $"{symbol} {digits}"
                : $"{digits} {symbol}";
        }

        /// <summary>The SAME converted amount as <see cref="Format"/> but
        /// with NO currency token at all — just grouped digits ("12,500.00").
        ///
        /// For table cells whose column header already names the currency
        /// ("RATE (AED)", "VALUE (AED)"): repeating "AED" on every row
        /// doubles the text width of the narrowest columns and, on the
        /// Step 7 paper, pushed the value out of its cell. Books are still
        /// AED and the display conversion still applies.</summary>
        public string Digits(decimal baseAmount)
        {
            var info = CurrencyCatalog.Find(DisplayCurrency)
                       ?? CurrencyCatalog.Find(BaseCurrency)!;
            return Convert(baseAmount).ToString($"N{info.Decimals}",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        /// <summary>Rate used for the current display currency (1 BASE = X
        /// display). 0 = no rate available.</summary>
        public decimal CurrentRate()
        {
            if (IsBase) return 1m;
            if (_baseToTarget.Count == 0) SeedRates();
            return _baseToTarget.TryGetValue(DisplayCurrency, out var r) ? r : 0m;
        }

        private void SeedRates()
        {
            var fresh = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
            foreach (var kv in SeededFxRates.Table)
                fresh[kv.Key] = kv.Value;
            fresh[BaseCurrency] = 1m;
            _baseToTarget = fresh;   // atomic snapshot swap
            if (_ratesFetchedUtc == default)
                _rateSource = "seed";
        }

        /// <summary>
        /// Tries every configured endpoint in order (primary
        /// open.er-api, fallback frankfurter/ECB). Returns null only when
        /// ALL endpoints fail — the caller then keeps the last-fetched
        /// cache. Never throws.
        /// </summary>
        private static async Task<Dictionary<string, decimal>?> FetchLiveRatesAsync()
        {
            foreach (string endpoint in RateEndpoints)
            {
                string url = endpoint.Replace("{BASE}", BaseCurrency,
                    StringComparison.Ordinal);
                try
                {
                    using var http = new System.Net.Http.HttpClient
                    {
                        Timeout = TimeSpan.FromSeconds(10)
                    };
                    http.DefaultRequestHeaders.UserAgent.ParseAdd(
                        "ProGlassAutomation/1.0 (FX client)");
                    using var resp = await http.GetAsync(url).ConfigureAwait(false);
                    if (!resp.IsSuccessStatusCode) continue;
                    using var json = await resp.Content.ReadAsStreamAsync()
                        .ConfigureAwait(false);
                    using var doc = await JsonDocument.ParseAsync(json)
                        .ConfigureAwait(false);
                    if (!doc.RootElement.TryGetProperty("rates", out var rates))
                        continue;
                    var map = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                    foreach (var prop in rates.EnumerateObject())
                    {
                        if (prop.Value.ValueKind == JsonValueKind.Number &&
                            prop.Value.TryGetDecimal(out var d) && d > 0)
                            map[prop.Name] = d;
                    }
                    if (map.Count > 0)
                        return map;
                }
                catch
                {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("foreach suppressed");
            }
            }
            return null;
        }

        private void LoadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) { SeedRates(); return; }
                var json = File.ReadAllText(CachePath);
                var cache = JsonSerializer.Deserialize<FxCache>(json);
                if (cache?.Rates == null || cache.Rates.Count == 0) { SeedRates(); return; }

                var fresh = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase);
                foreach (var kv in cache.Rates)
                    fresh[kv.Key] = kv.Value;
                _baseToTarget = fresh;   // atomic snapshot swap
                _ratesFetchedUtc = cache.FetchedUtc;
                _rateSource = cache.FetchedUtc == default ? "seed" : "cache";
            }
            catch
            {
                SeedRates();
            }
        }

        private void WriteCache()
        {
            try
            {
                var dir = Path.GetDirectoryName(CachePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(CachePath, JsonSerializer.Serialize(
                    new FxCache
                    {
                        FetchedUtc = _ratesFetchedUtc,
                        Rates = new Dictionary<string, decimal>(_baseToTarget)
                    },
                    new JsonSerializerOptions { WriteIndented = true }));
            }
            catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("WriteCache suppressed");
            }
        }

        private void NotifyRates()
        {
            if (Application.Current?.Dispatcher != null)
                Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                {
                    OnPropertyChanged(nameof(RatesFetchedUtc));
                    OnPropertyChanged(nameof(RateSource));
                    OnPropertyChanged(nameof(HasLiveRates));
                    RatesChanged?.Invoke();
                    RefreshAmountBindings();
                }));
            else
            {
                RatesChanged?.Invoke();
                RefreshAmountBindings();
            }
        }

        /// <summary>
        /// Re-evaluates every visible binding after a currency switch so
        /// amounts rendered through CurrencyDisplayConverter repaint
        /// IMMEDIATELY (₹ → ¥ → €) without navigating or restarting.
        /// Bounded walk of the active window; failures are swallowed —
        /// the next navigation refreshes anyway.
        /// </summary>
        private static void RefreshAmountBindings()
        {
            try
            {
                if (Application.Current?.MainWindow is not { } root ||
                    root.Content is not System.Windows.UIElement ui) return;
                RefreshElement(ui, 0);
            }
            catch
            {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("RefreshAmountBindings suppressed");
            }
        }

        private static void RefreshElement(System.Windows.DependencyObject element, int depth)
        {
            if (depth > 12) return;

            if (element is System.Windows.Controls.TextBlock tb)
                RefreshProperty(tb, System.Windows.Controls.TextBlock.TextProperty,
                    System.Windows.Controls.TextBlock.TextProperty);
            else if (element is System.Windows.Controls.ContentControl cc)
                RefreshProperty(cc, System.Windows.Controls.ContentControl.ContentProperty,
                    System.Windows.Controls.ContentControl.ContentProperty);
            else if (element is System.Windows.Controls.DataGridCell cell)
                RefreshProperty(cell, System.Windows.Controls.DataGridCell.ContentProperty,
                    System.Windows.Controls.DataGridCell.ContentProperty);

            int count = System.Windows.Media.VisualTreeHelper.GetChildrenCount(element);
            for (int i = 0; i < count; i++)
                RefreshElement(System.Windows.Media.VisualTreeHelper.GetChild(element, i), depth + 1);
        }

        private static void RefreshProperty(
            System.Windows.DependencyObject target,
            System.Windows.DependencyProperty dp,
            System.Windows.DependencyProperty expressionFor)
        {
            var be = System.Windows.Data.BindingOperations
                .GetBindingExpression(target, expressionFor);
            be?.UpdateTarget();
        }

        private sealed class FxCache
        {
            public DateTime FetchedUtc { get; set; }
            public Dictionary<string, decimal> Rates { get; set; } = new();
        }

        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;

        private void OnPropertyChanged(string name) =>
            PropertyChanged?.Invoke(this,
                new System.ComponentModel.PropertyChangedEventArgs(name));

        /// <summary>Last-fetch status line for the settings page (already
        /// localized by the caller).</summary>
        public string StatusText()
        {
            if (_rateSource == "live")
                return $"live · {_ratesFetchedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            if (_rateSource == "cache" && _ratesFetchedUtc != default)
                return $"last fetched · {_ratesFetchedUtc.ToLocalTime():yyyy-MM-dd HH:mm}";
            return "seeded (offline — first online fetch will replace)";
        }
    }

}
