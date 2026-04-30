window.chart = null;
window.candleSeries = null;
window.isDarkMode = true;
window.isGridVisible = true;
window.isProcessingData = false;
window.currentSymbol = "Default";

const themes = {
    dark: { bg: '#131722', text: '#d1d4dc', grid: '#2a2e39', up: '#00FFFF', down: '#FF007F' },
    light: { bg: '#ffffff', text: '#131722', grid: '#f0f3fa', up: '#26a69a', down: '#ef5350' }
};
const PriceScaleMode = {
    Normal: 0,
    Logarithmic: 1, // Note: Dans les versions récentes, c'est une option booléenne séparée
    Percentage: 2,
    IndexedTo100: 3,
};

window.cyberLog = function(msg, isError = false) {
    const el = document.getElementById('debug-console');
    if(!el) return;
    const time = new Date().toLocaleTimeString();
    const style = isError ? "color:#ff4d4d; font-weight:bold;" : "color:#00FFFF;";
    el.innerHTML = `<div style="${style}">[${time}] ${msg}</div>` + el.innerHTML;
};

window.initChart = function() {
    if (window.chart) return;
    const container = document.getElementById('chart-container');

    window.chart = LightweightCharts.createChart(container, {
        layout: { 
            background: { type: 'solid', color: themes.dark.bg }, 
            textColor: themes.dark.text 
        },
        grid: { 
            vertLines: { color: themes.dark.grid, visible: window.isGridVisible }, 
            horzLines: { color: themes.dark.grid, visible: window.isGridVisible } 
        },
       crosshair: {
            // Mode Normal = le curseur ne colle pas aux bougies
            mode: LightweightCharts.CrosshairMode.Normal, 
            
            // Tu peux aussi personnaliser le style des lignes ici
            vertLine: {
                width: 1,
                color: '#758696',
                style: 3, // Pointillés
                labelBackgroundColor: '#131722',
            },
            horzLine: {
                width: 1,
                color: '#758696',
                style: 3,
                labelBackgroundColor: '#131722',
            },
        },
        timeScale: { 
            timeVisible: true,
            borderVisible: false,
        },
        rightPriceScale: {
            borderVisible: false,
            autoScale: true
        }
    });

    window.candleSeries = window.chart.addCandlestickSeries({
        upColor: themes.dark.up, downColor: themes.dark.down,
        borderUpColor: themes.dark.up, borderDownColor: themes.dark.down,
        wickUpColor: themes.dark.up, wickDownColor: themes.dark.down
    });

    window.DrawingManager.init(window.chart, window.candleSeries);
    window.syncDrawingWithChart();
    window.setupLazyLoading(); // Ne pas oublier d'activer le listener de scroll
    
    console.log("DataEdge v4.1.1 Engine Fully Loaded.");
};

