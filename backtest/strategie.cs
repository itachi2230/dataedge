using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows.Data;

namespace backtest
{
    public enum Resultat { SL, TP, TR, BE, PARTIAL }
    public enum TypeOrdre { BUY, SELL }

    // --- STRUCTURE DU FICHIER JSON ---
    public class StrategieData
    {
        public string Nom { get; set; }
        public string Description { get; set; }

        // Nouvelle propriété : stocke les noms des colonnes personnalisées (ex: "RSI", "ZONE")
        public List<string> ChampsCustomConfig { get; set; } = new List<string>();

        public List<Trade> Trades { get; set; } = new List<Trade>();
        public List<Trade> Journal { get; set; } = new List<Trade>();
        public Dictionary<string, object> StatsBasiques { get; set; } = new Dictionary<string, object>();
        public AdvancedStats StatsAvancees { get; set; } = new AdvancedStats();
    }

    public class Strategie
    {
        public static string dataFolder = "data";
        public static string metadataFolder = "metadata";
        public static string strategiesFile = Path.Combine(metadataFolder, "strategies.txt");

        public string Nom { get; set; }
        public string description { get; set; }

        public string filePath => Path.Combine(dataFolder, $"{Nom}.json");

        public Strategie(string nom, string description = "", bool temp = false)
        {
            Nom = nom;
            this.description = description;

            if (!Directory.Exists(dataFolder)) Directory.CreateDirectory(dataFolder);
            if (!Directory.Exists(metadataFolder)) Directory.CreateDirectory(metadataFolder);

            if (!temp)
            {
                if (!File.Exists(filePath))
                {
                    SaveData(new StrategieData { Nom = this.Nom, Description = this.description });
                }

                List<string> existingNames = GetAllStrategyNames();
                if (!existingNames.Contains(Nom))
                {
                    File.AppendAllText(strategiesFile, Nom + "%");
                }
            }
        }

        // --- GESTION DE LA STRUCTURE DYNAMIQUE ---

        /// <summary>
        /// Définit les champs personnalisés pour cette stratégie dans le JSON.
        /// </summary>
        public void SetStructure(List<string> nomsDesChamps)
        {
            var data = LoadData();
            data.ChampsCustomConfig = nomsDesChamps.Select(n => n.ToUpper()).ToList();
            SaveData(data);
        }

        /// <summary>
        /// Récupère la liste des noms des champs personnalisés configurés.
        /// </summary>
        public List<string> GetStructure()
        {
            return LoadData().ChampsCustomConfig;
        }

        // --- GESTION DU STOCKAGE JSON ---
        private void SaveData(StrategieData data)
        {
            var options = new JsonSerializerOptions { WriteIndented = true, Converters = { new JsonStringEnumConverter() } };
            string jsonString = JsonSerializer.Serialize(data, options);
            File.WriteAllText(filePath, jsonString);
        }

        private StrategieData LoadData()
        {
            if (!File.Exists(filePath)) return new StrategieData { Nom = this.Nom, Description = this.description };
            try
            {
                string jsonString = File.ReadAllText(filePath);
                return JsonSerializer.Deserialize<StrategieData>(jsonString, new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }) ?? new StrategieData();
            }
            catch { return new StrategieData { Nom = this.Nom, Description = this.description }; }
        }

        private List<string> GetAllStrategyNames()
        {
            if (!File.Exists(strategiesFile)) return new List<string>();
            string content = File.ReadAllText(strategiesFile);
            return content.Split(new[] { '%' }, StringSplitOptions.RemoveEmptyEntries)
                          .Select(n => n.Trim())
                          .Where(n => !string.IsNullOrEmpty(n))
                          .ToList();
        }

        // --- METHODES DE COMPATIBILITÉ ---
        public void AddTrade(Trade trade)
        {
            var data = LoadData();
            trade.Id = data.Trades.Count > 0 ? data.Trades.Max(t => t.Id) + 1 : 1;
            data.Trades.Add(trade);
            SaveData(data);
            CalculateStatistics(data);
        }

