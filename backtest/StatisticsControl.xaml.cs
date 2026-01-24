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

            // Description initiale
            richTextBox.Document.Blocks.Clear();
            richTextBox.Document.Blocks.Add(new Paragraph(new Run(strategie.description)));

            LoadTradesDataGrid();
        }

        public void LoadStatistics(Panel panel1, Panel panel2)
        {
            var stats = strategie.GetStatistics();

            // Gestion sécurisée des valeurs (évite le crash si stats vides)
            string topDay = stats.ContainsKey("Most Favorable Day") ? stats["Most Favorable Day"].ToString() : "N/A";
            string pireDay = stats.ContainsKey("Least Favorable Day") ? stats["Least Favorable Day"].ToString() : "N/A";
            string winrate = stats.ContainsKey("Winrate") ? $"{stats["Winrate"]:0.##}%" : "0%";
            string avgRR = stats.ContainsKey("Average RR") ? $"{stats["Average RR"]:0.##}" : "0";
            string maxRR = stats.ContainsKey("Max RR") ? $"{stats["Max RR"]:0.##}" : "0";
            string minRR = stats.ContainsKey("Min RR") ? $"{stats["Min RR"]:0.##}" : "0";

            // Mise à jour du Panel 1 (TOP/PIRE JOUR)
            UpdateStatCard(panel1, 0, "TOP JOUR", topDay, Colors.White);
            UpdateStatCard(panel1, 1, "PIRE JOUR", pireDay, Colors.White);

            // Mise à jour du Panel 2 (WINRATE, RR...)
            UpdateStatCard(panel2, 0, "WINRATE", winrate, Colors.Lime);
            UpdateStatCard(panel2, 1, "RR MOYEN", avgRR, Colors.White);
            UpdateStatCard(panel2, 2, "RR MAX", maxRR, Colors.Lime);
            UpdateStatCard(panel2, 3, "RR MIN", minRR, Colors.Red);
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

            // Ouvre la fenêtre de stats détaillées
            new Window1(this.strategie).ShowDialog();
        }
    }
}