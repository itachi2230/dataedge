# 🤖 DataEdge - Carte de la Partie IA (mapia.md)

> **Carte dédiée à l'agent IA** : front (client WPF) **et** back (serveur Symfony dans `fxglobal/`).
> Fichier complémentaire de `mapprojet.md` — il se concentre uniquement sur la brique « Agent IA » (chat + tools / function calling).

## 📌 Vue d'Ensemble

L'agent IA de DataEdge est un **copilote de trading intégré** au logiciel WPF. Il permet à l'utilisateur de :

- **Discuter** de ses performances, stratégies, trades et études en langage naturel.
- **Agir sur son espace local** via des *function calls* (tools) : lire le workspace, créer/supprimer des stratégies, ajouter des trades au journal, lire/chercher/créer/remplir/supprimer des études.

Le LLM est appelé **uniquement par le serveur** (les clés `OPENROUTER_API_KEY` / `GEMINI_API_KEY` ne quittent jamais le backend). Le fournisseur est choisi par le paramètre `ai_provider` de `fxglobal/config/services.yaml` : **`openrouter`** (défaut — API compatible OpenAI, modèle `openrouter_model`, ex. `z-ai/glm-5.3-flash` : ~10x moins cher que Gemini Flash, function calling testé) ou **`gemini`** (API `generativelanguage.googleapis.com`). Les deux implémentent `AiChatProviderInterface` et émettent le même protocole de chunks : le client ne change pas.

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
| **Back - LLM** | `fxglobal/src/Service/OpenRouterService.php` | Fournisseur OpenRouter (compatible OpenAI) : streaming SSE, function calling, chaîne de repli de modèles (429/402/5xx), plugin web `:online` pour la veille marché |
| **Back - contrat** | `fxglobal/src/Service/AiChatProviderInterface.php` | Contrat commun des fournisseurs LLM : chunks `text` / `tool_call` / `error` / `ping` |
| **Back - données** | `fxglobal/src/Entity/AIChatMessage.php` + `.../Repository/AIChatMessageRepository.php` | Historique de conversation en base |
| **Back - migration** | `fxglobal/migrations/Version20260714022623.php` | Table `aichat_message` |

> ℹ️ **Note importante** : `mapprojet.md` référencé auparavant d'anciens chemins `aiback/AIChatController (1).php` et `aiback/GeminiService (1).php` → **le backend réel est dans `fxglobal/src/`** (le dossier `aiback/` n'existe plus dans l'arborescence actuelle). Les fichiers `mapprojet.md` ont été mis à jour en conséquence.

## 🔄 Flux de communication (chat classique)

1. L'utilisateur clique sur le bouton **🤖 AGENT IA** (`MainWindow.xaml` ligne 114) → `ShowAiAgent()` instancie `FxAiChatControl(_cloudService)` dans `MainViewContainer`. À l'instanciation, `LoadHistoryAsync()` appelle `FxAiAgentService.GetChatHistoryAsync()` → **GET `/api/ai/history`** : si des échanges existent, ils remplacent le message d'accueil (historique affiché **sans rappeler Gemini**) ; la saisie est bloquée pendant le chargement.
2. L'utilisateur saisit un message → `SendMessage()` :
   - ajoute le message utilisateur dans la liste ;
   - affiche l'indicateur « L'agent réfléchit... » ;
   - construit le **contexte d'identité minimal** via `AgentWorkspaceService.BuildIdentityContextAsync()` (profil : nom/email/bio uniquement — plus aucun dump workspace automatique) ;
   - lance `FxAiAgentService.SendMessageToAiStreamAsync(...)` sur un thread de fond (`Task.Run`).
3. Le client POST `api/ai/chat` avec un **JWT Bearer** (via `FxCloudService.SecureRequestAsync`) :

   ```json
   {
     "message": "le prompt",
     "context": "{...identité JSON (profil)...}",
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
   - **interrupteurs admin** (`software_config` de l'app cliente, pilotables au dashboard `/admin/software`) : agent IA désactivé (`ai_chat_enabled`) → `503` JSON `{"error": ...}` ; quota utilisateur actif (`ai_quota_enabled`) et dépassé (comptage des tours `user` du jour) → `429` JSON — le client affiche le message métier du corps ;
   - **tour normal** : persiste d'abord le **contexte d'identité** du client en role=`context` (`persistContextOnce` — une seule fois par conversation : tant qu'un tour `context` existe dans la fenêtre des 40 derniers messages, il n'est pas re-stocké), puis le message **user** (role=`user`) ;
   - **tour outil** : sauvegarde les résultats en base sous forme d'un `AIChatMessage` role=`function` contenant `{"functionResponses": [{name, response: {name, content, is_error}}]}` ;
   - renvoie un `StreamedResponse` (SSE : `Content-Type: text/event-stream`, `Connection: keep-alive`, `X-Accel-Buffering: no`) ;
   - dans le callback : `GeminiService::generateStreamResponse(...)` avec un callback qui re-émet chaque morceau au client sous forme `data: {json}\n\n` (le texte et les tool_calls sont accumulés côté serveur) ;
   - après le stream : sauvegarde du tour **model** — texte final simple, **ou** JSON structuré `{"text": "...", "functionCalls": [{name, args}]}` si le tour contenait des appels d'outils.
5. `GeminiService` :
   - recharge les **40** derniers messages (`findChatHistory`) — y compris le tour `context` (identité), les tours `model` (functionCalls) et `function` (functionResponses) ;
   - reconstruit les `contents` Gemini dans l'ordre **exact exigé par l'API** : `[user: context/identité]` → `[model: functionCall]` → `[user: functionResponse]` → suite ; le prompt courant est dédoublonné puis ré-ajouté une seule fois, **sans aucun contexte collé au prompt** ;
   - appelle `POST https://generativelanguage.googleapis.com/v1beta/models/gemini-3.5-flash:streamGenerateContent?alt=sse&key=...` en cURL ; `alt=sse` garantit des événements SSE autonomes (un JSON complet par ligne `data:`), bufferisés ligne par ligne dans `CURLOPT_WRITEFUNCTION` ; garde-fous réseau : connexion 10 s, flux inactif 45 s (`STALL_TIMEOUT`), durée max 600 s (`MAX_STREAM_TIME`), boucle `curl_multi` avec pings keep-alive ;
   - injecte les **directives système** (constante `DIRECTIVES_TEXT`, cf. section « 🧠 Directives système ») et ajoute le bloc **`google_search`** (veille marché live) aux tools ; si l'API rejette la combinaison functionDeclarations + google_search (HTTP 400), **repli automatique** sur un appel sans google_search (`streamToGemini` / `stripGoogleSearch`) ; les fragments de raisonnement interne (`thought`) sont filtrés et ne sont jamais diffusés au client.
