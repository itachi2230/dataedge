using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Net.Http;

namespace backtest
{
    /// <summary>
    /// Interaction logic for StatisticsControl.xaml
    /// </summary>
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
            richTextBox.AppendText(strategie.description);
            // Charger les trades et configurer le DataGrid
            LoadTradesDataGrid();

        }
        public void LoadStatistics(StackPanel stackPanel1, StackPanel stackPanel2)
        {
            // Récupérer les statistiques
            var stats = strategie.GetStatistics();

            // Mise à jour du premier StackPanel
            UpdateStatCard(stackPanel1, 0, "Jour le plus favorable", stats["Most Favorable Day"].ToString(), Colors.Lime);
            UpdateStatCard(stackPanel1, 1, "Jour le moins favorable", stats["Least Favorable Day"].ToString(), Colors.Red);
            UpdateStatCard(stackPanel1, 2, "Trades Gagnants", stats["Winning Trades"].ToString(), Colors.Lime);
            UpdateStatCard(stackPanel1, 3, "Trades Perdus", stats["Losing Trades"].ToString(), Colors.Red);

            // Mise à jour du second StackPanel
            UpdateStatCard(stackPanel2, 0, "WINRATE", $"{stats["Winrate"]:0.##}%", Colors.Lime);
            UpdateStatCard(stackPanel2, 1, "RR MOYEN", $"{stats["Average RR"]:0.##}", Colors.Red);
            UpdateStatCard(stackPanel2, 2, "RR MAX", $"{stats["Max RR"]:0.##}", Colors.Lime);
            UpdateStatCard(stackPanel2, 3, "RR MIN", $"{stats["Min RR"]:0.##}", Colors.Red);
        }

        // Méthode utilitaire pour mettre à jour une carte de statistique
        private void UpdateStatCard(StackPanel stackPanel, int index, string title, string value, Color valueColor)
        {
            if (stackPanel.Children[index] is Border border &&
                border.Child is StackPanel childStackPanel)
            {
                // Mise à jour du titre
                if (childStackPanel.Children[0] is TextBlock titleBlock)
                {
                    titleBlock.Text = title;
                }

                // Mise à jour de la valeur
                if (childStackPanel.Children[1] is TextBlock valueBlock)
                {
                    valueBlock.Text = value;
                    valueBlock.Foreground = new SolidColorBrush(valueColor);
                }
            }
        }
        private void LoadTradesDataGrid()
        {
            // Vider les colonnes du DataGrid pour une reconfiguration
            TradesDataGrid.Columns.Clear();

            // Configuration des colonnes statiques
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "ID", Binding = new Binding("Id") ,Width=DataGridLength.Auto});
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "PAIRE", Binding = new Binding("Paire"), Width = DataGridLength.Auto });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "RESULTAT", Binding = new Binding("Result"), Width = DataGridLength.Auto });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "DATE ENTREE", Binding = new Binding("DateEntree"), Width = DataGridLength.Auto });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "DATE SORTIE", Binding = new Binding("DateSortie"), Width = DataGridLength.Auto });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "RR", Binding = new Binding("RR"), Width = DataGridLength.Auto });
            TradesDataGrid.Columns.Add(new DataGridTextColumn { Header = "TYPE", Binding = new Binding("TypeOrdre"), Width = DataGridLength.Auto });

            // Configuration des colonnes dynamiques basées sur les ChampsPersonnalises
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


            // Charger les données des trades
            //List<Trade> df= strategie.GetTrades();
            TradesDataGrid.ItemsSource = strategie.GetTrades();
            var column = TradesDataGrid.Columns.FirstOrDefault(c => c.Header.ToString() == "DATE ENTREE");
            if (column != null)
            {
                TradesDataGrid.Items.SortDescriptions.Clear(); // Supprime les tris existants
                TradesDataGrid.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("DateEntree",
                    System.ComponentModel.ListSortDirection.Descending));

                // Applique visuellement l'indicateur de tri à la colonne
                column.SortDirection = System.ComponentModel.ListSortDirection.Descending;
            }
            nbreTrade.Text = TradesDataGrid.Items.Count.ToString();
            // Exemple d'appel
            LoadStatistics(MyStackPanel1, MyStackPanel2);

        }
       
        private void BackToDashboard_Click(object sender, RoutedEventArgs e)
        {
            // Accéder à la MainWindow pour appeler ShowDashboard()
            if (Application.Current.MainWindow is MainWindow mainWindow)
            {
                mainWindow.ShowDashboard();
            }
            else
            {
                MessageBox.Show(Application.Current.MainWindow.ToString());
            }
        }

        private void addTrade(object sender, MouseButtonEventArgs e)
        {
            new AjoutTrade(this.strategie).ShowDialog();
            LoadTradesDataGrid();
        }
        //afficher lees images 
        private void TradesDataGrid_SelectionChanged_1(object sender, MouseButtonEventArgs e)
        {
           
                if (TradesDataGrid.SelectedItem is Trade selectedTrade)
                {
                    VisuelTrade visuelTradeWindow = new VisuelTrade(selectedTrade);
                    visuelTradeWindow.Show();
                }
          
        }

        private void TradesDataGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TradesDataGrid.SelectedItem !=null)
            {
                // Effacer le contenu actuel
                richTextBox.Document.Blocks.Clear();

                // Ajouter le nouveau texte
                richTextBox.Document.Blocks.Add(new Paragraph(new Run(((Trade)TradesDataGrid.SelectedItem).description)));
            }
        }

        private void TradesDataGrid_KeyUp(object sender, KeyEventArgs e)
        {
            // Vérifie si la touche appuyée est "Delete"
            if (e.Key == Key.Delete)
            {
                // Vérifie si un élément est sélectionné et qu'il s'agit d'un trade
                if (TradesDataGrid.SelectedItem is Trade tr)
                {
                    // Affiche une boîte de confirmation
                    MessageBoxResult result = MessageBox.Show(
                        $"Êtes-vous sûr de vouloir supprimer le trade avec l'ID {tr.Id} ?",
                        "Confirmation de suppression",
                        MessageBoxButton.YesNo,
                        MessageBoxImage.Warning
                    );

                    // Vérifie si l'utilisateur a confirmé la suppression
                    if (result == MessageBoxResult.Yes)
                    {
                        this.strategie.RemoveTradeById(tr.Id);

                        LoadTradesDataGrid();
                    }
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
