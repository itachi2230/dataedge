using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace backtest // Adaptez le namespace si nécessaire
{
    public partial class ImageViewer : Window
    {
        public ImageViewer(BitmapSource imageSource)
        {
            InitializeComponent();
            FullScreenImage.Source = imageSource;
        }

        // Fermer la fenêtre lorsque l'on clique n'importe où
        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.Close();
        }
    }
}