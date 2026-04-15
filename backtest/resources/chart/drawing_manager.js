//point fonctionnel avec rectangle
window.DrawingManager = {
    mode: null,
    drawings: [], 
    tempStart: null,
    selectedIdx: null,
    dragTarget: null,
    setMode(id) {
        this.mode = (this.mode === id) ? null : id;
        const container = document.getElementById('chart-container');
        if (container) container.style.cursor = this.mode ? 'crosshair' : 'default';
        document.querySelectorAll('#drawing-tools button').forEach(b => b.classList.remove('active'));
        if (this.mode) document.getElementById(`btn-${id}`)?.classList.add('active');
        this.tempStart = null;
        window.DrawingUtils.updatePreview(null);
        this.redraw();
    },
	clearAllDrawings() {
        if (!confirm("Supprimer tous les dessins de cette paire ?")) return;

        // 1. Retirer chaque série du graphique
        this.drawings.forEach(d => {
            if (d.series) {
                window.chart.removeSeries(d.series);
            }
        });

        // 2. Vider le tableau en mémoire
        this.drawings = [];
        this.selectedIdx = null;

        // 3. Supprimer du localStorage pour cette paire spécifique
        if (window.currentSymbol) {
            localStorage.removeItem('Draw_' + window.currentSymbol);
        }

        // 4. Rafraîchir l'affichage
        this.redraw();
        console.log("Tous les dessins ont été supprimés pour " + window.currentSymbol);
    },
    getActiveSeries: () => window.candleSeries,

    addDrawing(type, start, end) {
        if (!start || !end || (start.time === end.time && start.price === end.price)) return;
        
        const typeConfig = window.DrawingUtils.types[type] || window.DrawingUtils.types.trendline;
        const series = typeConfig.create(window.chart);
        
        const drawing = { data: { type, start, end }, series: series };
        this.drawings.push(drawing);
        this.updateDrawingSeries(drawing);
        return drawing;
    },

    updateDrawingSeries(drawing) {
        if (!drawing.series) return;
        try {
            const typeConfig = window.DrawingUtils.types[drawing.data.type];
            typeConfig.update(drawing.series, drawing.data);
        } catch (e) { console.error("Erreur de rendu:", e); }
    },

    redraw() {
        this.drawings.forEach((d, i) => {
            const isSel = (i === this.selectedIdx);
            const color = isSel ? '#FFD700' : (window.isDarkMode ? '#00FFFF' : '#2196F3');
            
            if (d.data.type === 'rectangle') {
                d.series.applyOptions({
                    lineColor: color,
                    topColor: isSel ? 'rgba(255, 215, 0, 0.3)' : 'rgba(33, 150, 243, 0.2)',
                    bottomColor: isSel ? 'rgba(255, 215, 0, 0.3)' : 'rgba(33, 150, 243, 0.2)',
                });
            } else {
                d.series.applyOptions({ color: color, lineWidth: isSel ? 4 : 2 });
            }
        });
    },

    saveDrawings() {
        if (window.currentSymbol && window.currentSymbol !== "Default") {
            const data = this.drawings.map(d => ({ type: d.data.type, start: d.data.start, end: d.data.end }));
            localStorage.setItem('Draw_' + window.currentSymbol, JSON.stringify(data));
        }
    },

    loadDrawings() {
        if (!window.chart || !window.currentSymbol) return;
        this.drawings.forEach(d => { if(d.series) window.chart.removeSeries(d.series); });
        this.drawings = [];
        const saved = localStorage.getItem('Draw_' + window.currentSymbol);
        if (saved) {
            try {
                JSON.parse(saved).forEach(d => this.addDrawing(d.type, d.start, d.end));
            } catch (e) { console.error(e); }
        }
    },

    deleteSelected() {
        if (this.selectedIdx === null) return;
        window.chart.removeSeries(this.drawings[this.selectedIdx].series);
        this.drawings.splice(this.selectedIdx, 1);
        this.selectedIdx = null;
        this.saveDrawings();
        this.redraw();
    }
};

