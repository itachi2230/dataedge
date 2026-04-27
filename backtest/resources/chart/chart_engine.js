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
    
    // --- MODIF REPLAY : On stocke les données brutes ---
    window.replayState.allData = data; 
    // --------------------------------------------------

    const timeline = getExtendedTimeline(data);
    
    // Si le replay est actif, on initialise l'index à la fin (ou au début selon ton choix)
    if (window.replayState.isActive) {
        window.replayState.currentIndex = data.length - 1;
    }

    window.candleSeries.setData(timeline);
    
    window.chart.priceScale('right').applyOptions({ autoScale: true });

    if (data.length > 0) {
        const lastIndex = timeline.length - 800; 
        const firstVisibleIndex = lastIndex - 100; 
        
        window.chart.timeScale().setVisibleLogicalRange({
            from: firstVisibleIndex,
            to: lastIndex + 20, 
        });
    }

    if(window.DrawingManager) window.DrawingManager.load(); 
    window.updateScaleButtonsUI();

    setTimeout(() => { 
        window.isProcessingData = false;
        window.cyberLog(`${symbol} centré.`);
    }, 200);

    setTimeout(() => {
        const s = window.chart.priceScale('right');
        s.applyOptions({ autoScale: false });
        if (typeof window.updateScaleButtonsUI === 'function') {
            window.updateScaleButtonsUI();
        }
        console.log("AutoScale released");
    }, 500);
};

window.setupLazyLoading = function() {
    window.chart.timeScale().subscribeVisibleTimeRangeChange(async range => {
        if (!range || window.isProcessingData) return;
        const data = window.candleSeries.data().filter(d => d.close !== undefined);
        if (data.length && range.from <= data[0].time) {
            window.isProcessingData = true;
            try {
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
    
    // Sécurité index
    if(window.replayState.currentIndex < 0) window.replayState.currentIndex = 0;
    if(window.replayState.currentIndex >= window.replayState.allData.length) {
        window.replayState.currentIndex = window.replayState.allData.length - 1;
        window.replayState.isPlaying = false;
    }

    const partialData = window.replayState.allData.slice(0, window.replayState.currentIndex + 1);
    window.candleSeries.setData(partialData);
    
    // C'est ici qu'on appellera plus tard le check des Setups (TP/SL)
};
window.jumpToReplayDate = function() {
    const dateInput = document.getElementById('replay-date-input').value;
    if (!dateInput || !window.replayState.allData.length) return;

    // On cherche l'index de la bougie dont la date correspond au input
    // Note: on compare les dates en format string YYYY-MM-DD
    const targetDate = dateInput; 
    const index = window.replayState.allData.findIndex(d => {
        const dDate = typeof d.time === 'string' ? d.time : new Date(d.time * 1000).toISOString().split('T')[0];
        return dDate >= targetDate;
    });

    if (index !== -1) {
        window.replayState.currentIndex = index;
        window.stepReplay(0); // Rafraîchir
        window.cyberLog(`Replay jump: ${dateInput}`);
    } else {
        window.cyberLog("Date non trouvée", true);
    }
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
    
    window.stepReplay(1);
    
    // Vitesse de la boucle (ex: 500ms)
    setTimeout(runReplayLoop, 500); 
}
//zone replay

function getExtendedTimeline(realData) {
    if (!realData.length) return [];
    const interval = realData.length > 1 ? (realData[1].time - realData[0].time) : 3600;
    let timeline = [];
    for (let i = 150; i > 0; i--) timeline.push({ time: realData[0].time - (i * interval) });
    timeline = [...timeline, ...realData];
    for (let i = 1; i <= 800; i++) timeline.push({ time: realData[realData.length-1].time + (i * interval) });
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