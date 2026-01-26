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
using backtest.Services;
using System.Text.Json;
using System.Windows.Media.Imaging;
using System.Threading.Tasks;

namespace backtest
{
    public partial class MainWindow : Window
    {
        private DateTime currentWeekStart;
        private readonly string notesFolderPath = Path.Combine(Environment.CurrentDirectory, "Notes");
        private ObservableCollection<Trade> Journal;
        SettingsView settingsView;
        private FxCloudService _cloudService = new FxCloudService();
        private readonly string _sessionFilePath;

        public MainWindow()
        {
            InitializeComponent();
            currentWeekStart = GetStartOfWeek(DateTime.Now);

            // Initialisation des données
            LoadNotesForCurrentWeek();
            LoadInvestingCalendar();
            loadStrategies();
            settingsView = new SettingsView();
            _sessionFilePath = _cloudService._sessionFilePath;
            LoadUserProfile();

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
        public void LoadUserProfile()
        {
            // 1. VERIFICATION DU FICHIER LOCAL (INSTANTANÉ)
            if (File.Exists(_sessionFilePath))
            {
                try
                {
                    string json = File.ReadAllText(_sessionFilePath);
                    var session = JsonSerializer.Deserialize<UserSessionData>(json);

                    if (session != null && session.IsLoggedIn)
                    {
                        ApplyUserInterface(session);
                        return; // On a affiché le profil local, on s'arrête là pour l'instant
                    }
                }
                catch { /* Fichier corrompu ou illisible */ }
            }

            // 2. SI PAS DE FICHIER OU PAS CONNECTÉ
            userNameText.Text = "NON_CONNECTER";txtLastSync.Text = "SYNC: Jamais";
        }
        // Cette méthode met à jour l'UI avec les données qu'on lui donne (locales ou serveurs)
        public void ApplyUserInterface(UserSessionData data)
        {
            userNameText.Text = data.FullName.ToUpper();
            if (data.LastSyncDate.HasValue)
            {
                // Calcul du temps écoulé
                TimeSpan diff = DateTime.Now - data.LastSyncDate.Value;
                string timeAgo = FormatTimeAgo(diff);
                txtLastSync.Text = $"SYNC: {timeAgo}";
            }
            else
            {
                txtLastSync.Text = "SYNC: Jamais";
            }
            // Chargement de l'image (Gestion du chemin local vs URL)
            if (!string.IsNullOrEmpty(data.ImagePath) && File.Exists(data.ImagePath))
            {
                SmallUserImg.ImageSource = new BitmapImage(new Uri(data.ImagePath));
            }
            else
            {
                // Image par défaut si le chemin n'existe plus
                SmallUserImg.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/default_user.png"));
            }
        }
        private string FormatTimeAgo(TimeSpan diff)
        {
            if (diff.TotalMinutes < 1) return "À l'instant";
            if (diff.TotalMinutes < 60) return $"Il y a {(int)diff.TotalMinutes} min";
            if (diff.TotalHours < 24) return $"Il y a {(int)diff.TotalHours} h";
            return $"Le {diff.ToString(@"dd/MM/yyyy")}";
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

        private void ShowEtude()
        {
            // 1. Création du contrôle de statistiques
            var etudew = new EtudesControl();

            // 2. On remplace le contenu du conteneur central (ContentControl dans ton XAML)
            MainViewContainer.Content = etudew;

            // 3. Animation de fondu
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            etudew.BeginAnimation(OpacityProperty, fadeIn);
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
        private async void BtnSync_Click(object sender, RoutedEventArgs e)
        {
            var btn = sender as Button;
            btn.IsEnabled = false;
            // 1. Démarrer l'animation
            Storyboard sb = (Storyboard)this.FindResource("RotationSyncAnim");
            sb.Begin();

            try
            {
                // 2. Lancer la synchronisation
                List<string> results = await _cloudService.FullSyncAsync();
                FxCloudService.Log(String.Join("\n", results));
                // 3. Analyse intelligente des résultats
                // On vérifie si une ligne contient "success" ou "mis à jour"
                int changeCount = results.Count(line => line.Contains("success") || line.Contains("mis à jour") && !line.Contains("0"));
                bool hasCriticalError = results.Any(line => line.Contains("Erreur") || line.Contains("inaccessible"));
                // Construction du message de notification
                string messageFinal;
                bool isError = hasCriticalError;
                if (hasCriticalError)
                {
                    messageFinal = "La synchronisation a échoué. Vérifiez votre connexion.";
                }
                else if (changeCount > 0)
                {
                    messageFinal = $"Synchro réussie : {changeCount} éléments synchronisés.";
                    // Mise à jour de l'UI pour la date
                    DateTime now = DateTime.Now;
                    _cloudService.UpdateLocalLastSync(now);
                    txtLastSync.Text = "SYNC: " + now.ToString("g");
                }
                else
                {
                    messageFinal = "Tout est déjà à jour.";
                }
                await ShowNotification(messageFinal, isError, false, 0.5);
            }
            catch (Exception ex)
            {
                await ShowNotification("Erreur imprévue : " + ex.Message, true, true, 0.5);
            }
            finally
            {
                // 3. Arrêter l'animation à la fin (même si erreur)
                sb.Stop();
                btn.IsEnabled = true;
            }

        }
        public void Logout()
        {
            // On efface le fichier comme dans Settings
            if (File.Exists(_sessionFilePath)) File.Delete(_sessionFilePath);

            LoadUserProfile();
            SmallUserImg.ImageSource = new BitmapImage(new Uri("pack://application:,,,/Resources/default_user.png"));
        }

        public async Task ShowNotification(string message, bool isError = false, bool keepOpen = false, double secondes = 0.2)
        {
            Color themeColor = isError ? Color.FromRgb(255, 69, 69) : Color.FromRgb(0, 255, 255);
            SolidColorBrush themeBrush = new SolidColorBrush(themeColor);

            ToastText.Text = message.ToUpper();
            CyberToast.BorderBrush = themeBrush;
            ToastGlow.Color = themeColor;
            ToastIconCircle.Stroke = themeBrush;
            ToastIconPath.Stroke = themeBrush;

            // Icone : Sablier pour le chargement, Croix pour erreur, Check pour succès
            if (keepOpen && !isError)
                ToastIconPath.Data = Geometry.Parse("M 5,5 L 15,5 L 10,10 L 5,15 L 15,15"); // Simple Sablier
            else
                ToastIconPath.Data = isError ? Geometry.Parse("M 5,5 L 13,13 M 13,5 L 5,13") : Geometry.Parse("M 4,9 L 8,13 L 14,5");

            CyberToast.Opacity = 0;
            CyberToast.Visibility = Visibility.Visible;
            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(secondes));
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (!keepOpen)
            {
                await Task.Delay(3000);
                DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.5));
                fadeOut.Completed += (s, e) => CyberToast.Visibility = Visibility.Collapsed;
                CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
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
            OverlayClose.Visibility = (OverlayClose.Visibility == Visibility.Visible) ? Visibility.Collapsed : Visibility.Visible;
        }
        private void OverlayClose_MouseDown(object sender, MouseButtonEventArgs e)
        {
            OverlayClose.Visibility = Visibility.Collapsed;
        }
        private void Button_Click_2(object sender, RoutedEventArgs e) { new addStrategieWindow().ShowDialog(); loadStrategies(); } // + Strat
        private void ButtonEtude(object sender, RoutedEventArgs e) { ShowEtude(); } 

        private void CloseButton_Click(object sender, RoutedEventArgs e) { SaveNotes(); this.Close(); }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e) => this.WindowState = WindowState.Minimized;
        private void MaximizeButton_Click(object sender, RoutedEventArgs e){if (this.WindowState == WindowState.Maximized){this.WindowState = WindowState.Normal; MaximizeBtn.Content = "▢";}else{this.WindowState = WindowState.Maximized; MaximizeBtn.Content = "❐"; }}
        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e) { if (e.LeftButton == MouseButtonState.Pressed) DragMove(); }
        private void AccountBorder_MouseDown(object sender, MouseButtonEventArgs e)
        {
            // Capture la souris pour activer le trigger visuel "IsMouseCaptured"
            ((IInputElement)sender).CaptureMouse();
            settingsView.ShowAccountPanel();
            MainViewContainer.Content = settingsView;

            // 3. Animation de fondu
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            settingsView.BeginAnimation(OpacityProperty, fadeIn);

            e.Handled = true;
        }

        // Optionnel : Relâcher la capture au MouseUp pour finir l'effet visuel
        private void AccountBorder_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (((IInputElement)sender).IsMouseCaptured)
            {
                ((IInputElement)sender).ReleaseMouseCapture();
            }
        }
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
        private void btnsettingclick(object sender, RoutedEventArgs e)
        {
            MainViewContainer.Content = settingsView;
            // 3. Animation de fondu
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            settingsView.BeginAnimation(OpacityProperty, fadeIn);

            e.Handled = true;
        }

        private async void Button_Click_1(object sender, RoutedEventArgs e)
        {
            await ShowNotification("Comming soon !!", false, false, 0.5);

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
    // Cette classe "masque" la MessageBox par défaut de System.Windows
    #endregion
}