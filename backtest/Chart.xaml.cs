using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Linq;
using backtest.services;
using backtest.Services;
using Newtonsoft.Json;
using System.Threading;
using Microsoft.Web.WebView2.Core;
using System.Windows.Media;

namespace backtest
{
    public partial class Chart : UserControl
    {
        private readonly ChartBridge _chartBridge;
        private readonly Dataservice _dataService;

        private string _currentSymbol = "EURUSD";
        private string _imageHtf = "";
        private string _imageLtf = "";
        private string _currentTF = "15m";
        private int _currentYear = DateTime.Now.Year;
        private bool _isLoadingMore = false;
        private Strategie _strategie;
        private bool _endOfDataReached = false;

        private CancellationTokenSource _ctsGlobal;

        public Chart()
        {
            InitializeComponent();
            LoadUserSettings();

            // On récupère l'URL du serveur depuis FxCloudService (config.txt),
            // pour que le graphique utilise le même serveur que le reste de l'app
            // (ex. http://localhost:8000 en dev, https://fxdataedge.com/ en prod)
            _dataService = new Dataservice(FxCloudService.getServeur());
            _chartBridge = new ChartBridge(this);

            InitTimeframeButtons();
            LoadWatchlist();
            InitBrowser(); // On initialise le browser, c'est lui qui déclenchera la suite
            _ctsGlobal = new CancellationTokenSource();
            TypeOrdreComboBox.ItemsSource = Enum.GetValues(typeof(TypeOrdre));
            ResultComboBox.ItemsSource = Enum.GetValues(typeof(Resultat));
        }
        public Chart(Strategie strategie) : this() // Appelle d'abord le constructeur par défaut
        {
            _strategie = strategie;

            // Mise à jour du label de titre dans l'onglet
            ActionTitle.Text = "STRATÉGIE :";
            MainTitle.Text = _strategie.Nom.ToUpper();

            ChargerChampsDynamiques();
        }
        private void ChargerChampsDynamiques()
        {
            DynamicFieldsPanel.Children.Clear();
            if (_strategie == null) return;

            List<string> structure = _strategie.GetStructure();

            foreach (var header in structure)
            {
                TextBlock lbl = new TextBlock
                {
                    Text = header.ToUpper(),
                    Foreground = Brushes.Gray,
                    FontSize = 9,
                    Margin = new Thickness(0, 10, 0, 2)
                };

                TextBox txt = new TextBox
                {
                    Style = (Style)this.FindResource("ModernField"),
                    Height = 28,
                    Tag = header, // Très important pour récupérer la valeur plus tard
                    Margin = new Thickness(0, 0, 0, 8)
                };

                DynamicFieldsPanel.Children.Add(lbl);
                DynamicFieldsPanel.Children.Add(txt);
            }
        }
        private void SaveTrade_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // On combine Date et Heure
                DateTime entree = DateEntreePicker.SelectedDate.Value.Date + TimeEntreePicker.Value.Value.TimeOfDay;
                DateTime sortie = DateSortiePicker.SelectedDate.Value.Date + TimeSortiePicker.Value.Value.TimeOfDay;

                // Récupération des confluences dynamiques
                var confluences = DynamicFieldsPanel.Children
                                    .OfType<TextBox>()
                                    .Select(tb => new ChampPersonnalise(tb.Tag.ToString(), tb.Text))
                                    .ToList();

                var nouveauTrade = new Trade(double.Parse(profitTxt.Text))
                {
                    Paire = PaireTextBox.Text,
                    DateEntree = entree,
                    prixOpen = Convert.ToDouble(EntryPriceTxt.Text),
                    prixClose = Convert.ToDouble(ExitPriceTxt.Text),
                    DateSortie = sortie,
                    TypeOrdre = (TypeOrdre)TypeOrdreComboBox.SelectedItem,
                    Result = (Resultat)ResultComboBox.SelectedItem,
                    RR = utils.ParseFloat(RrTextBox.Text),
                    ChampsPersonnalises = confluences,
                    ImageHtf = _imageHtf,
                    ImageLtf = _imageLtf,
                    strategie = _strategie.Nom,
                    description = descriptionTextbox.Text
                };

