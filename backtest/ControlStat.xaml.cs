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
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace backtest
{
    /// <summary>
    /// Interaction logic for ControlStat.xaml
    /// </summary>
    public partial class ControlStat : UserControl
    {
        public ControlStat(string label="",double valeur=0)
        {
            InitializeComponent();
            controlLabel.Text = label;
            controlBar.Text = valeur.ToString() + " $";
            if (valeur < 0)
            {
                controlBar.Foreground = Brushes.Red;
            }
            else if (valeur == 0)
            {
                controlBar.Foreground = Brushes.Orange;
            }
            else
            {
                controlBar.Text = "+ " + valeur.ToString() +" $";
            }

            
        }
    }
}
