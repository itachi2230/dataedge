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
                Text = "DataEdge Intelligence. Je suis le module d'analyse intégré au logiciel : votre journal, vos stratégies et le marché en direct passent par moi. Que voulez-vous regarder ?",
                Timestamp = DateTime.Now
            });

            // Chargement automatique de l'historique persisté côté serveur :
            // si des échanges existent déjà, ils remplacent le message d'accueil.
            _ = LoadHistoryAsync();
        }

        /// <summary>
        /// Charge l'historique de conversation depuis le serveur (aucun appel Gemini)
        /// et l'affiche à la place du message d'accueil. La saisie reste bloquée
        /// pendant le chargement ; si un premier message a déjà été envoyé entre-
        /// temps, l'historique n'écrase pas la conversation en cours (il sera
        /// rechargé à la prochaine ouverture de la fenêtre).
        /// </summary>
        private async Task LoadHistoryAsync()
        {
            TxtInput.IsEnabled = false;
            BtnSend.IsEnabled = false;
            try
            {
                var history = await Task.Run(() => _aiService.GetChatHistoryAsync());
                await Dispatcher.InvokeAsync(() =>
                {
                    bool userAlreadyActive = false;
                    foreach (var message in Messages)
                    {
                        if (message.Sender == "User")
                        {
                            userAlreadyActive = true;
                            break;
                        }
                    }

                    if (history.Count > 0 && !userAlreadyActive)
                    {
                        Messages.Clear();
                        foreach (var message in history)
                        {
                            Messages.Add(message);
                        }
                        ScrollToBottom();
                    }
                });
            }
            finally
            {
                TxtInput.IsEnabled = true;
                BtnSend.IsEnabled = true;
                TxtInput.Focus();
            }
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

            // 2. Afficher l'indicateur de frappe
            if (LoadingIndicator != null)
            {
                LoadingIndicator.Visibility = Visibility.Visible;
            }

            // 3. Ajouter la bulle vide de l'IA (qui va se remplir en direct)
            var aiMessage = new ChatMessage { Sender = "AI", Text = "", Timestamp = DateTime.Now };
            Messages.Add(aiMessage);
            ScrollToBottom();
            _activeAiMessage = aiMessage;

            // 4. Statut live (style Cline) : réflexion du modèle + exécution des outils,
            //    affichés dans le bandeau de la bulle avec un chrono. Jamais mélangés
            //    au texte final : le bandeau disparaît dès que la réponse commence.
            StartStatusTracking(aiMessage);

            // 5. Lancer la lecture du flux asynchrone
            try
            {
                string identityContext = await _workspaceService.BuildIdentityContextAsync();
                await Task.Run(async () =>
                {
                    await _aiService.SendMessageToAiStreamAsync(query, (chunk) =>
                    {
                        // Mettre à jour l'UI sur le thread principal
                        Dispatcher.Invoke(() =>
                        {
                            // Cacher l'indicateur dès qu'on reçoit le premier token
                            if (LoadingIndicator != null && LoadingIndicator.Visibility == Visibility.Visible)
                            {
                                LoadingIndicator.Visibility = Visibility.Collapsed;
                            }

                            // Premier fragment de la réponse finale : le bandeau de
                            // statut s'efface, la place est laissée au texte propre.
                            StopStatusTracking();

                            // On ajoute le fragment. Grâce à INotifyPropertyChanged, l'UI se met à jour instantanément !
                            aiMessage.Text += chunk;

                            ScrollToBottom();
                        });
                    }, identityContext, _workspaceService.GetToolDefinitions(), HandleToolCallAsync,
                    (status) =>
                    {
                        // Statuts transitoires (reasoning + outils) : marshalés vers l'UI.
                        Dispatcher.Invoke(() => PushStatus(status));
                    });
                });
            }
            catch (Exception ex)
            {
                // En cas d'erreur réseau ou API, on affiche l'erreur dans la bulle
                StopStatusTracking();
                aiMessage.Text = ex is AiAgentException aiException
                    ? $"[ERREUR AGENT IA]\n{aiException.Error.ToDisplayText()}"
                    : $"[ERREUR AGENT IA]\nType : {ex.GetType().FullName}\nMessage : {ex.Message}";
                FxCloudService.Log($"Erreur affichée dans le chat : {ex}");
            }
            finally
            {
                // 6. Réactiver les contrôles de saisie
                StopStatusTracking();
                _activeAiMessage = null;
                if (LoadingIndicator != null)
                {
                    LoadingIndicator.Visibility = Visibility.Collapsed;
                }
                TxtInput.IsEnabled = true;
                BtnSend.IsEnabled = true;
                TxtInput.Focus();
            }
        }

        #region Bandeau de statut live (réflexion + outils)

        private ChatMessage _statusMessage;
        private ChatMessage _activeAiMessage;
        private DateTime _toolStatusAt;
        private readonly System.Text.StringBuilder _reasoningTail = new System.Text.StringBuilder();
        private string _toolStatusLine;
        private System.Windows.Threading.DispatcherTimer _statusTimer;

        /// <summary>
        /// Démarre le suivi du statut pour la bulle en cours de génération :
        /// dernier événement (raisonnement ou outil) rafraîchi 10x/s. Aucun
        /// chrono affiché : l'utilisateur ne doit pas percevoir la latence.
        /// </summary>
        private void StartStatusTracking(ChatMessage message)
        {
            StopStatusTracking();
            _statusMessage = message;
            _reasoningTail.Clear();
            _toolStatusLine = null;
            _statusTimer = new System.Windows.Threading.DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
            _statusTimer.Tick += (s, e) => RefreshStatusText();
            _statusTimer.Start();
        }

        /// <summary>
        /// Arrête le suivi et masque le bandeau (réponse finale commencée, erreur
        /// ou fin de génération) : la bulle reste propre, aucun contenu technique.
        /// </summary>
        private void StopStatusTracking()
        {
            if (_statusTimer != null)
            {
                _statusTimer.Stop();
                _statusTimer = null;
            }
            if (_statusMessage != null)
            {
                _statusMessage.StatusText = null;
                _statusMessage = null;
            }
        }

        /// <summary>
        /// Reçoit un fragment de statut (thread UI) : soit un morceau de réflexion
        /// du modèle (chunks reasoning OpenRouter), soit un statut d'outil local
        /// (🔍 en cours / ✓ terminé). Le dernier événement d'outil prime sur la
        /// réflexion pendant 4 s pour que l'action reste lisible.
        /// </summary>
        private void PushStatus(string status)
        {
            if (string.IsNullOrEmpty(status)) return;

            // Tour multi-étapes : après un premier fragment de texte le suivi est
            // arrêté (bulle propre) ; une action d'outil réarme le bandeau.
            if (_statusMessage == null)
            {
                if (_activeAiMessage == null) return;
                StartStatusTracking(_activeAiMessage);
            }

            if (status.StartsWith("🔍") || status.StartsWith("✓"))
            {
                _toolStatusLine = status;
                _toolStatusAt = DateTime.Now;
            }
            else
            {
                // Réflexion du modèle : on garde la fin (~220 derniers caractères)
                // pour un affichage compact qui « défile » comme un stream.
                _reasoningTail.Append(status.Replace('\n', ' ').Replace('\r', ' '));
                if (_reasoningTail.Length > 220)
                {
                    _reasoningTail.Remove(0, _reasoningTail.Length - 220);
                }
            }
            RefreshStatusText();
        }

        private void RefreshStatusText()
        {
            if (_statusMessage == null) return;

            string activity = null;

            // L'action d'outil masque la réflexion pendant 4 s après son émission.
            if (_toolStatusLine != null && (DateTime.Now - _toolStatusAt).TotalSeconds < 4)
            {
                activity = _toolStatusLine;
            }
            else if (_reasoningTail.Length > 0)
            {
                string tail = _reasoningTail.ToString().TrimStart();
                activity = tail.Length > 160 ? "…" + tail.Substring(tail.Length - 160) : tail;
            }

            // Aucun décompte de secondes : on ne montre jamais la latence,
            // seulement l'activité en cours (raisonnement ou outil).
            _statusMessage.StatusText = activity ?? "Réflexion en cours…";
        }

        #endregion

        /// <summary>
        /// Demandé par l'en-tête du contrôle (mode panneau flottant) : le
        /// MainWindow masque le copilote sans quitter la vue courante.
        /// </summary>
        public event EventHandler CloseRequested;

        /// <summary>
        /// Demandé par le bouton ⤢ de l'en-tête : le MainWindow bascule le
        /// panneau entre sa largeur normale et une largeur étendue.
        /// </summary>
        public event EventHandler ExpandRequested;

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            CloseRequested?.Invoke(this, EventArgs.Empty);
        }

        private void BtnExpand_Click(object sender, RoutedEventArgs e)
        {
            ExpandRequested?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>
        /// Le MainWindow notifie l'état réel du panneau après bascule pour que
        /// le bouton affiche le bon glyphe (⤢ agrandir / ⤡ réduire).
        /// </summary>
        public void SetExpandedState(bool expanded)
        {
            BtnExpand.Content = expanded ? "⤡" : "⤢";
            BtnExpand.ToolTip = expanded ? "Réduire la discussion" : "Agrandir la discussion";
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

    /// <summary>
    /// Convertisseur de largeur : renvoie la largeur source (ex. ActualWidth du
    /// ScrollViewer du chat) diminuée de la marge passée en paramètre. Sert à
    /// rendre les tuiles de message et le bandeau de statut fluides : leur
    /// largeur maximale suit celle du panneau (normal 430 px ou agrandi).
    /// </summary>
    public class WidthMinusConverter : System.Windows.Data.IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            if (value is double width &&
                double.TryParse(parameter as string, System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out double margin))
            {
                double result = width - margin;
                return result > 0 ? result : 0d;
            }

            // Source pas encore mesurée : aucune limite.
            return double.NaN;
        }

        public object ConvertBack(object value, Type targetType, object parameter, System.Globalization.CultureInfo culture)
        {
            throw new NotSupportedException();
        }
    }
}