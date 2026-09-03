using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using backtest.Models;

namespace backtest.Services
{
    public sealed class AgentWorkspaceService
    {
        private readonly FxCloudService _cloudService;

        public AgentWorkspaceService(FxCloudService cloudService)
        {
            _cloudService = cloudService ?? throw new ArgumentNullException(nameof(cloudService));
        }

        public IReadOnlyList<AiToolDefinition> GetToolDefinitions()
        {
            return new List<AiToolDefinition>
            {
                new AiToolDefinition("get_workspace_snapshot", "Lire le profil, les habitudes, les stratégies et les métriques globales de l'utilisateur.", false),
                new AiToolDefinition("get_strategy_details", "Lire les trades, statistiques et configurations d'une stratégie précise.", false,
                    new AiToolParameter("strategy_name", "string", "Nom exact de la stratégie à inspecter.")),
                new AiToolDefinition("search_trades", "Rechercher dans le journal et le backtest par paire, résultat ou stratégie.", false,
                    new AiToolParameter("query", "string", "Texte à rechercher : paire (ex: EURUSD), nom de stratégie ou résultat (WIN/TP/SL). Chaîne vide pour tout lister.", false)),
                new AiToolDefinition("get_study_catalog", "Lister les études et notes disponibles dans l'espace local.", false),
                new AiToolDefinition("create_strategy", "Créer une stratégie dans DataEdge.", true,
                    new AiToolParameter("name", "string", "Nom unique de la nouvelle stratégie."),
                    new AiToolParameter("description", "string", "Description de la stratégie.", false)),
                new AiToolDefinition("delete_strategy", "Supprimer une stratégie et ses données locales (action définitive).", true,
                    new AiToolParameter("name", "string", "Nom exact de la stratégie à supprimer.")),
                new AiToolDefinition("add_journal_trade", "Ajouter un trade au journal d'une stratégie.", true,
                    new AiToolParameter("strategy_name", "string", "Nom exact de la stratégie cible."),
                    new AiToolParameter("pair", "string", "Paire tradée, ex: EURUSD, XAUUSD."),
                    new AiToolParameter("result", "string", "Résultat du trade : TP, SL, TR, BE ou PARTIAL."),
                    new AiToolParameter("order_type", "string", "Type d'ordre : BUY ou SELL."),
                    new AiToolParameter("entry", "string", "Date/heure d'entrée, ex: 2026-09-03 14:30.", false),
                    new AiToolParameter("exit", "string", "Date/heure de sortie, ex: 2026-09-03 16:45.", false),
                    new AiToolParameter("rr", "number", "Ratio risque/rendement, ex: 2.5.", false),
                    new AiToolParameter("profit", "number", "Profit en devise du compte (négatif si perte).", false),
                    new AiToolParameter("description", "string", "Notes sur le trade (setup, contexte...).", false)),
                new AiToolDefinition("add_habit", "Ajouter une habitude de trading à suivre.", true,
                    new AiToolParameter("name", "string", "Intitulé de l'habitude.")),
                new AiToolDefinition("mark_habit", "Marquer une habitude comme faite ou non faite aujourd'hui.", true,
                    new AiToolParameter("name", "string", "Intitulé exact de l'habitude."),
                    new AiToolParameter("is_checked", "boolean", "true si l'habitude est faite aujourd'hui, sinon false."))
            };
        }

        /// <summary>
        /// Indique si un tool nécessite une confirmation utilisateur avant exécution.
        /// Par sécurité, un tool inconnu est considéré comme nécessitant une confirmation.
        /// </summary>
        public bool RequiresConfirmation(string toolName)
        {
            var definition = GetToolDefinitions().FirstOrDefault(tool => string.Equals(tool.Name, toolName, StringComparison.Ordinal));
            return definition?.RequiresConfirmation ?? true;
        }

