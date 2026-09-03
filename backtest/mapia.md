# 🤖 DataEdge - Carte de la Partie IA (mapia.md)

> **Carte dédiée à l'agent IA** : front (client WPF) **et** back (serveur Symfony dans `fxglobal/`).
> Fichier complémentaire de `mapprojet.md` — il se concentre uniquement sur la brique « Agent IA » (chat + tools / function calling).

## 📌 Vue d'Ensemble

L'agent IA de DataEdge est un **copilote de trading intégré** au logiciel WPF. Il permet à l'utilisateur de :

- **Discuter** de ses performances, stratégies, trades et habitudes en langage naturel.
- **Agir sur son espace local** via des *function calls* (tools) : lire le workspace, créer/supprimer des stratégies, ajouter des trades au journal, gérer les habitudes.

Le modèle LLM utilisé est **Google Gemini** (API `generativelanguage.googleapis.com`), appelé **uniquement par le serveur** (la clé `GEMINI_API_KEY` ne quitte jamais le backend).

```
┌───────────────────────────────┐        ┌─────────────────────────────────────┐        ┌──────────────────────────┐
│  CLIENT WPF (backtest/)       │  JWT  │  BACKEND Symfony (fxglobal/)        │ HTTPS │  GOOGLE GEMINI API       │
│                               │ Bearer │                                     │        │                          │
│  FxAiChatControl (UI)         │ ─────▶ │  AIChatController::chat (POST)      │        │  /v1beta/models/          │
│  FxAiAgentService (client)    │  SSE   │  GeminiService::generateStreamResp.  │ ───▶  │  gemini-*.flash:         │
│  AgentWorkspaceService        │ ◀───── │  (cURL CURLOPT_WRITEFUNCTION)        │ flux   │  streamGenerateContent   │
│  (exécution des tools)        │        │  AIChatMessage (historique en BDD)      │        │                          │
└───────────────────────────────┘        └─────────────────────────────────────┘        └──────────────────────────┘
```

## 🗂️ Périmètre du mapping

| Couche | Emplacement | Rôle |
|---|---|---|
| **Front - UI** | `Views/FxAiChatControl.xaml` + `.xaml.cs` | Fenêtre de chat de l'agent (bulles, quick prompts, streaming) |
| **Front - logique** | `services/FxAiAgentService.cs` | Client HTTP du chat : envoi, lecture du flux SSE, boucle agent |
| **Front - tools** | `services/AgentWorkspaceService.cs` | Définitions des tools + exécution locale + contexte utilisateur |
| **Front - modèles** | `Models/ChatMessage.cs`, `Models/AiAgentError.cs` | Objets de données du chat et erreurs agent |
| **Back - endpoint** | `fxglobal/src/Controller/AIChatController.php` | Route `POST /api/ai/chat`, streaming SSE, persistance BDD |
| **Back - LLM** | `fxglobal/src/Service/GeminiService.php` | Appel API Gemini, déclaration des fonctions, conversion `functionCall` |
| **Back - données** | `fxglobal/src/Entity/AIChatMessage.php` + `.../Repository/AIChatMessageRepository.php` | Historique de conversation en base |
| **Back - migration** | `fxglobal/migrations/Version20260714022623.php` | Table `aichat_message` |

> ℹ️ **Note importante** : `mapprojet.md` référencé auparavant d'anciens chemins `aiback/AIChatController (1).php` et `aiback/GeminiService (1).php` → **le backend réel est dans `fxglobal/src/`** (le dossier `aiback/` n'existe plus dans l'arborescence actuelle). Les fichiers `mapprojet.md` ont été mis à jour en conséquence.

## 🔄 Flux de communication (chat classique)

