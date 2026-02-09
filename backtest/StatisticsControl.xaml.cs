using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;
using System.Collections.Generic;

namespace backtest
{
    public partial class StatisticsControl : UserControl
    {
        Strategie strategie;
        StatisticsView stv;
        public StatisticsControl()
        {
            InitializeComponent();
            strategie = new Strategie("aucune strategie", "", true);
            
        }

        public StatisticsControl(Strategie strategie)
        {
            InitializeComponent();
            this.strategie = strategie;
            nomText.Text = strategie.Nom;

            richTextBox.Document.Blocks.Clear();
            richTextBox.Document.Blocks.Add(new Paragraph(new Run(strategie.description)));

            // ORDRE CRITIQUE : 
            // 1. Calculer les stats (remplit les dictionnaires)
            strategie.CalculateStatistics();

            // 2. Charger le DataGrid (qui utilise les colonnes dynamiques basées sur les stats)
            LoadTradesDataGrid();

            // 3. Créer la vue graphique (qui a maintenant des données fraîches)
            stv = new StatisticsView(strategie);
            ComplexStatsHost.Children.Clear();
            ComplexStatsHost.Children.Add(stv);
        }

        public void LoadStatistics(Panel panel1, Panel panel2)
        {
            var stats = strategie.GetStatistics();
            var advanced = strategie.RetrieveStats();

            // --- Calcul du Meilleur et Pire Jour ---
            string bestDayStr = "N/A";
            string worstDayStr = "N/A";

            if (advanced.DayOfWeekStats != null && advanced.DayOfWeekStats.Count > 0)
            {
                // On trie les jours par Expectancy (le profit moyen par trade ce jour-là)
                var sortedDays = advanced.DayOfWeekStats
                    .OrderByDescending(d => d.Value.Expectancy)
                    .ToList();

                bestDayStr = TranslateDayToFrench(sortedDays.First().Key);
                worstDayStr = TranslateDayToFrench(sortedDays.Last().Key);
            }

            // --- Récupération des autres valeurs ---
            string winrate = stats.ContainsKey("Winrate") ? $"{GetSafeDouble(stats["Winrate"]):F1}%" : "0%";
            string pf = stats.ContainsKey("Profit Factor") ? $"{GetSafeDouble(stats["Profit Factor"]):F2}" : "1.00";

            // --- Mise à jour du Panel 1 (Setups) ---
            string topConfig = (advanced.BestConfigs?.Count > 0) ? advanced.BestConfigs[0].NomParametre : "N/A";
            string pireConfig = (advanced.WorstConfigs?.Count > 0) ? advanced.WorstConfigs[0].NomParametre : "N/A";

            UpdateStatCard(panel1, 0, "MEILLEUR SETUP", topConfig, Colors.Lime);
            UpdateStatCard(panel1, 1, "PIRE SETUP", pireConfig, Colors.OrangeRed);

            // --- Mise à jour du Panel 2 (Performance & Jours) ---
            UpdateStatCard(panel2, 0, "WINRATE", winrate, Colors.Lime);

            // REMPLACEMENT DEMANDÉ : Meilleur Jour
            UpdateStatCard(panel2, 1, "TOP JOUR", bestDayStr, Colors.Cyan);

            UpdateStatCard(panel2, 2, "PROFIT FACTOR", pf, Colors.Gold);

            // REMPLACEMENT DEMANDÉ : Pire Jour
            UpdateStatCard(panel2, 3, "PIRE JOUR", worstDayStr, Colors.Salmon);
        }

       
        private double GetSafeDouble(object value)
        {
            if (value == null) return 0;
            // Gère le format JSON et les différences de culture (point vs virgule)
            string s = value.ToString().Replace(",", ".");
            double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out double res);
            return res;
        }
        private void UpdateStatCard(Panel container, int index, string title, string value, Color valueColor)
        {
            if (index < container.Children.Count && container.Children[index] is Border border &&
                border.Child is StackPanel childStackPanel)
            {
                if (childStackPanel.Children[0] is TextBlock titleBlock) titleBlock.Text = title;
                if (childStackPanel.Children[1] is TextBlock valueBlock)
                {
                    valueBlock.Text = value;
                    valueBlock.Foreground = new SolidColorBrush(valueColor);
                }
            }
        }

        private void ToggleDataGrid_Click(object sender, RoutedEventArgs e)
        {
            if (ColVisual.Width != new GridLength(0))
            {
                ColVisual.Width = new GridLength(0);
                VisualContainer.Visibility = Visibility.Collapsed;
                ColDataGrid.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                ColVisual.Width = new GridLength(1, GridUnitType.Star);
                VisualContainer.Visibility = Visibility.Visible;
            }
        }

