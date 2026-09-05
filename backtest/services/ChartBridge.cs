using System;
using System.Windows;
using System.Runtime.InteropServices; 
using Newtonsoft.Json;
using System.IO;
using backtest.Services;

namespace backtest.services
{
    [ComVisible(true)]
    public class ChartBridge
    {
        private readonly Chart _chartInstance;
        string _cacheFolder = TradeVisualizerControl._cacheFolder;
        public ChartBridge(Chart instance)
        {
            _chartInstance = instance;
        }

        /// <summary>
        /// Sauvegarde la position de replay pour une stratégie/paire/timeframe.
        /// Appelé depuis JS quand l'utilisateur quitte le mode replay.
        /// </summary>
        public void SaveReplayPosition(string symbol, string timeframe, long timestamp)
        {
            var strategy = _chartInstance.StrategyName;
            ReplayPositionStore.Set(strategy, symbol, timeframe, timestamp);
        }

        /// <summary>
        /// Retourne le timestamp UNIX de la dernière position replay sauvegardée
        /// pour la stratégie/paire/timeframe courante, ou 0 si aucun replay n'a été fait.
        /// Appelé depuis JS au démarrage du replay.
        /// </summary>
        public long GetReplayPositionTimestamp(string symbol, string timeframe)
        {
            var strategy = _chartInstance.StrategyName;
            var pos = ReplayPositionStore.Get(strategy, symbol, timeframe);
            return pos.HasValue ? pos.Value : 0;
        }

        public void OnSetupCreated(string jsonDrawing)
        {
            // Logique de dessin
        }
        public void LoadYearForBacktest(int year)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                // On passe 'true' si on veut vider le graphique avant le jump (plus propre pour le replay)
                await _chartInstance.LoadYearForBacktest(year, true);
            });
        }
        public void loadPreviousYear(long firstVisibleTimestamp)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                if (_chartInstance != null)
                    await _chartInstance.LoadMoreData(firstVisibleTimestamp, true);
            });
        }
       public void loadNextYear(long lastVisibleTimestamp)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                if (_chartInstance != null)
                    await _chartInstance.LoadMoreData(lastVisibleTimestamp, false);
            });
        }
        public void LoadPreviousYearForReplay(long firstVisibleTimestamp)
        {
            Application.Current.Dispatcher.Invoke(async () =>
            {
                if (_chartInstance != null)
                    await _chartInstance.LoadPreviousYearForReplay(firstVisibleTimestamp);
            });
        }
        public void ExitReplayMode()
        {
            // On appelle la méthode de sortie sur l'instance de Chart
            _chartInstance.Dispatcher.Invoke(() => _chartInstance.ExitReplayAndGoToPresent());
        }
        // Dans ChartBridge.cs
        public void OnTradeSetupCompleted(string jsonTrade)
        {
            // On repasse sur le thread UI pour modifier les TextBox/ComboBox
            Application.Current.Dispatcher.Invoke(() =>
            {
                try
                {
                    var trade = JsonConvert.DeserializeObject<Trade>(jsonTrade);
                    if (trade != null)
                    {
                        _chartInstance.MaintabControl.SelectedIndex = 1;
                        _chartInstance.PopulateTradeForm(trade);
                    }
                }
                catch (Exception ex)
                {
                    // Loguer l'erreur si nécessaire
                }
            });
        }
        public void SaveChartScreenshot(string type, string base64Data)
        {
            try
            {
                // Nettoyage de la chaîne Base64 (on retire "data:image/png;base64,")
                string base64 = base64Data.Split(',')[1];
                byte[] imageBytes = Convert.FromBase64String(base64);

                // Génération du nom de fichier : 202605031024_HTF.png
                string timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");
                string fileName = $"{timestamp}_{type.ToUpper()}.png";
                string fullPath = Path.Combine(_cacheFolder, fileName);

                // Création du dossier si manquant
                if (!Directory.Exists(_cacheFolder)) Directory.CreateDirectory(_cacheFolder);

                // Enregistrement physique
                File.WriteAllBytes(fullPath, imageBytes);

                // Notification à l'UI WPF (via un événement ou une action)
                // Ici, on met à jour le chemin dans l'objet Trade en cours
                Application.Current.Dispatcher.Invoke(() => {
                    _chartInstance.UpdateCurrentTradeImagePath(type, fileName);
                });
            }
            catch (Exception ex)
            {
                MessageBox.Show("Erreur capture : " + ex.Message);
            }
        }

       
    }
}
