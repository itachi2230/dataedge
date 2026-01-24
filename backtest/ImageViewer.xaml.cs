using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace backtest
{
    public partial class ImageViewer : Window
    {
        // Constructeur pour une seule image
        public ImageViewer(ImageSource imageUn)
        {
            InitializeComponent();
            AfficherImages(imageUn, null);
        }

        // Constructeur pour deux images (Haut et Bas)
        public ImageViewer(ImageSource imageHaut, ImageSource imageBas)
        {
            InitializeComponent();
            AfficherImages(imageHaut, imageBas);
        }

        private void AfficherImages(ImageSource img1, ImageSource img2)
        {
            if (img1 != null)
            {
                ImageHaut.Source = img1;
                ImageHaut.Visibility = Visibility.Visible;
            }

            if (img2 != null)
            {
                ImageBas.Source = img2;
                ImageBas.Visibility = Visibility.Visible;
                // Si on a deux images, on peut réduire un peu la marge
                ImageHaut.Margin = new Thickness(0, 0, 0, 10);
            }
            else
            {
                // Si une seule image, pas de marge en bas
                ImageHaut.Margin = new Thickness(0);
            }
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            this.Close();
        }
    }
}