        public async Task<string> BuildContextAsync()
        {
            var profile = await _cloudService.GetProfileAsync();
            var strategies = utils.getStrategies();
            var trades = strategies.SelectMany(strategy => strategy.GetJournal().Select(trade => new { strategy = strategy.Nom, trade }))
                .ToList();
            var habits = new HabitsManager();
            var context = new
            {
                profile = profile == null ? null : new { name = profile.FullName, email = profile.Email, bio = profile.Bio },
                habits = habits.Habits.Select(h => h.Name).ToList(),
                habits_today = habits.DailyHabitStates.Select(h => new { name = h.HabitName, completed = h.IsChecked }).ToList(),
                strategies = strategies.Select(strategy => new
                {
                    name = strategy.Nom,
                    description = strategy.description,
                    journal_trades = strategy.GetJournal().Count,
                    backtest_trades = strategy.GetTrades().Count,
                    statistics = strategy.GetStatistics(),
                    best_configurations = strategy.RetrieveStats()?.BestConfigs
                }).ToList(),
                recent_trades = trades.OrderByDescending(item => item.trade.DateEntree).Take(100).Select(ToTradeSummary).ToList(),
                studies = GetStudyCatalog()
            };
            return JsonSerializer.Serialize(context);
        }

        public async Task<AiToolResult> ExecuteAsync(AiToolCall call, Func<AiToolCall, Task<bool>> confirmMutation)
        {
            if (call == null || string.IsNullOrWhiteSpace(call.Name))
                return AiToolResult.Error("Appel d'outil invalide.");

            var definition = GetToolDefinitions().FirstOrDefault(tool => tool.Name == call.Name);
            if (definition == null)
                return AiToolResult.Error("Cet outil n'est pas disponible dans DataEdge.");

            if (definition.RequiresConfirmation && (confirmMutation == null || !await confirmMutation(call)))
                return AiToolResult.Error("Action refusée ou annulée par l'utilisateur.");

            try
            {
                switch (call.Name)
                {
                    case "get_workspace_snapshot":
                        return AiToolResult.Success(await BuildContextAsync());
                    case "get_strategy_details":
                        return GetStrategyDetails(call.Arguments);
                    case "search_trades":
                        return SearchTrades(call.Arguments);
                    case "get_study_catalog":
                        return AiToolResult.Success(JsonSerializer.Serialize(GetStudyCatalog()));
                    case "create_strategy":
                        return CreateStrategy(call.Arguments);
                    case "delete_strategy":
                        return DeleteStrategy(call.Arguments);
                    case "add_journal_trade":
                        return AddJournalTrade(call.Arguments);
                    case "add_habit":
                        return AddHabit(call.Arguments);
                    case "mark_habit":
                        return MarkHabit(call.Arguments);
                    default:
                        return AiToolResult.Error("Outil non implémenté.");
                }
            }
            catch (Exception ex)
            {
                FxCloudService.Log($"Erreur outil agent {call.Name}: {ex}");
                return AiToolResult.Error(ex.Message);
            }
        }

        private AiToolResult GetStrategyDetails(JsonElement arguments)
        {
            string name = GetString(arguments, "strategy_name");
            var strategy = utils.getStrategies().FirstOrDefault(item => string.Equals(item.Nom, name, StringComparison.OrdinalIgnoreCase));
            if (strategy == null) return AiToolResult.Error("Stratégie introuvable.");
            var result = new
            {
                name = strategy.Nom,
                description = strategy.description,
                structure = strategy.GetStructure(),
                statistics = strategy.GetStatistics(),
                advanced_statistics = strategy.RetrieveStats(),
                trades = strategy.GetTrades().Select(ToTradeSummary).ToList(),
                journal = strategy.GetJournal().Select(ToTradeSummary).ToList()
            };
            return AiToolResult.Success(JsonSerializer.Serialize(result));
        }

