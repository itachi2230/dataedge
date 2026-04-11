window.chart = null;
window.candleSeries = null;
window.lineSeries = null;
window.isDarkMode = true;
window.isGridVisible = true;
window.isProcessingData = false;
window.currentSymbol = "Default";
window.currentChartType = 'candles';

const themes = {
    dark: { bg: '#131722', text: '#d1d4dc', grid: 'rgba(42, 46, 57, 0.5)', up: '#00FFFF', down: '#FF007F' },
    light: { bg: '#ffffff', text: '#131722', grid: 'rgba(235, 228, 245, 0.5)', up: '#26a69a', down: '#ef5350' }
};

window.cyberLog = function(msg) {
    const el = document.getElementById('debug-console');
    if(el) el.innerHTML = `<div>> ${msg}</div>` + el.innerHTML;
};

window.saveUISettings = function() {
    const settings = {
        isDarkMode: window.isDarkMode,
        isGridVisible: window.isGridVisible,
        upColor: document.getElementById('upColor')?.value || themes.dark.up,
        downColor: document.getElementById('downColor')?.value || themes.dark.down
    };
    localStorage.setItem('DataEdge_UISettings', JSON.stringify(settings));
};

window.initChart = function() {
    const container = document.getElementById('chart-container');
    if (!container) return;

    const saved = localStorage.getItem('DataEdge_UISettings');
    let startTheme = themes.dark;
    
    if (saved) {
        const s = JSON.parse(saved);
        window.isDarkMode = s.isDarkMode;
        window.isGridVisible = s.isGridVisible;
        startTheme = window.isDarkMode ? themes.dark : themes.light;
    }

    window.chart = LightweightCharts.createChart(container, {
        layout: { background: { color: startTheme.bg }, textColor: startTheme.text },
        grid: { 
            vertLines: { color: startTheme.grid, visible: window.isGridVisible }, 
            horzLines: { color: startTheme.grid, visible: window.isGridVisible } 
        },
        rightPriceScale: { 
            autoScale: false, 
            borderVisible: false, 
            scaleMargins: { top: 0.1, bottom: 0.1 } 
        },
        timeScale: { 
            timeVisible: true, 
            secondsVisible: false, 
            rightOffset: 40, 
            barSpacing: 10,
            fixLeftEdge: false,
            fixRightEdge: false, 
            shiftVisibleRangeOnNewBar: true, 
            borderVisible: false,
            minBarSpacing: 0.5,
            allowBoldLabels: true,
            uniformDistribution: false
        },
        crosshair: { mode: LightweightCharts.CrosshairMode.Normal },
        handleScroll: { mouseWheel: true, pressedMouseMove: true, horzTouchDrag: true, vertTouchDrag: true },
        localization: { locale: 'fr-FR' },
        handleScale: true,
    });

    window.createSeries('candles');
    if (window.updateScaleButtonsUI) window.updateScaleButtonsUI();
    if (window.setupLazyLoading) window.setupLazyLoading();
    if (window.syncDrawingWithChart) window.syncDrawingWithChart();
};

window.setupLazyLoading = function() {
    window.chart.timeScale().subscribeVisibleTimeRangeChange(range => {
        if (!range || window.isProcessingData) return;
        const series = (window.DrawingManager) ? window.DrawingManager.getActiveSeries() : window.candleSeries;
        if (!series) return;
        
        // On ne récupère que les bougies réelles (avec prix) pour le check
        const data = series.data().filter(d => d.close !== undefined);
        if (data.length > 0 && range.from <= data[0].time) {
            if (window.chartService) {
                window.isProcessingData = true;
                window.cyberLog("Chargement historique...");
                window.chartService.loadPreviousYear();
            }
        }
    });
};

window.createSeries = function(type) {
    if (window.candleSeries) window.chart.removeSeries(window.candleSeries);
    if (window.lineSeries) window.chart.removeSeries(window.lineSeries);
    window.currentChartType = type;
    const up = document.getElementById('upColor')?.value || themes.dark.up;
    const down = document.getElementById('downColor')?.value || themes.dark.down;

    if (type === 'candles') {
        window.candleSeries = window.chart.addCandlestickSeries({
            upColor: up, downColor: down, borderVisible: false, wickUpColor: up, wickDownColor: down
        });
    } else {
        window.lineSeries = window.chart.addLineSeries({ color: window.isDarkMode ? '#00FFFF' : '#2196F3', lineWidth: 2 });
    }
};

// Fonction utilitaire pour ajouter les extensions de temps
function getExtendedTimeline(realData) {
    if (!realData || realData.length === 0) return [];
    const interval = realData.length > 1 ? (realData[1].time - realData[0].time) : 3600;
    let timeline = [];

    // Extension Passé (50 bougies vides)
    const firstTime = realData[0].time;
    for (let i = 50; i > 0; i--) {
        timeline.push({ time: firstTime - (i * interval) });
    }

    // Données Réelles
    timeline = [...timeline, ...realData];

    // Extension Futur (150 bougies vides)
    const lastTime = realData[realData.length - 1].time;
    for (let i = 1; i <= 150; i++) {
        timeline.push({ time: lastTime + (i * interval) });
    }
    return timeline;
}

