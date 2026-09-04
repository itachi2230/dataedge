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
        /// <param name="onStatusReceived">
        /// Callback optionnel pour les statuts transitoires (réflexion du modèle,
        /// exécution des outils) : jamais mélangés au texte final de la bulle.
        /// Appelé sur un thread de fond — l'appelant doit marshaler vers l'UI.
        /// </param>
        public async Task SendMessageToAiStreamAsync(string prompt, Action<string> onChunkReceived, string userContext = "", IReadOnlyList<AiToolDefinition> tools = null, Func<AiToolCall, Task<AiToolResult>> toolHandler = null, Action<string> onStatusReceived = null)
        {
            // 1. Vérifications réseau habituelles
            string status = await _cloudService.GetCloudStatusAsync(useCachedIfFresh: true);
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
                // Compteur des appels d'outils (nom + arguments) : coupe les boucles ou
                // le modele rappellerait indefiniment le meme outil avec les memes arguments.
                var executedCalls = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int turn = 0; turn < 8; turn++)
                {
                    // L'identité (context) ne part qu'au premier tour : le serveur la
                    // persiste une fois puis la rejoue depuis l'historique BDD.
                    var turnResult = await SendTurnAsync(prompt, turn == 0 ? userContext : string.Empty, sessionId, tools, pendingResults, onChunkReceived, onStatusReceived);

                    // Réponse textuelle finale : plus aucun outil demandé, la boucle se termine.
                    if (turnResult.ToolCalls.Count == 0) break;

                    if (toolHandler == null)
                        throw new AiAgentException(new AiAgentError("L'agent a demandé une action mais aucun exécuteur local n'est disponible."));

                    // Exécution locale de TOUS les outils demandés par le modèle (appels parallèles inclus),
                    // puis nouveau tour serveur avec les résultats au format functionResponse.
                    pendingResults = new List<AiToolResultPayload>();
                    foreach (var toolCall in turnResult.ToolCalls)
                    {
                        // Garde anti-boucle : un meme appel (outil + arguments identiques)
                        // deja execute deux fois n'est plus re-execute ; le modele recoit
                        // une erreur lui demandant de repondre avec les donnees obtenues.
                        string callKey = toolCall.Name + "|" + (toolCall.Arguments.ValueKind == JsonValueKind.Object ? toolCall.Arguments.GetRawText() : "{}");
                        executedCalls.TryGetValue(callKey, out int alreadyRun);
                        AiToolResult toolResult;
                        if (alreadyRun >= 2)
                        {
                            toolResult = AiToolResult.Error("Cet outil a deja ete execute deux fois avec des arguments identiques : le resultat figure deja dans la conversation. Reponds maintenant directement a partir des donnees obtenues, sans rappeler l'outil.");
                            FxCloudService.Log($"Agent IA : appel repete bloque ({toolCall.Name}).");
                        }
                        else
                        {
                            // Statut local : l'utilisateur voit l'action en cours dans le
                            // bandeau de statut (style Cline), jamais dans la bulle finale.
                            onStatusReceived?.Invoke($"🔍 {DescribeTool(toolCall.Name)}…");
                            toolResult = await ExecuteSafelyAsync(toolHandler, toolCall);
                            executedCalls[callKey] = alreadyRun + 1;
                            onStatusReceived?.Invoke($"✓ {DescribeTool(toolCall.Name)} — terminé");
                        }
                        // Aucun marqueur technique dans la bulle : le narratif du modele
                        // decrit deja ses acces. Journalisation seule.
                        FxCloudService.Log($"Agent IA outil {toolCall.Name}: {(toolResult.IsSuccess ? "termine" : "refuse")}");
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

        /// <summary>
        /// Charge l'historique de conversation persisté côté serveur (GET api/ai/history).
        /// Utilisé à l'ouverture du chat pour afficher les échanges précédents SANS
        /// rappeler Gemini. Retourne une liste vide si indisponible (hors ligne, non
        /// connecté, erreur) : le chat démarre alors sur son message d'accueil.
        /// </summary>
        public async Task<List<ChatMessage>> GetChatHistoryAsync(int limit = 40)
        {
            var history = new List<ChatMessage>();

            string status = await _cloudService.GetCloudStatusAsync(useCachedIfFresh: true);
            if (status == "OFFLINE_NO_INTERNET" || status == "OFFLINE_SERVER_DOWN"
                || status == "ONLINE_NO_ACCOUNT" || string.IsNullOrEmpty(_cloudService.CurrentToken))
            {
                return history;
            }

            try
            {
                var response = await _cloudService.SecureRequestAsync(() =>
                    _cloudService.GetHttpClient().GetAsync($"api/ai/history?limit={limit}"));
                if (!response.IsSuccessStatusCode)
                {
                    return history;
                }

                var body = await response.Content.ReadAsStringAsync();
                using (var doc = JsonDocument.Parse(body))
                {
                    if (!doc.RootElement.TryGetProperty("messages", out var messages)
                        || messages.ValueKind != JsonValueKind.Array)
                    {
                        return history;
                    }

                    foreach (var element in messages.EnumerateArray())
                    {
                        string role = element.TryGetProperty("role", out var r) && r.ValueKind == JsonValueKind.String
                            ? r.GetString() : null;
                        string content = element.TryGetProperty("content", out var c) && c.ValueKind == JsonValueKind.String
                            ? c.GetString() : null;
                        if (string.IsNullOrWhiteSpace(role) || string.IsNullOrWhiteSpace(content))
                        {
                            continue;
                        }

                        var timestamp = DateTime.Now;
                        if (element.TryGetProperty("createdAt", out var ts) && ts.ValueKind == JsonValueKind.String
                            && DateTime.TryParse(ts.GetString(), System.Globalization.CultureInfo.InvariantCulture,
                                System.Globalization.DateTimeStyles.RoundtripKind, out var parsed))
                        {
                            timestamp = parsed.ToLocalTime();
                        }

                        history.Add(new ChatMessage
                        {
                            Sender = role == "user" ? "User" : "AI",
                            Text = content,
                            Timestamp = timestamp
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                FxCloudService.Log($"Chargement historique agent IA : {ex.Message}");
            }

            return history;
        }

        private async Task<ToolTurnResult> SendTurnAsync(string prompt, string userContext, string sessionId, IReadOnlyList<AiToolDefinition> tools, List<AiToolResultPayload> toolResults, Action<string> onChunkReceived, Action<string> onStatusReceived = null)
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
                // Interrupteurs admin côté serveur (agent désactivé => 503, quota
                // utilisateur atteint => 429) : on privilégie le message métier du
                // corps JSON {"error": "..."} plutôt que le brut du code HTTP.
                throw new AiAgentException(new AiAgentError(ReadServerErrorMessage(body,
                    $"Le serveur IA a refusé la requête ({(int)response.StatusCode} {response.StatusCode})."), body));
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
                    EmitChunk(line, onChunkReceived, onStatusReceived);
                }
                return new ToolTurnResult { ToolCalls = toolCalls };
            }
        }

        /// <summary>
        /// Extrait le message métier d'une réponse d'erreur JSON du serveur
        /// {"error": "..."} (agent désactivé, quota utilisateur atteint...), avec
        /// repli sur le message technique générique si le corps n'est pas du JSON.
        /// </summary>
        private static string ReadServerErrorMessage(string body, string fallback)
        {
            try
            {
                using (var doc = JsonDocument.Parse(body))
                {
                    if (doc.RootElement.TryGetProperty("error", out var error)
                        && error.ValueKind == JsonValueKind.String)
                    {
                        string message = error.GetString();
                        if (!string.IsNullOrWhiteSpace(message))
                        {
                            return message;
                        }
                    }
                }
            }
            catch (JsonException)
            {
                // Corps non JSON (page d'erreur HTML, vide...) : on garde le repli.
            }
            return fallback;
        }

        private static void EmitChunk(string line, Action<string> onChunkReceived, Action<string> onStatusReceived = null)
        {
            // Lignes de commentaire SSE (": ...") : techniques, jamais du contenu.
            if (line.StartsWith(":", StringComparison.Ordinal)) return;

            var payload = line.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
                ? line.Substring(5).Trim()
                : line;
            if (payload.Length == 0) return;

            try
            {
                using (var doc = JsonDocument.Parse(payload))
                {
                    var root = doc.RootElement;
                    if (root.TryGetProperty("error", out var error))
                        throw new AiAgentException(new AiAgentError("Le serveur IA a retourné une erreur.", error.ToString()));
                    // Réflexion interne du modèle (delta.reasoning OpenRouter) : chunk
                    // DÉDIÉ {"reasoning": ...} — routé vers le bandeau de statut, jamais
                    // mélangé au texte de la bulle.
                    if (root.TryGetProperty("reasoning", out var reasoning) && reasoning.ValueKind == JsonValueKind.String)
                    {
                        string fragment = reasoning.GetString();
                        if (!string.IsNullOrEmpty(fragment))
                            onStatusReceived?.Invoke(fragment);
                        return;
                    }
                    if (root.TryGetProperty("text", out var text))
                        onChunkReceived?.Invoke(text.GetString() ?? string.Empty);
                    return;
                }
            }
            catch (JsonException)
            {
                // Payload non JSON (bruit reseau, fragment technique) : ne doit JAMAIS
                // s'afficher tel quel dans le chat - journalise puis ignore.
                FxCloudService.Log($"Agent IA : ligne SSE non JSON ignoree : {payload}");
            }
        }

        /// <summary>
        /// Libellé lisible d'un tool pour le bandeau de statut ("🔍 Lecture du
        /// workspace…" au lieu du nom technique brut).
        /// </summary>
        internal static string DescribeTool(string toolName)
        {
            switch (toolName)
            {
                case "get_workspace_snapshot": return "Lecture du workspace";
                case "get_strategy_details": return "Analyse de la stratégie";
                case "search_trades": return "Recherche dans le journal";
                case "get_study_catalog": return "Parcours des études";
                case "create_strategy": return "Création de la stratégie";
                case "delete_strategy": return "Suppression de la stratégie";
                case "add_journal_trade": return "Ajout du trade au journal";
                case "add_habit": return "Ajout de l'habitude";
                case "mark_habit": return "Mise à jour de l'habitude";
                default: return toolName;
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
                    if (!call.TryGetProperty("name", out var nameElement) || nameElement.ValueKind != JsonValueKind.String
                        || string.IsNullOrWhiteSpace(nameElement.GetString()))
                    {
                        // functionCall sans nom exploitable : ignore (jamais d'affichage brut).
                        return false;
                    }
                    toolCall = new AiToolCall
                    {
                        Id = string.IsNullOrEmpty(id) ? Guid.NewGuid().ToString() : id,
                        Name = nameElement.GetString(),
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