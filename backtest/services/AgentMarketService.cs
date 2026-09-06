using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Xml.Linq;

namespace backtest.Services
{
    public static class AgentMarketService
    {
        private const string BrowserUa = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36";
        private const int DefaultCalendarResults = 60;
        private const int MaxCalendarResults = 150;
        private const int DefaultSearchResults = 8;
        private const int MaxSearchResults = 25;
        private const int DefaultNewsResults = 10;
        private const int MaxNewsResults = 30;
        private const int DefaultPageChars = 8000;
        private const int MaxPageChars = 30000;
        private const int MaxOverviewSymbols = 14;
        private static readonly TimeSpan CacheQuotes = TimeSpan.FromMinutes(5);
        private static readonly TimeSpan CacheCalendar = TimeSpan.FromMinutes(20);
        private static readonly TimeSpan CacheNews = TimeSpan.FromMinutes(15);
        private static readonly TimeSpan CacheSearch = TimeSpan.FromMinutes(10);
        private static readonly TimeSpan CacheFedWatch = TimeSpan.FromMinutes(30);

        private static readonly HttpClient _http = CreateHttpClient();
        private sealed class CacheEntry { public DateTime ExpiresUtc; public string Value; }
        private static readonly ConcurrentDictionary<string, CacheEntry> _cache = new ConcurrentDictionary<string, CacheEntry>(StringComparer.Ordinal);
        private sealed class FetchResult { public int Status; public string Content; }

