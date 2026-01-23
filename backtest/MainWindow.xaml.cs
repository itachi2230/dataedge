using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Globalization;
using System.Windows.Data;
using System.Windows.Media.Animation;

namespace backtest
{
    public partial class MainWindow : Window
    {
        private DateTime currentWeekStart;
        private readonly string notesFolderPath = Path.Combine(Environment.CurrentDirectory, "Notes");
        private ObservableCollection<Trade> Journal;
        private HabitsManager habitsManager;

        public MainWindow()
        {
            InitializeComponent();
            currentWeekStart = GetStartOfWeek(DateTime.Now);

            // Initialisation des données
            LoadNotesForCurrentWeek();
            LoadInvestingCalendar();
            loadStrategies();

            // Initialisation des Habitudes (Checklist)
            habitsManager = new HabitsManager();
            // Note: Comme ton design n'a pas de bordure 'croissance' par défaut, 
            // je l'attache au conteneur de notes ou tu peux en créer un.
            // Pour l'instant, je commente pour éviter le crash si 'croissance' est absent du XAML
            // DisplayHabitsInBorder(); 
        }

        #region CHARGEMENT DES STRATÉGIES ET JOURNAL
        public void loadStrategies()
        {
            var strategies = utils.getStrategies();

            // Remplissage du journal global
            Journal = new ObservableCollection<Trade>();
            foreach (Strategie st in strategies)
            {
                var trades = st.GetJournal();
                if (trades != null)
                {
                    foreach (Trade tr in trades)
                    {
                        tr.strategie = st.Nom;
                        Journal.Add(tr);
                    }
                }
            }

            // Liaison au DataGrid
            TradesDataGri.ItemsSource = Journal;

            // Tri par date décroissante
            var column = TradesDataGri.Columns.FirstOrDefault(c => c.Header.ToString().Contains("Paire")); // Adapté à tes colonnes
            if (column != null)
            {
                TradesDataGri.Items.SortDescriptions.Clear();
                TradesDataGri.Items.SortDescriptions.Add(new System.ComponentModel.SortDescription("DateEntree", System.ComponentModel.ListSortDirection.Descending));
            }

            // Statistiques Dashboard
            nbreText.Text = Journal.Count.ToString();
            Statistics stats = utils.CalculateStatistics(Journal);
            tauxBuy.Text = stats.SuccessRateBuy.ToString() + "%";
            tauxSell.Text = stats.SuccessRateSell.ToString() + "%";
            meilleurePaire.Text = stats.BestPair ?? "---";
            PirePaire.Text = stats.WorstPair ?? "---";

            // Génération des vignettes de performance
            perfStrat.Children.Clear();
            foreach (var statStr in stats.StrategyPerformance)
            {
                var ctrl = new ControlStat(statStr.Key, statStr.Value);
                // Action au clic : Navigation vers StatisticsControl
                ctrl.MouseLeftButtonUp += (s, e) => {
                    var selectedStr = strategies.FirstOrDefault(x => x.Nom == statStr.Key);
                    if (selectedStr != null) ShowStatisticsDirect(selectedStr);
                };
                perfStrat.Children.Add(ctrl);
            }
        }
        #endregion

        #region NAVIGATION (Dashboard <-> StatisticsControl)

        // Cette méthode corrige l'erreur de ton StatisticsControl.xaml.cs
        private void ShowStatisticsDirect(Strategie st)
        {
            // 1. Création du contrôle de statistiques
            var statisticsControl = new StatisticsControl(st);

            // 2. On remplace le contenu du conteneur central par les stats
            // MainViewContainer est le ContentControl qui englobe les Rows 1 et 2
            MainViewContainer.Content = statisticsControl;

            // 3. Animation de fondu pour une transition fluide
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            statisticsControl.BeginAnimation(OpacityProperty, fadeIn);
        }

        public void ShowDashboard()
        {
            // 1. On remet la Grid originale (nommée DashboardView dans le XAML) 
            // comme contenu du conteneur central
            MainViewContainer.Content = DashboardView;

            // 2. Animation de fondu pour le retour au Dashboard
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.3)
            };
            DashboardView.BeginAnimation(OpacityProperty, fadeIn);

