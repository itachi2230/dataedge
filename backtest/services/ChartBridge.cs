using System;
using System.Windows;
using System.Runtime.InteropServices; // <--- AJOUTÉ pour [ComVisible]

namespace backtest.services
{
    // Indispensable pour WebView2 : cela permet au JS de voir cette classe
    [ComVisible(true)]
    public class ChartBridge
    {
        private readonly Chart _chartInstance;

        public ChartBridge(Chart instance)
        {
            _chartInstance = instance;
        }

        public void OnSetupCreated(string jsonDrawing)
        {
            // Logique de dessin
        }

        public void loadPreviousYear()
        {
            System.Diagnostics.Debug.WriteLine("Appel JS reçu via WebView2 !");

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