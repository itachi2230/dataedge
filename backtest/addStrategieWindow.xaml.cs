using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace backtest
{
    public partial class addStrategieWindow : Window
    {
        private Strategie _existingStrategie = null;
        private bool IsEditMode => _existingStrategie != null;

        // Constructeur pour la CRÉATION
        public addStrategieWindow()
        {
            InitializeComponent();
        }

        // Constructeur pour la MODIFICATION
        public addStrategieWindow(Strategie strategie)
        {
            InitializeComponent();
            _existingStrategie = strategie;
            PrepareEditMode();
        }

        private void PrepareEditMode()
        {
            // Changer les titres de l'UI
            ActionTitle.Text = "MODIFIER";
            ActionTitle.Foreground = Brushes.Cyan;

            // Remplir les champs
            StrategieNom.Text = _existingStrategie.Nom;
            descriptionTextbox.Text = _existingStrategie.description;

            // Charger les champs personnalisés existants
            var structure = _existingStrategie.GetStructure();
            if (structure != null)
            {
                foreach (var fieldName in structure)
                {
                    AddFieldToUI(fieldName);
                }
            }
        }

        private void AddCustomField_Click(object sender, RoutedEventArgs e)
        {
            AddFieldToUI(""); // Ajoute un champ vide
        }

        // Centralisation de la création de ligne de critère pour réutilisation
        private void AddFieldToUI(string fieldName)
        {
            Style modernStyle = (Style)this.FindResource("ModernField");
            Grid fieldGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });

            bool isPlaceholder = string.IsNullOrEmpty(fieldName);

            TextBox nameTextBox = new TextBox
            {
                Style = modernStyle,
                FontSize = 13,
                Height = 35,
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = isPlaceholder ? "Nom du critère (ex: RSI, Trend...)" : fieldName,
                Foreground = isPlaceholder ? Brushes.Gray : Brushes.White,
                Tag = isPlaceholder ? "placeholder" : ""
            };

            nameTextBox.GotFocus += (s, ev) => {
                if (nameTextBox.Tag?.ToString() == "placeholder")
                {
                    nameTextBox.Text = "";
                    nameTextBox.Foreground = Brushes.White;
                    nameTextBox.Tag = "";
                }
            };

            Button deleteButton = new Button
            {
                Content = "✕",
                Width = 30,
                Height = 30,
                Background = new SolidColorBrush(Color.FromRgb(45, 20, 20)),
                Foreground = Brushes.Red,
                BorderThickness = new Thickness(0),
                Cursor = System.Windows.Input.Cursors.Hand,
                Margin = new Thickness(10, 0, 0, 0)
            };

            // Template arrondi pour le bouton supprimer
            ControlTemplate template = new ControlTemplate(typeof(Button));
            FrameworkElementFactory border = new FrameworkElementFactory(typeof(Border));
            border.SetValue(Border.CornerRadiusProperty, new CornerRadius(8));
            border.SetValue(Border.BackgroundProperty, new TemplateBindingExtension(Button.BackgroundProperty));
            FrameworkElementFactory cp = new FrameworkElementFactory(typeof(ContentPresenter));
            cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
            cp.SetValue(ContentPresenter.VerticalAlignmentProperty, VerticalAlignment.Center);
            border.AppendChild(cp);
            template.VisualTree = border;
            deleteButton.Template = template;

            deleteButton.Click += (s, args) => CustomFieldsPanel.Children.Remove(fieldGrid);

            Grid.SetColumn(nameTextBox, 0);
            Grid.SetColumn(deleteButton, 1);
            fieldGrid.Children.Add(nameTextBox);
            fieldGrid.Children.Add(deleteButton);
            CustomFieldsPanel.Children.Add(fieldGrid);
        }

        private void SaveStrategie_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string nom = StrategieNom.Text.Trim().ToUpper();
                if (string.IsNullOrEmpty(nom)) return;

                // Récupération de la nouvelle structure
                List<string> structureDynamique = new List<string>();
                foreach (Grid grid in CustomFieldsPanel.Children)
                {
                    var txt = grid.Children.OfType<TextBox>().FirstOrDefault();
                    if (txt != null && !string.IsNullOrWhiteSpace(txt.Text) && txt.Tag?.ToString() != "placeholder")
                    {
                        structureDynamique.Add(txt.Text.Trim().ToUpper());
                    }
                }

                if (IsEditMode)
                {
                    // MODE MODIFICATION
                    _existingStrategie.ModifierInfosGenerales(nom, descriptionTextbox.Text);
                    _existingStrategie.SetStructure(structureDynamique);
                }
                else
                {
                    // MODE CRÉATION
                    Strategie nouvelleSt = new Strategie(nom, descriptionTextbox.Text);
                    nouvelleSt.SetStructure(structureDynamique);
                }

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) => this.Close();
    }
}