1. L'utilisateur clique sur le bouton **🤖 AGENT IA** (`MainWindow.xaml` ligne 114) → `ShowAiAgent()` instancie `FxAiChatControl(_cloudService)` dans `MainViewContainer`.
2. L'utilisateur saisit un message → `SendMessage()` :
   - ajoute le message utilisateur dans la liste ;
   - affiche l'indicateur « L'agent réfléchit... » ;
   - construit le **contexte local** via `AgentWorkspaceService.BuildContextAsync()` (profil, habitudes, stratégies, trades, études) ;
   - lance `FxAiAgentService.SendMessageToAiStreamAsync(...)` sur un thread de fond (`Task.Run`).
3. Le client POST `api/ai/chat` avec un **JWT Bearer** (via `FxCloudService.SecureRequestAsync`) :

   ```json
   {
     "message": "le prompt",
     "context": "{...contexte workspace JSON...}",
     "app_id": "FX_DATAEDGE",
     "session_id": "guid",
     "tools": [ { "name": "...", "description": "...", "requires_confirmation": true,
                  "parameters": [ { "name": "...", "type": "string|number|boolean", "description": "...", "required": true } ] } ],
     "tool_results": [ { "name": "...", "id": "...", "arguments": {...}, "content": "résultat", "is_error": false } ]
   }
   ```

   > `tool_results` (tableau) n'est présent que sur les **tours outil** de la boucle agent (tour 1 : absent). L'ancien format `tool_result` (objet unique) reste accepté par le serveur pour compatibilité.

4. Le backend (`AIChatController::chat`) :
   - vérifie `$this->getUser()` (firewall JWT) → `401` sinon ;
   - **tour normal** : sauvegarde le message **user** en base (`AIChatMessage` role=`user`) ;
   - **tour outil** : sauvegarde les résultats en base sous forme d'un `AIChatMessage` role=`function` contenant `{"functionResponses": [{name, response: {name, content, is_error}}]}` ;
   - renvoie un `StreamedResponse` (SSE : `Content-Type: text/event-stream`, `Connection: keep-alive`, `X-Accel-Buffering: no`) ;
   - dans le callback : `GeminiService::generateStreamResponse(...)` avec un callback qui re-émet chaque morceau au client sous forme `data: {json}\n\n` (le texte et les tool_calls sont accumulés côté serveur) ;
   - après le stream : sauvegarde du tour **model** — texte final simple, **ou** JSON structuré `{"text": "...", "functionCalls": [{name, args}]}` si le tour contenait des appels d'outils.
5. `GeminiService` :
   - recharge les **40** derniers messages (`findChatHistory`) — y compris les tours `model` (functionCalls) et `function` (functionResponses) ;
   - reconstruit les `contents` Gemini dans l'ordre **exact exigé par l'API** : `[model: functionCall]` → `[user: functionResponse]` → suite ; le prompt courant est dédoublonné puis ré-ajouté une seule fois, augmenté du `context` client ;
   - appelle `POST https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:streamGenerateContent?alt=sse&key=...` en cURL ; `alt=sse` garantit des événements SSE autonomes (un JSON complet par ligne `data:`), bufferisés ligne par ligne dans `CURLOPT_WRITEFUNCTION`.
6. Chaque événement reçu de Google est analysé (`processSseLine`) :
   - `part.text` → émis au client : `{"text": "..."}` ;
   - `part.functionCall` → émis au client : `{"tool_call": {id, name, arguments}}` (plusieurs appels parallèles possibles) ;
   - erreur API/HTTP/cURL → émis : `{"error": "..."}` (le client lève `AiAgentException`).
7. Le client (`FxAiAgentService`) lit le flux **en entier** :
   - lignes `text` → affichées en direct dans la bulle (`Dispatcher.Invoke`) ;
   - lignes `tool_call` → **collectées** ; à la fin du flux, chaque outil est **exécuté localement** puis un **nouveau tour serveur** est lancé avec `tool_results`.

## 🔁 Boucle agent (function calling)

`SendMessageToAiStreamAsync` exécute une boucle de **8 itérations maximum** :

