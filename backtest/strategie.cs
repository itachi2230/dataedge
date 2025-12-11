using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Data;
using OfficeOpenXml;
using OfficeOpenXml.Table;

namespace backtest
{
    public enum Resultat
    {
        SL, TP, TR, BE, PARTIAL
    }

    public enum TypeOrdre
    {
        BUY, SELL
    }

    public class Strategie
    {
        public static string dataFolder = "data";
        public static string metadataFolder = "metadata";
        public static string etudeFolder = "Etudes";
        public static string strategies = "" + metadataFolder + "/strategies.txt";

        public string Nom { get; set; }
        public string description { get; set; }
        public string filePath;
        private string metadataFilePath;
        public string journalFilePath;
        private string journalMetadataFilePath;
        private List<string> tradeHeaders;

        public Strategie(string nom,string description="",bool temp=false)
        {
            ExcelPackage.LicenseContext = LicenseContext.NonCommercial;
            Nom = nom;
            filePath = $"{dataFolder}/{Nom}.xlsx";
            journalFilePath = $"{dataFolder}/J{Nom}.xlsx";
            this.description = description;
            tradeHeaders = new List<string>();
            metadataFilePath = $"{metadataFolder}/{Nom}_metadata.xlsx";
            journalMetadataFilePath = $"{metadataFolder}/{Nom}_Jmetadata.xlsx";
            // Création du fichier de métadonnées si non existant
            if (!Directory.Exists(metadataFolder))
            {
                Directory.CreateDirectory(metadataFolder);
            }
            if (!Directory.Exists(dataFolder))
            {
                Directory.CreateDirectory(dataFolder);
            }
            
            if (!temp) {
                if (!File.Exists(metadataFilePath))
                {
                    CreateMetadataFile();

                }
                File.AppendAllText(strategies, Nom + "%"); }

        }
        public void SupprimerStrategie()
        {

            // Suppression des fichiers liés à la stratégie
            if (File.Exists(filePath)) File.Delete(filePath);
            if (File.Exists(journalFilePath)) File.Delete(journalFilePath);
            if (File.Exists(metadataFilePath)) File.Delete(metadataFilePath);
            if (File.Exists(journalMetadataFilePath)) File.Delete(journalMetadataFilePath);

            // Mettre à jour le fichier des stratégies
            if (File.Exists(strategies))
            {
                // Lire le contenu actuel du fichier des stratégies
                string contenu = File.ReadAllText(strategies);

                // Supprimer la stratégie
                string contenuMisAJour = contenu.Replace($"{Nom}%", string.Empty);

                // Écrire les nouvelles données dans le fichier
                File.WriteAllText(strategies, contenuMisAJour);
            }
        }

