using System.Windows;
using System.Windows.Controls;
using System.Collections.Generic;
using System;
using System.Windows.Media.Animation;

namespace backtest
{
    public partial class addStrategieWindow : Window
    {
        public addStrategieWindow()
        {
            InitializeComponent();
            ResultComboBox.ItemsSource = Enum.GetValues(typeof(Resultat));
            TypeOrdreComboBox.ItemsSource = Enum.GetValues(typeof(TypeOrdre));

        }
        private void StartEllipseAnimation(object sender, RoutedEventArgs e)
        {
            Storyboard rotateStoryboard = (Storyboard)FindResource("RotateEllipsesAnimation");
            rotateStoryboard.Begin();
        }


        private void AddCustomField_Click(object sender, RoutedEventArgs e)
        {
            // Conteneur pour le champ personnalisé
            StackPanel customFieldPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Center,
                Margin = new Thickness(0, 5, 0, 5)
            };

            // Champ de texte pour le nom du champ personnalisé
            TextBox nameTextBox = new TextBox
            {
                Width = 100,
                Margin = new Thickness(5),
                Text = "Nom du Champ",
                Foreground = System.Windows.Media.Brushes.Black,
                Background = System.Windows.Media.Brushes.Gray
            };

            TextBox valueTextBox = new TextBox
            {
                Width = 100,
                Margin = new Thickness(5),
                Text = "Valeur du Champ",
                Foreground = System.Windows.Media.Brushes.Black,
                Background = System.Windows.Media.Brushes.Gray
            };
            // Bouton pour supprimer le champ personnalisé
            Button deleteButton = new Button
            {
                Content = "X",
                Width = 30,
                Height = 30,
                Background = System.Windows.Media.Brushes.Red,
                Foreground = System.Windows.Media.Brushes.White,
                Margin = new Thickness(5)
            };

            deleteButton.Click += (s, args) => CustomFieldsPanel.Children.Remove(customFieldPanel);

            customFieldPanel.Children.Add(nameTextBox);
            customFieldPanel.Children.Add(valueTextBox);
            customFieldPanel.Children.Add(deleteButton);

            CustomFieldsPanel.Children.Add(customFieldPanel);
        }

        private void SaveStrategie_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Récupère le nom de la stratégie
                string strategieNom = StrategieNom.Text.ToUpper();

                if (string.IsNullOrEmpty(strategieNom))
                {
                    MessageBox.Show("Veuillez entrer un nom pour la stratégie.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                // Crée une nouvelle stratégie avec le nom spécifié
                Strategie strategie = new Strategie(strategieNom,descriptionTextbox.Text);

                // Récupère la date d'entrée et l'heure d'entrée
                DateTime dateEntree = DateEntreePicker.SelectedDate ?? DateTime.Now;
                string timeEntreeText = TimeEntreePicker.Text; // Récupère le texte du TimePicker
                TimeSpan timeEntree = ParseTime(timeEntreeText); // Convertit le texte en TimeSpan

                // Combine la date d'entrée et l'heure d'entrée
                dateEntree = dateEntree.Date + timeEntree;

                // Récupère la date de sortie et l'heure de sortie
                DateTime dateSortie = DateSortiePicker.SelectedDate ?? DateTime.Now;
                string timeSortieText = TimeSortiePicker.Text; // Récupère le texte du TimePicker
                TimeSpan timeSortie = ParseTime(timeSortieText); // Convertit le texte en TimeSpan

                // Combine la date de sortie et l'heure de sortie
                dateSortie = dateSortie.Date + timeSortie;
                //recuperation du resultqt
                Resultat tempresult;
                if (ResultComboBox.SelectedItem is Resultat resultatSelectionne)
                {
                    tempresult = resultatSelectionne;
                }
                else
                {
                    MessageBox.Show("Veuillez sélectionner un résultat valide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                //recuperation du type d'ordre
                TypeOrdre tempordre;
                if (TypeOrdreComboBox.SelectedItem is TypeOrdre typeordreSelectionne)
                {
                    tempordre = typeordreSelectionne;
                }
                else
                {
                    MessageBox.Show("Veuillez sélectionner un type d'ordre valide.", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }
                

                // Création de l'objet Trade a  vec date et heure
                Trade trade = new Trade
                {
                    Paire = PaireTextBox.Text.ToUpper(),
                    Result = tempresult,
                    DateEntree = dateEntree,
                    DateSortie = dateSortie,
                    RR = float.Parse(RrTextBox.Text),
                    TypeOrdre = tempordre,
                    ImageLtf = ImageLtfTextBox.Text,
                    ImageHtf = ImageHtfTextBox.Text,
                    description=descriptionTextbox.Text
                };

                // Récupère les champs personnalisés
                foreach (StackPanel panel in CustomFieldsPanel.Children)
                {
                    TextBox nomTextBox = panel.Children[0] as TextBox;
                    TextBox valeurTextBox = panel.Children[1] as TextBox;

                    ChampPersonnalise champ = new ChampPersonnalise(nomTextBox.Text.ToUpper(), valeurTextBox.Text);
                   

                    trade.ChampsPersonnalises.Add(champ);
                }

                // Ajoute le trade à la stratégie
                strategie.AddTrade(trade);
                //MessageBox.Show("Trade ajouté avec succès!", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                this.Close();

            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur: {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // Méthode pour parser le texte en TimeSpan
        private TimeSpan ParseTime(string timeText)
        {
            // Vérifie que le texte n'est pas vide
            if (string.IsNullOrEmpty(timeText))
                return TimeSpan.Zero;

            // Essaye de parser le texte en TimeSpan
            string[] parts = timeText.Split(':');
            if (parts.Length == 2 && int.TryParse(parts[0], out int hours) && int.TryParse(parts[1], out int minutes))
            {
                return new TimeSpan(hours, minutes, 0); // Créé un TimeSpan avec les heures et les minutes
            }
            else
            {
                throw new FormatException("Le format de l'heure est invalide. Utilisez le format HH:mm.");
            }
        }


        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
