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
    /// Interaction logic for newvisu.xaml
    /// </summary>
    public partial class newvisu : Window
    {
        public newvisu(Trade trade)
        {
            InitializeComponent();
            TradeVisualizer.DisplayTrade(trade);
        }
        private void ExternalGrid_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Si on arrive ici, c'est qu'on a cliqué sur la Grid (l'extérieur du border)
            this.Close();
        }

        private void VisualContainer_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            // Empêche le clic de "traverser" le border et d'atteindre la Grid
            e.Handled = true;
        }
    }
}
