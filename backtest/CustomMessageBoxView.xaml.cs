using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace backtest
{
    public partial class CustomMessageBoxView : Window
    {
        public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

        // Géométries vectorielles (SVG)
        private const string GeometryInfo = "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M13,17H11V11H13V17M13,9H11V7H13V9Z";
        private const string GeometryError = "M12,2L1,21H23L12,2M12,17A1,1 0 1,1 11,18A1,1 0 0,1 12,17M11,10H13V15H11V10Z";
        private const string GeometryQuestion = "M12,2A10,10 0 1,0 22,12A10,10 0 0,0 12,2M12,18A1.5,1.5 0 1,1 13.5,16.5A1.5,1.5 0 0,1 12,18M12,15A3,3 0 0,1 9,12H11A1,1 0 0,0 12,13A1,1 0 0,0 13,12A1,1 0 0,0 12,11A3,3 0 0,1 15,8A3,3 0 0,1 12,5A3,3 0 0,1 9,8H11A1,1 0 0,0 12,7A1,1 0 0,0 13,8A1,1 0 0,0 12,9A3,3 0 0,1 9,12H11";

        public CustomMessageBoxView(string message, string caption, MessageBoxButton buttons, MessageBoxImage icon)
        {
            InitializeComponent();
            MessageTextBlock.Text = message;
            TitleTextBlock.Text = caption;
            SetupIcon(icon);
            SetupButtons(buttons);
        }

        private void SetupIcon(MessageBoxImage icon)
        {
            switch (icon)
            {
                case MessageBoxImage.Information:
                    IconPath.Data = Geometry.Parse(GeometryInfo);
                    IconPath.Fill = Brushes.DodgerBlue;
                    break;
                case MessageBoxImage.Error:
                    IconPath.Data = Geometry.Parse(GeometryError);
                    IconPath.Fill = Brushes.Crimson;break;
                
                case MessageBoxImage.Question:
                    IconPath.Data = Geometry.Parse(GeometryQuestion);
                    IconPath.Fill = Brushes.MediumSeaGreen;
                    break;
                
                default:
                    IconPath.Data = Geometry.Parse(GeometryQuestion);
                    IconPath.Fill = Brushes.MediumSeaGreen;
                    break;
            }
        }

        private void SetupButtons(MessageBoxButton buttons)
        {
            if (buttons == MessageBoxButton.OK) BtnOk.Visibility = Visibility.Visible;
            else if (buttons == MessageBoxButton.OKCancel) { BtnOk.Visibility = Visibility.Visible; BtnCancel.Visibility = Visibility.Visible; }
            else if (buttons == MessageBoxButton.YesNo) { BtnYes.Visibility = Visibility.Visible; BtnNo.Visibility = Visibility.Visible; }
            else if (buttons == MessageBoxButton.YesNoCancel) { BtnYes.Visibility = Visibility.Visible; BtnNo.Visibility = Visibility.Visible; BtnCancel.Visibility = Visibility.Visible; }
        }

        private void Btn_Click(object sender, RoutedEventArgs e)
        {
            string tag = (sender as Button).Tag.ToString();
            Result = (MessageBoxResult)System.Enum.Parse(typeof(MessageBoxResult), tag);
            this.DialogResult = true;
            this.Close();
        }

        private void TitleBar_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e) => DragMove();
    }
    public static class MessageBox
    {
        public static MessageBoxResult Show(string message, string caption = "Information", MessageBoxButton button = MessageBoxButton.OK, MessageBoxImage icon = MessageBoxImage.None)
        {
            var msg = new CustomMessageBoxView(message, caption, button, icon);
            msg.ShowDialog();
            return msg.Result;
        }
    }
}