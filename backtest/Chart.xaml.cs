using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Globalization;
using System.Linq;
using backtest.services;
using Newtonsoft.Json;
using System.Threading;
using Microsoft.Web.WebView2.Core;

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

        private CancellationTokenSource _ctsGlobal;

        public Chart()
        {
            InitializeComponent();
            LoadUserSettings();

            _dataService = new Dataservice("https://fxdataedge.com/");
            _chartBridge = new ChartBridge(this);

            InitTimeframeButtons();
            LoadWatchlist();
            InitBrowser(); // On initialise le browser, c'est lui qui déclenchera la suite
            _ctsGlobal = new CancellationTokenSource();
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

        // Dans Chart.xaml.cs

        public async Task LoadYearForBacktest(int year, bool sr=true)
        {
            if (_isLoadingMore) return;
            _isLoadingMore = true;

            try
            {
                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, year.ToString(), _ctsGlobal.Token);

                if (result.success)
                {
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath));
                    if (candles != null)
                    {
                        _currentYear = year;
                        string json = JsonConvert.SerializeObject(candles);
                        // On utilise une fonction JS différente pour ne pas déclencher le centrage de 2026
                        await SafeExecuteJs($"window.setupBacktestData({json}, {year});");
                    }
                }
            }
            catch (Exception ex)
            {
                await SafeExecuteJs($"window.cyberLog('Erreur : {ex.Message}', true);");
            }
            finally { _isLoadingMore = false; }
        }
        public async Task LoadBacktestData(CancellationToken ct)
        {
            // Sécurité : si le browser n'est pas encore initialisé, on quitte
            if (ChartBrowser?.CoreWebView2 == null) return;

            _endOfDataReached = false;
            _isLoadingMore = false;

            try
            {
                SetStatus("Chargement...", "#FFB900");

                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, _currentYear.ToString(), ct);

                if (result.success)
                {
                    ct.ThrowIfCancellationRequested();
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath), ct);

                    if (candles != null && candles.Count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        string json = JsonConvert.SerializeObject(candles);

                        // On s'assure d'être sur le thread UI pour le JS
                        await Dispatcher.InvokeAsync(async () => {
                            await SafeExecuteJs($"updateChartData({json}, '{_currentSymbol}');");
                        });

                        SetStatus(_currentSymbol + " OK", "#00FF7F");
                        SaveUserSettings();
                        return;
                    }
                }

                await SafeExecuteJs($"updateChartData([], '{_currentSymbol}');");
                SetStatus("Aucune donnée", "#FF4B4B");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex) { SetStatus("Erreur: " + ex.Message, "#FF4B4B"); }
        }
        public async Task LoadMoreData()
        {
            if (_isLoadingMore || _endOfDataReached) return;

            if (_ctsGlobal == null) _ctsGlobal = new CancellationTokenSource();
            var ct = _ctsGlobal.Token;

            _isLoadingMore = true;

            try
            {
                int yearToLoad = _currentYear - 1;
                SetStatus($"Historique {yearToLoad}...", "#FFB900");

                var result = await _dataService.GetMarketDataAsync(_currentSymbol, _currentTF, yearToLoad.ToString(), ct);

                if (result.success)
                {
                    ct.ThrowIfCancellationRequested();
                    var candles = await Task.Run(() => ParseCsvToCandles(result.filePath), ct);

                    if (candles != null && candles.Count > 0)
                    {
                        ct.ThrowIfCancellationRequested();
                        _currentYear = yearToLoad;
                        string json = JsonConvert.SerializeObject(candles);
                        await SafeExecuteJs($"prependChartData({json});");
                        SetStatus(_currentSymbol + " " + _currentTF, "#00FF7F");
                        return;
                    }
                }

                _endOfDataReached = true;
                SetStatus("FIN DES DONNÉES", "#FF4B4B");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                await SafeExecuteJs($"cyberLog('Erreur LoadMore: {ex.Message.Replace("'", "\\'")}');");
            }
            finally
            {
                _isLoadingMore = false;
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
                    // On échappe bien les quotes pour éviter de casser le script JS
                    string safeError = errorMsg.Replace("'", "\\'").Replace("\r", "").Replace("\n", "");
                    ChartBrowser.ExecuteScriptAsync($"cyberLog('{safeError}', true);");
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

                    await SafeExecuteJs($"window.cyberLog('Changement vers {_currentSymbol}...');");
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