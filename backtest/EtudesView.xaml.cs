using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace backtest
{
    // ---------------------------------------------------------------------
    // Nœud d'arborescence construit hors du thread UI (scan disque)
    // ---------------------------------------------------------------------
    internal class StudyNode
    {
        public string Name { get; set; }
        public string Path { get; set; }
        public bool IsDirectory { get; set; }
        public List<StudyNode> Children { get; } = new List<StudyNode>();
    }

    public partial class EtudesControl : UserControl
    {
        private const string StudiesRootPath = "etudes";

        // Le nom utilisé par les URI pack:// est <AssemblyName> du .csproj ("dataedge"),
        // pas le namespace C# ("backtest").
        private const string AssemblyResource = "dataedge";
        private const string ResourcePrefix = "resources/";

        private bool IsStudyModified = false;
        private string CurrentStudyPath = null;

        private int _treeRefreshVersion;
        private string _searchFilter = "";
        private CancellationTokenSource _loadCts;

        // Icônes décodées une seule fois puis figées (thread-safe)
        private static ImageSource _folderIcon;
        private static ImageSource _fileIcon;

        public EtudesControl()
        {
            InitializeComponent();
            this.Unloaded += (s, e) => EnsureSavedState(CurrentStudyPath);
            InitializeStudiesModule();
        }

        private void InitializeStudiesModule()
        {
            if (!Directory.Exists(StudiesRootPath))
            {
                try { Directory.CreateDirectory(StudiesRootPath); } catch { }
            }
            UpdateStatusInfo();
            LoadStudiesTree();
        }

        // =================================================================
        // RÉ-OUVERTURE DEPUIS LE DASHBOARD (l'instance est conservée en mémoire)
        // =================================================================
        public void RefreshFromDashboard()
        {
            if (IsStudyModified)
            {
                StudiesStatusText.Text = "Étude non enregistrée — l'arbre sera actualisé après la sauvegarde.";
                return;
            }
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
                StudiesStatusText.Text = "Enregistré : " + Path.GetFileName(packagePath);
                UpdateStatusInfo();
            }
            catch (Exception ex) { MessageBox.Show("Erreur sauvegarde : " + ex.Message); }
        }

        public void LoadStudyFile(string packagePath)
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
            catch (Exception ex)
            {
                CurrentStudyPath = null;
                MessageBox.Show("Erreur chargement : " + ex.Message);
            }
            finally
            {
                UpdateEmptyPlaceholder();
                UpdateStatusInfo();
            }
        }

        public void ApplyImageSizingAndEvents()
        {
            foreach (Block block in StudyContentRichTextBox.Document.Blocks)
            {
                if (block is Paragraph p)
                {
                    foreach (Inline inline in p.Inlines)
                    {
                        if (inline is InlineUIContainer container && container.Child is Image img)
                        {
                            img.MaxWidth = 300;
                            img.Stretch = Stretch.Uniform;
                            img.Cursor = Cursors.Hand;
                        }
                    }
                }
            }
        }
