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
        public addStrategieWindow()
        {
            InitializeComponent();
        }

        /// <summary>
        /// Ajoute un nouveau champ dynamique (Critère) à l'interface.
        /// </summary>
        private void AddCustomField_Click(object sender, RoutedEventArgs e)
        {
            // Récupération du style défini dans votre XAML
            Style modernStyle = (Style)this.FindResource("ModernField");

            // Structure pour aligner proprement le texte et le bouton supprimer
            Grid fieldGrid = new Grid { Margin = new Thickness(0, 0, 0, 10) };
            fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            fieldGrid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(45) });

            // Champ de saisie du nom du critère
            TextBox nameTextBox = new TextBox
            {
                Style = modernStyle,
                FontSize = 13,
                Height = 35,
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = "Nom du critère (ex: RSI, Trend...)",
                Foreground = Brushes.Gray,
                Tag = "placeholder" // Petit marqueur pour savoir si c'est le texte par défaut
            };

            // Gestion du Placeholder
            nameTextBox.GotFocus += (s, ev) => {
                if (nameTextBox.Tag?.ToString() == "placeholder")
                {
                    nameTextBox.Text = "";
                    nameTextBox.Foreground = Brushes.White;
                    nameTextBox.Tag = "";
                }
            };

            // Bouton de suppression stylisé
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

            // Arrondir les angles du bouton via un template rapide
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

        /// <summary>
        /// Sauvegarde la stratégie et sa structure de champs personnalisés dans le JSON.
        /// </summary>
        private void SaveStrategie_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string nom = StrategieNom.Text.Trim().ToUpper();

                if (string.IsNullOrEmpty(nom))
                {
                    MessageBox.Show("Veuillez entrer un nom pour la stratégie.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                // 1. Initialisation de la stratégie (crée le fichier JSON avec Nom et Description)
                Strategie nouvelleSt = new Strategie(nom, descriptionTextbox.Text);

                // 2. Récupération des noms des champs personnalisés pour créer la "Structure"
                List<string> structureDynamique = new List<string>();
                foreach (Grid grid in CustomFieldsPanel.Children)
                {
                    TextBox txt = grid.Children.OfType<TextBox>().FirstOrDefault();

                    // On vérifie que le texte n'est pas vide et que ce n'est pas le placeholder
                    if (txt != null && !string.IsNullOrWhiteSpace(txt.Text) && txt.Tag?.ToString() != "placeholder")
                    {
                        structureDynamique.Add(txt.Text.Trim().ToUpper());
                    }
                }

                // 3. Sauvegarde de la structure (la liste des critères) dans le fichier JSON
                nouvelleSt.SetStructure(structureDynamique);

                this.DialogResult = true;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur lors de la création : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false;
            this.Close();
        }
    }
}