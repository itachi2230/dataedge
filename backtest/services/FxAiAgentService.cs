using System;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using backtest.Models;

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
        /// Envoie la requête au serveur et lit sa réponse streamée en temps réel.
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

            // Identifiant conservé pour permettre au backend d'associer une future session.
            string sessionId = Guid.NewGuid().ToString();

            try
            {
                // Le serveur actuel renvoie les fragments directement sur la réponse POST.
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
                    return await _cloudService.GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
                });

                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync();
                    throw new AiAgentException(new AiAgentError(
                        $"Le serveur IA a refusé la requête ({(int)response.StatusCode} {response.StatusCode}).",
                        string.IsNullOrWhiteSpace(body) ? "Réponse serveur vide." : body));
                }

                using (response)
                using (var responseStream = await response.Content.ReadAsStreamAsync())
                using (var reader = new StreamReader(responseStream, Encoding.UTF8))
                {
                    while (!reader.EndOfStream)
                    {
                        var line = await reader.ReadLineAsync();
                        if (!string.IsNullOrEmpty(line)) EmitChunk(line, onChunkReceived);
                    }
                }
            }
            catch (AiAgentException) { throw; }
            catch (Exception ex)
            {
                FxCloudService.Log($"Erreur globale FxAiAgent : {ex.Message}");
                throw new AiAgentException(new AiAgentError(
                    "La communication avec l'agent IA a échoué.", ex.Message, ex), ex);
            }
        }

        private static void EmitChunk(string line, Action<string> onChunkReceived)
        {
            var payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? line.Substring(5).Trim()
                : line;

            try
            {
                using (var doc = JsonDocument.Parse(payload))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var error))
                        throw new AiAgentException(new AiAgentError("Le serveur IA a retourné une erreur.", error.ToString()));
                    if (root.TryGetProperty("text", out var text))
                        onChunkReceived?.Invoke(text.GetString() ?? string.Empty);
                    return;
                }
            }
            catch (JsonException)
            {
                onChunkReceived?.Invoke(payload);
            }
        }
    }

    public sealed class AiAgentException : Exception
    {
        public AiAgentException(AiAgentError error, Exception innerException = null)
            : base(error?.ToDisplayText(), innerException)
        {
            Error = error;
        }

        public AiAgentError Error { get; }
    }
}