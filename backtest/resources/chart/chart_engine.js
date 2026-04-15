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

window.cyberLog = function(msg, isError = false) {
    const el = document.getElementById('debug-console');
    if(!el) return;
    const time = new Date().toLocaleTimeString();
    const style = isError ? "color:#ff4d4d; font-weight:bold;" : "color:#00FFFF;";
    el.innerHTML = `<div style="${style}">[${time}] ${msg}</div>` + el.innerHTML;
};

window.initChart = function() {
    if (window.chart) return; // Sécurité contre double init
    const container = document.getElementById('chart-container');
    const t = themes.dark;

    window.chart = LightweightCharts.createChart(container, {
        layout: { background: { color: t.bg }, textColor: t.text },
        grid: { 
            vertLines: { color: t.grid, visible: window.isGridVisible }, 
            horzLines: { color: t.grid, visible: window.isGridVisible } 
        },
        rightPriceScale: { autoScale: true, borderVisible: false },
        timeScale: { timeVisible: true, borderVisible: false, rightOffset: 50, barSpacing: 10 },
        handleScroll: true,
        handleScale: true,
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
    });


    window.candleSeries = window.chart.addCandlestickSeries({
        upColor: t.up, downColor: t.down, borderVisible: false, wickUpColor: t.up, wickDownColor: t.down
    });
    
    window.setupLazyLoading();
     
    setTimeout(() => {
        if(window.DrawingManager) {
            window.DrawingManager.loadDrawings();
            window.syncDrawingWithChart();
            window.cyberLog("Système de dessin prêt.");
        }
    }, 50);

    window.updateScaleButtonsUI();
    window.cyberLog("Moteur initialisé (WebView2).");
};

// --- LOGIQUE DE DONNÉES CORRIGÉE ---
window.updateChartData = function(data, symbol = "Default") {
    if (!window.candleSeries) {
        window.initChart(); 
    }

    window.cyberLog(`Symbole : ${symbol}`);
    window.currentSymbol = symbol;
    window.isProcessingData = true;
    
    // 1. On injecte les données
    const timeline = getExtendedTimeline(data);
    window.candleSeries.setData(timeline);
    
    // 2. FORCE L'AUTOSCALE (Réinitialise l'échelle de prix)
    window.chart.priceScale('right').applyOptions({
        autoScale: true,
    });

    // 3. CADRAGE INTELLIGENT
    // Au lieu de fitContent (qui montre tout), on cadre sur les bougies réelles
    if (data.length > 0) {
        const lastIndex = timeline.length - 150; // On retire la marge de droite
        const firstVisibleIndex = lastIndex - 100; // On montre les 100 dernières bougies
        
        window.chart.timeScale().setVisibleLogicalRange({
            from: firstVisibleIndex,
            to: lastIndex + 20, // Petite marge pour voir le prix actuel
        });
    }

    // 4. SYNC DESSINS
    if(window.DrawingManager) window.DrawingManager.loadDrawings();
    
    window.updateScaleButtonsUI();

    setTimeout(() => { 
        window.isProcessingData = false;
        window.cyberLog(`${symbol} centré.`);
    }, 200);
};

// --- LE RESTE RESTE IDENTIQUE À TON NOUVEAU CODE ---
window.setupLazyLoading = function() {
    window.chart.timeScale().subscribeVisibleTimeRangeChange(async range => {
        if (!range || window.isProcessingData) return;
        const data = window.candleSeries.data().filter(d => d.close !== undefined);
        if (data.length && range.from <= data[0].time) {
            window.isProcessingData = true;
            try {
                const bridge = chrome.webview.hostObjects.chartService;
                if (bridge) await bridge.loadPreviousYear();
            } catch (err) {
                window.isProcessingData = false;
            }
        }
    });
};

function getExtendedTimeline(realData) {
    if (!realData.length) return [];
    const interval = realData.length > 1 ? (realData[1].time - realData[0].time) : 3600;
    let timeline = [];
    for (let i = 50; i > 0; i--) timeline.push({ time: realData[0].time - (i * interval) });
    timeline = [...timeline, ...realData];
    for (let i = 1; i <= 150; i++) timeline.push({ time: realData[realData.length-1].time + (i * interval) });
    return timeline;
}

window.prependChartData = function(newData) {
    const current = window.candleSeries.data().filter(d => d.close !== undefined);
    const combined = [...newData, ...current].sort((a,b) => a.time - b.time);
    window.candleSeries.setData(getExtendedTimeline(combined));
    window.isProcessingData = false;
};

window.toggleGrid = function() {
    window.isGridVisible = !window.isGridVisible;
    window.chart.applyOptions({
        grid: { vertLines: { visible: window.isGridVisible }, horzLines: { visible: window.isGridVisible } }
    });
};
window.updateColors = function() {
    const up = document.getElementById('upColor').value;
    const down = document.getElementById('downColor').value;
    window.candleSeries.applyOptions({ upColor: up, downColor: down, wickUpColor: up, wickDownColor: down });
    window.cyberLog("Couleurs mises à jour.");
};
window.toggleTheme = function() {
    window.isDarkMode = !window.isDarkMode;
    const t = window.isDarkMode ? themes.dark : themes.light;
    window.chart.applyOptions({ layout: { background: { color: t.bg }, textColor: t.text } });
};

window.updateScaleButtonsUI = function() {
    const opts = window.chart.priceScale('right').options();
    const btn = document.getElementById('btn-auto-scale');
    if(btn) btn.classList.toggle('active', opts.autoScale);
};

window.toggleAutoScale = function() {
    const s = window.chart.priceScale('right');
    s.applyOptions({ autoScale: !s.options().autoScale });
    window.updateScaleButtonsUI();
};

// INITIALISATION
document.addEventListener('DOMContentLoaded', () => {
    window.initChart();
});