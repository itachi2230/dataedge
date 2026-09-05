# 📈 DataEdge - Documentation Détaillée du Module Chart

## Introduction

Le module **Chart** de DataEdge est un composant WPF (`Chart.xaml` / `Chart.xaml.cs`) qui héberge un graphique boursier interactif via **WebView2** (Chromium), utilisant la bibliothèque **TradingView Lightweight Charts™**. Le graphique permet la visualisation de données de prix historiques, le dessin d'outils d'analyse technique, le backtesting/replay de trades, et la capture d'écrans.

---

## Architecture Globale

```
┌─────────────────────────────────────────────────────────┐
│                    C# / WPF Layer                        │
│  ┌─────────────────────────────────────────────────┐    │
│  │  Chart.xaml.cs                                   │    │
│  │  - Cycle de vie du WebView2                      │    │
│  │  - Gestion watchlist, timeframes, symboles        │    │
│  │  - Parsing CSV → CandleModel                     │    │
│  │  - Formulaire ajout de trade                     │    │
│  │  - Navigation replay sortie                      │    │
│  └──────────────┬──────────────────────────────────┘    │
│                 │ AddHostObjectToScript                  │
│                 ▼                                        │
│  ┌─────────────────────────────────────────────────┐    │
│  │  services/ChartBridge.cs                         │    │
│  │  Pont C# ↔ JavaScript (ComVisible)               │    │
│  │  - OnSetupCreated() → draw                        │    │
│  │  - OnTradeSetupCompleted() → form populating      │    │
│  │  - SaveChartScreenshot() → image saving           │    │
│  │  - LoadYearForBacktest() → year jump              │    │
│  │  - loadPreviousYear() / loadNextYear()            │    │
│  │  - ExitReplayMode() → back to present             │    │
│  └──────────────┬──────────────────────────────────┘    │
└─────────────────┼───────────────────────────────────────┘
                  │ chrome.webview.hostObjects.chartService
                  ▼
┌─────────────────────────────────────────────────────────┐
│                   HTML / JavaScript Layer                │
│  ┌─────────────────────────────────────────────────┐    │
│  │  index.html                                      │    │
│  │  - Toolbar (theme, grid, replay)                 │    │
│  │  - Sidebar (outils de dessin)                    │    │
│  │  - Chart container                               │    │
│  │  - Load JS files in order                        │    │
│  └─────────────────────────────────────────────────┘    │
│                                                          │
│  ┌─────────────────────────────────────────────────┐    │
│  │  lightweight-charts.js (TradingView Library)     │    │
│  │  - Fonctions : createChart, addCandlestickSeries │    │
│  │  - API : subscribeClick, timeScale, series       │    │
│  └─────────────────────────────────────────────────┘    │
│                                                          │
│  ┌─────────────────────────────────────────────────┐    │
│  │  chart_engine.js (Moteur principal)              │    │
│  │  - initChart() → création chart + series         │    │
│  │  - updateChartData() → setData avec extended TL │    │
│  │  - getReplayPosition()/getVisibleCenterTime()   │    │
│  │  - Replay state machine (play/pause/step/jump)   │    │
│  │  - setupLazyLoading → chargement infini          │    │
│  │  - captureChart() → screenshot → C#              │    │
│  │  - getExtendedTimeline() → bougies fantômes      │    │
│  │  - ensureLeftContext/mergePrecedingData →       │    │
│  │      contexte gauche replay (année précédente)   │    │
│  │  - Gestion thème, grille, échelle (P/L/A)        │    │
│  └─────────────────────────────────────────────────┘    │
│                                                          │
│  ┌─────────────────────────────────────────────────┐    │
│  │  drawing_manager.js (Contrôleur dessins)         │    │
│  │  - DrawingManager : mode, points[], drawings[]   │    │
│  │  - setMode(id) → active/désactive outil          │    │
│  │  - addPoint(time,price) → ajoute point + finish  │    │
│  │  - finishDrawing() → calcule points finaux + save│    │
│  │  - syncDrawingWithChart() → events chart/souris  │    │
│  │  - Sélection, déplacement, redimensionnement     │    │
│  │  - Sauvegarde localStorage (Drawings_{symbol})   │    │
│  │  - editText() / editFibo() → éditeurs visuels    │    │
│  └─────────────────────────────────────────────────┘    │
│                                                          │
│  ┌─────────────────────────────────────────────────┐    │
│  │  drawing_configs.js (Définitions des outils)    │    │
│  │  - window.DrawingConfigs                        │    │
│  │  - Chaque config : { clicks, render, preview }  │    │
│  │  - 12 outils définis (voir section ci-dessous)  │    │
│  └─────────────────────────────────────────────────┘    │
│                                                          │
│  ┌─────────────────────────────────────────────────┐    │
│  │  drawing_plugin.js (Rendu Canvas)               │    │
│  │  - DrawingPlugin class (ISeriesPrimitive)       │    │
│  │  - paneViews() → renderer → draw()              │    │
│  │  - _resolveX() → snap temporel robuste          │    │
│  │  - _shouldRender() → filtre visibilité          │    │
│  │  - _renderPreviews() → preview pendant dessin   │    │
│  │  - Gestion des ancres de sélection              │    │
│  └─────────────────────────────────────────────────┘    │
│                                                          │
│  ┌─────────────────────────────────────────────────┐    │
│  │  drawing_utils.js (Utilitaires dessin)          │    │
│  │  - isOverPoint() → hit test ancre               │    │
│  │  - getDistanceToSegment() → distance point→ligne │    │
│  │  - updatePreview() → SVG preview overlay        │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

---

## Flux de Données et Initialisation

### 1. Initialisation du Graphique

```
Chart.xaml.cs                          chart_engine.js
    │                                        │
    │  InitBrowser()                          │
    │  ├── EnsureCoreWebView2Async()          │
    │  ├── VirtualHost (dataedge.local)       │
    │  ├── AddHostObjectToScript (bridge)     │
    │  └── Navigate → index.html              │
    │                                        │
    │                                        │  DOMContentLoaded
    │                                        │  → initChart()
    │                                        │    ├── createChart(container, opts)
    │                                        │    ├── addCandlestickSeries()
    │                                        │    ├── DrawingManager.init(chart, series)
    │                                        │    ├── syncDrawingWithChart()
    │                                        │    └── setupLazyLoading()
    │                                        │
    │  NavigationCompleted                    │
    │  → LoadBacktestData()                   │
    │    ├── GetMarketDataAsync() ──API──►    │
    │    ├── ParseCsvToCandles()              │
    │    └── ExecuteScriptAsync(              │
    │        updateChartData(json, symbol[,  │
    │        focusTime]))                     │
    │                                        │  updateChartData(data, symbol, focusTime|null)
    │                                        │  ├── candleSeries.setData(extended)
    │                                        │  ├── replayState.allData = data
    │                                        │  ├── replayState.endReached = false
    │                                        │  └── timeScale.setVisibleLogicalRange()
    │                                        │      ├── Repère leftPhantoms=200 (1re bougie réelle)
    │                                        │      ├── focusTime → from:200+centerIdx-150 to:200+centerIdx+50
    │                                        │      └── sinon → from:200+dataLen-150 to:200+dataLen+50
