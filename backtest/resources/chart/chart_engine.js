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
    Logarithmic: 1,
    Percentage: 2,
    IndexedTo100: 3,
};

const CACHE_KEY = "dataedge_user_prefs";

window.savePrefs = function() {
    const prefs = {
        dark: window.isDarkMode,
        grid: window.isGridVisible
    };
    localStorage.setItem(CACHE_KEY, JSON.stringify(prefs));
};

window.loadPrefs = function() {
    const saved = localStorage.getItem(CACHE_KEY);
    if (saved) {
        const parsed = JSON.parse(saved);
        window.isDarkMode = parsed.dark;
        window.isGridVisible = parsed.grid;
    }
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
    window.loadPrefs();
    
    const container = document.getElementById('chart-container');
    const t = window.isDarkMode ? themes.dark : themes.light;

    window.chart = LightweightCharts.createChart(container, {
        layout: { 
            background: { type: 'solid', color: t.bg }, 
            textColor: t.text
        },
        grid: { 
            vertLines: { color: t.grid, visible: window.isGridVisible }, 
            horzLines: { color: t.grid, visible: window.isGridVisible } 
        },
        crosshair: {
            mode: LightweightCharts.CrosshairMode.Normal, 
            vertLine: {
                width: 1,
                color: '#758696',
                style: 3,
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
        upColor: t.up, downColor: t.down,
        borderUpColor: t.up, borderDownColor: t.down,
        wickUpColor: t.up, wickDownColor: t.down
    });

    window.DrawingManager.init(window.chart, window.candleSeries);
    window.syncDrawingWithChart();
    window.setupLazyLoading();
    
};

window.updateChartData = function(data, symbol = "Default") {
    if (!window.candleSeries) window.initChart(); 

    window.currentSymbol = symbol;
    window.isProcessingData = true;
    
    let currentTime = null;
    if (window.replayState.isActive && window.replayState.allData && window.replayState.allData.length > 0) {
        const currentCandle = window.replayState.allData[window.replayState.currentIndex];
        if (currentCandle) currentTime = currentCandle.time;
    }

    window.replayState.allData = data; 

    if (window.replayState.isActive) {
        if (currentTime) {
            const newIndex = data.findIndex(d => d.time >= currentTime);
            window.replayState.currentIndex = (newIndex !== -1) ? newIndex : data.length - 1;
        } else {
            window.replayState.currentIndex = data.length - 1;
        }

        const history = data.slice(0, window.replayState.currentIndex + 1);
        window.candleSeries.setData(getExtendedTimeline(history));
        window.chart.timeScale().scrollToPosition(0, false);
    } 
    else {
        const timeline = getExtendedTimeline(data);
        window.candleSeries.setData(timeline);
    
        if (data.length > 0) {
            const lastRealIndex = timeline.length - 2000;
            window.chart.timeScale().setVisibleLogicalRange({
                from: lastRealIndex - 150, 
                to: lastRealIndex + 50,    
            });
        }
    }

    window.chart.priceScale('right').applyOptions({ autoScale: true });
    if(window.DrawingManager) window.DrawingManager.load(); 
    
    setTimeout(() => { 
        window.isProcessingData = false;
    }, 200);

    setTimeout(() => {
        window.chart.priceScale('right').applyOptions({ autoScale: false });
    }, 500);        
};

window.setupLazyLoading = function() {
    window.chart.timeScale().subscribeVisibleTimeRangeChange(async range => {
        if (!range || window.isProcessingData || window.replayState.isActive) return;

        const data = window.candleSeries.data().filter(d => d.close !== undefined);
        if (!data.length) return;

        const bridge = window.chrome.webview.hostObjects.chartService;
        if (!bridge) return;

        if (range.from <= data[3].time) {
            window.isProcessingData = true;
            await bridge.loadPreviousYear(data[3].time);
        } 
    });
};

window.replayState = {
    isActive: false,
    isPlaying: false,
    currentIndex: 0,
    speed: 1,
    allData: []
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
        if (bridge) bridge.ExitReplayMode(); 
        return;
    }

    window.replayState.isActive = true;
    btn.classList.add('active');
    
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
        </div>`;
    
    document.getElementById('chart-container').insertAdjacentHTML('beforeend', html);
    makeDraggable(document.getElementById('replay-dashboard'), document.getElementById('replay-header'));
};

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
        elmnt.style.transform = "none";
    }

    function closeDragElement() {
        document.onmouseup = null;
        document.onmousemove = null;
    }
}

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

    const partialData = window.replayState.allData.slice(0, window.replayState.currentIndex + 1);
    window.candleSeries.setData(getExtendedTimeline(partialData));
};

window.jumpToReplayDate = async function() {
    const dateInput = document.getElementById('replay-date-input').value;
    if (!dateInput) return;

    const targetYear = parseInt(dateInput.split('-')[0]);
    window.isProcessingData = true; 

    let index = window.replayState.allData.findIndex(d => {
        const dDate = typeof d.time === 'string' ? d.time : new Date(d.time * 1000).toISOString().split('T')[0];
        return dDate >= dateInput;
    });

    if (index === -1 || index === 0) {
        const bridge = window.chrome.webview.hostObjects.chartService;
        if (bridge) {
            await bridge.LoadYearForBacktest(targetYear);
            return;
        }
    }
    applyJump(index, dateInput);
};

window.setupBacktestData = function(newData, year) {
    window.replayState.allData = newData;
    const dateInput = document.getElementById('replay-date-input').value;
    const newIndex = window.replayState.allData.findIndex(d => {
        const dDate = typeof d.time === 'string' ? d.time : new Date(d.time * 1000).toISOString().split('T')[0];
        return dDate >= dateInput;
    });
    applyJump(newIndex !== -1 ? newIndex : 0, dateInput);
};

function applyJump(index, dateText) {
    window.replayState.currentIndex = index;
    const history = window.replayState.allData.slice(0, index + 1);
    window.candleSeries.setData(getExtendedTimeline(history));
    window.chart.timeScale().scrollToPosition(0, false);
    
    setTimeout(() => {
        window.isProcessingData = false;
        window.chart.priceScale('right').applyOptions({ autoScale: true });
    }, 700);

    setTimeout(() => {
        window.chart.priceScale('right').applyOptions({ autoScale: false });
        if (typeof window.updateScaleButtonsUI === 'function') window.updateScaleButtonsUI();
    }, 200);
}

window.appendOrPrependData = function(newData, year) {
    const combined = [...window.replayState.allData, ...newData];
    const uniqueMap = new Map();
    combined.forEach(item => uniqueMap.set(item.time, item));
    const sortedUnique = Array.from(uniqueMap.values()).sort((a, b) => a.time - b.time);
    window.replayState.allData = sortedUnique;

    if (window.replayState.isActive) {
        const history = window.replayState.allData.slice(0, window.replayState.currentIndex + 1);
        window.candleSeries.setData(getExtendedTimeline(history));
    } else {
        window.candleSeries.setData(getExtendedTimeline(sortedUnique));
    }
    window.isProcessingData = false; 
};

window.togglePlayReplay = function() {
    window.replayState.isPlaying = !window.replayState.isPlaying;
    const btn = document.getElementById('btn-play-pause');
    if(btn) btn.innerText = window.replayState.isPlaying ? "⏸" : "▶";
    if(window.replayState.isPlaying) runReplayLoop();
};

function runReplayLoop() {
    if(!window.replayState.isPlaying || !window.replayState.isActive) return;
    
    const threshold = 50; 
    if (window.replayState.currentIndex >= window.replayState.allData.length - threshold && !window.isProcessingData) {
        const lastCandle = window.replayState.allData[window.replayState.allData.length - 1];
        if (lastCandle) {
            window.isProcessingData = true;
            const bridge = window.chrome.webview.hostObjects.chartService;
            if (bridge) bridge.loadNextYear(lastCandle.time); 
        }
    }

    window.stepReplay(1);
    const currentCandle = window.replayState.allData[window.replayState.currentIndex];
    if (currentCandle) checkLastSetupStatus(currentCandle);
    
    setTimeout(runReplayLoop, 500); 
}

window.addEventListener('resize', () => {
    window.fitChartToContainer();
});
window.fitChartToContainer = function() {
    if (window.chart) {
        const container = document.getElementById('chart-container'); // Vérifiez l'ID de votre div
        if (container) {
            window.chart.applyOptions({ 
                width: container.clientWidth, 
                height: container.clientHeight 
            });
            // Pour les anciennes versions de la lib, on utilisait :
            // window.chart.resize(container.clientWidth, container.clientHeight);
        }
    }
};
function checkLastSetupStatus(currentCandle) {
    const dm = window.DrawingManager;
    if (!dm.lastActiveSetup) return;

    const setup = dm.lastActiveSetup.data;
    const tp = setup.points[1].price;
    const sl = setup.points[2].price;

    let isClosed = false;
    let result = 2;

    if (setup.type === 'long_pos') {
        if (currentCandle.high >= tp) { isClosed = true; result = 0; }
        else if (currentCandle.low <= sl) { isClosed = true; result = 1; }
    } else {
        if (currentCandle.low <= tp) { isClosed = true; result = 0; }
        else if (currentCandle.high >= sl) { isClosed = true; result = 1; }
    }

    if (isClosed) {
        sendTradeToCSharp(setup, currentCandle, result);
        if (window.replayState.isPlaying) window.togglePlayReplay();
        dm.lastActiveSetup = null;
    }
}

function sendTradeToCSharp(setup, candle, result) {
    const bridge = window.chrome.webview.hostObjects.chartService;
    if (!bridge) return;

    const entryPrice = setup.points[0].price;
    const targetPrice = (result === 0) ? setup.points[1].price : setup.points[2].price;
    const risk = Math.abs(entryPrice - setup.points[2].price);
    const reward = Math.abs(entryPrice - setup.points[1].price);
    const rr = risk !== 0 ? (reward / risk) : 0;

    const tradeObj = {
        Paire: window.currentSymbol || "Inconnue",
        Result: result,
        DateEntree: new Date(setup.points[0].time * 1000).toISOString(),
        DateSortie: new Date(candle.time * 1000).toISOString(),
        RR: parseFloat(rr.toFixed(2)),
        prixOpen: entryPrice.toString(),
        prixClose: targetPrice.toString(),
        description: `Backtest Auto-Close (${result === 0 ? 'TP' : 'SL'})`,
        TypeOrdre: setup.type === 'long_pos' ? 0 : 1,
        strategie: window.currentStrategy || "Default",
        Profit: 0
    };
    bridge.OnTradeSetupCompleted(JSON.stringify(tradeObj));
}
// Affiche l'overlay avec un message personnalisé
window.showLoader = function(message) {
    const loader = document.getElementById('cyber-loader');
    const msgEl = document.getElementById('loader-message');
    if (loader) {
        if (message) msgEl.innerText = message.toUpperCase();
        loader.classList.add('active');
    }
};

// Masque l'overlay de manière fluide
window.hideLoader = function() {
    const loader = document.getElementById('cyber-loader');
    if (loader) {
        loader.classList.remove('active');
    }
};
window.captureChart = function(type) {
    if (!window.chart) return;
    const canvas = window.chart.takeScreenshot();
    const dataURL = canvas.toDataURL("image/png");
    const bridge = window.chrome.webview.hostObjects.chartService;
    if (bridge) bridge.SaveChartScreenshot(type, dataURL);
};

function getExtendedTimeline(realData) {
    if (!realData.length) return [];

    // Calcul de l'intervalle médian sur les 20 premières bougies
    // pour éviter les faux gaps liés aux weekends sur les premières paires
    let interval = 3600;
    if (realData.length > 1) {
        const sampleSize = Math.min(20, realData.length - 1);
        const deltas = [];
        for (let i = 0; i < sampleSize; i++) {
            const delta = realData[i + 1].time - realData[i].time;
            if (delta > 0) deltas.push(delta);
        }
        if (deltas.length > 0) {
            deltas.sort((a, b) => a - b);
            // Médiane = le plus petit delta fréquent (élimine les gaps weekend/férié)
            interval = deltas[Math.floor(deltas.length * 0.25)]; // 1er quartile
        }
    }

    let timeline = [];
    
    for (let i = 200; i > 0; i--) {
        timeline.push({ time: realData[0].time - (i * interval) });
    }
    timeline = [...timeline, ...realData];
    
    const lastTime = realData[realData.length - 1].time;
    for (let i = 1; i <= 2000; i++) {
        timeline.push({ time: lastTime + (i * interval) });
    }
    return timeline;
}

window.prependChartData = function(newData) {
    const currentData = window.candleSeries.data().filter(d => d.close !== undefined);
    const combined = [...newData, ...currentData];
    const uniqueMap = new Map();
    combined.forEach(item => uniqueMap.set(item.time, item));
    const finalData = Array.from(uniqueMap.values()).sort((a, b) => a.time - b.time);
    window.candleSeries.setData(getExtendedTimeline(finalData));
    window.isProcessingData = false;
};

window.toggleGrid = function() {
    window.isGridVisible = !window.isGridVisible;
    window.chart.applyOptions({
        grid: { vertLines: { visible: window.isGridVisible }, horzLines: { visible: window.isGridVisible } }
    });
    window.savePrefs();
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
    window.savePrefs();
};

window.updateColors = function() {
    const up = document.getElementById('upColor').value;
    const down = document.getElementById('downColor').value;
    window.candleSeries.applyOptions({ upColor: up, downColor: down, wickUpColor: up, wickDownColor: down });
};

window.updateScaleButtonsUI = function() {
    const s = window.chart.priceScale('right');
    const opts = s.options();
    const btnAuto = document.getElementById('btn-auto-scale');
    if(btnAuto) btnAuto.classList.toggle('active', opts.autoScale);
    const btnPercent = document.getElementById('btn-percent-scale');
    if(btnPercent) btnPercent.classList.toggle('active', opts.mode === 2);
    const btnLog = document.getElementById('btn-log-scale');
    if(btnLog) btnLog.classList.toggle('active', opts.mode === 1);
};

window.togglePercentScale = function() {
    const s = window.chart.priceScale('right');
    const isPercent = s.options().mode === 2;
    s.applyOptions({ mode: isPercent ? 0 : 2 });
    window.updateScaleButtonsUI();
};

window.toggleLogScale = function() {
    const s = window.chart.priceScale('right');
    const isLog = s.options().mode === 1;
    s.applyOptions({ mode: isLog ? 0 : 1 });
    window.updateScaleButtonsUI();
};

window.toggleAutoScale = function() {
    const s = window.chart.priceScale('right');
    s.applyOptions({ autoScale: !s.options().autoScale });
    window.updateScaleButtonsUI();
};

document.addEventListener('DOMContentLoaded', () => {
    window.initChart();
});