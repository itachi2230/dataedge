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

        public StrategieData LoadData()
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
            if (trades == null || trades.Count == 0) return;

            // --- Stats Basiques ---
            data.StatsBasiques["Total Trades"] = trades.Count;
            int totalTp = trades.Count(t => t.Result == Resultat.TP);
            data.StatsBasiques["Winrate"] = (float)totalTp / trades.Count * 100;
            data.StatsBasiques["Average RR"] = (float)trades.Average(t => t.RR);

            // Ajout de métriques de rentabilité globale
            double totalGrossProfit = trades.Where(t => t.Result == Resultat.TP).Sum(t => t.RR);
            double totalGrossLoss = trades.Where(t => t.Result == Resultat.SL).Count(); // En admettant que 1 SL = -1R
            data.StatsBasiques["Expectancy"] = (float)(totalGrossProfit - totalGrossLoss) / trades.Count;
            data.StatsBasiques["Profit Factor"] = totalGrossLoss > 0 ? (float)(totalGrossProfit / totalGrossLoss) : (float)totalGrossProfit;

            // Réinitialisation
            data.StatsAvancees = new AdvancedStats();

            foreach (var trade in trades)
            {
                // On calcule la valeur de performance pour ce trade (ici on utilise le RR)
                double profit = trade.Result == Resultat.TP ? trade.RR : 0;
                double loss = trade.Result == Resultat.SL ? 1.0 : 0; // On considère 1R de perte par SL

                // 1. Stats par Jour
                UpdateAdvancedStat(data.StatsAvancees.DayOfWeekStats, trade.DateEntree.DayOfWeek, trade.Result, profit, loss);

                // 2. Stats par Type d'ordre (BUY/SELL)
                UpdateAdvancedStat(data.StatsAvancees.TypeOrdreStats, trade.TypeOrdre, trade.Result, profit, loss);

                // 3. Stats par Paire
                string paire = string.IsNullOrEmpty(trade.Paire) ? "Inconnue" : trade.Paire;
                UpdateAdvancedStat(data.StatsAvancees.PairStats, paire, trade.Result, profit, loss);

                // 4. Stats par Session
                string session = GetSession(trade.DateEntree.Hour);
                UpdateAdvancedStat(data.StatsAvancees.SessionStats, session, trade.Result, profit, loss);

                // 5. Analyse des configurations (Champs Dynamiques)
                if (trade.ChampsPersonnalises != null)
                {
                    foreach (var field in trade.ChampsPersonnalises)
                    {
                        if (field.Valeur == null) continue;
                        string valStr = field.Valeur.ToString();
                        string key = field.Nom;

                        if (!data.StatsAvancees.PerformanceStats.ContainsKey(key))
                            data.StatsAvancees.PerformanceStats[key] = new Dictionary<string, PerformanceStat>();

                        UpdatePerf(data.StatsAvancees.PerformanceStats[key], valStr, trade.Result, profit, loss);
                    }
                }
            }

            // --- IDENTIFICATION DES MEILLEURES ET PIRES CONFIGS ---
            // On peut maintenant chercher quelle valeur de quel champ a la meilleure Expectancy
            IdentifyTopPerformers(data);

            SaveData(data);
        }

        // Helper pour simplifier les mises à jour
        private void UpdateAdvancedStat<T>(Dictionary<T, PerformanceStat> dict, T key, Resultat res, double profit, double loss)
        {
            if (!dict.ContainsKey(key)) dict[key] = new PerformanceStat();
            UpdatePerf(dict[key], res, profit, loss);
        }

        // Surcharge de UpdatePerf pour inclure les gains/pertes
        private void UpdatePerf(PerformanceStat stat, Resultat res, double profit, double loss)
        {
            if (res == Resultat.TP) stat.CountTP++;
            else if (res == Resultat.SL) stat.CountSL++;

            stat.TotalProfit += profit;
            stat.TotalLoss += loss;
        }

        // Surcharge pour les dictionnaires imbriqués
        private void UpdatePerf(Dictionary<string, PerformanceStat> dict, string key, Resultat res, double profit, double loss)
        {
            if (!dict.ContainsKey(key)) dict[key] = new PerformanceStat();
            UpdatePerf(dict[key], res, profit, loss);
        }
        // Helper pour déterminer la session
        private string GetSession(int hour)
        {
            if (hour >= 0 && hour < 8) return "Tokyo";
            if (hour >= 8 && hour < 13) return "Londres";
            if (hour >= 13 && hour < 20) return "New York";
            return "Hors Session";
        }
        private void IdentifyTopPerformers(StrategieData data)
        {
            var allConfigs = new List<ConfigRank>();

            // 1. Analyser les Sessions
            foreach (var item in data.StatsAvancees.SessionStats)
                allConfigs.Add(MapToRank(item.Key, "Session", item.Value));

            // 2. Analyser les Paires
            foreach (var item in data.StatsAvancees.PairStats)
                allConfigs.Add(MapToRank(item.Key, "Paire", item.Value));

            // 3. Analyser les Jours de la semaine
            foreach (var item in data.StatsAvancees.DayOfWeekStats)
                allConfigs.Add(MapToRank(item.Key.ToString(), "Jour", item.Value));

            // 4. Analyser les Champs Dynamiques (les plus importants !)
            foreach (var category in data.StatsAvancees.PerformanceStats)
            {
                foreach (var val in category.Value)
                {
                    allConfigs.Add(MapToRank($"{category.Key}: {val.Key}", "Setup", val.Value));
                }
            }

            // --- FILTRAGE ET TRI ---
            // On ne garde que les configs ayant un nombre minimum de trades pour éviter les stats faussées
            var validConfigs = allConfigs.Where(c => c.NombreTrades >= 3).ToList();

            // Top 5 Meilleures (Expectancy la plus haute)
            data.StatsAvancees.BestConfigs = validConfigs
                .OrderByDescending(c => c.Expectancy)
                .Take(5)
                .ToList();

            // Top 5 Pires (Expectancy la plus basse / négative)
            data.StatsAvancees.WorstConfigs = validConfigs
                .OrderBy(c => c.Expectancy)
                .Take(5)
                .ToList();
        }

        // Petit helper pour convertir une PerformanceStat en ConfigRank
        private ConfigRank MapToRank(string name, string category, PerformanceStat stat)
        {
            return new ConfigRank
            {
                NomParametre = name,
                Categorie = category,
                Expectancy = Math.Round(stat.Expectancy, 2),
                NombreTrades = stat.TotalTrades
            };
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
        public int CountTP { get; set; }
        public int CountSL { get; set; }
        public double TotalProfit { get; set; } // Nouveau
        public double TotalLoss { get; set; }   // Nouveau
        public double NetProfit => TotalProfit - TotalLoss;
        public int TotalTrades => CountTP + CountSL;
        public double Winrate => TotalTrades > 0 ? (double)CountTP / TotalTrades * 100 : 0;
        public double Expectancy => TotalTrades > 0 ? NetProfit / TotalTrades : 0;
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
        public List<ConfigRank> BestConfigs { get; set; } = new List<ConfigRank>();
        public List<ConfigRank> WorstConfigs { get; set; } = new List<ConfigRank>();
    }
    public class ConfigRank
    {
        public string NomParametre { get; set; } // ex: "RSI > 70"
        public string Categorie { get; set; }    // ex: "Indicateur"
        public double Expectancy { get; set; }
        public int NombreTrades { get; set; }
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