window.updateChartData = function(data, symbol = "Default") {
    if (!window.chart || !window.candleSeries) return;
    window.isProcessingData = true;
    window.currentSymbol = symbol;

    window.chart.priceScale('right').applyOptions({ autoScale: true });

    // On prépare la timeline étendue
    const fullTimeline = getExtendedTimeline(data);
    window.candleSeries.setData(fullTimeline);

    if (data.length > 0) {
        window.chart.timeScale().setVisibleRange({
            from: data[0].time,
            to: data[data.length - 1].time + ((data.length > 1 ? data[1].time - data[0].time : 3600) * 20)
        });
    }

    if(window.DrawingManager) window.DrawingManager.loadDrawings();

    setTimeout(() => {
        window.chart.priceScale('right').applyOptions({ autoScale: false });
        if (window.updateScaleButtonsUI) window.updateScaleButtonsUI();
        window.isProcessingData = false;
        if(window.DrawingManager) window.DrawingManager.redraw();
        window.cyberLog(`${symbol} chargé.`);
    }, 300);
};

window.prependChartData = function(newHistoricalData) {
    const series = (window.DrawingManager) ? window.DrawingManager.getActiveSeries() : window.candleSeries;
    if(!series) return;

    // 1. On récupère les données actuelles ET on filtre pour ne garder QUE les bougies réelles (avec prix)
    // Cela supprime tous les anciens whitespaces qui pourraient bloquer la fusion
    const currentRealData = series.data().filter(d => d.close !== undefined);
    
    // 2. Fusion des bougies réelles uniquement + Tri
    const allRealData = [...newHistoricalData, ...currentRealData].sort((a, b) => a.time - b.time);
    
    // 3. Suppression des doublons de timestamps réels
    const uniqueRealData = allRealData.filter((v, i, a) => i === 0 || v.time !== a[i-1].time);

    // 4. On reconstruit une timeline étendue propre (Passé -> Réel -> Futur)
    const newFullTimeline = getExtendedTimeline(uniqueRealData);

    // 5. Injection propre
    series.setData(newFullTimeline);

    setTimeout(() => {
        window.isProcessingData = false;
        window.cyberLog("Historique fusionné et timeline reconstruite.");
    }, 500);
};

window.toggleTheme = function() {
    window.isDarkMode = !window.isDarkMode;
    const t = window.isDarkMode ? themes.dark : themes.light;
    window.chart.applyOptions({
        layout: { background: { color: t.bg }, textColor: t.text },
        grid: { vertLines: { color: t.grid }, horzLines: { color: t.grid } }
    });
    if (window.candleSeries) {
        window.candleSeries.applyOptions({ upColor: t.up, downColor: t.down, wickUpColor: t.up, wickDownColor: t.down });
    }
    document.body.style.backgroundColor = t.bg;
    window.saveUISettings();
    if(window.DrawingManager) window.DrawingManager.redraw();
};

window.resetChart = function() {
    if (!window.chart) return;
    window.chart.timeScale().fitContent();
    window.chart.priceScale('right').applyOptions({ autoScale: true });
    if (window.DrawingManager) {
        window.DrawingManager.drawings = [];
        window.DrawingManager.redraw();
        if (window.currentSymbol) localStorage.removeItem('Draw_' + window.currentSymbol);
    }
};

window.toggleGrid = function() {
    window.isGridVisible = !window.isGridVisible;
    window.chart.applyOptions({ grid: { vertLines: { visible: window.isGridVisible }, horzLines: { visible: window.isGridVisible } } });
    window.saveUISettings();
};

window.updateColors = function() {
    const up = document.getElementById('upColor')?.value || themes.dark.up;
    const down = document.getElementById('downColor')?.value || themes.dark.down;
    if (window.candleSeries) window.candleSeries.applyOptions({ upColor: up, downColor: down, wickUpColor: up, wickDownColor: down });
    window.saveUISettings();
};

window.getRightPriceScale = function() { return window.chart ? window.chart.priceScale('right') : null; };

window.updateScaleButtonsUI = function() {
    const scale = window.getRightPriceScale();
    if (!scale) return;
    const options = scale.options();
    const btnA = document.getElementById('btn-auto-scale');
    if(btnA) btnA.classList.toggle('active', options.autoScale === true);
    const btnL = document.getElementById('btn-log-scale');
    if(btnL) btnL.classList.toggle('active', options.mode === 1);
    const btnP = document.getElementById('btn-percent-scale');
    if(btnP) btnP.classList.toggle('active', options.mode === 2);
};

window.toggleAutoScale = function() {
    const scale = window.getRightPriceScale();
    if(!scale) return;
    const newState = !scale.options().autoScale;
    scale.applyOptions({ autoScale: newState });
    if (newState && scale.options().mode !== 0) scale.applyOptions({ mode: 0 });
    window.updateScaleButtonsUI();
};

window.toggleLogScale = function() {
    const scale = window.getRightPriceScale();
    if(!scale) return;
    const newMode = (scale.options().mode === 1) ? 0 : 1;
    scale.applyOptions({ mode: newMode });
    window.updateScaleButtonsUI();
};

window.togglePercentScale = function() {
    const scale = window.getRightPriceScale();
    if(!scale) return;
    const newMode = (scale.options().mode === 2) ? 0 : 2;
    scale.applyOptions({ mode: newMode });
    window.updateScaleButtonsUI();
};

if (document.readyState === 'complete') window.initChart();
else window.addEventListener('load', window.initChart);