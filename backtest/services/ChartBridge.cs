using System;
using System.Windows; // <--- C'est ce namespace qui manquait pour 'Application'
using CefSharp;

namespace backtest
{
    public class ChartBridge
    {
        private readonly Chart _chartInstance;

        public ChartBridge(Chart instance)
        {
            _chartInstance = instance;
        }
        public void OnSetupCreated(string jsonDrawing)
        {
            // Ici tu reçois le type de dessin, le prix et le temps.
            // Tu peux le parser pour remplir tes formulaires de trading automatiquement.
        }
        /// <summary>
        /// Cette méthode est appelée par le JavaScript (chart_index.html) 
        /// via window.chartService.loadPreviousYear()
        /// </summary>
        public void loadPreviousYear()
        {
            // Log pour debug (optionnel : regarde ta console de sortie VS)
            System.Diagnostics.Debug.WriteLine("Appel JS reçu !");

            Application.Current.Dispatcher.Invoke(async () =>
            {
                if (_chartInstance != null)
                {
                    await _chartInstance.LoadMoreData();
                }
            });
        }
    }
}