        private AiToolResult SearchTrades(JsonElement arguments)
        {
            string query = GetString(arguments, "query");
            var results = utils.getStrategies().SelectMany(strategy => strategy.GetTrades().Concat(strategy.GetJournal())
                .Where(trade => string.IsNullOrWhiteSpace(query)
                    || (trade.Paire ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || (trade.strategie ?? strategy.Nom).IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0
                    || trade.Result.ToString().IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0)
                .Select(ToTradeSummary)).Take(200).ToList();
            return AiToolResult.Success(JsonSerializer.Serialize(results));
        }

        private AiToolResult AddHabit(JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            if (string.IsNullOrWhiteSpace(name)) return AiToolResult.Error("Le nom de l'habitude est obligatoire.");
            var manager = new HabitsManager();
            manager.AddHabit(name);
            return AiToolResult.Success($"Habitude ajoutée: {name}");
        }

        private AiToolResult CreateStrategy(JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            if (string.IsNullOrWhiteSpace(name) || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
                return AiToolResult.Error("Nom de stratégie invalide.");
            if (utils.getStrategies().Any(item => string.Equals(item.Nom, name, StringComparison.OrdinalIgnoreCase)))
                return AiToolResult.Error("Cette stratégie existe déjà.");
            new Strategie(name.Trim(), GetString(arguments, "description"));
            return AiToolResult.Success($"Stratégie créée: {name.Trim()}");
        }

        private AiToolResult DeleteStrategy(JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            var strategy = utils.getStrategies().FirstOrDefault(item => string.Equals(item.Nom, name, StringComparison.OrdinalIgnoreCase));
            if (strategy == null) return AiToolResult.Error("Stratégie introuvable.");
            strategy.SupprimerStrategie();
            return AiToolResult.Success($"Stratégie supprimée: {strategy.Nom}");
        }

        private AiToolResult AddJournalTrade(JsonElement arguments)
        {
            string strategyName = GetString(arguments, "strategy_name");
            var strategy = utils.getStrategies().FirstOrDefault(item => string.Equals(item.Nom, strategyName, StringComparison.OrdinalIgnoreCase));
            if (strategy == null) return AiToolResult.Error("Stratégie introuvable.");
            if (!Enum.TryParse(GetString(arguments, "result"), true, out Resultat result)) return AiToolResult.Error("Résultat invalide. Valeurs attendues : TP, SL, TR, BE ou PARTIAL.");
            if (!Enum.TryParse(GetString(arguments, "order_type"), true, out TypeOrdre orderType)) return AiToolResult.Error("Type d'ordre invalide. Valeurs attendues : BUY ou SELL.");
            DateTime.TryParse(GetString(arguments, "entry"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var entry);
            DateTime.TryParse(GetString(arguments, "exit"), System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out var exit);
            float rr = (float)GetNumber(arguments, "rr");
            double profit = GetNumber(arguments, "profit");
            strategy.AddJournal(new Trade
            {
                Paire = GetString(arguments, "pair"), Result = result, TypeOrdre = orderType,
                DateEntree = entry, DateSortie = exit, RR = rr, Profit = profit,
                description = GetString(arguments, "description"), strategie = strategy.Nom
            });
            return AiToolResult.Success($"Trade ajouté au journal de {strategy.Nom}.");
        }

        private AiToolResult MarkHabit(JsonElement arguments)
        {
            string name = GetString(arguments, "name");
            bool isChecked = GetBool(arguments, "is_checked");
            var manager = new HabitsManager();
            manager.UpdateHabitState(name, isChecked);
            return AiToolResult.Success($"Habitude {(isChecked ? "validée" : "dévalidée")}: {name}");
        }

        private static object ToTradeSummary(dynamic item)
        {
            return new
            {
                strategy = item.strategy,
                id = item.trade.Id,
                pair = item.trade.Paire,
                result = item.trade.Result.ToString(),
                entry = item.trade.DateEntree,
                exit = item.trade.DateSortie,
                order_type = item.trade.TypeOrdre.ToString(),
                rr = item.trade.RR,
                profit = item.trade.Profit,
                description = item.trade.description,
                custom_fields = item.trade.ChampsPersonnalises
            };
        }

        private static object ToTradeSummary(Trade trade)
        {
            return new
            {
                id = trade.Id, pair = trade.Paire, result = trade.Result.ToString(), entry = trade.DateEntree,
                exit = trade.DateSortie, order_type = trade.TypeOrdre.ToString(), rr = trade.RR,
                profit = trade.Profit, description = trade.description, custom_fields = trade.ChampsPersonnalises
            };
        }

        private static List<string> GetStudyCatalog()
        {
            var studies = new List<string>();
            AddFiles(studies, "etudes", "*.etude");
            AddFiles(studies, "Notes", "*.etude");
            return studies;
        }

        private static void AddFiles(List<string> target, string folder, string pattern)
        {
            if (Directory.Exists(folder)) target.AddRange(Directory.GetFiles(folder, pattern, SearchOption.AllDirectories));
        }

        private static string GetString(JsonElement arguments, string property)
        {
            if (!arguments.TryGetProperty(property, out var value)) return string.Empty;
            switch (value.ValueKind)
            {
                case JsonValueKind.String: return value.GetString() ?? string.Empty;
                case JsonValueKind.Number: return value.GetRawText();
                case JsonValueKind.True: return "true";
                case JsonValueKind.False: return "false";
                default: return string.Empty;
            }
        }

        private static bool GetBool(JsonElement arguments, string property, bool fallback = false)
        {
            if (!arguments.TryGetProperty(property, out var value)) return fallback;
            switch (value.ValueKind)
            {
                case JsonValueKind.True: return true;
                case JsonValueKind.False: return false;
                case JsonValueKind.Number: return value.GetDouble() != 0;
                case JsonValueKind.String:
                    string text = value.GetString();
                    if (bool.TryParse(text, out bool parsed)) return parsed;
                    if (text == "1" || string.Equals(text, "oui", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "yes", StringComparison.OrdinalIgnoreCase)) return true;
                    if (text == "0" || string.Equals(text, "non", StringComparison.OrdinalIgnoreCase) || string.Equals(text, "no", StringComparison.OrdinalIgnoreCase)) return false;
                    return fallback;
                default: return fallback;
            }
        }

        private static double GetNumber(JsonElement arguments, string property, double fallback = 0)
        {
            if (!arguments.TryGetProperty(property, out var value)) return fallback;
            switch (value.ValueKind)
            {
                case JsonValueKind.Number: return value.GetDouble();
                case JsonValueKind.True: return 1;
                case JsonValueKind.False: return 0;
                case JsonValueKind.String:
                    string text = (value.GetString() ?? string.Empty).Trim().Replace(',', '.');
                    return double.TryParse(text, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out double parsed) ? parsed : fallback;
                default: return fallback;
            }
        }
    }

    /// <summary>
    /// Paramètre typé déclaré au modèle pour un tool (mappé en functionDeclarations Gemini).
    /// </summary>
    public sealed class AiToolParameter
    {
        public AiToolParameter(string name, string type, string description, bool required = true)
        {
            Name = name; Type = type; Description = description; Required = required;
        }
        [JsonPropertyName("name")] public string Name { get; }
        [JsonPropertyName("type")] public string Type { get; } // "string", "number", "integer", "boolean"
        [JsonPropertyName("description")] public string Description { get; }
        [JsonPropertyName("required")] public bool Required { get; }
    }

    public sealed class AiToolDefinition
    {
        public AiToolDefinition(string name, string description, bool requiresConfirmation, params AiToolParameter[] parameters)
        {
            Name = name; Description = description; RequiresConfirmation = requiresConfirmation; Parameters = parameters;
        }
        [JsonPropertyName("name")] public string Name { get; }
        [JsonPropertyName("description")] public string Description { get; }
        [JsonPropertyName("requires_confirmation")] public bool RequiresConfirmation { get; }
        [JsonPropertyName("parameters")] public IReadOnlyList<AiToolParameter> Parameters { get; }
    }

    public sealed class AiToolCall
    {
        public string Id { get; set; }
        public string Name { get; set; }
        public JsonElement Arguments { get; set; }
    }

    public sealed class AiToolResult
    {
        public bool IsSuccess { get; private set; }
        public string Content { get; private set; }
        public static AiToolResult Success(string content) => new AiToolResult { IsSuccess = true, Content = content };
        public static AiToolResult Error(string content) => new AiToolResult { IsSuccess = false, Content = content };
    }
}