- **Tour 1** : envoie `message` + `context` + `tools` → le serveur renvoie du texte **et/ou** un ou plusieurs `tool_call` (appels parallèles inclus, tous collectés).
- Si aucun `tool_call` → fin de la boucle (réponse textuelle affichée).
- Sinon → chaque `toolHandler(toolCall)` exécute le tool localement (`AgentWorkspaceService.ExecuteAsync`, avec confirmation pour les mutations ; une exception d'outil est interceptée par `ExecuteSafelyAsync` et renvoyée comme `is_error: true`), puis un **tour N+1** est envoyé avec :

  ```json
  {
    "message": "",
    "context": "",
    "tools": [ ... ],
    "tool_results": [ { "name": "...", "id": "...", "arguments": {...}, "content": "résultat", "is_error": false } ]
  }
  ```

- Le backend détecte `tool_results` (`$isToolTurn`) : il **persiste** un tour role=`function` (`{"functionResponses": [...]}`) puis `GeminiService` reconstruit depuis l'historique BDD la séquence exigée par Gemini : le tour **model** contenant les `functionCall` parts (persisté à la fin du stream précédent), suivi du tour **user** contenant les `functionResponse` parts.
- Tant que le modèle renvoie encore des `functionCall`, la boucle continue (plafond 8 tours côté client).

```
Client                         Serveur                            Gemini
  │  POST /api/ai/chat          │                                 │
  │  {tool_results: [...]}      │  tour model (functionCall)      │
  │ ──────────────────────────▶ │  + tour user (functionResponse) │
  │                             │ ──────────────────────────────▶ │
  │                             │ ◀────────────────────────────── │  nouveau functionCall OU texte final
  │ ◀── data: {tool_call} ───── │                                 │
  │  exécution locale du tool   │                                 │
  │  (AgentWorkspaceService)    │                                 │
```

## 🖥️ FRONT - Client WPF (détail des fichiers)

### `Views/FxAiChatControl.xaml` (+ `.xaml.cs`) — Interface du chat

| Élément | Description |
|---|---|
| **En-tête** | Avatar néon cyan, titre « DATAEDGE AI AGENT », badge « OUTILS ACTIFS » |
| **Liste des messages** | `ItemsControl` lié à `ObservableCollection<ChatMessage>`, bulles différenciées (User / IA), boutons **📋 Copier** et **🔄 Relancer** par message |
| **Indicateur** | Spinner rotatif + « L'agent réfléchit... » (`LoadingIndicator`), masqué dès le 1er token |
| **Quick prompts** | 3 boutons : « Bilan », « Mes règles », « Analyser » (envoient un prompt pré-rempli) |
| **Saisie** | `TextBox` multiligne, envoi par bouton « ➔ » ou touche `Entrée` |

#### Code-behind — fonctions principales

| Fonction | Rôle |
|---|---|
| `FxAiChatControl(FxCloudService)` | Ctor : instancie `FxAiAgentService` + `AgentWorkspaceService`, message d'accueil |
| `SendMessage(messageText)` | Pipeline complet : ajout msg user → spinner → bulle IA vide → `BuildContextAsync()` → `SendMessageToAiStreamAsync()` (dans `Task.Run`) → mise à jour UI via `Dispatcher.Invoke` |
| `HandleToolCallAsync(AiToolCall)` | Demande une `MessageBox` Yes/No pour **tout** tool dont la définition déclare `requires_confirmation: true` (via `AgentWorkspaceService.RequiresConfirmation`) ; si refus → `AiToolResult.Error("Action refusée ou annulée par l'utilisateur.")` transmis au modèle en `is_error: true` |
| `ScrollToBottom()` | Auto-défilement du chat |
| `BtnCopy_Click` | Copie le texte de la bulle dans le presse-papiers (feedback « ✔️ Copié ! » temporaire) |
| `BtnResend_Click` | Relance le prompt correspondant |
| `TxtInput_KeyDown` | Envoi sur `Enter` |
| `QuickPrompt_Click` | Remplit et envoie un prompt prédéfini |

### `services/FxAiAgentService.cs` — Client HTTP de l'agent

| Fonction | Rôle |
|---|---|
| `SendMessageToAiStreamAsync(prompt, onChunkReceived, userContext, tools, toolHandler)` | **Point d'entrée.** Vérifications réseau/compte → boucle agent (max 8 tours) → exceptions `AiAgentException` |
| `SendTurnAsync(...)` | Envoie un tour complet (payload JSON : `message`, `context`, `app_id`, `session_id`, `tools`, `tool_results`) en POST `api/ai/chat` avec rejeu JWT (`SecureRequestAsync`), lit le flux SSE **en entier** et retourne la liste des `tool_call` collectés |
| `TryReadToolCall(line)` | Détecte une ligne contenant `tool_call` → construit `AiToolCall { Id, Name, Arguments }` (id généré si absent) |
| `NormalizeArguments(call)` | Normalise les arguments du functionCall vers un objet JSON (sinon `{}` vide) |
| `ExecuteSafelyAsync(toolHandler, call)` | Exécute un tool en interceptant toute exception → l'erreur est renvoyée au modèle (`is_error: true`) au lieu de casser la boucle |
| `EmitChunk(line, onChunkReceived)` | Parse une ligne SSE : si `error` → lève `AiAgentException` ; si `text` → callback ; sinon émet la ligne brute |
| `AiToolResultPayload` (classe) | Payload `tool_results` : `name`, `id`, `arguments`, `content`, `is_error` |

> 💡 **session_id** : généré par tour d'appel (`Guid.NewGuid()`), conservé sur toute la durée de la boucle agent pour un même prompt utilisateur.

### `services/AgentWorkspaceService.cs` — Contexte + exécution des tools

| Fonction | Rôle |
|---|---|
| `GetToolDefinitions()` | Retourne la liste des 9 tools déclarés au modèle, avec **paramètres typés** (`AiToolParameter` : `name`, `type` string/number/boolean, `description`, `required`) |
| `RequiresConfirmation(toolName)` | Indique si un tool nécessite une confirmation utilisateur (tool inconnu → `true` par sécurité) |
| `BuildContextAsync()` | Sérialise en JSON le « workspace » : profil cloud (`GetProfileAsync`), habitudes (`HabitsManager`), stratégies (`utils.getStrategies()`), stats, 100 derniers trades, catalogue d'études (`*.etude` dans `etudes/` et `Notes/`) |
| `ExecuteAsync(call, confirmMutation)` | Dispatch par `switch` vers l'implémentation de chaque outil ; gère la **confirmation** pour les outils marqués `requiresConfirmation` ; log + `AiToolResult.Error` sur exception |
| Coercition des arguments | `GetString` / `GetBool` / `GetNumber` tolèrent tous les ValueKind JSON (nombre, booléen, chaîne) — évite les exceptions quand Gemini envoie `rr`/`profit` en number ou `is_checked` en boolean |
| Outils lecture | `GetStrategyDetails`, `SearchTrades`, `GetStudyCatalog` |
| Outils mutation | `CreateStrategy`, `DeleteStrategy`, `AddJournalTrade`, `AddHabit`, `MarkHabit` |

> ℹ️ Le contexte local n'est **jamais** envoyé tel quel à Gemini par le client : il est inclus dans le champ `context` du POST, puis injecté par le serveur dans le prompt envoyé à l'API.

### Modèles dédiés

| Fichier | Classe(s) | Rôle |
|---|---|---|
| `Models/ChatMessage.cs` | `ChatMessage` | Bulle de chat avec `INotifyPropertyChanged`, `Alignement`, `BubbleColor`, `BorderColor` (UI temps réel) |
| `Models/AiAgentError.cs` | `AiAgentError` | Structuration des erreurs agent (message + détails + exception), `ToDisplayText()` |

## 🖧 BACK - Serveur Symfony (fxglobal/)

### `src/Controller/AIChatController.php` — Endpoint IA

| Élément | Détail |
|---|---|
| **Route** | `#[Route('/api/ai')]` sur la classe + `#[Route('/chat', name: 'api_ai_chat', methods: ['POST'])]` sur `chat()` |
| **Sécurité** | `$this->getUser()` (firewall `api` : JWT, `IS_AUTHENTICATED_FULLY`) → `401` « Not authenticated » sinon |
| **Entrée** | Body JSON : `message`, `context`, `tools`, `tool_results` (tableau ; legacy `tool_result` accepté) |
| **Persistance tour normal** | Sauvegarde d'un `AIChatMessage` (role=`user`) avant le stream |
| **Persistance tour outil** | Sauvegarde d'un `AIChatMessage` (role=`function`) contenant `{"functionResponses": [{name, response}]}` avant le stream |
| **Sortie** | `StreamedResponse` (SSE) : headers `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`, `X-Accel-Buffering: no` |
| **Stream** | Appelle `GeminiService::generateStreamResponse()` avec callback → chaque chunk émis en `data: <json>\n\n` ; le texte et les tool_calls sont accumulés ; `try/catch` → chunk `{"error": ...}` |
| **Persistance modèle** | Après le stream : si le tour contenait des functionCalls → `AIChatMessage` (role=`model`) avec JSON `{"text": "...", "functionCalls": [{name, args}]}` ; sinon texte final simple. Rien n'est persisté si le tour est vide (erreur) |

### `src/Service/GeminiService.php` — Moteur Gemini

| Fonction | Rôle |
|---|---|
| `__construct(AIChatMessageRepository, string $geminiApiKey)` | Injection ; `$geminiApiKey` bindée depuis `config/services.yaml` (`%env(GEMINI_API_KEY)%`) |
| `preparePayload(User, userPrompt, context, tools)` | Construit le payload pour l'API Gemini : |
| | • **historique** : `findChatHistory(user, 40)` — inclut les tours `user`, `model` (texte ou functionCalls) et `function` (functionResponses) |
| | • **prompt user** : dédoublonné de l'historique puis ré-ajouté une seule fois + `\n\nDONNEES ESPACE UTILISATEUR:\n` + `context` |
| | • **tours outils** : `buildContents` / `decodeModelTurn` / `buildFunctionResponseParts` reconstruisent l'ordre exigé : `[model: functionCall]` → `[user: functionResponse]` |
| | • **systemInstruction** : prompt système « DataEdge AI Assistant » (identité, expertise, directives, **section 5 : utilisation des outils**) |
| | • **generationConfig** : `temperature: 0.7`, `maxOutputTokens: 8192` |
| | • **tools.functionDeclarations** : conversion des déclarations client — paramètres typés `{name, type, description, required}` (format chaîne simple legacy accepté), `properties` en objet JSON, `required` omis si vide |
| `generateStreamResponse(...)` | Appelle `streamGenerateContent?alt=sse` du modèle **`gemini-3.5-flash`** en cURL ; `CURLOPT_WRITEFUNCTION` bufferise et traite les lignes SSE complètes ; `processSseLine` parse chaque événement et émet `text`, `tool_call` ou `error` ; après `curl_exec` : erreur cURL / code HTTP ≥ 400 → chunk `error` |

> ℹ️ **Boucle agent** : c'est côté **serveur** que la conversion `functionCall` → `tool_call` se fait (dans `GeminiService`), puis c'est le **client** qui exécute l'outil et renvoie `tool_results`. Le serveur persiste **tous les tours** (model functionCalls + function responses), ce qui permet de rejouer la boucle à chaque requête sans état en mémoire.

### `src/Entity/AIChatMessage.php` — Entité de l'historique

| Colonne | Type | Description |
|---|---|---|
| `id` | INT PK auto | Identifiant |
| `user` | FK → `user(id)` ON DELETE CASCADE | Propriétaire du message |
| `role` | VARCHAR(20) | `user` (message utilisateur), `model` (texte final ou JSON `{"text","functionCalls"}`) ou `function` (JSON `{"functionResponses":[...]}`) |
| `content` | LONGTEXT | Texte du message, ou JSON structuré pour les tours outils |
| `createdAt` | DATETIME (auto `new \DateTime()`) | Horodatage |

### `src/Repository/AIChatMessageRepository.php`

| Fonction | Rôle |
|---|---|
| `findChatHistory(User $user, int $limit = 30)` | Récupère les messages d'un utilisateur triés DESC puis **inversés** (ordre chronologique pour Gemini) ; limit 30 par défaut (le service appelle avec `40` pour couvrir les tours outils) |

### Migration & config

| Fichier | Rôle |
|---|---|
| `migrations/Version20260714022623.php` | Crée la table `aichat_message` + FK `user_id` |
| `config/services.yaml` | Paramètre `gemini_api_key: '%env(GEMINI_API_KEY)%'` → bindé sur `$geminiApiKey` |
| `.env` | Contient `GEMINI_API_KEY` (clé remise via la variable d'environnement) |
| `config/packages/security.yaml` | Firewall `api` (`jwt`) sur `^/api` ; `api/ai/chat` est donc protégé par JWT |

## 🔧 Inventaire des Tools de l'agent (function calling)

Définis dans `AgentWorkspaceService.GetToolDefinitions()` et transmis au serveur (→ `functionDeclarations` Gemini). Les paramètres sont **typés** (`AiToolParameter` : `name`, `type` = `string`/`number`/`boolean`, `description`, `required`) et convertis par `GeminiService::buildFunctionDeclarations` en schéma JSON Gemini (`STRING`/`NUMBER`/`BOOLEAN` + tableau `required`).

| Tool | Type | Paramètres (type) | Requiert confirmation * | Implémentation C# |
|---|---|---|---|---|
| `get_workspace_snapshot` | Lecture | — | non | `BuildContextAsync()` |
| `get_strategy_details` | Lecture | `strategy_name` (string) | non | `GetStrategyDetails(arguments)` |
| `search_trades` | Lecture | `query` (string, optionnel) | non | `SearchTrades(arguments)` |
| `get_study_catalog` | Lecture | — | non | `GetStudyCatalog()` |
| `create_strategy` | Mutation | `name` (string), `description` (string, optionnel) | **oui** | `CreateStrategy(arguments)` |
| `delete_strategy` | Mutation | `name` (string) | **oui** | `DeleteStrategy(arguments)` |
| `add_journal_trade` | Mutation | `strategy_name` (string), `pair` (string), `result` (string : TP/SL/TR/BE/PARTIAL), `order_type` (string : BUY/SELL), `entry` (string date), `exit` (string date), `rr` (number), `profit` (number), `description` (string) | **oui** | `AddJournalTrade(arguments)` |
| `add_habit` | Mutation | `name` (string) | **oui** | `AddHabit(arguments)` |
| `mark_habit` | Mutation | `name` (string), `is_checked` (boolean) | **oui** | `MarkHabit(arguments)` |

### Règles de sécurité actuelles (importantes à connaître)

Dans `FxAiChatControl.HandleToolCallAsync` :

- **Tous les tools marqués `requires_confirmation: true`** (soit `create_strategy`, `delete_strategy`, `add_journal_trade`, `add_habit`, `mark_habit`) → une `MessageBox` « Autoriser cette modification ? » est affichée avec le nom du tool et ses arguments ; l'exécution n'a lieu que si l'utilisateur répond **Yes**.
- **Refus** → `AiToolResult.Error("Action refusée ou annulée par l'utilisateur.")` est renvoyé au modèle en `is_error: true` : l'agent est informé et peut reformuler au lieu de réessayer en boucle.
- **Outils inconnus** → `RequiresConfirmation()` retourne `true` par sécurité (confirmation demandée).
- **Exceptions d'exécution** → interceptées par `ExecuteSafelyAsync` côté `FxAiAgentService` : l'outil plante sans casser la boucle, le message d'erreur est transmis au modèle qui peut se corriger.

## 🔌 Intégration dans le logiciel (points d'entrée)

| Élément | Description |
|---|---|
| `MainWindow.xaml` ligne 114 | Bouton **🤖 AGENT IA** (tooltip), icône robot, style `CircleNavButton` |
| `MainWindow.xaml.cs` `ButtonAiAgent` → `ShowAiAgent()` | Instancie `new Views.FxAiChatControl(_cloudService)` et le place dans `MainViewContainer` (animation de fondu) |
| `FxCloudService` | Fournit HTTP client (`BaseAddress` = serveur, `GetHttpClient()` + `SecureRequestAsync` avec refresh JWT) ; `AppId` = `FX_DATAEDGE` |
| Authentification | Le client envoie `Authorization: Bearer <token>` via `SetAuthHeader()` ; le backend exige le firewall JWT (`IS_AUTHENTICATED_FULLY`) |

## ❗ Diagnostic : pourquoi les function calls peuvent ne pas fonctionner

Points relevés dans le code actuel (à vérifier dans l'ordre) :

1. **Nom du modèle Gemini** : l'URL utilise `gemini-3.5-flash` alors que le commentaire dans le code indique « Gemini 2.5 Flash ».
   - Si le modèle `gemini-3.5-flash` n'existe pas (ou est limité) sur le compte Google Cloud, l'appel API répond en erreur (`404`/`403`) et le flux est vide → aucun message ne s'affiche.
   - ➡️ Vérifier avec un modèle existant (ex : `gemini-2.5-flash`, `gemini-2.0-flash`) et confirmer qu'il gère les **function calling** (tools).

2. **Parsing du flux `streamGenerateContent` (côté serveur)** : `CURLOPT_WRITEFUNCTION` reçoit des **morceaux arbitraires** (pas des lignes JSON complètes). Or le code fait :
   ```php
   $cleanData = trim($data, ", \t\n\r\0\x0B[]");
   $decoded = json_decode($cleanData, true);
   ```
   - L'API `streamGenerateContent` renvoie du **SSE** (`data: [...]`) avec le tableau JSON qui s'agrandit progressivement (le JSON n'est complet/valide que sur le **dernier** événement).
   - Si un morceau contient un JSON partiel, des virgules ou plusieurs events collés → `json_decode` **échoue** → le texte et surtout le `functionCall` sont **silencieusement perdus** → l'agent ne « voit » jamais les tools.
   - ➡️ Cette fonction de callback est le **suspect n°1** pour les tools qui ne marchent pas aujourd'hui.

3. **Modèle de données `tool_result` côté serveur** : `GeminiService` lit `$toolResult['name']` et `$toolResult['content']`.
   - Le client (`FxAiAgentService`) envoie bien `{ name, id, content }` → compatible. ✅

4. **Confirmation des mutations** : ✅ **corrigé** — le handler client demande désormais une confirmation pour **tout** tool déclaré `requires_confirmation: true` (voir `AgentWorkspaceService.RequiresConfirmation`).

5. **Streaming côté client `ReadLineAsync`** : le serveur émet `data: {json}\n\n` ; le client lit ligne par ligne et ignore les lignes vides. Compatible avec le format SSE actuel. ✅ (si le format change, l'émetteur doit rester « ligne par ligne »)

6. **Session / historique** : ✅ **corrigé** — les tours de boucle sont désormais persistés : role=`model` avec `{"text","functionCalls"}` quand le modèle appelle des outils, role=`function` avec les `functionResponses` quand le client renvoie les résultats. `GeminiService` reconstruit à chaque requête la séquence `functionCall → functionResponse` exigée par l'API.

## 🧪 Checklist de debug rapide

- [x] ~~Parser ligne par ligne le flux~~ → `streamGenerateContent?alt=sse` + buffer de lignes complètes dans `CURLOPT_WRITEFUNCTION` (`GeminiService`).
- [x] ~~Ré-injecter le tour model `functionCall` avant le `functionResponse`~~ → persistance `{"functionCalls": [...]}` (role=`model`) + `{"functionResponses": [...]}` (role=`function`), reconstruction dans `buildContents`.
- [x] ~~Dédoublonner le prompt~~ (envoyé 2 fois : persisté en BDD + ré-ajouté au payload) → suppression de la dernière entrée user identique dans `buildContents`.
- [x] ~~Confirmer toutes les mutations~~ → confirmation branchée sur `requires_confirmation` dans `HandleToolCallAsync`.
- [ ] Tester un prompt qui force l'appel d'un tool (`get_workspace_snapshot` par exemple) et tracer la ligne `tool_call` reçue côté client (`TryReadToolCall`).
- [ ] Vérifier la boucle complète en conditions réelles : plusieurs tours d'outils successifs + appels parallèles (2 functionCall dans un même tour).
- [ ] En cas d'erreur 400 de Gemini, vérifier dans le chat le message `{"error": ...}` émis par le serveur (contient le code HTTP et le nom du modèle).

## 🧭 Glossaire technique

| Terme | Signification |
|---|---|
| **SSE** | Server-Sent Events : flux HTTP où le serveur pousse des morceaux (`data: ...`) en continu |
| **JWT** | Jeton JSON Web Token envoyé en `Authorization: Bearer` pour authentifier le client |
| **Tool / Function Call** | Fonction déclarée au LLM, que le modèle peut « appeler » en renvoyant un `functionCall` structuré |
| **`functionResponse`** | Format Gemini pour renvoyer le **résultat** d'un tool au modèle |
| **`tool_result`** | Payload client→serveur transportant le résultat d'un tool |
| **`functionDeclarations`** | Schéma JSON décrivant les tools à Gemini (section `tools`) |
| **`StreamedResponse`** | Objet Symfony permettant d'écrire la réponse progressivement |
| **`CURLOPT_WRITEFUNCTION`** | Callback cURL appelé à chaque réception de données du flux HTTP |

## 🎯 Prochaines étapes suggérées (après ce mapping)

1. ✅ ~~**Réparer les function calls**~~ → fait : parsing SSE `alt=sse`, reconstruction `functionCall → functionResponse` depuis l'historique, déclarations typées.
2. ✅ ~~**Uniformiser la confirmation des mutations**~~ → fait : confirmation branchée sur `requires_confirmation` pour tous les tools.
3. **Rendre le chat persistant côté client** : le client ne recharge pas l'historique (`findChatHistory` n'est utilisé que par le serveur pour le modèle) — possibilité de charger les derniers messages au démarrage du `FxAiChatControl`.
4. **Ajouter la gestion des erreurs réseau au niveau de l'UI** : distinguer « message CTA » des vraies erreurs (déjà partiellement fait avec `AiAgentError`).
5. **Améliorer le prompt système** : le contexte `DONNEES ESPACE UTILISATEUR` est envoyé à chaque tour — attention à la limite de tokens (20 messages + contexte + tools) avec `maxOutputTokens: 1500`.
6. **Prévoir un format de stream robuste** : si le backend est déployé derrière un reverse proxy, garder `X-Accel-Buffering: no` (et PHP `output_buffering=off` dans `php.ini` pour un flush immédiat).

## 📐 Règle de mise à jour de ce fichier

Comme pour `mapprojet.md`, toute modification impactant la partie IA doit mettre `mapia.md` à jour :

- ✅ Ajout/changement de **tools** (nouveaux `AiToolDefinition`, modification des paramètres)
- ✅ Changement de **route** (`/api/ai/...`), du **modèle Gemini**, du format de payload/stream
- ✅ Changement du **schéma BDD** (`AIChatMessage`) ou de l'**historique** utilisé par le modèle
- ✅ Changement du **flux agent** (boucle, confirmation, exécution des tools)
- ✅ Ajout/suppression de fichier IA côté client ou serveur
- ❌ Pas de mise à jour pour du refactor/interne sans impact sur le comportement

---
📄 **Fichier généré à partire de l'analyse du code** — `backtest/` (WPF) + `fxglobal/` (Symfony). Dernière vérification : chemins, noms de fonctions et flux décrits conformes au code réel.