```

### 2. Flux de Changement de Timeframe / Symbole

```
Timeframe_Click / Watchlist_SelectionChanged
    │
    ├── _ctsGlobal?.Cancel()      ← Annule requête précédente
    ├── _ctsGlobal = new CTS()
    ├── _currentTF / _currentSymbol mis à jour
    │
    ├── Timeframe_Click uniquement :
    │   └── Interroge le JS (getReplayPosition() / getVisibleCenterTime())
    │        ├── Replay actif + position connue
    │        │   → _currentYear = année de la position  ← LE BON FICHIER
    │        │   → focusTime = position replay (recalage exact)
    │        └── Mode normal + centre visible
    │            → _currentYear = année du centre       ← CORRIGÉ (était oublié → fichier erroné)
    │            → focusTime = centre (conserve la vue si couverte par le fichier)
    │
    └── LoadBacktestData(token, focusTime?)
         └── updateChartData(json, symbol, focusTime)

RÉGLE ANTI-REBOND : l'année du fichier à charger est décidée par la POSITION
(timestamp) courante côté JS, plus jamais par le simple _currentYear qui pouvait
être périmé après un replay avancé sur plusieurs années.
```

### 3. Flux de Chargement Infini (Lazy Loading)

```
chart_engine.js : subscribeVisibleTimeRangeChange
    │
    │  Quand range.from ≤ data[3].time :
    │  → bridge.loadPreviousYear(data[3].time)
    │  (ignoré si replayState.endReached === true)
    │
    ▼
ChartBridge.cs → loadPreviousYear(long timestamp)
    │
    ▼
Chart.xaml.cs → LoadMoreData(timestamp, isPrevious=true)
    ├── Détermine année cible (GetFileToRequest)
    ├── _currentYear = année réellement chargée (targetYear)
    ├── GetMarketDataAsync()
    ├── ParseCsvToCandles()
    └── ExecuteScriptAsync(prependChartData(json))
         └── Anti-rebond : si uniqueMap.size === currentData.length
             (même bloc 4h/D redemandé) → endReached = true, pas de setData
