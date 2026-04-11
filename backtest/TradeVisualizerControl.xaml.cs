using System;
using System.IO;
using System.Net;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace backtest
{
    public partial class TradeVisualizerControl : UserControl
    {
        // Dossier de cache à côté de l'exécutable
        private readonly string _cacheFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cacheimage");

        public TradeVisualizerControl()
        {
            InitializeComponent();

            // Créer le dossier s'il n'existe pas
            if (!Directory.Exists(_cacheFolder))
            {
                Directory.CreateDirectory(_cacheFolder);
            }
        }

        public void DisplayTrade(Trade trade)
        {
            ImgHtf.Source = null;
            ImgLtf.Source = null;

            if (string.IsNullOrEmpty(trade.ImageHtf) && string.IsNullOrEmpty(trade.ImageLtf))
            {
                NoImageText.Visibility = Visibility.Visible;
                return;
            }

            NoImageText.Visibility = Visibility.Collapsed;

            if (!string.IsNullOrEmpty(trade.ImageHtf))
                ProcessImage(ImgHtf, trade.ImageHtf, "HTF_" + trade.Id);

            if (!string.IsNullOrEmpty(trade.ImageLtf))
                ProcessImage(ImgLtf, trade.ImageLtf, "LTF_" + trade.Id);
        }

        private async void ProcessImage(Image imageControl, string rawValue, string fileNamePrefix)
        {
            if (string.IsNullOrWhiteSpace(rawValue)) return;

            string localPath = "";
            string directUrl = "";

            // 1. DISTINCTION ENTRE URL ET FICHIER LOCAL
            if (rawValue.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            {
                // C'est une URL (nouvelle version)
                directUrl = ConvertToDirectUrl(rawValue);
                string imageCode = Path.GetFileNameWithoutExtension(directUrl);
                localPath = Path.Combine(_cacheFolder, $"{imageCode}.png");
            }
            else
            {
                // C'est un nom de fichier (ancienne version de ton ami, ex: "122.png")
                // On s'assure que l'extension est présente
                string fileName = rawValue.EndsWith(".png") ? rawValue : rawValue + ".png";
                localPath = Path.Combine(_cacheFolder, fileName);
            }

            // 2. LOGIQUE DE CHARGEMENT / TÉLÉCHARGEMENT
            if (File.Exists(localPath))
            {
                // Si le fichier existe (soit migré, soit déjà caché), on charge
                LoadImageFromFile(imageControl, localPath);
            }
            else if (!string.IsNullOrEmpty(directUrl))
            {
                // Si le fichier n'existe pas MAIS qu'on a une URL, on télécharge
                await DownloadAndCacheImage(directUrl, localPath, imageControl);
            }
            else
            {
                // Le fichier local est introuvable et ce n'est pas une URL
                System.Diagnostics.Debug.WriteLine($"Fichier introuvable dans le cache : {localPath}");
                imageControl.Source = null;
            }
        }

        private void LoadImageFromFile(Image img, string path)
        {
            try
            {
                BitmapImage bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(path, UriKind.Absolute);
                bitmap.CacheOption = BitmapCacheOption.OnLoad; // Important pour libérer le fichier
                bitmap.EndInit();
                img.Source = bitmap;
            }
            catch (Exception ex) { System.Diagnostics.Debug.WriteLine("Erreur chargement local: " + ex.Message); }
        }
        private void Image_MouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            // On vérifie si c'est un double-clic (ClickCount == 2)
            if (e.ClickCount == 2)
            {
                if (sender is Border border && border.Child is Image img && img.Source != null)
                {
                    ZoomImageWindow zoomWin = new ZoomImageWindow(img.Source);
                    zoomWin.ShowDialog();
                    
                }
            }
            e.Handled = true;
        }
        private async System.Threading.Tasks.Task DownloadAndCacheImage(string url, string localPath, Image img)
        {
            try
            {
                using (WebClient client = new WebClient())
                {
                    // Téléchargement asynchrone pour ne pas freezer l'interface
                    await client.DownloadFileTaskAsync(new Uri(url), localPath);

                    // Une fois téléchargé, on l'affiche
                    LoadImageFromFile(img, localPath);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine("Erreur téléchargement: " + ex.Message);
                // Si le téléchargement échoue (ex: pas d'internet), on peut tenter de charger l'URL directement au cas où
            }
        }
        private string ConvertToDirectUrl(string url)
        {
            if (string.IsNullOrWhiteSpace(url)) return "";
            try
            {
                if (url.Contains("tradingview.com"))
                {
                    var parts = url.TrimEnd('/').Split('/');
                    string code = parts[parts.Length - 1];
                    return $"https://s3.tradingview.com/snapshots/{code[0].ToString().ToLower()}/{code}.png";
                }
                else if (url.Contains("gocharting.com"))
                {
                    var parts = url.TrimEnd('/').Split('/');
                    return $"https://gocharting.com/screenshots/{parts[parts.Length - 1]}.png";
                }
            }
            catch { }
            return url;
        }

        
    }
}