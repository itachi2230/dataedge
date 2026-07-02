using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Media;

namespace backtest
{
    public partial class Demo : Window
    {
        private int currentSlideIndex = 0;
        private const int TotalSlides = 4;

        public Demo()
        {
            InitializeComponent();
            ShowSlide(0, animate: false);
        }

        private void Window_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                this.DragMove();
            }
        }

        private void ShowSlide(int index, bool animate = true)
        {
            // Hide all slides
            Slide1.Visibility = Visibility.Collapsed;
            Slide2.Visibility = Visibility.Collapsed;
            Slide3.Visibility = Visibility.Collapsed;
            Slide4.Visibility = Visibility.Collapsed;

            // Reset animations
            Slide1.BeginAnimation(UIElement.OpacityProperty, null);
            Slide2.BeginAnimation(UIElement.OpacityProperty, null);
            Slide3.BeginAnimation(UIElement.OpacityProperty, null);
            Slide4.BeginAnimation(UIElement.OpacityProperty, null);
            Slide1.RenderTransform = new TranslateTransform(0, 30);
            Slide2.RenderTransform = new TranslateTransform(0, 30);
            Slide3.RenderTransform = new TranslateTransform(0, 30);
            Slide4.RenderTransform = new TranslateTransform(0, 30);

            // Show the target slide
            Grid targetSlide = index switch
            {
                0 => Slide1,
                1 => Slide2,
                2 => Slide3,
                3 => Slide4,
                _ => Slide1
            };

            targetSlide.Visibility = Visibility.Visible;

            if (animate)
            {
                // Animate in
                var storyboard = new Storyboard();

                var opacityAnim = new DoubleAnimation
                {
                    From = 0,
                    To = 1,
                    Duration = TimeSpan.FromSeconds(0.4),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(opacityAnim, targetSlide);
                Storyboard.SetTargetProperty(opacityAnim, new PropertyPath(UIElement.OpacityProperty));

                var translateAnim = new DoubleAnimation
                {
                    From = 30,
                    To = 0,
                    Duration = TimeSpan.FromSeconds(0.4),
                    EasingFunction = new QuadraticEase { EasingMode = EasingMode.EaseOut }
                };
                Storyboard.SetTarget(translateAnim, targetSlide);
                Storyboard.SetTargetProperty(translateAnim, new PropertyPath("(UIElement.RenderTransform).(TranslateTransform.Y)"));

                storyboard.Children.Add(opacityAnim);
                storyboard.Children.Add(translateAnim);
                storyboard.Begin();
            }
            else
            {
                targetSlide.Opacity = 1;
                targetSlide.RenderTransform = new TranslateTransform(0, 0);
            }

            // Update dots
            UpdateDots(index);

            // Update navigation buttons
            PreviousBtn.IsEnabled = index > 0;
            NextBtn.Content = index == TotalSlides - 1 ? "COMMENCER →" : "SUIVANT →";
        }

        private void UpdateDots(int activeIndex)
        {
            var dots = new[] { Dot1, Dot2, Dot3, Dot4 };
            for (int i = 0; i < dots.Length; i++)
            {
                if (i == activeIndex)
                {
                    dots[i].Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#00BFFF");
                    dots[i].Stroke = (SolidColorBrush)new BrushConverter().ConvertFrom("#00BFFF");
                    dots[i].Width = 10;
                    dots[i].Height = 10;
                }
                else
                {
                    dots[i].Fill = (SolidColorBrush)new BrushConverter().ConvertFrom("#333");
                    dots[i].Stroke = (SolidColorBrush)new BrushConverter().ConvertFrom("#555");
                    dots[i].Width = 8;
                    dots[i].Height = 8;
                }
            }
        }

        private void Next_Click(object sender, RoutedEventArgs e)
        {
            if (currentSlideIndex < TotalSlides - 1)
            {
                currentSlideIndex++;
                ShowSlide(currentSlideIndex);
            }
            else
            {
                // Last slide - close demo and open MainWindow
                OpenMainWindow();
            }
        }

        private void Previous_Click(object sender, RoutedEventArgs e)
        {
            if (currentSlideIndex > 0)
            {
                currentSlideIndex--;
                ShowSlide(currentSlideIndex);
            }
        }

        private void Skip_Click(object sender, RoutedEventArgs e)
        {
            OpenMainWindow();
        }

        private void OpenMainWindow()
        {
            MainWindow mainWindow = new MainWindow();
            mainWindow.Show();
            Application.Current.MainWindow = mainWindow;
            this.Close();
        }
    }
}