using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Imaging;

namespace backtest
{
    public partial class Demo : Window
    {
        private List<string> slides = new List<string>
        {
            "Images/slide1.png",
            "Images/slide2.png",
            "Images/slide3.png"
        };

        private int currentSlideIndex = 0;

        public Demo()
        {
            InitializeComponent();
            LoadSlide();
        }
        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void LoadSlide()
        {
            if (currentSlideIndex >= 0 && currentSlideIndex < slides.Count)
            {
                SlideImage.Source = new BitmapImage(new Uri(slides[currentSlideIndex], UriKind.Relative));
            }

            // Désactiver le bouton Précédent si on est sur le premier slide
            PreviousSlideButton.IsEnabled = currentSlideIndex > 0;

            // Désactiver le bouton Suivant si on est sur le dernier slide
            NextSlideButton.Content = currentSlideIndex == slides.Count - 1 ? "Terminer" : "Suivant";
        }

        private void NextSlide_Click(object sender, RoutedEventArgs e)
        {
            if (currentSlideIndex < slides.Count - 1)
            {
                currentSlideIndex++;
                LoadSlide();
            }
            else
            {
                // Fermer la fenêtre de démo et ouvrir la fenêtre principale
                
                MainWindow mainWindow = new MainWindow();
                mainWindow.Show();
                Application.Current.MainWindow = mainWindow;
                this.Close();
            }
        }

        private void PreviousSlide_Click(object sender, RoutedEventArgs e)
        {
            if (currentSlideIndex > 0)
            {
                currentSlideIndex--;
                LoadSlide();
            }
        }
    }
}
