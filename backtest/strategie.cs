using OfficeOpenXml;
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
    public enum Resultat { TP, SL, TR, BE, PARTIAL }
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

        public void UpdateTrade(Trade updatedTrade)
        {
            var data = LoadData();
            // On cherche l'index du trade existant via son ID
            int index = data.Trades.FindIndex(t => t.Id == updatedTrade.Id);

            if (index != -1)
            {
                data.Trades[index] = updatedTrade; // Remplacement
                CalculateStatistics(data); // Important : Recalculer après modification
                SaveData(data);
            }
        }
        public void UpdateJournal(Trade updatedTrade)
        {
            var data = LoadData();
            int index = data.Journal.FindIndex(t => t.Id == updatedTrade.Id);

            if (index != -1)
            {
                data.Journal[index] = updatedTrade;
                // On peut aussi recalculer les stats ici si ton journal impacte le Dashboard global
                CalculateStatistics(data);
                SaveData(data);
            }
        }
        public void ModifierInfosGenerales(string nouveauNom, string nouvelleDescription)
        {
            if (string.IsNullOrWhiteSpace(nouveauNom)) return;

            var data = LoadData();
            string ancienNom = data.Nom;
            string ancienPath = filePath;

            // Mise à jour des propriétés de l'objet de données
            data.Nom = nouveauNom;
            data.Description = nouvelleDescription;

            // 1. Si le nom a changé, on gère les fichiers
            if (ancienNom != nouveauNom)
            {
                this.Nom = nouveauNom; // Met à jour la propriété de l'instance pour que 'filePath' change

                // Supprimer l'ancien nom dans strategies.txt et ajouter le nouveau
                if (File.Exists(strategiesFile))
                {
                    string contenu = File.ReadAllText(strategiesFile);
                    contenu = contenu.Replace($"{ancienNom}%", ""); // Retire l'ancien
                    File.WriteAllText(strategiesFile, contenu + nouveauNom + "%"); // Ajoute le nouveau
                }

                // Supprimer l'ancien fichier JSON après avoir sauvegardé le nouveau
                if (File.Exists(ancienPath)) File.Delete(ancienPath);
            }

            this.description = nouvelleDescription;
            SaveData(data); // Sauvegarde le fichier (avec le nouveau nom si changé)
        }
        public void RemoveTradeById(long tradeId)
        {
            var data = LoadData();
            data.Trades.RemoveAll(t => t.Id == tradeId);
            CalculateStatistics(data);
            SaveData(data);
        }
        public void RemoveJournalById(long tradeId)
        {
            var data = LoadData();
            data.Journal.RemoveAll(t => t.Id == tradeId);
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


        //added

    }

    public class Trade
    {
        public long Id { get; set; }
        public string Paire { get; set; }
        public Resultat Result { get; set; }
        public DateTime DateEntree { get; set; }
        public DateTime DateSortie { get; set; }
        public float RR { get; set; }
        public string prixOpen { get; set; }
        public string prixClose { get; set; }
        public string description { get; set; }
        public TypeOrdre TypeOrdre { get; set; }
        public string ImageLtf { get; set; }
        public string ImageHtf { get; set; }
        public List<ChampPersonnalise> ChampsPersonnalises { get; set; } = new List<ChampPersonnalise>();
        public double Profit { get; set; }
        public string strategie { get; set; }
        public Trade() { }
        public Trade(double profit = 0, string prixOpen="0",string prixClose="0") { Profit = profit;this.prixOpen = prixOpen;this.prixClose = prixClose; }
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

        /// <summary>
        /// Vérifie s'il existe des fichiers Excel (.xlsx) qui n'ont pas encore été migrés en JSON.
        /// </summary>
        public static bool HasOldDataToMigrate()
        {
            string dataPath = Strategie.dataFolder;
            if (!Directory.Exists(dataPath)) return false;

            var excelFiles = Directory.GetFiles(dataPath, "*.xlsx")
                                      .Where(f => !Path.GetFileName(f).StartsWith("J"))
                                      .ToList();

            foreach (var file in excelFiles)
            {
                string jsonEquivalent = Path.Combine(dataPath, Path.GetFileNameWithoutExtension(file) + ".json");
                if (!File.Exists(jsonEquivalent)) return true; // On a trouvé au moins un fichier à migrer
            }
            return false;
        }

        /// <summary>
        /// Exécute la migration et déplace les anciens fichiers dans un dossier "old_version".
        /// </summary>
        public static void ExecuteFullMigration()
        {
            ExcelPackage.License.SetNonCommercialPersonal("djiguiba"); 
            string dataPath = Strategie.dataFolder;
            string backupPath = Path.Combine(dataPath, "old_version");

            if (!Directory.Exists(backupPath)) Directory.CreateDirectory(backupPath);

            var excelFiles = Directory.GetFiles(dataPath, "*.xlsx")
                                      .Where(f => !Path.GetFileName(f).StartsWith("J"))
                                      .ToList();

            foreach (var excelPath in excelFiles)
            {
                string strategyName = Path.GetFileNameWithoutExtension(excelPath);
                try
                {
                    // 1. On effectue la migration (réutilise la méthode MigrateSingleFile précédente)
                    MigrateSingleFile(strategyName, excelPath);

                    // 2. Déplacement du fichier Excel vers le dossier de sauvegarde
                    string destExcel = Path.Combine(backupPath, Path.GetFileName(excelPath));
                    if (File.Exists(destExcel)) File.Delete(destExcel); // Évite l'erreur si déjà présent
                    File.Move(excelPath, destExcel);

                    // 3. Optionnel : On déplace aussi les métadonnées Excel si elles existent
                    string metadataFile = Path.Combine(Strategie.metadataFolder, $"{strategyName}_metadata.xlsx");
                    if (File.Exists(metadataFile))
                    {
                        string destMeta = Path.Combine(backupPath, Path.GetFileName(metadataFile));
                        if (File.Exists(destMeta)) File.Delete(destMeta);
                        File.Move(metadataFile, destMeta);
                    }
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Erreur lors de la migration de {strategyName} : {ex.Message}");
                }
            }
        }

        private static void MigrateSingleFile(string name, string excelPath)
        {
            // On crée l'objet Stratégie (le constructeur gère la création du JSON de base et l'ajout au fichier index)
            var newStrategy = new Strategie(name, "Migré depuis l'ancienne version", false);
            var data = new StrategieData { Nom = name, Description = "Migré depuis l'ancienne version" };
            var tradesList = new List<Trade>();

            using (var package = new ExcelPackage(new FileInfo(excelPath)))
            {
                var ws = package.Workbook.Worksheets[0];
                if (ws.Dimension == null) return;

                int rows = ws.Dimension.Rows;
                int cols = ws.Dimension.Columns;

                var headers = new Dictionary<int, string>();
                for (int c = 1; c <= cols; c++) headers[c] = ws.Cells[1, c].Text.Trim();

                var std = new List<string> { "ID", "PAIRE", "RESULTAT", "DATE ENTREE", "DATE SORTIE", "RR", "TYPE ORDRE", "IMAGE LTF", "IMAGE HTF", "DESCRIPTION", "PROFIT" };

                data.ChampsCustomConfig = headers.Values
                    .Where(h => !std.Contains(h.ToUpper()) && !string.IsNullOrEmpty(h))
                    .Select(h => h.ToUpper()).ToList();

                for (int r = 2; r <= rows; r++)
                {
                    var t = new Trade { strategie = name };
                    var customs = new List<ChampPersonnalise>();

                    for (int c = 1; c <= cols; c++)
                    {
                        string h = headers[c].ToUpper();
                        string val = ws.Cells[r, c].Text;

                        switch (h)
                        {
                            case "ID": t.Id = Convert.ToInt64(val) ; break;
                            case "PAIRE": t.Paire = val; break;
                            case "RESULTAT": t.Result = ParseEnum<Resultat>(val); break;
                            case "DATE ENTREE": t.DateEntree = ParseDate(val); break;
                            case "DATE SORTIE": t.DateSortie = ParseDate(val); break;
                            case "RR": t.RR = (float)ParseDouble(val); break;
                            case "TYPE ORDRE": t.TypeOrdre = val.ToUpper().Contains("BUY") ? TypeOrdre.BUY : TypeOrdre.SELL; break;
                            case "IMAGE LTF": t.ImageLtf = val; break;
                            case "IMAGE HTF": t.ImageHtf = val; break;
                            case "DESCRIPTION": t.description = val; break;
                            case "PROFIT": t.Profit = ParseDouble(val); break;
                            default:
                                if (data.ChampsCustomConfig.Contains(h))
                                    customs.Add(new ChampPersonnalise(h, val));
                                break;
                        }
                    }
                    t.ChampsPersonnalises = customs;
                    tradesList.Add(t);
                }
            }
            data.Trades = tradesList;
            newStrategy.CalculateStatistics(data); // Cette méthode sauvegarde le JSON
        }

        // Helpers statiques pour la conversion propre
        private static T ParseEnum<T>(string val) where T : struct
            {
                if (Enum.TryParse(val, true, out T res)) return res;
                return default;
            }

        private static DateTime ParseDate(string val)
        {
            if (string.IsNullOrWhiteSpace(val)) return DateTime.Now;

            // 1. Essayer le format spécifique de ton Excel (Jour/Mois/Année Heure:Minute)
            string[] formats = { "dd/MM/yyyy HH:mm", "dd/MM/yyyy HH:mm:ss", "d/M/yyyy H:mm", "dd-MM-yyyy HH:mm" };

            if (DateTime.TryParseExact(val.Trim(), formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime dt))
            {
                return dt;
            }

            // 2. Si l'extraction exacte échoue, tenter un parse générique
            if (DateTime.TryParse(val, CultureInfo.CurrentCulture, DateTimeStyles.None, out dt))
            {
                return dt;
            }

            // 3. Valeur par défaut pour éviter de faire planter la migration
            return DateTime.Now;
        }   

        private static double ParseDouble(string val)
            {
                if (string.IsNullOrEmpty(val)) return 0;
                val = val.Replace(",", ".");
                double.TryParse(val, NumberStyles.Any, CultureInfo.InvariantCulture, out double res);
                return res;
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