window.updateChartData = function(data, symbol = "Default") {
    if (!window.candleSeries) window.initChart(); 

    window.cyberLog(`Symbole : ${symbol}`);
    window.currentSymbol = symbol;
    window.isProcessingData = true;
    
    let currentTime = null;
    if (window.replayState.isActive && window.replayState.allData && window.replayState.allData.length > 0) {
        const currentCandle = window.replayState.allData[window.replayState.currentIndex];
        if (currentCandle) currentTime = currentCandle.time;
    }

    window.replayState.allData = data; 

    if (window.replayState.isActive) {
        // --- MODE BACKTEST ---
		window.cyberLog(`MODE BACKTEST`);
        if (currentTime) {
            const newIndex = data.findIndex(d => d.time >= currentTime);
            window.replayState.currentIndex = (newIndex !== -1) ? newIndex : data.length - 1;
        } else {
            window.replayState.currentIndex = data.length - 1;
        }

        // IMPORTANT : On applique le futur même sur le slice de l'historique
        const history = data.slice(0, window.replayState.currentIndex + 1);
        window.candleSeries.setData(getExtendedTimeline(history));
        
        window.chart.timeScale().scrollToPosition(0, false); // On laisse 15 bougies de marge à droite
    } 
	   else {
		window.cyberLog(`MODE NORMAL`);
		// --- MODE NORMAL ---
		const timeline = getExtendedTimeline(data);
		window.candleSeries.setData(timeline);
    
		if (data.length > 0) {
			// Le nombre total de bougies réelles (sans le padding futur) 
			// se situe à l'index (timeline.length - 2000)
			const lastRealIndex = timeline.length - 2000;

			// On affiche par exemple les 150 dernières bougies réelles
			// et on laisse 50 bougies de marge dans le futur pour respirer
			window.chart.timeScale().setVisibleLogicalRange({
				from: lastRealIndex - 150, 
				to: lastRealIndex + 50,    
			});
		}
	}

    // ... Reste de la fonction (Drawings, AutoScale) ...
    window.chart.priceScale('right').applyOptions({ autoScale: true });
    if(window.DrawingManager) window.DrawingManager.load(); 
    
    setTimeout(() => { 
        window.isProcessingData = false;
        window.cyberLog(`${symbol} synchronisé.`);
    }, 200);

    setTimeout(() => {
        window.chart.priceScale('right').applyOptions({ autoScale: false });
    }, 500);        
};
window.setupLazyLoading = function() {window.cyberLog(`LOZYLOADING1`) ;
    window.chart.timeScale().subscribeVisibleTimeRangeChange(async range => {
        if (!range || window.isProcessingData || window.replayState.isActive ) return;
		window.cyberLog(`LOZYLOADING`);
        const data = window.candleSeries.data().filter(d => d.close !== undefined);
        if (data.length && range.from <= data[0].time) {
            window.isProcessingData = true;
            try {
			window.cyberLog(`LOZYLOADING`);
                const bridge = window.chrome.webview.hostObjects.chartService;
                if (bridge) await bridge.loadPreviousYear();
            } catch (err) {
                window.isProcessingData = false;
            }
        }
    });
};
//zone replay
// --- ÉTAT DU REPLAY ---
window.replayState = {
    isActive: false,
    isPlaying: false,
    currentIndex: 0,
    speed: 1, // Nombre de bougies par saut
    allData: [] // Stockage complet des données reçues du C#
};

window.toggleReplayUI = function() {
    let dashboard = document.getElementById('replay-dashboard');
    const btn = document.getElementById('btn-replay-mode');
    
    if (dashboard) {
        dashboard.remove();
        btn.classList.remove('active');
        window.replayState.isActive = false;
        if(window.replayState.allData.length > 0) window.candleSeries.setData(window.replayState.allData);
		const bridge = window.chrome.webview.hostObjects.chartService;
        if (bridge) {
            bridge.ExitReplayMode(); 
        }
        return;
    }

    window.replayState.isActive = true;
    btn.classList.add('active');
    
    // Structure horizontale ultra-compacte
    const html = `
        <div id="replay-dashboard" style="display: flex; align-items: center; padding: 4px 10px; gap: 8px;">
            <div id="replay-header" style="cursor: move; display: flex; flex-direction: column; gap: 2px; padding-right: 8px; border-right: 1px solid #363c4e;">
                <div style="width: 3px; height: 3px; background: #555; border-radius: 50%;"></div>
                <div style="width: 3px; height: 3px; background: #555; border-radius: 50%;"></div>
                <div style="width: 3px; height: 3px; background: #555; border-radius: 50%;"></div>
            </div>

            <div class="replay-group" style="display: flex; align-items: center; gap: 5px;">
                <input type="date" id="replay-date-input" class="replay-input" style="width: 120px;">
                <button class="icon-btn" onclick="jumpToReplayDate()">Go</button>
            </div>

            <div class="replay-divider" style="width: 1px; height: 20px; background: #363c4e;"></div>

            <div class="replay-group" style="display: flex; align-items: center; gap: 5px;">
                <button class="icon-btn" onclick="stepReplay(-1)">❮</button>
                <button id="btn-play-pause" class="icon-btn" onclick="togglePlayReplay()">▶</button>
                <button class="icon-btn" onclick="stepReplay(1)">❯</button>
            </div>

            <div class="replay-divider" style="width: 1px; height: 20px; background: #363c4e;"></div>

            <select class="replay-speed" onchange="window.replayState.speed = parseInt(this.value)" style="background: #2a2e39; color: white; border: 1px solid #444; border-radius: 4px; font-size: 11px;">
                <option value="1">1x</option>
                <option value="5">5x</option>
                <option value="10">10x</option>
            </select>

            <button class="icon-btn" onclick="toggleReplayUI()" style="color:#ff4d4d; margin-left: 5px;">✖</button>
        </div>
    `;
    
    document.getElementById('chart-container').insertAdjacentHTML('beforeend', html);
    
    // Activer le Drag & Drop
    makeDraggable(document.getElementById('replay-dashboard'), document.getElementById('replay-header'));
};