        private static HttpClient CreateHttpClient()
        {
            var handler = new HttpClientHandler
            {
                AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate,
                UseCookies = true, AllowAutoRedirect = true
            };
            ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;
            var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(20) };
            client.DefaultRequestHeaders.Clear();
            client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", BrowserUa);
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,application/json;q=0.8,*/*;q=0.7");
            client.DefaultRequestHeaders.TryAddWithoutValidation("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
            return client;
        }

        private static string GetCached(string key)
        {
            if (_cache.TryGetValue(key, out var entry) && entry.ExpiresUtc > DateTime.UtcNow)
                return entry.Value;
            return null;
        }

        private static void SetCache(string key, string value, TimeSpan ttl)
        {
            _cache[key] = new CacheEntry { ExpiresUtc = DateTime.UtcNow + ttl, Value = value };
        }

        private static async Task<FetchResult> FetchAsync(string url, int timeoutSeconds = 20, IDictionary<string, string> headers = null, int maxBytes = 500000)
        {
            try
            {
                using (var request = new HttpRequestMessage(HttpMethod.Get, url))
                {
                    if (headers != null)
                        foreach (var kv in headers) request.Headers.TryAddWithoutValidation(kv.Key, kv.Value);
                    using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds)))
                    using (var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false))
                    {
                        var result = new FetchResult { Status = (int)response.StatusCode, Content = string.Empty };
                        if (!response.IsSuccessStatusCode) return result;
                        using (var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false))
                        using (var memory = new MemoryStream())
                        {
                            var buffer = new byte[8192]; long total = 0; int read;
                            while ((read = await stream.ReadAsync(buffer, 0, buffer.Length, cts.Token).ConfigureAwait(false)) > 0)
                            {
                                if (total + read > maxBytes) { total = maxBytes; break; }
                                memory.Write(buffer, 0, read); total += read;
                            }
                            result.Content = Encoding.UTF8.GetString(memory.ToArray());
                        }
                        return result;
                    }
                }
            }
            catch { return null; }
        }

        private static string DecodeXmlText(string content)
        {
            if (string.IsNullOrEmpty(content) || content.Length < 64) return content;
            var match = Regex.Match(content, "encoding=[\"']([A-Za-z0-9\\-]+)[\"']", RegexOptions.IgnoreCase);
            if (!match.Success) return content;
            string enc = match.Groups[1].Value.ToLowerInvariant();
            if (enc.Contains("utf")) return content;
            int cp;
            switch (enc)
            {
                case "windows-1252": case "iso-8859-1": case "latin1": cp = 1252; break;
                case "windows-1251": cp = 1251; break;
                default: try { cp = Encoding.GetEncoding(enc).CodePage; } catch { return content; } break;
            }
            try { var bytes = Encoding.UTF8.GetBytes(content); return Encoding.GetEncoding(cp).GetString(bytes); }
            catch { return content; }
        }

        // =========================================================================
        // HELPERS D'ARGUMENTS
        // =========================================================================
        private static string GetString(JsonElement arguments, string property, string fallback = "")
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(property, out var value))
                return fallback;
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return (value.GetString() ?? string.Empty).Trim();
                case JsonValueKind.Number: return value.GetDouble().ToString(CultureInfo.InvariantCulture);
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return fallback;
            }
        }
        private static int GetInt(JsonElement arguments, string property, int fallback)
        {
            if (arguments.ValueKind != JsonValueKind.Object || !arguments.TryGetProperty(property, out var value))
                return fallback;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number: return (int)value.GetDouble();
                case JsonValueKind.True: return 1; case JsonValueKind.False: return 0;
                case JsonValueKind.String:
                    var text = (value.GetString() ?? string.Empty).Trim();
                    return int.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
                default: return fallback;
            }
        }
        private static DateTime ParseDate(string raw, DateTime defaultValue)
        {
            if (string.IsNullOrWhiteSpace(raw)) return defaultValue;
            raw = raw.Trim();
            if (DateTime.TryParseExact(raw, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var iso))
                return DateTime.SpecifyKind(iso, DateTimeKind.Utc);
            if (DateTime.TryParseExact(raw, "dd/MM/yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var eu))
                return DateTime.SpecifyKind(eu, DateTimeKind.Utc);
            if (DateTime.TryParse(raw, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var any))
                return DateTime.SpecifyKind(any, DateTimeKind.Utc);
            return defaultValue;
        }
        private static string Cap(string value, int maxChars)
        {
            if (value == null) return string.Empty;
            if (value.Length <= maxChars) return value;
            return value.Substring(0, maxChars) + "\u2026";
        }

        // =========================================================================
        // 1) CALENDRIER ECONOMIQUE
        // =========================================================================
        private sealed class CalEvent
        {
            public DateTime SortKey; public string Date; public string Time; public string Currency;
            public string Title; public string Impact; public string Forecast; public string Previous; public string Actual; public string Source;
            public object ToJson() => new { date = Date, time = string.IsNullOrEmpty(Time) ? null : Time, currency = string.IsNullOrEmpty(Currency) ? null : Currency, title = Title, impact = Impact, forecast = string.IsNullOrEmpty(Forecast) ? null : Forecast, previous = string.IsNullOrEmpty(Previous) ? null : Previous, actual = string.IsNullOrEmpty(Actual) ? null : Actual, source = Source };
        }

        private static List<CalEvent> ApplyCalendarFilters(List<CalEvent> events, DateTime from, DateTime to, string currency, string importance)
        {
            var result = events.Where(e => e.SortKey >= from.Date && e.SortKey < to.Date.AddDays(1)).ToList();
            if (currency != "ALL")
                result = result.Where(e => string.Equals(e.Currency, currency, StringComparison.OrdinalIgnoreCase) || (e.Currency == "ALL" && currency != "ALL")).ToList();
            if (importance != "ALL")
                result = result.Where(e => MatchesImportance(e.Impact, importance)).ToList();
            return result;
        }

        private static bool MatchesImportance(string impact, string importance)
        {
            if (string.IsNullOrWhiteSpace(impact)) return false;
            string i = impact.Trim().ToLowerInvariant();
            int numeric; bool isNumeric = int.TryParse(i, out numeric);
            switch (importance)
            {
                case "high": if (isNumeric) return numeric >= 2; return i == "high" || i.Contains("haut") || i.Contains("fort");
                case "medium": if (isNumeric) return numeric == 1 || numeric == 2; return i == "medium" || i.Contains("moyen") || i.Contains("moy");
                case "low": if (isNumeric) return numeric == 1 || numeric <= 0; return i == "low" || i == "1" || i.Contains("bas");
                case "holiday": return i.Contains("holiday") || i.Contains("feri");
                default: return true;
            }
        }

        public static async Task<AiToolResult> GetEconomicCalendar(JsonElement arguments)
        {
            DateTime from = ParseDate(GetString(arguments, "from"), DateTime.UtcNow.Date);
            DateTime to = ParseDate(GetString(arguments, "to"), from.AddDays(7));
            if (to < from) to = from.AddDays(7);
            if ((to - from).TotalDays > 120) to = from.AddDays(120);
            string currency = GetString(arguments, "currency", "all").Trim().ToUpperInvariant();
            string importance = GetString(arguments, "importance", "all").Trim().ToLowerInvariant();
            string source = GetString(arguments, "source", "auto").Trim().ToLowerInvariant();
            int maxResults = Math.Max(1, Math.Min(GetInt(arguments, "max_results", DefaultCalendarResults), MaxCalendarResults));
            string rangeKey = $"cal_{from:yyyyMMdd}_{to:yyyyMMdd}_{currency}_{importance}_{source}";
            string cached = GetCached(rangeKey);
            if (cached != null) return AiToolResult.Success(cached);

            var events = new List<CalEvent>();
            var sourcesUsed = new List<string>();
            bool tryFF = source == "auto" || source == "forexfactory";
            bool tryTV = source == "auto" || source == "tradingview" || source == "investing";

            if (tryFF) { var ff = await FetchForexFactoryEventsAsync(from, to); if (ff != null) { events.AddRange(ff); sourcesUsed.Add("ForexFactory"); } else sourcesUsed.Add("ForexFactory(non disponible)"); }
            if (tryTV) { var tv = await FetchTradingViewCalendarAsync(from, to); if (tv != null) { events.AddRange(tv); sourcesUsed.Add("TradingView"); } else sourcesUsed.Add("TradingView(non disponible)"); }
            if (source == "investing") { var inv = await FetchInvestingCalendarAsync(from, to); if (inv != null) events.AddRange(inv); else return AiToolResult.Error("investing.com n'a pas repondu. Utilisez source=auto."); }
            if (source == "dukascopy") { var dk = await FetchDukascopyCalendarAsync(from, to); if (dk != null) events.AddRange(dk); else return AiToolResult.Error("Dukascopy n'a pas repondu. Utilisez source=forexfactory ou source=tradingview."); }

            if (events.Count == 0) return AiToolResult.Error("AUCUNE DONNEE CALENDAIRE DISPONIBLE. Sources tentees : " + string.Join(", ", sourcesUsed) + ". CONSIGNE : Je ne dois PAS inventer de dates, chiffres, previsions ou titres. Je propose a l'utilisateur de reessayer ou d'utiliser les sites officiels (bls.gov, ecb.europa.eu).");

            var deduped = new List<CalEvent>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var e in events.OrderBy(e => e.SortKey).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase))
            { string key = e.Date + "|" + e.Title + "|" + e.Currency; if (seen.Add(key)) deduped.Add(e); }

            var filtered = ApplyCalendarFilters(deduped, from, to, currency, importance);
            if (filtered.Count == 0) return AiToolResult.Error("Aucun evenement " + currency + " (importance " + importance + ") entre " + from.ToString("yyyy-MM-dd") + " et " + to.ToString("yyyy-MM-dd") + " dans les donnees recuperees (" + deduped.Count + " evenements bruts). Sources : " + string.Join(", ", sourcesUsed) + ".");

            var sorted = filtered.OrderBy(e => e.SortKey).ThenBy(e => e.Title, StringComparer.OrdinalIgnoreCase).Take(maxResults).Select(e => e.ToJson()).ToList();
            string serialized = JsonSerializer.Serialize(new { sources = string.Join(" + ", sourcesUsed), from = from.ToString("yyyy-MM-dd"), to = to.ToString("yyyy-MM-dd"), timezone_display = "heures : ForexFactory en fuseaux locaux, TradingView en UTC", count = sorted.Count, total_matching_filter = filtered.Count, total_raw_events = deduped.Count, events = sorted });
            SetCache(rangeKey, serialized, CacheCalendar);
            return AiToolResult.Success(serialized);
        }

        private static async Task<List<CalEvent>> FetchForexFactoryEventsAsync(DateTime from, DateTime to)
        {
            const string baseUrl = "https://nfs.faireconomy.media/";
            var feeds = new[] { "ff_calendar_yesterday.xml", "ff_calendar_today.xml", "ff_calendar_tomorrow.xml", "ff_calendar_thisweek.xml", "ff_calendar_nextweek.xml" };
            var seen = new HashSet<string>(StringComparer.Ordinal);
            var events = new List<CalEvent>();

            foreach (var feed in feeds)
            {
                string xml = GetCached("ff_" + feed);
                if (xml == null)
                {
                    var response = await FetchAsync(baseUrl + feed, 15, null, 300000);
                    if (response == null || response.Status != 200 || string.IsNullOrEmpty(response.Content)) continue;
                    xml = DecodeXmlText(response.Content);
                    SetCache("ff_" + feed, xml, CacheCalendar);
                }

                try
                {
                    var doc = XDocument.Parse(xml);
                    foreach (var item in doc.Root?.Elements("event") ?? Enumerable.Empty<XElement>())
                    {
                        var title = (string)item.Element("title") ?? "";
                        var country = ((string)item.Element("country") ?? "ALL").Trim().ToUpperInvariant();
                        DateTime day;
                        if (!DateTime.TryParseExact(((string)item.Element("date") ?? "").Trim(), "MM-dd-yyyy", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out day))
                            continue;
                        var timeRaw = (string)item.Element("time") ?? "";
                        var sortKey = day.Date.Add(ParseFfTime(timeRaw));
                        string key = sortKey.ToString("yyyyMMddHHmm") + "|" + title + "|" + country;
                        if (!seen.Add(key)) continue;
                        events.Add(new CalEvent
                        {
                            SortKey = sortKey, Date = day.ToString("yyyy-MM-dd"),
                            Time = string.IsNullOrEmpty(timeRaw) ? null : timeRaw.Trim(),
                            Currency = country, Title = title.Trim(),
                            Impact = string.IsNullOrEmpty((string)item.Element("impact")) ? "Low" : ((string)item.Element("impact")).Trim(),
                            Forecast = ((string)item.Element("forecast") ?? "").Trim(),
                            Previous = ((string)item.Element("previous") ?? "").Trim(),
                            Actual = ((string)item.Element("actual") ?? "").Trim(),
                            Source = "ForexFactory"
                        });
                    }
                }
                catch { }
            }
            return events.Count > 0 ? events : null;
        }

        private static TimeSpan ParseFfTime(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw)) return new TimeSpan(12, 0, 0);
            raw = raw.Trim().ToLowerInvariant();
            bool pm = raw.EndsWith("pm", StringComparison.Ordinal);
            bool am = raw.EndsWith("am", StringComparison.Ordinal);
            if (pm || am) raw = raw.Substring(0, raw.Length - 2).Trim();
            if (!TimeSpan.TryParse(raw, CultureInfo.InvariantCulture, out var ts)) return new TimeSpan(12, 0, 0);
            if (pm && ts.Hours < 12) ts = ts.Add(new TimeSpan(12, 0, 0));
            if (am && ts.Hours == 12) ts = ts.Subtract(new TimeSpan(12, 0, 0));
            return ts;
        }

        private static async Task<List<CalEvent>> FetchTradingViewCalendarAsync(DateTime from, DateTime to)
        {
            string url = $"https://economic-calendar.tradingview.com/events?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}";
            string cacheKey = $"tv_cal_{from:yyyyMMdd}_{to:yyyyMMdd}";
            string json = GetCached(cacheKey);
            if (json == null)
            {
                var headers = new Dictionary<string, string> { { "Origin", "https://www.tradingview.com" }, { "Referer", "https://www.tradingview.com/" } };
                var response = await FetchAsync(url, 20, headers, 1000000);
                if (response == null || response.Status != 200 || string.IsNullOrEmpty(response.Content)) return null;
                json = response.Content;
                SetCache(cacheKey, json, CacheCalendar);
            }

            var events = new List<CalEvent>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    if (!doc.RootElement.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array) return null;
                    foreach (var item in results.EnumerateArray())
                    {
                        var title = GetString(item, "title");
                        if (string.IsNullOrEmpty(title)) continue;
                        var currency = GetString(item, "currency");
                        if (string.IsNullOrEmpty(currency)) currency = GetString(item, "country");
                        DateTime dt;
                        if (!DateTime.TryParse(GetString(item, "date"), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out dt)) continue;
                        int imp = -1;
                        if (item.TryGetProperty("importance", out var impEl) && impEl.ValueKind == JsonValueKind.Number) imp = impEl.GetInt32();
                        string impact = imp >= 3 ? "High" : imp == 2 ? "Medium" : imp == 1 ? "Low" : title.IndexOf("Holiday", StringComparison.OrdinalIgnoreCase) >= 0 ? "Holiday" : "Low";
                        string key = dt.ToString("yyyyMMddHHmm") + "|" + title + "|" + currency;
                        if (!seen.Add(key)) continue;
                        events.Add(new CalEvent
                        {
                            SortKey = dt, Date = dt.ToString("yyyy-MM-dd"), Time = dt.ToString("HH:mm", CultureInfo.InvariantCulture) + " UTC",
                            Currency = string.IsNullOrEmpty(currency) ? "ALL" : currency.ToUpperInvariant(), Title = title, Impact = impact,
                            Forecast = GetString(item, "forecast"), Previous = GetString(item, "previous"),
                            Actual = GetString(item, "actual") == "null" ? "" : GetString(item, "actual"), Source = "TradingView"
                        });
                    }
                }
            }
            catch { return null; }
            return events.Count > 0 ? events : null;
        }

        private static async Task<List<CalEvent>> FetchInvestingCalendarAsync(DateTime from, DateTime to) { return null; }
        private static async Task<List<CalEvent>> FetchDukascopyCalendarAsync(DateTime from, DateTime to) { return null; }

        // =========================================================================
        // 2) FED WATCH (CME)
        // =========================================================================
        public static async Task<AiToolResult> GetFedWatch(JsonElement arguments)
        {
            string meeting = GetString(arguments, "meeting", "next").Trim().ToLowerInvariant();
            string cached = GetCached("fedwatch_" + meeting);
            if (cached != null) return AiToolResult.Success(cached);
            var cme = await FetchCmeFedWatchAsync();
            if (cme != null) { string s = BuildFedWatchPayload(cme, meeting); SetCache("fedwatch_" + meeting, s, CacheFedWatch); return AiToolResult.Success(s); }
            string fallback = JsonSerializer.Serialize(await BuildFedWatchFallbackAsync());
            SetCache("fedwatch_" + meeting, fallback, CacheFedWatch);
            return AiToolResult.Success(fallback);
        }

        private sealed class CmeFedWatch { public string TradeDate; public List<CmePeriod> Periods = new List<CmePeriod>(); }
        private sealed class CmePeriod { public string Period; public List<KeyValuePair<string, string>> Probabilities = new List<KeyValuePair<string, string>>(); }

        private static async Task<CmeFedWatch> FetchCmeFedWatchAsync()
        {
            string from = DateTime.UtcNow.AddDays(-7).ToString("yyyy-MM-dd");
            string to = DateTime.UtcNow.AddDays(150).ToString("yyyy-MM-dd");
            var headers = new Dictionary<string, string> { { "Accept", "application/json, text/plain, */*" }, { "Referer", "https://www.cmegroup.com/markets/interest-rates/cme-fedwatch-tool.html" }, { "Origin", "https://www.cmegroup.com" } };
            foreach (var url in new[] { $"https://www.cmegroup.com/services/trades/fedwatch?fromDate={from}&toDate={to}", $"https://www.cmegroup.com/services/trades/fedwatch?page=fedWatch&fromDate={from}&toDate={to}" })
            {
                var response = await FetchAsync(url, 20, headers, 2000000);
                if (response != null && response.Status == 200 && !string.IsNullOrWhiteSpace(response.Content))
                    return ParseCmeResponse(response.Content);
            }
            return null;
        }

