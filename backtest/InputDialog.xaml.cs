using System.Windows;

namespace backtest
{
    public partial class InputDialog : Window
    {
        // Propriété publique pour récupérer la valeur saisie
        public string InputValue { get; private set; }

        public InputDialog(string message, string defaultInput = "")
        {
            InitializeComponent();
            MessageTextBlock.Text = message;
            InputTextBox.Text = defaultInput;
            this.Loaded += (s, e) => InputTextBox.Focus();
        }

        private void OkButton_Click(object sender, RoutedEventArgs e)
        {
            // La saisie est le résultat
            InputValue = InputTextBox.Text;
            DialogResult = true; // Indique que la saisie est valide
            this.Close();
        }

        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            // Laisser InputValue à null ou vide (selon l'initialisation)
            DialogResult = false; // Indique que l'action est annulée
            this.Close();
        }

        private void InputTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            // Sélectionne tout le texte au focus (pratique pour effacer ou éditer rapidement)
            InputTextBox.SelectAll();
        }
    }
}