// Placeholder de design : caché dès qu'un document contient du texte
        private void UpdateEmptyPlaceholder()
        {
            if (EmptyPlaceholder == null || StudyContentRichTextBox == null) return;
            try
            {
                TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);
                EmptyPlaceholder.Visibility = range.Text.Length > 0 ? Visibility.Collapsed : Visibility.Visible;
            }
            catch { }
        }

        // Barre de statut : fichier courant, taille, caractères, état modifié
        private void UpdateStatusInfo()
        {
            if (StudiesStatusText == null || StudyContentRichTextBox == null) return;
            try
            {
                TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);
                int charCount = range.Text.Length;

                string info = "";
                if (!string.IsNullOrEmpty(CurrentStudyPath) && File.Exists(CurrentStudyPath))
                {
                    long size = new FileInfo(CurrentStudyPath).Length;
                    info += Path.GetFileName(CurrentStudyPath) + " · " + FormatBytes(size);
                    if (charCount > 0) info += " · " + charCount + " caractères";
                }
                else if (charCount > 0)
                {
                    info += charCount + " caractères";
                }

                if (IsStudyModified) info += " · ● modifié";
                if (string.IsNullOrEmpty(info)) info = "Prêt";

                StudiesStatusText.Text = info;
            }
            catch { }
        }

        private static string FormatBytes(long bytes)
        {
            if (bytes < 1024) return bytes + " o";
            if (bytes < 1024 * 1024) return (bytes / 1024d).ToString("0.#") + " Ko";
            return (bytes / (1024d * 1024d)).ToString("0.##") + " Mo";
        }

        // =================================================================
        // 2. OPTIMISATION ET INSERTION DES IMAGES (NETTOYAGE)
        // =================================================================

        public void StudyContentRichTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
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

        public static BitmapSource CompressImage(BitmapSource source)
        {
            // 1. Redimensionnement (max 1000 px de large)
            double maxWidth = 1000;
            double scale = source.PixelWidth > maxWidth ? maxWidth / source.PixelWidth : 1.0;
            TransformedBitmap resized = new TransformedBitmap(source, new ScaleTransform(scale, scale));

            // 2. Encodage JPEG (qualité 70 : invisible à l'œil nu, ~30% plus léger)
            JpegBitmapEncoder encoder = new JpegBitmapEncoder();
            encoder.QualityLevel = 70;
            encoder.Frames.Add(BitmapFrame.Create(resized));

            using (MemoryStream ms = new MemoryStream())
            {
                encoder.Save(ms);
                ms.Seek(0, SeekOrigin.Begin);

                // 3. On force la mise en mémoire du flux compressé puis on fige l'image
                BitmapImage result = new BitmapImage();
                result.BeginInit();
                result.StreamSource = ms;
                result.CacheOption = BitmapCacheOption.OnLoad;
                result.EndInit();
                result.Freeze();
                return result;
            }
        }

        public void InsertImage(BitmapSource source)
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
            UpdateStatusInfo();
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
        // 4. TREEVIEW ET NAVIGATION (scan asynchrone)
        // =================================================================

        private void LoadStudiesTree(string filter = null)
        {
            _treeRefreshVersion++;
            int version = _treeRefreshVersion;
            if (filter != null) _searchFilter = filter;
            string activeFilter = _searchFilter;

            if (!Directory.Exists(StudiesRootPath))
            {
                try { Directory.CreateDirectory(StudiesRootPath); } catch { }
            }

            if (!HasStudyOpen())
                StudiesStatusText.Text = "Chargement de la structure...";

            Task.Run(() => ScanStudies(StudiesRootPath, activeFilter))
                .ContinueWith(t =>
                {
                    // Résultat obsolète : un scan plus récent a été lancé entre-temps
                    if (version != _treeRefreshVersion) return;
                    if (t.IsFaulted)
                    {
                        if (!HasStudyOpen())
                            StudiesStatusText.Text = "Erreur de lecture du dossier études.";
                        return;
                    }

                    List<StudyNode> nodes = t.Result;
                    StudiesTreeView.Items.Clear();

                    var root = new TreeViewItem
                    {
                        Header = CreateHeaderContent("Analyses Local", "ROOT"),
                        Tag = StudiesRootPath,
                        IsExpanded = true
                    };
                    BuildTreeViewFromNodes(root, nodes);
                    StudiesTreeView.Items.Add(root);

                    int fileCount = CountFiles(nodes);
                    StudiesCountText.Text = fileCount + " étude" + (fileCount > 1 ? "s" : "");

                    if (!HasStudyOpen())
                    {
                        StudiesStatusText.Text = string.IsNullOrEmpty(activeFilter)
                            ? "Prêt"
                            : "Filtre actif : « " + activeFilter + " »";
                    }
                }, TaskScheduler.FromCurrentSynchronizationContext());
        }

        private bool HasStudyOpen()
        {
            return !string.IsNullOrEmpty(CurrentStudyPath) && File.Exists(CurrentStudyPath);
        }

        // Scan récursif effectué HORS du thread UI
        private static List<StudyNode> ScanStudies(string path, string filter)
        {
            var result = new List<StudyNode>();
            string[] dirs, files;
            try
            {
                dirs = Directory.GetDirectories(path);
                files = Directory.GetFiles(path, "*.etude");
            }
            catch
            {
                return result;
            }

            Array.Sort(dirs, StringComparer.OrdinalIgnoreCase);
            Array.Sort(files, StringComparer.OrdinalIgnoreCase);

            foreach (string dir in dirs)
            {
                var node = new StudyNode { Name = Path.GetFileName(dir), Path = dir, IsDirectory = true };
                node.Children.AddRange(ScanStudies(dir, filter));
                if (MatchFilter(node.Name, filter) || node.Children.Count > 0)
                    result.Add(node);
            }
            foreach (string f in files)
            {
                string name = Path.GetFileNameWithoutExtension(f);
                if (MatchFilter(name, filter))
                    result.Add(new StudyNode { Name = name, Path = f, IsDirectory = false });
            }
            return result;
        }

        private static bool MatchFilter(string name, string filter)
        {
            return string.IsNullOrWhiteSpace(filter) ||
                   name.IndexOf(filter, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static int CountFiles(List<StudyNode> nodes)
        {
            int count = 0;
            foreach (var n in nodes)
            {
                if (n.IsDirectory) count += CountFiles(n.Children);
                else count++;
            }
            return count;
        }

        private void BuildTreeViewFromNodes(TreeViewItem parent, List<StudyNode> nodes)
        {
            foreach (var node in nodes)
            {
                var item = new TreeViewItem
                {
                    Header = CreateHeaderContent(node.Name, node.IsDirectory ? "FOLDER" : "FILE"),
                    Tag = node.Path
                };
                if (node.IsDirectory)
                {
                    item.IsExpanded = true;
                    BuildTreeViewFromNodes(item, node.Children);
                }
                parent.Items.Add(item);
            }
        }
private async void StudiesTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            if (CurrentStudyPath != null) EnsureSavedState(CurrentStudyPath);

            if (StudiesTreeView.SelectedItem is TreeViewItem item && item.Tag is string path)
            {
                if (File.Exists(path) && path.EndsWith(".etude"))
                {
                    StudiesTitle.Text = "ÉTUDE : " + Path.GetFileNameWithoutExtension(path).ToUpper();

                    // Debounce : on annule le chargement précédent (navigation clavier rapide)
                    _loadCts?.Cancel();
                    _loadCts = new CancellationTokenSource();
                    CancellationToken token = _loadCts.Token;

                    try
                    {
                        await Task.Delay(300, token);
                    }
                    catch (TaskCanceledException)
                    {
                        return;
                    }

                    // Vérifie que la sélection n'a pas changé pendant le délai
                    if (StudiesTreeView.SelectedItem is TreeViewItem current &&
                        current.Tag is string currentPath &&
                        string.Equals(currentPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        StudiesStatusText.Text = "Chargement de l'étude...";
                        LoadStudyFile(path);
                    }
                }
                else
                {
                    CurrentStudyPath = null;
                    StudiesTitle.Text = "DOSSIER SÉLECTIONNÉ";
                    UpdateStatusInfo();
                }
            }
        }

        private void StudiesSearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            _searchFilter = StudiesSearchBox?.Text ?? "";
            LoadStudiesTree(_searchFilter);
        }
// =================================================================
        // 5. ACTIONS SUR LES FICHIERS / DOSSIERS
        // =================================================================

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
                UpdateEmptyPlaceholder();
                UpdateStatusInfo();
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

        // =================================================================
        // 6. EXPORT PDF / IMPRESSION
        // =================================================================

        private void ExportRTF_Click(object sender, RoutedEventArgs e)
        {
            // Copie du document pour ne pas modifier l'affichage actuel
            FlowDocument docToPrint = CopyDocument(StudyContentRichTextBox.Document);

            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                docToPrint.PageHeight = printDialog.PrintableAreaHeight;
                docToPrint.PageWidth = printDialog.PrintableAreaWidth;
                docToPrint.PagePadding = new Thickness(50);
                docToPrint.ColumnGap = 0;
                docToPrint.ColumnWidth = printDialog.PrintableAreaWidth;

                IDocumentPaginatorSource idpSource = docToPrint;
                printDialog.PrintDocument(idpSource.DocumentPaginator, "Exportation Étude");
            }
        }

        // Copie le document par sérialisation (évite les erreurs de thread)
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
// =================================================================
        // 7. PRÉSENTATION DE L'ARBRE (icônes en cache)
        // =================================================================

        private object CreateHeaderContent(string name, string type)
        {
            StackPanel sp = new StackPanel { Orientation = Orientation.Horizontal };
            Image img = CreateIconImage(type == "FILE");
            if (img != null) sp.Children.Add(img);
            sp.Children.Add(new TextBlock
            {
                Text = name,
                Margin = new Thickness(5, 0, 0, 0),
                Foreground = Brushes.White,
                VerticalAlignment = VerticalAlignment.Center
            });
            return sp;
        }

        private static Image CreateIconImage(bool isFile)
        {
            try
            {
                ImageSource source = GetCachedIcon(isFile);
                return source == null ? null : new Image { Source = source, Width = 16, Height = 16 };
            }
            catch { return null; }
        }

        private static ImageSource GetCachedIcon(bool isFile)
        {
            ImageSource icon = isFile ? _fileIcon : _folderIcon;
            if (icon != null) return icon;

            // L'URI pack:// doit matcher <AssemblyName> du .csproj ("dataedge")
            // et le chemin réel des ressources ("resources/", en minuscules).
            string iconName = isFile ? "file.png" : "folder.png";
            var bmp = new BitmapImage(new Uri(
                "pack://application:,,,/" + AssemblyResource + ";component/" + ResourcePrefix + iconName,
                UriKind.Absolute));
            bmp.Freeze();
            if (isFile) _fileIcon = bmp; else _folderIcon = bmp;
            return bmp;
        }

        private void BtnRetour_Click(object sender, RoutedEventArgs e)
        {
            EnsureSavedState(CurrentStudyPath);
            if (Application.Current.MainWindow is MainWindow mw) mw.ShowDashboard();
        }