// Initialisation de la synchro (à appeler une fois le graphique créé)
window.syncDrawingWithChart = function() {
    const container = document.getElementById('chart-container');
    const mainSeries = window.DrawingManager.getActiveSeries();

    container.addEventListener('mousedown', (e) => {
        if (window.DrawingManager.mode || window.DrawingManager.selectedIdx === null) return;
        const rect = container.getBoundingClientRect();
        const x = e.clientX - rect.left, y = e.clientY - rect.top;
        const drawing = window.DrawingManager.drawings[window.DrawingManager.selectedIdx];
        const d = drawing.data;
        const x1 = window.chart.timeScale().timeToCoordinate(d.start.time);
        const x2 = window.chart.timeScale().timeToCoordinate(d.end.time);
        const y1 = mainSeries.priceToCoordinate(d.start.price);
        const y2 = mainSeries.priceToCoordinate(d.end.price);

        if (Math.hypot(x - x1, y - y1) < 15) window.DrawingManager.dragTarget = { type: 'p1', index: window.DrawingManager.selectedIdx };
        else if (Math.hypot(x - x2, y - y2) < 15) window.DrawingManager.dragTarget = { type: 'p2', index: window.DrawingManager.selectedIdx };
        else window.DrawingManager.dragTarget = { type: 'line', index: window.DrawingManager.selectedIdx };
    });

    window.addEventListener('mouseup', () => {
        if (window.DrawingManager.dragTarget) { window.DrawingManager.saveDrawings(); window.DrawingManager.dragTarget = null; }
    });

    window.chart.subscribeClick(param => {
        if (!param.point || window.DrawingManager.dragTarget) return;
        const price = mainSeries.coordinateToPrice(param.point.y);
        if (window.DrawingManager.mode) {
            if (!window.DrawingManager.tempStart) {
                window.DrawingManager.tempStart = { time: param.time, price, x: param.point.x, y: param.point.y };
            } else {
                window.DrawingManager.addDrawing(window.DrawingManager.mode, window.DrawingManager.tempStart, {time: param.time, price});
                window.DrawingManager.tempStart = null;
                window.DrawingUtils.updatePreview(null);
                window.DrawingManager.setMode(null);
                window.DrawingManager.saveDrawings();
            }
        } else {
            let found = null;
            window.DrawingManager.drawings.forEach((dr, i) => {
                const x1 = window.chart.timeScale().timeToCoordinate(dr.data.start.time);
                const x2 = window.chart.timeScale().timeToCoordinate(dr.data.end.time);
                const y1 = mainSeries.priceToCoordinate(dr.data.start.price);
                const y2 = mainSeries.priceToCoordinate(dr.data.end.price);
                const t = ((param.point.x - x1) * (x2 - x1) + (param.point.y - y1) * (y2 - y1)) / (Math.pow(x2 - x1, 2) + Math.pow(y2 - y1, 2));
                const cx = x1 + Math.max(0, Math.min(1, t)) * (x2 - x1), cy = y1 + Math.max(0, Math.min(1, t)) * (y2 - y1);
                if (Math.hypot(param.point.x - cx, param.point.y - cy) < 15) found = i;
            });
            window.DrawingManager.selectedIdx = found;
            window.DrawingManager.redraw();
        }
    });

    window.chart.subscribeCrosshairMove(param => {
        if (!param.point) return;
        if (window.DrawingManager.mode && window.DrawingManager.tempStart) window.DrawingUtils.updatePreview(window.DrawingManager.mode, window.DrawingManager.tempStart, param.point);
        if (window.DrawingManager.dragTarget) {
            const dr = window.DrawingManager.drawings[window.DrawingManager.dragTarget.index];
            const p = mainSeries.coordinateToPrice(param.point.y), t = param.time;
            if (window.DrawingManager.dragTarget.type === 'p1') dr.data.start = { time: t, price: p };
            else if (window.DrawingManager.dragTarget.type === 'p2') dr.data.end = { time: t, price: p };
            else {
                // Déplacement global simplifié
                const d1 = Math.abs(param.point.x - window.chart.timeScale().timeToCoordinate(dr.data.start.time));
                const d2 = Math.abs(param.point.x - window.chart.timeScale().timeToCoordinate(dr.data.end.time));
                if (d1 < d2) dr.data.start = { time: t, price: p }; else dr.data.end = { time: t, price: p };
            }
            window.DrawingManager.updateDrawingSeries(dr);
        }
    });
};