6. Chaque événement reçu de Google est analysé (`processSseLine`) :
   - `part.text` → émis au client : `{"text": "..."}` ;
   - `part.functionCall` → émis au client : `{"tool_call": {id, name, arguments}}` (plusieurs appels parallèles possibles) ;
   - battement keep-alive : pendant les silences du modèle (grounding `google_search`), le serveur émet toutes les ~10 s un `{"ping": true}` — ignoré par le client et non persisté ;
   - erreur API/HTTP/cURL → émis : `{"error": "..."}` (le client lève `AiAgentException`).
7. Le client (`FxAiAgentService`) lit le flux **en entier** :
   - lignes `text` → affichées en direct dans la bulle (`Dispatcher.Invoke`) ;
   - lignes `reasoning` (réflexion du modèle) → routées vers le **callback de statut** (`onStatusReceived`), affichées dans le bandeau de statut de la bulle — jamais dans le texte final ;
   - lignes `tool_call` → **collectées** ; à la fin du flux, chaque outil est **exécuté localement** (statuts `🔍 …` / `✓ …` émis au même callback de statut) puis un **nouveau tour serveur** est lancé avec `tool_results`.

## 🔁 Boucle agent (function calling)

`SendMessageToAiStreamAsync` exécute une boucle de **8 itérations maximum** :

- **Tour 1** : envoie `message` + `context` (identité) + `tools` → le serveur renvoie du texte **et/ou** un ou plusieurs `tool_call` (appels parallèles inclus, tous collectés). Les tours suivants n'envoient plus le `context` : le serveur rejoue le tour `context` persisté depuis l'historique.
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

Design « copilote » futuriste/pro : orbe IA néon (icône robot vectorielle), en-tête dégradé avec ligne d'accent cyan, bulles dégradées distinctes (IA / utilisateur), chips de prompts rapides, bouton d'envoi dégradé avec halo, indicateur de frappe à 3 points pulsés. Hébergé dans un **panneau latéral flottant** (`MainWindow`).

| Élément | Description |
|---|---|
| **En-tête** | Orbe IA néon statique (double anneau + icône robot vectorielle, **aucun effet de blur ni animation** — rendu logiciel), titre « DATAEDGE **AI** », pastille verte « Copilote connecté · vos données restent locales », bouton **⤢** (`ExpandRequested` → agrandit/réduit le panneau) et bouton **✕** (`CloseRequested` → masque le panneau) |
| **Bandeau de statut live** | Dans la bulle IA : point cyan pulsé + texte italique cyan (#7FD8E8) affiché **uniquement pendant le travail de l'agent** — **aucun chrono/décompte de secondes** (l'utilisateur ne doit pas percevoir la latence) : mention « Réflexion en cours… », fragments de réflexion du modèle (stream `reasoning`) et actions d'outils (`🔍 …` / `✓ … terminé`). Disparaît dès que la réponse finale commence (propriété `StatusText` → `HasStatus`, convertisseur `BoolToVis`) |
| **Liste des messages** | `ItemsControl` lié à `ObservableCollection<ChatMessage>`, bulles dégradées différenciées (User / IA via `DataTrigger IsUser`), **largeur fluide** : `MaxWidth` des tuiles lié à l'`ActualWidth` du `ScrollViewer` via `WidthMinusConverter` (les bulles s'agrandissent quand le panneau est agrandi), boutons fantômes **⧉ Copier** et **↺ Relancer** |
| **Indicateur** | 3 points cyan ondulants + « L'agent analyse... » (`LoadingIndicator`), masqué dès le 1er token |
| **Quick prompts** | 3 chips : « ◆ Bilan », « ◆ Mes règles », « ◆ Analyser » (envoient un prompt pré-rempli) |
| **Saisie** | `TextBox` multiligne dans une bordure arrondie (caret cyan), envoi par bouton disque dégradé « ➤ » ou touche `Entrée` |

#### Code-behind — fonctions principales

