using System;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Controls;

namespace backtest
{
    public static class RichTextService
    {
        // Sauvegarder au format XamlPackage (.etude)
        public static void SavePackage(RichTextBox rtb, string filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return;
            TextRange range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            using (FileStream fs = new FileStream(filePath, FileMode.Create))
            {
                range.Save(fs, DataFormats.XamlPackage);
            }
        }

        // Charger au format XamlPackage
        public static void LoadPackage(RichTextBox rtb, string filePath)
        {
            if (!File.Exists(filePath))
            {
                rtb.Document.Blocks.Clear();
                return;
            }
            TextRange range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            using (FileStream fs = new FileStream(filePath, FileMode.Open))
            {
                if (fs.Length > 0) range.Load(fs, DataFormats.XamlPackage);
            }
        }

        // Compression d'image (Ton code optimisé)
        public static BitmapSource CompressImage(BitmapSource source)
        {
            double maxWidth = 1000;
            double scale = source.PixelWidth > maxWidth ? maxWidth / source.PixelWidth : 1.0;
            TransformedBitmap resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));

            JpegBitmapEncoder encoder = new JpegBitmapEncoder { QualityLevel = 70 };
            encoder.Frames.Add(BitmapFrame.Create(resized));

            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Seek(0, SeekOrigin.Begin);
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.StreamSource = ms;
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }

        // Appliquer le style aux images (Taille et curseur)
        public static void FormatImagesInDocument(RichTextBox rtb,int tailleImages=400)
        {
            foreach (Block block in rtb.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    foreach (Inline inline in p.Inlines)
                    {
                        if (inline is InlineUIContainer container && container.Child is Image img)
                        {
                            img.MaxWidth = tailleImages;
                            img.Stretch = Stretch.Uniform;
                            img.Cursor = System.Windows.Input.Cursors.Hand;

                        }
                    }
                }
            }
        }

        private static void Image_MouseLeftButtonDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            if (sender is Image img && img.Source is BitmapSource src)
            {
                // On utilise la fenêtre de zoom que tu as déjà créée
                new ZoomImageWindow(src).ShowDialog();
                e.Handled = true;
            }
        }
    }
}