        private static CmeFedWatch ParseCmeResponse(string json)
        {
            try
            {
                using (var doc = JsonDocument.Parse(json))
                {
                    if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
                    CmeFedWatch best = null;
                    foreach (var entry in doc.RootElement.EnumerateArray())
                    {
                        var cme = new CmeFedWatch { TradeDate = GetString(entry, "tradeDate") };
                        if (entry.TryGetProperty("periods", out var periods) && periods.ValueKind == JsonValueKind.Array)
                            foreach (var p in periods.EnumerateArray())
                            {
                                var gp = new CmePeriod { Period = GetString(p, "period") };
                                ExtractPeriodProbabilities(p, gp.Probabilities);
                                if (gp.Probabilities.Count > 0 || gp.Period.Length > 0) cme.Periods.Add(gp);
                            }
                        if (best == null || string.CompareOrdinal(cme.TradeDate, best.TradeDate) > 0) best = cme;
                    }
                    return best != null && best.Periods.Count > 0 ? best : null;
                }
            }
            catch { return null; }
        }

        private static void ExtractPeriodProbabilities(JsonElement period, List<KeyValuePair<string, string>> probabilities)
        {
            if (period.TryGetProperty("rates", out var rates) && rates.ValueKind == JsonValueKind.Array && period.TryGetProperty("probs", out var probs) && probs.ValueKind == JsonValueKind.Array)
            {
                for (int i = 0; i < rates.GetArrayLength() && i < probs.GetArrayLength(); i++)
                    probabilities.Add(new KeyValuePair<string, string>(GetString(rates[i], ""), GetString(probs[i], "")));
                return;
            }
            if (period.TryGetProperty("probPerGap", out var gaps) && gaps.ValueKind == JsonValueKind.Array)
                foreach (var gap in gaps.EnumerateArray())
                { var r = GetString(gap, "rate"); var p = GetString(gap, "prob"); if (!string.IsNullOrEmpty(r) && !string.IsNullOrEmpty(p)) probabilities.Add(new KeyValuePair<string, string>(r, p)); }
        }

