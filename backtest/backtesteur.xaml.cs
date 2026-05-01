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
            receveur.Children.Add(new Chart(st));
        }
    }
}
