using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using backtest.Models;
using backtest.Services;

namespace backtest.Views
{
    public partial class FxAiChatControl : UserControl
    {
        private readonly FxAiAgentService _aiService;
        public ObservableCollection<ChatMessage> Messages { get; set; }

        public FxAiChatControl(FxCloudService cloudService)
        {
            InitializeComponent();
            _aiService = new FxAiAgentService(cloudService);
            Messages = new ObservableCollection<ChatMessage>();
            ChatItemsControl.ItemsSource = Messages;

            Messages.Add(new ChatMessage
            {
                Sender = "AI",
                Text = "Bonjour ! Je suis l'agent IA de DataEdge. Posez-moi des questions sur vos performances, le sentiment de marché ou pour synthétiser vos notes de trading.",
                Timestamp = DateTime.Now
            });
        }

        private async void SendMessage(string messageText = null)
        {
            string query = messageText ?? TxtInput.Text.Trim();
            if (string.IsNullOrEmpty(query)) return;

            // Désactiver la saisie pendant la génération
            TxtInput.IsEnabled = false;
            BtnSend.IsEnabled = false;

            if (messageText == null)
            {
                TxtInput.Clear();
            }

            // 1. Ajouter le message de l'utilisateur
            Messages.Add(new ChatMessage { Sender = "User", Text = query, Timestamp = DateTime.Now });
            ScrollToBottom();

            // 2. Afficher l'indicateur de chargement
            if (LoadingIndicator != null)
            {
                LoadingIndicator.Visibility = Visibility.Visible;
            }

            // 3. Ajouter la bulle vide de l'IA (qui va se remplir en direct)
            var aiMessage = new ChatMessage { Sender = "AI", Text = "", Timestamp = DateTime.Now };
            Messages.Add(aiMessage);
            ScrollToBottom();

            // 4. Lancer la lecture du flux asynchrone
            try
            {
                await Task.Run(async () =>
                {
                    await _aiService.SendMessageToAiStreamAsync(query, (chunk) =>
                    {
                        // Mettre à jour l'UI sur le thread principal
                        Dispatcher.Invoke(() =>
                        {
                            // Cacher le chargement dès qu'on reçoit le premier token
                            if (LoadingIndicator != null && LoadingIndicator.Visibility == Visibility.Visible)
                            {
                                LoadingIndicator.Visibility = Visibility.Collapsed;
                            }

                            // On ajoute le fragment. Grâce à INotifyPropertyChanged, l'UI se met à jour instantanément !
                            aiMessage.Text += chunk;

                            ScrollToBottom();
                        });
                    });
                });
            }
            catch (Exception ex)
            {
                // En cas d'erreur réseau ou API, on affiche l'erreur dans la bulle
                aiMessage.Text = $"[Erreur de connexion : {ex.Message}]";
            }
            finally
            {
                // 5. Réactiver les contrôles de saisie
                if (LoadingIndicator != null)
                {
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                }
                TxtInput.IsEnabled = true;
                BtnSend.IsEnabled = true;
                TxtInput.Focus();
            }
        }
        private void ScrollToBottom()
        {
            ChatScrollViewer.UpdateLayout();
            ChatScrollViewer.ScrollToBottom();
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e) => SendMessage();

        private void TxtInput_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
            {
                SendMessage();
                e.Handled = true;
            }
        }

        private void BtnCopy_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string textToCopy)
            {
                if (!string.IsNullOrEmpty(textToCopy))
                {
                    Clipboard.SetText(textToCopy);

                    string originalContent = btn.Content.ToString();
                    btn.Content = "✔️ Copié !";
                    btn.Foreground = System.Windows.Media.Brushes.LightGreen;

                    var timer = new System.Windows.Threading.DispatcherTimer
                    {
                        Interval = TimeSpan.FromSeconds(1.5)
                    };
                    timer.Tick += (s, args) =>
                    {
                        btn.Content = originalContent;
                        btn.Foreground = System.Windows.Media.Brushes.Gray;
                        timer.Stop();
                    };
                    timer.Start();
                }
            }
        }

        private void BtnResend_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string textToResend)
            {
                SendMessage(textToResend);
            }
        }
    }
}