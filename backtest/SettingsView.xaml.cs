using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Animation;
using Microsoft.Win32;
using System.Threading.Tasks;
using System.IO;
using System.ComponentModel;
using System.Collections.Specialized;
using System.Linq; // Any/Count sur les rapports de synchro
using System.Globalization; // Dates des notes hebdo / captures (TryParseExact)
using System.Text.Json; // Intégré à .NET pour gérer le fichier local
using System.Collections.Generic; // Listes fichiers/backups cloud
using backtest.Services;

namespace backtest
{
    // Classe simple pour stocker les données
    public class UserSessionData
    {
        public bool IsLoggedIn { get; set; }
        public string FullName { get; set; }
        public string Email { get; set; }
        public string Phone { get; set; }
        public string Bio { get; set; }
        public DateTime? LastSyncDate { get; set; }
        public string ImagePath { get; set; }
    }

    /// <summary>
    /// Utilitaire de formatage (partage entre SettingsView et les modeles de données cloud).
    /// </summary>
    internal static class CloudFormatHelper
    {
        public static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " o";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " Ko";
            return (bytes / 1024.0 / 1024.0).ToString("F2") + " Mo";
        }

        public static string FormatServerDate(long serverTime)
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(serverTime).LocalDateTime.ToString("dd/MM/yyyy HH:mm"); }
            catch { return "---"; }
        }

        /// <summary>
        /// Traduit les phases techniques de la synchro en libellés lisibles
        /// (partagé par la barre de progression des paramètres et du dashboard).
        /// </summary>
        public static string SyncPhaseLabel(string phase)
        {
            switch ((phase ?? "").Trim().ToLowerInvariant())
            {
                case "connecting...": return "Connexion au cloud…";
                case "scanning...": return "Analyse des données…";
                case "upload": return "Envoi vers le cloud…";
                case "download": return "Récupération des données…";
                case "cleanup": return "Nettoyage…";
                case "done": return "Synchronisation terminée";
                default: return string.IsNullOrEmpty(phase) ? "Synchronisation…" : phase;
            }
        }
    }

    /// <summary>
    /// Ligne affichée dans l'onglet CLOUD des paramètres.
    /// En plus des données brutes (chemin/taille/date), chaque ligne porte une
    /// catégorie métier et un libellé lisible pour l'utilisateur : le logiciel
    /// gère tout seul les fichiers techniques (metadata/, cacheimage/…), tandis
    /// que l'utilisateur ne connaît que ses stratégies, ses études et ses notes
    /// hebdomadaires.
    /// </summary>
    public class CloudFileItem
    {
        public const string CatStrategie = "STRATEGIE";
        public const string CatEtude = "ETUDE";
        public const string CatNote = "NOTE";
        public const string CatCapture = "CAPTURE";
        public const string CatTechnique = "TECHNIQUE";

        public string FilePath { get; set; }
        public string FileName { get; set; }
        public long Size { get; set; }
        public long LastModified { get; set; }
        public string SizeText => CloudFormatHelper.FormatBytes(Size);
        public string DateText => CloudFormatHelper.FormatServerDate(LastModified);

        // ---- Intitulés « métier » (ce que l'utilisateur comprend) ----
        public string Category { get; set; }
        public string CategoryLabel { get; set; }
        public string DisplayName { get; set; }
        public string SubLabel { get; set; }

        /// <summary>Vrai pour les fichiers internes (metadata/…) masqués par défaut.</summary>
        public bool IsTechnical { get; set; }

        // Couleurs du badge de catégorie affiché dans la liste.
        public Brush CategoryBrush => CloudDisplayHelper.GetCategoryBrush(Category);
        public Brush CategoryTextBrush => CloudDisplayHelper.GetCategoryTextBrush(Category);
    }

    /// <summary>
    /// Traduit les chemins internes du cloud en libellés compréhensibles :
    ///   data/{Nom}.json                → stratégie « Nom »
    ///   Notes/Notes_yyyyMMdd.etude     → « Semaine du … »
    ///   etudes/{dossier}/{nom}.etude   → étude « dossier / nom »
    ///   cacheimage/{ts}_{HTF|LTF}.png  → « Capture HTF/LTF · date »
    ///   metadata/* et autres           → fichier technique (masqué par défaut)
    /// </summary>
    internal static class CloudDisplayHelper
    {
        private static readonly SolidColorBrush _cyan = new SolidColorBrush(Color.FromRgb(0, 213, 255));
        private static readonly SolidColorBrush _purple = new SolidColorBrush(Color.FromRgb(179, 136, 255));
        private static readonly SolidColorBrush _green = new SolidColorBrush(Color.FromRgb(105, 240, 174));
        private static readonly SolidColorBrush _amber = new SolidColorBrush(Color.FromRgb(255, 215, 64));
        private static readonly SolidColorBrush _gray = new SolidColorBrush(Color.FromRgb(90, 90, 90));

        public static string GetCategoryLabel(string category)
        {
            switch (category)
            {
                case CloudFileItem.CatStrategie: return "STRATÉGIE";
                case CloudFileItem.CatEtude: return "ÉTUDE";
                case CloudFileItem.CatNote: return "NOTE";
                case CloudFileItem.CatCapture: return "CAPTURE";
                default: return "SYSTÈME";
            }
        }

        public static Brush GetCategoryBrush(string category)
        {
            switch (category)
            {
                case CloudFileItem.CatStrategie: return _cyan;
                case CloudFileItem.CatEtude: return _purple;
                case CloudFileItem.CatNote: return _green;
                case CloudFileItem.CatCapture: return _amber;
                default: return _gray;
            }
        }

        public static Brush GetCategoryTextBrush(string category)
            => category == CloudFileItem.CatTechnique ? Brushes.White : Brushes.Black;

        /// <summary>Construit une ligne lisible à partir du chemin distant brut.</summary>
        public static CloudFileItem BuildCloudItem(string remotePath, long size, long lastModified)
        {
            string p = remotePath ?? "";
            string lower = p.ToLowerInvariant();
            string fname = p;
            int slash = p.LastIndexOf('/');
            if (slash >= 0) fname = p.Substring(slash + 1);
            string baseName = fname;
            int dot = fname.LastIndexOf('.');
            if (dot > 0) baseName = fname.Substring(0, dot);

            var item = new CloudFileItem
            {
                FilePath = p,
                FileName = fname,
                Size = size,
                LastModified = lastModified
            };

            if (lower.StartsWith("data/"))
            {
                // data/{Nom}.json → stratégie (le JSON contient la stratégie + son journal)
                item.Category = CloudFileItem.CatStrategie;
                item.DisplayName = string.IsNullOrEmpty(baseName) ? "Stratégie" : baseName;
                item.SubLabel = "Stratégie de trading + journal";
            }
            else if (lower.StartsWith("etudes/"))
            {
                // etudes/{dossier}/{nom}.etude → étude
                string rel = p.Substring("etudes/".Length);
                string folder = rel;
                slash = rel.LastIndexOf('/');
                if (slash >= 0) folder = rel.Substring(0, slash);
                item.Category = CloudFileItem.CatEtude;
                item.DisplayName = baseName;
                item.SubLabel = !string.IsNullOrEmpty(folder) ? "Étude · " + folder.Trim('/') : "Étude";
            }
            else if (lower.StartsWith("notes/"))
            {
                // Notes/Notes_yyyyMMdd.etude → note hebdomadaire (semaine nommée sur le lundi)
                DateTime? d = ParseNoteDate(baseName);
                item.Category = CloudFileItem.CatNote;
                item.DisplayName = d.HasValue ? "Semaine du " + d.Value.ToString("dd/MM/yyyy") : baseName;
                item.SubLabel = "Note hebdomadaire";
            }
            else if (lower.StartsWith("cacheimage/"))
            {
                // cacheimage/{yyyyMMddHHmmss}_{HTF|LTF}.png → capture d'écran de trade
                var cap = ParseCapture(baseName);
                item.Category = CloudFileItem.CatCapture;
                item.DisplayName = cap.HasValue
                    ? "Capture " + cap.Value.type + " · " + cap.Value.date.ToString("dd/MM/yyyy HH:mm")
                    : baseName;
                item.SubLabel = "Capture d'écran de trade";
            }
            else
            {
                // metadata/ et tout le reste → géré automatiquement par le logiciel
                item.Category = CloudFileItem.CatTechnique;
                item.DisplayName = baseName;
                item.SubLabel = "Fichier technique — géré automatiquement";
                item.IsTechnical = true;
            }

            item.CategoryLabel = GetCategoryLabel(item.Category);
            return item;
        }

        /// <summary>Date d'une note hebdo extraite de son nom (Notes_20260907 ou 20260907).</summary>
        public static DateTime? ParseNoteDate(string fileName)
        {
            var m = System.Text.RegularExpressions.Regex.Match(fileName ?? "", "(?<d>\\d{8})");
            if (m.Success && DateTime.TryParseExact(m.Groups["d"].Value, "yyyyMMdd",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                return d;
            return null;
        }

        /// <summary>Timestamp + type (HTF/LTF) d'une capture cacheimage extraits du nom.</summary>
        public static (DateTime date, string type)? ParseCapture(string name)
        {
            var m = System.Text.RegularExpressions.Regex.Match(name ?? "",
                "(?<ts>\\d{14})_(?<type>HTF|LTF)", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            if (m.Success && DateTime.TryParseExact(m.Groups["ts"].Value, "yyyyMMddHHmmss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                return (d, m.Groups["type"].Value.ToUpperInvariant());
            return null;
        }

        /// <summary>
        /// Libellé lisible d'une sauvegarde .bak ({chemin}.{yyyyMMdd_HHmmss}.bak) :
        /// « Nom (catégorie) — version du dd/MM/yyyy HH:mm — taille — date ».
        /// </summary>
        public static string BuildBackupLabel(CloudBackupInfo b)
        {
            string p = b.path ?? "";
            string basePath = p;
            if (basePath.EndsWith(".bak", StringComparison.OrdinalIgnoreCase))
                basePath = basePath.Substring(0, basePath.Length - 4);

            string suffix = "";
            var m = System.Text.RegularExpressions.Regex.Match(basePath, "\\.(\\d{8}_\\d{6})(?:_\\d+)?$");
            if (m.Success)
            {
                basePath = basePath.Substring(0, m.Index);
                if (DateTime.TryParseExact(m.Groups[1].Value, "yyyyMMdd_HHmmss",
                    CultureInfo.InvariantCulture, DateTimeStyles.None, out DateTime d))
                    suffix = " — version du " + d.ToString("dd/MM/yyyy HH:mm");
            }

            var item = BuildCloudItem(basePath, b.size, b.last_modified);
            string name = item.DisplayName ?? basePath;
            return name + suffix + " ( " + item.SubLabel + " )   —   " + CloudFormatHelper.FormatBytes(b.size)
                + "   —   " + CloudFormatHelper.FormatServerDate(b.last_modified);
        }
    }

    public partial class SettingsView : UserControl
    {
        private FxCloudService _cloudService = new FxCloudService();
        private string selectedImagePath = "";
        public bool _isLoggedIn = false;
        private bool _isEditMode = false;
        private string _tempUpdateImagePath = "";
        // Chemin du fichier local (dans AppData pour être persistant)
        private readonly string _sessionFilePath;

        public SettingsView()
        {
            _sessionFilePath = _cloudService._sessionFilePath;
            InitializeComponent();
            LoadSessionFromDisk(); // Charger les données au démarrage
            ShowAppPanel();
        }



        private void HideAllPanels()
        {
            PanelApp.Visibility = Visibility.Collapsed;
            PanelLogin.Visibility = Visibility.Collapsed;
            PanelProfile.Visibility = Visibility.Collapsed;
            PanelRegister.Visibility = Visibility.Collapsed;
            PanelCloud.Visibility = Visibility.Collapsed;
        }

        private async Task ShowNotification(string message, bool isError = false, bool keepOpen = false)
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
            DoubleAnimation fadeIn = new DoubleAnimation(1, TimeSpan.FromSeconds(0.2));
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeIn);

            if (!keepOpen)
            {
                await Task.Delay(3000);
                DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.5));
                fadeOut.Completed += (s, e) => CyberToast.Visibility = Visibility.Collapsed;
                CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
            }
        }

        // Méthode pour forcer la fermeture
        private void HideNotification()
        {
            DoubleAnimation fadeOut = new DoubleAnimation(0, TimeSpan.FromSeconds(0.3));
            fadeOut.Completed += (s, e) => CyberToast.Visibility = Visibility.Collapsed;
            CyberToast.BeginAnimation(UIElement.OpacityProperty, fadeOut);
        }
        private void BtnAppTab_Click(object sender, RoutedEventArgs e) => ShowAppPanel();
        private void ChkAgentEnabled_Click(object sender, RoutedEventArgs e)
        {
            bool isEnabled = ChkAgentEnabled.IsChecked ?? true;
            Properties.Settings.Default.IsAgentEnabled = isEnabled;
            Properties.Settings.Default.Save();
        
            // Propager le changement immédiatement sur le bouton flottant du MainWindow
            if (Application.Current.MainWindow is MainWindow mw)
                mw.UpdateAgentFabVisibility();
        }

        // Toggle cacheimage/ : appliqué immédiatement en mémoire + config.txt
        // (partagé avec l'instance de MainWindow via le champ statique).
        private void ChkSyncCacheImage_Click(object sender, RoutedEventArgs e)
        {
            FxCloudService.SetSyncCacheImageEnabled(ChkSyncCacheImageLegacy.IsChecked ?? true);
        }

        // Bouton « Synchroniser maintenant » : lance la synchro complète depuis
        // les paramètres et affiche un résumé.
        private async void BtnSyncNow_Click(object sender, RoutedEventArgs e)
        {
            BtnSyncNow.IsEnabled = false;
            BorderSyncProgress.Visibility = Visibility.Visible;
            ProgSync.Value = 0;
            TxtSyncPercent.Text = "0%";
            TxtSyncPhase.Text = "Préparation…";
            TxtSyncCurrentFile.Text = "";

            var progress = new Progress<SyncProgressInfo>(p =>
            {
                if (p.IsIndeterminate) { 
                    TxtSyncPhase.Text = CloudFormatHelper.SyncPhaseLabel(p.Phase);
                    ProgSync.IsIndeterminate = true;
                } else {
                    ProgSync.IsIndeterminate = false;
                    TxtSyncPhase.Text = CloudFormatHelper.SyncPhaseLabel(p.Phase);
                    ProgSync.Value = p.PercentComplete;
                    TxtSyncPercent.Text = p.PercentComplete + "%";
                    if (!string.IsNullOrEmpty(p.CurrentFile)) TxtSyncCurrentFile.Text = p.CurrentFile;
                }
            });

            try
            {
                List<string> results = await _cloudService.FullSyncAsync(progress);
                FxCloudService.Log(String.Join("\n", results));
                int changeCount = results.Count(line => line.Contains("success") || line.Contains("mis a jour") || line.Contains("traitees") || line.Contains("Jour"));
                bool hasCriticalError = results.Any(line => line.Contains("Erreur") || line.Contains("inaccessible"));
                string messageFinal;
                bool isError = hasCriticalError;
                if (hasCriticalError)
                {
                    messageFinal = "La synchronisation a echoue. Verifiez votre connexion.";
                }
                else if (changeCount > 0)
                {
                    messageFinal = $"Synchro reussie : {changeCount} element(s) synchronises.";
                    TxtSyncStatus.Text = "Derniere sync : " + DateTime.Now.ToString("g");
                }
                else
                {
                    messageFinal = "Tout est deja a jour.";
                    TxtSyncStatus.Text = "Derniere sync : " + DateTime.Now.ToString("g") + " (aucun changement)";
                }
                await ShowNotification(messageFinal, isError);

                // RAICHIR l-UI apres la sync :Strategies, trades, etudes
                if (!isError && Application.Current.MainWindow is MainWindow mw)
                {
                    try { mw.loadStrategies(); } catch { }
                    try { mw.ReloadCurrentWeekNotes(); } catch { }
                }
            }
            catch (Exception ex)
            {
                TxtSyncStatus.Text = "Erreur : " + ex.Message;
                await ShowNotification("Erreur imprevue : " + ex.Message, true);
            }
            finally
            {
                BtnSyncNow.IsEnabled = true;
                await System.Threading.Tasks.Task.Delay(1500);
                BorderSyncProgress.Visibility = Visibility.Collapsed;
            }
        }

        
        private void BtnAccountTab_Click(object sender, RoutedEventArgs e) => ShowAccountPanel();

        public void ShowAppPanel()
        {
            HideAllPanels();
            UpdateTabVisuals("app");
            PanelApp.Visibility = Visibility.Visible;
            TxtPassword.Clear();
            // Synchroniser le contrôle avec le paramètre persistant
            ChkAgentEnabled.IsChecked = Properties.Settings.Default.IsAgentEnabled;
            ChkSyncCacheImageLegacy.IsChecked = FxCloudService.SyncCacheImageEnabled;
        }

        public void ShowAccountPanel()
        {
            HideAllPanels();
            UpdateTabVisuals("account");

            if (_isLoggedIn)
            {
                PanelProfile.Visibility = Visibility.Visible;
                BtnLogout.Visibility = Visibility.Visible;
            }
            else
            {
                PanelLogin.Visibility = Visibility.Visible;
                BtnLogout.Visibility = Visibility.Collapsed;
            }
        }

        // ==================================================================
        // ONGLET CLOUD : fichiers distants, sauvegardes .bak, stockage.
        // ==================================================================

        private List<CloudFileInfo> _cloudFiles = new List<CloudFileInfo>();
        private List<CloudBackupInfo> _cloudBackups = new List<CloudBackupInfo>();
        private List<CloudBackupInfo> _backupsForSelection = new List<CloudBackupInfo>();

        public void ShowCloudPanel()
        {
            HideAllPanels();
            UpdateTabVisuals("cloud");
            PanelCloud.Visibility = Visibility.Visible;
            SynchronizeSyncFolderChecks();
            _ = RefreshCloudPanelAsync();
        }

        // Réflète l'état des exclusions de synchro (config.txt) sur les checkboxes.
        private void SynchronizeSyncFolderChecks()
        {
            ChkSyncData.IsChecked = !FxCloudService.IsCloudFolderExcluded("data/");
            ChkSyncEtudes.IsChecked = !FxCloudService.IsCloudFolderExcluded("etudes/");
            ChkSyncNotes.IsChecked = !FxCloudService.IsCloudFolderExcluded("Notes/");
            ChkSyncCacheImage.IsChecked = !FxCloudService.IsCloudFolderExcluded("cacheimage/");
            ChkSyncMetadata.IsChecked = !FxCloudService.IsCloudFolderExcluded("metadata/");
        }

        private void ChkSyncFolder_Click(object sender, RoutedEventArgs e)
        {
            var chk = (CheckBox)sender;
            string folder = null;
            if (chk == ChkSyncData) folder = "data";
            else if (chk == ChkSyncEtudes) folder = "etudes";
            else if (chk == ChkSyncNotes) folder = "Notes";
            else if (chk == ChkSyncCacheImage) folder = "cacheimage";
            else if (chk == ChkSyncMetadata) folder = "metadata";
            if (folder == null) return;

            FxCloudService.SetCloudFolderSyncEnabled(folder, chk.IsChecked ?? true);
            string label = folder == "data" ? "Stratégies & journal"
                : folder == "etudes" ? "Études"
                : folder == "Notes" ? "Notes hebdomadaires"
                : folder == "cacheimage" ? "Captures d'écran (trades)"
                : "Fichiers techniques";
            _ = ShowNotification(label + " : synchronisation " + ((chk.IsChecked ?? true) ? "activée" : "désactivée") + ".");
        }


        public async void chargerprofile()
        {
            var profile = await _cloudService.GetProfileAsync();
            if (Application.Current.MainWindow is MainWindow mw) mw.ApplyUserInterface(LoadSessionFromDisk());
            if (profile != null)
            {
                _isLoggedIn = true;
                
                // --- FIN DU CHARGEMENT (SUCCÈS) ---
                await ShowNotification("Connecté avec succès !");
                ShowAccountPanel();

            }
        }
        private async void Login_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtIdentifier.Text) || string.IsNullOrWhiteSpace(TxtPassword.Password))
            {
                ShowNotification("Champs requis", true);
                return;
            }

            // --- DEBUT DU CHARGEMENT ---
            btnLogin.IsEnabled = false; // Désactive le bouton
            await ShowNotification("Connexion au serveur...", false, true); // Notification persistante
            // 1. Authentification pour obtenir le Token
            string success = await _cloudService.LoginAsync(TxtIdentifier.Text, TxtPassword.Password);

            if (success == "yes")
            {
                chargerprofile();

            }
            else
            {
                // --- FIN DU CHARGEMENT (ERREUR) ---
                await ShowNotification(success, true);
            }
            btnLogin.IsEnabled = true; // Réactive le bouton
        }
        private async void Register_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(RegFullName.Text) || string.IsNullOrWhiteSpace(RegPassword.Password))
            {
                ShowNotification("Nom et mot de passe requis", true);
                return;
            }

            btnRegister.IsEnabled = false;

            await ShowNotification("Création du profil et envoi de l'image...", false, true);
            string success = await _cloudService.RegisterAsync(RegEmail.Text, RegPhone.Text, RegPassword.Password, RegFullName.Text, RegBio.Text, selectedImagePath);

            if (success == "yes")
            {
                _isLoggedIn = true;

                // Sauvegarde immédiate dans le fichier local
                _cloudService.SaveSessionToDisk(RegFullName.Text, RegEmail.Text, RegPhone.Text, RegBio.Text, selectedImagePath);
                LoadSessionFromDisk(); // Met à jour l'UI avec les infos du fichier

                await ShowNotification("Compte créé !");
                btnRegister.IsEnabled = true;
                ShowAccountPanel();
            }
            else
            {
                ShowNotification(success, true);
            }
        }
        private UserSessionData LoadSessionFromDisk()
        {
            var session= new UserSessionData();
            if (!File.Exists(_sessionFilePath)) return session;

            try
            {
                string json = File.ReadAllText(_sessionFilePath);
                session = JsonSerializer.Deserialize<UserSessionData>(json);

                if (session != null && session.IsLoggedIn)
                {
                    _isLoggedIn = true;
                    TxtSessionName.Text = session.FullName;
                    TxtProfileEmail.Text = session.Email;
                    TxtProfilePhone.Text = session.Phone;
                    TxtProfileBio.Text = session.Bio;

                    if (!string.IsNullOrEmpty(session.ImagePath))
                    {
                        // Si c'est un chemin local et qu'il existe
                        if (File.Exists(session.ImagePath))
                        {
                            ImgUserSession.ImageSource = new BitmapImage(new Uri(session.ImagePath));
                        }
                        // Si c'est une URL (cas de secours)
                        else if (session.ImagePath.StartsWith("http"))
                        {
                            ImgUserSession.ImageSource = new BitmapImage(new Uri(session.ImagePath));
                        }
                    }
                }
            }
            catch { /* Erreur lecture */ }
            return session;
        }
        private void Logout_Click(object sender, RoutedEventArgs e)
        {

            _isLoggedIn = false;
            // On vide les champs UI
            TxtSessionName.Text = "Utilisateur";
            TxtProfileEmail.Text = "---";

            ShowNotification("Session locale effacée");
            ShowAccountPanel();
            if (Application.Current.MainWindow is MainWindow mw) mw.Logout();
        }
        // 1. Basculer entre Mode Vue et Mode Édition
        private void BtnEditToggle_Click(object sender, RoutedEventArgs e)
        {
            _isEditMode = !_isEditMode;

            // Visibilité des textes vs inputs
            TxtSessionName.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
            EditFullName.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            TxtProfilePhone.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
            EditPhone.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            TxtProfileBio.Visibility = _isEditMode ? Visibility.Collapsed : Visibility.Visible;
            EditBio.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            // Boutons et Image
            BtnSaveProfile.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;
            EditPhotoOverlay.Visibility = _isEditMode ? Visibility.Visible : Visibility.Collapsed;

            if (_isEditMode)
            {
                // Pré-remplir les champs avec les données actuelles
                EditFullName.Text = TxtSessionName.Text;
                EditPhone.Text = TxtProfilePhone.Text;
                EditBio.Text = TxtProfileBio.Text == "Aucune description disponible." ? "" : TxtProfileBio.Text;
                BtnEditToggle.Content = "ANNULER";
                _tempUpdateImagePath = ""; // Reset
            }
            else
            {
                BtnEditToggle.Content = "MODIFIER";
            }
        }

        // 2. Sélectionner une nouvelle photo durant l'édition
        private void SelectPhotoUpdate_Click(object sender, MouseButtonEventArgs e)
        {
            if (!_isEditMode) return;

            OpenFileDialog op = new OpenFileDialog { Filter = "Images (*.jpg;*.png)|*.jpg;*.png" };
            if (op.ShowDialog() == true)
            {
                _tempUpdateImagePath = op.FileName;
                ImgUserSession.ImageSource = new BitmapImage(new Uri(_tempUpdateImagePath));
            }
        }

        // 3. Sauvegarde finale vers le serveur
        private async void BtnSaveProfile_Click(object sender, RoutedEventArgs e)
        {
            BtnSaveProfile.IsEnabled = false;
            await ShowNotification("Mise à jour du profil...", false, true);

            var result = await _cloudService.UpdateUserProfileAsync(
                EditFullName.Text,
                EditPhone.Text,
                EditBio.Text,
                _tempUpdateImagePath
            );

            if (result.success)
            {
                // On quitte le mode édition
                BtnEditToggle_Click(null, null);

                // On relance TA méthode pour rafraîchir l'UI proprement depuis le serveur
                chargerprofile();

                await ShowNotification("Profil mis à jour !");
            }
            else
            {
                await ShowNotification(result.message, true);
            }

            BtnSaveProfile.IsEnabled = true;
        }
        private void ShowRegister_Click(object sender, RoutedEventArgs e)
        {
            HideAllPanels();
            PanelRegister.Visibility = Visibility.Visible;
        }

        private void ShowLogin_Click(object sender, RoutedEventArgs e) => ShowAccountPanel();

        private void SelectPhoto_Click(object sender, MouseButtonEventArgs e)
        {
            OpenFileDialog op = new OpenFileDialog { Filter = "Images (*.jpg;*.png)|*.jpg;*.png" };
            if (op.ShowDialog() == true)
            {
                selectedImagePath = op.FileName;
                ImgProfilePreview.ImageSource = new BitmapImage(new Uri(selectedImagePath));
            }
        }

        private void UpdateTabVisuals(string activeTab)
        {
            // Onglet actif : Cyan ; inactifs : gris (#222)
            BtnAppTab.Foreground = activeTab == "app" ? Brushes.Cyan : Brushes.Gray;
            BtnAppTab.BorderBrush = activeTab == "app" ? Brushes.Cyan : new SolidColorBrush(Color.FromRgb(34, 34, 34));
            BtnAccountTab.Foreground = activeTab == "account" ? Brushes.Cyan : Brushes.Gray;
            BtnAccountTab.BorderBrush = activeTab == "account" ? Brushes.Cyan : new SolidColorBrush(Color.FromRgb(34, 34, 34));
            BtnCloudTab.Foreground = activeTab == "cloud" ? Brushes.Cyan : Brushes.Gray;
            BtnCloudTab.BorderBrush = activeTab == "cloud" ? Brushes.Cyan : new SolidColorBrush(Color.FromRgb(34, 34, 34));
        }

        private void BtnRetour_Click(object sender, RoutedEventArgs e)
        {
            if (Application.Current.MainWindow is MainWindow mw) mw.ShowDashboard();

        }

        // ==================================================================
        // ONGLET CLOUD : chargement / restauration / suppression distantes.
        // ==================================================================

        private async Task RefreshCloudPanelAsync()
        {
            if (string.IsNullOrEmpty(FxCloudService.CurrentToken))
            {
                TxtCloudStorage.Text = "Non connecté";
                TxtCloudFileCount.Text = "0";
                TxtCloudBackupCount.Text = "0";
                TxtCloudLastSync.Text = "Jamais";
                LvCloudFiles.Items.Clear();
                FillBackupsForSelection();
                return;
            }

            BtnCloudRefresh.IsEnabled = false;

            var storageTask = _cloudService.GetCloudStorageInfoAsync();
            var filesTask = _cloudService.FetchCloudManifestPublicAsync();
            var backupsTask = _cloudService.GetCloudBackupsAsync();
            await Task.WhenAll(storageTask, filesTask, backupsTask);

            var storage = storageTask.Result;
            TxtCloudStorage.Text = storage != null
                ? FormatBytes(storage.total_bytes)
                : "Inaccessible";

            // Dernière synchronisation connue (session locale).
            try
            {
                var session = LoadSessionFromDisk();
                TxtCloudLastSync.Text = (session != null && session.LastSyncDate.HasValue)
                    ? session.LastSyncDate.Value.ToString("dd/MM/yyyy HH:mm")
                    : "Jamais";
            }
            catch { TxtCloudLastSync.Text = "Jamais"; }

            _cloudFiles = filesTask.Result ?? new List<CloudFileInfo>();
            // Tri alphabétique pour une navigation plus lisible.
            _cloudFiles.Sort((a, b) => string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase));
            // Le compteur « ÉLÉMENTS » ne reflète que ce que l'utilisateur voit :
            // les fichiers techniques (metadata/…) en sont exclus.
            int visibleCount = 0;
            foreach (var f in _cloudFiles)
                if (!CloudDisplayHelper.BuildCloudItem(f.path, f.size, f.last_modified).IsTechnical)
                    visibleCount++;
            TxtCloudFileCount.Text = visibleCount.ToString();
            RefreshCloudFilesList();

            _cloudBackups = backupsTask.Result ?? new List<CloudBackupInfo>();
            TxtCloudBackupCount.Text = _cloudBackups.Count.ToString();
            FillBackupsForSelection();

            BtnCloudRefresh.IsEnabled = true;
        }

        // ==================================================================
        // Recherche / filtrage de la liste des fichiers du cloud.
        // Chaque ligne est un CloudFileItem porteur de son chemin complet :
        // la sélection est donc robuste même quand le filtre est actif.
        // ==================================================================
        private void RefreshCloudFilesList()
        {
            string filter = (TxtCloudSearch.Text ?? "").Trim();
            bool showTech = ChkShowTechnical != null && ChkShowTechnical.IsChecked == true;

            // 1. Traduit les chemins bruts du cloud en lignes « métier » lisibles.
            var items = new List<CloudFileItem>();
            foreach (var f in _cloudFiles)
                items.Add(CloudDisplayHelper.BuildCloudItem(f.path, f.size, f.last_modified));

            // 2. Tri : Stratégies → Études → Notes → Captures → Système,
            //    puis nom croissant (stratégies/études/…) et date décroissante
            //    pour les notes et les captures.
            int CatOrder(string c)
            {
                switch (c)
                {
                    case CloudFileItem.CatStrategie: return 0;
                    case CloudFileItem.CatEtude: return 1;
                    case CloudFileItem.CatNote: return 2;
                    case CloudFileItem.CatCapture: return 3;
                    default: return 4;
                }
            }

            items = items
                .OrderBy(i => CatOrder(i.Category))
                .ThenByDescending(i => (i.Category == CloudFileItem.CatNote || i.Category == CloudFileItem.CatCapture) ? i.LastModified : long.MinValue)
                .ThenBy(i => i.DisplayName ?? "", StringComparer.OrdinalIgnoreCase)
                .ToList();

            // 3. Filtres : recherche libre + fichiers techniques masqués par défaut.
            LvCloudFiles.Items.Clear();
            foreach (var it in items)
            {
                if (!showTech && it.IsTechnical) continue;
                if (filter.Length > 0)
                {
                    bool hit = (it.DisplayName ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                        || (it.SubLabel ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0
                        || (it.FilePath ?? "").IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hit) continue;
                }
                LvCloudFiles.Items.Add(it);
            }
            if (LvCloudFiles.Items.Count > 0)
                LvCloudFiles.SelectedIndex = 0;
        }

        // Bascule « Afficher les fichiers techniques » : relance le filtre.
        private void ChkShowTechnical_Click(object sender, RoutedEventArgs e) => RefreshCloudFilesList();

        private CloudFileItem SelectedCloudItem()
        {
            if (LvCloudFiles.SelectedItem is CloudFileItem it) return it;
            return null;
        }

        // Bascule entre les onglets FICHIERS / SAUVEGARDES.
        private void ShowFilesTab()
        {
            TabFiles.IsChecked = true;
            TabBackups.IsChecked = false;
            ViewFiles.Visibility = Visibility.Visible;
            ViewBackups.Visibility = Visibility.Collapsed;
        }

        private void TabFiles_Click(object sender, RoutedEventArgs e) => ShowFilesTab();

        private void TabBackups_Click(object sender, RoutedEventArgs e)
        {
            TabBackups.IsChecked = true;
            TabFiles.IsChecked = false;
            ViewFiles.Visibility = Visibility.Collapsed;
            ViewBackups.Visibility = Visibility.Visible;
            FillBackupsForSelection();
        }


        // Filtre en direct pendant la saisie de recherche.
        private void TxtCloudSearch_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (TxtCloudSearchPlaceholder != null)
                TxtCloudSearchPlaceholder.Visibility = string.IsNullOrEmpty(TxtCloudSearch.Text)
                    ? Visibility.Visible : Visibility.Collapsed;
            RefreshCloudFilesList();
        }

        private void FillBackupsForSelection()
        {
            LstCloudBackups.Items.Clear();
            _backupsForSelection.Clear();

            CloudFileItem sel = SelectedCloudItem();
            string path = sel != null ? sel.FilePath : null;

            if (path == null)
            {
                // Aucun élément sélectionné : montrer toutes les versions du compte.
                TxtBackupFilterInfo.Text = "Toutes les versions du compte :";
                var all = new List<CloudBackupInfo>(_cloudBackups);
                all.Sort((a, b) => a.last_modified < b.last_modified ? 1 : (a.last_modified > b.last_modified ? -1 : 0));
                foreach (var b in all)
                {
                    _backupsForSelection.Add(b);
                    LstCloudBackups.Items.Add(CloudDisplayHelper.BuildBackupLabel(b));
                }
                if (_backupsForSelection.Count == 0)
                    LstCloudBackups.Items.Add("— Aucune sauvegarde sur le compte —");
                return;
            }

            // Un fichier est sélectionné : montrer uniquement ses versions.
            TxtBackupFilterInfo.Text = "Versions de \"" + (sel.DisplayName ?? path) + "\" :";
            string prefix = path + ".";
            foreach (var b in _cloudBackups)
            {
                if (!b.path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
                _backupsForSelection.Add(b);
                LstCloudBackups.Items.Add(CloudDisplayHelper.BuildBackupLabel(b));
            }
            if (_backupsForSelection.Count == 0)
                LstCloudBackups.Items.Add("— Aucune sauvegarde pour ce fichier —");
        }

        private void LvCloudFiles_SelectionChanged(object sender, SelectionChangedEventArgs e)
            => FillBackupsForSelection();

        private void BtnCloudTab_Click(object sender, RoutedEventArgs e) => ShowCloudPanel();

        private void BtnCloudRefresh_Click(object sender, RoutedEventArgs e)
            => _ = RefreshCloudPanelAsync();

        // Télécharge la version serveur du fichier sélectionné (écrase la copie
        // locale — utile quand la copie locale est corrompue/perdue).
        private async void BtnCloudRestoreFile_Click(object sender, RoutedEventArgs e)
        {
            CloudFileItem item = SelectedCloudItem();
            if (item == null)
            {
                await ShowNotification("Sélectionnez d'abord un fichier", true);
                return;
            }

            string path = item.FilePath;
            string localPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, path.Replace('/', Path.DirectorySeparatorChar));
            string label = item.DisplayName ?? path;

            if (MessageBox.Show("Réécrire la copie locale de \"" + label + "\" avec la version du cloud ?",
                "Restaurer depuis le cloud", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            bool ok = await _cloudService.DownloadFileAsync(path, localPath);
            await ShowNotification(ok ? "« " + label + " » restauré en local !" : "Erreur de téléchargement", !ok);
            if (ok) FxCloudService.Log("Cloud: restauration locale de " + path);
        }

        // Supprime le fichier sélectionné du cloud (fichier + backups .bak).
        private async void BtnCloudDeleteFile_Click(object sender, RoutedEventArgs e)
        {
            CloudFileItem item = SelectedCloudItem();
            if (item == null)
            {
                await ShowNotification("Sélectionnez d'abord un fichier", true);
                return;
            }

            string path = item.FilePath;
            string label = item.DisplayName ?? path;
            if (MessageBox.Show("Supprimer \"" + label + "\" du cloud (et ses versions précédentes) ?\nLa copie locale reste sur votre PC.",
                "Supprimer du cloud", MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            var (success, message) = await _cloudService.DeleteCloudFileAsync(path);
            await ShowNotification(message, !success);
            if (success)
            {
                FxCloudService.Log("Cloud: suppression distante de " + path);
                await RefreshCloudPanelAsync();
            }
        }

        // Restaure la sauvegarde .bak sélectionnée comme version courante du
        // serveur (cas : version récente corrompue, on récupère une ancienne).
        private async void BtnCloudRestoreBackup_Click(object sender, RoutedEventArgs e)
        {
            int sel = LstCloudBackups.SelectedIndex;
            if (sel < 0 || sel >= _backupsForSelection.Count)
            {
                await ShowNotification("Sélectionnez d'abord une sauvegarde", true);
                return;
            }

            CloudBackupInfo backup = _backupsForSelection[sel];
            if (MessageBox.Show("Restaurer la version suivante comme version actuelle sur le serveur ?\n\n"
                + CloudDisplayHelper.BuildBackupLabel(backup),
                "Restaurer une sauvegarde", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes)
                return;

            var (success, message) = await _cloudService.RestoreCloudBackupAsync(backup.path);
            await ShowNotification(success ? "Sauvegarde restaurée ! Relancez une synchro pour la récupérer en local." : message, !success);
            if (success) FxCloudService.Log("Cloud: restauration serveur de " + backup.path);
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " o";
            if (bytes < 1024 * 1024) return (bytes / 1024.0).ToString("F1") + " Ko";
            return (bytes / 1024.0 / 1024.0).ToString("F2") + " Mo";
        }

        private static string FormatServerDate(long serverTime)
        {
            try { return DateTimeOffset.FromUnixTimeSeconds(serverTime).LocalDateTime.ToString("dd/MM/yyyy HH:mm"); }
            catch { return "---"; }
        }
    }
}