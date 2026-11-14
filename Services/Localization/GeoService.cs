// ═══════════════════════════════════════════════════════════════════════
// FILE: Services/Localization/GeoService.cs
// PROJECT: ProGlassAutomation
// MODULE: Global locale (2026-09-01) — country detection
//
// Detects where the app is running so the system can SUGGEST language,
// currency and the phone dial code. Three-tier resolution, never blocks
// the UI and never throws:
//
//   1. Cached geo result (%AppData%\ProGlassAutomation\geo-cache.json);
//   2. Online country lookup (https://ipwho.is — free, no key);
//   3. OS fallback: CultureInfo region (a Windows region setting is a
//      strong signal on machines without internet).
//
// The lookup is a SYNCHRONOUS read of the cache + a fire-and-forget
// refresh; the completion callback is dispatched to the UI thread and
// raises LocationResolved so language suggestion and phone defaults can
// apply once the answer arrives.
// ═══════════════════════════════════════════════════════════════════════

using ProGlassAutomation.Services.AI.AIConfig;
using ProGlassAutomation.Services.Logging;
using System;
using System.Globalization;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;

namespace ProGlassAutomation.Services.Localization
{
    public sealed class GeoResult
    {
        public string CountryIso { get; set; } = "";
        public string CountryName { get; set; } = "";
        public string DialCode { get; set; } = "";
        public string CurrencyCode { get; set; } = "";
        public DateTime ResolvedUtc { get; set; }
        public string Source { get; set; } = ""; // ip | culture | cache
    }

    public sealed class GeoService
    {
        public static GeoService Instance { get; } = new();

        /// <summary>Resolved after InitAsync — null until then.</summary>
        public GeoResult? Current { get; private set; }

        /// <summary>Raised on the UI thread when the country is known.</summary>
        public event Action<GeoResult>? LocationResolved;

        private static readonly string CachePath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "ProGlassAutomation", "geo-cache.json");

        private GeoService() { }

        /// <summary>Reads the cache synchronously (fast), then refreshes
        /// online in the background. Safe to call at startup.</summary>
        public void Initialize()
        {
            Current = ReadCache();
            ApplyToConfig();
        }

        /// <summary>Online lookup + cache write. Fire-and-forget with
        /// `_ =` at the call site; never throws.</summary>
        public async Task RefreshAsync()
        {
            try
            {
                var result = await QueryIpWhoIsAsync().ConfigureAwait(false);
                if (string.IsNullOrEmpty(result.CountryIso))
                {
                    result = OsRegionFallback();
                }
                else
                {
                    result.Source = "ip";
                    WriteCache(result);
                }
                if (string.IsNullOrEmpty(result.CurrencyCode))
                    result.CurrencyCode =
                        CountryDialCode.Find(result.CountryIso) is { } c
                            ? CurrencyCatalog.CurrencyForRegion(c.Iso)
                            : "";
                if (string.IsNullOrEmpty(result.DialCode))
                    result.DialCode =
                        CountryDialCode.Find(result.CountryIso)?.DialCode ?? "";

                Current = result;
                ApplyToConfig();

                if (Application.Current?.Dispatcher != null)
                    Application.Current.Dispatcher.BeginInvoke(
                        new Action(() => LocationResolved?.Invoke(result)));
            }
            catch (Exception ex)
            {
                AppLogService.Warning($"Geo refresh failed: {ex.Message}",
                    new LogContext { Module = "Localization", Operation = "GeoRefresh" });
                Current = OsRegionFallback();
            }
        }

        private void ApplyToConfig()
        {
            if (Current == null || App.Config == null) return;
            var loc = App.Config.Localization;
            if (string.IsNullOrEmpty(loc.LastKnownCountryIso))
                loc.LastKnownCountryIso = Current.CountryIso;
            if (string.IsNullOrEmpty(loc.PhoneCountryIso))
                loc.PhoneCountryIso = Current.CountryIso;
            if (string.IsNullOrEmpty(loc.CurrencyCode) && !string.IsNullOrEmpty(Current.CurrencyCode))
                loc.CurrencyCode = Current.CurrencyCode;
            try { ConfigService.Save(App.Config); } catch {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("ApplyToConfig suppressed");
            }
        }

        private static async Task<GeoResult> QueryIpWhoIsAsync()
        {
            try
            {
                using var http = new System.Net.Http.HttpClient
                {
                    Timeout = TimeSpan.FromSeconds(8)
                };
                using var resp = await http.GetAsync("https://ipwho.is/")
                    .ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return new GeoResult();
                using var json = await resp.Content.ReadAsStreamAsync()
                    .ConfigureAwait(false);
                using var doc = await JsonDocument.ParseAsync(json).ConfigureAwait(false);
                var root = doc.RootElement;
                bool success = root.TryGetProperty("success", out var ok) &&
                               ok.ValueKind == JsonValueKind.True && ok.GetBoolean();
                if (!success || !root.TryGetProperty("country_code", out var cc))
                    return new GeoResult();
                string iso = cc.GetString() ?? "";
                return new GeoResult
                {
                    CountryIso = iso.ToUpperInvariant(),
                    CountryName = root.TryGetProperty("country", out var n)
                        ? n.GetString() ?? "" : "",
                    DialCode = root.TryGetProperty("calling_code", out var dial)
                        ? "+" + (dial.GetString() ?? "").Replace(" ", "") : "",
                    CurrencyCode = root.TryGetProperty("currency", out var cur)
                        ? cur.GetString() ?? "" : "",
                    ResolvedUtc = DateTime.UtcNow,
                    Source = "ip"
                };
            }
            catch
            {
                return new GeoResult();
            }
        }

        private static GeoResult OsRegionFallback()
        {
            try
            {
                var region = new RegionInfo(CultureInfo.CurrentCulture.Name.Length > 0
                    ? CultureInfo.CurrentCulture.Name
                    : "AE");
                var country = CountryDialCode.Find(region.TwoLetterISORegionName);
                return new GeoResult
                {
                    CountryIso = region.TwoLetterISORegionName.ToUpperInvariant(),
                    CountryName = region.EnglishName,
                    DialCode = country?.DialCode ?? "",
                    CurrencyCode = CurrencyCatalog.CurrencyForRegion(
                        region.TwoLetterISORegionName),
                    ResolvedUtc = DateTime.UtcNow,
                    Source = "culture"
                };
            }
            catch
            {
                return new GeoResult { CountryIso = "AE", Source = "culture" };
            }
        }

        private static GeoResult? ReadCache()
        {
            try
            {
                if (!File.Exists(CachePath)) return null;
                var json = File.ReadAllText(CachePath);
                return JsonSerializer.Deserialize<GeoResult>(json);
            }
            catch
            {
                return null;
            }
        }

        private static void WriteCache(GeoResult result)
        {
            try
            {
                var dir = Path.GetDirectoryName(CachePath)!;
                Directory.CreateDirectory(dir);
                File.WriteAllText(CachePath,
                    JsonSerializer.Serialize(result,
                        new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                ProGlassAutomation.Services.Logging.AppLogService.Debug("WriteCache suppressed");
            }
        }
    }
}
