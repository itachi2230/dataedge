using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace backtest
{
    public partial class ControlStat : UserControl
    {
        // Événement public pour que MainWindow puisse s'abonner (même comportement que le double-clic)
        public event EventHandler StatsClicked;
        public event EventHandler BacktestClicked;

        public ControlStat(string label = "", double profit = 0, int tradeCount = 0, double winrate = 0, double profitFactor = 0)
        {
            InitializeComponent();
            controlLabel.Text = label;
            txtTrades.Text = tradeCount.ToString();
            txtWinrate.Text = $"{winrate:F0}%";
            txtProfitFactor.Text = profitFactor >= 99 ? "∞" : $"{profitFactor:F2}";

            Brush profitBrush;
            string profitText;
            if (profit < 0)
            {
                profitBrush = Brushes.OrangeRed;
                profitText = $"{profit:F0} $";
                StatusDot.Color = Colors.OrangeRed;
            }
            else if (profit == 0)
            {
                profitBrush = Brushes.Orange;
                profitText = "0 $";
                StatusDot.Color = Colors.Orange;
            }
            else
            {
                profitBrush = new SolidColorBrush(Color.FromRgb(0x3A, 0xF9, 0x09));
                profitText = $"+{profit:F0} $";
                StatusDot.Color = Color.FromRgb(0x3A, 0xF9, 0x09);
            }

            controlBar.Text = profitText;
            controlBar.Foreground = profitBrush;
        }

        private void BtnStats_Click(object sender, RoutedEventArgs e)
        {
            StatsClicked?.Invoke(this, EventArgs.Empty);
        }

        private void BtnBacktest_Click(object sender, RoutedEventArgs e)
        {
            BacktestClicked?.Invoke(this, EventArgs.Empty);
        }
    }
}