| Fonction | Rôle |
|---|---|
| `FxAiChatControl(FxCloudService)` | Ctor : instancie `FxAiAgentService` + `AgentWorkspaceService`, message d'accueil |
| `SendMessage(messageText)` | Pipeline complet : ajout msg user → indicateur de frappe → bulle IA vide → `StartStatusTracking()` → `BuildContextAsync()` → `SendMessageToAiStreamAsync(..., onStatusReceived)` (dans `Task.Run`) → mise à jour UI via `Dispatcher.Invoke` ; le 1er fragment de texte appelle `StopStatusTracking()` (bandeau effacé, bulle propre) |
| `StartStatusTracking / StopStatusTracking / PushStatus / RefreshStatusText` | Région « Bandeau de statut live » : rafraîchissement 10x/s (`DispatcherTimer` 100 ms) **sans aucun chrono affiché**, fragments `reasoning` conservés sur ~220 caractères (queue défilante), statuts d'outils prioritaires 4 s ; ré-armé automatiquement si un outil s'exécute après un premier fragment de texte (tours multi-étapes) |
| `CloseRequested` / `ExpandRequested` (+ `BtnClose_Click`, `BtnExpand_Click`, `SetExpandedState`) | Événements levés par les boutons de l'en-tête : ✕ → le `MainWindow` masque le panneau flottant ; ⤢ → le `MainWindow` bascule la largeur du panneau (430 px ↔ étendue) puis rappelle `SetExpandedState(bool)` pour basculer le glyphe ⤢/⤡ |
| `LoadHistoryAsync()` | Au démarrage du contrôle : `GetChatHistoryAsync()` en tâche de fond → si historique non vide **et** aucun message déjà envoyé, remplace le message d'accueil par l'historique ; saisie bloquée pendant le chargement, réactivée ensuite (échec silencieux, log) |
| `HandleToolCallAsync(AiToolCall)` | Demande une `MessageBox` Yes/No pour **tout** tool dont la définition déclare `requires_confirmation: true` (via `AgentWorkspaceService.RequiresConfirmation`) ; si refus → `AiToolResult.Error("Action refusée ou annulée par l'utilisateur.")` transmis au modèle en `is_error: true` |
| `ScrollToBottom()` | Auto-défilement du chat |
| `BtnCopy_Click` | Copie le texte de la bulle dans le presse-papiers (feedback temporaire) |
| `BtnResend_Click` | Relance le prompt correspondant |
| `TxtInput_KeyDown` | Envoi sur `Enter` |
| `QuickPrompt_Click` | Remplit et envoie un prompt prédéfini |

### `services/FxAiAgentService.cs` — Client HTTP de l'agent

| Fonction | Rôle |
|---|---|
| `SendMessageToAiStreamAsync(prompt, onChunkReceived, userContext, tools, toolHandler, onStatusReceived)` | **Point d'entrée.** Vérifications réseau/compte → boucle agent (max 8 tours, garde anti-boucle : un même appel outil+arguments exécuté 2 fois n'est plus ré-exécuté, le modèle doit répondre avec les données obtenues) → exceptions `AiAgentException`. `onStatusReceived` (optionnel) reçoit les statuts transitoires : fragments `reasoning` du modèle + statuts locaux d'exécution des outils (`🔍 <libellé>…` / `✓ <libellé> — terminé`, via `DescribeTool()`) — appelé sur un thread de fond, l'UI marshal via `Dispatcher` |
| `GetChatHistoryAsync(limit = 40)` | GET `api/ai/history` via `SecureRequestAsync` → parse `{messages: [{role, content, createdAt}]}` → `List<ChatMessage>` (Sender `User`/`AI`, horodatage ISO-8601 → local) ; liste vide si hors ligne/non connecté/erreur |
| `SendTurnAsync(...)` | Envoie un tour complet (payload JSON : `message`, `context`, `app_id`, `session_id`, `tools`, `tool_results` — le `context` n'est transmis qu'au tour 1) en POST `api/ai/chat` avec rejeu JWT (`SecureRequestAsync`), lit le flux SSE **en entier** et retourne la liste des `tool_call` collectés |
| `TryReadToolCall(line)` | Détecte une ligne contenant `tool_call` → construit `AiToolCall { Id, Name, Arguments }` (id généré si absent) |
| `ReadServerErrorMessage(body, fallback)` | Extrait le message métier `{"error": "..."}` des réponses 4xx/5xx du serveur (agent désactivé 503, quota dépassé 429) pour un affichage propre dans le chat ; repli technique sinon |
| `NormalizeArguments(call)` | Normalise les arguments du functionCall vers un objet JSON (sinon `{}` vide) |
| `ExecuteSafelyAsync(toolHandler, call)` | Exécute un tool en interceptant toute exception → l'erreur est renvoyée au modèle (`is_error: true`) au lieu de casser la boucle |
| `EmitChunk(line, onChunkReceived, onStatusReceived)` | Parse une ligne SSE : si `error` → lève `AiAgentException` ; si `reasoning` → callback de statut (réflexion du modèle, jamais mélangée au texte) ; si `text` → callback de contenu ; payload non JSON (bruit, fragment technique) → journalisé puis **ignoré**, jamais affiché |
| `DescribeTool(toolName)` | Traduit un nom technique de tool en libellé lisible pour le bandeau de statut (« get_workspace_snapshot » → « Lecture du workspace ») |
| `AiToolResultPayload` (classe) | Payload `tool_results` : `name`, `id`, `arguments`, `content`, `is_error` |

> 💡 **session_id** : généré par tour d'appel (`Guid.NewGuid()`), conservé sur toute la durée de la boucle agent pour un même prompt utilisateur.

### `services/AgentWorkspaceService.cs` — Contexte + exécution des tools

| Fonction | Rôle |
|---|---|
| `GetToolDefinitions()` | Retourne la liste des 13 tools déclarés au modèle, avec **paramètres typés** (`AiToolParameter` : `name`, `type` string/number/boolean, `description`, `required`) |
| `RequiresConfirmation(toolName)` | Indique si un tool nécessite une confirmation utilisateur (tool inconnu → `true` par sécurité) |
| `BuildIdentityContextAsync()` | Sérialise en JSON l'**identité seule** (profil cloud via `GetProfileCachedAsync`, cache 5 min) — envoyée au premier tour, persistée role=`context` côté serveur |
| `BuildWorkspaceSnapshotAsync()` | Sérialise le **résumé workspace** (stratégies + stats, 25 derniers trades, chemins des études) — renvoyé uniquement quand le modèle appelle `get_workspace_snapshot` |
| `ExecuteAsync(call, confirmMutation)` | Dispatch par `switch` vers l'implémentation de chaque outil ; gère la **confirmation** pour les outils marqués `requiresConfirmation` ; log + `AiToolResult.Error` sur exception |
| Coercition des arguments | `GetString` / `GetBool` / `GetNumber` tolèrent tous les ValueKind JSON (nombre, booléen, chaîne) — évite les exceptions quand le modèle envoie `rr`/`profit` en number |
| Outils lecture | `GetStrategyDetails`, `SearchTrades`, `AgentStudiesService` (`GetCatalog`, `Read`, `Search`) |
| Outils mutation | `CreateStrategy`, `DeleteStrategy`, `AddJournalTrade`, `AgentStudiesService` (`Create`, `Write`, `Delete`) |

