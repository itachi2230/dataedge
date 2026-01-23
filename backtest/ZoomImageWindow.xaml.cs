using System.Windows;
using System.Windows.Media;

namespace backtest
{
    public partial class ZoomImageWindow : Window
    {
        public ZoomImageWindow(ImageSource source)
        {
            InitializeComponent();
            FullImage.Source = source;
        }

        private void Window_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            this.Close(); // Ferme si on clique sur le fond sombre
        }

        private void Close_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}