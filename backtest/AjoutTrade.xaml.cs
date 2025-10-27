using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using backtest;

namespace backtest
{
    public partial class AjoutTrade : Window
    {
        private Strategie _strategie;
        private bool modeJournal;
        public AjoutTrade(Strategie strategie,bool modeJournal=false)
        {
            InitializeComponent();
            _strategie = strategie;
            this.modeJournal = modeJournal;
            if (modeJournal == true)
            {
                profitTxt.Visibility = Visibility.Visible;
            }
            // Initialiser les combobox avec les options disponibles
            TypeOrdreComboBox.ItemsSource = Enum.GetValues(typeof(TypeOrdre)).Cast<TypeOrdre>();
            ResultComboBox.ItemsSource = Enum.GetValues(typeof(Resultat)).Cast<Resultat>();

            // Charger les champs dynamiques spécifiques à la stratégie
            ChargerChampsDynamique();
        }

        private void ChargerChampsDynamique()
        {
            foreach (var header in _strategie.GetDynamicHeaders())
            {
                var label = new TextBlock
                {
                    Text = header,
                    Foreground = System.Windows.Media.Brushes.White,
                    Margin = new Thickness(0, 5, 0, 5)
                };

                var textBox = new TextBox
                {
                    Width = 380,
                    Height = 30,
                    Background = System.Windows.Media.Brushes.Gray,
                    Foreground = System.Windows.Media.Brushes.White,
                    BorderBrush = System.Windows.Media.Brushes.Transparent,
                    Margin = new Thickness(0, 5, 0, 5),
                    Tag = header // Utilisé pour identifier le champ
                };

                DynamicFieldsPanel.Children.Add(label);
                DynamicFieldsPanel.Children.Add(textBox);
            }
        }

        private void SaveTrade_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                // Récupérer les informations du formulaire
                var paire = PaireTextBox.Text.ToUpper();
                var typeOrdre = (TypeOrdre)TypeOrdreComboBox.SelectedItem;
                var result = (Resultat)ResultComboBox.SelectedItem;
                var dateEntree = CombineDateTime(DateEntreePicker.SelectedDate, TimeEntreePicker.Value);
                var dateSortie = CombineDateTime(DateSortiePicker.SelectedDate, TimeSortiePicker.Value);
                var rr = float.Parse(RrTextBox.Text);
                var imageLtf = ImageLtfTextBox.Text;
                var imageHtf = ImageHtfTextBox.Text;
                var description = descriptionTextbox.Text;
                var profit = Int64.Parse(profitTxt.Text);
                // Champs personnalisés
                var champsPersonnalises = DynamicFieldsPanel.Children
                    .OfType<TextBox>()
                    .Select(tb => new ChampPersonnalise(tb.Tag.ToString(), tb.Text.ToLower()))
                    .ToList();

                // Créer le nouvel objet Trade
                var trade = new Trade(profit)
                {
                    Paire = paire,
                    TypeOrdre = typeOrdre,
                    Result = result,
                    DateEntree = dateEntree,
                    DateSortie = dateSortie,
                    RR = rr,
                    ImageLtf = imageLtf,
                    ImageHtf = imageHtf,
                    description = description,
                    ChampsPersonnalises = champsPersonnalises
                    
                };

                // Ajouter le trade à la stratégie
                if (modeJournal == false)
                {
                    _strategie.AddTrade(trade);
                }
                else
                {
                    _strategie.AddJournal(trade);
                }
                

                texetat.Visibility = Visibility.Visible;
                PaireTextBox.Text = ""; TypeOrdreComboBox.SelectedItem = null; ResultComboBox.SelectedItem = null; DateEntreePicker.SelectedDate = null; DateSortiePicker.SelectedDate = null; RrTextBox.Text = "";
                ImageLtfTextBox.Text = ""; ImageHtfTextBox.Text = null; descriptionTextbox.Text = null; DynamicFieldsPanel.Children.Clear(); ChargerChampsDynamique();


            }
            catch (Exception ex)
            {
                MessageBox.Show($"Une erreur est survenue : {ex.Message}", "Erreur", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            // Fermer la fenêtre sans enregistrer
            this.Close();
        }

        private DateTime CombineDateTime(DateTime? date, DateTime? time)
        {
            if (date == null || time == null)
            {
                throw new InvalidOperationException("Les champs Date et Heure doivent être remplis.");
            }

            return date.Value.Date + time.Value.TimeOfDay;
        }
    }
}