> ℹ️ Depuis l'optimisation du payload : le client n'envoie plus que l'**identité** (profil) — persistée une fois par conversation (role `context`) et rejouée depuis l'historique BDD. Les données du workspace (stratégies, trades, études) ne sont plus jamais injectées automatiquement : le modèle les lit à la demande via `get_workspace_snapshot` et les autres tools.

### `services/AgentStudiesService.cs` — Tools IA « études »

| Fonction | Rôle |
|---|---|
| `GetCatalog()` | Liste compacte des études/notes (`etudes/` + `Notes/`) : nom, chemin relatif, dossier, taille, date — aucun fichier lu |
| `Read(args)` | Extrait le **contenu textuel** d'une étude (XamlPackage), images remplacées par `[image]` ; troncature `max_chars` (défaut 8000, plafond 24000) ; cache par date de modification |
| `Search(args)` | Recherche plein texte dans toutes les études, extraits contextuels (`max_results`, défaut 8) |
| `Create(args)` | Crée une étude dans `etudes/` (sous-dossier optionnel) et la remplit d'un contenu markdown initial |
| `Write(args)` | Écrit dans une étude existante : `replace` / `append` / `prepend` ; les images existantes sont conservées |
| `Delete(args)` | Supprime définitivement le fichier d'une étude |
| Extraction | Parcours du FlowDocument (Run, Bold/Italic/Underline, listes, tableaux, `[image]`) sur thread **STA** (obligatoire WPF), via `RichTextService` |
| Écriture | Convertit un **markdown léger** (`#`, `##`, `###`, `**gras**`, `*italique*`, `__souligné__`, `-`/`1.` listes) **+ mise en forme avancée** (`[color=...]...[/color]` couleur nom ou #RRGGBB, `[size=...]...[/size]` police) en FlowDocument puis sauvegarde XamlPackage. Couleur claire par défaut (fond sombre). Les blocs sont créés directement dans le document cible (jamais de reparenting inter-documents). Emojis autorisés avec modération. |

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
| **Persistance tour normal** | Persiste d'abord le contexte d'identité (`persistContextOnce` : role=`context`, une seule fois par conversation) puis un `AIChatMessage` (role=`user`) avant le stream |
| **Persistance tour outil** | Sauvegarde d'un `AIChatMessage` (role=`function`) contenant `{"functionResponses": [{name, response}]}` avant le stream |
| **Sortie** | `StreamedResponse` (SSE) : headers `Content-Type: text/event-stream`, `Cache-Control: no-cache`, `Connection: keep-alive`, `X-Accel-Buffering: no` |
| **Stream** | Appelle `GeminiService::generateStreamResponse()` avec callback → chaque chunk émis en `data: <json>\n\n` ; le texte et les tool_calls sont accumulés ; `try/catch` → chunk `{"error": ...}` |
| **Persistance modèle** | Après le stream : si le tour contenait des functionCalls → `AIChatMessage` (role=`model`) avec JSON `{"text": "...", "functionCalls": [{name, args, thought_signature}], "thought_signature": "..."}` ; sinon texte final simple (ou JSON avec `thought_signature` seule si un part texte en portait une). Rien n'est persisté si le tour est vide (erreur) |
| **Interrupteurs admin** | Avant toute persistance : `software_config` de `app_id` → agent désactivé (`ai_chat_enabled`) = `503` JSON ; quota utilisateur actif (`ai_quota_enabled`) et atteint = `429` JSON (tours `user` du jour via `countUserMessagesToday`). Pas de ligne de config = fonctionnalités ouvertes |
| **`GET /api/ai/history`** | `history()` : renvoie `{messages: [{role, content, createdAt}]}` pour l'affichage client — rôles `user`/`model` uniquement (tours internes `context`/`function` et tours model sans texte exclus via `extractModelText`) ; query `limit` (défaut 40, plafonné 200). **Accessible même si l'agent IA est désactivé** : aucun quota Gemini |
| **Repli fournisseur croisé** | Dans la closure de stream : callback d'émission **gardé** — un chunk `{"error": ...}` survenu **avant toute émission utile** est avalé et journalisé, puis le **fournisseur secondaire** (Gemini si principal OpenRouter, et inversement) tente la génération ; les pings traversent sans compter comme émission. Une fois le stream commencé, impossible de basculer (les erreurs passent au client). Les deux plateformes en échec → message final neutre « Le serveur IA de DataEdge est momentanément indisponible… ». L'historique BDD est agnostique du fournisseur : la bascule fonctionne en milieu de conversation |

### `src/Service/GeminiService.php` — Moteur Gemini

| Fonction | Rôle |
|---|---|
| `__construct(AIChatMessageRepository, string $geminiApiKey)` | Injection ; `$geminiApiKey` bindée depuis `config/services.yaml` (`%env(GEMINI_API_KEY)%`) |
| `preparePayload(User, userPrompt, tools)` | Construit le payload pour l'API Gemini : |
| | • **historique** : `findChatHistory(user, 40)` — inclut le tour `context` (identité), les tours `user`, `model` (texte ou functionCalls) et `function` (functionResponses) |
| | • **prompt user** : dédoublonné de l'historique puis ré-ajouté une seule fois, **sans contexte collé** (identité via le tour `context`, workspace via les tools) |
| | • **tours outils** : `buildContents` / `decodeModelTurn` / `buildFunctionResponseParts` reconstruisent l'ordre exigé : `[model: functionCall]` → `[user: functionResponse]` |
| | • **systemInstruction** : prompt système « DataEdge AI Assistant » (identité, expertise, directives, **section 5 : utilisation des outils**) |
| | • **generationConfig** : `temperature: 0.7`, `maxOutputTokens: 8192` |
| | • **tools.functionDeclarations** : conversion des déclarations client — paramètres typés `{name, type, description, required}` (format chaîne simple legacy accepté), `properties` en objet JSON, `required` omis si vide |
| | • **google_search à la demande** : attaché seulement si `shouldEnableWebSearch()` est vrai — `isMarketNewsIntent()` matche le prompt contre `MARKET_NEWS_PATTERN` (prix, cours, news, macro, paires, calendrier…, regex `\b...\b/u` anti faux positifs type « discours ») ; sur les tours outils (prompt vide), l'intention du dernier message `user` de l'historique est rejouée ; master switch `ENABLE_WEB_SEARCH` et repli HTTP 400 (`stripGoogleSearch`) inchangés |
| | • **thought signatures (Gemini 3, obligatoires)** : `processSseLine` capture la `thoughtSignature` de chaque part (texte ET functionCall — préfixe de namespace `default_api.` / `default_api:` retiré du nom d'appel) → le contrôleur la persiste dans le JSON du tour `model` → `decodeModelTurn` la rejoue verbatim au tour suivant (champ camelCase `thoughtSignature`), sinon l'API répond **HTTP 400** ; les tours historiques persistés avant ce mécanisme (sans signature) sont **rétrogradés en texte simple** dans `buildContents` et leurs `functionResponse` deviennent orphelins (purgés par `sanitizeContents`) au lieu de casser la conversation |
| `generateStreamResponse(...)` | Appelle `streamGenerateContent?alt=sse` du modèle **`gemini-3.5-flash`** en cURL ; `CURLOPT_WRITEFUNCTION` bufferise et traite les lignes SSE complètes ; `processSseLine` parse chaque événement et émet `text`, `tool_call` ou `error` ; après `curl_exec` : erreur cURL / code HTTP ≥ 400 → chunk `error` |

> ℹ️ **Boucle agent** : c'est côté **serveur** que la conversion `functionCall` → `tool_call` se fait (dans `GeminiService`), puis c'est le **client** qui exécute l'outil et renvoie `tool_results`. Le serveur persiste **tous les tours** (model functionCalls + function responses), ce qui permet de rejouer la boucle à chaque requête sans état en mémoire.

### `src/Entity/AIChatMessage.php` — Entité de l'historique

| Colonne | Type | Description |
|---|---|---|
| `id` | INT PK auto | Identifiant |
| `user` | FK → `user(id)` ON DELETE CASCADE | Propriétaire du message |
| `role` | VARCHAR(20) | `context` (identité persistée une fois par conversation), `user` (message utilisateur), `model` (texte final ou JSON `{"text","functionCalls"}`) ou `function` (JSON `{"functionResponses":[...]}`) |
| `content` | LONGTEXT | Texte du message, ou JSON structuré pour les tours outils |
| `createdAt` | DATETIME (auto `new \DateTime()`) | Horodatage |

### `src/Repository/AIChatMessageRepository.php`

| Fonction | Rôle |
|---|---|
| `findChatHistory(User $user, int $limit = 30)` | Récupère les messages d'un utilisateur triés DESC (tiebreaker `id` : départage les tours écrits dans la même seconde) puis **inversés** (ordre chronologique pour Gemini) ; limit 30 par défaut (le service appelle avec `40` pour couvrir les tours outils) |
| `hasRecentRole(User $user, string $role, int $limit = 40)` | Indique si un tour du rôle donné existe dans la fenêtre d'historique rejouée au modèle (utilisé par `persistContextOnce` : identité stockée une seule fois par conversation) |
| `countUserMessagesToday(User $user)` | Nombre de messages `user` depuis minuit (heure serveur) — alimente le **quota par utilisateur** de l'agent IA (activable au dashboard admin) |

### `src/Service/OpenRouterService.php` — Fournisseur OpenRouter (défaut)

| Élément | Détail |
|---|---|
| **Constructeur** | `__construct(AIChatMessageRepository, string $openRouterApiKey, string $openRouterModel, string $openRouterReasoningEffort = 'low')` — paramètres bindés depuis `config/services.yaml` |
| **Latence — effort de raisonnement** | Payload OpenRouter : `reasoning: {effort: <openrouter_reasoning_effort>}` (défaut **`low`** — premier token quasi immédiat pour un copilote de chat ; `''`/`'none'` = champ omis). Supporté par GLM 5.3 Flash et DeepSeek V4 Flash (vérifié API) |
| **Latence — fenêtre d'historique** | `HISTORY_LIMIT = 24` : fenêtre **rejouée au modèle** (distincte de l'affichage client qui reste à 40) → prefill plus court/moins cher à chaque tour d'outil |
| **Réflexion diffusée** | `EMIT_REASONING = true` : les fragments `delta.reasoning` (ou `reasoning_content` selon le fournisseur) sont émis au client sous forme de chunks **`{"reasoning": "..."}`** — protocole inchangé pour le reste (un type de chunk de plus) ; jamais mélangés au texte |
| **Chaîne de repli** | `FALLBACK_MODELS = ['deepseek/deepseek-v4-flash-0731', 'z-ai/glm-5.2:free']` sur 429/402/5xx, repli silencieux. Sur 429/5xx : le plugin `:online` est **abandonné d'abord sur le même modèle** (il aggrave la charge), puis modèle suivant — avec **backoff croissant** (`RETRY_BACKOFF_MS = 800` × n : 0.8/1.6/2.4 s) car les pools saturés se libèrent vite. Chaîne épuisée → **messages neutres DataEdge** (jamais de mention du fournisseur ni de code HTTP côté client ; le détail technique va dans le log serveur) : 429 = « Le serveur IA de DataEdge est momentanément saturé… », 402 = « Le service IA de DataEdge est temporairement indisponible… » |
| **Messages d'erreur neutres** | Toutes les chaînes `{"error": ...}` émises au client (connexion impossible, flux inactif, erreur API, interruption) parlent du « serveur IA de DataEdge » — l'utilisateur ne doit jamais voir le nom d'un fournisseur tiers ; `error_log('[AIChat][openrouter|gemini] …')` conserve le détail pour l'administrateur |
| **Veille marché** | `ENABLE_WEB_SEARCH = true` : suffixe `:online` (plugin web OpenRouter) si l'intention touche au présent ; retente sans le plugin en cas de refus HTTP 4xx (combinaison tools + web) |
| **Garde-fous réseau** | `CONNECT_TIMEOUT=10`, `STALL_TIMEOUT=45` (flux sans octet → coupure), `MAX_STREAM_TIME=600`, pings SSE `{"ping":true}` toutes les ~10 s pendant les silences, coupure si le client se déconnecte |

### Migration & config

| Fichier | Rôle |
|---|---|
| `migrations/Version20260714022623.php` | Crée la table `aichat_message` + FK `user_id` |
| `migrations/Version20260904100000.php` | Ajoute sur `software_config` : `ai_chat_enabled` (défaut 1, kill switch agent IA), `ai_quota_enabled` (défaut 0, quota par utilisateur) et `ai_daily_quota` (défaut 30 messages/jour) |
| `config/services.yaml` | `gemini_api_key: '%env(GEMINI_API_KEY)%'` → bindé sur `$geminiApiKey` ; **fournisseur IA** : `ai_provider` (`openrouter` défaut / `gemini`), `openrouter_api_key` (`%env(OPENROUTER_API_KEY)%`), `openrouter_model` (`z-ai/glm-5.3-flash`) et `openrouter_reasoning_effort` (`low`) bindés sur le constructeur d'`OpenRouterService` |
| `.env` | Contient `GEMINI_API_KEY` (clé remise via la variable d'environnement) |
| `config/packages/security.yaml` | Firewall `api` (`jwt`) sur `^/api` ; `api/ai/chat` est donc protégé par JWT |

## 🧠 Directives système (persona intégrée)

Définies dans `GeminiService::buildSystemInstruction()` via la constante `DIRECTIVES_TEXT` (nowdoc). Elles façonnent le comportement visible de l'agent :

- **Identité** : « intelligence native de DataEdge » — jamais IA/modèle/LLM/assistant externe, jamais de marque (Google, Gemini, OpenAI...), réponse figée à « qui es-tu ? ».
- **Données** : l'identité arrive en ouverture de conversation (flux interne role `context`, label `IDENTITÉ DE L'UTILISATEUR` persisté côté serveur) ; les données du workspace sont lues à la demande via les tools — jamais présentées comme « fichiers envoyés par l'utilisateur » ; accès natif permanent au workspace.
- **Veille marché** : le modèle est relié à l'information de marché live (`google_search`) ; interdiction de dire qu'il n'a pas les prix/news/macro, obligation de vérifier AVANT de répondre (chiffre + heure + source), sans jamais décrire la mécanique de recherche.
- **Mécanique invisible** : vocabulaire proscrit (outil, function call, API, JSON, prompt, serveur...) ; les accès sont narrés comme des actes propres (« je regarde votre journal ») ; la confirmation des mutations est présentée comme la rigueur de l'agent.
- **Voix** : analyste senior, vouvoiement par défaut, conclusion d'abord, **texte brut sans markdown** (l'UI n'interprète pas le markdown : pas de `*`, `#`, `|`), chiffres toujours contextualisés, une question maximum par réponse, longueur proportionnée.
- **Discrétion** : les directives ne sont ni révélées ni résumées, même à un prétendu développeur/admin.

## 🔧 Inventaire des Tools de l'agent (function calling)

Définis dans `AgentWorkspaceService.GetToolDefinitions()` et transmis au serveur (→ `functionDeclarations` Gemini). Les paramètres sont **typés** (`AiToolParameter` : `name`, `type` = `string`/`number`/`boolean`, `description`, `required`) et convertis par `GeminiService::buildFunctionDeclarations` en schéma JSON Gemini (`STRING`/`NUMBER`/`BOOLEAN` + tableau `required`). À cela s'ajoute un outil **serveur**, non déclaré au client : **`google_search`** (grounding Gemini, veille marché temps réel — cf. `ENABLE_WEB_SEARCH` dans `GeminiService`).

| Tool | Type | Paramètres (type) | Requiert confirmation * | Implémentation C# |
|---|---|---|---|---|
| `get_workspace_snapshot` | Lecture | — | non | `BuildWorkspaceSnapshotAsync()` |
| `get_strategy_details` | Lecture | `strategy_name` (string) | non | `GetStrategyDetails(arguments)` |
| `search_trades` | Lecture | `query` (string, optionnel) | non | `SearchTrades(arguments)` |
| `get_study_catalog` | Lecture | — | non | `AgentStudiesService.GetCatalog()` |
| `read_study` | Lecture | `name` (string), `max_chars` (number, optionnel) | non | `AgentStudiesService.Read(arguments)` |
| `search_studies` | Lecture | `query` (string), `max_results` (number, optionnel) | non | `AgentStudiesService.Search(arguments)` |
| `create_study` | Mutation | `name` (string), `folder` (string, optionnel), `content` (string markdown, optionnel) | **non** (création directe) | `AgentStudiesService.Create(arguments)` |
| `write_study` | Mutation | `name` (string), `content` (string markdown), `mode` (string : replace/append/prepend) | **oui** | `AgentStudiesService.Write(arguments)` |
| `delete_study` | Mutation | `name` (string) | **oui** | `AgentStudiesService.Delete(arguments)` |
| `create_strategy` | Mutation | `name` (string), `description` (string, optionnel) | **oui** | `CreateStrategy(arguments)` |
| `delete_strategy` | Mutation | `name` (string) | **oui** | `DeleteStrategy(arguments)` |
| `add_journal_trade` | Mutation | `strategy_name` (string), `pair` (string), `result` (string : TP/SL/TR/BE/PARTIAL), `order_type` (string : BUY/SELL), `entry` (string date), `exit` (string date), `rr` (number), `profit` (number), `description` (string) | **oui** | `AddJournalTrade(arguments)` |

### Règles de sécurité actuelles (importantes à connaître)

Dans `FxAiChatControl.HandleToolCallAsync` :

- **Tous les tools marqués `requires_confirmation: true`** (soit `create_study`, `write_study`, `delete_study`, `create_strategy`, `delete_strategy`, `add_journal_trade`) → une `MessageBox` « Autoriser cette modification ? » est affichée avec le nom du tool et ses arguments ; l'exécution n'a lieu que si l'utilisateur répond **Yes**. Les lectures (`read_study`, `search_studies`, catalogues...) ne demandent aucune confirmation.
- **Refus** → `AiToolResult.Error("Action refusée ou annulée par l'utilisateur.")` est renvoyé au modèle en `is_error: true` : l'agent est informé et peut reformuler au lieu de réessayer en boucle.
- **Outils inconnus** → `RequiresConfirmation()` retourne `true` par sécurité (confirmation demandée).
- **Exceptions d'exécution** → interceptées par `ExecuteSafelyAsync` côté `FxAiAgentService` : l'outil plante sans casser la boucle, le message d'erreur est transmis au modèle qui peut se corriger.

## 🔌 Intégration dans le logiciel (points d'entrée)

| Élément | Description |
|---|---|
| `MainWindow.xaml` | **Bouton flottant `BtnAiFab`** (disque néon **46 px**, **double anneau statique** sans effet de blur ni animation continue, **icône « sparkle » IA** — étoile à quatre branches courbes, bas-droit du dashboard proche du bord, `Panel.ZIndex=400`) — déclencheur unique du copilote, accessible depuis n'importe quelle vue. Masqué tant que le panneau est ouvert, réapparaît à la fermeture |
| `MainWindow.xaml.cs` `BtnAiFab_Click` → `ToggleAiAgent()` | Ouvre/ferme le **panneau latéral flottant** `AiAgentDrawer` (Grid superposée, `Panel.ZIndex=500`, ancrée à droite, largeur normale 430 px, coins arrondis + profondeur simulée par bordures statiques) qui héberge `FxAiChatControl` (`AiAgentPanelHost`). **Ouverture/fermeture instantanées** (simple bascule de `Visibility`, **aucune animation** : le rendu logiciel de l'app rend chaque animation coûteuse et avait ralenti toute la fenêtre). L'instance est créée **une seule fois** (`_aiChat`) : l'historique et la conversation sont conservés entre les ouvertures. La vue courante (dashboard, chart, journal...) reste intacte derrière le panneau ; `CloseRequested` masque le panneau (`HideAiAgent`), `ExpandRequested` bascule la taille (`ToggleAiAgentSize`) |
| `MainWindow.xaml.cs` `ToggleAiAgentSize()` | **Agrandissement du panneau** (demandé par le bouton ⤢ du chat) : largeur 430 px ↔ largeur étendue généreuse (`min(1000, largeur vue - 90)`), **application directe sans animation**, état mémorisé (`_aiAgentExpanded`), glyphe du bouton mis à jour via `SetExpandedState` ; les tuiles de message suivent la largeur via `WidthMinusConverter` |
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
| **`thought_signature`** | Signature de raisonnement chiffrée attachée par les modèles Gemini 3 aux parts (texte et functionCall) — doit être renvoyée **verbatim** au tour suivant (rejeu d'historique), sinon l'API répond HTTP 400 ; persistée dans le JSON du tour `model` et restituée en camelCase `thoughtSignature` dans les parts |
| **`StreamedResponse`** | Objet Symfony permettant d'écrire la réponse progressivement |
| **`CURLOPT_WRITEFUNCTION`** | Callback cURL appelé à chaque réception de données du flux HTTP |

## 🎯 Prochaines étapes suggérées (après ce mapping)

1. ✅ ~~**Réparer les function calls**~~ → fait : parsing SSE `alt=sse`, reconstruction `functionCall → functionResponse` depuis l'historique, déclarations typées.
2. ✅ ~~**Uniformiser la confirmation des mutations**~~ → fait : confirmation branchée sur `requires_confirmation` pour tous les tools.
3. ✅ ~~**Rendre le chat persistant côté client**~~ → fait : `FxAiChatControl.LoadHistoryAsync()` charge automatiquement l'historique serveur (`GET /api/ai/history`, 40 messages) à l'ouverture du chat et remplace le message d'accueil ; aucun appel Gemini ; garde-fou si un message est envoyé pendant le chargement.
4. **Ajouter la gestion des erreurs réseau au niveau de l'UI** : distinguer « message CTA » des vraies erreurs (déjà partiellement fait avec `AiAgentError`).
5. ✅ ~~**Améliorer le prompt système**~~ → fait : noyau directif complet (constante `DIRECTIVES_TEXT` de `GeminiService`), veille marché via `google_search` (repli automatique en HTTP 400) ; §1 réécrit pour le nouveau flux (identité en ouverture, workspace à la demande par tools, regroupement des accès dans un même tour).
6. **Prévoir un format de stream robuste** : si le backend est déployé derrière un reverse proxy, garder `X-Accel-Buffering: no` (et PHP `output_buffering=off` dans `php.ini` pour un flush immédiat).
7. ✅ ~~**Alléger le payload et la latence par message**~~ → fait : identité seule envoyée au premier tour (persistée role `context` une fois par conversation, auto-régénérée en sortie de fenêtre), workspace uniquement via tools (`get_workspace_snapshot` compact : 25 trades, sans best_configs), cache 30 s des sondes réseau (`GetCloudStatusAsync`), cache 5 min du profil, ordre d'historique déterministe (tiebreaker `id`).
8. ✅ ~~**Supprimer le contenu technique du chat**~~ → fait : marqueurs `[Outil ...]` retirés de la bulle (journalisation seule via `FxCloudService.Log`), payloads SSE non JSON ignorés côté client (`EmitChunk`), `TryReadToolCall` durci (functionCall sans nom ignoré), garde anti-boucle sur les appels d'outils répétés, `extractModelText` ne renvoie plus les tours JSON bruts dans `/api/ai/history`.

9. ✅ ~~**Ajouter un fournisseur LLM économique**~~ → fait : `OpenRouterService` (API compatible OpenAI, `ai_provider=openrouter`, modèle principal `z-ai/glm-5.3-flash`, replis `deepseek/deepseek-v4-flash-0731` puis `z-ai/glm-5.2:free` sur 429/402/5xx, plugin `:online` pour la veille marché avec repli automatique) ; Gemini conservé comme fournisseur alternatif (`ai_provider=gemini`).
10. ✅ ~~**Réduire la latence réelle (premier token)**~~ → fait : `reasoning: {effort: 'low'}` injecté dans le payload OpenRouter (paramètre `openrouter_reasoning_effort`, défaut `low` — TTFT quasi immédiat, effort max inutile pour un copilote) + fenêtre d'historique **rejouée au modèle** réduite à 24 tours (`HISTORY_LIMIT` OpenRouterService, affichage client inchangé à 40).
11. ✅ ~~**Ne plus jamais laisser un écran figé pendant la réflexion / les outils**~~ → fait : chunks `{"reasoning": ...}` émis par `OpenRouterService` (delta.reasoning) et routés côté client vers un **bandeau de statut live** dans la bulle IA (`ChatMessage.StatusText`) : réflexion qui défile (sans chrono affiché) + statuts d'outils `🔍 / ✓` (`DescribeTool`) ; le bandeau disparaît dès que la réponse finale commence (aucun contenu technique dans la bulle). Interface redessinée « copilote » (orbe néon, bulles dégradées, chips, bouton gradient) et agent déplacé dans un **panneau latéral flottant** (`AiAgentDrawer`) accessible depuis n'importe quelle vue (bouton flottant, bascule ouvre/ferme).
12. ✅ ~~**Fiabiliser la saturation + neutraliser les messages + restaurer la fluidité**~~ → fait : (a) **repli fournisseur croisé** dans `AIChatController` — si le fournisseur principal échoue avant tout octet émis (429, crédits, panne), la plateforme secondaire (Gemini ↔ OpenRouter) tente la génération ; (b) **messages d'erreur 100 % DataEdge** dans les deux services (jamais de mention du fournisseur tiers ni de code HTTP côté client, détails en `error_log`) ; (c) **suppression de toutes les animations coûteuses** (halo pulsé du bouton flottant, slide/fade du panneau, animation de largeur, blurs de l'orbe et du point de statut) — l'app tourne en rendu logiciel où chaque animation continue force une ré-rastérisation de la fenêtre : manipulation désormais instantanée, look conservé via designs statiques (double anneau néon, bordures en couches).
13. ✅ ~~**Faux « Connexion Internet non disponible » alors que le serveur répond**~~ → fait : dans `GetCloudStatusAsync` (client), la sonde HTTP vers le serveur fait désormais foi — serveur joignable = `READY`/`ONLINE_NO_ACCOUNT`, même si l'ICMP sortant (ping 8.8.8.8) est bloqué par le réseau (VPN, pare-feu, FAI) ; le ping ne sert plus qu'à départager « pas d'internet » vs « serveur down » quand le serveur est réellement injoignable, et `IsInternetAvailableAsync` gagne des replis (ping 1.1.1.1 puis HEAD HTTPS `gstatic.com/generate_204` via un `HttpWebRequest` isolé, sans l'en-tête Bearer du client partagé).


## 🚀 Déploiement — Checklist CloudPanel / VPS (streaming SSE)

Le flux IA est un **SSE long** (jusqu'à 600 s) : sans ces réglages, nginx/PHP-FPM coupent ou bufferisent le stream (chat figé, coupure à 5 min).

### PHP-FPM (`php.ini` + pool FPM)
| Réglage | Valeur cible | Pourquoi |
|---|---|---|
| `request_terminate_timeout` | **700** | Défaut 300 s : PHP-FPM **tuerait** le worker en plein flux de 10 min |
| `max_execution_time` | **0** (ou ≥ 700) | Idem, côté PHP |
| `output_buffering` | **0** | Sinon les chunks `data:` sont retenus → l'utilisateur ne voit rien arriver |
| `zlib.output_compression` | **Off** | La compression attend de remplir son buffer → détruit le streaming |
| `pm.max_children` | ≥ nb utilisateurs IA simultanés + marge | **1 child FPM = 1 chat SSE** pendant toute la génération ; sous-dimensionné → les autres requêtes du site attendent |

### nginx (vhost CloudPanel)
```nginx
# Dans le bloc location ~ \.php$ du vhost :
fastcgi_buffering off;          # équivalent serveur du header X-Accel-Buffering: no (déjà émis par AIChatController)
fastcgi_read_timeout 700s;      # ne pas couper avant la fin du flux
gzip off;                       # sur text/event-stream uniquement si règle globale
```
> Le contrôleur émet déjà `X-Accel-Buffering: no` — `fastcgi_buffering off` est le filet de sécurité si un `location` ne transmet pas le header.

### Divers
- **VPS en région EU** (Paris/Frankfurt) : OpenRouter est servi depuis l'EU → RTT minimal pour le TTFT.
- **Valider après déploiement** : `php -l` OK, un message de chat avec observation du **TTFT avant/après** (`effort low` vs `max`), bandeau de statut visible pendant la réflexion et les tools, et aucune coupure sur une réponse longue (> 5 min).

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