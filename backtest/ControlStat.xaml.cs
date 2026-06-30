using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace backtest
{
    public partial class ControlStat : UserControl
    {
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

            double barRatio = Math.Min(Math.Abs(profit) / 500.0, 1.0);
            if (profit == 0) barRatio = 0.05;
            ProfitBarFill.Width = barRatio * ActualWidth;
            Loaded += (_, __) =>
            {
                var parent = ProfitBarFill.Parent as Grid;
                if (parent != null && parent.ActualWidth > 0)
                    ProfitBarFill.Width = barRatio * parent.ActualWidth;
            };
        }
    }
}
