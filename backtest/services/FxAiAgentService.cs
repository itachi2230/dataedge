using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
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
        public async Task SendMessageToAiStreamAsync(string prompt, Action<string> onChunkReceived, string userContext = "", IReadOnlyList<AiToolDefinition> tools = null, Func<AiToolCall, Task<AiToolResult>> toolHandler = null)
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
                List<AiToolResultPayload> pendingResults = null;
                for (int turn = 0; turn < 8; turn++)
                {
                    var turnResult = await SendTurnAsync(prompt, userContext, sessionId, tools, pendingResults, onChunkReceived);

                    // Réponse textuelle finale : plus aucun outil demandé, la boucle se termine.
                    if (turnResult.ToolCalls.Count == 0) break;

                    if (toolHandler == null)
                        throw new AiAgentException(new AiAgentError("L'agent a demandé une action mais aucun exécuteur local n'est disponible."));

                    // Exécution locale de TOUS les outils demandés par le modèle (appels parallèles inclus),
                    // puis nouveau tour serveur avec les résultats au format functionResponse.
                    pendingResults = new List<AiToolResultPayload>();
                    foreach (var toolCall in turnResult.ToolCalls)
                    {
                        AiToolResult toolResult = await ExecuteSafelyAsync(toolHandler, toolCall);
                        onChunkReceived?.Invoke($"\n[Outil {toolCall.Name}: {(toolResult.IsSuccess ? "terminé" : "refusé")}]\n");
                        pendingResults.Add(new AiToolResultPayload
                        {
                            Name = toolCall.Name,
                            Id = toolCall.Id,
                            Arguments = toolCall.Arguments,
                            Content = toolResult.Content,
                            IsError = !toolResult.IsSuccess
                        });
                    }
                    prompt = string.Empty; // Les tours suivants ne transportent que les résultats d'outils
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

        private async Task<ToolTurnResult> SendTurnAsync(string prompt, string userContext, string sessionId, IReadOnlyList<AiToolDefinition> tools, List<AiToolResultPayload> toolResults, Action<string> onChunkReceived)
        {
            var payload = new
            {
                message = prompt,
                context = userContext,
                app_id = _cloudService.AppId,
                session_id = sessionId,
                tools = tools ?? new List<AiToolDefinition>(),
                tool_results = toolResults
            };
            var payloadJson = JsonSerializer.Serialize(payload);
            var response = await _cloudService.SecureRequestAsync(async () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, "api/ai/chat")
                {
                    Content = new StringContent(payloadJson, Encoding.UTF8, "application/json")
                };
                return await _cloudService.GetHttpClient().SendAsync(request, HttpCompletionOption.ResponseHeadersRead);
            });
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync();
                throw new AiAgentException(new AiAgentError($"Le serveur IA a refusé la requête ({(int)response.StatusCode} {response.StatusCode}).", body));
            }

            using (response)
            using (var reader = new StreamReader(await response.Content.ReadAsStreamAsync(), Encoding.UTF8))
            {
                // On lit le flux EN ENTIER : un même tour du modèle peut contenir du texte
                // ET plusieurs functionCall en parallèle. Le texte est affiché en direct,
                // les tool_calls sont collectés puis exécutés après la fin du flux.
                var toolCalls = new List<AiToolCall>();
                while (!reader.EndOfStream)
                {
                    var line = await reader.ReadLineAsync();
                    if (string.IsNullOrWhiteSpace(line)) continue;
                    if (TryReadToolCall(line, out var toolCall))
                    {
                        toolCalls.Add(toolCall);
                        continue;
                    }
                    EmitChunk(line, onChunkReceived);
                }
                return new ToolTurnResult { ToolCalls = toolCalls };
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

        private static bool TryReadToolCall(string line, out AiToolCall toolCall)
        {
            toolCall = null;
            var payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? line.Substring(5).Trim() : line;
            try
            {
                using (var doc = JsonDocument.Parse(payload))
                {
                    var root = doc.RootElement;
                    if (!root.TryGetProperty("tool_call", out var call)) return false;
                    string id = call.TryGetProperty("id", out var idElement) && idElement.ValueKind == JsonValueKind.String
                        ? idElement.GetString()
                        : null;
                    toolCall = new AiToolCall
                    {
                        Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id,
                        Name = call.GetProperty("name").GetString(),
                        Arguments = NormalizeArguments(call)
                    };
                    return true;
                }
            }
            catch (JsonException)
            {
                return false;
            }
        }

        /// <summary>
        /// Les arguments d'un functionCall Gemini doivent être un objet JSON.
        /// Si absents ou invalides, on normalise vers un objet vide.
        /// </summary>
        private static JsonElement NormalizeArguments(JsonElement call)
        {
            if (call.TryGetProperty("arguments", out var args) && args.ValueKind == JsonValueKind.Object)
                return args.Clone();
            return JsonDocument.Parse("{}").RootElement.Clone();
        }

        /// <summary>
        /// Exécute un tool via le handler local en interceptant toute exception :
        /// un outil qui plante ne doit jamais casser la boucle agent (le modèle
        /// reçoit l'erreur sous forme de functionResponse et peut se corriger).
        /// </summary>
        private static async Task<AiToolResult> ExecuteSafelyAsync(Func<AiToolCall, Task<AiToolResult>> toolHandler, AiToolCall call)
        {
            try
            {
                var result = await toolHandler(call);
                return result ?? AiToolResult.Error("Résultat vide retourné par l'outil.");
            }
            catch (Exception ex)
            {
                FxCloudService.Log($"Erreur exécution outil agent {call.Name}: {ex.Message}");
                return AiToolResult.Error($"Échec de l'outil {call.Name} : {ex.Message}");
            }
        }

        private sealed class ToolTurnResult
        {
            public List<AiToolCall> ToolCalls { get; set; } = new List<AiToolCall>();
        }
    }

    /// <summary>
    /// Payload client -> serveur transportant le résultat de l'exécution locale d'un tool
    /// (rejoué côté serveur en part functionResponse pour Gemini).
    /// </summary>
    public sealed class AiToolResultPayload
    {
        [JsonPropertyName("name")] public string Name { get; set; }
        [JsonPropertyName("id")] public string Id { get; set; }
        [JsonPropertyName("arguments")] public JsonElement Arguments { get; set; }
        [JsonPropertyName("content")] public string Content { get; set; }
        [JsonPropertyName("is_error")] public bool IsError { get; set; }
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