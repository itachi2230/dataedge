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
using System.Collections.Generic;

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
            // DisplayHabitsInBorder(); // Décommenter si tu as l'élément 'croissance' dans ton XAML
        }

        #region CHARGEMENT DES STRATÉGIES ET JOURNAL
        public void loadStrategies()
        {
            // 1. Récupération de la liste de TOUTES les stratégies existantes
            var strategies = utils.getStrategies();
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

            // 2. Mise à jour du DataGrid
            if (TradesDataGri != null)
            {
                TradesDataGri.ItemsSource = Journal;
            }

            // 3. Calcul des statistiques globales pour le Dashboard
            Statistics stats = utils.CalculateStatistics(Journal);

            if (nbreText != null) nbreText.Text = Journal.Count.ToString();
            if (tauxBuy != null) tauxBuy.Text = stats.SuccessRateBuy.ToString() + "%";
            if (tauxSell != null) tauxSell.Text = stats.SuccessRateSell.ToString() + "%";
            if (meilleurePaire != null) meilleurePaire.Text = stats.BestPair ?? "---";
            if (PirePaire != null) PirePaire.Text = stats.WorstPair ?? "---";

            // 4. Affichage des vignettes (badges) de performance
            if (perfStrat != null)
            {
                perfStrat.Children.Clear();

                // On boucle sur TOUTES les stratégies chargées au début
                foreach (var st in strategies)
                {
                    // On cherche le profit dans les stats. 
                    // Si la stratégie n'a pas de trades, on met 0 par défaut.
                    double profit = 0;
                    if (stats.StrategyPerformance != null && stats.StrategyPerformance.ContainsKey(st.Nom))
                    {
                        profit = stats.StrategyPerformance[st.Nom];
                    }

                    // On crée le contrôle avec le nom de la stratégie et son profit (éventuellement 0)
                    var ctrl = new ControlStat(st.Nom, profit);

                    // On conserve l'événement de clic pour naviguer vers les détails
                    ctrl.MouseLeftButtonUp += (s, e) => {
                        ShowStatisticsDirect(st);
                    };

                    perfStrat.Children.Add(ctrl);
                }
            }
        }
        #endregion

        #region NAVIGATION (Dashboard <-> StatisticsControl)

        private void ShowStatisticsDirect(Strategie st)
        {
            // 1. Création du contrôle de statistiques
            var statisticsControl = new StatisticsControl(st);

            // 2. On remplace le contenu du conteneur central (ContentControl dans ton XAML)
            MainViewContainer.Content = statisticsControl;

            // 3. Animation de fondu
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
            MainViewContainer.Content = DashboardView;

            // 2. Animation de fondu
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.3)
            };
            DashboardView.BeginAnimation(OpacityProperty, fadeIn);

            // 3. Rafraîchissement des données pour refléter les changements
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
                    try { textRange.Load(fs, DataFormats.Rtf); } catch { }
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
            if (richTextBoxNotesWeeks == null) return;
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
            // Ouvre le popup pour choisir dans quelle stratégie ajouter le trade du journal
            StrategyListBox.ItemsSource = utils.getStrategies();
            StrategyListBox.DisplayMemberPath = "Nom";
            StrategyPopup.IsOpen = true;
        }

        private void AddStrategy_Click(object sender, RoutedEventArgs e)
        {
             new addStrategieWindow().ShowDialog();
           
                loadStrategies();
        }

        private void StrategyListBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StrategyListBox.SelectedItem is Strategie selectedStrategy)
            {
                StrategyPopup.IsOpen = false;
                // ModeJournal = true pour ajouter au journal (avec champ profit)
                new AjoutTrade(selectedStrategy, true).ShowDialog();
                loadStrategies();
            }
        }

        private void TradesDataGri_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (TradesDataGri.SelectedItem is Trade selectedTrade)
            {
                new newvisu(selectedTrade).Show();
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
        {
            try { InvestingCalendarBrowser.Address = "https://sslecal2.investing.com?columns=exc_flags,exc_currency,exc_importance,exc_actual,exc_forecast,exc_previous&features=datepicker,timezone&countries=110,17,25,34,32,6,37,26,5,22,39,93,14,48,10,35,105,43,38,4,36,12,72&calType=week&timeZone=55&lang=5"; } catch { }
        }

        private DateTime GetStartOfWeek(DateTime date)
        {
            int daysToSubtract = (int)date.DayOfWeek - (int)DayOfWeek.Monday;
            if (date.DayOfWeek == DayOfWeek.Sunday) daysToSubtract = 6;
            return date.AddDays(-daysToSubtract).Date;
        }
        #endregion
    }

    #region CONVERTERS
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
    #endregion
}