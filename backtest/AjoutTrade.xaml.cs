using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace backtest
{
    public partial class AjoutTrade : Window
    {
        private Strategie _strategie;
        private bool modeJournal;

        public AjoutTrade(Strategie strategie, bool modeJournal = false)
        {
            InitializeComponent();
            _strategie = strategie;
            this.modeJournal = modeJournal;

            // Configuration de l'interface
            if (profitTxt != null)
                stackprofit.Visibility = modeJournal ? Visibility.Visible : Visibility.Collapsed;

            // Initialisation des Enums
            TypeOrdreComboBox.ItemsSource = Enum.GetValues(typeof(TypeOrdre));
            ResultComboBox.ItemsSource = Enum.GetValues(typeof(Resultat));

            // Setup des placeholders (comme dans addStrategieWindow)
            SetupPlaceholders(MainStack);

            // Chargement de la structure dynamique
            ChargerChampsDynamique();
        }

        private void SetupPlaceholders(Panel container)
        {
            foreach (var child in container.Children)
            {
                if (child is TextBox tb)
                {
                    tb.GotFocus += (s, e) =>
                    {
                        if (tb.Tag?.ToString() == "placeholder")
                        {
                            tb.Text = "";
                            tb.Foreground = Brushes.White;
                            tb.Tag = "";
                        }
                    };
                }
                else if (child is Panel p) SetupPlaceholders(p); // Récursivité pour les Grids/StackPanels
            }
        }

        private void ChargerChampsDynamique()
        {
            DynamicFieldsPanel.Children.Clear();
            List<string> structure = _strategie.GetStructure();

            foreach (var header in structure)
            {
                TextBlock lbl = new TextBlock
                {
                    Text = header.ToUpper(),
                    Foreground = Brushes.Gray,
                    FontSize = 9,
                    Margin = new Thickness(0, 10, 0, 5)
                };

                TextBox txt = new TextBox
                {
                    Style = (Style)this.FindResource("ModernField"),
                    Height = 35,
                    Tag = header, // Utilisé pour mapper la sauvegarde
                    Margin = new Thickness(0, 0, 0, 10)
                };

                DynamicFieldsPanel.Children.Add(lbl);
                DynamicFieldsPanel.Children.Add(txt);
            }
        }

        private void SaveTrade_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (TypeOrdreComboBox.SelectedItem == null || ResultComboBox.SelectedItem == null)
                {
                    MessageBox.Show("Sélectionnez le type et le résultat.", "Champs manquants", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                double profitValue = 0;
                if (modeJournal && profitTxt.Tag?.ToString() != "placeholder")
                    double.TryParse(profitTxt.Text.Replace(".", ","), out profitValue);

                float rrValue = 0;
                if (RrTextBox.Tag?.ToString() != "placeholder")
                    float.TryParse(RrTextBox.Text.Replace(".", ","), out rrValue);

                var trade = new Trade(profitValue)
                {
                    Paire = (PaireTextBox.Tag?.ToString() == "placeholder") ? "N/A" : PaireTextBox.Text.ToUpper(),
                    TypeOrdre = (TypeOrdre)TypeOrdreComboBox.SelectedItem,
                    Result = (Resultat)ResultComboBox.SelectedItem,
                    DateEntree = CombineDateTime(DateEntreePicker.SelectedDate, TimeEntreePicker.Value),
                    DateSortie = CombineDateTime(DateSortiePicker.SelectedDate, TimeSortiePicker.Value),
                    RR = rrValue,
                    ImageLtf = (ImageLtfTextBox.Tag?.ToString() == "placeholder") ? "" : ImageLtfTextBox.Text,
                    ImageHtf = (ImageHtfTextBox.Tag?.ToString() == "placeholder") ? "" : ImageHtfTextBox.Text,
                    description = (descriptionTextbox.Tag?.ToString() == "placeholder") ? "" : descriptionTextbox.Text,
                    strategie = _strategie.Nom,
                    ChampsPersonnalises = DynamicFieldsPanel.Children
                        .OfType<TextBox>()
                        .Select(tb => new ChampPersonnalise(tb.Tag.ToString(), tb.Text))
                        .ToList()
                };

                if (modeJournal) _strategie.AddJournal(trade);
                else _strategie.AddTrade(trade);

                if (texetat != null)
                {
                    texetat.Visibility = Visibility.Visible;
                    texetat.Text = $"Trade {trade.Paire} enregistré !";
                }

                // On vide les champs pour le trade suivant
                ViderChamps();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Erreur : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        private void ViderChamps()
        {
            // 1. Vider les TextBoxes standards
            PaireTextBox.Text = "";
            RrTextBox.Text = "";
            ImageLtfTextBox.Text = "";
            ImageHtfTextBox.Text = "";
            descriptionTextbox.Text = "";
            if (profitTxt != null) profitTxt.Text = "";

            // 2. Vider les champs dynamiques (Indicateurs, etc.)
            foreach (var child in DynamicFieldsPanel.Children)
            {
                if (child is TextBox tb) tb.Text = "";
            }

            // 3. Réinitialiser les sélections
            TypeOrdreComboBox.SelectedIndex = -1;
            ResultComboBox.SelectedIndex = -1;

            // 4. Facultatif : Remettre les placeholders si nécessaire
            SetupPlaceholders(MainStack);

            // 5. Focus sur le premier champ pour recommencer direct
            PaireTextBox.Focus();
        }
        private DateTime CombineDateTime(DateTime? date, DateTime? time)
        {
            if (!date.HasValue || !time.HasValue)
                throw new Exception("Date et Heure obligatoires.");
            return date.Value.Date + time.Value.TimeOfDay;
        }

        private void Cancel_Click(object sender, RoutedEventArgs e) { this.DialogResult = true; this.Close(); }
    }
}