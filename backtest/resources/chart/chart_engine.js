// chart_engine.js
window.chart = null;
window.candleSeries = null;
window.lineSeries = null;
window.isDarkMode = true;
window.isGridVisible = true;
window.isProcessingData = false;
window.currentChartType = 'candles';

const themes = {
    dark: { bg: '#131722', text: '#d1d4dc', grid: 'rgba(42, 46, 57, 0.5)', up: '#00FFFF', down: '#FF007F' },
    light: { bg: '#ffffff', text: '#131722', grid: 'rgba(235, 228, 245, 0.5)', up: '#26a69a', down: '#ef5350' }
};

window.cyberLog = function(msg) {
    const el = document.getElementById('debug-console');
    if(el) el.innerHTML = `<div>> ${msg}</div>` + el.innerHTML;
};

// Sauvegarde des réglages UI
window.saveUISettings = function() {
    const settings = {
        isDarkMode: window.isDarkMode,
        isGridVisible: window.isGridVisible,
        upColor: document.getElementById('upColor').value,
        downColor: document.getElementById('downColor').value
    };
    localStorage.setItem('DataEdge_UISettings', JSON.stringify(settings));
};

window.initChart = function() {
    const container = document.getElementById('chart-container');
    
    // Charger les réglages sauvegardés avant de créer le chart
    const saved = localStorage.getItem('DataEdge_UISettings');
    let startTheme = themes.dark;
    if (saved) {
        const s = JSON.parse(saved);
        window.isDarkMode = s.isDarkMode;
        window.isGridVisible = s.isGridVisible;
        startTheme = window.isDarkMode ? themes.dark : themes.light;
        document.getElementById('upColor').value = s.upColor;
        document.getElementById('downColor').value = s.downColor;
    }

    window.chart = LightweightCharts.createChart(container, {
        layout: { background: { color: startTheme.bg }, textColor: startTheme.text },
        grid: { 
            vertLines: { color: startTheme.grid, visible: window.isGridVisible }, 
            horzLines: { color: startTheme.grid, visible: window.isGridVisible } 
        },
		rightPriceScale: {
            autoScale: true, // IMPORTANT : Recalage automatique
            borderVisible: false,
        },
        timeScale: { 
            timeVisible: true, 
            secondsVisible: false,
            rightOffset: 12,
            barSpacing: 10,
        },
        crosshair: { mode: LightweightCharts.CrosshairMode.Normal },
        handleScroll: true,
        handleScale: true,
    });

    window.createSeries('candles');

    window.chart.timeScale().subscribeVisibleTimeRangeChange(range => {
        if (!range || window.isProcessingData) return;
        const series = window.currentChartType === 'candles' ? window.candleSeries : window.lineSeries;
        if (!series) return;
        
        const data = series.data();
        if (data.length > 0 && range.from <= data[0].time) {
            if (window.chartService) {
                window.isProcessingData = true;
                window.cyberLog("Appel historique...");
                window.chartService.loadPreviousYear();
            }
        }
    });

    // Synchronisation pour le dessin (évite le flottement)
    window.chart.timeScale().subscribeVisibleTimeRangeChange(() => {
        if(window.DrawingManager) window.DrawingManager.redraw();
    });
};

window.createSeries = function(type) {
    if (window.candleSeries) window.chart.removeSeries(window.candleSeries);
    if (window.lineSeries) window.chart.removeSeries(window.lineSeries);
    
    const t = window.isDarkMode ? themes.dark : themes.light;
    const up = document.getElementById('upColor').value;
    const down = document.getElementById('downColor').value;

    if (type === 'candles') {
        window.candleSeries = window.chart.addCandlestickSeries({
            upColor: up, downColor: down, borderVisible: false,
            wickUpColor: up, wickDownColor: down
        });
    } else {
        window.lineSeries = window.chart.addLineSeries({ 
            color: window.isDarkMode ? '#00FFFF' : '#2196F3', 
            lineWidth: 2 
        });
    }
};

window.toggleTheme = function() {
    window.isDarkMode = !window.isDarkMode;
    const t = window.isDarkMode ? themes.dark : themes.light;
    
    window.chart.applyOptions({
        layout: { background: { color: t.bg }, textColor: t.text },
        grid: { vertLines: { color: t.grid }, horzLines: { color: t.grid } }
    });

    if (window.candleSeries) {
        window.candleSeries.applyOptions({
            upColor: t.up, downColor: t.down,
            wickUpColor: t.up, wickDownColor: t.down
        });
        document.getElementById('upColor').value = t.up;
        document.getElementById('downColor').value = t.down;
    }
    
    document.body.style.backgroundColor = t.bg;
    window.saveUISettings();
    if(window.DrawingManager) window.DrawingManager.redraw();
};

window.toggleGrid = function() {
    window.isGridVisible = !window.isGridVisible;
    window.chart.applyOptions({
        grid: { vertLines: { visible: window.isGridVisible }, horzLines: { visible: window.isGridVisible } }
    });
    window.saveUISettings();
};

window.updateColors = function() {
    const up = document.getElementById('upColor').value;
    const down = document.getElementById('downColor').value;
    if(window.candleSeries) {
        window.candleSeries.applyOptions({ upColor: up, downColor: down, wickUpColor: up, wickDownColor: down });
    }
    window.saveUISettings();
};

// Bridge C# (Ajout de la gestion du symbole pour les dessins)
window.updateChartData = function(data, symbol = "Default") {
    if (!window.chart || !window.candleSeries) return;
    
    window.isProcessingData = true;
    window.currentSymbol = symbol;
    
    // 1. Mettre à jour les données
    window.candleSeries.setData(data);
    
    // 2. FORCER LE RECALAGE (Crucial pour le changement de paire)
    window.chart.priceScale('right').applyOptions({ autoScale: true });
    window.chart.timeScale().fitContent();
    
    // 3. Charger les dessins de cette paire
    if(window.DrawingManager) window.DrawingManager.loadDrawings();
    
    setTimeout(() => window.isProcessingData = false, 500);
};

window.prependChartData = function(data) {
    const series = window.currentChartType === 'candles' ? window.candleSeries : window.lineSeries;
    const currentData = series.data();
    series.setData([...data, ...currentData]);
    setTimeout(() => window.isProcessingData = false, 500);
};

if (document.readyState === 'complete') {
    window.initChart();
} else {
    window.addEventListener('load', window.initChart);
}