// Fonction utilitaire pour le déplacement
function makeDraggable(elmnt, handle) {
    let pos1 = 0, pos2 = 0, pos3 = 0, pos4 = 0;
    handle.onmousedown = dragMouseDown;

    function dragMouseDown(e) {
        e.preventDefault();
        pos3 = e.clientX;
        pos4 = e.clientY;
        document.onmouseup = closeDragElement;
        document.onmousemove = elementDrag;
    }

    function elementDrag(e) {
        e.preventDefault();
        pos1 = pos3 - e.clientX;
        pos2 = pos4 - e.clientY;
        pos3 = e.clientX;
        pos4 = e.clientY;
        elmnt.style.top = (elmnt.offsetTop - pos2) + "px";
        elmnt.style.left = (elmnt.offsetLeft - pos1) + "px";
        // On enlève le transform: translateX(-50%) pour éviter les conflits de position
        elmnt.style.transform = "none";
    }

    function closeDragElement() {
        document.onmouseup = null;
        document.onmousemove = null;
    }
}

// Fonctions de contrôle (Stubs pour l'instant)
window.stepReplay = function(direction) {
    const step = window.replayState.speed * direction;
    window.replayState.currentIndex += step;
    
    if(window.replayState.currentIndex < 0) window.replayState.currentIndex = 0;
    if(window.replayState.currentIndex >= window.replayState.allData.length) {
        window.replayState.currentIndex = window.replayState.allData.length - 1;
        window.replayState.isPlaying = false;
        const btn = document.getElementById('btn-play-pause');
        if(btn) btn.innerText = "▶";
    }

    // ON RE-GÉNÈRE LA TIMELINE À CHAQUE PAS POUR POUSSER LE FUTUR
    const partialData = window.replayState.allData.slice(0, window.replayState.currentIndex + 1);
    const extendedData = getExtendedTimeline(partialData);
    
    window.candleSeries.setData(extendedData);
    
    // On garde le focus sur la bougie actuelle sans sauter brutalement
    // window.chart.timeScale().scrollToPosition(0, false); 
};
// A mettre dans votre fichier JS

window.jumpToReplayDate = async function() {
    const dateInput = document.getElementById('replay-date-input').value; // ex: "2022-05-15"
    if (!dateInput) return;

    const targetYear = parseInt(dateInput.split('-')[0]);

    // --- 1. ON VERROUILLE TOUT ---
    // Cela empêche le système de scroll (LazyLoading) de s'activer
    window.isProcessingData = true; 

    // On cherche si on a déjà les bougies en mémoire
    let index = window.replayState.allData.findIndex(d => {
        const dDate = typeof d.time === 'string' ? d.time : new Date(d.time * 1000).toISOString().split('T')[0];
        return dDate >= dateInput;
    });
	window.cyberLog(`index=` +index);
    if (index === -1 || index===0) {
        window.cyberLog(`Chargement de l'année ${targetYear}...`);
        const bridge = window.chrome.webview.hostObjects.chartService;
        if (bridge) {
		window.cyberLog(`yes bridge`);
            await bridge.LoadYearForBacktest(targetYear);
            return; // On s'arrête là, le C# rappellera setupBacktestData
        }
		else{
		window.cyberLog(`no bridge`);
		}
    }

    // Si on a déjà les données, on applique le saut
    applyJump(index, dateInput);
};