        private sealed class FedMeeting { public string meeting; public List<object> probabilities = new List<object>(); public object most_likely; }

        private static string BuildFedWatchPayload(CmeFedWatch cme, string meeting)
        {
            var all = new List<FedMeeting>();
            foreach (var p in cme.Periods.OrderBy(p => p.Period, StringComparer.OrdinalIgnoreCase))
            {
                double maxProb = -1; string maxRate = null; var probs = new List<object>();
                foreach (var kv in p.Probabilities)
                {
                    double prob; double.TryParse(kv.Value.Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out prob);
                    if (prob > maxProb) { maxProb = prob; maxRate = kv.Key; }
                    probs.Add(new { target_rate = kv.Key, probability = kv.Value.EndsWith("%", StringComparison.Ordinal) ? kv.Value : kv.Value + "%" });
                }
                all.Add(new FedMeeting { meeting = p.Period, probabilities = probs.Take(12).ToList(), most_likely = maxRate != null ? (object)new { target_rate = maxRate, probability = maxProb.ToString("0.0", CultureInfo.InvariantCulture) + "%" } : null });
            }
            string norm = meeting.Replace(" ", "").ToUpperInvariant();
            object selected;
            if (norm == "ALL" || string.IsNullOrEmpty(norm)) selected = all;
            else if (norm == "NEXT") selected = all.Take(1).ToList();
            else { var match = all.FirstOrDefault(m => m.meeting.ToUpperInvariant().Replace("-", "").Contains(norm.Replace("-", "")) || norm.Replace("-", "").Contains(m.meeting.ToUpperInvariant().Replace("-", ""))); selected = match != null ? (object)match : (object)all.Take(1).ToList(); }
            return JsonSerializer.Serialize(new { source = "CME FedWatch Tool", as_of = cme.TradeDate, note = "Probabilites du marche (Fed Funds futures) - CME Group.", meetings = selected });
        }

        private static async Task<object> BuildFedWatchFallbackAsync()
        {
            var fomcDates = new[] { "2026-01-27", "2026-03-17", "2026-04-28", "2026-06-16", "2026-07-28", "2026-09-15", "2026-10-27", "2026-12-08" };
            var meetings = new List<object>(); var upcomingMeetings = new List<object>();
            DateTime now = DateTime.UtcNow;
            foreach (var ds in fomcDates)
            {
                DateTime d; if (!DateTime.TryParse(ds, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out d)) continue;
                bool upcoming = d >= now;
                meetings.Add(new { meeting = d.ToString("MMM-yyyy", CultureInfo.InvariantCulture), start_date = d.ToString("yyyy-MM-dd"), status = upcoming ? "a venir" : "passe" });
                if (upcoming) upcomingMeetings.Add(meetings[meetings.Count - 1]);
            }
            string last = null;
            try { var tv = await FetchTradingViewCalendarAsync(now.AddDays(-90), now.AddDays(7)); var latest = tv?.Where(e => e.SortKey <= now && (e.Title.IndexOf("Rate Decision", StringComparison.OrdinalIgnoreCase) >= 0 || e.Title.IndexOf("Fed", StringComparison.OrdinalIgnoreCase) >= 0)).OrderByDescending(e => e.SortKey).FirstOrDefault(); if (latest != null) last = $"{latest.Title} - {latest.Date} (actual: {(string.IsNullOrEmpty(latest.Actual) ? "n/a" : latest.Actual)}, forecast: {(string.IsNullOrEmpty(latest.Forecast) ? "n/a" : latest.Forecast)})"; } catch { }
            return new { source = "CME FedWatch indisponible depuis ce reseau", note = "Probabilites CME non joignables. Calendrier FOMC officiel 2026 et derniere annonce Fed ci-dessous.", live_probabilities = "non disponibles", last_fed_decision = last, upcoming_meetings = upcomingMeetings };
        }