        private void ToggleVisual_Click(object sender, RoutedEventArgs e)
        {
            if (ColDataGrid.Width != new GridLength(0))
            {
                ColDataGrid.Width = new GridLength(0);
                DataGridContainer.Visibility = Visibility.Collapsed;
                ColVisual.Width = new GridLength(1, GridUnitType.Star);
            }
            else
            {
                ColDataGrid.Width = new GridLength(2, GridUnitType.Star);
                DataGridContainer.Visibility = Visibility.Visible;
            }
        }

        private void LoadTradesDataGrid()
        {
            TradesDataGrid.Columns.Clear();

            // Configuration des colonnes de base
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "PAIRE", Binding = new Binding("Paire"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "RESULT", Binding = new Binding("Result"), Width = 80 });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "RR", Binding = new Binding("RR"), Width = 60 });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "ENTRÉE", Binding = new Binding("DateEntree") { StringFormat = "dd/MM/yy" }, Width = 100 });

            // Remplacement de GetDynamicHeaders par l'analyse des stats JSON
            var advancedStats = strategie.RetrieveStats();
            if (advancedStats != null && advancedStats.PerformanceStats != null)
            {
                foreach (var header in advancedStats.PerformanceStats.Keys)
                {
                    TradesDataGrid.Columns.Add(new DataGridTextColumn
                    {
                        Header = header,
                        Binding = new Binding("ChampsPersonnalises")
                        {
                            Converter = new ChampPersonnaliseConverter(),
                            ConverterParameter = header
                        }
                    });
                }
            }

            var trades = strategie.GetTrades();
            TradesDataGrid.ItemsSource = trades;

            // Compteur de trades
            nbreTrade.Text = trades.Count.ToString();

            // Mise à jour des cartes de stats
            LoadStatistics(MyStackPanel1, MyStackPanel2);
        }

        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowDashboard();
            }
        }

        private void addTrade(object sender, MouseButtonEventArgs e)
        {
            var fenetreAjout = new AjoutTrade(this.strategie);
            if (fenetreAjout.ShowDialog() == true || fenetreAjout.DialogResult == null)
            {
                LoadTradesDataGrid();
                stv = new StatisticsView(strategie);
                ComplexStatsHost.Children.Clear();
                ComplexStatsHost.Children.Add(stv);
            }
        }

        private void TradesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TradesDataGrid.SelectedItem is Trade selectedTrade)
            {
                richTextBox.Document.Blocks.Clear();
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(selectedTrade.description)));

                // Si TradeVisualizer est présent dans ton projet
                try { TradeVisualizer.DisplayTrade(selectedTrade); } catch { }
            }
            else
            {
                richTextBox.Document.Blocks.Clear();
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(strategie.description)));
            }
        }

        private void TradesDataGrid_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && TradesDataGrid.SelectedItem is Trade tr)
            {
                if (MessageBox.Show($"Supprimer le trade {tr.Paire} du {tr.DateEntree:dd/MM} ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    this.strategie.RemoveTradeById(tr.Id);
                    LoadTradesDataGrid();
                }
            }
        }

        private void NbreTrade_MouseUp(object sender, RoutedEventArgs e)
        {
            // CalculateStatistics met déjà à jour les stats avancées dans le JSON
            strategie.CalculateStatistics();

        }
        private void DataGrid_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (!e.Handled)
            {
                e.Handled = true;

                var eventArg = new MouseWheelEventArgs(e.MouseDevice, e.Timestamp, e.Delta)
                {
                    RoutedEvent = UIElement.MouseWheelEvent,
                    Source = sender
                };

                // On utilise FrameworkElement ici pour être plus large (accepte Border, Grid, DataGrid, etc.)
                var uiElement = sender as FrameworkElement;
                var parent = uiElement?.Parent as UIElement;

                parent?.RaiseEvent(eventArg);
            }
        }
        // Helper pour avoir des noms propres en français
        private string TranslateDayToFrench(DayOfWeek day)
        {
            switch (day)
            {
                case DayOfWeek.Monday: return "Lundi";
                case DayOfWeek.Tuesday: return "Mardi";
                case DayOfWeek.Wednesday: return "Mercredi";
                case DayOfWeek.Thursday: return "Jeudi";
                case DayOfWeek.Friday: return "Vendredi";
                default: return day.ToString();
            }
        }
    }
}