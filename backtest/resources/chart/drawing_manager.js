window.DrawingManager = {
    mode: null,
    drawings: [], 
    tempStart: null,
    selectedIdx: null,
    series: null,
	chart: null,

   init(chart, series) { // Reçoit les deux maintenant
        this.chart = chart;
        this.series = series;
        
        // On passe manager, chart et series au plugin
        const plugin = new DrawingPlugin(this, chart, series);
        series.attachPrimitive(plugin);
    },

    setMode(id) {
        this.mode = (this.mode === id) ? null : id;
        document.body.style.cursor = this.mode ? 'crosshair' : 'default';
        
        // UI Update
        document.querySelectorAll('#drawing-tools button').forEach(b => b.classList.remove('active'));
        if (this.mode) {
            const btn = document.getElementById(`btn-${id}`);
            if (btn) btn.classList.add('active');
        }
        
        this.tempStart = null;
        if (window.DrawingUtils) window.DrawingUtils.updatePreview(null);
    },

    addDrawing(type, start, end) {
        this.drawings.push({ 
            data: { 
                type, 
                start: { time: start.time, price: start.price }, 
                end: { time: end.time, price: end.price } 
            } 
        });
        this.save();
        this.series.applyOptions({}); // Force le rafraîchissement du plugin
    },

    deleteSelected() {
        if (this.selectedIdx !== null) {
            this.drawings.splice(this.selectedIdx, 1);
            this.selectedIdx = null;
            this.save();
            this.series.applyOptions({});
        }
    },

    clearAllDrawings() {
        if (this.drawings.length > 0 && confirm("Supprimer tous les dessins ?")) {
            this.drawings = [];
            this.save();
            this.series.applyOptions({});
            window.cyberLog("Tous les dessins supprimés.");
        }
    },

    save() {
        if (!window.currentSymbol) return;
        const key = 'Drawings_' + window.currentSymbol;
        localStorage.setItem(key, JSON.stringify(this.drawings.map(d => d.data)));
    },

    load() {
        if (!window.currentSymbol) return;
        const key = 'Drawings_' + window.currentSymbol;
        const saved = localStorage.getItem(key);
        this.drawings = saved ? JSON.parse(saved).map(d => ({ data: d })) : [];
        if (this.series) this.series.applyOptions({});
        window.cyberLog(`Dessins chargés (${this.drawings.length})`);
    }
};

window.syncDrawingWithChart = function() {
    window.chart.subscribeClick(param => {
        if (!param.point || !window.candleSeries) return;
        const price = window.candleSeries.coordinateToPrice(param.point.y);

        if (window.DrawingManager.mode) {
            if (!window.DrawingManager.tempStart) {
                window.DrawingManager.tempStart = { time: param.time, price, x: param.point.x, y: param.point.y };
            } else {
                window.DrawingManager.addDrawing(window.DrawingManager.mode, window.DrawingManager.tempStart, { time: param.time, price });
                window.DrawingManager.setMode(null);
            }
        } else {
            // Hit-test pour sélection
            let found = null;
            window.DrawingManager.drawings.forEach((dr, i) => {
                const x1 = window.chart.timeScale().timeToCoordinate(dr.data.start.time);
                const x2 = window.chart.timeScale().timeToCoordinate(dr.data.end.time);
                const y1 = window.candleSeries.priceToCoordinate(dr.data.start.price);
                const y2 = window.candleSeries.priceToCoordinate(dr.data.end.price);
                const dist = window.DrawingUtils.getDistanceToSegment(param.point.x, param.point.y, x1, y1, x2, y2);
                if (dist < 15) found = i;
            });
            window.DrawingManager.selectedIdx = found;
            window.candleSeries.applyOptions({});
        }
    });

    window.chart.subscribeCrosshairMove(param => {
        if (param.point && window.DrawingManager.mode && window.DrawingManager.tempStart) {
            window.DrawingUtils.updatePreview(window.DrawingManager.mode, window.DrawingManager.tempStart, param.point);
        }
    });

    // Touche Delete pour supprimer
    window.addEventListener('keydown', (e) => {
        if (e.key === 'Delete' || e.key === 'Backspace') window.DrawingManager.deleteSelected();
    });
};