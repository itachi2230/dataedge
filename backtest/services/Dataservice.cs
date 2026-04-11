using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;

namespace backtest.services
{
    public class Dataservice
    {
        // --- ROUTES API ---
        private const string ROUTE_FETCH_DATA = "api/public/data/fetch";
        private const string ROUTE_LIST_PAIRS = "api/public/data/pairs";

        private readonly HttpClient _httpClient;
        private readonly string _localDataFolder;

        public Dataservice(string baseUrl)
        {
            string finalBaseUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(finalBaseUrl),
                Timeout = TimeSpan.FromSeconds(30)
            };

            _localDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chart", "historical");

            if (!Directory.Exists(_localDataFolder))
                Directory.CreateDirectory(_localDataFolder);
        }

        #region Watchlist & Paires

        /// <summary>
        /// Récupère dynamiquement la liste des paires depuis le serveur Symfony
        /// </summary>
        public async Task<List<WatchlistSymbol>> GetWatchlistAsync()
        {
            try
            {
                var response = await _httpClient.GetAsync(ROUTE_LIST_PAIRS);

                if (response.IsSuccessStatusCode)
                {
                    string json = await response.Content.ReadAsStringAsync();
                    // Désérialisation du format Symfony : [{symbol: "EURUSD", created_at: "..."}]
                    var remotePairs = JsonConvert.DeserializeObject<List<RemotePairDTO>>(json);

                    return remotePairs.Select(p => new WatchlistSymbol
                    {
                        Symbol = p.Symbol,
                        Price = "---", // Le prix sera mis à jour par le flux live plus tard
                        Change = "0.00%"
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchlist] Erreur serveur: {ex.Message}");
            }

            // Fallback : Si le serveur est injoignable, on retourne une liste par défaut
            return GetDefaultWatchlist();
        }

        public List<WatchlistSymbol> GetDefaultWatchlist()
        {
            return new List<WatchlistSymbol>
            {
                new WatchlistSymbol { Symbol = "EURUSD", Price = "1.08450", Change = "0.00%" },
                new WatchlistSymbol { Symbol = "GBPUSD", Price = "1.26310", Change = "0.00%" },
                new WatchlistSymbol { Symbol = "XAUUSD", Price = "2345.12", Change = "0.00%" }
            };
        }

        #endregion

        #region Récupération de Données (Historical)

        public async Task<(bool success, string message, string filePath)> GetMarketDataAsync(string pair, string timeframe, string year)
        {
            string cleanPair = pair.ToUpper().Trim();
            string serverTf = MapTimeframeToServer(timeframe);

            string fileNameCsv = $"{cleanPair}_{serverTf}_{year}.csv";
            string localPath = Path.Combine(_localDataFolder, fileNameCsv);

            // 1. Check Cache Local
            if (File.Exists(localPath))
            {
                return (true, "DATA_LOCAL_READY", localPath);
            }

            try
            {
                // 2. Appel API
                string url = $"{ROUTE_FETCH_DATA}?pair={cleanPair}&tf={serverTf}&year={year}";
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead);

                if (!response.IsSuccessStatusCode)
                {
                    return (false, $"SERVER_ERROR: {response.StatusCode}", null);
                }

                // 3. Décompression GZip vers CSV
                using (var compressedStream = await response.Content.ReadAsStreamAsync())
                using (var decompressionStream = new GZipStream(compressedStream, CompressionMode.Decompress))
                using (var fileStream = File.Create(localPath))
                {
                    await decompressionStream.CopyToAsync(fileStream);
                }

                return (true, "DOWNLOAD_SUCCESS", localPath);
            }
            catch (Exception ex)
            {
                return (false, $"EXCEPTION: {ex.Message}", null);
            }
        }

        public bool IsDataAvailableLocally(string pair, string tf, string year)
        {
            string serverTf = MapTimeframeToServer(tf);
            string fileName = $"{pair.ToUpper()}_{serverTf}_{year}.csv";
            return File.Exists(Path.Combine(_localDataFolder, fileName));
        }

        private string MapTimeframeToServer(string tf)
        {
            string input = tf.ToLower().Replace(" ", "").Trim();
            return input switch
            {
                "1m" => "1min",
                "5m" => "5mins",
                "15m" => "15mins",
                "30m" => "30mins",
                "1h" => "hourly",
                "4h" => "4hours",
                "d" => "daily",
                "w" => "weekly",
                _ => input
            };
        }

        #endregion
    }

    #region Models

    // Objet reçu du serveur Symfony
    public class RemotePairDTO
    {
        [JsonProperty("symbol")]
        public string Symbol { get; set; }
        [JsonProperty("created_at")]
        public string CreatedAt { get; set; }
    }

    public class WatchlistSymbol
    {
        public string Symbol { get; set; }
        public string Price { get; set; }
        public string Change { get; set; }
    }

    public class CandleModel
    {
        [JsonProperty("time")]
        public long time { get; set; }
        [JsonProperty("open")]
        public double open { get; set; }
        [JsonProperty("high")]
        public double high { get; set; }
        [JsonProperty("low")]
        public double low { get; set; }
        [JsonProperty("close")]
        public double close { get; set; }
        [JsonProperty("value")]
        public double value { get; set; }
    }

    #endregion
}