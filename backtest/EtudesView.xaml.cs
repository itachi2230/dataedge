using Microsoft.Win32;
using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace backtest
{
    public partial class EtudesControl : UserControl
    {
        private const string StudiesRootPath = "etudes";
        private bool IsStudyModified = false;
        private string CurrentStudyPath = null;
        private const string AssemblyName = "backtest";
        
        public EtudesControl()
        {
            InitializeComponent();
            this.Unloaded += (s, e) => EnsureSavedState(CurrentStudyPath);
            InitializeStudiesModule();
            
        }

        private void InitializeStudiesModule()
        {
            if (!Directory.Exists(StudiesRootPath)) Directory.CreateDirectory(StudiesRootPath);
            LoadStudiesTree();
        }

        // =================================================================
        // 1. GESTION DU PACKAGE .ETUDE (XAMLPACKAGE)
        // =================================================================

        private void EnsureSavedState(string packagePath)
        {
            if (!IsStudyModified || string.IsNullOrEmpty(packagePath)) return;
            try
            {
                using (FileStream fs = new FileStream(packagePath, FileMode.Create))
                {
                    TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);
                    range.Save(fs, DataFormats.XamlPackage);
                }
                IsStudyModified = false;
            }
            catch (Exception ex) { MessageBox.Show("Erreur sauvegarde : " + ex.Message); }
        }

        private void LoadStudyFile(string packagePath)
        {
            try
            {
                if (File.Exists(packagePath))
                {
                    using (FileStream fs = new FileStream(packagePath, FileMode.Open))
                    {
                        TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);
                        if (fs.Length > 0) range.Load(fs, DataFormats.XamlPackage);
                    }
                    CurrentStudyPath = packagePath;
                    IsStudyModified = false;
                    ApplyImageSizingAndEvents();
                }
            }
            catch (Exception ex) { MessageBox.Show("Erreur chargement : " + ex.Message); }
        }

        private void ApplyImageSizingAndEvents()
        {
            foreach (Block block in StudyContentRichTextBox.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    foreach (Inline inline in p.Inlines)
                    {
                        if (inline is InlineUIContainer container && container.Child is Image img)
                        {
                            img.MaxWidth = 300; // Augmenté un peu pour la lisibilité
                            img.Stretch = Stretch.Uniform;
                            img.Cursor = Cursors.Hand;
                        }
                    }
                }
            }
        }

        // =================================================================
        // 2. OPTIMISATION ET INSERTION DES IMAGES (NETTOYAGE)
        // =================================================================

        private void StudyContentRichTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            if (e.DataObject.GetDataPresent(typeof(BitmapSource)))
            {
                if (e.DataObject.GetData(typeof(BitmapSource)) is BitmapSource bitmap)
                {
                    // On compresse avant d'insérer
                    BitmapSource compressed = CompressImage(bitmap);
                    InsertImage(compressed);
                    e.CancelCommand();
                }
            }
        }
        //ZD9!R4m@82Lp

        private BitmapSource CompressImage(BitmapSource source)
        {
            // 1. Redimensionnement
            double maxWidth = 1000; // On peut descendre à 1000px pour gagner encore plus de place
            double scale = source.PixelWidth > maxWidth ? maxWidth / source.PixelWidth : 1.0;

            TransformedBitmap resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));

            // 2. Encodage JPEG avec une compression un peu plus forte (70 au lieu de 80)
            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = 70; // 70% est invisible à l'œil nu mais gagne 30% de poids vs 80%
            encoder.Frames.Add(BitmapFrame.Create(resized));

            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Seek(0, SeekOrigin.Begin);

                // 3. IMPORTANT : On crée l'image en forçant le chargement immédiat du flux compressé
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.StreamSource = ms;
                result.CacheOption = BitmapCacheOption.OnLoad; // Force la mise en mémoire du flux compressé
                result.EndInit();
                result.Freeze();

                return result;
            }
        }
        private void InsertImage(BitmapSource source)
        {
            var img = new Image
            {
                Source = source,
                MaxWidth = 300,
                Stretch = Stretch.Uniform,
                Cursor = Cursors.Hand
            };
            new InlineUIContainer(img, StudyContentRichTextBox.CaretPosition);
            IsStudyModified = true;
        }

        // =================================================================
        // 3. ACTIONS ET INTERFACE (FORMATAGE)
        // =================================================================

        private void Bold_Click(object sender, RoutedEventArgs e) => ToggleProperty(TextElement.FontWeightProperty, FontWeights.Bold, FontWeights.Normal);
        private void Italic_Click(object sender, RoutedEventArgs e) => ToggleProperty(TextElement.FontStyleProperty, FontStyles.Italic, FontStyles.Normal);

        private void ToggleProperty(DependencyProperty prop, object val, object norm)
        {
            var current = StudyContentRichTextBox.Selection.GetPropertyValue(prop);
            StudyContentRichTextBox.Selection.ApplyPropertyValue(prop, current.Equals(val) ? norm : val);
        }

        private void Underline_Click(object sender, RoutedEventArgs e)
        {
            var decorations = (TextDecorationCollection)StudyContentRichTextBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            StudyContentRichTextBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty,
                (decorations != null && decorations.Count > 0) ? null : TextDecorations.Underline);
        }

        private void FontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudyContentRichTextBox != null && FontSizeComboBox.SelectedItem is ComboBoxItem item)
            {
                if (double.TryParse(item.Content.ToString(), out double size))
                    StudyContentRichTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, size);
            }
        }

        private void AlignLeft_Click(object sender, RoutedEventArgs e) => StudyContentRichTextBox.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Left);
        private void AlignCenter_Click(object sender, RoutedEventArgs e) => StudyContentRichTextBox.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Center);
        private void AlignRight_Click(object sender, RoutedEventArgs e) => StudyContentRichTextBox.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Right);

        private void FontColor_Click(object sender, RoutedEventArgs e)
        {
            using (var colorDialog = new System.Windows.Forms.ColorDialog())
            {
                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    var wpfColor = Color.FromArgb(colorDialog.Color.A, colorDialog.Color.R, colorDialog.Color.G, colorDialog.Color.B);
                    var brush = new SolidColorBrush(wpfColor);
                    StudyContentRichTextBox.Selection.ApplyPropertyValue(TextElement.ForegroundProperty, brush);
                    if (SelectedColorIndicator != null) SelectedColorIndicator.Fill = brush;
                }
            }
        }

        private void Numbering_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleNumbering.Execute(null, StudyContentRichTextBox);
        private void Bullets_Click(object sender, RoutedEventArgs e) => EditingCommands.ToggleBullets.Execute(null, StudyContentRichTextBox);

        private void StudyContentRichTextBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            var pos = StudyContentRichTextBox.GetPositionFromPoint(e.GetPosition(StudyContentRichTextBox), true);
            if (pos?.Parent is InlineUIContainer container && container.Child is Image img)
            {
                if (img.Source is BitmapSource src)
                {
                    new ZoomImageWindow(src).ShowDialog();
                    e.Handled = true;
                }
            }
        }

        // =================================================================
        // 4. TREEVIEW ET NAVIGATION
        // =================================================================

        private void LoadStudiesTree()
        {
            StudiesTreeView.Items.Clear();
            var root = new TreeViewItem { Header = CreateHeaderContent("Analyses Local", "ROOT"), Tag = StudiesRootPath, IsExpanded = true };
            PopulateTreeView(root, StudiesRootPath);
            StudiesTreeView.Items.Add(root);
        }

        private void PopulateTreeView(TreeViewItem parent, string path)
        {
            foreach (string dir in Directory.GetDirectories(path))
            {
                var item = new TreeViewItem { Header = CreateHeaderContent(Path.GetFileName(dir), "FOLDER"), Tag = dir };
                parent.Items.Add(item);
                PopulateTreeView(item, dir);
            }
            foreach (string f in Directory.GetFiles(path, "*.etude"))
                parent.Items.Add(new TreeViewItem { Header = CreateHeaderContent(Path.GetFileNameWithoutExtension(f), "FILE"), Tag = f });
        }

        private void StudiesTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CurrentStudyPath != null) EnsureSavedState(CurrentStudyPath);
            if (StudiesTreeView.SelectedItem is TreeViewItem item && item.Tag is string path)
            {
                if (File.Exists(path) && path.EndsWith(".etude"))
                {
                    LoadStudyFile(path);
                    StudiesTitle.Text = $"ÉTUDE : {Path.GetFileNameWithoutExtension(path).ToUpper()}";
                }
                else
                {
                    CurrentStudyPath = null;
                    StudiesTitle.Text = "DOSSIER SÉLECTIONNÉ";
                }
            }
        }

        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            InputDialog dialog = new InputDialog("Nom du dossier :");
            if (dialog.ShowDialog() != true) return;
            string folderName = dialog.InputValue.Trim();
            if (string.IsNullOrEmpty(folderName)) return;
            string parentPath;
            GetCurrentParentItem(out parentPath);
            string newPath = Path.Combine(parentPath, folderName);
            if (!Directory.Exists(newPath)) { Directory.CreateDirectory(newPath); LoadStudiesTree(); }
        }

        private void NewStudy_Click(object sender, RoutedEventArgs e)
        {
            string parentPath;
            GetCurrentParentItem(out parentPath);
            InputDialog dialog = new InputDialog("Nom de l'étude :");
            if (dialog.ShowDialog() != true) return;
            string name = dialog.InputValue.Trim();
            if (string.IsNullOrEmpty(name)) return;
            string packagePath = Path.Combine(parentPath, name + ".etude");
            try
            {
                using (FileStream fs = File.Create(packagePath))
                {
                    TextRange tr = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);
                    tr.Text = "";
                    tr.Save(fs, DataFormats.XamlPackage);
                }
                LoadStudiesTree();
            }
            catch (Exception ex) { MessageBox.Show(ex.Message); }
        }

        private TreeViewItem GetCurrentParentItem(out string parentPath)
        {
            parentPath = StudiesRootPath;
            if (StudiesTreeView.SelectedItem is TreeViewItem selectedItem && selectedItem.Tag is string path)
            {
                parentPath = Directory.Exists(path) ? path : Path.GetDirectoryName(path);
                return Directory.Exists(path) ? selectedItem : (TreeViewItem)selectedItem.Parent;
            }
            return (TreeViewItem)StudiesTreeView.Items[0];
        }

        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            if (!(StudiesTreeView.SelectedItem is TreeViewItem selectedItem)) return;
            string path = selectedItem.Tag.ToString();
            if (path == StudiesRootPath) return;
            if (MessageBox.Show("Supprimer ?", "Confirmation", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
            {
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else File.Delete(path);
                LoadStudiesTree();
                StudyContentRichTextBox.Document.Blocks.Clear();
                CurrentStudyPath = null;
            }
        }

        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            if (!(StudiesTreeView.SelectedItem is TreeViewItem selectedItem) || selectedItem.Tag.ToString() == StudiesRootPath) return;
            string oldPath = selectedItem.Tag.ToString();
            bool isDir = Directory.Exists(oldPath);
            string oldName = isDir ? Path.GetFileName(oldPath) : Path.GetFileNameWithoutExtension(oldPath);
            InputDialog dialog = new InputDialog("Nouveau nom :", oldName);
            if (dialog.ShowDialog() == true)
            {
                string newPath = Path.Combine(Path.GetDirectoryName(oldPath), dialog.InputValue.Trim() + (isDir ? "" : ".etude"));
                if (isDir) Directory.Move(oldPath, newPath); else File.Move(oldPath, newPath);
                LoadStudiesTree();
            }
        }

        private void ExportRTF_Click(object sender, RoutedEventArgs e)
        {
            // 1. On prépare le document (copie pour ne pas modifier l'affichage actuel)
            FlowDocument docToPrint = CopyDocument(StudyContentRichTextBox.Document);

            // 2. Configuration du PrintDialog
            PrintDialog printDialog = new PrintDialog();

            // On peut soit forcer l'imprimante PDF, soit laisser l'utilisateur choisir
            if (printDialog.ShowDialog() == true)
            {
                // Ajustement de la mise en page pour le papier
                docToPrint.PageHeight = printDialog.PrintableAreaHeight;
                docToPrint.PageWidth = printDialog.PrintableAreaWidth;
                docToPrint.PagePadding = new Thickness(50); // Marges propres
                docToPrint.ColumnGap = 0;
                docToPrint.ColumnWidth = printDialog.PrintableAreaWidth;

                // On lance l'impression (si l'utilisateur choisit "Microsoft Print to PDF", il aura son fichier)
                IDocumentPaginatorSource idpSource = docToPrint;
                printDialog.PrintDocument(idpSource.DocumentPaginator, "Exportation Étude");
            }
        }

        // Fonction utilitaire pour copier le document (évite les erreurs de thread)
        private FlowDocument CopyDocument(FlowDocument source)
        {
            var copy = new FlowDocument();
            using (var ms = new MemoryStream())
            {
                var sourceRange = new TextRange(source.ContentStart, source.ContentEnd);
                sourceRange.Save(ms, DataFormats.XamlPackage);
                var copyRange = new TextRange(copy.ContentStart, copy.ContentEnd);
                copyRange.Load(ms, DataFormats.XamlPackage);
            }
            return copy;
        }

        private object CreateHeaderContent(string name, string type)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            string iconName = type == "FILE" ? "file.png" : "folder.png";
            Image img = CreateIconImage(iconName, 22);
            if (img != null) sp.Children.Add(img);
            sp.Children.Add(new TextBlock { Text = name, Margin = new Thickness(5, 0, 0, 0), Foreground = Brushes.White });
            return sp;
        }

        private Image CreateIconImage(string name, double size)
        {
            try
            {
                return new Image { Source = new BitmapImage(new Uri($"pack://application:,,,/{AssemblyName};component/Resources/{name}")), Width = size, Height = size };
            }
            catch { return null; }
        }

        private void BtnRetour_Click(object sender, RoutedEventArgs e)
        {
            EnsureSavedState(CurrentStudyPath);
            if (Application.Current.MainWindow is MainWindow mw) mw.ShowDashboard();
        }
        private async void MigrateAndCleanupFiles()
        {
            string[] rtfFiles = Directory.GetFiles(StudiesRootPath, "*.rtf", SearchOption.AllDirectories);
            if (rtfFiles.Length == 0) return;

            MigrationOverlay.Visibility = Visibility.Visible;
            MigrationProgress.Maximum = rtfFiles.Length;
            var filesToDelete = new System.Collections.Generic.List<string>();

            // On lance la migration dans un thread séparé
            await Task.Run(() =>
            {
                foreach (string oldFilePath in rtfFiles)
                {
                    // On crée un thread STA pour chaque fichier afin de manipuler le RichTextBox sans bloquer l'UI
                    var thread = new System.Threading.Thread(() =>
                    {
                        try
                        {
                            RichTextBox converterBuffer = new RichTextBox();
                            TextRange range = new TextRange(converterBuffer.Document.ContentStart, converterBuffer.Document.ContentEnd);

                            using (FileStream fs = new FileStream(oldFilePath, FileMode.Open, FileAccess.Read))
                            {
                                range.Load(fs, DataFormats.Rtf);
                            }

                            // On compresse les images ici (dans ce thread séparé !)
                            foreach (Block block in converterBuffer.Document.Blocks)
                            {
                                if (block is Paragraph p)
                                {
                                    foreach (Inline inline in p.Inlines)
                                    {
                                        if (inline is InlineUIContainer container && container.Child is Image img)
                                        {
                                            if (img.Source is BitmapSource src)
                                            {
                                                img.Source = CompressImage(src);
                                            }
                                        }
                                    }
                                }
                            }

                            string newFilePath = Path.ChangeExtension(oldFilePath, ".etude");
                            using (FileStream fs = new FileStream(newFilePath, FileMode.Create, FileAccess.ReadWrite))
                            {
                                range.Save(fs, DataFormats.XamlPackage);
                            }

                            lock (filesToDelete) { filesToDelete.Add(oldFilePath); }
                        }
                        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
                    });

                    // Configurer le thread en STA est obligatoire pour manipuler des objets WPF (Image, RichTextBox)
                    thread.SetApartmentState(System.Threading.ApartmentState.STA);
                    thread.Start();
                    thread.Join(); // On attend que ce fichier soit fini avant de passer au suivant

                    // On met à jour la barre de progression sans bloquer
                    Dispatcher.BeginInvoke(new Action(() => {
                        MigrationProgress.Value += 1;
                        MigrationStatus.Text = $"Migration : {Path.GetFileName(oldFilePath)}";
                    }));
                }
            });

            MigrationOverlay.Visibility = Visibility.Collapsed;
            if (filesToDelete.Count > 0)
            {
                var result = MessageBox.Show(
                    $"{filesToDelete.Count} fichiers migrés. Supprimer les originaux ?",
                    "Succès", MessageBoxButton.YesNo);

                if (result == MessageBoxResult.Yes)
                {
                    foreach (string path in filesToDelete)
                    {
                        try { File.Delete(path); } catch { }
                    }
                }
            }

            LoadStudiesTree();
        }
        private void StudyContentRichTextBox_TextChanged(object sender, TextChangedEventArgs e) => IsStudyModified = true;
        private void SaveStudy_Click(object sender, RoutedEventArgs e) => EnsureSavedState(CurrentStudyPath);
        private void StudiesTreeView_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Delete) Delete_Click(sender, e); }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            MigrateAndCleanupFiles();
        }
    }
}