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
        private readonly AgentWorkspaceService _workspaceService;
        public ObservableCollection<ChatMessage> Messages { get; set; }

        public FxAiChatControl(FxCloudService cloudService)
        {
            InitializeComponent();
            _aiService = new FxAiAgentService(cloudService);
            _workspaceService = new AgentWorkspaceService(cloudService);
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
                string workspaceContext = await _workspaceService.BuildContextAsync();
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
                    }, workspaceContext, _workspaceService.GetToolDefinitions(), HandleToolCallAsync);
                });
            }
            catch (Exception ex)
            {
                // En cas d'erreur réseau ou API, on affiche l'erreur dans la bulle
                aiMessage.Text = ex is AiAgentException aiException
                    ? $"[ERREUR AGENT IA]\n{aiException.Error.ToDisplayText()}"
                    : $"[ERREUR AGENT IA]\nType : {ex.GetType().FullName}\nMessage : {ex.Message}";
                FxCloudService.Log($"Erreur affichée dans le chat : {ex}");
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

        private async Task<AiToolResult> HandleToolCallAsync(AiToolCall call)
        {
            // Toute action déclarée requires_confirmation (création/suppression de stratégie,
            // ajout de trade, habitudes...) passe par une validation explicite de l'utilisateur.
            bool allowed = true;
            if (_workspaceService.RequiresConfirmation(call.Name))
            {
                allowed = (bool)Dispatcher.Invoke(new Func<bool>(() =>
                    MessageBox.Show(
                        $"L'agent souhaite effectuer l'action '{call.Name}'.\n\nArguments : {call.Arguments}\n\nAutoriser cette modification ?",
                        "Confirmation requise", MessageBoxButton.YesNo, MessageBoxImage.Question) == MessageBoxResult.Yes));
            }

            if (!allowed)
                return AiToolResult.Error("Action refusée ou annulée par l'utilisateur.");

            return await _workspaceService.ExecuteAsync(call, requestedCall => Task.FromResult(true));
        }
        private void ScrollToBottom()
        {
            ChatScrollViewer.UpdateLayout();
            ChatScrollViewer.ScrollToBottom();
        }

        private void BtnSend_Click(object sender, RoutedEventArgs e) => SendMessage();

        private void QuickPrompt_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button button && button.Tag is string prompt)
            {
                TxtInput.Text = prompt;
                SendMessage(prompt);
            }
        }

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