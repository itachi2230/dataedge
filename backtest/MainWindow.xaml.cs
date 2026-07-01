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
        private Point _startPoint;
        private bool _isDragging = false;
        Chart chart ;
        private FxCloudService _cloudService = new FxCloudService();
        private readonly string _sessionFilePath;

        public MainWindow()
        {
            // À faire UNE SEULE FOIS au lancement de l'app
            
            InitializeComponent();
            currentWeekStart = GetStartOfWeek(DateTime.Now);

            // Initialisation des données
            LoadNotesForCurrentWeek();
            LoadInvestingCalendar();
            
            settingsView = new SettingsView();
            _sessionFilePath = _cloudService._sessionFilePath;
            LoadUserProfile();
            chart = new Chart();
            if (utils.HasOldDataToMigrate())
            {
                var result = MessageBox.Show(
                    "Des données de l'ancienne version (.xlsx) ont été détectées. Voulez-vous les migrer vers le nouveau format JSON ?\n\nLes fichiers originaux seront déplacés dans le dossier 'data/old_version'.",
                    "Migration de données",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);

                if (result == MessageBoxResult.Yes)
                {
                    utils.ExecuteFullMigration();
                    MessageBox.Show("Migration terminée avec succès !", "Info", MessageBoxButton.OK, MessageBoxImage.Information);

                    // Optionnel : Rafraîchir ta liste de stratégies après migration
                    // LoadStrategiesList(); 
                }
            }
            loadStrategies();
            // À mettre dans le constructeur après InitializeComponent()
            ContextMenu journalMenu = new ContextMenu();

            // Option MODIFIER
            MenuItem editTrade = new MenuItem { Header = "Modifier ce trade", Foreground = Brushes.Cyan };
            editTrade.Click += (s, e) => {
                if (TradesDataGri.SelectedItem is Trade selectedTrade)
                {
                    // On ouvre la fenêtre d'édition avec les données du trade
                    var strat = new Strategie(selectedTrade.strategie);
                    var editWin = new AjoutTrade(strat, selectedTrade,true);
                    editWin.ShowDialog();
                    loadStrategies();
                }
            };

            // Option SUPPRIMER
            MenuItem deleteTrade = new MenuItem { Header = "Supprimer ce trade", Foreground = Brushes.OrangeRed };
            deleteTrade.Click += (s, e) => {
                if (TradesDataGri.SelectedItem is Trade selectedTrade)
                {
                    if (MessageBox.Show("Supprimer ?", "Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        new Strategie(selectedTrade.strategie).RemoveJournalById(selectedTrade.Id);
                        loadStrategies();
                    }
                }
            };

            journalMenu.Items.Add(editTrade);
            journalMenu.Items.Add(deleteTrade);
            TradesDataGri.ContextMenu = journalMenu;
            PerformSystemHandshake();
            UpdateAllSentimentIndices();
        }

        #region CHARGEMENT DES STRATÉGIES ET JOURNAL ET NOTIFS

        // Événement au clic sur le graphique Fear & Greed
       
        private async Task UpdateAllSentimentIndices()
        {
            // --- 1. MISE À JOUR MARCHÉ US (CNN) ---
            int usIndex = await NetworkUtils.GetFearAndGreedIndexAsync();
            fearGreedValeur.Text = usIndex == -1 ? "??" : usIndex.ToString();

            Color usColor = GetSentimentColorAndLabel(usIndex, out string usLabel);
            fearGreedLabel.Text = usLabel;
            fearGreedLabel.Foreground = new SolidColorBrush(usColor);
            CenterArc.Stroke = new SolidColorBrush(usColor);
            CenterArc.Fill = new SolidColorBrush(Color.FromArgb(40, usColor.R, usColor.G, usColor.B));

            if (usIndex != -1)
            {
                NeedleRotation.Angle = (usIndex * 1.8) - 90;
            }

            // --- 2. MISE À JOUR MARCHÉ CRYPTO (Même logique et design) ---
            int cryptoIndex = await NetworkUtils.GetCryptoFearAndGreedIndexAsync();
            cryptoFGValeur.Text = cryptoIndex == -1 ? "??" : cryptoIndex.ToString();

            Color cryptoColor = GetSentimentColorAndLabel(cryptoIndex, out string cryptoLabel);
            cryptoFGLabel.Text = cryptoLabel;
            cryptoFGLabel.Foreground = new SolidColorBrush(cryptoColor);
            CenterArcCrypto.Stroke = new SolidColorBrush(cryptoColor);
            CenterArcCrypto.Fill = new SolidColorBrush(Color.FromArgb(40, cryptoColor.R, cryptoColor.G, cryptoColor.B));

            if (cryptoIndex != -1)
            {
                NeedleRotationCrypto.Angle = (cryptoIndex * 1.8) - 90;
            }
        }
        // Fonction utilitaire partagée pour la traduction française et les couleurs
        private Color GetSentimentColorAndLabel(int value, out string label)
        {
            if (value == -1) { label = "..."; return Color.FromRgb(100, 110, 120); }
            if (value <= 25) { label = "Peur Extrême"; return Color.FromRgb(242, 54, 69); }
            if (value <= 45) { label = "Peur"; return Color.FromRgb(255, 106, 2); }
            if (value <= 55) { label = "Neutre"; return Color.FromRgb(220, 220, 220); }
            if (value <= 75) { label = "Cupidité"; return Color.FromRgb(112, 168, 0); }
            label = "Cupidité Extr."; return Color.FromRgb(0, 150, 0);
        }
        private async void PerformSystemHandshake()
        {
            string currentVersion = "1.0.0";
            string user = userNameText.Text ?? "Guest";
            // Chemin du fichier de sauvegarde
            string notifFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_notif.txt");
            var notificationsList = new List<NotificationItem>();

            try
            {
                string cloudStatus = await _cloudService.GetCloudStatusAsync();
                if (cloudStatus.StartsWith("OFFLINE")) { /* ... gestion erreur ... */ return; }

                var handshake = await _cloudService.CheckSoftwareStatusAsync(currentVersion, user);

                if (handshake != null)
                {
                    if (handshake.IsLocked) { Application.Current.Shutdown(); return; }

                    // --- 1. RÉCUPÉRATION DE L'ID SAUVEGARDÉ ---
                    int savedId = 0;
                    if (File.Exists(notifFilePath))
                    {
                        int.TryParse(File.ReadAllText(notifFilePath), out savedId);
                    }

                    // --- 2. CONSTRUCTION DE LA LISTE (FLUX COMPLET) ---
                    bool hasSystemMsg = handshake.SystemMessage != null && !string.IsNullOrEmpty(handshake.SystemMessage.Body);
                    if (hasSystemMsg)
                    {
                        notificationsList.Add(new NotificationItem
                        {
                            Title = handshake.SystemMessage.Title?.ToUpper() ?? "SYSTEM",
                            Body = handshake.SystemMessage.Body,
                            Date = "IMPORTANT",
                            Color = handshake.SystemMessage.Type == "danger" ? Brushes.OrangeRed : Brushes.Cyan
                        });
                    }

                    if (handshake.PushNotifications != null)
                    {
                        foreach (var notif in handshake.PushNotifications)
                        {
                            notificationsList.Add(new NotificationItem
                            {
                                Id = notif.Id,
                                Title = notif.Title?.ToUpper() ?? "ANNONCE",
                                Body = notif.Content,
                                Date = notif.Date,
                                Color = Brushes.White
                            });
                        }
                    }

                    // --- 3. CALCUL DU COMPTEUR (UNIQUEMENT LES NOUVEAUX) ---
                    int newNotifsCount = 0;

                    // On compte 1 si message système présent
                    if (hasSystemMsg) newNotifsCount++;

                    // On compte uniquement les push dont l'ID > savedId
                    if (handshake.PushNotifications != null)
                    {
                        newNotifsCount += handshake.PushNotifications.Count(n => n.Id > savedId);
                    }

                    // --- 4. MISE À JOUR UI ---
                    ItemsNotif.ItemsSource = notificationsList;

                    if (newNotifsCount > 0)
                    {
                        BadgeNotif.Visibility = Visibility.Visible;
                        txtNotifCount.Text = newNotifsCount.ToString();
                    }
                    else
                    {
                        BadgeNotif.Visibility = Visibility.Collapsed;
                    }

                    // Gestion version...
                    txtLastSync.Text = (handshake.LatestVersion != currentVersion) ? $"MAJ: {handshake.LatestVersion}" : $"SYNC: {DateTime.Now:HH:mm}";
                }
            }
            catch (Exception ex) { FxCloudService.Log($"Erreur: {ex.Message}"); }
        }
        // Events pour la cloche
        private void BtnNotifications_Click(object sender, RoutedEventArgs e)
        {
            PopupNotif.IsOpen = !PopupNotif.IsOpen;
            // Une fois ouvert, on peut cacher le badge
            BadgeNotif.Visibility = Visibility.Collapsed;
        }

        private void CloseNotif_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // 1. On récupère la liste liée à l'UI
                var currentItems = ItemsNotif.ItemsSource as List<NotificationItem>;

                // 2. On cherche l'ID le plus récent (on ignore le message système s'il n'a pas d'ID)
                // On prend le premier élément qui a un ID positif (ce sera la notif push la plus récente)
                var latestPush = currentItems?.FirstOrDefault(x => x.Id > 0);

                if (latestPush != null)
                {
                    string notifFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "last_notif.txt");

                    // 3. On écrase le fichier avec le nouvel ID
                    File.WriteAllText(notifFilePath, latestPush.Id.ToString());
                }

                // 4. UI
                BadgeNotif.Visibility = Visibility.Collapsed;
                PopupNotif.IsOpen = false;
            }
            catch (Exception ex)
            {
                FxCloudService.Log($"Erreur sauvegarde lecture notif: {ex.Message}");
                PopupNotif.IsOpen = false;
            }
        }
        // Ouvre le popup de support
        private void BtnSupport_Click(object sender, RoutedEventArgs e)
        {
            txtSupportStatus.Text = "";
            PopupSupport.IsOpen = true;
        }

        // Gère l'envoi du message
        private async void BtnSendSupport_Click(object sender, RoutedEventArgs e)
        {
            string contact = txtSupportContact.Text.Trim();
            string message = txtSupportMessage.Text.Trim();
            string user = userNameText.Text;

            if (string.IsNullOrEmpty(message))
            {
                txtSupportStatus.Text = "Veuillez écrire un message.";
                txtSupportStatus.Foreground = Brushes.OrangeRed;
                return;
            }

            // Préparation de l'UI pendant l'envoi
            BtnSendSupport.IsEnabled = false;
            txtSupportStatus.Text = "Transmission en cours...";
            txtSupportStatus.Foreground = Brushes.Cyan;

            // On concatène le contact au message pour le serveur ou on utilise le champ 'type'
            // Ici on envoie le contact dans le paramètre 'type' pour que Symfony le logge bien
            bool success = await _cloudService.SendSupportMessageAsync(contact, message, user);

            if (success)
            {
                txtSupportStatus.Text = "Message transmis avec succès !";
                txtSupportStatus.Foreground = Brushes.LimeGreen;

                // On vide les champs après un court délai et on ferme
                await Task.Delay(1500);
                txtSupportContact.Clear();
                txtSupportMessage.Clear();
                PopupSupport.IsOpen = false;
            }
            else
            {
                txtSupportStatus.Text = "Erreur de connexion au serveur.";
                txtSupportStatus.Foreground = Brushes.OrangeRed;
            }

            BtnSendSupport.IsEnabled = true;
        }
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

            // 2. Mise à jour du DataGrid (toujours avec le Journal)
            if (TradesDataGri != null)
            {
                TradesDataGri.ItemsSource = Journal;
            }

            // 3. Calcul des statistiques globales pour le Dashboard (toujours avec le Journal)
            Statistics stats = utils.CalculateStatistics(Journal);

            if (nbreText != null) nbreText.Text = Journal.Count.ToString();
            if (tauxBuy != null) tauxBuy.Text = stats.SuccessRateBuy.ToString() + "%";
            if (tauxSell != null) tauxSell.Text = stats.SuccessRateSell.ToString() + "%";
            if (meilleurePaire != null) meilleurePaire.Text = stats.BestPair ?? "---";
            if (PirePaire != null) PirePaire.Text = stats.WorstPair ?? "---";

            // 4. Affichage des vignettes (badges) de performance dans le tab BACKTEST
            //    Utilise les données de backtest (Trades) et les StatsBasiques déjà calculées
            if (perfStrat != null)
            {
                perfStrat.Children.Clear();
                foreach (var st in strategies)
                {
                    var data = st.LoadData();
                    var backtestTrades = data.Trades;
                    var statsBasiques = data.StatsBasiques;

                    int tradeCount = backtestTrades?.Count ?? 0;

                    double winrate = 0;
                    if (statsBasiques != null && statsBasiques.ContainsKey("Winrate"))
                    {
                        var wr = statsBasiques["Winrate"];
                        if (wr is System.Text.Json.JsonElement je) winrate = je.GetDouble();
                        else winrate = Convert.ToDouble(wr);
                    }

                    double profitFactor = 0;
                    if (statsBasiques != null && statsBasiques.ContainsKey("Profit Factor"))
                    {
                        var pf = statsBasiques["Profit Factor"];
                        if (pf is System.Text.Json.JsonElement je) profitFactor = je.GetDouble();
                        else profitFactor = Convert.ToDouble(pf);
                    }

                    // Calcul du profit net à partir des trades de backtest
                    double profit = 0;
                    if (backtestTrades != null && backtestTrades.Count > 0)
                    {
                        profit = backtestTrades.Sum(t => t.Profit);
                    }

                    var ctrl = new ControlStat(st.Nom, profit, tradeCount, winrate, profitFactor);

                    // --- MENU CONTEXTUEL ---
                    ContextMenu cm = new ContextMenu();

                    // Option MODIFIER
                    MenuItem editMenu = new MenuItem { Header = "Modifier les infos", Foreground = Brushes.Cyan };
                    editMenu.Click += (s, e) => {
                        // Ici, on ouvre une fenêtre de saisie (ex: EditStratWindow)
                        var editWin = new addStrategieWindow(st);
                        if (editWin.ShowDialog() == true)
                        {
                            loadStrategies(); // Rafraîchir tout
                        }
                    };

                    // Option SUPPRIMER
                    MenuItem deleteMenu = new MenuItem { Header = "Supprimer la stratégie", Foreground = Brushes.Red };
                    deleteMenu.Click += (s, e) => {
                        if (MessageBox.Show($"Supprimer {st.Nom} ?", "Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                        {
                            st.SupprimerStrategie();
                            loadStrategies();
                        }
                    };

                    cm.Items.Add(editMenu);
                    cm.Items.Add(new Separator()); // Petite ligne de séparation
                    cm.Items.Add(deleteMenu);

                    ctrl.ContextMenu = cm;
                    ctrl.MouseDoubleClick += (s, e) => { ShowStatisticsDirect(st); };
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

        private void ShowChart()
        {
            // 1. Création du contrôle de statistiques
            

            // 2. On remplace le contenu du conteneur central (ContentControl dans ton XAML)
            MainViewContainer.Content = chart;

            // 3. Animation de fondu
            DoubleAnimation fadeIn = new DoubleAnimation
            {
                From = 0,
                To = 1,
                Duration = TimeSpan.FromSeconds(0.4),
                EasingFunction = new QuarticEase { EasingMode = EasingMode.EaseOut }
            };
            chart.BeginAnimation(OpacityProperty, fadeIn);
        }
        #endregion

        #region GESTION DES NOTES
        private void LoadNotesForCurrentWeek()
        {
            if (!Directory.Exists(notesFolderPath)) Directory.CreateDirectory(notesFolderPath);

            // On utilise l'extension .etude maintenant
            string filePath = Path.Combine(notesFolderPath, $"Notes_{currentWeekStart:yyyyMMdd}.etude");

            RichTextService.LoadPackage(richTextBoxNotesWeeks, filePath);
            RichTextService.FormatImagesInDocument(richTextBoxNotesWeeks,200);

            UpdateWeekStartDateDisplay();
        }
        private void SaveNotes()
        {
            if (richTextBoxNotesWeeks == null) return;
            string filePath = Path.Combine(notesFolderPath, $"Notes_{currentWeekStart:yyyyMMdd}.etude");

            RichTextService.SavePackage(richTextBoxNotesWeeks, filePath);
        }
        // Pour gérer le "Coller" d'images dans les notes du dashboard
        private void richTextBoxNotesWeeks_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(BitmapSource)))
            {
                if (e.DataObject.GetData(typeof(BitmapSource)) is BitmapSource bitmap)
                {
                    BitmapSource compressed = RichTextService.CompressImage(bitmap);
                    Image img = new Image { Source = compressed, MaxWidth  =200 };
                    
                    new InlineUIContainer(img, richTextBoxNotesWeeks.CaretPosition);
                    e.CancelCommand();
                }
            }
        }
        private void richTextBoxNotesWeeks_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = richTextBoxNotesWeeks.GetPositionFromPoint(e.GetPosition(richTextBoxNotesWeeks), true);
            if (pos?.Parent is InlineUIContainer container && container.Child is Image img)
            {
                if (img.Source is BitmapSource src)
                {
                    new ZoomImageWindow(src).ShowDialog();
                    e.Handled = true;
                }
            }
        }

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
        private void ButtonHome(object sender, RoutedEventArgs e) { ShowDashboard(); }
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
        private async void LoadInvestingCalendar()
        {
            try
            {
                // On attend que le moteur WebView2 soit initialisé
                await InvestingCalendarBrowser.EnsureCoreWebView2Async(null);

                // On navigue vers l'URL d'Investing
                string url = "https://sslecal2.investing.com?columns=exc_flags,exc_currency,exc_importance,exc_actual,exc_forecast,exc_previous&features=datepicker,timezone&countries=110,17,25,34,32,6,37,26,5,22,39,93,14,48,10,35,105,43,38,4,36,12,72&calType=week&timeZone=55&lang=5";
                InvestingCalendarBrowser.CoreWebView2.Navigate(url);
            }
            catch (Exception ex)
            {
                // Optionnel : un petit log pour toi en debug
                System.Diagnostics.Debug.WriteLine("Erreur WebView2 : " + ex.Message);
            }
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
            //await ShowNotification("Comming soon !!", false, false, 0.5);
            ShowChart();
        }

        #endregion

        private async void FearGreed_Click(object sender, RoutedEventArgs e)
        {
            // Ouvre l'overlay
            OverlaySentiment.Visibility = Visibility.Visible;
            await FetchDetailedSentimentData();
        }
        private async Task FetchDetailedSentimentData()
        {
            try
            {
                // 1. APPEL ET AFFICHAGE US MARKET (DYNAMIQUE ET PROPRE)
                SentimentDetail usData = await NetworkUtils.GetDetailedUsSentimentAsync();

                popUsValue.Text = usData.CurrentValue == -1 ? "??" : usData.CurrentValue.ToString();
                GetSentimentColorAndLabel(usData.CurrentValue, out string usLabel);
                popUsLabel.Text = usLabel.ToUpper();
                popUsLabel.Foreground = new SolidColorBrush(GetSentimentColor(usData.CurrentValue));

                // Historique US
                UpdatePopupHistoryRow(usData.YesterdayValue, popUsHier, progressUsHier);
                UpdatePopupHistoryRow(usData.LastWeekValue, popUsSemaine, progressUsSemaine);
                UpdatePopupHistoryRow(usData.LastMonthValue, popUsMois, progressUsMois);


                // 2. APPEL ET AFFICHAGE CRYPTO MARKET (DYNAMIQUE ET PROPRE)
                SentimentDetail cryptoData = await NetworkUtils.GetDetailedCryptoSentimentAsync();

                popCryptoValue.Text = cryptoData.CurrentValue == -1 ? "??" : cryptoData.CurrentValue.ToString();
                GetSentimentColorAndLabel(cryptoData.CurrentValue, out string cryptoLabel);
                popCryptoLabel.Text = cryptoLabel.ToUpper();
                popCryptoLabel.Foreground = new SolidColorBrush(GetSentimentColor(cryptoData.CurrentValue));

                // Historique Crypto
                UpdatePopupHistoryRow(cryptoData.YesterdayValue, popCryptoHier, progressCryptoHier);
                UpdatePopupHistoryRow(cryptoData.LastWeekValue, popCryptoSemaine, progressCryptoSemaine);
                UpdatePopupHistoryRow(cryptoData.LastMonthValue, popCryptoMois, progressCryptoMois);
            }
            catch
            {
                // Gestion silencieuse des erreurs réseau
            }
        }

        // Méthode utilitaire pour rafraîchir élégamment une ligne d'historique (Texte + ProgressBar)
        private void UpdatePopupHistoryRow(int value, TextBlock textControl, ProgressBar progressControl)
        {
            if (value == -1)
            {
                textControl.Text = "--";
                progressControl.Value = 0;
                return;
            }

            GetSentimentColorAndLabel(value, out string label);
            textControl.Text = $"{value} / {label}";
            progressControl.Value = value;
            progressControl.Foreground = new SolidColorBrush(GetSentimentColor(value));
        }
        // Traducteur rapide pour l'API alternative.me
       

        // Retourne uniquement la couleur
        private Color GetSentimentColor(int value)
        {
            if (value <= 25) return Color.FromRgb(242, 54, 69);
            if (value <= 45) return Color.FromRgb(255, 106, 2);
            if (value <= 55) return Color.FromRgb(220, 220, 220);
            if (value <= 75) return Color.FromRgb(112, 168, 0);
            return Color.FromRgb(0, 150, 0);
        }

        private void Button_MouseEnter(object sender, MouseEventArgs e)
        {
            UpdateAllSentimentIndices();
        }
        private void PopupContent_MouseDown(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }
        // Ferme le popup si on clique à côté ou sur la croix
        private void OverlaySentiment_Close_Click(object sender, RoutedEventArgs e)
        {
            OverlaySentiment.Visibility = Visibility.Collapsed;
            PopupTransform.X = 0;
            PopupTransform.Y = 0;
        }
        private void OverlaySentiment_Close_Click(object sender, MouseButtonEventArgs e)
        {
            OverlaySentiment.Visibility = Visibility.Collapsed;
            PopupTransform.X = 0;
            PopupTransform.Y = 0;
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Limite la fenêtre à la WorkArea pour ne pas cacher la taskbar
            this.MaxWidth = SystemParameters.WorkArea.Width;
            this.MaxHeight = SystemParameters.WorkArea.Height;
        }
        // Quand l'utilisateur clique sur l'en-tête du popup
        private void Header_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            _isDragging = true;
            _startPoint = e.GetPosition(this); // Position relative à la fenêtre principale

            // On capture la souris pour continuer à suivre le mouvement même si l'utilisateur sort de l'en-tête en allant trop vite
            element.CaptureMouse();

            // On s'abonne temporairement aux mouvements et au relâchement
            element.MouseMove += Header_MouseMove;
            element.MouseLeftButtonUp += Header_MouseLeftButtonUp;
        }

        // Pendant que l'utilisateur déplace la souris
        private void Header_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging) return;

            Point currentPoint = e.GetPosition(this);

            // Calcul de l'écart (Delta)
            double deltaX = currentPoint.X - _startPoint.X;
            double deltaY = currentPoint.Y - _startPoint.Y;

            // Application du décalage sur la transformation du Border
            PopupTransform.X += deltaX;
            PopupTransform.Y += deltaY;

            // On redéfinit le point de départ pour le prochain pixel de mouvement
            _startPoint = currentPoint;
        }

        // Quand l'utilisateur relâche le clic
        private void Header_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            var element = sender as FrameworkElement;
            if (element == null) return;

            _isDragging = false;
            element.ReleaseMouseCapture(); // Relâche le contrôle de la souris

            // On se désabonne pour libérer les ressources
            element.MouseMove -= Header_MouseMove;
            element.MouseLeftButtonUp -= Header_MouseLeftButtonUp;
        }

        // Sécurité : Évite que cliquer sur la croix "✕" ne tente de déplacer le volet
        private void Button_MouseLeftButtonDown_Handled(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true;
        }

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

    // Classe pour l'affichage uniforme dans la liste
    public class NotificationItem
    {
        public int Id { get; set; }
        public string Title { get; set; }
        public string Body { get; set; }
        public string Date { get; set; }
        public SolidColorBrush Color { get; set; }
    }
    // Cette classe "masque" la MessageBox par défaut de System.Windows
    #endregion
}