        public void CalculateStatistics()
        {
            var trades = GetTrades();

            // Initialisations des statistiques de base
            int totalTrades = trades.Count;
            int winningTrades = trades.Count(t => t.Result == Resultat.TP);
            int losingTrades = trades.Count(t => t.Result == Resultat.SL);
            float winRate = totalTrades > 0 ? (float)winningTrades / totalTrades * 100 : 0;
            float avgRR = trades.Count > 0 ? trades.Average(t => t.RR) : 0;
            float maxRR = trades.Count > 0 ? trades.Max(t => t.RR) : 0;
            float minRR = trades.Count > 0 ? trades.Min(t => t.RR) : 0;

            // Analyse des jours favorables/défavorables
            var dayGroups = trades.GroupBy(t => t.DateEntree.DayOfWeek);
            var mostFavorableDay = dayGroups.OrderByDescending(g => g.Count(t => t.Result == Resultat.TP)).FirstOrDefault()?.Key.ToString();
            var leastFavorableDay = dayGroups.OrderByDescending(g => g.Count(t => t.Result == Resultat.SL)).FirstOrDefault()?.Key.ToString();

            // Analyse des champs dynamiques
            var dynamicStats = new Dictionary<string, Dictionary<string, int>>();

            foreach (var trade in trades)
            {
                foreach (var champ in trade.ChampsPersonnalises)
                {
                    if (!dynamicStats.ContainsKey(champ.Nom))
                    {
                        dynamicStats[champ.Nom] = new Dictionary<string, int>();
                    }

                    string valeur = champ.Valeur?.ToString() ?? "Inconnu";

                    if (!dynamicStats[champ.Nom].ContainsKey(valeur))
                    {
                        dynamicStats[champ.Nom][valeur] = 0;
                    }

                    dynamicStats[champ.Nom][valeur]++;
                }
            }

            // Enregistrement des statistiques dans les métadonnées
            using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets["Metadata"];

                // Statistiques de base
                worksheet.Cells[3, 1].Value = "Total Trades";
                worksheet.Cells[3, 2].Value = totalTrades;

                worksheet.Cells[4, 1].Value = "Winning Trades";
                worksheet.Cells[4, 2].Value = winningTrades;

                worksheet.Cells[5, 1].Value = "Losing Trades";
                worksheet.Cells[5, 2].Value = losingTrades;

                worksheet.Cells[6, 1].Value = "Winrate";
                worksheet.Cells[6, 2].Value = winRate;

                worksheet.Cells[7, 1].Value = "Average RR";
                worksheet.Cells[7, 2].Value = avgRR;

                worksheet.Cells[8, 1].Value = "Max RR";
                worksheet.Cells[8, 2].Value = maxRR;

                worksheet.Cells[9, 1].Value = "Min RR";
                worksheet.Cells[9, 2].Value = minRR;

                worksheet.Cells[10, 1].Value = "Most Favorable Day";
                worksheet.Cells[10, 2].Value = mostFavorableDay;

                worksheet.Cells[11, 1].Value = "Least Favorable Day";
                worksheet.Cells[11, 2].Value = leastFavorableDay;

                // Écriture des statistiques dynamiques
                int startRow = 13; // Ligne de départ pour les champs dynamiques
                foreach (var champ in dynamicStats)
                {
                    worksheet.Cells[startRow, 1].Value = champ.Key; // Nom du champ
                    int col = 2; // Colonne de départ pour les valeurs

                    foreach (var valeur in champ.Value)
                    {
                        worksheet.Cells[startRow, col].Value = $"{valeur.Key}: {valeur.Value}";
                        col++;
                    }

                    startRow++;
                }

                package.Save();
            }
        }
        public void CalculateStatsPlus()
        {
            var trades = GetTrades();

            // Dictionnaire pour stocker les performances par champ dynamique
            var performanceStats = new Dictionary<string, Dictionary<string, PerformanceStat>>();

            // Dictionnaire pour stocker les performances par jour de la semaine
            var dayOfWeekStats = new Dictionary<DayOfWeek, PerformanceStat>();

            // Dictionnaire pour stocker les performances par paire de trading
            var pairStats = new Dictionary<string, PerformanceStat>();

            // Dictionnaire pour stocker les performances par session de trading
            var sessionStats = new Dictionary<string, PerformanceStat>
    {
        { "Tokyo", new PerformanceStat() },
        { "Londres", new PerformanceStat() },
        { "New York", new PerformanceStat() }
    };

            // Dictionnaire pour stocker les performances par type d'ordre (BUY/SELL)
            var typeOrdreStats = new Dictionary<TypeOrdre, PerformanceStat>
    {
        { TypeOrdre.BUY, new PerformanceStat() },
        { TypeOrdre.SELL, new PerformanceStat() }
    };

            // Initialisation des statistiques pour chaque jour de la semaine
            foreach (DayOfWeek day in Enum.GetValues(typeof(DayOfWeek)))
            {
                dayOfWeekStats[day] = new PerformanceStat();
            }

            // Analyse des champs dynamiques pour chaque trade
            foreach (var trade in trades)
            {
                // Calcul des statistiques par jour de la semaine
                if (trade.DateEntree != null)
                {
                    var dayOfWeek = trade.DateEntree.DayOfWeek;

                    if (trade.Result == Resultat.TP)
                    {
                        dayOfWeekStats[dayOfWeek].PercentTP++;
                    }
                    else if (trade.Result == Resultat.SL)
                    {
                        dayOfWeekStats[dayOfWeek].PercentSL++;
                    }
                }

                // Calcul des statistiques par paire de trading
                if (!string.IsNullOrEmpty(trade.Paire))
                {
                    if (!pairStats.ContainsKey(trade.Paire))
                    {
                        pairStats[trade.Paire] = new PerformanceStat();
                    }

                    if (trade.Result == Resultat.TP)
                    {
                        pairStats[trade.Paire].PercentTP++;
                    }
                    else if (trade.Result == Resultat.SL)
                    {
                        pairStats[trade.Paire].PercentSL++;
                    }
                }

                // Calcul des statistiques par session de trading
                if (trade.DateEntree != null)
                {
                    var heure = trade.DateEntree.TimeOfDay;

                    if (heure >= TimeSpan.FromHours(23) || heure < TimeSpan.FromHours(8)) // Tokyo
                    {
                        if (trade.Result == Resultat.TP)
                            sessionStats["Tokyo"].PercentTP++;
                        else if (trade.Result == Resultat.SL)
                            sessionStats["Tokyo"].PercentSL++;
                    }
                    else if (heure >= TimeSpan.FromHours(8) && heure < TimeSpan.FromHours(13)) // Londres
                    {
                        if (trade.Result == Resultat.TP)
                            sessionStats["Londres"].PercentTP++;
                        else if (trade.Result == Resultat.SL)
                            sessionStats["Londres"].PercentSL++;
                    }
                    else if (heure >= TimeSpan.FromHours(13) && heure < TimeSpan.FromHours(22)) // New York
                    {
                        if (trade.Result == Resultat.TP)
                            sessionStats["New York"].PercentTP++;
                        else if (trade.Result == Resultat.SL)
                            sessionStats["New York"].PercentSL++;
                    }
                }

                // Calcul des statistiques par type d'ordre
                if (trade.Result == Resultat.TP)
                {
                    typeOrdreStats[trade.TypeOrdre].PercentTP++;
                }
                else if (trade.Result == Resultat.SL)
                {
                    typeOrdreStats[trade.TypeOrdre].PercentSL++;
                }

                // Calcul des statistiques par champs dynamiques
                foreach (var champ in trade.ChampsPersonnalises)
                {
                    if (!performanceStats.ContainsKey(champ.Nom))
                    {
                        performanceStats[champ.Nom] = new Dictionary<string, PerformanceStat>();
                    }

                    string valeur = champ.Valeur?.ToString() ?? "Inconnu";

                    if (!performanceStats[champ.Nom].ContainsKey(valeur))
                    {
                        performanceStats[champ.Nom][valeur] = new PerformanceStat();
                    }

                    if (trade.Result == Resultat.TP)
                    {
                        performanceStats[champ.Nom][valeur].PercentTP++;
                    }
                    else if (trade.Result == Resultat.SL)
                    {
                        performanceStats[champ.Nom][valeur].PercentSL++;
                    }
                }
            }

            // Calcul des pourcentages pour les statistiques
            void CalculatePercentages(Dictionary<string, PerformanceStat> stats)
            {
                foreach (var key in stats.Keys.ToList())
                {
                    var stat = stats[key];
                    var totalTrades = stat.PercentTP + stat.PercentSL;//76188542
                    if (totalTrades > 0)
                    {
                        stat.PercentTP = (stat.PercentTP / totalTrades) * 100;
                        stat.PercentSL = (stat.PercentSL / totalTrades) * 100;
                    }
                }
            }

            CalculatePercentages(sessionStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            CalculatePercentages(pairStats.ToDictionary(kvp => kvp.Key, kvp => kvp.Value));
            CalculatePercentages(typeOrdreStats.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value));

            // Enregistrement dans Excel
            using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets["DetailedStats"] ?? package.Workbook.Worksheets.Add("DetailedStats");
                worksheet.Cells.Clear();

                // Écriture des statistiques par session
                int row = 1;
                worksheet.Cells[row, 1].Value = "Session";
                worksheet.Cells[row, 2].Value = "Pourcentage TP (%)";
                worksheet.Cells[row, 3].Value = "Pourcentage SL (%)";
                row++;

                foreach (var session in sessionStats)
                {
                    worksheet.Cells[row, 1].Value = session.Key;
                    worksheet.Cells[row, 2].Value = session.Value.PercentTP;
                    worksheet.Cells[row, 3].Value = session.Value.PercentSL;
                    row++;
                }
                // Ajout des statistiques par jour de la semaine
                row++;
                worksheet.Cells[row, 1].Value = "Jour de la semaine";
                worksheet.Cells[row, 2].Value = "Pourcentage TP (%)";
                worksheet.Cells[row, 3].Value = "Pourcentage SL (%)";
                row++;
                
                foreach (var dayStat in dayOfWeekStats)
                {
                    double total = dayStat.Value.PercentTP+ dayStat.Value.PercentSL; ;
                    worksheet.Cells[row, 1].Value = dayStat.Key.ToString();
                    worksheet.Cells[row, 2].Value = Math.Round(dayStat.Value.PercentTP*100/total,2);
                    worksheet.Cells[row, 3].Value = Math.Round(dayStat.Value.PercentSL*100/total,2);
                    row++;
                }

                // Ajout des statistiques par paire de trading
                row++;
                worksheet.Cells[row, 1].Value = "Paire de trading";
                worksheet.Cells[row, 2].Value = "Pourcentage TP (%)";
                worksheet.Cells[row, 3].Value = "Pourcentage SL (%)";
                row++;

                foreach (var pairStat in pairStats)
                {
                    worksheet.Cells[row, 1].Value = pairStat.Key;
                    worksheet.Cells[row, 2].Value = pairStat.Value.PercentTP;
                    worksheet.Cells[row, 3].Value = pairStat.Value.PercentSL;
                    row++;
                }

                // Ajout des statistiques par type d'ordre
                row++;
                worksheet.Cells[row, 1].Value = "Type d'ordre";
                worksheet.Cells[row, 2].Value = "Pourcentage TP (%)";
                worksheet.Cells[row, 3].Value = "Pourcentage SL (%)";
                row++;

                foreach (var typeOrdreStat in typeOrdreStats)
                {
                    worksheet.Cells[row, 1].Value = typeOrdreStat.Key.ToString();
                    worksheet.Cells[row, 2].Value = typeOrdreStat.Value.PercentTP;
                    worksheet.Cells[row, 3].Value = typeOrdreStat.Value.PercentSL;
                    row++;
                }

                // Ajout des statistiques par champs dynamiques
                row++;
                worksheet.Cells[row, 1].Value = "Champ dynamique";
                worksheet.Cells[row, 2].Value = "Valeur";
                worksheet.Cells[row, 3].Value = "Pourcentage TP (%)";
                worksheet.Cells[row, 4].Value = "Pourcentage SL (%)";
                row++;

                foreach (var champStat in performanceStats)
                {
                    foreach (var valeurStat in champStat.Value)
                    {
                        worksheet.Cells[row, 1].Value = champStat.Key; // Nom du champ
                        worksheet.Cells[row, 2].Value = valeurStat.Key; // Valeur spécifique
                        worksheet.Cells[row, 3].Value = valeurStat.Value.PercentTP; // Pourcentage de TP
                        worksheet.Cells[row, 4].Value = valeurStat.Value.PercentSL; // Pourcentage de SL
                        row++;
                    }
                }


                package.Save();
            }
        }
        public  AdvancedStats GetAdvancedStatsFromExcel()
    {
            
    var stats = new AdvancedStats();

    // Vérifier si le fichier existe
    if (!File.Exists(metadataFilePath))
    {
        throw new FileNotFoundException("Le fichier Excel spécifié est introuvable.", metadataFilePath);
    }

    using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
    {
        var worksheet = package.Workbook.Worksheets["DetailedStats"];
        if (worksheet == null)
        {
            throw new Exception("La feuille 'DetailedStats' n'existe pas dans le fichier Excel.");
        }

        int row = 2; // Commencer après l'en-tête

        // Lecture des statistiques par session
        while (worksheet.Cells[row, 1].Value != null && worksheet.Cells[row, 1].Value.ToString() != "Jour de la semaine")
        {
            string session = worksheet.Cells[row, 1].Value.ToString();
            double percentTP = Convert.ToDouble(worksheet.Cells[row, 2].Value);
            double percentSL = Convert.ToDouble(worksheet.Cells[row, 3].Value);

            if (stats.SessionStats.ContainsKey(session))
            {
                stats.SessionStats[session].PercentTP = percentTP;
                stats.SessionStats[session].PercentSL = percentSL;
            }
            row++;
        }

        row += 2; // Sauter les en-têtes

        // Lecture des statistiques par jour de la semaine
        while (worksheet.Cells[row, 1].Value != null && worksheet.Cells[row, 1].Value.ToString() != "Paire de trading")
        {
                    string dayString = worksheet.Cells[row, 1].Value?.ToString().Trim(); // Vérifie que la valeur n'est pas nulle

                    if (Enum.TryParse(dayString, true, out DayOfWeek day))
                    {
                        double percentTPd = Convert.ToDouble(worksheet.Cells[row, 2].Value);
                        double percentSLd = Convert.ToDouble(worksheet.Cells[row, 3].Value);

                        stats.DayOfWeekStats[day] = new PerformanceStat { PercentTP = percentTPd, PercentSL = percentSLd };
                    }
                    else
                    {
                        Console.WriteLine($"Jour invalide trouvé dans l'Excel : {dayString}");
                    }
                    double percentTP = Convert.ToDouble(worksheet.Cells[row, 2].Value);
            double percentSL = Convert.ToDouble(worksheet.Cells[row, 3].Value);

            stats.DayOfWeekStats[day] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
            row++;
        }

        row += 2;

        // Lecture des statistiques par paire de trading
        while (worksheet.Cells[row, 1].Value != null && worksheet.Cells[row, 1].Value.ToString() != "Champ dynamique")
        {
            string pair = worksheet.Cells[row, 1].Value.ToString();
            double percentTP = Convert.ToDouble(worksheet.Cells[row, 2].Value);
            double percentSL = Convert.ToDouble(worksheet.Cells[row, 3].Value);

            stats.PairStats[pair] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
            row++;
        }

        row += 2;

        // Lecture des statistiques par champ dynamique
        while (worksheet.Cells[row, 1].Value != null)
        {
            string champ = worksheet.Cells[row, 1].Value.ToString();
            string valeur = worksheet.Cells[row, 2].Value.ToString();
            double percentTP = Convert.ToDouble(worksheet.Cells[row, 3].Value);
            double percentSL = Convert.ToDouble(worksheet.Cells[row, 4].Value);

            if (!stats.PerformanceStats.ContainsKey(champ))
            {
                stats.PerformanceStats[champ] = new Dictionary<string, PerformanceStat>();
            }

            stats.PerformanceStats[champ][valeur] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
            row++;
        }
    }

    return stats;
}
     
         public Dictionary<string, Dictionary<string, PerformanceStat>> GetSavedStats()
        {
            var performanceStats = new Dictionary<string, Dictionary<string, PerformanceStat>>();

            // Vérifier si le fichier existe
            if (!File.Exists(metadataFilePath))
            {
                throw new FileNotFoundException("Le fichier des métadonnées n'existe pas.");
            }

            using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets["DetailedStats"];
                if (worksheet == null)
                {
                    throw new InvalidOperationException("L'onglet 'DetailedStats' n'existe pas dans le fichier des métadonnées.");
                }

                // Lire les données
                int row = 2; // Commencer après les en-têtes
                while (worksheet.Cells[row, 1].Value != null)
                {
                    string champ = worksheet.Cells[row, 1].Text; // Nom du champ
                    string valeur = worksheet.Cells[row, 2].Text; // Valeur spécifique

                    // Lire les pourcentages
                    float percentTP = float.TryParse(worksheet.Cells[row, 3].Text, out var tp) ? tp : 0;
                    float percentSL = float.TryParse(worksheet.Cells[row, 4].Text, out var sl) ? sl : 0;

                    if (!performanceStats.ContainsKey(champ))
                    {
                        performanceStats[champ] = new Dictionary<string, PerformanceStat>();
                    }

                    performanceStats[champ][valeur] = new PerformanceStat
                    {
                        PercentTP = percentTP,
                        PercentSL = percentSL
                    };

                    row++;
                }
            }

            return performanceStats;
        }
     
        public void CalculateStatisticsJ()//pour le journql
        {
           
        } 

        public Dictionary<string, object> GetStatistics()
        {
            var statistics = new Dictionary<string, object>();

            using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets["Metadata"];

                // Lecture des statistiques de base
                statistics["Total Trades"] = worksheet.Cells[3, 2].GetValue<int>();
                statistics["Winning Trades"] = worksheet.Cells[4, 2].GetValue<int>();
                statistics["Losing Trades"] = worksheet.Cells[5, 2].GetValue<int>();
                statistics["Winrate"] = worksheet.Cells[6, 2].GetValue<float>();
                statistics["Average RR"] = worksheet.Cells[7, 2].GetValue<float>();
                statistics["Max RR"] = worksheet.Cells[8, 2].GetValue<float>();
                statistics["Min RR"] = worksheet.Cells[9, 2].GetValue<float>();

                // Traduction des jours pour les statistiques des jours favorables et défavorables
                string mostFavorableDay = worksheet.Cells[10, 2].GetValue<string>();
                string leastFavorableDay = worksheet.Cells[11, 2].GetValue<string>();

                statistics["Most Favorable Day"] = TranslateDayToFrench(mostFavorableDay);
                statistics["Least Favorable Day"] = TranslateDayToFrench(leastFavorableDay);

                // Lecture des statistiques dynamiques
                var dynamicStats = new Dictionary<string, Dictionary<string, int>>();
                int startRow = 13; // Ligne de départ pour les champs dynamiques
                while (!string.IsNullOrWhiteSpace(worksheet.Cells[startRow, 1].GetValue<string>()))
                {
                    string champ = worksheet.Cells[startRow, 1].GetValue<string>();
                    dynamicStats[champ] = new Dictionary<string, int>();

                    int col = 2;
                    while (!string.IsNullOrWhiteSpace(worksheet.Cells[startRow, col].GetValue<string>()))
                    {
                        string[] parts = worksheet.Cells[startRow, col].GetValue<string>().Split(':');
                        if (parts.Length == 2 && int.TryParse(parts[1], out int value))
                        {
                            dynamicStats[champ][parts[0].Trim()] = value;
                        }
                        col++;
                    }

                    startRow++;
                }

                statistics["Dynamic Stats"] = dynamicStats;
            }

            return statistics;
        }
        // Dictionnaire de correspondance FR → EN pour les jours de la semaine
        private static readonly Dictionary<string, DayOfWeek> JourEnAnglais = new Dictionary<string, DayOfWeek>
{
    { "Lundi", DayOfWeek.Monday },
    { "Mardi", DayOfWeek.Tuesday },
    { "Mercredi", DayOfWeek.Wednesday },
    { "Jeudi", DayOfWeek.Thursday },
    { "Vendredi", DayOfWeek.Friday },
    { "Samedi", DayOfWeek.Saturday },
    { "Dimanche", DayOfWeek.Sunday }
};
        // Méthode utilitaire pour traduire les jours en français
        private string TranslateDayToFrench(string day)
        {
            return day switch
            {
                "Monday" => "Lundi",
                "Tuesday" => "Mardi",
                "Wednesday" => "Mercredi",
                "Thursday" => "Jeudi",
                "Friday" => "Vendredi",
                "Saturday" => "Samedi",
                "Sunday" => "Dimanche",
                _ => "Inconnu"
            };
        }

        public List<string> GetDynamicHeaders()
        {
            return tradeHeaders;
        }
        // Création du fichier de métadonnées avec nom et description
        private void CreateMetadataFile()
        {
            using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets.Add("Metadata");

                worksheet.Cells[1, 1].Value = "Nom";
                worksheet.Cells[1, 2].Value = "Description";

                worksheet.Cells[2, 1].Value = Nom.ToUpper();
                worksheet.Cells[2, 2].Value = description;

                package.Save();
            }

            using (var package = new ExcelPackage(new FileInfo(journalMetadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets.Add("Metadata");

                worksheet.Cells[1, 1].Value = "Nom";
                worksheet.Cells[1, 2].Value = "Description";

                worksheet.Cells[2, 1].Value = Nom;
                worksheet.Cells[2, 2].Value = description;

                package.Save();
            }
        }
        // Mise à jour des métadonnées si elles changent
        public void UpdateMetadata(string nom, string descriptions)
        {
            Nom = nom;
            description = descriptions;

            using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets["Metadata"];
                worksheet.Cells[2, 1].Value = Nom;
                worksheet.Cells[2, 2].Value = description;

                package.Save();
            }
        }
        // Récupération des métadonnées pour une stratégie
        public void LoadMetadata()
        {
            if (File.Exists(metadataFilePath))
            {
                using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets["Metadata"];
                    Nom = worksheet.Cells[2, 1].Text;
                    description = worksheet.Cells[2, 2].Text;
                }
            }

            if (File.Exists(filePath))
            {

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];

                    // Mise à jour de tradeHeaders depuis la première ligne du fichier Excel
                    tradeHeaders.Clear();
                    for (int col = 12; col <= worksheet.Dimension.End.Column; col++)
                    {
                        var headerName = worksheet.Cells[1, col].Value?.ToString();
                        if (!string.IsNullOrEmpty(headerName))
                        {
                            tradeHeaders.Add(headerName);
                        }
                    }

                }
            }
        }
        // Méthode pour obtenir le prochain ID disponible
        public long GetNextId(string type="b")//b pour le backtest et j pour le journal
        {
            if (type == "b")
            {
                if (!File.Exists(filePath))
                    return 1;

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    long maxId = 0;

                    for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                    {
                        long currentId = Convert.ToInt64(worksheet.Cells[row, 1].Value);
                        if (currentId > maxId)
                        {
                            maxId = currentId;
                        }
                    }

                    return maxId + 1;
                }
            }
            else
            {
                if (!File.Exists(journalFilePath))
                    return 1;

                using (var package = new ExcelPackage(new FileInfo(journalFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    long maxId = 0;

                    for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                    {
                        long currentId = Convert.ToInt64(worksheet.Cells[row, 1].Value);
                        if (currentId > maxId)
                        {
                            maxId = currentId;
                        }
                    }

                    return maxId + 1;
                }
            }
        }
        public void RemoveTradeById(long tradeId)
        {
            try
            {
                // Vérifie si le fichier existe
                if (!File.Exists(filePath))
                {
                    throw new FileNotFoundException("Le fichier des trades est introuvable.");
                }

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    if (worksheet.Dimension == null)
                    {
                        throw new InvalidOperationException("Le fichier Excel est vide.");
                    }

                    int totalRows = worksheet.Dimension.End.Row;
                    bool tradeFound = false;

                    // Parcours les lignes pour trouver le Trade à supprimer
                    for (int row = 2; row <= totalRows; row++) // On commence à 2 pour ignorer les en-têtes
                    {
                        var cellValue = worksheet.Cells[row, 1].Value;

                        if (cellValue != null && long.TryParse(cellValue.ToString(), out long currentId) && currentId == tradeId)
                        {
                            worksheet.DeleteRow(row);
                            tradeFound = true;
                            break;
                        }
                    }

                    if (!tradeFound)
                    {
                        throw new KeyNotFoundException($"Le trade avec l'ID {tradeId} n'a pas été trouvé.");
                    }

                    // Sauvegarde les modifications
                    package.Save();

                    // Mise à jour des statistiques
                    CalculateStatistics();
                }
            }
            catch (Exception ex)
            {
                // Gère les exceptions (journalisation ou affichage d'un message d'erreur)
                Console.WriteLine($"Erreur lors de la suppression du trade : {ex.Message}");
            }
        }
        public void RemoveJournalById(long tradeId)
        {
            try
            {
                // Vérifie si le fichier existe
                if (!File.Exists(journalFilePath))
                {
                    throw new FileNotFoundException("Le fichier des journals est introuvable.");
                }

                using (var package = new ExcelPackage(new FileInfo(journalFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    if (worksheet.Dimension == null)
                    {
                        throw new InvalidOperationException("Le fichier Excel est vide.");
                    }

                    int totalRows = worksheet.Dimension.End.Row;
                    bool tradeFound = false;

                    // Parcours les lignes pour trouver le Trade à supprimer
                    for (int row = 2; row <= totalRows; row++) // On commence à 2 pour ignorer les en-têtes
                    {
                        var cellValue = worksheet.Cells[row, 1].Value;

                        if (cellValue != null && long.TryParse(cellValue.ToString(), out long currentId) && currentId == tradeId)
                        {
                            worksheet.DeleteRow(row);
                            tradeFound = true;
                            break;
                        }
                    }

                    if (!tradeFound)
                    {
                        throw new KeyNotFoundException($"Le trade avec l'ID {tradeId} n'a pas été trouvé.");
                    }

                    // Sauvegarde les modifications
                    package.Save();

                }
            }
            catch (Exception ex)
            {
                // Gère les exceptions (journalisation ou affichage d'un message d'erreur)
                Console.WriteLine($"Erreur lors de la suppression du trade : {ex.Message}");
            }
        }

        public void AddTrade(Trade trade)
        {
            trade.Id = GetNextId(); // Assigne l'ID suivant au trade
            try
            {
                if (!File.Exists(filePath))
                {
                    CreateNewExcelWithHeaders(trade);
                }

                using (var package = new ExcelPackage(new FileInfo(filePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    int row = worksheet.Dimension.End.Row + 1;

                    worksheet.Cells[row, 1].Value = trade.Id;
                    worksheet.Cells[row, 2].Value = trade.Paire.ToUpper();
                    worksheet.Cells[row, 3].Value = trade.Result;
                    worksheet.Cells[row, 4].Value = trade.DateEntree.ToShortDateString() + " " + trade.DateEntree.ToShortTimeString();
                    worksheet.Cells[row, 5].Value = trade.DateSortie.ToShortDateString() + " " + trade.DateSortie.ToShortTimeString();
                    worksheet.Cells[row, 6].Value = trade.RR;
                    worksheet.Cells[row, 7].Value = trade.TypeOrdre;
                    worksheet.Cells[row, 8].Value = trade.ImageLtf;
                    worksheet.Cells[row, 9].Value = trade.ImageHtf;
                    worksheet.Cells[row, 10].Value = trade.description;
                    worksheet.Cells[row, 11].Value = trade.Profit;
                    for (int i = 0; i < trade.ChampsPersonnalises.Count; i++)
                    {
                        worksheet.Cells[row, 12 + i].Value = trade.ChampsPersonnalises[i].Valeur;
                    }

                    package.Save();
                }
                // Mise à jour des statistiques
                CalculateStatistics();
            }
            catch { }
        }

        public void AddJournal(Trade trade)
        {
            trade.Id = GetNextId("j"); // Assigne l'ID suivant au trade
            try
            {
                if (!File.Exists(journalFilePath))
                {
                    CreateNewExcelWithHeaders(trade);
                }

                using (var package = new ExcelPackage(new FileInfo(journalFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets[0];
                    int row = worksheet.Dimension.End.Row + 1;

                    worksheet.Cells[row, 1].Value = trade.Id;
                    worksheet.Cells[row, 2].Value = trade.Paire.ToUpper();
                    worksheet.Cells[row, 3].Value = trade.Result;
                    worksheet.Cells[row, 4].Value = trade.DateEntree.ToShortDateString() + " " + trade.DateEntree.ToShortTimeString();
                    worksheet.Cells[row, 5].Value = trade.DateSortie.ToShortDateString() + " " + trade.DateSortie.ToShortTimeString();
                    worksheet.Cells[row, 6].Value = trade.RR;
                    worksheet.Cells[row, 7].Value = trade.TypeOrdre;
                    worksheet.Cells[row, 8].Value = trade.ImageLtf;
                    worksheet.Cells[row, 9].Value = trade.ImageHtf;
                    worksheet.Cells[row, 10].Value = trade.description;
                    worksheet.Cells[row, 11].Value = trade.Profit;
                    for (int i = 0; i < trade.ChampsPersonnalises.Count; i++)
                    {
                        worksheet.Cells[row, 12 + i].Value = trade.ChampsPersonnalises[i].Valeur;
                    }

                    package.Save();
                }
                // Mise à jour des statistiques du journAL
                CalculateStatisticsJ();
            }
            catch { }
        }

        public List<Trade> GetTrades()
        {
            List<Trade> trades = new List<Trade>();

            if (!File.Exists(filePath)) return trades;

            using (var package = new ExcelPackage(new FileInfo(filePath)))
            {
                var worksheet = package.Workbook.Worksheets[0];

                // Mise à jour de tradeHeaders depuis la première ligne du fichier Excel
                tradeHeaders.Clear();
                for (int col = 12; col <= worksheet.Dimension.End.Column; col++)
                {
                    var headerName = worksheet.Cells[1, col].Value?.ToString();
                    if (!string.IsNullOrEmpty(headerName))
                    {
                        tradeHeaders.Add(headerName);
                    }
                }

                // Lecture des trades à partir de la deuxième ligne
                for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                {
                    var trade = new Trade
                    {
                        Id = Convert.ToInt64(worksheet.Cells[row, 1].Value),
                        Paire = worksheet.Cells[row, 2].Value.ToString(),
                        Result = (Resultat)Enum.Parse(typeof(Resultat), worksheet.Cells[row, 3].Value.ToString()),
                        DateEntree = DateTime.Parse(worksheet.Cells[row, 4].Value.ToString()),
                        DateSortie = DateTime.Parse(worksheet.Cells[row, 5].Value.ToString()),
                        RR = float.Parse(worksheet.Cells[row, 6].Value.ToString()),
                        TypeOrdre = (TypeOrdre)Enum.Parse(typeof(TypeOrdre), worksheet.Cells[row, 7].Value.ToString()),
                        ImageLtf = worksheet.Cells[row, 8].Value.ToString(),
                        ImageHtf = worksheet.Cells[row, 9].Value.ToString(),
                        description = worksheet.Cells[row, 10].Value.ToString(),
                        Profit = double.Parse(worksheet.Cells[row, 11].Value.ToString())
                    };

                    trade.ChampsPersonnalises = new List<ChampPersonnalise>();

                    for (int col = 12; col <= worksheet.Dimension.End.Column; col++)
                    {
                        int headerIndex = col - 12;
                        if (headerIndex < tradeHeaders.Count)
                        {
                            var champValue = worksheet.Cells[row, col].Value;
                            var champ = new ChampPersonnalise
                            {
                                Nom = tradeHeaders[headerIndex],
                                Valeur = champValue
                            };
                            trade.ChampsPersonnalises.Add(champ);
                        }
                    }

                    trades.Add(trade);
                }
            }
            return trades;
        }
        public List<Trade> GetJournal()//pour le journal
        {
            List<Trade> trades = new List<Trade>();

            if (!File.Exists(journalFilePath)) return trades;

            using (var package = new ExcelPackage(new FileInfo(journalFilePath)))
            {
                var worksheet = package.Workbook.Worksheets[0];

                // Mise à jour de tradeHeaders depuis la première ligne du fichier Excel
                tradeHeaders.Clear();
                for (int col = 12; col <= worksheet.Dimension.End.Column; col++)
                {
                    var headerName = worksheet.Cells[1, col].Value?.ToString();
                    if (!string.IsNullOrEmpty(headerName))
                    {
                        tradeHeaders.Add(headerName);
                    }
                }

                // Lecture des trades à partir de la deuxième ligne
                for (int row = 2; row <= worksheet.Dimension.End.Row; row++)
                {
                    var trade = new Trade
                    {
                        Id = Convert.ToInt64(worksheet.Cells[row, 1].Value),
                        Paire = worksheet.Cells[row, 2].Value.ToString(),
                        Result = (Resultat)Enum.Parse(typeof(Resultat), worksheet.Cells[row, 3].Value.ToString()),
                        DateEntree = DateTime.Parse(worksheet.Cells[row, 4].Value.ToString()),
                        DateSortie = DateTime.Parse(worksheet.Cells[row, 5].Value.ToString()),
                        RR = float.Parse(worksheet.Cells[row, 6].Value.ToString()),
                        TypeOrdre = (TypeOrdre)Enum.Parse(typeof(TypeOrdre), worksheet.Cells[row, 7].Value.ToString()),
                        ImageLtf = worksheet.Cells[row, 8].Value.ToString(),
                        ImageHtf = worksheet.Cells[row, 9].Value.ToString(),
                        description = worksheet.Cells[row, 10].Value.ToString(),
                        Profit = double.Parse(worksheet.Cells[row, 11].Value.ToString())
                    };

                    trade.ChampsPersonnalises = new List<ChampPersonnalise>();

                    for (int col = 12; col <= worksheet.Dimension.End.Column; col++)
                    {
                        int headerIndex = col - 12;
                        if (headerIndex < tradeHeaders.Count)
                        {
                            var champValue = worksheet.Cells[row, col].Value;
                            var champ = new ChampPersonnalise
                            {
                                Nom = tradeHeaders[headerIndex],
                                Valeur = champValue
                            };
                            trade.ChampsPersonnalises.Add(champ);
                        }
                    }

                    trades.Add(trade);
                }
            }
            return trades;
        }

        private void CreateNewExcelWithHeaders(Trade trade)
        {
            try
            {//excel pour le backtest
                    using (var package = new ExcelPackage(new FileInfo(filePath)))
                    {
                        var worksheet = package.Workbook.Worksheets.Add("Trades");

                        worksheet.Cells[1, 1].Value = "ID";
                        worksheet.Cells[1, 2].Value = "Paire";
                        worksheet.Cells[1, 3].Value = "Resultat";
                        worksheet.Cells[1, 4].Value = "Date Entree";
                        worksheet.Cells[1, 5].Value = "Date Sortie";
                        worksheet.Cells[1, 6].Value = "RR";
                        worksheet.Cells[1, 7].Value = "Type Ordre";
                        worksheet.Cells[1, 8].Value = "Image LTF";
                        worksheet.Cells[1, 9].Value = "Image HTF";
                        worksheet.Cells[1, 10].Value = "description";
                        worksheet.Cells[1, 11].Value = "Profit";
                        if (trade.ChampsPersonnalises != null)
                        {
                            for (int i = 0; i < trade.ChampsPersonnalises.Count; i++)
                            {
                                worksheet.Cells[1, 12 + i].Value = trade.ChampsPersonnalises[i].Nom;
                                tradeHeaders.Add(trade.ChampsPersonnalises[i].Nom);
                            }
                        }
                        worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                        package.Save();
                    }
                    //ensuite pour le journal
                using (var package = new ExcelPackage(new FileInfo(journalFilePath)))
                {
                    var worksheet = package.Workbook.Worksheets.Add("Trades");

                    worksheet.Cells[1, 1].Value = "ID";
                    worksheet.Cells[1, 2].Value = "Paire";
                    worksheet.Cells[1, 3].Value = "Resultat";
                    worksheet.Cells[1, 4].Value = "Date Entree";
                    worksheet.Cells[1, 5].Value = "Date Sortie";
                    worksheet.Cells[1, 6].Value = "RR";
                    worksheet.Cells[1, 7].Value = "Type Ordre";
                    worksheet.Cells[1, 8].Value = "Image LTF";
                    worksheet.Cells[1, 9].Value = "Image HTF";
                    worksheet.Cells[1, 10].Value = "description";
                    worksheet.Cells[1, 11].Value = "Profit";
                    if (trade.ChampsPersonnalises != null)
                    {
                        for (int i = 0; i < trade.ChampsPersonnalises.Count; i++)
                        {
                            worksheet.Cells[1, 12 + i].Value = trade.ChampsPersonnalises[i].Nom;
                            tradeHeaders.Add(trade.ChampsPersonnalises[i].Nom);
                        }
                    }
                    worksheet.Cells[worksheet.Dimension.Address].AutoFitColumns();
                    package.Save();
                }

            }
            catch { }
        }
        public AdvancedStats RetrieveStats()
        {
            var stats = new AdvancedStats();

            using (var package = new ExcelPackage(new FileInfo(metadataFilePath)))
            {
                var worksheet = package.Workbook.Worksheets["DetailedStats"];
                if (worksheet == null)
                    throw new Exception("La feuille de statistiques détaillées n'existe pas.");

                int row = 2; // Commencer après l'en-tête

                // Lecture des statistiques par session
                while (!string.IsNullOrEmpty(worksheet.Cells[row, 1].Text))
                {
                    string session = worksheet.Cells[row, 1].Text;
                    double percentTP = double.Parse(worksheet.Cells[row, 2].Text);
                    double percentSL = double.Parse(worksheet.Cells[row, 3].Text);
                    stats.SessionStats[session] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
                    row++;
                }

                // Lecture des statistiques par jour de la semaine
                row += 2; // Sauter les en-têtes
                while (!string.IsNullOrEmpty(worksheet.Cells[row, 1].Text))
                {
                    DayOfWeek day = (DayOfWeek)Enum.Parse(typeof(DayOfWeek), worksheet.Cells[row, 1].Text);
                    double percentTP = double.Parse(worksheet.Cells[row, 2].Text);
                    double percentSL = double.Parse(worksheet.Cells[row, 3].Text);
                    stats.DayOfWeekStats[day] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
                    row++;
                }

                // Lecture des statistiques par paire de trading
                row += 2;
                while (!string.IsNullOrEmpty(worksheet.Cells[row, 1].Text))
                {
                    string pair = worksheet.Cells[row, 1].Text;
                    double percentTP = double.Parse(worksheet.Cells[row, 2].Text);
                    double percentSL = double.Parse(worksheet.Cells[row, 3].Text);
                    stats.PairStats[pair] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
                    row++;
                }

                // Lecture des statistiques par type d'ordre
                row += 2;
                while (!string.IsNullOrEmpty(worksheet.Cells[row, 1].Text))
                {
                    TypeOrdre typeOrdre = (TypeOrdre)Enum.Parse(typeof(TypeOrdre), worksheet.Cells[row, 1].Text);
                    double percentTP = double.Parse(worksheet.Cells[row, 2].Text);
                    double percentSL = double.Parse(worksheet.Cells[row, 3].Text);
                    stats.TypeOrdreStats[typeOrdre] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
                    row++;
                }

                // Lecture des statistiques par champs dynamiques
                row += 2;
                while (!string.IsNullOrEmpty(worksheet.Cells[row, 1].Text))
                {
                    string champ = worksheet.Cells[row, 1].Text;
                    string valeur = worksheet.Cells[row, 2].Text;
                    double percentTP = double.Parse(worksheet.Cells[row, 3].Text);
                    double percentSL = double.Parse(worksheet.Cells[row, 4].Text);

                    if (!stats.PerformanceStats.ContainsKey(champ))
                        stats.PerformanceStats[champ] = new Dictionary<string, PerformanceStat>();

                    stats.PerformanceStats[champ][valeur] = new PerformanceStat { PercentTP = percentTP, PercentSL = percentSL };
                    row++;
                }
            }

            return stats;
        }

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
        public List<ChampPersonnalise> ChampsPersonnalises { get; set; } 
        public double Profit { get; set;  }
        public string strategie { get; set; }
        // Propriété calculée pour obtenir un DateTime
        public Trade(double profit=0)
        {
            ChampsPersonnalises = new List<ChampPersonnalise>();
            Profit = profit;
            strategie="";
        }
    }

    public class ChampPersonnalise
    {
        public string Nom { get; set; }
        public object Valeur { get; set; }

        public ChampPersonnalise() { }
        public ChampPersonnalise(string nom, object valeur = null)
        {
            Nom = nom.ToUpper();
            Valeur = valeur;
        }
    }
    public class ChampPersonnaliseConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is List<ChampPersonnalise> champs && parameter is string champNom)
            {
                var champ = champs.FirstOrDefault(c => c.Nom == champNom);
                return champ?.Valeur ?? string.Empty;
            }
            return string.Empty;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (parameter is string champNom && value is string valeur)
            {
                // Rechercher et mettre à jour la liste des ChampsPersonnalises
                var champs = new List<ChampPersonnalise>();

                var champ = champs.FirstOrDefault(c => c.Nom == champNom);
                if (champ != null)
                {
                    champ.Valeur = valeur;
                }
                else
                {
                    champs.Add(new ChampPersonnalise(champNom, valeur));
                }

                return champs;
            }
            return null;

        }
    }

    public class utils
    {
        public static List<Strategie> getStrategies()
        {
            
            List<Strategie> str = new List<Strategie>();
            try
            {
                foreach (string nom in (File.ReadAllText(Strategie.strategies)).Split('%'))
                {
                    if (nom != "")
                    {
                        Strategie strate = new Strategie(nom, "", true);
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

            // 1. Total profit et perte
            stats.TotalProfit = trades.Where(t => t.Profit > 0).Sum(t => t.Profit);
            stats.TotalLoss = trades.Where(t => t.Profit < 0).Sum(t => t.Profit);

            // 2. Meilleure et pire paire
            var pairPerformance = trades
                .GroupBy(t => t.Paire)
                .Select(g => new { Paire = g.Key, Profit = g.Sum(t => t.Profit) })
                .OrderByDescending(p => p.Profit)
                .ToList();
            stats.BestPair = pairPerformance.FirstOrDefault()?.Paire ?? "Aucune";
            stats.WorstPair = pairPerformance.LastOrDefault()?.Paire ?? "Aucune";

            // 3. Taux de succès pour BUY et SELL
            var buyTrades = trades.Where(t => t.TypeOrdre == TypeOrdre.BUY);
            var sellTrades = trades.Where(t => t.TypeOrdre == TypeOrdre.SELL);

            stats.SuccessRateBuy = buyTrades.Any()
                 ? Math.Round(buyTrades.Count(t => t.Profit > 0) / (double)buyTrades.Count() * 100)
                 : 0;

            stats.SuccessRateSell = sellTrades.Any()
                ? Math.Round(sellTrades.Count(t => t.Profit > 0) / (double)sellTrades.Count() * 100)
                : 0;

            // 4. Performance par stratégie
            stats.StrategyPerformance = trades
                .GroupBy(t => t.strategie)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Profit));

            // 5. Performance pour les 7 jours de la semaine
            stats.WeeklyPerformance = trades
                .GroupBy(t => t.DateEntree.DayOfWeek)
                .ToDictionary(g => g.Key, g => g.Sum(t => t.Profit));

            // 6. Meilleur trade réalisé
            stats.BestTrade = trades.OrderByDescending(t => t.Profit).FirstOrDefault();

            return stats;
        }
    }
    public class AdvancedStats
    {
        public Dictionary<string, Dictionary<string, PerformanceStat>> PerformanceStats { get; set; }
        public Dictionary<DayOfWeek, PerformanceStat> DayOfWeekStats { get; set; }
        public Dictionary<string, PerformanceStat> PairStats { get; set; }
        public Dictionary<string, PerformanceStat> SessionStats { get; set; }
        public Dictionary<TypeOrdre, PerformanceStat> TypeOrdreStats { get; set; } // Ajout des stats par type d'ordre

        public AdvancedStats()
        {
            PerformanceStats = new Dictionary<string, Dictionary<string, PerformanceStat>>();
            DayOfWeekStats = new Dictionary<DayOfWeek, PerformanceStat>();
            PairStats = new Dictionary<string, PerformanceStat>();
            SessionStats = new Dictionary<string, PerformanceStat>
        {
            { "Tokyo", new PerformanceStat() },
            { "Londres", new PerformanceStat() },
            { "New York", new PerformanceStat() }
        };
            TypeOrdreStats = new Dictionary<TypeOrdre, PerformanceStat> // Initialisation
        {
            { TypeOrdre.BUY, new PerformanceStat() },
            { TypeOrdre.SELL, new PerformanceStat() }
        };
        }
    }

}
