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
            _dataService = new Dataservice("https://fxdataedge.com/");
            _chartBridge = new ChartBridge(this); // On passe l'instance au bridge

            InitTimeframeButtons();
            InitBrowser();
            LoadWatchlist();
        }

        private async void LoadWatchlist()
        {
            try
            {
                var list = await _dataService.GetWatchlistAsync();
                WatchlistItems.ItemsSource = list;
            }
            catch (Exception ex) { SetStatus("Erreur Watchlist: " + ex.Message, "#FF4B4B"); }
        }

        private void InitTimeframeButtons()
        {
            // Liste des timeframes pour le sélecteur
            var tfs = new List<TimeframeItem>
            {
                new TimeframeItem { Name = "1m", Value = "1m" },
                new TimeframeItem { Name = "5m", Value = "5m" },
                new TimeframeItem { Name = "15m", Value = "15m" },
                new TimeframeItem { Name = "1h", Value = "1h" },
                new TimeframeItem { Name = "4h", Value = "4h" },
                new TimeframeItem { Name = "D", Value = "d" }
            };

            // Marquer la TF actuelle comme active
            foreach (var item in tfs) item.IsActive = (item.Value == _currentTF);

            TimeframeSelector.ItemsSource = tfs;
        }

        private void InitBrowser()
        {
            var settings = new BrowserSettings { WebGl = CefState.Enabled, DefaultEncoding = "UTF-8" };
            ChartBrowser.BrowserSettings = settings;
            ChartBrowser.JavascriptObjectRepository.Settings.LegacyBindingEnabled = true;
            
            // Liaison du bridge
            ChartBrowser.JavascriptObjectRepository.Register("chartService", _chartBridge, isAsync: true);

            ChartBrowser.FrameLoadEnd += async (s, e) => {
                if (e.Frame.IsMain) await Dispatcher.InvokeAsync(async () => await LoadBacktestData());
            };

            string indexPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources/chart", "index.html");
            if (File.Exists(indexPath)) ChartBrowser.Address = indexPath;
        }

        private async void OnBrowserFrameLoadEnd(object sender, FrameLoadEndEventArgs e)
        {
            if (e.Frame.IsMain)
            {
                await Dispatcher.InvokeAsync(async () => await LoadBacktestData());
            }
        }

        public async Task LoadBacktestData()
        {
            _endOfDataReached = false;
            try
            {
                SetStatus("Chargement...", "#FFB900");
                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, _currentYear.ToString());

                if (result.success)
                {
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath));
                    if (candles.Count > 0)
                    {
                        string json = JsonConvert.SerializeObject(candles);

                        // CORRECTION ICI : Bien séparer json et symbol par une virgule en dehors des quotes
                        await ChartBrowser.EvaluateScriptAsync($"updateChartData({json}, '{_currentSymbol}');");

                        SetStatus("Connecté", "#00FF7F");
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

        // Pagination : Appelée lors du scroll vers l'arrière
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

                        // On ajoute les données au début (le JS gère le "isProcessingData" pour éviter les bugs)
                        await ChartBrowser.EvaluateScriptAsync($"prependChartData({json});");

                        SetStatus("Historique ajouté", "#00FF7F");
                        _isLoadingMore = false;
                        return;
                    }
                }

                _endOfDataReached = true;
                SetStatus("FIN DES DONNÉES", "#FF4B4B");
            }
            catch { _isLoadingMore = false; }
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
                    reader.ReadLine();
                    while (!reader.EndOfStream)
                    {
                        var line = reader.ReadLine();
                        var parts = line.Split(',');
                        if (parts.Length < 6) continue;
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
                // Mise à jour visuelle des boutons
                InitTimeframeButtons();

                await LoadBacktestData();
            }
        }

        private async void Watchlist_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (WatchlistItems.SelectedItem is WatchlistSymbol selected)
            {
                _currentSymbol = selected.Symbol;
                _currentYear = DateTime.Now.Year;
                await LoadBacktestData();
            }
        }

       // private void ResetChart_Click(object sender, RoutedEventArgs e)
       // {
         //   if (ChartBrowser.IsBrowserInitialized)
       //         ChartBrowser.ExecuteScriptAsync("chart.timeScale().fitContent();");
       // }

        #endregion
    }

    // Classe pour gérer l'état des boutons Timeframe
    public class TimeframeItem
    {
        public string Name { get; set; }
        public string Value { get; set; }
        public bool IsActive { get; set; }
    }
}