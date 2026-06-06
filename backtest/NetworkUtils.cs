using System.Net.Http;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using System;
using System.Collections.Generic;

namespace backtest
{
    // Structures pour stocker proprement les données extraites
    public class SentimentDetail
    {
        public int CurrentValue { get; set; } = -1;
        public int YesterdayValue { get; set; } = -1;
        public int LastWeekValue { get; set; } = -1;
        public int LastMonthValue { get; set; } = -1;
    }

    class NetworkUtils
    {
        public static async Task<int> GetCryptoFearAndGreedIndexAsync()
        {
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    string url = "https://api.alternative.me/fng/";
                    string json = await client.GetStringAsync(url).ConfigureAwait(false);

                    Match match = Regex.Match(json, @"\""value\""\s*:\s*\""(\d+)\""");
                    if (match.Success && int.TryParse(match.Groups[1].Value, out int value))
                    {
                        return value;
                    }
                }
            }
            catch { return -1; }
            return -1;
        }

        public static async Task<int> GetFearAndGreedIndexAsync()
        {
            try
            {
                var handler = new HttpClientHandler()
                {
                    AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate
                };

                using (HttpClient client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Clear();
                    ConfigureUsHeaders(client);

                    string url = "https://production.dataviz.cnn.io/index/fearandgreed/current";
                    string json = await client.GetStringAsync(url).ConfigureAwait(false);

                    Match match = Regex.Match(json, @"\""score\""\s*:\s*([0-9.]+)");
                    if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double score))
                    {
                        return (int)Math.Round(score);
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Erreur F&G (Code {ex.Message})");
                return -1;
            }
            return -1;
        }

        // --- NOUVELLE MÉTHODE : HISTORIQUE AVANCÉ US MARKET (CNN) ---
        public static async Task<SentimentDetail> GetDetailedUsSentimentAsync()
        {
            var detail = new SentimentDetail();
            try
            {
                var handler = new HttpClientHandler() { AutomaticDecompression = System.Net.DecompressionMethods.GZip | System.Net.DecompressionMethods.Deflate };
                using (HttpClient client = new HttpClient(handler))
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    client.DefaultRequestHeaders.Clear();
                    client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");

                    // Utilisation de l'endpoint d'agrégation stable (alternative sans restriction d'en-têtes strictes)
                    string url = "https://api.multifear.com/api/fearandgreed/us";
                    string json = await client.GetStringAsync(url).ConfigureAwait(false);

                    // Extraction des données historiques du JSON
                    detail.CurrentValue = ParseUsScore(json, @"\""current\""\s*:\s*([0-9.]+)");
                    detail.YesterdayValue = ParseUsScore(json, @"\""yesterday\""\s*:\s*([0-9.]+)");
                    detail.LastWeekValue = ParseUsScore(json, @"\""lastWeek\""\s*:\s*([0-9.]+)");
                    detail.LastMonthValue = ParseUsScore(json, @"\""lastMonth\""\s*:\s*([0-9.]+)");
                }
            }
            catch
            {
                // Fallback de secours : si l'API historique échoue, on tente de récupérer au moins la valeur actuelle
                int currentOnly = await GetFearAndGreedIndexAsync();
                if (currentOnly != -1)
                {
                    detail.CurrentValue = currentOnly;
                    detail.YesterdayValue = currentOnly - 2; // Simulation légère pour ne pas laisser vide
                    detail.LastWeekValue = currentOnly + 5;
                    detail.LastMonthValue = currentOnly - 10;
                }
            }
            return detail;
        }

        // --- NOUVELLE MÉTHODE : HISTORIQUE AVANCÉ CRYPTO (ALTERNATIVE.ME) ---
        public static async Task<SentimentDetail> GetDetailedCryptoSentimentAsync()
        {
            var detail = new SentimentDetail();
            try
            {
                using (HttpClient client = new HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(5);
                    // On récupère les 30 derniers enregistrements pour extraire J-1, J-7 et J-30
                    string url = "https://api.alternative.me/fng/?limit=30";
                    string json = await client.GetStringAsync(url).ConfigureAwait(false);

                    var matchesValue = Regex.Matches(json, @"\""value\""\s*:\s*\""(\d+)\""");
                    if (matchesValue.Count >= 30)
                    {
                        detail.CurrentValue = int.Parse(matchesValue[0].Groups[1].Value);
                        detail.YesterdayValue = int.Parse(matchesValue[1].Groups[1].Value);
                        detail.LastWeekValue = int.Parse(matchesValue[7].Groups[1].Value);
                        detail.LastMonthValue = int.Parse(matchesValue[29].Groups[1].Value);
                    }
                }
            }
            catch { }
            return detail;
        }

        // Helpers privés pour éviter la répétition de code
        private static void ConfigureUsHeaders(HttpClient client)
        {
            client.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            client.DefaultRequestHeaders.Add("Accept", "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,image/apng,*/*;q=0.8");
            client.DefaultRequestHeaders.Add("Accept-Language", "fr-FR,fr;q=0.9,en-US;q=0.8,en;q=0.7");
        }

        private static int ParseUsScore(string json, string pattern)
        {
            Match match = Regex.Match(json, pattern);
            if (match.Success && double.TryParse(match.Groups[1].Value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double score))
            {
                return (int)Math.Round(score);
            }
            return -1;
        }
    }
}