using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Linq;
using CefSharp;
using CefSharp.Wpf;
using backtest.services;
using Newtonsoft.Json;

namespace backtest
{
    public partial class Chart : UserControl
    {
        private readonly ChartBridge _chartBridge;
        private readonly Dataservice _dataService;

        private string _currentSymbol = "EURUSD";
        private string _currentTF = "15m";
        private int _currentYear = DateTime.Now.Year;
        private bool _isLoadingMore = false;
        private bool _endOfDataReached = false;

        public Chart()
        {
            InitializeComponent();

            // 1. Charger les derniers réglages sauvegardés avant d'initialiser le reste
            LoadUserSettings();

            _dataService = new Dataservice("https://fxdataedge.com/");
            _chartBridge = new ChartBridge(this);

            InitTimeframeButtons();
            InitBrowser();
            LoadWatchlist();
        }

        private void LoadUserSettings()
        {
            // Récupération des réglages (avec valeurs par défaut si vide)
            _currentSymbol = !string.IsNullOrEmpty(Properties.Settings.Default.LastSymbol)
                             ? Properties.Settings.Default.LastSymbol : "EURUSD";
            TxtCurrentSymbol.Text = _currentSymbol;
            _currentTF = !string.IsNullOrEmpty(Properties.Settings.Default.LastTimeframe)
                         ? Properties.Settings.Default.LastTimeframe : "15m";
        }

        private void SaveUserSettings()
        {
            // Sauvegarde dans les paramètres de l'application
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

                // Optionnel : Sélectionner visuellement la paire actuelle dans la liste
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
                new TimeframeItem { Name = "1h", Value = "1h" },
                new TimeframeItem { Name = "4h", Value = "4h" },
                new TimeframeItem { Name = "D", Value = "d" }
            };

            foreach (var item in tfs) item.IsActive = (item.Value.ToLower() == _currentTF.ToLower());
            TimeframeSelector.ItemsSource = tfs;
        }

        private void InitBrowser()
        {
            var settings = new BrowserSettings { WebGl = CefState.Enabled, DefaultEncoding = "UTF-8" };
            ChartBrowser.BrowserSettings = settings;
            ChartBrowser.JavascriptObjectRepository.Settings.LegacyBindingEnabled = true;

            ChartBrowser.JavascriptObjectRepository.Register("chartService", _chartBridge, isAsync: true);

            ChartBrowser.FrameLoadEnd += async (s, e) => {
                if (e.Frame.IsMain) await Dispatcher.InvokeAsync(async () => await LoadBacktestData());
            };

            string indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources/chart", "index.html");
            if (File.Exists(indexPath)) ChartBrowser.Address = indexPath;
        }

        public async Task LoadBacktestData()
        {
            _endOfDataReached = false;
            try
            {
                SetStatus("Chargement...", "#FFB900");

                // On s'assure que l'année est remise au maximum pour un changement de paire/TF
                if (!_isLoadingMore) _currentYear = DateTime.Now.Year;

                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, _currentYear.ToString());

                if (result.success)
                {
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath));
                    if (candles.Count > 0)
                    {
                        string json = JsonConvert.SerializeObject(candles);
                        await ChartBrowser.EvaluateScriptAsync($"updateChartData({json}, '{_currentSymbol}');");

                        SetStatus(_currentSymbol + " " + _currentTF, "#00FF7F");
                        SaveUserSettings(); // On sauvegarde à chaque succès de chargement
                        return;
                    }
                }
                SetStatus("Aucune donnée", "#FF4B4B");
            }
            catch (Exception ex) { SetStatus("Erreur: " + ex.Message, "#FF4B4B"); }
        }

        private void SetStatus(string msg, string colorHex)
        {
            TxtStatus.Text = msg.ToUpper();
            TxtStatus.Foreground = (System.Windows.Media.Brush)new System.Windows.Media.BrushConverter().ConvertFromString(colorHex);
        }

        public async Task LoadMoreData()
        {
            if (_isLoadingMore || _endOfDataReached) return;
            _isLoadingMore = true;

            try
            {
                _currentYear--;
                SetStatus($"Chargement historique {_currentYear}...", "#FFB900");

                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, _currentYear.ToString());

                if (result.success)
                {
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath));
                    if (candles.Count > 0)
                    {
                        string json = JsonConvert.SerializeObject(candles);
                        await ChartBrowser.EvaluateScriptAsync($"prependChartData({json});");
                        SetStatus(_currentSymbol + " " + _currentTF, "#00FF7F");
                        return;
                    }
                }

                _endOfDataReached = true;
                SetStatus("FIN DES DONNÉES", "#FF4B4B");
            }
            catch { }
            finally { _isLoadingMore = false; }
        }

        private List<CandleModel> ParseCsvToCandles(string filePath)
        {
            var candles = new List<CandleModel>();
            var culture = CultureInfo.InvariantCulture;
            try
            {
                using (var reader = new StreamReader(filePath))
                {
                    reader.ReadLine(); // Skip header
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var parts = line.Split(',');
                        if (parts.Length < 5) continue;

                        DateTime dt = DateTime.ParseExact(parts[0], "yyyy.MM.dd HH:mm:ss", culture);
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
            }
            catch { }
            return candles;
        }

        #region Events

        private async void Timeframe_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag != null)
            {
                _currentTF = btn.Tag.ToString().ToLower();
                _currentYear = DateTime.Now.Year;
                InitTimeframeButtons();
                await LoadBacktestData();
            }
        }

        private async void Watchlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WatchlistItems.SelectedItem is WatchlistSymbol selected)
            {
                // Mise à jour de la paire
                _currentSymbol = selected.Symbol;
                _currentYear = DateTime.Now.Year;
                TxtCurrentSymbol.Text = _currentSymbol;
                // On relance le chargement complet
                await LoadBacktestData();
            }
        }

        private void ResetChart_Click(object sender, RoutedEventArgs e)
        {
            if (ChartBrowser.IsBrowserInitialized)
                ChartBrowser.ExecuteScriptAsync("resetChart();");
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