using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace backtest
{
    /// <summary>
    /// Interaction logic for backtesteur.xaml
    /// </summary>
    public partial class backtesteur : Window
    {
        Strategie st;
        public backtesteur(Strategie st)
        {
            InitializeComponent();
            this.st = st;
            var chart = new Chart(st);
            receveur.Children.Add(chart);
            
            // Sauvegarde la position replay à la fermeture de la fenêtre
            this.Closing += async (s, e) =>
            {
                try
                {
                    await chart.SafeExecuteJs("saveReplayPosition(true);");
                }
                catch { }
            };
        }
        protected override void OnSourceInitialized(EventArgs e)
        {
            base.OnSourceInitialized(e);
            // Maximisation correcte : sans ce hook, WindowChrome (GlassFrameThickness=0)
            // agrandit la fenêtre à la zone de travail + bordure système → contenu
            // coupé d'environ 8 px sur les 4 bords. Le hook force la taille exacte.
            backtest.Services.MaximizeHelper.Hook(this);
        }
        private void MinimizeClick(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeClick(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Maximized)
                this.WindowState = WindowState.Normal;
            else
                this.WindowState = WindowState.Maximized;
        }

        private void CloseClick(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
