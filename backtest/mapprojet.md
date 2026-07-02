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
| **EPPlus** | Migration Excel → JSON |
| **Newtonsoft.Json + System.Text.Json** | Sérialisation JSON |
| **Xceed Extended WPF Toolkit** | Composants UI (AvalonDock) |
| **Pack : Extended.Wpf.Toolkit 4.6.1** | |

### Dépendances Principales (NuGet)
- `EPPlus 8.5.0` - Manipulation Excel
- `Microsoft.Web.WebView2 1.0.3912.50` - Navigateur intégré
- `OxyPlot.Wpf 2.0.0` - Graphiques statistiques
- `Newtonsoft.Json 13.0.3` - JSON
- `Extended.Wpf.Toolkit 4.6.1` - Composants UI

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
│   ├── FirstLaunchManager.cs → Détection premier lancement
│   ├── HabitsManager.cs   → Gestionnaire d'habitudes (sérialisation binaire)
│   └── NetworkUtils.cs    → Récupération indices Fear & Greed (API CNN + alternative.me)
│
├── 📄 Services
│   ├── services/
│   │   ├── FxCloudService.cs  → Service cloud (auth, sync, profil, support, crash reports)
│   │   ├── ChartBridge.cs     → Bridge C# ↔ JavaScript (WebView2 graphique)
│   │   └── Dataservice.cs     → Récupération données marché (API Symfony, cache CSV)
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
POST /api/cloud/sync-file                 → Upload fichier
POST /api/cloud/file-info                 → Vérification hash
GET  /api/cloud/list?app_id=...           → Liste fichiers distants
GET  /api/cloud/download?app_id=...       → Download fichier
GET  /api/public/data/fetch?pair=&tf=&year= → Données CSV historiques
GET  /api/public/data/pairs               → Liste paires disponibles
```

---

## Fichiers Clés & Leurs Rôles

| Fichier | Rôle Principal |
|---|---|
| `MainWindow.xaml.cs` | Hub central : dashboard, navigation, journal, notifications, Fear & Greed |
| `strategie.cs` | Modèle + logique métier : CRUD trades, calcul stats, migration Excel→JSON |
| `Chart.xaml.cs` | Graphique TradingView WebView2, gestion timeframes, watchlist, ajout trades |
| `FxCloudService.cs` | Service cloud : auth, sync, profil, handshake, crash reporting |
| `Dataservice.cs` | Récupération données marché depuis API Symfony, cache CSV local |
| `ChartBridge.cs` | Pont C#↔JS pour le WebView2 du graphique (dessins, captures) |
| `StatisticsControl.xaml.cs` | Contrôle statistiques d'une stratégie (DataGrid + vues) |
| `StatisticsView.xaml.cs` | Visualisation OxyPlot (courbe équité, winrate, sessions, audit) |
| `SettingsView.xaml.cs` | Gestion compte cloud, profil, paramètres app |
| `EtudesView.xaml.cs` | Éditeur d'études (format .etude, arborescence, formatage riche) |
| `NetworkUtils.cs` | Indices Fear & Greed (CNN US + alternative.me Crypto) |
| `HabitsManager.cs` | Gestionnaire d'habitudes quotidiennes (sérialisation binaire) |
| `TradeVisualizerControl.xaml.cs` | Affiche screenshots trades (HTF/LTF) avec cache et download |
| `RichTextService.cs` | Sauvegarde/chargement RichTextBox en format XamlPackage (.etude) |
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