        public void AddJournal(Trade trade)
        {
            var data = LoadData();
            trade.Id = data.Journal.Count > 0 ? data.Journal.Max(t => t.Id) + 1 : 1;
            data.Journal.Add(trade);
            SaveData(data);
        }

        public List<Trade> GetTrades() => LoadData().Trades;
        public List<Trade> GetJournal() => LoadData().Journal;

        public void RemoveTradeById(long tradeId)
        {
            var data = LoadData();
            data.Trades.RemoveAll(t => t.Id == tradeId);
            CalculateStatistics(data);
            SaveData(data);
        }

        public void SupprimerStrategie()
        {
            if (File.Exists(filePath)) File.Delete(filePath);
            if (File.Exists(strategiesFile))
            {
                string contenu = File.ReadAllText(strategiesFile);
                File.WriteAllText(strategiesFile, contenu.Replace($"{Nom}%", string.Empty));
            }
        }

        public void LoadMetadata()
        {
            var data = LoadData();
            this.Nom = data.Nom;
            this.description = data.Description;
        }

        // --- CALCUL DES STATISTIQUES ---
        public void CalculateStatistics(StrategieData data = null)
        {
            if (data == null) data = LoadData();
            var trades = data.Trades;
            if (trades.Count == 0) return;

            data.StatsBasiques["Total Trades"] = trades.Count;
            data.StatsBasiques["Winrate"] = (float)trades.Count(t => t.Result == Resultat.TP) / trades.Count * 100;
            data.StatsBasiques["Average RR"] = (float)trades.Average(t => t.RR);
            data.StatsBasiques["Max RR"] = (float)trades.Max(t => t.RR);
            data.StatsBasiques["Min RR"] = (float)trades.Min(t => t.RR);

            var dayGroups = trades.GroupBy(t => t.DateEntree.DayOfWeek);
            data.StatsBasiques["Most Favorable Day"] = dayGroups.OrderByDescending(g => g.Count(t => t.Result == Resultat.TP)).FirstOrDefault()?.Key.ToString();
            data.StatsBasiques["Least Favorable Day"] = dayGroups.OrderByDescending(g => g.Count(t => t.Result == Resultat.SL)).FirstOrDefault()?.Key.ToString();

            data.StatsAvancees = new AdvancedStats();
            foreach (var trade in trades)
            {
                UpdatePerf(data.StatsAvancees.TypeOrdreStats[trade.TypeOrdre], trade.Result);
                if (!data.StatsAvancees.PairStats.ContainsKey(trade.Paire))
                    data.StatsAvancees.PairStats[trade.Paire] = new PerformanceStat();
                UpdatePerf(data.StatsAvancees.PairStats[trade.Paire], trade.Result);
            }
            SaveData(data);
        }

        private void UpdatePerf(PerformanceStat stat, Resultat res)
        {
            if (res == Resultat.TP) stat.PercentTP++;
            else if (res == Resultat.SL) stat.PercentSL++;
        }