// Cette fonction reçoit les données du C#
window.setupBacktestData = function(newData, year) {
    // On remplace les données actuelles par l'année chargée
    window.replayState.allData = newData;
    window.cyberLog(`received new data processing jump`);
    // On cherche l'index de la date choisie dans ces nouvelles données
    const dateInput = document.getElementById('replay-date-input').value;
    const newIndex = window.replayState.allData.findIndex(d => {
        const dDate = typeof d.time === 'string' ? d.time : new Date(d.time * 1000).toISOString().split('T')[0];
        return dDate >= dateInput;
    });

    applyJump(newIndex !== -1 ? newIndex : 0, dateInput);
};

function applyJump(index, dateText) {
    window.replayState.currentIndex = index;
    
    // On ne montre au graphique que le passé (jusqu'à la date du jump)
    const history = window.replayState.allData.slice(0, index + 1);
    window.candleSeries.setData(getExtendedTimeline(history));
    
    // On force le graphique à montrer la fin (la bougie du jump)
    window.chart.timeScale().scrollToPosition(0, false);
    
    window.cyberLog(`Jump terminé : ${dateText}`);

    // --- 2. ON DÉVERROUILLE ---
    	
    setTimeout(() => {
        window.isProcessingData = false;const s = window.chart.priceScale('right');
        s.applyOptions({ autoScale: true });
    }, 700);
	// On attend un peu que le graphique se stabilise avant d'autoriser à nouveau le scroll
	setTimeout(() => {
        const s = window.chart.priceScale('right');
        s.applyOptions({ autoScale: false });
        if (typeof window.updateScaleButtonsUI === 'function') {
            window.updateScaleButtonsUI();
        }
        console.log("AutoScale released");
    }, 200);
};

window.appendOrPrependData = function(newData, year) {
    // 1. On fusionne et on dédoublonne dans la mémoire (allData)
    const combined = [...window.replayState.allData, ...newData];
    const unique = Array.from(new Map(combined.map(item => [item.time, item])).values());
    unique.sort((a, b) => a.time - b.time);
    
    window.replayState.allData = unique;

    // 2. IMPORTANT : Si on est en mode Replay, on ne met à jour le graphique 
    // QUE jusqu'à l'index actuel. On ne veut pas voir l'année future d'un coup !
    if (window.replayState.isActive) {
        const history = window.replayState.allData.slice(0, window.replayState.currentIndex + 1);
        window.candleSeries.setData(getExtendedTimeline(history));
    } else {
        // Mode normal : on peut tout afficher
        window.candleSeries.setData(getExtendedTimeline(unique));
    }
    
    window.isProcessingData = false; 
    window.cyberLog(`Année ${year} prête (chargée en silence).`);
};
window.togglePlayReplay = function() {
    window.replayState.isPlaying = !window.replayState.isPlaying;
    const btn = document.getElementById('btn-play-pause'); // Correction ID
    if(btn) btn.innerText = window.replayState.isPlaying ? "⏸" : "▶";
    
    if(window.replayState.isPlaying) {
        runReplayLoop();
    }
};

function runReplayLoop() {
    if(!window.replayState.isPlaying || !window.replayState.isActive) return;
    
    // --- DETECTION DE FIN DE DONNÉES PROCHE ---
    const threshold = 50; // On anticipe 50 bougies avant la fin
    if (window.replayState.currentIndex >= window.replayState.allData.length - threshold && !window.isProcessingData) {
        
        // Calcul de l'année suivante à charger
        const lastCandle = window.replayState.allData[window.replayState.allData.length - 1];
        const lastDate = new Date(lastCandle.time * 1000);
        const nextYear = lastDate.getFullYear() + 1;

        window.cyberLog(`Anticipation : Chargement de l'année ${nextYear}...`);
        window.isProcessingData = true; // Verrou pour ne pas lancer 10 appels

        const bridge = window.chrome.webview.hostObjects.chartService;
        if (bridge) {
            // On appelle une méthode qui va utiliser appendOrPrependData
            bridge.loadNextYear(); 
        }
    }

    window.stepReplay(1);
    
    // Vitesse de la boucle
    setTimeout(runReplayLoop, 500); 
}
//zone replay

