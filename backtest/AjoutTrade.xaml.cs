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
        private Trade _tradeEnEdition = null; // Stocke le trade si on est en mode modif
        private bool IsEditMode => _tradeEnEdition != null;

        // Constructeur Standard (Ajout)
        public AjoutTrade(Strategie strategie, bool modeJournal = false)
        {
            InitializeComponent();
            _strategie = strategie;
            this.modeJournal = modeJournal;
            InitInterface();
        }

        // Constructeur pour la MODIFICATION
        public AjoutTrade(Strategie strategie, Trade tradeAModifier, bool modeJournal = false)
        {
            InitializeComponent();
            _strategie = strategie;
            this.modeJournal = modeJournal;
            _tradeEnEdition = tradeAModifier;

            InitInterface();
            PreparerModeModification();
        }

        private void InitInterface()
        {
            if (profitTxt != null)
                stackprofit.Visibility = modeJournal ? Visibility.Visible : Visibility.Collapsed;

            TypeOrdreComboBox.ItemsSource = Enum.GetValues(typeof(TypeOrdre));
            ResultComboBox.ItemsSource = Enum.GetValues(typeof(Resultat));

            SetupPlaceholders(MainStack);
            ChargerChampsDynamique();
        }

        private void PreparerModeModification()
        {
            // Update UI Titles
            ActionTitle.Text = "MODIFIER";
            MainTitle.Text = $"TRADE {_tradeEnEdition.Paire}";

            // Remplissage des champs standards
            PaireTextBox.Text = _tradeEnEdition.Paire;
            PaireTextBox.Foreground = Brushes.White;
            PaireTextBox.Tag = ""; // Enlever placeholder

            TypeOrdreComboBox.SelectedItem = _tradeEnEdition.TypeOrdre;
            ResultComboBox.SelectedItem = _tradeEnEdition.Result;

            DateEntreePicker.SelectedDate = _tradeEnEdition.DateEntree;
            TimeEntreePicker.Value = _tradeEnEdition.DateEntree;

            DateSortiePicker.SelectedDate = _tradeEnEdition.DateSortie;
            TimeSortiePicker.Value = _tradeEnEdition.DateSortie;

            RrTextBox.Text = _tradeEnEdition.RR.ToString();
            RrTextBox.Foreground = Brushes.White;
            RrTextBox.Tag = "";

            ImageLtfTextBox.Text = _tradeEnEdition.ImageLtf;
            ImageLtfTextBox.Foreground = Brushes.White;
            ImageLtfTextBox.Tag = "";

            ImageHtfTextBox.Text = _tradeEnEdition.ImageHtf;
            ImageHtfTextBox.Foreground = Brushes.White;
            ImageHtfTextBox.Tag = "";

            descriptionTextbox.Text = _tradeEnEdition.description;
            descriptionTextbox.Foreground = Brushes.White;
            descriptionTextbox.Tag = "";

            if (modeJournal && profitTxt != null)
            {
                profitTxt.Text = _tradeEnEdition.Profit.ToString();
                profitTxt.Foreground = Brushes.White;
                profitTxt.Tag = "";
            }

            // Remplissage des champs dynamiques (Confluences)
            foreach (var cp in _tradeEnEdition.ChampsPersonnalises)
            {
                // On cherche le TextBox qui a le Tag correspondant au nom du critère
                var tb = DynamicFieldsPanel.Children.OfType<TextBox>()
                         .FirstOrDefault(t => t.Tag?.ToString() == cp.Nom);

                if (tb != null)
                {
                    tb.Text = cp.Valeur.ToString();
                    tb.Foreground = Brushes.White;
                }
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
                    Tag = header,
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

                // Parsing des valeurs numériques
                double.TryParse(profitTxt.Text.Replace(".", ","), out double profitValue);
                float.TryParse(RrTextBox.Text.Replace(".", ","), out float rrValue);

                // On récupère les confluences
                var confluences = DynamicFieldsPanel.Children
                                    .OfType<TextBox>()
                                    .Select(tb => new ChampPersonnalise(tb.Tag.ToString(), tb.Text))
                                    .ToList();

                if (IsEditMode)
                {
                    // --- LOGIQUE DE MISE À JOUR ---
                    _tradeEnEdition.Paire = PaireTextBox.Text.ToUpper();
                    _tradeEnEdition.TypeOrdre = (TypeOrdre)TypeOrdreComboBox.SelectedItem;
                    _tradeEnEdition.Result = (Resultat)ResultComboBox.SelectedItem;
                    _tradeEnEdition.DateEntree = CombineDateTime(DateEntreePicker.SelectedDate, TimeEntreePicker.Value);
                    _tradeEnEdition.DateSortie = CombineDateTime(DateSortiePicker.SelectedDate, TimeSortiePicker.Value);
                    _tradeEnEdition.RR = rrValue;
                    _tradeEnEdition.ImageLtf = ImageLtfTextBox.Text;
                    _tradeEnEdition.ImageHtf = ImageHtfTextBox.Text;
                    _tradeEnEdition.description = descriptionTextbox.Text;
                    _tradeEnEdition.Profit = profitValue;
                    _tradeEnEdition.ChampsPersonnalises = confluences;

                    if (modeJournal) _strategie.UpdateJournal(_tradeEnEdition);
                    else _strategie.UpdateTrade(_tradeEnEdition);
                    MessageBox.Show("Trade mis à jour !", "Succès", MessageBoxButton.OK, MessageBoxImage.Information);
                    this.Close();
                }
                else
                {
                    // --- LOGIQUE D'AJOUT CLASSIQUE ---
                    var nouveauTrade = new Trade(profitValue)
                    {
                        Paire = PaireTextBox.Text.ToUpper(),
                        TypeOrdre = (TypeOrdre)TypeOrdreComboBox.SelectedItem,
                        Result = (Resultat)ResultComboBox.SelectedItem,
                        DateEntree = CombineDateTime(DateEntreePicker.SelectedDate, TimeEntreePicker.Value),
                        DateSortie = CombineDateTime(DateSortiePicker.SelectedDate, TimeSortiePicker.Value),
                        RR = rrValue,
                        ImageLtf = ImageLtfTextBox.Text,
                        ImageHtf = ImageHtfTextBox.Text,
                        description = descriptionTextbox.Text,
                        strategie = _strategie.Nom,
                        ChampsPersonnalises = confluences
                    };

                    if (modeJournal) _strategie.AddJournal(nouveauTrade);
                    else _strategie.AddTrade(nouveauTrade);

                    if (texetat != null)
                    {
                        texetat.Visibility = Visibility.Visible;
                        texetat.Text = $"Trade {nouveauTrade.Paire} enregistré !";
                    }
                    ViderChamps();
                }
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

        private void Cancel_Click(object sender, RoutedEventArgs e) { this.DialogResult = true; this.Close(); }
    }
}