                _strategie.AddTrade(nouveauTrade);
                ResetTradeForm();
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur de saisie : " + ex.Message);
            }
            MaintabControl.SelectedIndex = 0;
        }
        private void trash_Click(object sender, RoutedEventArgs e)
        {
            ResetTradeForm(); MaintabControl.SelectedIndex = 0;
        }
        
        private void ResetTradeForm()
        {
            // Réinitialisation des textes et valeurs numériques
            PaireTextBox.Text = string.Empty;
            EntryPriceTxt.Text = "0";
            ExitPriceTxt.Text = "0";
            RrTextBox.Text = "0";
            profitTxt.Text = "0";
            descriptionTextbox.Text = string.Empty;

            // Réinitialisation des sélections
            TypeOrdreComboBox.SelectedIndex = -1;
            ResultComboBox.SelectedIndex = -1;

            // Réinitialisation des Dates (on remet à "Maintenant")
            DateEntreePicker.SelectedDate = null;
            TimeEntreePicker.Value = null;
            DateSortiePicker.SelectedDate = null;
            TimeSortiePicker.Value = null;

            // Nettoyage des confluences dynamiques
            foreach (var tb in DynamicFieldsPanel.Children.OfType<TextBox>())
            {
                tb.Text = string.Empty;
            }

            // Réinitialisation des variables d'images et des labels de statut
            _imageHtf = string.Empty;
            _imageLtf = string.Empty;

            if (StatusHtf != null) { StatusHtf.Text = "Aucune image"; StatusHtf.Foreground = System.Windows.Media.Brushes.DimGray; }
            if (StatusLtf != null) { StatusLtf.Text = "Aucune image"; StatusLtf.Foreground = System.Windows.Media.Brushes.DimGray; }
        }
        private void LoadUserSettings()
        {
            _currentSymbol = !string.IsNullOrEmpty(Properties.Settings.Default.LastSymbol)
                            ? Properties.Settings.Default.LastSymbol : "EURUSD";
            TxtCurrentSymbol.Text = _currentSymbol;
            _currentTF = !string.IsNullOrEmpty(Properties.Settings.Default.LastTimeframe)
                         ? Properties.Settings.Default.LastTimeframe : "15m";
        }

        private void SaveUserSettings()
        {
            Properties.Settings.Default.LastSymbol = _currentSymbol;
            Properties.Settings.Default.LastTimeframe = _currentTF;
            Properties.Settings.Default.Save();
        }

        private async void LoadWatchlist()
        {
            try
            {
                var list = await _dataService.GetWatchlistAsync();
                WatchlistItems.ItemsSource = list;
                var current = list.FirstOrDefault(x => x.Symbol == _currentSymbol);
                if (current != null) WatchlistItems.SelectedItem = current;
            }
            catch (Exception ex) { SetStatus("Erreur Watchlist: " + ex.Message, "#FF4B4B"); }
        }

        private void InitTimeframeButtons()
        {
            var tfs = new List<TimeframeItem>
            {
                new TimeframeItem { Name = "1m", Value = "1m" },
                new TimeframeItem { Name = "5m", Value = "5m" },
                new TimeframeItem { Name = "15m", Value = "15m" },
                new TimeframeItem { Name = "30m", Value = "30m" },
                new TimeframeItem { Name = "1h", Value = "1h" },
                new TimeframeItem { Name = "4h", Value = "4h" },
                new TimeframeItem { Name = "D", Value = "d" }
            };
            foreach (var item in tfs) item.IsActive = (item.Value.ToLower() == _currentTF.ToLower());
            TimeframeSelector.ItemsSource = tfs;
        }

        private async void InitBrowser()
        {
            await ChartBrowser.EnsureCoreWebView2Async(null);

            string rootFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "chart");

            ChartBrowser.CoreWebView2.SetVirtualHostNameToFolderMapping(
                "dataedge.local",
                rootFolder,
                CoreWebView2HostResourceAccessKind.Allow);

            ChartBrowser.CoreWebView2.AddHostObjectToScript("chartService", _chartBridge);

            // IMPORTANT : On attend que la page soit chargée avant d'envoyer les données
            ChartBrowser.NavigationCompleted += async (s, e) =>
            {
                if (e.IsSuccess)
                {
                    // La page est prête, on charge les données de la paire par défaut
                    await LoadBacktestData(_ctsGlobal.Token);
                }
            };

            ChartBrowser.CoreWebView2.Navigate("https://dataedge.local/index.html");
        }   

        // Méthode utilitaire pour exécuter du JS sans crash
        private async Task SafeExecuteJs(string script)
        {
            if (ChartBrowser != null && ChartBrowser.CoreWebView2 != null)
            {
                await ChartBrowser.ExecuteScriptAsync(script);
            }
        }
        // Dans Chart.xaml.cs
        public async Task LoadYearForBacktest(int year, bool sr = true)
        {
            if (_isLoadingMore) return;
            _isLoadingMore = true;
            await ToggleLoader(true, "");
            try
            {
                // On appelle notre nouvelle fonction
                string fileToRequest = GetFileToRequest(year, _currentTF);

                SetStatus($"JUMP : {year} (pack {fileToRequest})", "#FFB900");

                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, fileToRequest, _ctsGlobal.Token);

                if (result.success)
                {
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath));
                    if (candles != null)
                    {
                        _currentYear = year;
                        string json = JsonConvert.SerializeObject(candles);

                        // Envoi au JS
                        await SafeExecuteJs($"window.setupBacktestData({json}, {year});");
                    }
                }
                else
                {
                    await SafeExecuteJs($"window.cyberLog('Fichier {fileToRequest} introuvable sur le serveur', true);");
                }
            }
            catch (Exception ex)
            {
                await SafeExecuteJs($"window.cyberLog('Erreur Jump: {ex.Message}', true);");
            }
            finally
            {
                _isLoadingMore = false;
                await SafeExecuteJs("window.isProcessingData = false;");
                await ToggleLoader(false);
            }
        }
        public async Task LoadBacktestData(CancellationToken ct)
        {
            if (ChartBrowser?.CoreWebView2 == null) return;

            _endOfDataReached = false;
            _isLoadingMore = false;
            await ToggleLoader(true, $"Chargement de {_currentSymbol} ({_currentTF})...");
            try
            {
                // Utilisation de la fonction pour déterminer le fichier (bloc) correct
                string fileToRequest = GetFileToRequest(_currentYear, _currentTF);

                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, fileToRequest, ct);

                if (result.success)
                {
                    ct.ThrowIfCancellationRequested();
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath), ct);

                    if (candles != null && candles.Count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        string json = JsonConvert.SerializeObject(candles);

                        await Dispatcher.InvokeAsync(async () => {
                            await SafeExecuteJs($"updateChartData({json}, '{_currentSymbol}');");
                        });

                        SetStatus($"{_currentSymbol} OK ({fileToRequest})", "#00FF7F");
                        SaveUserSettings();
                        return;
                    }
                }

                await SafeExecuteJs($"updateChartData([], '{_currentSymbol}');");
                SetStatus("Aucune donnée", "#FF4B4B");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetStatus("Erreur: " + ex.Message, "#FF4B4B"); }
            finally
            {
                //e bloc finally s'exécute QUOI QU'IL ARRIVE (Succès, Annulation, ou Erreur)
                await ToggleLoader(false);
            }
        }
        public async Task LoadMoreData(long referenceTimestamp, bool isPrevious = true)
        {
            if (_isLoadingMore || _endOfDataReached) return;

            if (_ctsGlobal == null) _ctsGlobal = new CancellationTokenSource();
            _isLoadingMore = true;
            ;
            await ToggleLoader(true, "");
            try
            {
                DateTime refDate = DateTimeOffset.FromUnixTimeSeconds(referenceTimestamp).DateTime;
                _currentYear = refDate.Year;
                string targetYear;
                string tf = _currentTF.ToLower();

                if (isPrevious)
                {
                    // En arrière : On prend l'année de la première bougie
                    switch (tf)
                    {
                        case "1m":
                        case "5m":
                        case "15m":
                        case "30m":
                        case "1h":
                            targetYear = GetFileToRequest(refDate.Year-1, _currentTF);
                            break;
                        default:
                            targetYear = GetFileToRequest(refDate.Year, _currentTF);
                            break;

                    }
                    
                }
                else
                {
                   
                    targetYear = GetFileToRequest(refDate.Year+1, _currentTF);
                }

                SetStatus($"RECHERCHE {targetYear}...", "#FFB900");

                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, targetYear, _ctsGlobal.Token);

                if (result.success)
                {
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath));
                    if (candles != null && candles.Count > 0)
                    {
                        // On met à jour l'année courante avec l'année réelle demandée
                       
                        string json = JsonConvert.SerializeObject(candles);

                        if (isPrevious)
                        {
                            await SafeExecuteJs($"prependChartData({json});");
                        }
                        else
                        {
                            await SafeExecuteJs($"appendOrPrependData({json}, {_currentYear});");
                        }

                        SetStatus($"{_currentSymbol} {tf.ToUpper()} ({targetYear})", "#00FF7F");
                        return;
                    }
                }

                if (isPrevious) _endOfDataReached = true;
                SetStatus(isPrevious ? "DÉBUT DE L'HISTORIQUE" : "FIN DES DONNÉES", "#FF4B4B");
            }
            catch (Exception ex)
            {
                await SafeExecuteJs($"window.cyberLog('Erreur LoadMore: {ex.Message.Replace("'", "\\'")}');");
            }
            finally { _isLoadingMore = false; await ToggleLoader(false); }
        }
        private string GetFileToRequest(int year, string timeframe)
        {
            string tf = timeframe.ToLower();

            switch (tf)
            {
                case "w":
                case "m":
                    // Tout est regroupé dans le fichier 2026 pour Hebdo/Mensuel
                    return "2026";

                case "4h":
                case "d":
                    // Partitionnement par blocs de 10 ans
                    if (year >= 2006 && year <= 2016)
                    {
                        return "2016";
                    }
                    else if (year >= 2017 && year <= 2026)
                    {
                        return "2026";
                    }
                    return year.ToString();

                default:
                    // Pour 1H et moins, on utilise l'année précise
                    return year.ToString();
            }
        }
        private async Task ToggleLoader(bool show, string message = "")
        {
            if (ChartBrowser?.CoreWebView2 != null)
            {
                if (show)
                {
                    // Échappe la chaîne pour éviter tout bug JS avec des apostrophes
                    string safeMsg = message.Replace("'", "\\'");
                    await ChartBrowser.ExecuteScriptAsync($"window.showLoader('{safeMsg}');");
                }
                else
                {
                    await ChartBrowser.ExecuteScriptAsync("window.hideLoader();");
                }
            }
        }
        public async void ExitReplayAndGoToPresent()
        {
            try
            {
                // 1. On remet l'année sur l'année actuelle
                _currentYear = DateTime.Now.Year;

                // 2. On réinitialise les drapeaux de données
                _endOfDataReached = false;

                // 3. On recharge les données normales (ce qui appellera updateChartData en JS)
                await LoadBacktestData(_ctsGlobal.Token);

                await SafeExecuteJs("window.cyberLog('Retour au temps réel...');");
            }
            catch (Exception ex)
            {
                await SafeExecuteJs($"window.cyberLog('Erreur sortie replay: {ex.Message}', true);");
            }
        }
        private List<CandleModel> ParseCsvToCandles(string filePath)
        {
            var candles = new List<CandleModel>();
            var culture = CultureInfo.InvariantCulture;
            int lineCount = 0;

            try
            {
                using (var reader = new StreamReader(filePath, System.Text.Encoding.UTF8, true, 65536))
                {
                    reader.ReadLine();
                    while (!reader.EndOfStream)
                    {
                        lineCount++;
                        var line = reader.ReadLine();
                        if (string.IsNullOrWhiteSpace(line)) continue;
                        var parts = line.Split(',');
                        if (parts.Length < 5) continue;

                        try
                        {
                            if (DateTime.TryParseExact(parts[0], "yyyy.MM.dd HH:mm:ss", culture, DateTimeStyles.None, out DateTime dt))
                            {
                                candles.Add(new CandleModel
                                {
                                    time = ((DateTimeOffset)dt).ToUnixTimeSeconds(),
                                    open = double.Parse(parts[1], culture),
                                    high = double.Parse(parts[2], culture),
                                    low = double.Parse(parts[3], culture),
                                    close = double.Parse(parts[4], culture)
                                });
                            }
                        }
                        catch { continue; }
                    }
                }
            }
            catch (Exception ex)
            {
                if (ChartBrowser?.CoreWebView2 != null)
                {
                    string errorMsg = $"Erreur Parsing ligne {lineCount}: {ex.Message}";
                    string safeError = errorMsg.Replace("'", "\\'").Replace("\r", "").Replace("\n", "");
                }
            }
            return candles;
        }
        private void SetStatus(string msg, string colorHex)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => SetStatus(msg, colorHex));
                return;
            }
            TxtStatus.Text = msg.ToUpper();
            TxtStatus.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colorHex);
        }
        public void PopulateTradeForm(Trade trade)
        {
            // 1. Informations de base
            PaireTextBox.Text = _currentSymbol;
            RrTextBox.Text = trade.RR.ToString("F2", CultureInfo.InvariantCulture);
            EntryPriceTxt.Text = trade.prixOpen.ToString();
            ExitPriceTxt.Text = trade.prixClose.ToString();

            // 2. Enums (Type et Résultat)
            TypeOrdreComboBox.SelectedIndex = (int)trade.TypeOrdre;
            ResultComboBox.SelectedIndex = (int)trade.Result;

            // 3. Dates et Heures (Entrée)
            if (trade.DateEntree != DateTime.MinValue)
            {
                DateEntreePicker.SelectedDate = trade.DateEntree.Date;
                TimeEntreePicker.Value = trade.DateEntree;
            }

            // 4. Dates et Heures (Sortie)
            if (trade.DateSortie != DateTime.MinValue)
            {
                DateSortiePicker.SelectedDate = trade.DateSortie.Date;
                TimeSortiePicker.Value = trade.DateSortie;
            }

            // 5. Autre
            profitTxt.Text = trade.Profit.ToString(CultureInfo.InvariantCulture);

        }
        public void UpdateCurrentTradeImagePath(string type, string fileName)
        {
            if (type.ToUpper() == "HTF")
                _imageHtf = fileName;
            else
                _imageLtf = fileName;

        }

        #region Events

        private async void Timeframe_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                await SafeExecuteJs("window.isProcessingData = true;");
                _ctsGlobal?.Cancel();
                _ctsGlobal = new CancellationTokenSource();

                _currentTF = btn.Tag.ToString().ToLower();
                InitTimeframeButtons();
                await LoadBacktestData(_ctsGlobal.Token);
            }
        }
        private async void BtnCaptureHtf_Click(object sender, RoutedEventArgs e)
        {
            // On appelle la fonction JS définie plus haut
            await SafeExecuteJs("captureChart('HTF')");
            StatusHtf.Text = "✅ Capturé";
            StatusHtf.Foreground = System.Windows.Media.Brushes.LightGreen;
        }

        private async void BtnCaptureLtf_Click(object sender, RoutedEventArgs e)
        {
            await SafeExecuteJs("captureChart('LTF')");
            StatusLtf.Text = "✅ Capturé";
            StatusLtf.Foreground = Brushes.LightGreen;
        }
        private async void Watchlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WatchlistItems.SelectedItem is WatchlistSymbol selected)
            {
                _currentYear = DateTime.Now.Year;

                _ctsGlobal?.Cancel();
                _ctsGlobal = new CancellationTokenSource();
                var token = _ctsGlobal.Token;

                try
                {
                    _currentSymbol = selected.Symbol;
                    TxtCurrentSymbol.Text = _currentSymbol;

                    await LoadBacktestData(token);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    await SafeExecuteJs($"window.cyberLog('Erreur sélection: {ex.Message}', true);");
                }
            }
        }

        private async void ResetChart_Click(object sender, RoutedEventArgs e)
        {
            if (ChartBrowser?.CoreWebView2 != null)
            {
                await ChartBrowser.ExecuteScriptAsync("resetChart();");
            }
        }

        #endregion
    }

    public class TimeframeItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public bool IsActive { get; set; }
    }
}