function getExtendedTimeline(realData) {
    if (!realData.length) return [];
    
    // On calcule l'intervalle moyen (ex: 3600s pour 1H)
    const interval = realData.length > 1 ? (realData[1].time - realData[0].time) : 3600;
    
    let timeline = [];
    
    // Passé (Marge à gauche)
    for (let i = 200; i > 0; i--) {
        timeline.push({ time: realData[0].time - (i * interval) });
    }
    
    // Données réelles
    timeline = [...timeline, ...realData];
    
    // Futur (Marge à droite "infinie")
    // On génère 2000 bougies vides dans le futur pour permettre le dessin
    const lastTime = realData[realData.length - 1].time;
    for (let i = 1; i <= 2000; i++) {
        timeline.push({ time: lastTime + (i * interval) });
    }
    
    return timeline;
}

window.prependChartData = function(newData) {
    const current = window.candleSeries.data().filter(d => d.close !== undefined);
    const combined = [...newData, ...current].sort((a,b) => a.time - b.time);
    window.candleSeries.setData(getExtendedTimeline(combined));
    window.isProcessingData = false;
    window.cyberLog("Historique fusionné.");
};

window.toggleGrid = function() {
    window.isGridVisible = !window.isGridVisible;
    window.chart.applyOptions({
        grid: { vertLines: { visible: window.isGridVisible }, horzLines: { visible: window.isGridVisible } }
    });
};

window.toggleTheme = function() {
    window.isDarkMode = !window.isDarkMode;
    const t = window.isDarkMode ? themes.dark : themes.light;
    window.chart.applyOptions({ 
        layout: { background: { color: t.bg }, textColor: t.text },
        grid: { vertLines: { color: t.grid }, horzLines: { color: t.grid } }
    });
    window.candleSeries.applyOptions({
        upColor: t.up, downColor: t.down, wickUpColor: t.up, wickDownColor: t.down,
        borderUpColor: t.up, borderDownColor: t.down
    });
};
window.updateColors = function() {
    const up = document.getElementById('upColor').value;
    const down = document.getElementById('downColor').value;
    window.candleSeries.applyOptions({ upColor: up, downColor: down, wickUpColor: up, wickDownColor: down });
    window.cyberLog("Couleurs mises à jour.");
};

window.updateScaleButtonsUI = function() {
    const s = window.chart.priceScale('right');
    const opts = s.options();
    
    // Auto Scale
    const btnAuto = document.getElementById('btn-auto-scale');
    if(btnAuto) btnAuto.classList.toggle('active', opts.autoScale);

    // Percentage Mode
    const btnPercent = document.getElementById('btn-percent-scale');
    if(btnPercent) btnPercent.classList.toggle('active', opts.mode === 2); // 2 = Percentage

    // Logarithmic Mode
    const btnLog = document.getElementById('btn-log-scale');
    if(btnLog) btnLog.classList.toggle('active', opts.mode === 1); // 1 = Logarithmic
};

// Toggle POURCENTAGE
window.togglePercentScale = function() {
    const s = window.chart.priceScale('right');
    const isPercent = s.options().mode === 2;
    
    s.applyOptions({
        mode: isPercent ? 0 : 2 // Revient en Normal (0) ou passe en Percent (2)
    });
    window.updateScaleButtonsUI();
};

// Toggle LOGARITHMIQUE
window.toggleLogScale = function() {
    const s = window.chart.priceScale('right');
    const isLog = s.options().mode === 1;
    
    s.applyOptions({
        mode: isLog ? 0 : 1 // Revient en Normal (0) ou passe en Log (1)
    });
    window.updateScaleButtonsUI();
};

// Ton AutoScale reste identique mais on s'assure qu'il update bien l'UI
window.toggleAutoScale = function() {
    const s = window.chart.priceScale('right');
    s.applyOptions({ autoScale: !s.options().autoScale });
    window.updateScaleButtonsUI();
};

// INITIALISATION
document.addEventListener('DOMContentLoaded', () => {
    window.initChart();
});