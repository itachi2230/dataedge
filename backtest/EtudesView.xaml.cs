using Microsoft.Win32; // NÉCESSAIRE POUR SAVEFILEDIALOG
using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging; // Nécessaire pour BitmapImage

namespace backtest
{
    public partial class EtudesView : Window
    {
        // Chemin d'accès où les études seront stockées
        private const string StudiesRootPath = "Etudes";
        private bool IsStudyModified = false;
        private string CurrentStudyPath = null;
        // Nom de l'assemblage (votre projet)
        private const string AssemblyName = "backtest";

        public EtudesView()
        {
            InitializeComponent();
            InitializeStudiesModule();
        }

        private void InitializeStudiesModule()
        {
            // Initialiser la ComboBox de taille de police (à ajouter si non fait dans le XAML)
            // FontSizeComboBox.ItemsSource = new double[] { 10, 12, 14, 16, 18, 24, 36, 48 };
            // FontSizeComboBox.SelectedValue = 14.0; 

            // Charger l'arborescence des études au démarrage 
            LoadStudiesTree();
        }

        // =================================================================
        // 1. CONTRÔLES DE FENÊTRE (Gestion du style WindowStyle="None")
        // ... (Contrôles de fenêtre inchangés) ...

        private void TitleBar_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                if (this.WindowState == WindowState.Maximized)
                {
                    this.WindowState = WindowState.Normal;
                    // Note: System.Windows.Forms n'est pas inclus ici. Si vous utilisez WPF pur, 
                    // la position du curseur doit être gérée différemment ou simplement ignorée.
                }
                this.DragMove();
            }
        }

        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void MaximizeRestoreButton_Click(object sender, RoutedEventArgs e)
        {
            if (this.WindowState == WindowState.Normal)
            {
                this.WindowState = WindowState.Maximized;
            }
            else
            {
                this.WindowState = WindowState.Normal;
            }
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }

        // =================================================================
        // 2. GESTION DU FORMATAGE (Lié au RichTextBox)
        // ... (Fonctions de formatage inchangées) ...

        private void Bold_Click(object sender, RoutedEventArgs e)
        {
            // Applique la commande Bold au texte sélectionné
            StudyContentRichTextBox.Selection.ApplyPropertyValue(
                TextElement.FontWeightProperty,
                StudyContentRichTextBox.Selection.GetPropertyValue(TextElement.FontWeightProperty).Equals(FontWeights.Bold) ? FontWeights.Normal : FontWeights.Bold
            );
        }

        private void Italic_Click(object sender, RoutedEventArgs e)
        {
            // Applique la commande Italic au texte sélectionné
            StudyContentRichTextBox.Selection.ApplyPropertyValue(
                TextElement.FontStyleProperty,
                StudyContentRichTextBox.Selection.GetPropertyValue(TextElement.FontStyleProperty).Equals(FontStyles.Italic) ? FontStyles.Normal : FontStyles.Italic
            );
        }

        private void Underline_Click(object sender, RoutedEventArgs e)
        {
            // Applique la commande Underline au texte sélectionné
            TextDecorationCollection decorations = (TextDecorationCollection)StudyContentRichTextBox.Selection.GetPropertyValue(Inline.TextDecorationsProperty);
            if (decorations != null && decorations.Contains(TextDecorations.Underline[0]))
            {
                StudyContentRichTextBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, null);
            }
            else
            {
                StudyContentRichTextBox.Selection.ApplyPropertyValue(Inline.TextDecorationsProperty, TextDecorations.Underline);
            }
        }

        private void FontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (StudyContentRichTextBox != null && FontSizeComboBox != null && FontSizeComboBox.SelectedItem != null)
            {
                double fontSize = Convert.ToDouble(FontSizeComboBox.Text);
                StudyContentRichTextBox.Selection.ApplyPropertyValue(TextElement.FontSizeProperty, fontSize);
            }
        }

        private void AlignLeft_Click(object sender, RoutedEventArgs e)
        {
            StudyContentRichTextBox.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Left);
        }

        private void AlignCenter_Click(object sender, RoutedEventArgs e)
        {
            StudyContentRichTextBox.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Center);
        }

        private void AlignRight_Click(object sender, RoutedEventArgs e)
        {
            StudyContentRichTextBox.Selection.ApplyPropertyValue(Block.TextAlignmentProperty, TextAlignment.Right);
        }

        private void FontColor_Click(object sender, RoutedEventArgs e)
        {
            using (System.Windows.Forms.ColorDialog colorDialog = new System.Windows.Forms.ColorDialog())
            {
                object currentForeground = StudyContentRichTextBox.Selection.GetPropertyValue(TextElement.ForegroundProperty);
                if (currentForeground is SolidColorBrush solidColor)
                {
                    System.Drawing.Color currentClr = System.Drawing.Color.FromArgb(
                        solidColor.Color.A, solidColor.Color.R, solidColor.Color.G, solidColor.Color.B
                    );
                    colorDialog.Color = currentClr;
                }

                if (colorDialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                {
                    System.Drawing.Color selectedClr = colorDialog.Color;
                    System.Windows.Media.Color wpfColor = System.Windows.Media.Color.FromArgb(
                        selectedClr.A, selectedClr.R, selectedClr.G, selectedClr.B
                    );

                    SolidColorBrush newColorBrush = new SolidColorBrush(wpfColor);

                    // Appliquer la nouvelle couleur au texte sélectionné
                    StudyContentRichTextBox.Selection.ApplyPropertyValue(
                        TextElement.ForegroundProperty,
                        newColorBrush
                    );

                    // Mise à jour de l'indicateur
                    if (SelectedColorIndicator != null)
                    {
                        SelectedColorIndicator.Fill = newColorBrush;
                    }
                }
            }
        }

        private void Numbering_Click(object sender, RoutedEventArgs e)
        {
            // Exécute la commande native pour basculer la numérotation.
            // Le second argument (target) est le RichTextBox lui-même.
            EditingCommands.ToggleNumbering.Execute(null, StudyContentRichTextBox);
        }

        private void Bullets_Click(object sender, RoutedEventArgs e)
        {
            // Exécute la commande native pour basculer les puces.
            EditingCommands.ToggleBullets.Execute(null, StudyContentRichTextBox);
        }
        private void StudyContentRichTextBox_SelectionChanged(object sender, RoutedEventArgs e)
        {
            object currentSize = StudyContentRichTextBox.Selection.GetPropertyValue(TextElement.FontSizeProperty);
            if (currentSize is double && (double)currentSize > 0 && FontSizeComboBox != null)
            {
                FontSizeComboBox.SelectedValue = (double)currentSize;
            }
        }
        private void StudyContentRichTextBox_Pasting(object sender, DataObjectPastingEventArgs e)
        {
            // 1. Annuler la commande de coller par défaut si nous gérons le contenu
            bool handled = false;

            if (e.DataObject.GetDataPresent(typeof(System.Windows.Media.Imaging.BitmapSource)))
            {
                // CAS 1: Image BitmapSource (Captures d'écran, Coller depuis Photoshop)
                System.Windows.Media.Imaging.BitmapSource image = e.DataObject.GetData(typeof(System.Windows.Media.Imaging.BitmapSource)) as System.Windows.Media.Imaging.BitmapSource;
                if (image != null)
                {
                    TreeViewItem selectedStudy = StudiesTreeView.SelectedItem as TreeViewItem;
                    if (selectedStudy != null && selectedStudy.Tag is string studyPath)
                    {
                        InsertImage(image, studyPath); // Maintenant avec studyPath
                    }
                    handled = true;
                }
            }
            else if (e.DataObject.GetDataPresent(System.Windows.DataFormats.FileDrop))
            {
                // CAS 2: Fichier déposé ou copié depuis l'explorateur (Ctrl+C sur un fichier .jpg)
                string[] files = e.DataObject.GetData(System.Windows.DataFormats.FileDrop) as string[];
                if (files != null && files.Length > 0)
                {
                    string filePath = files[0];
                    string extension = Path.GetExtension(filePath)?.ToLower();

                    // Vérifier si c'est un format d'image que WPF peut charger
                    if (extension == ".png" || extension == ".jpg" || extension == ".jpeg" || extension == ".bmp" || extension == ".gif")
                    {
                        // Charger l'image à partir du fichier
                        System.Windows.Media.Imaging.BitmapImage image = new System.Windows.Media.Imaging.BitmapImage(new Uri(filePath));
                        TreeViewItem selectedStudy = StudiesTreeView.SelectedItem as TreeViewItem;
                        if (selectedStudy != null && selectedStudy.Tag is string studyPath)
                        {
                            InsertImage(image, studyPath); // Maintenant avec studyPath
                        }
                        handled = true;
                    }
                }
            }

            if (handled)
            {
                e.CancelCommand(); // Annuler si nous avons géré l'insertion d'une image
            }
        }
        private string GetStudyResourceFolder(string studyPath)
        {
            string studyFileName = Path.GetFileNameWithoutExtension(studyPath);
            // Ex: "MonEtude.rtf" -> "MonEtude_files"
            string resourceFolderName = $"{studyFileName}_fichiers";
            string resourceFolderPath = Path.Combine(Path.GetDirectoryName(studyPath), resourceFolderName);

            if (!Directory.Exists(resourceFolderPath))
            {
                Directory.CreateDirectory(resourceFolderPath);
            }
            return resourceFolderPath;
        }
        private void StudyContentRichTextBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            // Si l'étude est chargée et que l'utilisateur tape, marquer comme modifié.
           if (!IsStudyModified)
            {
                IsStudyModified = true;
               
            }
        }
        private void StudyContentRichTextBox_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {

            /// 2. Déterminer la position du curseur au clic
            TextPointer clickPosition = StudyContentRichTextBox.GetPositionFromPoint(e.GetPosition(StudyContentRichTextBox), true);

            if (clickPosition == null) return;

            // 3. Obtenir le parent Inline (l'élément logique contenant le contenu à la position du clic)
            Inline parentInline = clickPosition.Parent as Inline;

            // Si l'élément Inline est un InlineUIContainer, il contient un contrôle WPF (comme notre Image)
            if (parentInline is InlineUIContainer container)
            {
                if (container.Child is Image clickedImage)
                {
                    // Image trouvée !
                    if (clickedImage.Source is System.Windows.Media.Imaging.BitmapSource source)
                    {
                        // Ouvrir la fenêtre d'aperçu
                        ImageViewer viewer = new ImageViewer(source);
                        viewer.ShowDialog();

                        // Marquer l'événement comme géré
                        e.Handled = true;
                    }
                }
            }
        }
        private bool SaveCurrentStudy(string filePathToSave)
        {
            if (string.IsNullOrEmpty(filePathToSave)) return false;

            try
            {
                // 1. Récupérer le contenu du RichTextBox
                TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);

                // 2. Sauvegarder en RTF dans le fichier spécifié
                using (FileStream fStream = new FileStream(filePathToSave, FileMode.Create))
                {
                    range.Save(fStream, System.Windows.DataFormats.Rtf);
                }

                IsStudyModified = false; // Marquer comme non modifié après succès
                return true;
            }
            catch (Exception ex)
            {
                // Afficher une erreur si la sauvegarde échoue (par exemple, si le fichier est verrouillé)
                MessageBox.Show($"Erreur lors de la sauvegarde automatique : {ex.Message}", "Erreur Critique de Sauvegarde", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }
        private void EnsureSavedState(string studyPathToSave)
        {
            // Si l'étude a été modifiée ET que nous avons un chemin valide (pas null)
            if (IsStudyModified && !string.IsNullOrEmpty(studyPathToSave))
            {
                SaveCurrentStudy(studyPathToSave);
            }
        }
        /// <summary>
        /// Remonte l'arborescence visuelle pour trouver le contrôle Image parent.
        /// </summary>
        private void InsertImage(BitmapSource imageSource, string studyPath)
        {
            try
            {
                string resourceFolder = GetStudyResourceFolder(studyPath);
                string imageFileName = $"image_{DateTime.Now.Ticks}.png"; // Nom unique
                string imageFilePath = Path.Combine(resourceFolder, imageFileName);

                // 1. Sauvegarder l'image sur le disque (format PNG pour la qualité)
                using (var fileStream = new FileStream(imageFilePath, FileMode.Create))
                {
                    BitmapEncoder encoder = new PngBitmapEncoder();
                    encoder.Frames.Add(BitmapFrame.Create(imageSource));
                    encoder.Save(fileStream);
                }

                // 2. Création de l'élément Image WPF
                Image imageControl = new Image
                {
                    // La source est maintenant le chemin du fichier sur le disque
                    Source = imageSource,
                    MaxWidth = 400,
                    MaxHeight = 400,
                    Stretch = Stretch.Uniform,
                    Cursor = Cursors.Hand,

                    // Le Tag stocke le chemin relatif pour la sauvegarde/rechargement futur
                    Tag = imageFileName
                };
              

                InlineUIContainer container = new InlineUIContainer(imageControl, StudyContentRichTextBox.CaretPosition);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de l'insertion de l'image : {ex.Message}", "Erreur d'Image", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        // =================================================================
        // 3. GESTION DES ÉTUDES (Logique de Fichier et de TreeView)
        // =================================================================
        /// <summary>
        /// Crée un contrôle Image à partir d'une ressource URI.
        /// </summary>
        private Image CreateIconImage(string imageFileName, double size = 24)
        {
            try
            {
                // Utilise pack://application pour charger les images marquées comme 'Resource'
                // Assurez-vous que l'URI correspond à la structure de votre projet (backtest/Resources/)
                string uriString = $"pack://application:,,,/{AssemblyName};component/Resources/{imageFileName}";

                return new Image
                {
                    Source = new BitmapImage(new Uri(uriString)),
                    Width = size,
                    Height = size,
                    Margin = new Thickness(0, 0, 6, 0), // Petite marge à droite
                    VerticalAlignment = VerticalAlignment.Center
                };
            }
            catch
            {
                // Retourne null si l'image n'est pas trouvée (pour éviter le crash)
                return null;
            }
        }

        /// <summary>
        /// Crée le contenu complet du Header (Icône Image + Texte).
        /// </summary>
        private object CreateHeaderContent(string name, string type)
        {
            string iconFileName = "";
            Brush foregroundColor = Brushes.LightGray;

            if (type == "ROOT")
            {
                iconFileName = "folder.png"; // Icône du dossier racine
                foregroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FFD700")); // Jaune Or
            }
            else if (type == "FOLDER")
            {
                iconFileName = "folder.png"; // Icône de dossier
            }
            else // FILE
            {
                iconFileName = "file.png"; // Icône de fichier
                foregroundColor = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#00AEEF")); // Bleu DataEdge
            }

            Image icon = CreateIconImage(iconFileName);

            // Construction du StackPanel pour le Header
            StackPanel stackPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                VerticalAlignment = VerticalAlignment.Center
            };

            if (icon != null)
            {
                stackPanel.Children.Add(icon);
            }

            // Ajout du TextBlock
            stackPanel.Children.Add(new TextBlock
            {
                Text = name,
                Margin = new Thickness(0),
                VerticalAlignment = VerticalAlignment.Center,
                FontSize = 16,
                Foreground = foregroundColor
            });

            return stackPanel;
        }
        // --- Fonctions de Gestion du TreeView ---

        private void LoadStudiesTree()
        {
            if (!Directory.Exists(StudiesRootPath))
            {
                Directory.CreateDirectory(StudiesRootPath);
            }

            StudiesTreeView.Items.Clear();

            // Créer le noeud racine
            TreeViewItem rootItem = new TreeViewItem
            {
                Header = CreateHeaderContent("DataEdge Analyses (Local)", "ROOT"), // Utilisation de l'icône
                Tag = StudiesRootPath,
                IsExpanded = true,
                Padding = new Thickness(5, 3, 5, 3) // Ajout d'un padding pour l'esthétique
            };

            PopulateTreeView(rootItem, StudiesRootPath);
            StudiesTreeView.Items.Add(rootItem);
        }

        private void PopulateTreeView(TreeViewItem parentItem, string path)
        {
            try
            {
                // 1. Ajout des sous-dossiers
                foreach (string dir in Directory.GetDirectories(path))
                {
                    TreeViewItem folderItem = new TreeViewItem
                    {
                        Header = CreateHeaderContent(Path.GetFileName(dir), "FOLDER"), // Utilisation de l'icône
                        Tag = dir,
                        Padding = new Thickness(5, 3, 5, 3)
                    };
                    parentItem.Items.Add(folderItem);
                    PopulateTreeView(folderItem, dir);
                }

                // 2. Ajout des fichiers RTF
                foreach (string file in Directory.GetFiles(path, "*.rtf"))
                {
                    TreeViewItem fileItem = new TreeViewItem
                    {
                        Header = CreateHeaderContent(Path.GetFileNameWithoutExtension(file), "FILE"), // Utilisation de l'icône
                        Tag = file,
                        Padding = new Thickness(5, 3, 5, 3)
                    };
                    parentItem.Items.Add(fileItem);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du chargement des études : {ex.Message}", "Erreur Système de Fichier", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void MainWindow_Closing(object sender, System.ComponentModel.CancelEventArgs e)
        {
            // Obtenez l'instance de EtudesView (vous devrez peut-être la passer en argument ou la chercher)
            // EtudesView etudesView = ...

            EnsureSavedState(CurrentStudyPath);
        }
        private void StudiesTreeView_SelectedItemChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
        {
            // L'ancien chemin (l'étude que l'on quitte)
            string oldStudyPath = CurrentStudyPath;

            // Déterminer le chemin de la NOUVELLE sélection
            string newStudyPath = null;
            if (e.NewValue is TreeViewItem selectedItem)
            {
                newStudyPath = selectedItem.Tag as string;
            }

            // --- Étape 1 : Auto-Sauvegarde de l'ancienne étude ---

            // Si nous quittons un fichier RTF pour aller vers un autre élément (fichier ou dossier)
            if (oldStudyPath != null && oldStudyPath != newStudyPath )
            {
                // Sauvegarde forcée de l'étude que nous sommes en train de quitter (oldStudyPath)
                EnsureSavedState(oldStudyPath);
            }

            // --- Étape 2 : Chargement du nouvel élément ---

            if (newStudyPath != null)
            {
                if (File.Exists(newStudyPath))
                {
                    LoadStudyFile(newStudyPath); // Ceci mettra à jour CurrentStudyPath
                    StudiesTitle.Text = $"Fichier: {Path.GetFileNameWithoutExtension(newStudyPath)}";
                }
                else if (Directory.Exists(newStudyPath))
                {
                    StudiesTitle.Text = $"Dossier: {Path.GetFileName(newStudyPath)}";
                    // Important : Si c'est un dossier, nous ne sommes sur aucun fichier RTF actif.
                    CurrentStudyPath = null;
                }
            }
        }
        /// <summary>
        /// Sauvegarde automatiquement l'étude courante si elle a été modifiée.
        /// </summary>
        private void LoadStudyFile(string filePath)
        {
            // Logique pour charger le contenu RTF dans le RichTextBox
            try
            {
                // 1. Charger le contenu RTF et initialiser l'état
                StudyContentRichTextBox.Document.Blocks.Clear();
                TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);
                range.ApplyPropertyValue(TextElement.FontSizeProperty, 14.0); // Assurer la lisibilité du texte

                using (FileStream fStream = new FileStream(filePath, FileMode.Open))
                {
                    range.Load(fStream, DataFormats.Rtf);
                    // Une fois le chargement terminé :
                    CurrentStudyPath = filePath;
                    IsStudyModified = false;
                }

                // === CORRECTION DE TAILLE DES IMAGES (Sécurisée) ===
                // Cette fonction réapplique la taille Max/Min aux images après le chargement.
                ApplyImageSizingAndEvents();

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Impossible de charger le fichier : {ex.Message}", "Erreur de Chargement", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Parcourt le FlowDocument pour trouver les images et réappliquer les contraintes de taille 
        /// et les événements, en gérant de manière sécurisée les différents types de blocs (Paragraphs, Lists).
        /// </summary>
        private void ApplyImageSizingAndEvents()
        {
            // Fonction locale pour le traitement d'une liste d'Inlines
            Action<InlineCollection> processInlines = (inlines) =>
            {
                foreach (Inline inline in inlines)
                {
                    if (inline is InlineUIContainer container)
                    {
                        if (container.Child is Image imageControl)
                        {
                            // Appliquer la correction de taille et le curseur cliquable
                            imageControl.MaxWidth = 400;
                            imageControl.MaxHeight = 400;
                            imageControl.Stretch = System.Windows.Media.Stretch.Uniform;
                            imageControl.Cursor = Cursors.Hand;

                            // Note : Nous n'attachons pas l'événement MouseLeftButtonDown ici, 
                            // car le gestionnaire global du RichTextBox le capte déjà (la solution la plus fiable).
                        }
                    }
                }
            };

            // Parcourir tous les blocs du document
            foreach (Block block in StudyContentRichTextBox.Document.Blocks)
            {
                if (block is Paragraph paragraph)
                {
                    // CAS 1 : C'est un paragraphe (texte normal)
                    processInlines(paragraph.Inlines);
                }
                else if (block is System.Windows.Documents.List list)
                {
                    // CAS 2 : C'est une liste (puces/numérotée)
                    foreach (ListItem listItem in list.ListItems)
                    {
                        // Les list items contiennent eux-mêmes des blocs (souvent des Paragraphs)
                        foreach (Block itemBlock in listItem.Blocks)
                        {
                            if (itemBlock is Paragraph listParagraph)
                            {
                                processInlines(listParagraph.Inlines);
                            }
                        }
                    }
                }
                // Ignorer les Table, Section, etc.
            }
        }
        // --- Gestion des Boutons de la Barre d'Outils ---

        // La fonction NewFolder_Click est mise à jour pour utiliser CreateHeaderContent
        private void NewFolder_Click(object sender, RoutedEventArgs e)
        {
            InputDialog dialog = new InputDialog("Entrez le nom du nouveau dossier d'analyse :");
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string folderName = dialog.InputValue.Trim();

            if (string.IsNullOrEmpty(folderName))
            {
                MessageBox.Show("Le nom du dossier ne peut pas être vide.", "Erreur de Saisie", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Déterminer le parent (dossier sélectionné ou racine)
            string parentPath;
            TreeViewItem parentItem = GetCurrentParentItem(out parentPath);

            string newPath = Path.Combine(parentPath, folderName);

            try
            {
                if (Directory.Exists(newPath))
                {
                    MessageBox.Show("Un dossier avec ce nom existe déjà à cet emplacement.", "Erreur de Création", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 3. Création du répertoire sur le disque
                Directory.CreateDirectory(newPath);

                // 4. Création du nouvel élément TreeView avec l'icône
                TreeViewItem newFolderItem = new TreeViewItem
                {
                    Header = CreateHeaderContent(folderName, "FOLDER"), // Utilisation de l'icône
                    Tag = newPath,
                    Padding = new Thickness(5, 3, 5, 3)
                };

                // 5. Mise à jour de l'UI
                if (parentItem != null)
                {
                    parentItem.Items.Add(newFolderItem);
                    parentItem.IsExpanded = true;
                }
                else
                {
                    LoadStudiesTree();
                }

                newFolderItem.IsSelected = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la création du dossier : {ex.Message}", "Erreur Système de Fichier", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Fonction d'aide pour déterminer l'élément parent actuel dans le TreeView.
        /// </summary>
        private TreeViewItem GetCurrentParentItem(out string parentPath)
        {
            TreeViewItem selectedItem = StudiesTreeView.SelectedItem as TreeViewItem;
            parentPath = StudiesRootPath;
            TreeViewItem parentItem = null;

            if (selectedItem != null && selectedItem.Tag is string path)
            {
                if (Directory.Exists(path))
                {
                    parentPath = path;
                    parentItem = selectedItem;
                }
                else if (File.Exists(path))
                {
                    parentPath = Path.GetDirectoryName(path);
                    parentItem = (TreeViewItem)selectedItem.Parent;
                }
                else
                {
                    parentItem = (StudiesTreeView.Items.Count > 0) ? (TreeViewItem)StudiesTreeView.Items[0] : null;
                    if (parentItem != null) parentPath = parentItem.Tag as string;
                }
            }
            else
            {
                // Rien n'est sélectionné, utilise le dossier racine visible
                parentItem = (StudiesTreeView.Items.Count > 0) ? (TreeViewItem)StudiesTreeView.Items[0] : null;
                if (parentItem != null) parentPath = parentItem.Tag as string;
            }

            return parentItem;
        }


        // Dans EtudesView.xaml.cs :

        /// <summary>
        /// Gère la création d'une nouvelle étude (fichier .rtf).
        /// </summary>
        private void NewStudy_Click(object sender, RoutedEventArgs e)
        {
            // 1. Obtenir le chemin du dossier parent (même logique que NewFolder)
            string parentPath;
            TreeViewItem parentItem = GetCurrentParentItem(out parentPath);

            // Si nous n'avons pas réussi à déterminer un parent valide
            if (parentItem == null || !Directory.Exists(parentPath))
            {
                MessageBox.Show("Veuillez sélectionner un dossier valide pour créer votre nouvelle étude.", "Erreur de Saisie", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 2. Ouvrir la fenêtre de saisie pour le nom de l'étude
            InputDialog dialog = new InputDialog("Entrez le nom de la nouvelle étude (sans extension) :");
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string studyName = dialog.InputValue.Trim();

            if (string.IsNullOrEmpty(studyName))
            {
                MessageBox.Show("Le nom de l'étude ne peut pas être vide.", "Erreur de Saisie", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Déterminer le chemin complet du nouveau fichier
            string fileName = studyName + ".rtf";
            string newFilePath = Path.Combine(parentPath, fileName);

            try
            {
                if (File.Exists(newFilePath))
                {
                    MessageBox.Show($"Une étude nommée '{studyName}' existe déjà à cet emplacement.", "Erreur de Création", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 4. Création du fichier RTF vide sur le disque
                // L'utilisation de FileMode.Create crée le fichier ou l'écrase s'il existe (mais nous avons vérifié)
                using (FileStream fs = File.Create(newFilePath))
                {
                    // Laisser le fichier vide. Nous écrirons le contenu lors de la première sauvegarde.
                    // Optionnel : Écrire un bloc RTF minimal initial
                    TextRange tr = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);
                    tr.Text = "Nouvelle étude d'analyse DataEdge...";
                    tr.Save(fs, DataFormats.Rtf);
                }

                // 5. Création du nouvel élément TreeView
                TreeViewItem newFileItem = new TreeViewItem
                {
                    Header = CreateHeaderContent(studyName, "FILE"), // Utilise l'icône de fichier
                    Tag = newFilePath,
                    Padding = new Thickness(5, 3, 5, 3)
                };

                // 6. Mise à jour de l'UI
                parentItem.Items.Add(newFileItem);
                parentItem.IsExpanded = true;

                // 7. Sélectionner le nouvel élément et afficher son contenu dans le RichTextBox
                newFileItem.IsSelected = true;

                // Effacer le contenu du RichTextBox pour l'édition de la nouvelle étude
                StudyContentRichTextBox.Document.Blocks.Clear();

                // Note: La fonction LoadStudyFile n'est pas appelée ici car le fichier est déjà vide.
                StudiesTitle.Text = $"Fichier: {studyName} (Nouveau)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la création de l'étude : {ex.Message}", "Erreur Système de Fichier", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Gère la sauvegarde du contenu du RichTextBox dans le fichier RTF sélectionné.
        /// </summary>
        private void SaveStudy_Click(object sender, RoutedEventArgs e)
        {
            // 1. Identifier l'étude courante (élément sélectionné dans le TreeView)
            TreeViewItem selectedItem = StudiesTreeView.SelectedItem as TreeViewItem;

            if (selectedItem == null || selectedItem.Tag == null)
            {
                MessageBox.Show("Aucune étude n'est sélectionnée pour la sauvegarde.", "Sauvegarde Annulée", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            string filePath = selectedItem.Tag as string;

            // 2. Valider que c'est un fichier RTF existant
            if (!File.Exists(filePath) || Path.GetExtension(filePath).ToLower() != ".rtf")
            {
                // Dans ce cas, il pourrait s'agir d'un nouveau fichier non encore sauvegardé, ou d'un dossier.
                // Si c'est un dossier, nous ne faisons rien. 
                // Si c'est un nouveau fichier, le chemin doit exister suite à NewStudy_Click.
                MessageBox.Show("L'élément sélectionné n'est pas un fichier d'étude valide pour la sauvegarde.", "Sauvegarde Annulée", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // 3. Sauvegarder le contenu du RichTextBox
            try
            {
                TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);

                using (FileStream fStream = new FileStream(filePath, FileMode.Create)) // FileMode.Create écrase le contenu existant
                {
                    // Enregistre le contenu au format RTF
                    range.Save(fStream, DataFormats.Rtf);
                    // Une fois la sauvegarde réussie :
                    IsStudyModified = false;
                }

                // 4. Feedback
                StudiesTitle.Text = $"Fichier: {Path.GetFileNameWithoutExtension(filePath)} (Sauvegardé)";
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la sauvegarde du fichier : {ex.Message}", "Erreur de Sauvegarde", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ExportRTF_Click(object sender, RoutedEventArgs e)
        {
            // 1. Identifier l'étude courante
            TreeViewItem selectedItem = StudiesTreeView.SelectedItem as TreeViewItem;

            // Si rien n'est sélectionné ou si ce n'est pas un fichier (Tag est le chemin)
            if (selectedItem == null || !(selectedItem.Tag is string currentPath) || !File.Exists(currentPath))
            {
                MessageBox.Show("Veuillez sélectionner un fichier d'étude valide à exporter.", "Exportation Annulée", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string currentFileName = Path.GetFileNameWithoutExtension(currentPath);

            // 2. Initialiser et afficher la boîte de dialogue "Save File As"
            SaveFileDialog saveDialog = new SaveFileDialog
            {
                FileName = currentFileName, // Nom de fichier par défaut
                DefaultExt = ".rtf",
                Filter = "Rich Text Format (*.rtf)|*.rtf|Tous les fichiers (*.*)|*.*",
                Title = "Exporter l'étude d'analyse DataEdge"
            };

            // Afficher la boîte de dialogue et vérifier si l'utilisateur a cliqué sur OK
            if (saveDialog.ShowDialog() == true)
            {
                // Le chemin sélectionné par l'utilisateur
                string exportPath = saveDialog.FileName;

                // 3. Sauvegarder le contenu du RichTextBox dans le nouveau chemin
                try
                {
                    TextRange range = new TextRange(StudyContentRichTextBox.Document.ContentStart, StudyContentRichTextBox.Document.ContentEnd);

                    using (FileStream fStream = new FileStream(exportPath, FileMode.Create)) // FileMode.Create écrase le fichier cible s'il existe
                    {
                        // Enregistre le contenu au format RTF
                        range.Save(fStream, DataFormats.Rtf);
                    }

                    // 4. Feedback
                    MessageBox.Show($"L'étude a été exportée avec succès vers :\n{exportPath}", "Exportation Réussie", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Erreur lors de l'exportation du fichier : {ex.Message}", "Erreur d'Exportation", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
            // Si ShowDialog() retourne false, l'utilisateur a annulé, on ne fait rien.
        }

        private void StudiesTreeView_KeyDown(object sender, KeyEventArgs e)
        {
            // 1. Vérifier si la touche enfoncée est la touche Delete
            if (e.Key == Key.Delete)
            {
                // 2. Vérifier si un élément est sélectionné dans l'arborescence
                if (StudiesTreeView.SelectedItem != null)
                {
                    // 3. Appeler la logique de suppression existante
                    Delete_Click(sender, e);

                    // Marquer l'événement comme géré pour éviter toute propagation indésirable
                    e.Handled = true;
                }
            }
        }
        private void Delete_Click(object sender, RoutedEventArgs e)
        {
            // 1. Identifier la cible
            TreeViewItem selectedItem = StudiesTreeView.SelectedItem as TreeViewItem;

            if (selectedItem == null || selectedItem.Tag == null)
            {
                MessageBox.Show("Veuillez sélectionner un fichier ou un dossier à supprimer.", "Suppression Annulée", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            string pathToDelete = selectedItem.Tag as string;
            string name = selectedItem.Header.ToString();
            string type = "";

            try
            {
                bool isDirectory = Directory.Exists(pathToDelete);
                bool isFile = File.Exists(pathToDelete);

                if (!isDirectory && !isFile)
                {
                    MessageBox.Show("L'élément sélectionné n'existe plus sur le disque.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 2. Préparation du message de confirmation
                if (isDirectory)
                {
                    type = "Dossier";
                    // Vérification de la racine : interdire la suppression de la racine StudiesRootPath
                    if (pathToDelete.Equals(StudiesRootPath, StringComparison.OrdinalIgnoreCase))
                    {
                        MessageBox.Show("La suppression du dossier racine ('Etudes') n'est pas autorisée.", "Interdit", MessageBoxButton.OK, MessageBoxImage.Stop);
                        return;
                    }
                }
                else
                {
                    type = "Fichier";
                }

                // Afficher la boîte de dialogue de confirmation
                string confirmationMessage = $"Êtes-vous sûr de vouloir supprimer définitivement le {type} ?\nCette action est irréversible.";

                MessageBoxResult result = MessageBox.Show(confirmationMessage, $"Confirmation de Suppression DataEdge : {type}", MessageBoxButton.YesNo, MessageBoxImage.Exclamation);

                if (result == MessageBoxResult.Yes)
                {
                    // --- 3. Suppression ---
                    if (isDirectory)
                    {
                        Directory.Delete(pathToDelete, true); // true = suppression récursive
                    }
                    else if (isFile)
                    {
                        File.Delete(pathToDelete);
                    }

                    // --- 4. Mise à jour de l'UI ---

                    // Trouver l'élément parent dans le TreeView
                    ItemsControl parent = ItemsControl.ItemsControlFromItemContainer(selectedItem);

                    if (parent is TreeViewItem parentItem)
                    {
                        // Si le parent est un autre TreeViewItem (sous-dossier), le retirer de ses Items
                        parentItem.Items.Remove(selectedItem);
                    }
                    else if (parent is TreeView treeView)
                    {
                        // Si le parent est le TreeView principal (élément à la racine), le retirer des Items
                        treeView.Items.Remove(selectedItem);
                    }

                    // Effacer le RichTextBox si le fichier supprimé était ouvert
                    StudyContentRichTextBox.Document.Blocks.Clear();
                    StudiesTitle.Text = "Élément supprimé.";
                }
            }
            catch (IOException ex)
            {
                // Gérer les cas où le fichier/dossier est en cours d'utilisation
                MessageBox.Show($"Erreur I/O: Impossible de supprimer l'élément. Il est peut-être utilisé par un autre programme.\n{ex.Message}", "Erreur de Suppression", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Une erreur inattendue est survenue lors de la suppression : {ex.Message}", "Erreur Système", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// Gère le renommage d'un fichier ou d'un dossier sélectionné.
        /// </summary>
        private void Rename_Click(object sender, RoutedEventArgs e)
        {
            // L'élément sélectionné au moment du clic droit est la cible
            TreeViewItem selectedItem = StudiesTreeView.SelectedItem as TreeViewItem;

            if (selectedItem == null || !(selectedItem.Tag is string oldPath))
            {
                MessageBox.Show("Veuillez sélectionner un élément valide à renommer.", "Renommage Annulé", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            // Interdire le renommage de la racine 'Etudes'
            if (oldPath.Equals(StudiesRootPath, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("Le renommage du dossier racine ('Etudes') n'est pas autorisé.", "Interdit", MessageBoxButton.OK, MessageBoxImage.Stop);
                return;
            }

            bool isDirectory = Directory.Exists(oldPath);
            string currentName = isDirectory ? Path.GetFileName(oldPath) : Path.GetFileNameWithoutExtension(oldPath);
            string extension = isDirectory ? "" : Path.GetExtension(oldPath);

            // 1. Demander le nouveau nom (en pré-remplissant l'ancien nom)
            InputDialog dialog = new InputDialog("Entrez le nouveau nom :", currentName);
            if (dialog.ShowDialog() != true)
            {
                return;
            }

            string newName = dialog.InputValue.Trim();

            if (string.IsNullOrEmpty(newName) || newName.Equals(currentName, StringComparison.OrdinalIgnoreCase))
            {
                // Nom inchangé ou vide
                return;
            }

            // 2. Construire le nouveau chemin
            string parentDir = Path.GetDirectoryName(oldPath);
            string newPath = Path.Combine(parentDir, newName + extension);

            try
            {
                // 3. Vérifier si le nouveau nom existe déjà
                if (File.Exists(newPath) || Directory.Exists(newPath))
                {
                    MessageBox.Show($"Un élément nommé '{newName}' existe déjà à cet emplacement.", "Erreur de Renommage", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // 4. Renommage physique sur le disque
                if (isDirectory)
                {
                    Directory.Move(oldPath, newPath);
                    // Mettre à jour les chemins de tous les descendants dans l'UI
                    UpdateDescendantPaths(selectedItem, oldPath, newPath);
                    // =========================================================

                    // Mise à jour du Tag et du Header de l'élément parent
                    selectedItem.Tag = newPath;
                    selectedItem.Header = CreateHeaderContent(newName, "FOLDER");
                }
                else
                {
                    File.Move(oldPath, newPath);
                    // Mise à jour du Tag et du Header de l'élément fichier
                    selectedItem.Tag = newPath;
                    selectedItem.Header = CreateHeaderContent(newName, "FILE");

                    // Rafraîchissement de l'éditeur (logique de correction précédente)
                    StudiesTitle.Text = $"Fichier: {newName}";
                    if (selectedItem.IsSelected)
                    {
                        selectedItem.IsSelected = true;
                        LoadStudyFile(newPath);
                    }
                }

                // 5. Mise à jour de l'UI
                // Mettre à jour le Header (Icône + Texte)
                // Stocker l'état de sélection avant de mettre à jour le Tag
                bool wasSelected = selectedItem.IsSelected;
                string headerType = isDirectory ? "FOLDER" : "FILE";
                selectedItem.Header = CreateHeaderContent(newName, headerType);


                // Mettre à jour le Tag (le chemin complet)
                selectedItem.Tag = newPath;

                // Mettre à jour le titre du panneau d'édition si un fichier est renommé
                if (!isDirectory)
                {
                    StudiesTitle.Text = $"Fichier: {newName}";
                    // Si ce fichier était sélectionné et ouvert:
                    if (wasSelected)
                    {

                        selectedItem.IsSelected = true;


                        LoadStudiesTree();
                        LoadStudyFile(newPath);
                    }
                }

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors du renommage : {ex.Message}", "Erreur Système de Fichier", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        /// <summary>
        /// Met à jour récursivement les chemins (Tag) des descendants après le renommage d'un dossier parent.
        /// </summary>
        private void UpdateDescendantPaths(TreeViewItem parentItem, string oldBasePath, string newBasePath)
        {
            // Parcourt tous les enfants de l'élément parent
            foreach (var item in parentItem.Items)
            {
                // On s'assure que c'est bien un TreeViewItem (pour les sous-dossiers et fichiers)
                if (item is TreeViewItem childItem)
                {
                    if (childItem.Tag is string oldChildPath && oldChildPath.StartsWith(oldBasePath))
                    {
                        // 1. Calculer le nouveau chemin en remplaçant l'ancienne base
                        string relativePath = oldChildPath.Substring(oldBasePath.Length);
                        string newChildPath = newBasePath + relativePath;

                        // 2. Mettre à jour le Tag
                        childItem.Tag = newChildPath;

                        // 3. Appel récursif pour les sous-dossiers (s'assurer que leur Tag est le nouveau chemin avant de passer à l'appel récursif)
                        UpdateDescendantPaths(childItem, oldChildPath, newChildPath);
                    }
                }
            }
        }

    }
}