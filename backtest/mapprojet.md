# 📊 DataEdge - Carte du Projet (Project Map)

## Vue d'Ensemble

**DataEdge** est une application WPF (.NET Framework 4.7.2) de **backtesting et journal de trading Forex/CFD**. Elle permet aux traders de :
- Gérer des **stratégies de trading** et leur **journal de trades**
- Visualiser des **graphiques de prix** (via WebView2 / TradingView Lightweight Charts)
- Calculer des **statistiques avancées** (winrate, expectancy, R, drawdown)
- Synchroniser les données avec un **serveur cloud Symfony** (fxdataedge.com)
- Prendre des **notes hebdomadaires** et des **études** (format .etude/XamlPackage)
- Afficher des **indices de sentiment** (Fear & Greed US + Crypto)
- **Backtester / Rejouer** l'historique des trades sur le graphique

---

## Architecture Technique

### Stack
| Technologie | Usage |
|---|---|
| **WPF .NET 4.7.2** | Interface graphique |
| **C# 8+** | Langage |
| **WebView2** | Rendu du graphique (Chromium) |
| **OxyPlot** | Graphiques statistiques (Winrate, Sessions, etc.) |
| **EPPlus** | (retiré — migration Excel → JSON terminée, licence non commerciale incompatible avec une distribution commerciale) |
| **PdfPig / ExcelDataReader / DocumentFormat.OpenXml** | Lecture PDF / Excel / Word par l'agent IA (licences Apache 2.0 / MIT, gratuites en commercial) |
| **QuestPDF** | Génération PDF (rapports de statistiques + documents de l'agent IA) |
| **Newtonsoft.Json + System.Text.Json** | Sérialisation JSON |

### Dépendances Principales (NuGet)
- `PdfPig 0.1.16` - Extraction de texte PDF (Apache 2.0) + `Microsoft.Bcl.HashCode 6.0.0`
- `DocumentFormat.OpenXml 2.20.0` - Création/lecture Word .docx (MIT)
- `ExcelDataReader 3.6.0` - Lecture Excel .xlsx/.xls (MIT)
- `QuestPDF 2026.8.0` - Génération PDF (Community : gratuit < 1 M$ de CA/an)
- `Microsoft.Web.WebView2 1.0.3912.50` - Navigateur intégré
- `OxyPlot.Wpf 2.0.0` - Graphiques statistiques
- `Newtonsoft.Json 13.0.3` - JSON


### Structure des Fichiers

```
backtest/
├── 📄 Fichiers Principaux (Code-Behind WPF)
│   ├── App.xaml / .cs          → Point d'entrée, gestion erreurs globales
│   ├── MainWindow.xaml / .cs   → Fenêtre principale (Dashboard,Journal,Chart,Settings)
│   ├── backtesteur.xaml / .cs  → Fenêtre de backtest (héberge Chart + Stratégie)
│   ├── Chart.xaml / .cs        → Contrôle graphique TradingView (WebView2)
│   ├── AjoutTrade.xaml / .cs   → Fenêtre d'ajout/édition de trade
│   ├── addStrategieWindow.xaml / .cs → Fenêtre ajout/modification stratégie
│   ├── newvisu.xaml / .cs      → Fenêtre visualisation détaillée d'un trade
│   ├── opening.xaml / .cs      → Fenêtre de démarrage/splash
│   └── ZoomImageWindow.xaml / .cs → Zoom sur une image (notes/études)
│
├── 📄 Contrôles Utilisateur (UserControls)
│   ├── StatisticsControl.xaml / .cs → Vue stats d'une stratégie (DataGrid + Graphiques)
│   ├── StatisticsView.xaml / .cs    → Visualisation avancée (OxyPlot, équité, audit)
│   ├── SettingsView.xaml / .cs      → Panneau paramètres (compte cloud, profil)
│   ├── ControlStat.xaml / .cs       → Badge/vignette de performance d'une stratégie
│   ├── EtudesView.xaml / .cs        → Module d'études (éditeur riche .etude)
│   ├── TradeVisualizerControl.xaml / .cs → Affiche les screenshots (HTF/LTF) d'un trade
│   ├── Demo.xaml / .cs              → Onboarding professionnel (4 slides textuels, animations, style cyber)
│   ├── CustomMessageBoxView.xaml / .cs → MessageBox personnalisée
│   └── InputDialog.xaml / .cs       → Boîte de dialogue pour saisie texte
│
├── 📄 Modèles & Logique Métier
│   ├── strategie.cs      → Classe Strategie, Trade, PerformanceStat, AdvancedStats, utils
│   ├── CalculStatistics.cs → Structure Statistics (stats globales)
│   ├── FirstLaunchManager.cs → Détection premier lancement + flag spotlight
│   ├── SpotlightStep.cs    → Modèle de données pour une étape du tour guidé
│   ├── SpotlightOverlay.cs → Système de Feature Highlight / Spotlight overlay pour le tour guidé du dashboard
│   └── NetworkUtils.cs    → Récupération indices Fear & Greed (API CNN + alternative.me)
│
├── 📄 Services
│   ├── services/
│   │   ├── FxCloudService.cs  → Service cloud (auth, sync, profil, support, crash reports)
│   │   ├── ChartBridge.cs     → Bridge C# ↔ JavaScript (WebView2 graphique)
│   │   ├── ReplayPositionStore.cs → Persistance JSON de la dernière position replay par paire/timeframe
│   │   ├── Dataservice.cs     → Récupération données marché (API Symfony, cache CSV)
│   │   ├── AgentWorkspaceService.cs → Contexte utilisateur et exécution contrôlée des tools IA
│   │   ├── AgentStudiesService.cs   → Tools IA « études » (lecture/recherche/création/écriture/suppression .etude, extraction texte sans images)
│   │   ├── AgentWeeksService.cs     → Tools IA « weeks » : notes hebdo du dashboard (catalogue, lecture, recherche, calendrier mensuel, création/écriture/suppression Notes_*.etude, markdown → FlowDocument Segoe UI 14 #EEEEEE)
│   │   ├── AgentMarketService.cs     → Tools IA « web & marché » : 7 nouveaux outils de données en direct (calendrier économique réel, FedWatch, sentiment retail, cotations, actualités, recherche web, lecture de page)
│   │   ├── AgentFileService.cs     → Tools IA « fichiers locaux » : lecture txt/md/json/csv/xml/pdf(PdfPig)/xlsx(ExcelDataReader)/docx(OpenXML) de TOUS les dossiers du PC (Bureau, Documents, Téléchargements, Images, disques…), recherche de texte, création txt/json/md/pdf/word, ouverture dossier ; lecture ouverte aux permissions Windows de la session, écriture confinée à DataEdge ; + AgentMarkdown (parseur partagé) et WordDocumentBuilder (.docx)
│   │   ├── PdfExportService.cs → Génération d’un rapport PDF moderne et structuré de la stratégie + ExportDocumentPdf : génération PDF générique markdown → design DataEdge (utilisée par l’agent IA)
│   │   └── RichTextService.cs → Service Rich Text (sauvegarde/chargement XamlPackage)
│   └── RichTextService.cs     → Service Rich Text (sauvegarde/chargement XamlPackage)
│
├── 🎨 Resources (XAML / UI)
│   ├── resources/
│   │   ├── default_image.png, default_user.png
│   │   ├── file.png, folder.png
│   │   └── chart/
│   │       ├── index.html                → Page HTML principale du graphique
│   │       ├── lightweight-charts.js     → Librairie TradingView
│   │       ├── chart_engine.js           → Moteur de graphique
│   │       ├── drawing_configs.js        → Configurations dessins
│   │       ├── drawing_manager.js        → Gestionnaire dessins
│   │       ├── drawing_plugin.js         → Plugin dessins
│   │       ├── drawing_utils.js          → Utilitaires dessins
│   │       ├── style.css, replay.css, toolbar.css → Styles
│   ├── Images/
│   │   └── slide1-3.png      → Slides de démonstration
│   └── Properties/
│       ├── AssemblyInfo.cs
│       ├── Resources.resx / Resources.Designer.cs
│       └── Settings.settings / Settings.Designer.cs
│
├── ⚙️ Configuration
│   ├── App.config
│   ├── backtest.csproj
│   └── packages.config
│
└── 📂 Binaries & Data (Runtime)
    ├── bin/Debug/
    │   ├── data/               → Stratégies (fichiers JSON)
    │   ├── metadata/           → Index des stratégies (strategies.txt)
    │   ├── Notes/              → Notes hebdomadaires (.etude)
    │   ├── etudes/             → Études (.etude)
    │   ├── cacheimage/         → Images mises en cache
    │   ├── chart/historical/   → Données CSV historiques + watchlist_cache.json
    │   └── config.txt          → Configuration connexion serveur
    └── obj/Debug/              → Fichiers compilés temporaires
```

---

## Flux de Navigation (UI)

```
┌─────────────────────────────────────────────────────────────────────┐
│                      MainWindow (Dashboard)                         │
│                                                                     │
│  [Home]  [Chart]  [Études]  [Journal]  [Settings]  [Sync] [Notifs] │
│  ┌───────────────────────────────────────────────────────────────┐  │
│  │                    MainViewContainer (ContentControl)          │  │
│  │                                                               │  │
│  │  DashboardView (defaut)                                       │  │
│  │  ├── Fear & Greed Indices (US + Crypto)                       │  │
│  │  ├── Notes hebdomadaires (RichTextBox)                        │  │
│  │  ├── Journal des trades (DataGrid)                            │  │
│  │  ├── Vignettes stratégies (perfStrat / ControlStat)           │  │
│  │  └── Calendrier économique (WebView2 Investing.com)           │  │
│  │                                                               │  │
│  │  StatisticsControl (vue stats stratégie)                      │  │
│  │  ├── DataGrid trades filtrable                                │  │
│  │  ├── Statistiques (Winrate, PF, Best/Worst Config)           │  │
│  │  ├── Bouton de partage PDF vers Documents                     │  │
│  │  └── StatisticsView (OxyPlot graphs)                         │  │
│  │                                                               │  │
│  │  Chart (WebView2 graphique)                                   │  │
│  │  ├── Watchlist, Timeframes, Indicateurs                       │  │
│  │  ├── TradingView Lightweight Charts (JS)                      │  │
│  │  ├── Système de dessins (drawing plugin)                      │  │
│  │  └── Formulaire ajout trade + captures écran                  │  │
│  │                                                               │  │
│  │  EtudesControl (éditeur d'études)                             │  │
│  │  ├── TreeView fichiers .etude                                 │  │
│  │  └── RichTextBox avec formatage (B/I/U, couleurs, images)    │  │
│  │                                                               │  │
│  │  SettingsView (paramètres)                                    │  │
│  │  ├── Login / Register / Profil                                │  │
│  │  └── Paramètres application                                   │  │
│  └───────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────┘
```

---

## Modèles de Données (Data Models)

### `StrategieData` (fichier JSON : `data/{Nom}.json`)
```json
{
  "Nom": "Ma Stratégie",
  "Description": "...",
  "ChampsCustomConfig": ["RSI", "ZONE"],
  "Trades": [ /* ... */ ],
  "Journal": [ /* ... */ ],
  "StatsBasiques": { "Total Trades": 50, "Winrate": 60.0, ... },
  "StatsAvancees": { /* AdvancedStats */ }
}
```

### `Trade`
| Propriété | Type | Description |
|---|---|---|
| Id | long | Identifiant unique |
| Paire | string | Ex: EURUSD, GBPUSD, XAUUSD |
| Result | enum Resultat | TP (Take Profit), SL (Stop Loss), TR, BE, PARTIAL |
| DateEntree | DateTime | Date/heure entrée position |
| DateSortie | DateTime | Date/heure sortie position |
| RR | float | Risk/Reward ratio (ex: 2.5) |
| prixOpen / prixClose | double | Prix d'entrée et sortie |
| TypeOrdre | enum TypeOrdre | BUY ou SELL |
| Profit | double | Profit en € |
| ImageLtf / ImageHtf | string | Screenshots (URL ou fichier local) |
| ChampsPersonnalises | List<ChampPersonnalise> | Champs dynamiques configurés |
| strategie | string | Nom de la stratégie parente |

### `Statistics` (Dashboard global)
- TotalProfit, TotalLoss, BestPair, WorstPair
- SuccessRateBuy, SuccessRateSell
- StrategyPerformance (Dictionary stratégie → profit)
- BestTrade

### `AdvancedStats` (Par stratégie)
- **DayOfWeekStats** : Performance par jour (Lundi-Vendredi)
- **PairStats** : Performance par paire de devises
- **SessionStats** : Tokyo, Londres, New York
- **TypeOrdreStats** : BUY vs SELL
- **PerformanceStats** : Par champs dynamiques (RSI, ZONE...)
- **BestConfigs / WorstConfigs** : Top 5 meilleures/pires configurations

---

## Flux de Données

### Stockage Local
```
UserProfile → session_v1.json
Strategies  → data/{Nom}.json (index dans metadata/strategies.txt)
Notes       → Notes/{Date}.etude (format XamlPackage)
Études      → etudes/{dossier}/{nom}.etude
Cache       → chart/historical/{SYMBOL}_{TF}_{ANNEE}.csv
Images      → cacheimage/{timestamp}_{HTF|LTF}.png
Config      → config.txt (server_url, app_id)
Tokens      → session.bin
```

### Synchronisation Cloud
```
FxCloudService.FullSyncAsync()
  ├── SyncEverythingAsync()   → Upload local → serveur
  │     ├── data/
  │     ├── etudes/
  │     ├── Notes/
  │     ├── cacheimage/
  │     └── metadata/
  └── SyncFromServerAsync()   → Download serveur → local
```

### API Server Endpoints (Symfony - fxdataedge.com)
```
POST /api/register
POST /api/login
POST /token/refresh
GET  /api/me                              → Profil utilisateur
POST /api/user/update                     → Update profil
POST /software/handshake                  → Version check + notifications
POST /software/report-crash               → Crash report
POST /support/send                        → Message support
POST /api/ai/chat                         → Chat IA authentifié, réponse streamée
                                          → Déclare les tools du fournisseur LLM (OpenRouter ou Gemini), émet `tool_call`, reçoit `tool_result` et poursuit la boucle agent
                                          → Interrupteurs admin : 503 si agent désactivé, 429 si quota utilisateur dépassé (software_config)
GET  /api/ai/history?limit=...            → Historique de conversation IA (affichage client, sans rappel Gemini)
DELETE /api/ai/history                     → Efface tout l'historique IA de l'utilisateur (bouton 🗑 du chat : serveur + mémoire du modèle remise à zéro)
POST /api/cloud/sync-file                 → Upload fichier
POST /api/cloud/file-info                 → Vérification hash
GET  /api/cloud/list?app_id=...           → Liste fichiers distants
GET  /api/cloud/download?app_id=...       → Download fichier
GET  /api/public/data/fetch?pair=&tf=&year= → Données CSV historiques
GET  /api/public/data/pairs               → Liste paires disponibles
GET  /admin/software/dashboard            → Back-office software (builds, notifs, quota IA)
GET  /admin/ai/dashboard                  → Back-office IA : config coût (toggles + sliders)
POST /admin/ai/settings/update            → Sauvegarde ai_settings
POST /admin/ai/sessions/clear             → Purge sessions cache expirées
```

---

## Fichiers Clés & Leurs Rôles

| Fichier | Rôle Principal |
|---|---|
| `MainWindow.xaml.cs` | Hub central : dashboard, navigation, journal, notifications, Fear & Greed |
| `strategie.cs` | Modèle + logique métier : CRUD trades, calcul stats, migration Excel→JSON |
| `Chart.xaml.cs` | Graphique TradingView WebView2, gestion timeframes, watchlist, ajout trades |
| `FxCloudService.cs` | Service cloud : auth, sync, profil, handshake, crash reporting |
| `services/AgentWorkspaceService.cs` | Expose le contexte local et les tools IA de lecture/mutation confirmée (dont `add_journal_trade` et `add_backtest_trade` pour ajouter un trade au journal ou au backtest d'une stratégie) |
| `fxglobal/src/Controller/AIChatController.php` | Endpoint IA : route `POST /api/ai/chat`, streaming SSE, persistance BDD |
| `fxglobal/src/Controller/AIAdminController.php` | Back-office `/admin/ai/**` : config coût de l'agent IA (fournisseur, modèle, sliding window, cache, pinning, web search) + stats |
| `fxglobal/src/Entity/AISettings.php` | Config globale de l'IA (table `ai_settings`, une ligne pilotée au dashboard admin) |
| `fxglobal/src/Entity/AIUserSession.php` | Sessions de Prompt Caching par utilisateur (`ai_user_session`, UUID stable régénéré à l'expiration) |
| `fxglobal/src/Service/GeminiService.php` | Appel API Gemini, déclare les fonctions et convertit ses `functionCall` en `tool_call` |
| `fxglobal/src/Service/OpenRouterService.php` | Fournisseur LLM OpenRouter (compatible OpenAI) : streaming SSE, function calling, chaîne de repli de modèles, plugin web `:online`, **Prompt Caching `session_id` + provider pinning** pilotés par `ai_settings` |
| `Dataservice.cs` | Récupération données marché depuis API Symfony, cache CSV local |
| `ChartBridge.cs` | Pont C#↔JS pour le WebView2 du graphique (dessins, captures, save/load replay positions) |
| `StatisticsControl.xaml.cs` | Contrôle statistiques d'une stratégie (DataGrid + vues) |
| `StatisticsView.xaml.cs` | Visualisation OxyPlot (courbe équité, winrate, sessions, audit) |
| `SettingsView.xaml.cs` | Gestion compte cloud, profil, paramètres app |
| `EtudesView.xaml.cs` | Éditeur d'études (format .etude, arborescence, formatage riche) |
| `NetworkUtils.cs` | Indices Fear & Greed (CNN US + alternative.me Crypto) |
| `TradeVisualizerControl.xaml.cs` | Affiche screenshots trades (HTF/LTF) avec cache et download |
| `RichTextService.cs` | Sauvegarde/chargement RichTextBox en format XamlPackage (.etude) |
| `services/ReplayPositionStore.cs` | Persistance JSON de la dernière position de replay (timestamp UNIX) par stratégie+paire+timeframe, dans %LOCALAPPDATA%/DataEdge/replay_positions.json |
| `services/AgentStudiesService.cs` | Tools IA études : catalogue, lecture texte (sans images), recherche, création/écriture (markdown → .etude), suppression |
| `services/AgentWeeksService.cs` | Tools IA weeks : agent sur les notes hebdomadaires de la section Weeks (`Notes/Notes_yyyyMMdd.etude`) — catalogue, lecture, recherche, calendrier mensuel (semaines ISO + notes existantes), création/écriture (replace/append/prepend, images préservées, mise en forme Segoe UI 14 #EEEEEE) et suppression ; recharge la note affichée dans le dashboard si l'agent modifie la semaine affichée (`MainWindow.RefreshWeekNotesIfCurrent`) |
| `services/AgentMarketService.cs` | Tools IA « web & marché » : 7 nouveaux outils de données en direct — calendrier économique réel multi-sources (ForexFactory/TradingView), probabilités FedWatch CME, sentiment retail MyFxBook, cotations Yahoo Finance, actualités Google/Bing, recherche web Brave/Bing, lecture de page web — sans clé API obligatoire, cache mémoire mutualisé, `apikeys.json` optionnel dans %LOCALAPPDATA% |
| `App.xaml.cs` | Entry point, gestionnaire exceptions global, crash reporter |
| `backtesteur.xaml.cs` | Fenêtre backtest/replay (chart + stratégie) |

---

## Notes Techniques Importantes

1. **WebView2** : Utilise un hôte virtuel (`dataedge.local`) mappé vers le dossier `resources/chart/` pour charger la page HTML du graphique
2. **JSON vs Excel** : Migration complète Excel→JSON effectuée. Les données sont stockées en JSON uniquement
3. **Format .etude** : Extension propriétaire pour les notes/études. C'est en réalité un XamlPackage (format WPF)
4. **Sécurité** : Utilise un système de handshake avec le serveur (version check, locking à distance)
5. **RenderMode** : SoftwareOnly pour éviter les crashs GPU (problème fréquent en trading)
6. **Champs Dynamiques** : Les stratégies peuvent avoir des champs personnalisés (RSI, ZONE, etc.) qui génèrent automatiquement des colonnes et des stats

---

## Commandes de Build

```bash
# Restaurer les packages NuGet
nuget restore backtest.sln

# Build Debug
msbuild backtest.csproj /p:Configuration=Debug

# Build Release
msbuild backtest.csproj /p:Configuration=Release

## Agent IA

`MainWindow` héberge `FxAiChatControl` dans un **panneau latéral flottant** (`AiAgentDrawer`, superposé à droite, largeur 430 px agrandissable jusqu'à ~1000 px via le bouton ⤢ du chat — les tuiles de message s'adaptent à la largeur). Le déclencheur est un **bouton flottant néon statique discret** (46 px, icône sparkle, bas-droit du dashboard, aucune animation — l'app tourne en rendu logiciel) accessible depuis n'importe quelle vue ; l'ouverture/fermeture du panneau est **instantanée** et le contenu courant (dashboard, chart, journal) reste visible/actif derrière. Le client appelle `/api/ai/chat` avec le token cloud (JWT Bearer) et lit directement la réponse streamée du serveur ; pendant la réflexion du modèle et l'exécution des tools, un bandeau de statut live (raisonnement + actions d'outils, **sans chrono affiché**) s'affiche dans la bulle IA. En cas de saturation du fournisseur principal, le serveur bascule automatiquement sur la plateforme IA secondaire avant tout octet envoyé. Le backend utilisé est celui du dossier `fxglobal/` (pas de publisher Mercure dans cette version).

📄 **Voir le mapping complet et détaillé de l'agent IA (front + back) dans `mapia.md`.**

### Fichiers locaux de l'agent (lecture + création + dossiers)

L'agent peut **lire et créer des documents sur la machine** via 7 nouveaux tools (`AgentFileService.cs`) et des **boutons dédiés dans le chat** :

- **Dossier maître** : `Documents\DataEdge` — `Documents\` (fichiers créés par l'agent), `Rapports\` (PDF statistiques + PDF agent), `Imports\` (copies des pièces jointes du chat).
- **Lecture locale** (tools `read_local_file`, `list_local_folder`, `search_in_files`) : `.txt/.md/.json/.csv/.xml/.log`, `.pdf` (extraction texte **PdfPig**), `.xlsx/.xls` (**ExcelDataReader**), `.docx` (**OpenXML**). **Accès à tous les dossiers du PC de l'utilisateur courant** (Bureau, Documents, Téléchargements, Images, Vidéos, autres lecteurs…) dans la limite des permissions Windows — plus aucune restriction artificielle par racines.
- **Création de fichiers** (tools `create_text_file`/`create_pdf_file`/`create_word_file`, avec confirmation) : txt/json (JSON validé)/md, **PDF** au design DataEdge (`PdfExportService.ExportDocumentPdf`, refactor de l'export de statistiques), **Word .docx** (`WordDocumentBuilder`). Écritures confinées à `Documents\DataEdge`, jamais d'écrasement (suffixes `_2`, `_3`…).
- **Pièces jointes du chat** : bouton 📎 → copie dans `DataEdge\Imports` → l'agent lit le contenu en local (`read_local_file`) ; bouton 📂 dans l'en-tête → ouvre `Documents\DataEdge` dans l'Explorateur. Le champ de saisie s'auto-extend jusqu'à 200 px (gros collages) avec un avertissement au-delà de 20 000 caractères.
- **Sécurité** : `ResolvePath` (lecture) accepte tout chemin Windows valide — les refus constatés correspondent à de vraies permissions OS ; `ResolveWriteSubfolder` (écriture) confine les écritures à `Documents\DataEdge`, jamais d'écrasement (suffixes `_2`, `_3`…). Les erreurs d'accès réelles sont remontées telles quelles à l'agent. Licences gratuites en commercial : PdfPig (Apache 2.0), ExcelDataReader (MIT), OpenXML (MIT), QuestPDF (Community < 1 M$).

## Backend — `fxglobal/` (Symfony)

Le dossier `fxglobal/` contient l'intégralité du backend Symfony 6.4 : API REST JWT consommée par le client WPF (auth, cloud storage, handshake, données marché, agent IA), site web public (Twig) et back-office admin (`/admin`). Sa structure complète (10 contrôleurs, 7 entités, 4 migrations, stockage, sécurité) est cartographiée dans un fichier dédié.

📄 **Voir le mapping complet et détaillé du backend dans `fxglobal/mapbackend.md`.**