        // =========================================================================
        // 3) SENTIMENT RETAIL (MYFXBOOK)
        // =========================================================================
        private static Dictionary<string, string> _apiConfig; private static DateTime _apiConfigLoadedAt = DateTime.MinValue;

        private static Dictionary<string, string> GetApiConfig()
        {
            if (_apiConfig != null && (DateTime.UtcNow - _apiConfigLoadedAt).TotalMinutes < 5) return _apiConfig;
            var config = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var path in new[] { Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "DataEdge", "apikeys.json"), Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "apikeys.json") })
            {
                try { if (File.Exists(path)) { string json = File.ReadAllText(path); using (var doc = JsonDocument.Parse(json)) { if (doc.RootElement.ValueKind == JsonValueKind.Object) foreach (var prop in doc.RootElement.EnumerateObject()) config[prop.Name] = prop.Value.ValueKind == JsonValueKind.String ? prop.Value.GetString() : prop.Value.GetRawText(); } } } catch { }
            }
            _apiConfig = config; _apiConfigLoadedAt = DateTime.UtcNow; return config;
        }

        public static async Task<AiToolResult> GetFxbookSentiment(JsonElement arguments)
        {
            string pairsArg = GetString(arguments, "pairs", "all").Trim();
            var wanted = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (pairsArg != "" && !pairsArg.Equals("all", StringComparison.OrdinalIgnoreCase))
                foreach (var token in pairsArg.Split(new[] { ',', ';', ' ', '/' }, StringSplitOptions.RemoveEmptyEntries))
                { string pair = token.Trim().ToUpperInvariant().Replace("/", ""); if (pair.Length == 6 && pair.All(char.IsLetter)) wanted.Add(pair); }

            var config = GetApiConfig(); List<object> outlook = null;
            if (config.ContainsKey("myfxbookEmail") && config.ContainsKey("myfxbookPassword"))
                outlook = await FetchMyFxBookApiOutlookAsync(config["myfxbookEmail"], config["myfxbookPassword"]);
            if (outlook == null || outlook.Count == 0) outlook = await FetchMyFxBookHtmlOutlookAsync();
            if (wanted.Count > 0 && outlook != null) outlook = outlook.Where(o => o is NameValuePair nv && wanted.Contains(nv.name)).Cast<object>().ToList();
            if (outlook == null || outlook.Count == 0)
                return AiToolResult.Error("MyFxBook n'a pas repondu. Pour debloquer l'API officielle, ajoutez myfxbookEmail et myfxbookPassword dans apikeys.json.");
            return AiToolResult.Success(JsonSerializer.Serialize(new { source = "MyFxBook Community Outlook", note = "Positionnement moyen des comptes MyFxBook (sentiment retail, signe contrarien possible).", data = outlook.Take(MaxOverviewSymbols).ToList() }));
        }

        private sealed class NameValuePair { public string name; public string name2; public string long_percent; public string short_percent; }

        private static async Task<string> PostFormAsync(string url, IEnumerable<KeyValuePair<string, string>> form, int timeoutSeconds = 15)
        {
            try { using (var content = new FormUrlEncodedContent(form)) using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(timeoutSeconds))) using (var response = await _http.PostAsync(url, content, cts.Token).ConfigureAwait(false)) { if (!response.IsSuccessStatusCode) return null; return await response.Content.ReadAsStringAsync().ConfigureAwait(false); } }
            catch { return null; }
        }

        private static async Task<List<object>> FetchMyFxBookApiOutlookAsync(string email, string password)
        {
            try
            {
                string loginJson = await PostFormAsync("https://www.myfxbook.com/api/login.json", new[] { new KeyValuePair<string, string>("email", email), new KeyValuePair<string, string>("password", password) });
                string session = null;
                if (!string.IsNullOrEmpty(loginJson)) { using (var doc = JsonDocument.Parse(loginJson)) { if (doc.RootElement.TryGetProperty("session", out var s) && s.ValueKind == JsonValueKind.String) session = s.GetString(); } }
                if (string.IsNullOrEmpty(session)) return null;
                string outlookJson = await PostFormAsync("https://www.myfxbook.com/api/getCommunityOutlook.json", new[] { new KeyValuePair<string, string>("session", session) });
                if (string.IsNullOrEmpty(outlookJson)) return null;
                var result = new List<object>();
                using (var doc = JsonDocument.Parse(outlookJson)) { if (doc.RootElement.TryGetProperty("outlooks", out var outlooks) && outlooks.ValueKind == JsonValueKind.Array) foreach (var item in outlooks.EnumerateArray()) result.Add(new NameValuePair { name = GetString(item, "pair"), long_percent = FormatPercent(GetString(item, "longRatio")), short_percent = FormatPercent(GetString(item, "shortRatio")) }); }
                return result.Count > 0 ? result : null;
            }
            catch { return null; }
        }

        private static string FormatPercent(string raw) { double v; return double.TryParse(raw.Trim().TrimEnd('%'), NumberStyles.Any, CultureInfo.InvariantCulture, out v) ? v.ToString("0.0", CultureInfo.InvariantCulture) + "%" : raw; }

        private static async Task<List<object>> FetchMyFxBookHtmlOutlookAsync()
        {
            const string url = "https://www.myfxbook.com/community/outlook";
            string html = GetCached("mfx_outlook");
            if (html == null) { var response = await FetchAsync(url, 20, null, 1200000); if (response == null || response.Status != 200) return null; html = response.Content; SetCache("mfx_outlook", html, CacheQuotes); }
            var result = new List<object>();
            try
            {
                var rows = Regex.Matches(html, @"<tr[^>]*class\s*=\s*[""'][^""']*outlook[^""']*[""'][^>]*>(.*?)</tr>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                if (rows.Count > 0) { foreach (Match row in rows) { var f = ParseOutlookRow(row.Groups[1].Value); if (f != null) result.Add(f); } }
                if (result.Count == 0)
                {
                    var text = StripHtml(html);
                    var matches = Regex.Matches(text, @"([A-Z]{3}/[A-Z]{3}|[A-Z]{6})\b[^\n]{0,160}(\d+(?:\.\d+)?)\s*%\s*[^\n]{0,40}(\d+(?:\.\d+)?)\s*%");
                    foreach (Match m in matches) { string pair = m.Groups[1].Value.Replace("/", "").ToUpperInvariant(); if (pair.Length != 6 || result.Any(r => r is NameValuePair p && p.name == pair)) continue; result.Add(new NameValuePair { name = pair, long_percent = m.Groups[2].Value + "%", short_percent = m.Groups[3].Value + "%" }); }
                }
            }
            catch { return null; }
            return result.Count > 0 ? result : null;
        }

        private static NameValuePair ParseOutlookRow(string fragment)
        {
            var pair = Regex.Match(fragment, @"([A-Z]{3})(?:/|\s)?([A-Z]{3})\b", RegexOptions.IgnoreCase);
            if (!pair.Success) return null;
            string name = (pair.Groups[1].Value + pair.Groups[2].Value).ToUpperInvariant();
            var percents = Regex.Matches(fragment, @"(\d+(?:\.\d+)?)\s*%", RegexOptions.IgnoreCase);
            var values = percents.Cast<Match>().Take(2).Select(m => m.Groups[1].Value + "%").ToList();
            return values.Count < 2 ? null : new NameValuePair { name = name, long_percent = values[0], short_percent = values[1] };
        }

        private static string StripHtml(string html)
        {
            string text = Regex.Replace(html, @"<script[^>]*>.*?</script>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<style[^>]*>.*?</style>", " ", RegexOptions.Singleline | RegexOptions.IgnoreCase);
            text = Regex.Replace(text, @"<[^>]+>", " ");
            text = System.Net.WebUtility.HtmlDecode(text);
            return Regex.Replace(text, @"\s+", " ");
        }

        // =========================================================================
        // 4) COTATIONS (YAHOO FINANCE)
        // =========================================================================
        public static async Task<AiToolResult> GetMarketOverview(JsonElement arguments)
        {
            var symbols = GetString(arguments, "symbols", "EURUSD,GBPUSD,XAUUSD,BTC-USD").Split(new[] { ',', ';', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim().ToUpperInvariant()).Where(s => s.Length > 0).Distinct(StringComparer.Ordinal).Take(MaxOverviewSymbols).ToList();
            if (symbols.Count == 0) return AiToolResult.Error("Aucun symbole valide. Exemple : symbols=EURUSD,GBPUSD,XAUUSD,BTC-USD,US500");
            var quotes = new List<object>(); var errors = new List<string>();
            foreach (var symbol in symbols)
            {
                string yahoo = NormalizeYahooSymbol(symbol); string data = GetCached("yahoo_" + yahoo);
                if (data == null) { var response = await FetchAsync($"https://query1.finance.yahoo.com/v8/finance/chart/{Uri.EscapeDataString(yahoo)}?interval=1d&range=5d", 15); if (response != null && response.Status == 200 && !string.IsNullOrWhiteSpace(response.Content)) { data = response.Content; SetCache("yahoo_" + yahoo, data, CacheQuotes); } }
                var quote = ParseYahooQuote(data, symbol, yahoo); if (quote != null) quotes.Add(quote); else errors.Add(symbol + " : cotation introuvable");
            }
            return AiToolResult.Success(JsonSerializer.Serialize(new { source = "Yahoo Finance", updated_display = DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture), count = quotes.Count, errors = errors.Count > 0 ? errors : null, quotes }));
        }

        private static string NormalizeYahooSymbol(string symbol)
        {
            switch (symbol)
            {
                case "EURUSD": return "EURUSD=X"; case "GBPUSD": return "GBPUSD=X"; case "USDJPY": return "JPY=X"; case "USDCHF": return "CHF=X";
                case "USDCAD": return "CAD=X"; case "AUDUSD": return "AUDUSD=X"; case "NZDUSD": return "NZDUSD=X"; case "EURGBP": return "EURGBP=X"; case "EURJPY": return "EURJPY=X";
                case "XAUUSD": case "GOLD": return "GC=F"; case "XAGUSD": case "SILVER": return "SI=F"; case "WTI": case "CL": return "CL=F"; case "BRENT": case "BZ": return "BZ=F";
                case "BTC": case "BTCUSD": return "BTC-USD"; case "ETH": case "ETHUSD": return "ETH-USD";
                case "US500": case "SPX": return "^GSPC"; case "US30": case "DJI": return "^DJI"; case "US100": case "NDQ": return "^NDX";
                case "GER40": case "DAX": return "^GDAXI"; case "UK100": case "FTSE": return "^FTSE"; case "FRA40": case "CAC": return "^FCHI";
                default: return symbol;
            }
        }

        private static object ParseYahooQuote(string data, string requestedSymbol, string yahooSymbol)
        {
            if (string.IsNullOrEmpty(data)) return null;
            try
            {
                using (var doc = JsonDocument.Parse(data))
                {
                    if (!doc.RootElement.TryGetProperty("chart", out var chart) || !chart.TryGetProperty("result", out var results) || results.ValueKind != JsonValueKind.Array || results.GetArrayLength() == 0) return null;
                    var meta = results[0].TryGetProperty("meta", out var m) ? m : default(JsonElement);
                    if (meta.ValueKind != JsonValueKind.Object) return null;
                    double price = GetDoubleProp(meta, "regularMarketPrice", double.NaN);
                    if (double.IsNaN(price)) return null;
                    return new { symbol = requestedSymbol, yahoo_symbol = yahooSymbol, name = GetString(meta, "longName") ?? GetString(meta, "shortName") ?? yahooSymbol, price = Round6(price), change = Round6(GetDoubleProp(meta, "regularMarketChange", double.NaN)), change_percent = Round6(GetDoubleProp(meta, "regularMarketChangePercent", double.NaN)), day_high = Round6(GetDoubleProp(meta, "regularMarketDayHigh", double.NaN)), day_low = Round6(GetDoubleProp(meta, "regularMarketDayLow", double.NaN)), prev_close = Round6(GetDoubleProp(meta, "chartPreviousClose", double.NaN)), week52_high = Round6(GetDoubleProp(meta, "fiftyTwoWeekHigh", double.NaN)), week52_low = Round6(GetDoubleProp(meta, "fiftyTwoWeekLow", double.NaN)), currency = GetString(meta, "currency"), last_update = GetLongProp(meta, "regularMarketTime", 0) > 0 ? DateTimeOffset.FromUnixTimeSeconds(GetLongProp(meta, "regularMarketTime", 0)).UtcDateTime.ToString("yyyy-MM-dd HH:mm 'UTC'", CultureInfo.InvariantCulture) : null };
                }
            }
            catch { return null; }
        }

        private static double GetDoubleProp(JsonElement obj, string property, double fallback)
        { if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var v)) return fallback; if (v.ValueKind == JsonValueKind.Number) return v.GetDouble(); if (v.ValueKind == JsonValueKind.String) { double d; return double.TryParse(v.GetString(), NumberStyles.Any, CultureInfo.InvariantCulture, out d) ? d : fallback; } return fallback; }
        private static long GetLongProp(JsonElement obj, string property, long fallback)
        { if (obj.ValueKind != JsonValueKind.Object || !obj.TryGetProperty(property, out var v)) return fallback; if (v.ValueKind == JsonValueKind.Number) return (long)v.GetDouble(); return fallback; }
        private static double Round6(double value) => double.IsNaN(value) ? 0 : Math.Round(value, 6, MidpointRounding.AwayFromZero);

        // =========================================================================
        // 5) ACTUALITES
        // =========================================================================
        public static async Task<AiToolResult> GetMarketNews(JsonElement arguments)
        {
            string query = GetString(arguments, "query", "forex"); string language = GetString(arguments, "language", "fr").ToLowerInvariant();
            int maxResults = Math.Max(1, Math.Min(GetInt(arguments, "max_results", DefaultNewsResults), MaxNewsResults));
            string cacheKey = $"news_{query.ToLowerInvariant()}_{language}_{maxResults}"; string cached = GetCached(cacheKey);
            if (cached != null) return AiToolResult.Success(cached);
            var articles = new List<object>(); string engine = "Google News";
            try
            {
                string gl = language.ToUpperInvariant(); string url = "https://news.google.com/rss/search?q=" + Uri.EscapeDataString(query) + $"&hl={language}&gl={gl}&ceid={Uri.EscapeDataString($"{gl}:{language}")}";
                var response = await FetchAsync(url, 15, null, 600000);
                if (response != null && response.Status == 200 && !string.IsNullOrEmpty(response.Content)) { articles = ParseNewsRss(response.Content, maxResults); engine = "Google News"; }
                if (articles.Count == 0) { var bing = await FetchRssAsync("https://www.bing.com/news/search?q=" + Uri.EscapeDataString(query) + "&format=rss", maxResults); if (bing != null) { articles = bing; engine = "Bing News"; } }
            }
            catch { var bing = await FetchRssAsync("https://www.bing.com/news/search?q=" + Uri.EscapeDataString(query) + "&format=rss", maxResults); if (bing != null) { articles = bing; engine = "Bing News"; } }
            if (articles.Count == 0) return AiToolResult.Error("Aucune actualite recuperee. Reessayez plus tard.");
            string serialized = JsonSerializer.Serialize(new { source = engine, query, count = articles.Count, articles });
            SetCache(cacheKey, serialized, CacheNews); return AiToolResult.Success(serialized);
        }

        private static List<object> ParseNewsRss(string xml, int maxResults)
        {
            var result = new List<object>();
            try { foreach (var item in XDocument.Parse(xml).Root?.Descendants("item") ?? Enumerable.Empty<XElement>()) { var title = ((string)item.Element("title") ?? "").Trim(); var link = ((string)item.Element("link") ?? "").Trim(); if (title.Length == 0) continue; int sep = title.LastIndexOf(" - ", StringComparison.Ordinal); string source = sep > 10 ? title.Substring(sep + 3).Trim() : null; if (sep > 10) title = title.Substring(0, sep).Trim(); result.Add(new { title, source, date = ((string)item.Element("pubDate") ?? "").Trim(), url = link }); if (result.Count >= maxResults) break; } } catch { }
            return result;
        }

        private static async Task<List<object>> FetchRssAsync(string url, int maxResults)
        {
            var response = await FetchAsync(url, 15, null, 600000);
            if (response == null || response.Status != 200) return null;
            var parsed = ParseNewsRss(response.Content, maxResults);
            return parsed.Count > 0 ? parsed : null;
        }

        // =========================================================================
        // 6) RECHERCHE WEB
        // =========================================================================
        public static async Task<AiToolResult> WebSearch(JsonElement arguments)
        {
            string query = GetString(arguments, "query"); if (string.IsNullOrWhiteSpace(query)) return AiToolResult.Error("Parametre query obligatoire.");
            int maxResults = Math.Max(1, Math.Min(GetInt(arguments, "max_results", DefaultSearchResults), MaxSearchResults));
            string cacheKey = $"search_{query.Trim().ToLowerInvariant()}_{maxResults}"; string cached = GetCached(cacheKey);
            if (cached != null) return AiToolResult.Success(cached);
            var config = GetApiConfig(); List<object> results = null; string engine = null;
            if (config.ContainsKey("braveApiKey"))
            {
                var response = await FetchAsync("https://api.search.brave.com/res/v1/web/search?q=" + Uri.EscapeDataString(query) + $"&count={maxResults}&search_lang=en", 20, new Dictionary<string, string> { { "Accept", "application/json" }, { "X-Subscription-Token", config["braveApiKey"] } }, 500000);
                results = ParseBraveSearch(response); if (results != null) engine = "Brave Search API";
            }
            if (results == null) { var response = await FetchAsync("https://www.bing.com/search?format=rss&q=" + Uri.EscapeDataString(query) + $"&count={maxResults}", 20, null, 800000); results = ParseBingRss(response); if (results != null) engine = "Bing (RSS)"; }
            if (results == null) { var response = await FetchAsync("https://www.bing.com/search?q=" + Uri.EscapeDataString(query) + $"&count={maxResults}", 20, null, 1200000); results = ParseBingHtml(response); if (results != null) engine = "Bing (HTML)"; }
            if (results == null) return AiToolResult.Error("Recherche web indisponible depuis ce reseau.");
            string ser = JsonSerializer.Serialize(new { engine, query, count = results.Count, results });
            SetCache(cacheKey, ser, CacheSearch); return AiToolResult.Success(ser);
        }

        private static List<object> ParseBraveSearch(FetchResult response)
        {
            if (response == null || response.Status != 200) return null;
            try { using (var doc = JsonDocument.Parse(response.Content)) { if (!doc.RootElement.TryGetProperty("web", out var web) || !web.TryGetProperty("results", out var items)) return null; return items.EnumerateArray().Select(item => (object)new { title = GetString(item, "title"), url = GetString(item, "url"), snippet = GetString(item, "description") }).ToList(); } }
            catch { return null; }
        }

        private static List<object> ParseBingRss(FetchResult response)
        {
            if (response == null || response.Status != 200) return null;
            try { return XDocument.Parse(response.Content).Root?.Descendants("item").Select(item => (object)new { title = ((string)item.Element("title") ?? "").Trim(), url = ((string)item.Element("link") ?? "").Trim(), snippet = ((string)item.Element("description") ?? "").Trim() }).Where(x => ((string)x.GetType().GetProperty("title").GetValue(x)).Length > 0).ToList(); }
            catch { return null; }
        }

        private static List<object> ParseBingHtml(FetchResult response)
        {
            if (response == null || response.Status != 200) return null;
            var list = new List<object>();
            try
            {
                foreach (Match match in Regex.Matches(response.Content, @"<li\s+class\s*=\s*[""']b_algo[""'][^>]*>(.*?)</li>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
                {
                    string block = match.Groups[1].Value; var link = Regex.Match(block, @"<h2[^>]*>\s*<a[^>]*href=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    if (!link.Success) continue;
                    string url = System.Net.WebUtility.HtmlDecode(link.Groups[1].Value); string title = StripHtml(link.Groups[2].Value).Trim();
                    var snippetMatch = Regex.Match(block, @"<p[^>]*>(.*?)</p>", RegexOptions.Singleline | RegexOptions.IgnoreCase);
                    string snippet = snippetMatch.Success ? StripHtml(snippetMatch.Groups[1].Value).Trim() : "";
                    list.Add(new { title = title.Length > 0 ? title : url, url, snippet });
                    if (list.Count >= 15) break;
                }
            }
            catch { return null; }
            return list.Count > 0 ? list : null;
        }

        // =========================================================================
        // 7) LECTURE PAGE WEB
        // =========================================================================
        public static async Task<AiToolResult> FetchWebPage(JsonElement arguments)
        {
            string url = GetString(arguments, "url"); int maxChars = Math.Max(500, Math.Min(GetInt(arguments, "max_chars", DefaultPageChars), MaxPageChars));
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)) return AiToolResult.Error("URL invalide (https/http requis).");
            if (IsLocalOrPrivateHost(uri)) return AiToolResult.Error("Acces a des adresses internes/reseau local refuse.");
            var response = await FetchAsync(url, 20, null, 700000);
            if (response == null) return AiToolResult.Error("Page injoignable (reseau/delai).");
            if (response.Status == 403) return AiToolResult.Error("La page a refuse l'acces (anti-bot HTTP 403).");
            if (response.Status == 404) return AiToolResult.Error("Page introuvable (HTTP 404).");
            if (response.Status >= 400) return AiToolResult.Error($"Erreur HTTP {response.Status}.");
            string html = response.Content;
            string title = StripHtml(Regex.Match(html, @"<title[^>]*>(.*?)</title>", RegexOptions.Singleline | RegexOptions.IgnoreCase).Groups[1].Value);
            string text = StripHtml(html);
            var links = new List<object>();
            foreach (Match m in Regex.Matches(html, @"<a[^>]*href\s*=\s*[""']([^""']+)[""'][^>]*>(.*?)</a>", RegexOptions.Singleline | RegexOptions.IgnoreCase))
            {
                string href = m.Groups[1].Value.Trim(); string anchor = StripHtml(m.Groups[2].Value).Trim();
                if (href.Length == 0 || anchor.Length == 0 || href.StartsWith("#", StringComparison.Ordinal) || href.StartsWith("javascript:", StringComparison.OrdinalIgnoreCase)) continue;
                string absolute;
                try { if (href.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || href.StartsWith("https://", StringComparison.OrdinalIgnoreCase)) absolute = href; else if (href.StartsWith("//", StringComparison.Ordinal)) absolute = uri.Scheme + ":" + href; else if (href.StartsWith("/", StringComparison.Ordinal)) absolute = uri.Scheme + "://" + uri.Authority + href; else absolute = uri.Scheme + "://" + uri.Authority + "/" + href.TrimStart('/'); }
                catch { continue; }
                if (links.Any(l => l is NameValuePair p && p.name == absolute)) continue;
                links.Add(new NameValuePair { name = absolute, long_percent = Cap(anchor, 120) });
                if (links.Count >= 10) break;
            }
            return AiToolResult.Success(JsonSerializer.Serialize(new { url = uri.ToString(), title = Cap(title, 200), text_length = text.Length, text = Cap(text, maxChars), links = links.Select(l => { var p = (NameValuePair)l; return (object)new { url = p.name, anchor = p.long_percent }; }).ToList() }));
        }

        private static bool IsLocalOrPrivateHost(Uri uri)
        {
            if (uri.IsLoopback) return true; var host = uri.Host;
            if (host == "localhost") return true;
            if (IPAddress.TryParse(host, out var ip))
            {
                if (ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                { var bytes = ip.GetAddressBytes(); if (bytes[0] == 127 || bytes[0] == 10) return true; if (bytes[0] == 192 && bytes[1] == 168) return true; if (bytes[0] == 172 && bytes[1] >= 16 && bytes[1] <= 31) return true; if (bytes[0] == 169 && bytes[1] == 254) return true; }
                return ip.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 && (ip.IsIPv6LinkLocal || ip.IsIPv6SiteLocal);
            }
            return false;
        }
    }
}