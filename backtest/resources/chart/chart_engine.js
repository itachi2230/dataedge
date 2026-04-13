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

// --- CONSOLE DE DEBUG & ERREUR ---
window.cyberLog = function(msg, isError = false) {
    const el = document.getElementById('debug-console');
    if(!el) return;
    const time = new Date().toLocaleTimeString();
    const style = isError ? "color:#ff4d4d; font-weight:bold;" : "color:#00FFFF;";
    el.innerHTML = `<div style="${style}">[${time}] ${msg}</div>` + el.innerHTML;
};

window.onerror = function(msg, url, line) {
    window.cyberLog(`ERREUR JS: ${msg} (Ligne: ${line})`, true);
};

window.initChart = function() {
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
		
    });

    window.candleSeries = window.chart.addCandlestickSeries({
        upColor: t.up, downColor: t.down, borderVisible: false, wickUpColor: t.up, wickDownColor: t.down
    });

    window.setupLazyLoading();
	setTimeout(() => {
    window.DrawingManager.loadDrawings();
    window.syncDrawingWithChart();
    window.cyberLog("Système de dessin prêt.");
}, 50);
    window.updateScaleButtonsUI();
    window.cyberLog("Moteur initialisé.");
};

// TIMELINE ÉTENDUE (GARDÉE)
function getExtendedTimeline(realData) {
    if (!realData.length) return [];
    const interval = realData.length > 1 ? (realData[1].time - realData[0].time) : 3600;
    let timeline = [];
    for (let i = 50; i > 0; i--) timeline.push({ time: realData[0].time - (i * interval) });
    timeline = [...timeline, ...realData];
    for (let i = 1; i <= 150; i++) timeline.push({ time: realData[realData.length-1].time + (i * interval) });
    return timeline;
}

window.updateChartData = function(data, symbol = "Default") {
    window.cyberLog(`Chargement de ${symbol}...`);
    window.currentSymbol = symbol;
    window.isProcessingData = true; // Verrouille le lazy loading pendant l'injection
    
    window.candleSeries.setData(getExtendedTimeline(data));
    window.chart.timeScale().fitContent();
    window.DrawingManager.loadDrawings();
    
    setTimeout(() => { 
        window.isProcessingData = false;
        window.cyberLog(`${symbol} prêt.`);
    }, 200);
};

window.prependChartData = function(newData) {
    window.cyberLog(`Fusion historique (${newData.length} bougies)`);
    const current = window.candleSeries.data().filter(d => d.close !== undefined);
    const combined = [...newData, ...current].sort((a,b) => a.time - b.time);
    window.candleSeries.setData(getExtendedTimeline(combined));
    window.isProcessingData = false;
};

window.setupLazyLoading = function() {
    window.chart.timeScale().subscribeVisibleTimeRangeChange(range => {
        if (!range || window.isProcessingData) return;
        const data = window.candleSeries.data().filter(d => d.close !== undefined);
        if (data.length && range.from <= data[0].time) {
            window.isProcessingData = true; // Évite les appels multiples
            window.cyberLog(`Détection bord gauche. Appel C#...`);
            // On vérifie le nom de ton bridge CefSharp
            // Si tu l'as nommé différemment en C#, change le nom ici
            const bridge = window.chartService || window.CefSharp?.BindObjectAsync("chartService");

            if (bridge && bridge.loadPreviousYear) {
                bridge.loadPreviousYear();
				 window.cyberLog("chargement de lanné precedent demandé..", false);
            } else {
                window.cyberLog("ERREUR : chartService (Bridge C#) non trouvé !", true);
                window.isProcessingData = false; // On déverrouille car l'appel a échoué
            }
        }
    });
};

// --- FONCTIONS MANQUANTES RE-INTÉGRÉES ---
window.toggleGrid = function() {
    window.isGridVisible = !window.isGridVisible;
    window.chart.applyOptions({
        grid: { vertLines: { visible: window.isGridVisible }, horzLines: { visible: window.isGridVisible } }
    });
    window.cyberLog(`Grille: ${window.isGridVisible ? 'ON' : 'OFF'}`);
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
    window.cyberLog(`Thème: ${window.isDarkMode ? 'Sombre' : 'Clair'}`);
};

// GESTION DES BOUTONS SCALE TV
window.updateScaleButtonsUI = function() {
    const opts = window.chart.priceScale('right').options();
    document.getElementById('btn-auto-scale')?.classList.toggle('active', opts.autoScale);
};

window.toggleAutoScale = function() {
    const s = window.chart.priceScale('right');
    s.applyOptions({ autoScale: !s.options().autoScale });
    window.updateScaleButtonsUI();
};

window.initChart();