```

La fin d'historique est détectée des DEUX côtés (avant et après) :
- Côté C# : `_endOfDataReached = true` si fichier introuvable / vide (les deux directions)
- Côté JS : `replayState.endReached = true` si `appendOrPrependData` / `prependChartData`
  ne produisent aucune nouvelle bougie

### 4. Préchargement du Contexte Gauche (Replay)

Quand le replay démarre en début d'année (ou après un changement de timeframe),
le buffer ne contient que l'année de la position : il y a du vide à gauche tant
que l'année précédente n'est pas chargée. Un **préchargement silencieux** est
déclenché en arrière-plan pour combler ce vide SANS latence (l'année cible
s'affiche immédiatement, l'année précédente arrive ensuite sans saut visuel).

```
chart_engine.js → ensureLeftContext()
    │  (déclenché via scheduleLeftContextCheck() après updateChartData /
    │   applyJump / toggleReplayUI / stepReplay — anti-rebond ~700 ms)
    │
    ├── Buffer commence dans la MÊME année que la position replay
    │   → bridge.LoadPreviousYearForReplay(firstCandle.time)
    │
    └── Replay en pause + utilisateur scrollé au bord gauche du buffer
        → idem (cooldown 4 s pour ne pas chaîner sans limite)

▼
ChartBridge.cs → LoadPreviousYearForReplay(long timestamp)

▼
Chart.xaml.cs → LoadPreviousYearForReplay(timestamp)
    ├── Ignore les TF blocs (w / m / 4h / d : déjà 10 ans en mémoire)
    ├── _isPrefetching (verrou dédié, silencieux : aucun ToggleLoader)
    ├── GetMarketDataAsync(année − 1)  (cache local → API)
    └── ExecuteScriptAsync(mergePrecedingData(json, symbol))

▼
chart_engine.js → mergePrecedingData(newData, symbol)
    ├── Fusionne UNIQUEMENT des bougies antérieures (Map + tri, dédoublonnage)
    ├── Recale replayState.currentIndex sur la MÊME bougie (delta = nb ajouté)
    └── Préserve la vue : setVisibleLogicalRange(from + delta, to + delta)
          → aucun saut visuel, position replay intacte

Drapeaux dédiés (côté JS) :
- window._leftContextLoading : préchargement en cours
- window._leftEndReached     : aucune année antérieure disponible (stoppe les retries)
- resetLeftContextState()    : réarmé à chaque nouveau chargement d'année/TF
```

---

## Les Outils de Dessin (Drawing Tools)

### Architecture

Chaque outil de dessin est défini par un objet de configuration dans `window.DrawingConfigs` :

```javascript
window.DrawingConfigs = {
    'tool_id': {
        clicks: Number,       // Nombre de clics avant finalisation (Infinity pour path)
        render: Function,     // Fonction de rendu Canvas (ctx, ...coords, width, height, isSelected, drawingObj)
        fill: Boolean,        // Optionnel : remplissage
        preview: Function     // Optionnel : prévisualisation SVG
    }
};
```

### Liste des Outils

| ID | Nom | Clics | Description | Points finaux |
|---|---|---|---|---|
| `trendline` | Ligne de tendance | 2 | Ligne droite entre 2 points | 2 |
| `rectangle` | Rectangle | 2 | Rectangle entre 2 coins | 2 |
| `long_pos` | Position Longue | 1 | Entry + TP/SL auto (20% largeur, 12% hauteur) | 3 (entry, TP, SL) |
| `short_pos` | Position Courte | 1 | Entry + TP/SL auto (20% largeur, 12% hauteur) | 3 (entry, TP, SL) |
| `curve` | Courbe quadratique | 3 | Arc de Bézier avec point de contrôle | 3 |
| `text` | Texte | 1 | Label cliquable éditable | 1 |
| `horz_ray` | Rayon Horizontal | 1 | Demi-droite horizontale vers la droite | 1 |
| `horz_line` | Ligne Horizontale | 1 | Ligne horizontale infinie | 1 |
| `vert_line` | Ligne Verticale | 1 | Ligne verticale infinie | 1 |
| `path` | Chemin libre | ∞ | Polyligne (double-clic pour finir) | Variable |
| `arrow` | Flèche | 2 | Ligne avec pointe de flèche | 2 |
| `fibo` | Retracement Fibonacci | 2 | Niveaux de retracement configurables | 2 |

### Cycle de Vie d'un Dessin

```
1. ACTIVATION
   User clique sur bouton sidebar
   → DrawingManager.setMode('tool_id')
     ├── mode = 'tool_id' (toggle si déjà actif)
     ├── points = [] (vide)
     ├── curseur = crosshair
     └── updateSidebarUI()

