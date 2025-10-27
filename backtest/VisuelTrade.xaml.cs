using System.Windows;
using System.Windows.Controls;

namespace backtest
{
    public partial class VisuelTrade : Window
    {
        public VisuelTrade(Trade trade)
        {
            InitializeComponent();

            // Chargez les images comme avant
            if (!string.IsNullOrEmpty(trade.ImageHtf))
            {
                string htfImageUrl = ConvertTradingViewLinkToImageUrl(trade.ImageHtf);
                LoadImageInBrowser(WebBrowserHtf, htfImageUrl);
            }

            if (!string.IsNullOrEmpty(trade.ImageLtf))
            {
                string ltfImageUrl = ConvertTradingViewLinkToImageUrl(trade.ImageLtf);
                LoadImageInBrowser(WebBrowserLtf, ltfImageUrl);
            }
        }

        private void DragWindow(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (e.ChangedButton == System.Windows.Input.MouseButton.Left)
                this.DragMove();
        }

        private void MinimizeWindow(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeWindow(object sender, RoutedEventArgs e)
        {
            this.WindowState = this.WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;
        }

        private void CloseWindow(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // Chargez les images avec la méthode existante
        private string ConvertTradingViewLinkToImageUrl(string tradingViewUrl)
        {
            if (tradingViewUrl.Contains("tradingview"))
            {
                try //https://www.tradingview.com/x/Qgs99PJR/  https://gocharting.com/sh/76c98804-309e-479f-9cb7-a4d3c2ade696  	https://gocharting.com/screenshots/76c98804-309e-479f-9cb7-a4d3c2ade696.png
                {
                    var parts = tradingViewUrl.Split('/');
                    string code = parts[parts.Length - 2]; // Le code est avant le dernier '/'

                    return $"https://s3.tradingview.com/snapshots/{code[0]}/{code}.png";
                }
                catch
                {
                    return "";
                }
            }
            else if (tradingViewUrl.Contains("gocharting"))
            {
                try {
                    var parts = tradingViewUrl.Split('/');
                    string code = parts[parts.Length - 1]; // Le code est apres le dernier '/'

                    return $"https://gocharting.com/screenshots/{code}.png";
                }
                catch
                {
                    return "";
                }
            }
            else
            {
                return tradingViewUrl;
            }
        }

        private void LoadImageInBrowser(WebBrowser browser, string imageUrl)
        {
            string html = $@"
                <html>
                <head>
                    <style>
                        body {{
                            margin: 0;
                            display: flex;
                            justify-content: center;
                            align-items: center;
                            height: 100vh;
                            background-color: #000000;
                        }}
                        img {{
                            max-width: 100%;
                            max-height: 100%;
                            object-fit: contain;
                        }}
                    </style>
                </head>
                <body>
                    <img src='{imageUrl}' alt='Image' />
                </body>
                </html>";
            browser.NavigateToString(html);
        }

        private void WebBrowserHtf_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

            
            if (WebBrowserLtf.Visibility == Visibility.Collapsed)
            {
                WebBrowserLtf.Visibility = Visibility.Visible;
            }
            else { WebBrowserLtf.Visibility = Visibility.Collapsed; }
        }

        private void WebBrowserLtf_MouseRightButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (WebBrowserHtf.Visibility == Visibility.Collapsed)
            {
                WebBrowserHtf.Visibility = Visibility.Visible;
            }
            else { WebBrowserHtf.Visibility = Visibility.Collapsed; }
        }

        private void WebBrowserHtf_MouseRightButtonDown_1(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {

        }
    }
}