        public Dictionary<string, object> GetStatistics() => LoadData().StatsBasiques;
        public AdvancedStats RetrieveStats() => LoadData().StatsAvancees;
    }

    public class Trade
    {
        public long Id { get; set; }
        public string Paire { get; set; }
        public Resultat Result { get; set; }
        public DateTime DateEntree { get; set; }
        public DateTime DateSortie { get; set; }
        public float RR { get; set; }
        public string description { get; set; }
        public TypeOrdre TypeOrdre { get; set; }
        public string ImageLtf { get; set; }
        public string ImageHtf { get; set; }
        public List<ChampPersonnalise> ChampsPersonnalises { get; set; } = new List<ChampPersonnalise>();
        public double Profit { get; set; }
        public string strategie { get; set; }
        public Trade() { }
        public Trade(double profit = 0) { Profit = profit; }
    }

    public class ChampPersonnalise
    {
        public string Nom { get; set; }
        public object Valeur { get; set; }
        public ChampPersonnalise() { }
        public ChampPersonnalise(string nom, object valeur = null) { Nom = nom?.ToUpper(); Valeur = valeur; }
    }

    public class PerformanceStat
    {
        public double PercentTP { get; set; }
        public double PercentSL { get; set; }
    }

    public class AdvancedStats
    {
        public Dictionary<string, Dictionary<string, PerformanceStat>> PerformanceStats { get; set; } = new Dictionary<string, Dictionary<string, PerformanceStat>>();
        public Dictionary<DayOfWeek, PerformanceStat> DayOfWeekStats { get; set; } = new Dictionary<DayOfWeek, PerformanceStat>();
        public Dictionary<string, PerformanceStat> PairStats { get; set; } = new Dictionary<string, PerformanceStat>();
        public Dictionary<string, PerformanceStat> SessionStats { get; set; } = new Dictionary<string, PerformanceStat> {
            { "Tokyo", new PerformanceStat() }, { "Londres", new PerformanceStat() }, { "New York", new PerformanceStat() }
        };
        public Dictionary<TypeOrdre, PerformanceStat> TypeOrdreStats { get; set; } = new Dictionary<TypeOrdre, PerformanceStat> {
            { TypeOrdre.BUY, new PerformanceStat() }, { TypeOrdre.SELL, new PerformanceStat() }
        };
    }

    public static class utils
    {
        public static List<Strategie> getStrategies()
        {
            List<Strategie> str = new List<Strategie>();
            if (!File.Exists(Strategie.strategiesFile)) return str;
            try
            {
                var names = File.ReadAllText(Strategie.strategiesFile).Split(new[] { '%' }, StringSplitOptions.RemoveEmptyEntries);
                foreach (string nom in names)
                {
                    string cleanName = nom.Trim();
                    if (!string.IsNullOrEmpty(cleanName))
                    {
                        Strategie strate = new Strategie(cleanName, "", true);
                        strate.LoadMetadata();
                        str.Add(strate);
                    }
                }
            }
            catch { }
            return str;
        }

        public static Statistics CalculateStatistics(IEnumerable<Trade> trades)
        {
            var stats = new Statistics();
            if (trades == null || !trades.Any()) return stats;

            stats.TotalProfit = trades.Where(t => t.Profit > 0).Sum(t => t.Profit);
            stats.TotalLoss = trades.Where(t => t.Profit < 0).Sum(t => t.Profit);

            var pairGroups = trades.Where(t => !string.IsNullOrEmpty(t.Paire)).GroupBy(t => t.Paire);
            if (pairGroups.Any())
            {
                var pairPerf = pairGroups.Select(g => new { P = g.Key, Prof = g.Sum(t => t.Profit) }).OrderByDescending(x => x.Prof);
                stats.BestPair = pairPerf.FirstOrDefault()?.P ?? "N/A";
                stats.WorstPair = pairPerf.LastOrDefault()?.P ?? "N/A";
            }

            stats.SuccessRateBuy = trades.Count(t => t.TypeOrdre == TypeOrdre.BUY) > 0 ?
                Math.Round((double)trades.Count(t => t.TypeOrdre == TypeOrdre.BUY && t.Result == Resultat.TP) / trades.Count(t => t.TypeOrdre == TypeOrdre.BUY) * 100) : 0;

            stats.SuccessRateSell = trades.Count(t => t.TypeOrdre == TypeOrdre.SELL) > 0 ?
                Math.Round((double)trades.Count(t => t.TypeOrdre == TypeOrdre.SELL && t.Result == Resultat.TP) / trades.Count(t => t.TypeOrdre == TypeOrdre.SELL) * 100) : 0;

            stats.StrategyPerformance = trades.GroupBy(t => t.strategie).ToDictionary(g => g.Key, g => g.Sum(t => t.Profit));
            stats.BestTrade = trades.OrderByDescending(t => t.Profit).FirstOrDefault();

            return stats;
        }
    }

    public class ChampPersonnaliseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is List<ChampPersonnalise> champs && parameter is string nom)
                return champs.FirstOrDefault(c => c.Nom == nom.ToUpper())?.Valeur ?? string.Empty;
            return string.Empty;
        }
        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => null;
    }
}