2. ADDITION DE POINTS (1 clic = 1 point)
   User clique sur le graphique
   → chart.subscribeClick(param)
     ├── param.time, param.point.y → price
     └── DrawingManager.addPoint(time, price)
           ├── points.push({ time, price })
           ├── series.applyOptions({}) → rafraîchit rendu
           └── SI points.length === conf.clicks
                 → finishDrawing()

3. PREVIEW PENDANT LE DESSIN
   Pendant le déplacement de la souris (entre les clics) :
   → mousemove event
     ├── Résout position souris en temps/prix
     └── DrawingUtils.updatePreview(mode, p1, mousePos)
           ├── Crée SVG overlay (#drawing-svg)
           └── Appelle conf.preview() pour générer le SVG

4. FINALISATION
   finishDrawing()
    ├── Pour long_pos/short_pos :
    │     Calcule TP/SL automatiques (20% largeur, 12% hauteur)
    ├── Pour fibo :
    │     Sauvegarde + ouvre éditeur visuel
    ├── Pour text :
    │     Sauvegarde + ouvre éditeur de texte
    ├── Pour path :
    │     Terminé via double-clic (dblclick event)
    ├── newDrawing = { data: { type, points }, id: Date.now() }
    ├── drawings.push(newDrawing)
    ├── save() → localStorage('Drawings_{symbol}')
    └── setMode(null) → reset

5. SAUVEGARDE
   save()
   → localStorage.setItem('Drawings_' + window.currentSymbol, JSON.stringify(drawings))

6. CHARGEMENT
   load()
   → localStorage.getItem('Drawings_' + window.currentSymbol)
   → Parse JSON → drawings[]
   → series.applyOptions({}) → force rendu plugin

7. SÉLECTION (mode normal, pas de dessin en cours)
   User clique sur le graphique
   → subscribeClick() sans mode de dessin
     ├── Calcule hit-test sur tous les dessins
     │   (ancres, bords, zone selon type)
     ├── found = index du dessin cliqué (ou null)
     └── selectedIdx = found
         → Affiche ancres de contrôle

8. DÉPLACEMENT / REDIMENSIONNEMENT
   mousedown sur dessin sélectionné :
   ├── Si clic sur ancre → dragState = { type: 'resize', index }
   └── Sinon → dragState = { type: 'move' }
   
   mousemove :
   ├── resize : modifie time/price du point concerné
   └── move : décale tous les points
   
   mouseup :
   ├── dragState = null
   └── save()

9. SUPPRESSION
   Touche Delete → deleteSelected()
   ├── Supprime drawings[selectedIdx]
   ├── Nettoie lastActiveSetup si correspond
   └── save()
   
   Bouton "Tout supprimer" → clearAllDrawings()
```

### Détail : Position Longue / Courte (long_pos / short_pos)

Les positions BUY/SELL sont des outils à 1 clic qui génèrent automatiquement 3 points :

```javascript
// Paramètres automatiques :
// - LARGEUR : 20% des bougies visibles (horizon temporel)
// - HAUTEUR TP/SL : 6% de la plage de prix visible chacun (12% total)

// LONG (BUY)
finalPoints = [
    { time: clickTime, price: entryPrice },         // Point d'entrée
    { time: futureTime, price: entryPrice + 6% },   // TP (au-dessus)
    { time: futureTime, price: entryPrice - 6% }    // SL (en-dessous)
];

// SHORT (SELL)
finalPoints = [
    { time: clickTime, price: entryPrice },          // Point d'entrée
    { time: futureTime, price: entryPrice - 6% },   // TP (en-dessous)
    { time: futureTime, price: entryPrice + 6% }    // SL (au-dessus)
];
```

**Backtesting automatique** : Pendant le mode Replay, `checkLastSetupStatus()` surveille chaque bougie pour détecter si le TP ou SL est touché, et déclenche automatiquement `sendTradeToCSharp()` qui peuple le formulaire via `OnTradeSetupCompleted()`.

### Détail : Chemin (path)

- `clicks: Infinity` → ne se finalise jamais automatiquement
- Finalisation via **double-clic** sur le graphique
- Chaque clic ajoute un segment
- Le dernier segment a une flèche directionnelle
- Prévisualisation : segment en pointillé entre dernier point et souris

### Détail : Fibonacci (fibo)

- 2 clics (point bas → point haut ou inversement)
- Ouvre un éditeur flottant pour configurer :
  - Niveaux affichés (0, 0.236, 0.382, 0.5, 0.618, 0.786, 1)
  - Remplissage ON/OFF
- Diagonale de contrôle en pointillé
- Prix calculés dynamiquement à chaque niveau

### Détail : Texte (text)

- 1 clic → place le label
- Ouvre éditeur input flottant
- Validation : Enter ou perte de focus
- Annulation : Escape

---

## Le Plugin de Rendu (DrawingPlugin)

`drawing_plugin.js` implémente l'interface `ISeriesPrimitive` de Lightweight Charts.

### Méthodes Clés

| Méthode | Rôle |
|---|---|
| `paneViews()` | Retourne le renderer avec la méthode `draw()` |
| `_resolveX(time, timeScale, step)` | Convertit timestamp → coordonnée X avec snap temporel |
| `_shouldRender(coords, type)` | Filtre les dessins hors écran |
| `_pointToCoord(p, timeScale, step, width)` | Convertit {time, price} → {x, y, text} |
| `_getCurrentStep(timeScale)` | Calcule le pas temporel (cache 2s) |
| `_renderPreviews(ctx, timeScale, step)` | Dessine la preview du dessin en cours |

### Algorithme de Résolution Temporelle (_resolveX)

```
1. Tentative directe : timeScale.timeToCoordinate(time)
2. Snap au multiple de step le plus proche (±3 steps)
3. Recherche binaire dans les données de la série (±3 bougies)
4. Retourne null si introuvable
```

---

## Le Mode Replay

### Architecture

```javascript
window.replayState = {
    isActive: false,        // Mode replay activé
    isPlaying: false,       // Lecture automatique
    currentIndex: 0,        // Index bougie courante
    speed: 1,               // Vitesse (1, 5, 10)
    allData: [],            // Toutes les bougies chargées
    endReached: false       // Fin d'historique (anti rechargement en boucle)
};
```

### Helpers de Position (utilisés pour le changement de TF)

```javascript
window.getReplayPosition()
  ├── null si replay inactif / buffer vide / index hors bornes
  └── timestamp (seconds) de la bougie courante du replay

window.getVisibleCenterTime()
  ├── null si pas de chart / pas de range visible
  └── timestamp (seconds) du centre de la vue (peut être une bougie fantôme)

Timeframe_Click (C#) interroge ces 2 helpers + replayState.isActive
  └── via un seul ExecuteScriptAsync retournant { replay, center, active }
```

### Fonctionnalités

- **Dashboard flottant** avec date picker, play/pause, step, speed selector
- **Draggable** via la poignée en haut à gauche
- **Jump to date** : saute à une date spécifique (charge l'année si nécessaire)
- **Lecture automatique** : boucle avec intervalle de 500ms × speed
- **Auto-fermeture des positions** : détection TP/SL pendant le replay
- **Retour au temps réel** via le bouton ✖ → `ExitReplayAndGoToPresent()`

### Flux du Replay

```
toggleReplayUI()
  ├── Si dashboard existe → le supprime + désactive replay
  └── Sinon →
      ├── Aligne currentIndex sur le centre visible
      │   (findIndex ≥ getVisibleCenterTime, sinon dernière bougie)
      ├── endReached = false
      └── Crée dashboard + pré-remplit replay-date-input avec la position

stepReplay(direction)
  ├── currentIndex += speed * direction (borné [0, allData.length-1])
  ├── partialData = allData.slice(0, currentIndex + 1)
  └── candleSeries.setData(getExtendedTimeline(partialData))

runReplayLoop()
  ├── Si endReached && currentIndex ⩾ allData.length-1
  │   → isPlaying = false (arrêt propre, plus de boucle de rechargement)
  ├── Si à -50 bougies de la fin (et pas endReached)
  │   → bridge.loadNextYear(lastCandle.time)
  ├── stepReplay(1)
  ├── checkLastSetupStatus(currentCandle)
  │     (vérifie TP/SL pour lastActiveSetup)
  └── setTimeout(runReplayLoop, 500)

jumpToReplayDate()
  ├── findIndex(d.time ≥ dateInput)
  ├── index === -1 → LoadYearForBacktest(targetYear)   ← uniquement si hors données
  └── sinon applyJump(index, dateInput)

applyJump(index, dateText)
  ├── currentIndex = index
  ├── candleSeries.setData(extended history)
  └── setVisibleLogicalRange(from:200+index-75, to:200+index+25)  ← CENTRÉ (plus de scrollToPosition(0))
```

---

## Le Pont C# ↔ JavaScript (ChartBridge)

ChartBridge est un objet COM exposé au WebView2 via `AddHostObjectToScript("chartService", bridge)`.

### Méthodes Disponibles dans JS

| Méthode JS | Méthode C# | Description |
|---|---|---|
| `bridge.OnSetupCreated(json)` | `OnSetupCreated(string)` | (Logique dessin) |
| `bridge.LoadYearForBacktest(year)` | `LoadYearForBacktest(int)` | Charge année spécifique pour replay |
| `bridge.loadPreviousYear(timestamp)` | `loadPreviousYear(long)` | Charge année précédente (lazy load) |
| `bridge.loadNextYear(timestamp)` | `loadNextYear(long)` | Charge année suivante |
| `bridge.LoadPreviousYearForReplay(timestamp)` | `LoadPreviousYearForReplay(long)` | Précharge silencieusement l'année précédente (replay, TF annuels) |
| `bridge.ExitReplayMode()` | `ExitReplayMode()` | Sort du mode replay |
| `bridge.OnTradeSetupCompleted(json)` | `OnTradeSetupCompleted(string)` | Trade détecté → peuple formulaire |
| `bridge.SaveChartScreenshot(type, base64)` | `SaveChartScreenshot(string, string)` | Sauvegarde screenshot PNG |

---

## Gestion des Timestamps et des Données

### Structuration des Fichiers CSV

Les données historiques sont partitionnées par timeframe et année :

```
chart/historical/{SYMBOL}_{TF}_{BLOK}.csv

Exemples :
  EURUSD_15m_2025.csv       → Intraday, par année
  EURUSD_4h_2026.csv        → 4h/Daily, par bloc de 10 ans
  EURUSD_1h_2025.csv        → 1h, par année
```

### Règle de Partitionnement (GetFileToRequest)

| Timeframe | Règle |
|---|---|
| `1m`, `5m`, `15m`, `30m`, `1h` | Fichier par année exacte |
| `4h`, `D` | Blocs 2006-2016 = "2016", 2017-2026 = "2026" |
| `W`, `M` | Toujours "2026" (tout dans un fichier) |

### Algorithme d'Extension de Timeline

`getExtendedTimeline()` ajoute des bougies "fantômes" avant et après les données réelles :

- **200 bougies avant** (basées sur l'intervalle médian 1er quartile)
- **2000 bougies après** (même intervalle)
- Permet un défilement fluide sans bordure visible
- Intervalle calculé sur les 20 premières bougies réelles (médiane du 1er quartile)

---

## Capture d'Écran

```javascript
window.captureChart = function(type) {
    const canvas = window.chart.takeScreenshot();
    const dataURL = canvas.toDataURL("image/png");
    const bridge = window.chrome.webview.hostObjects.chartService;
    bridge.SaveChartScreenshot(type, dataURL);
};
```

- Appelée depuis C# via `SafeExecuteJs("captureChart('HTF')")`
- Type : "HTF" (Higher Time Frame) ou "LTF" (Lower Time Frame)
- Image sauvegardée dans `cacheimage/{timestamp}_{TYPE}.png`
- Chemin stocké dans `_imageHtf` / `_imageLtf` pour le formulaire trade

---

## Formulaire d'Ajout de Trade (Intégré)

Le graphique inclut un formulaire complet d'ajout de trade :

```
TradeForm (dans Chart.xaml)
  ├── Paire (auto-rempli depuis watchlist)
  ├── Type d'ordre (BUY/SELL)
  ├── Résultat (TP/SL/BE/TR/PARTIAL)
  ├── Prix entrée / sortie
  ├── RR (Risk/Reward)
  ├── Profit
  ├── Dates (entrée + sortie avec time pickers)
  ├── Champs dynamiques (configurés par stratégie)
  ├── Description
  ├── Statuts captures (HTF/LTF)
  └── Boutons : Sauvegarder / Annuler
```

---

## Gestion des Conflits et Cas Particuliers

### Double-Clic vs Dessin

Le double-clic a plusieurs comportements selon le contexte :
1. **Mode path/polyline actif** → finalise le dessin
2. **Dessin sélectionné** → (réservé pour future édition)
3. **Mode replay actif** → jump à la bougie cliquée + pause du play

### Annulation

- **Escape** : Désactive le mode dessin courant, désélectionne
- **Delete** : Supprime le dessin sélectionné
- **Click hors dessin** : Désélectionne (mode normal)

### DragState et Verrouillage

Pendant le déplacement/redimensionnement :
- `handleScroll: false`, `handleScale: false` → empêche l'interaction avec le graphique
- Restauré à `true` dans `mouseup`

---

## Erreurs Connues et Résolution

### Bug : Saut du graphique au changement de Timeframe (replay / année défilée)

**Symptôme** : En replay H4 (bloc 10 ans en mémoire), passer à un TF inférieur (H1, M15) faisait sauter le graphique à l'endroit où le backtest avait commencé (ou en fin de l'année précédente après avoir défilé plusieurs années).

**Cause racine** : Le fichier à charger était choisi par `_currentYear`, qui pouvait être périmé :
- sur un replay 4h (bloc complet), aucun `LoadYearForBacktest` n'était déclenché → `_currentYear` restait sur l'année courante de démarrage ;
- sur un replay 1h avançant d'année en année, `_currentYear` n'était jamais mis à jour vers l'année réellement chargée ;
- côté JS, `findIndex(d.time >= currentTime)` retournait -1 et tombait en fallback aveugle sur `data.length - 1`.

**Solution** :
1. `Timeframe_Click` interroge maintenant le JS (`getReplayPosition()` / `getVisibleCenterTime()`) AVANT de charger :
   - en replay, `_currentYear` est mis à jour à l'année de la position → le bon fichier du nouveau TF est chargé ;
   - le `focusTime` est transmis à `updateChartData(data, symbol, focusTime)` pour recalibrer la vue.
2. `LoadMoreData` met à jour `_currentYear` vers l'année réellement chargée (`targetYear`).
3. `updateChartData` : calage au plus proche (0 si antérieur, fin si postérieur) au lieu du fallback aveugle.
4. `setupBacktestData` : ne se cale plus jamais sur `applyJump(0)` par défaut (dernière bougie si pas de date cible).

### Bug : Centrage cassé au changement de Timeframe et au Jump (non-replay + replay)

**Symptôme** : 
- Changement de timeframe en mode normal → la chart se retrouve collée à gauche (vue décentrée, proportions mauvaises).
- Jump dans le backtest (saut à une date) → la chart se retrouve collée à droite, obligeant un drag manuel pour recentrer.

**Cause racine** (2 bugs distincts) :

1. **`updateChartData` mode non-replay** : La variable `realBase` était calculée comme `timeline.length - 2000` (= 200 + realData.length, index du 1er fantôme droit) au lieu de `200` (index de la 1re bougie réelle après les 200 fantômes gauches). Le `setVisibleLogicalRange({ from: realBase + centerIdx - 150, ... })` ciblait alors une position bien trop à droite dans le timeline étendu, produisant un centrage aléatoire.

2. **`applyJump` et mode replay de `updateChartData`** : `window.chart.timeScale().scrollToPosition(0, false)` ne centre PAS la vue — elle colle la chart à la bordure droite (position 0 = extrême droite du timeline).

**Solution** :
1. Remplacer `realBase = timeline.length - 2000` par une constante `leftPhantoms = 200` (le nombre exact de bougies fantômes insérées avant les données réelles par `getExtendedTimeline`).
2. Remplacer `scrollToPosition(0, false)` par `setVisibleLogicalRange` avec les valeurs :
   - `from: leftPhantoms + index - 75` (75 bougies avant la position)
   - `to: leftPhantoms + index + 25` (25 bougies après) → 100 bougies visibles, centrage stable.
3. La même correction s'applique au mode replay de `updateChartData` (timeframe change en replay) et à `applyJump` (jump en backtest).

### Bug : Centrage cassé au changement de Timeframe en mode normal (non-replay)

**Symptôme** : En changeant de timeframe en mode normal, la chart se retrouvait collée à gauche (premières bougies visibles, proportions inutilisables), ou la dernière bougie était trop à droite.

**Cause racine** (2 problèmes) :

1. **`_currentYear` non mis à jour** : `focusTime` était mis à jour mais pas `_currentYear`. Si le centre visible était en 2023 mais que `_currentYear` valait 2024, le fichier 2024 du nouveau TF était chargé → le focusTime (2023) était avant la première bougie → `centerIdx = 0` → chart collée à gauche.

2. **`focusTime` préservé** : Le focusTime du centre visible de l'ancien TF était transmis à `updateChartData`, qui centrait sur cette position passée. Si l'utilisateur regardait des données historiques, le nouveau TF centrait sur le passé → la dernière bougie (la plus récente) devenait invisible ou trop à droite.

**Solution** :
1. Mettre à jour `_currentYear` dans le bloc non-replay (pour charger le bon fichier).
2. **Ne pas transmettre `focusTime`** en mode normal. Le JS centre alors sur la dernière bougie des nouvelles données, exactement comme au changement de paire où ça fonctionne parfaitement.
```csharp
_currentYear = DateTimeOffset.FromUnixTimeSeconds(pos.center.Value).DateTime.Year;
// focusTime volontairement NON transmis : centrage sur la dernière bougie (comportement paire).
```

### Optimisation : Exclure 1m et 5m du préchargement de contexte gauche (replay)

**Symptôme** : Au démarrage du replay ou après un changement de timeframe en replay 1m/5m, le téléchargement de l'année précédente prenait trop de temps (~60 000 bougies/fichier).

**Solution** : Ajouter `"1m"` et `"5m"` à la liste d'exclusion dans `LoadPreviousYearForReplay` côté C#. Les timeframes à fichier bloc (w/m/4h/d) étaient déjà exclus. Le buffer d'une année de M1/M5 est déjà suffisamment dense pour ne pas nécessiter de préchargement gauche.

### Bug : Rechargements en boucle en fin d'historique (4h / défilement gauche)

**Symptôme** : À la fin d'un replay 4h ou en touchant le bord gauche d'un bloc 4h, le même fichier était re-téléchargé continuellement (boucle de `loadNextYear` toutes les 500 ms / micro-sauts à gauche).

**Cause racine** : Aucune détection de non-croissance : `appendOrPrependData` et `prependChartData` re-rendaient les mêmes données sans jamais signaler la fin d'historique ; `_endOfDataReached` n'était posé que pour la direction arrière.

**Solution** : Détection dans `appendOrPrependData` (replay) et `prependChartData` (normal) :
- `replayState.endReached = true` quand aucune bougie nouvelle n'est ajoutée ;
- `runReplayLoop` arrête proprement le play à la fin ;
- `setupLazyLoading` ignore les déclenchements quand `endReached` ;
- côté C# `_endOfDataReached` est posé dans les deux directions.

### Bug : Démarrage du replay au tout début du buffer

**Symptôme** : Activer le replay puis appuyer sur ▶ ramenait le graphique aux 1-2 premières bougies du buffer (index 0).

**Cause racine** : `currentIndex` n'était jamais aligné sur la bougie visible à l'activation du replay.

**Solution** : `toggleReplayUI()` aligne `currentIndex` sur le centre visible (via `getVisibleCenterTime()`) ou la dernière bougie, et pré-remplit la date du dashboard.

**Symptôme** : Après sélection d'un outil à 1 clic (long_pos, short_pos, horz_line, etc.), cliquer sur le graphique ne place pas le dessin. Le mode reste actif et chaque clic ajoute un point sans jamais finaliser.

**Cause racine** : Dans `drawing_manager.js`, la fonction `finishDrawing()` (méthode de `DrawingManager`) appelle `resolveX(p.time)` à la ligne 61 pour calculer la position en pixels du point d'entrée lors du dessin des positions long/short. Or, `resolveX` était définie comme une **fonction locale** à l'intérieur de `syncDrawingWithChart()`, et n'était **pas accessible** depuis le scope de `DrawingManager.finishDrawing()`. Cela provoquait une `ReferenceError: resolveX is not defined`. L'exception JavaScript interrompait l'exécution avant que `this.setMode(null)` ne soit appelé, laissant le mode dessin indéfiniment actif. Les clics suivants ajoutaient des points sans jamais finaliser, imitant le comportement de l'outil `path` (qui a `clicks: Infinity`).

**Solution** : La fonction `resolveX` a été extraite et placée **globalement** comme `window.resolveChartX`. Elle est maintenant accessible depuis `DrawingManager.finishDrawing()` (qui l'appelle via `window.resolveChartX()`) et depuis `syncDrawingWithChart()` (qui crée un alias local `const resolveX = window.resolveChartX` pour ne pas casser les closures existantes). De plus, le bloc `else if (type === 'fibo')` a été sorti du bloc `if (type === 'long_pos' || type === 'short_pos')` où il était incorrectement imbriqué, ce qui empêchait le dessin Fibo de fonctionner correctement.

---

## Fichiers du Module Chart

| Fichier | Rôle | Langage |
|---|---|---|
| `Chart.xaml` | UI WPF du graphique | XAML |
| `Chart.xaml.cs` | Logique C# (WebView2, data, trade form) | C# |
| `services/ChartBridge.cs` | Pont C# ↔ JS | C# |
| `services/Dataservice.cs` | Récupération données marché | C# |
| `resources/chart/index.html` | Page HTML principale | HTML |
| `resources/chart/chart_engine.js` | Moteur graphique principal | JavaScript |
| `resources/chart/drawing_manager.js` | Gestionnaire de dessins | JavaScript |
| `resources/chart/drawing_configs.js` | Définitions des outils | JavaScript |
| `resources/chart/drawing_plugin.js` | Plugin de rendu Canvas | JavaScript |
| `resources/chart/drawing_utils.js` | Utilitaires hit-test/preview | JavaScript |
| `resources/chart/lightweight-charts.js` | Librairie TradingView | JavaScript |
| `resources/chart/style.css` | Styles généraux | CSS |
| `resources/chart/toolbar.css` | Styles toolbar/sidebar | CSS |
| `resources/chart/replay.css` | Styles mode replay | CSS |