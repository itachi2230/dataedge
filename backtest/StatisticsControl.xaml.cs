using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Data;

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

        // Changement ici : On accepte Panel1 et Panel2 qui sont maintenant des UniformGrid
        public void LoadStatistics(Panel panel1, Panel panel2)
        {
            var stats = strategie.GetStatistics();

            // Mise à jour du Panel 1 (TOP/PIRE JOUR) - 2 éléments
            UpdateStatCard(panel1, 0, "TOP JOUR", stats["Most Favorable Day"].ToString(), Colors.White);
            UpdateStatCard(panel1, 1, "PIRE JOUR", stats["Least Favorable Day"].ToString(), Colors.White);

            // Mise à jour du Panel 2 (WINRATE, RR...) - 4 éléments
            UpdateStatCard(panel2, 0, "WINRATE", $"{stats["Winrate"]:0.##}%", Colors.Lime);
            UpdateStatCard(panel2, 1, "RR MOYEN", $"{stats["Average RR"]:0.##}", Colors.White);
            UpdateStatCard(panel2, 2, "RR MAX", $"{stats["Max RR"]:0.##}", Colors.Lime);
            UpdateStatCard(panel2, 3, "RR MIN", $"{stats["Min RR"]:0.##}", Colors.Red); // Ajouté car présent dans le XAML
        }
        // On utilise 'Panel' pour être compatible avec StackPanel ET UniformGrid
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
        // Focus sur le DataGrid (cache le visuel)
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
        // Focus sur le Visuel (cache le DataGrid)
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

            // Configuration des colonnes
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "PAIRE", Binding = new Binding("Paire"), Width = new DataGridLength(1, DataGridLengthUnitType.Star) });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "RESULT", Binding = new Binding("Result"), Width = 80 });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "RR", Binding = new Binding("RR"), Width = 60 });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "ENTRÉE", Binding = new Binding("DateEntree") { StringFormat = "dd/MM/yy" }, Width = 100 });

            // Colonnes dynamiques
            foreach (var header in strategie.GetDynamicHeaders())
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

            TradesDataGrid.ItemsSource = strategie.GetTrades();

            // Tri et Compteur
            nbreTrade.Text = strategie.GetTrades().Count.ToString();

            // On passe les UniformGrid définis dans le XAML
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
            // Ouvre la fenêtre d'ajout et rafraîchit
            var fenetreAjout = new AjoutTrade(this.strategie);
            if (fenetreAjout.ShowDialog() == true || fenetreAjout.DialogResult == null)
            {
                LoadTradesDataGrid();
            }
        }
        private void TradesDataGrid_SelectionChanged_1(object sender, MouseButtonEventArgs e)
        {
            if (TradesDataGrid.SelectedItem is Trade selectedTrade)
            {
                new VisuelTrade(selectedTrade).Show();
            }
        }
        private void TradesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TradesDataGrid.SelectedItem is Trade selectedTrade)
            {
                richTextBox.Document.Blocks.Clear();
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(selectedTrade.description)));
            }
            else
            {
                // Si rien n'est sélectionné, on remet la description de la stratégie
                richTextBox.Document.Blocks.Clear();
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(strategie.description)));
            }
        }
        private void TradesDataGrid_KeyUp(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete && TradesDataGrid.SelectedItem is Trade tr)
            {
                if (MessageBox.Show($"Supprimer l'ID {tr.Id} ?", "Confirmation", MessageBoxButton.YesNo, MessageBoxImage.Warning) == MessageBoxResult.Yes)
                {
                    this.strategie.RemoveTradeById(tr.Id);
                    LoadTradesDataGrid();
                }
            }
        }
        private void NbreTrade_MouseUp(object sender, RoutedEventArgs e)
        {
            strategie.CalculateStatistics();
            strategie.CalculateStatsPlus();
            new Window1(this.strategie).ShowDialog();
        }
    }
}