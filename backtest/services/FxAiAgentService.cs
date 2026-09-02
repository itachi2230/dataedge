using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace backtest.Services
{
    public class FxAiAgentService
    {
        private readonly FxCloudService _cloudService;

        public FxAiAgentService(FxCloudService cloudService)
        {
            _cloudService = cloudService ?? throw new ArgumentNullException(nameof(cloudService));
        }

        /// <summary>
        /// Envoie la requête au serveur et s'abonne au Hub Mercure pour recevoir la réponse en temps réel.
        /// </summary>
        public async Task SendMessageToAiStreamAsync(string prompt, Action<string> onChunkReceived, string userContext = "")
        {
            // 1. Vérifications réseau habituelles
            string status = await _cloudService.GetCloudStatusAsync();
            if (status == "OFFLINE_NO_INTERNET")
            {
                onChunkReceived?.Invoke("Connexion Internet non disponible.");
                return;
            }
            if (status == "OFFLINE_SERVER_DOWN")
            {
                onChunkReceived?.Invoke("Le serveur de l'IA est actuellement injoignable.");
                return;
            }
            if (status == "ONLINE_NO_ACCOUNT" || string.IsNullOrEmpty(_cloudService.CurrentToken))
            {
                onChunkReceived?.Invoke("Veuillez vous connecter à votre compte DataEdge pour utiliser l'agent IA.");
                return;
            }

            // 2. Générer un ID de session unique pour cette discussion
            string sessionId = Guid.NewGuid().ToString();

            // L'URL publique de ton Hub Mercure sur le port 3000
            // Le "topic" doit être EXACTEMENT identique à celui défini dans ton contrôleur Symfony
            string topic = $"https://fxdataedge.com/chat/{sessionId}";
            string mercureHubUrl = $"https://fxdataedge.com:3000/.well-known/mercure?topic={Uri.EscapeDataString(topic)}";

            try
            {
                // 3. Ouvrir l'écouteur SSE (Server-Sent Events) vers Mercure en tâche de fond (Fire & Forget la connexion)
                using var httpClient = new HttpClient();
                httpClient.Timeout = TimeSpan.FromMinutes(5); // Ne pas couper la connexion trop vite

                // On lance l'écoute asynchrone du flux Mercure
                var listenTask = Task.Run(async () =>
                {
                    try
                    {
                        using var responseStream = await httpClient.GetStreamAsync(mercureHubUrl);
                        using var reader = new StreamReader(responseStream, Encoding.UTF8);

                        while (!reader.EndOfStream)
                        {
                            string line = await reader.ReadLineAsync();

                            // Le protocole SSE envoie les données préfixées par "data: "
                            if (!string.IsNullOrEmpty(line) && line.StartsWith("data:"))
                            {
                                string rawJson = line.Substring(5).Trim(); // On enlève "data:"

                                try
                                {
                                    // Extraction du fragment de texte envoyé par Symfony
                                    using var doc = JsonDocument.Parse(rawJson);
                                    var root = doc.RootElement;

                                    // Si le serveur a envoyé une erreur
                                    if (root.TryGetProperty("error", out var errorProp))
                                    {
                                        onChunkReceived?.Invoke($"\n[Erreur Serveur] {errorProp.GetString()}");
                                        break;
                                    }

                                    // Si la génération est terminée
                                    if (root.TryGetProperty("done", out var doneProp) && doneProp.GetBoolean() == true)
                                    {
                                        break; // On sort de la boucle de lecture
                                    }

                                    // Sinon, on récupère le texte
                                    if (root.TryGetProperty("text", out var textProp))
                                    {
                                        string chunk = textProp.GetString();
                                        if (!string.IsNullOrEmpty(chunk))
                                        {
                                            onChunkReceived?.Invoke(chunk);
                                        }
                                    }
                                }
                                catch (JsonException)
                                {
                                    // En cas de bruit ou de ligne vide du protocole
                                }
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        FxCloudService.Log($"Erreur de réception du flux Mercure : {ex.Message}");
                    }
                });

                // Petit délai de 100ms pour s'assurer que l'écouteur SSE est bien connecté avant d'envoyer le prompt
                await Task.Delay(100);

                // 4. Envoyer le prompt à l'API Symfony de manière classique
                var payload = new
                {
                    message = prompt,
                    context = userContext,
                    app_id = _cloudService.AppId,
                    session_id = sessionId // On passe l'ID de session pour que Symfony publie sur le bon canal !
                };

                var payloadJson = JsonSerializer.Serialize(payload);
                var requestUri = "api/ai/chat";

                var response = await _cloudService.SecureRequestAsync(async () =>
                {
                    var request = new HttpRequestMessage(HttpMethod.Post, requestUri)
                    {
                        Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                    };
                    return await _cloudService.GetHttpClient().SendAsync(request);
                });

                if (!response.IsSuccessStatusCode)
                {
                    onChunkReceived?.Invoke($"[Erreur {response.StatusCode}] Impossible de lancer la génération.");
                    return;
                }

                // 5. Attendre que la tâche d'écoute Mercure se termine proprement
                await listenTask;
            }
            catch (Exception ex)
            {
                FxCloudService.Log($"Erreur globale FxAiAgent : {ex.Message}");
                onChunkReceived?.Invoke("Une erreur de communication est survenue.");
            }
        }
    }
}