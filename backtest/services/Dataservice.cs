using System;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Threading.Tasks;
using System.Collections.Generic;
using Newtonsoft.Json;
using System.Linq;
using System.IO.Pipelines;
using System.Threading;

namespace backtest.services
{
    public class Dataservice
    {
        // --- ROUTES API ---
        private const string ROUTE_FETCH_DATA = "api/public/data/fetch";
        private const string ROUTE_LIST_PAIRS = "api/public/data/pairs";

        private const string WATCHLIST_CACHE_FILE = "watchlist_cache.json";
        private readonly HttpClient _httpClient;
        private readonly string _localDataFolder;
        private readonly string _cacheFilePath;

        public Dataservice(string baseUrl)
        {
            string finalBaseUrl = baseUrl.EndsWith("/") ? baseUrl : baseUrl + "/";
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri(finalBaseUrl),
                Timeout = TimeSpan.FromSeconds(18)
            };

            _localDataFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "chart", "historical");
            _cacheFilePath = Path.Combine(_localDataFolder, WATCHLIST_CACHE_FILE);

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
                    await SaveWatchlistLocallyAsync(json);
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
            var cachedWatchlist = await GetLocalWatchlistAsync();
            if (cachedWatchlist != null && cachedWatchlist.Any())
            {
                return cachedWatchlist;
            }
            // Fallback : Si le serveur est injoignable, on retourne une liste par défaut
            return GetDefaultWatchlist();
        }
        private async Task SaveWatchlistLocallyAsync(string json)
        {
            try
            {
                 File.WriteAllText(_cacheFilePath, json);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchlist] Impossible de sauvegarder le cache : {ex.Message}");
            }
        }
        private async Task<List<WatchlistSymbol>> GetLocalWatchlistAsync()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    string json = File.ReadAllText(_cacheFilePath);
                    var localPairs = JsonConvert.DeserializeObject<List<RemotePairDTO>>(json);

                    return localPairs.Select(p => new WatchlistSymbol
                    {
                        Symbol = p.Symbol,
                        Price = "---",
                        Change = "0.00% (offline)"
                    }).ToList();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[Watchlist] Erreur lecture cache : {ex.Message}");
            }
            return null;
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

        public async Task<(bool success, string message, string filePath)> GetMarketDataAsync(string pair, string timeframe, string year, CancellationToken cts)
        {
            string cleanPair = pair.ToUpper().Trim();
            string serverTf = MapTimeframeToServer(timeframe);
            string fileNameCsv = $"{cleanPair}_{serverTf}_{year}.csv";
            string localPath = Path.Combine(_localDataFolder, fileNameCsv);

            // Fichier temporaire pour éviter de corrompre le cache en cas de coupure
            string tempPath = localPath + ".tmp";

            // 1. Check Cache Local (Vérification de la taille pour éviter les fichiers vides)
            if (File.Exists(localPath))
            {
                var info = new FileInfo(localPath);
                if (info.Length > 0)
                    return (true, "DATA_LOCAL_READY", localPath);

                File.Delete(localPath); // Nettoyage si fichier invalide
            }

            try
            {
                // 2. Appel API avec stream pour ne pas charger tout en RAM
                string url = $"{ROUTE_FETCH_DATA}?pair={cleanPair}&tf={serverTf}&year={year}";

                // Timeout légèrement plus long pour les gros historiques
                var response = await _httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cts);

                if (!response.IsSuccessStatusCode)
                    return (false, $"SERVER_ERROR: {response.StatusCode}", null);

                // 3. Décompression vers fichier TEMPORAIRE
                using (var compressedStream = await response.Content.ReadAsStreamAsync())
                using (var decompressionStream = new GZipStream(compressedStream, CompressionMode.Decompress))
                using (var fileStream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, useAsync: true))
                {
                    // CopyToAsync est parfait ici car il ne bloque pas le thread UI
                    await decompressionStream.CopyToAsync(fileStream, 81920, cts);
                    await fileStream.FlushAsync();
                }

                // 4. Validation finale : On renomme le fichier temp en fichier final
                // C'est une opération atomique : soit le fichier est complet, soit il n'existe pas.
                if (File.Exists(localPath)) File.Delete(localPath);
                File.Move(tempPath, localPath);

                return (true, "DOWNLOAD_SUCCESS", localPath);
            }
            catch (Exception ex)
            {
                if (File.Exists(tempPath)) try { File.Delete(tempPath); } catch { }
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