            // 3. Rafraîchissement optionnel des données
            loadStrategies();
        }
        #endregion

        #region GESTION DES NOTES
        private void LoadNotesForCurrentWeek()
        {
            if (!Directory.Exists(notesFolderPath)) Directory.CreateDirectory(notesFolderPath);
            string filePath = GetNotesFilePath(currentWeekStart);

            if (File.Exists(filePath))
            {
                TextRange textRange = new TextRange(richTextBoxNotesWeeks.Document.ContentStart, richTextBoxNotesWeeks.Document.ContentEnd);
                using (FileStream fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    textRange.Load(fs, DataFormats.Rtf);
                }
            }
            else
            {
                richTextBoxNotesWeeks.Document.Blocks.Clear();
                SaveNotes();
            }
            UpdateWeekStartDateDisplay();
        }

        private void SaveNotes()
        {
            string filePath = GetNotesFilePath(currentWeekStart);
            TextRange textRange = new TextRange(richTextBoxNotesWeeks.Document.ContentStart, richTextBoxNotesWeeks.Document.ContentEnd);
            using (FileStream fs = new FileStream(filePath, FileMode.Create, FileAccess.Write))
            {
                textRange.Save(fs, DataFormats.Rtf);
            }
        }

        private string GetNotesFilePath(DateTime weekStart) => Path.Combine(notesFolderPath, $"Notes_{weekStart:yyyyMMdd}.rtf");

        private void PreviousWeek_Click(object sender, RoutedEventArgs e) { SaveNotes(); currentWeekStart = currentWeekStart.AddDays(-7); LoadNotesForCurrentWeek(); }
        private void NextWeek_Click(object sender, RoutedEventArgs e) { SaveNotes(); currentWeekStart = currentWeekStart.AddDays(7); LoadNotesForCurrentWeek(); }

        private void UpdateWeekStartDateDisplay() { weekStartDateText.Text = $"Semaine du {currentWeekStart:dd/MM/yy}"; }
        #endregion

        #region ACTIONS BOUTONS & EVENTS
        private void AddTradeButton_Click(object sender, RoutedEventArgs e)
        {
            StrategyListBox.ItemsSource = utils.getStrategies();
            StrategyListBox.DisplayMemberPath = "Nom";
            StrategyPopup.IsOpen = true;
        }

        private void StrategyListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StrategyListBox.SelectedItem is Strategie selectedStrategy)
            {
                StrategyPopup.IsOpen = false;
                new AjoutTrade(selectedStrategy, true).ShowDialog();
                loadStrategies();
            }
        }

        // Correction de l'erreur XAML : "TradesDataGri_MouseDoubleClick"
        private void TradesDataGri_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TradesDataGri.SelectedItem is Trade selectedTrade)
            {
                new VisuelTrade(selectedTrade).Show();
            }
        }

        private void Button_Click(object sender, RoutedEventArgs e) // News Toggle
        {
            annoncesInvesting.Visibility = (annoncesInvesting.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }

        private void Button_Click_2(object sender, RoutedEventArgs e) { new addStrategieWindow().ShowDialog(); loadStrategies(); } // + Strat

        private void Button_Click_3(object sender, RoutedEventArgs e) { new EtudesView().ShowDialog(); } // Studies

        private void CloseButton_Click(object sender, RoutedEventArgs e) { SaveNotes(); this.Close(); }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }

        private void LoadInvestingCalendar()
        {InvestingCalendarBrowser.Address = "https://sslecal2.investing.com?columns=exc_flags,exc_currency,exc_importance,exc_actual,exc_forecast,exc_previous&features=datepicker,timezone&countries=110,17,25,34,32,6,37,26,5,22,39,93,14,48,10,35,105,43,38,4,36,12,72&calType=week&timeZone=55&lang=5";}
        private DateTime GetStartOfWeek(DateTime date)
        {
            int daysToSubtract = (int)date.DayOfWeek - (int)DayOfWeek.Monday;
            if (date.DayOfWeek == DayOfWeek.Sunday) daysToSubtract = 6;
            return date.AddDays(-daysToSubtract).Date;
        }
        #endregion
    }


    // Converter pour les couleurs des types d'ordre (BUY/SELL)
    public class TypeToColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                string type = value?.ToString().ToUpper();
                if (type == "BUY") return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00BFFF"));
                if (type == "SELL") return new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFCD63A6"));
                return Brushes.Gray;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
        }

        // Converter pour le Profit (Vert si +, Rouge si -)
    public class ProfitToColorConverter : IValueConverter
        {
            public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            {
                if (value != null && double.TryParse(value.ToString().Replace("$", "").Trim(), out double profit))
                {
                    return profit >= 0 ? Brushes.LightGreen : Brushes.OrangeRed;
                }
                return Brushes.White;
            }

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
        }
}