// =================================================================
        // 8. MIGRATION DES ANCIENS FORMATS RTF → .ETUDE
        // =================================================================

        private async void MigrateAndCleanupFiles()
        {
            string[] rtfFiles = Directory.GetFiles(StudiesRootPath, "*.rtf", SearchOption.AllDirectories);
            if (rtfFiles.Length == 0) return;

            MigrationOverlay.Visibility = Visibility.Visible;
            MigrationProgress.Maximum = rtfFiles.Length;
            var filesToDelete = new List<string>();

            await Task.Run(() =>
            {
                foreach (string oldFilePath in rtfFiles)
                {
                    // Thread STA obligatoire pour manipuler des objets WPF (Image, RichTextBox)
                    var thread = new Thread(() =>
                    {
                        try
                        {
                            RichTextBox converterBuffer = new RichTextBox();
                            TextRange range = new TextRange(converterBuffer.Document.ContentStart, converterBuffer.Document.ContentEnd);

                            using (FileStream fs = new FileStream(oldFilePath, FileMode.Open, FileAccess.Read))
                            {
                                range.Load(fs, DataFormats.Rtf);
                            }

                            // Compression des images dans ce thread séparé
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

                    thread.SetApartmentState(ApartmentState.STA);
                    thread.Start();
                    thread.Join();

                    Dispatcher.BeginInvoke(new Action(() =>
                    {
                        MigrationProgress.Value += 1;
                        MigrationStatus.Text = "Migration : " + Path.GetFileName(oldFilePath);
                    }));
                }
            });

            MigrationOverlay.Visibility = Visibility.Collapsed;
            if (filesToDelete.Count > 0)
            {
                var result = MessageBox.Show(
                    filesToDelete.Count + " fichiers migrés. Supprimer les originaux ?",
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
// =================================================================
        // 9. ÉVÉNEMENTS DIVERS
        // =================================================================

        private void StudyContentRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            IsStudyModified = true;
            UpdateEmptyPlaceholder();
            UpdateStatusInfo();
        }

        private void SaveStudy_Click(object sender, RoutedEventArgs e) => EnsureSavedState(CurrentStudyPath);
        private void StudiesTreeView_KeyDown(object sender, KeyEventArgs e) { if (e.Key == Key.Delete) Delete_Click(sender, e); }

        private void Button_Click(object sender, RoutedEventArgs e)
        {
            MigrateAndCleanupFiles();
        }
    }
}