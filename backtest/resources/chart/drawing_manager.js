window.DrawingManager = {
    mode: null,
    drawings: [], 
    tempStart: null,
    selectedIdx: null,
    dragTarget: null, // Sera { type: 'p1'|'p2'|'line', index: x, offset: {x, y} }

    setMode(id) {
        this.mode = (this.mode === id) ? null : id;
        const container = document.getElementById('chart-container');
        if (container) container.style.cursor = this.mode ? 'crosshair' : 'default';

        document.querySelectorAll('#drawing-tools button').forEach(b => b.classList.remove('active'));
        if (this.mode) document.getElementById(`btn-${id}`)?.classList.add('active');

        this.selectedIdx = null;
        this.tempStart = null;
        this.updateSVG(null); 
        this.redraw();
    },

    getActiveSeries: () => window.candleSeries,

    createLineSeries() {
        if (!window.chart) return null;
        return window.chart.addLineSeries({
            color: window.isDarkMode ? '#00FFFF' : '#2196F3',
            lineWidth: 2,
            priceLineVisible: false,
            lastPriceAnimation: 0,
            crosshairMarkerVisible: false,
            autoscaleInfoProvider: () => null,
        });
    },

    updateSVG(p1, p2) {
        let svgLine = document.getElementById('temp-line');
        if (!svgLine) {
            const container = document.getElementById('chart-container');
            container.insertAdjacentHTML('beforeend', `
                <svg id="drawing-svg" style="position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:1000;">
                    <line id="temp-line" stroke="${window.isDarkMode ? '#00FFFF' : '#2196F3'}" stroke-width="2" stroke-dasharray="5,5" style="display:none"/>
                </svg>`);
            svgLine = document.getElementById('temp-line');
        }
        if (!p1 || !p2) { svgLine.style.display = 'none'; return; }
        svgLine.setAttribute('x1', p1.x); svgLine.setAttribute('y1', p1.y);
        svgLine.setAttribute('x2', p2.x); svgLine.setAttribute('y2', p2.y);
        svgLine.style.display = 'block';
    },

    saveDrawings() {
        if (window.currentSymbol && window.currentSymbol !== "Default") {
            const data = this.drawings.map(d => ({ type: d.type, start: d.start, end: d.end }));
            localStorage.setItem('Draw_' + window.currentSymbol, JSON.stringify(data));
        }
    },

    loadDrawings() {
        if (!window.chart || !window.currentSymbol) return;
        this.drawings.forEach(d => { if(d.series) window.chart.removeSeries(d.series); });
        this.drawings = [];
        const saved = localStorage.getItem('Draw_' + window.currentSymbol);
        if (!saved) return;
        try {
            const rawData = JSON.parse(saved);
            rawData.forEach(d => {
                const series = this.createLineSeries();
                series.setData([{ time: d.start.time, value: d.start.price }, { time: d.end.time, value: d.end.price }]);
                this.drawings.push({ ...d, series });
            });
        } catch (e) { console.error(e); }
    },

    deleteSelected() {
        if (this.selectedIdx === null) return;
        window.chart.removeSeries(this.drawings[this.selectedIdx].series);
        this.drawings.splice(this.selectedIdx, 1);
        this.selectedIdx = null;
        this.saveDrawings();
        this.redraw();
    },

    redraw() {
        this.drawings.forEach((d, i) => {
            const isSel = (i === this.selectedIdx);
            d.series.applyOptions({
                lineWidth: isSel ? 4 : 2,
                color: isSel ? '#FFD700' : (window.isDarkMode ? '#00FFFF' : '#2196F3')
            });
        });
    }
};

window.syncDrawingWithChart = function() {
    const container = document.getElementById('chart-container');
    const mainSeries = window.DrawingManager.getActiveSeries();

    // --- GESTION DU MOUSE DOWN (DÉBUT DU DÉPLACEMENT) ---
    container.addEventListener('mousedown', (e) => {
        if (window.DrawingManager.mode || window.DrawingManager.selectedIdx === null) return;

        const rect = container.getBoundingClientRect();
        const x = e.clientX - rect.left;
        const y = e.clientY - rect.top;

        const d = window.DrawingManager.drawings[window.DrawingManager.selectedIdx];
        const x1 = window.chart.timeScale().timeToCoordinate(d.start.time);
        const y1 = mainSeries.priceToCoordinate(d.start.price);
        const x2 = window.chart.timeScale().timeToCoordinate(d.end.time);
        const y2 = mainSeries.priceToCoordinate(d.end.price);

        // 1. Vérifier si on clique sur un point d'ancrage (Redimensionner)
        if (Math.hypot(x - x1, y - y1) < 15) {
            window.DrawingManager.dragTarget = { type: 'p1', index: window.DrawingManager.selectedIdx };
        } else if (Math.hypot(x - x2, y - y2) < 15) {
            window.DrawingManager.dragTarget = { type: 'p2', index: window.DrawingManager.selectedIdx };
        } 
        // 2. Vérifier si on clique sur la ligne (Déplacer toute la ligne)
        else {
            // Logique simplifiée : si sélectionné et on clique proche, on déplace
            window.DrawingManager.dragTarget = { type: 'line', index: window.DrawingManager.selectedIdx };
        }
    });

    window.addEventListener('mouseup', () => {
        if (window.DrawingManager.dragTarget) {
            window.DrawingManager.saveDrawings();
            window.DrawingManager.dragTarget = null;
        }
    });

    window.chart.subscribeClick(param => {
        if (!param.point || window.DrawingManager.dragTarget) return;
        const price = mainSeries.coordinateToPrice(param.point.y);

        if (window.DrawingManager.mode) {
            if (!window.DrawingManager.tempStart) {
                window.DrawingManager.tempStart = { time: param.time, price, x: param.point.x, y: param.point.y };
            } else {
                const series = window.DrawingManager.createLineSeries();
                series.setData([{ time: window.DrawingManager.tempStart.time, value: window.DrawingManager.tempStart.price }, { time: param.time, value: price }]);
                window.DrawingManager.drawings.push({ start: {time: window.DrawingManager.tempStart.time, price: window.DrawingManager.tempStart.price}, end: {time: param.time, price}, series });
                window.DrawingManager.tempStart = null;
                window.DrawingManager.updateSVG(null);
                window.DrawingManager.setMode(null);
                window.DrawingManager.saveDrawings();
            }
        } else {
            // Sélection par proximité
            let foundIdx = null;
            window.DrawingManager.drawings.forEach((d, i) => {
                const x1 = window.chart.timeScale().timeToCoordinate(d.start.time);
                const x2 = window.chart.timeScale().timeToCoordinate(d.end.time);
                const y1 = mainSeries.priceToCoordinate(d.start.price);
                const y2 = mainSeries.priceToCoordinate(d.end.price);
                if (x1 === null || x2 === null) return;
                const dx = x2 - x1, dy = y2 - y1;
                const t = ((param.point.x - x1) * dx + (param.point.y - y1) * dy) / (dx * dx + dy * dy);
                const cx = x1 + Math.max(0, Math.min(1, t)) * dx;
                const cy = y1 + Math.max(0, Math.min(1, t)) * dy;
                if (Math.hypot(param.point.x - cx, param.point.y - cy) < 10) foundIdx = i;
            });
            window.DrawingManager.selectedIdx = foundIdx;
            window.DrawingManager.redraw();
        }
    });

    window.chart.subscribeCrosshairMove(param => {
        if (!param.point) return;

        // Aperçu du dessin
        if (window.DrawingManager.mode && window.DrawingManager.tempStart) {
            window.DrawingManager.updateSVG(window.DrawingManager.tempStart, param.point);
        }

        // --- LOGIQUE DE DÉPLACEMENT / REDIMENSIONNEMENT ---
        if (window.DrawingManager.dragTarget) {
            const d = window.DrawingManager.drawings[window.DrawingManager.dragTarget.index];
            const price = mainSeries.coordinateToPrice(param.point.y);
            const time = param.time;

            if (window.DrawingManager.dragTarget.type === 'p1') {
                d.start = { time, price };
            } else if (window.DrawingManager.dragTarget.type === 'p2') {
                d.end = { time, price };
            } else if (window.DrawingManager.dragTarget.type === 'line') {
                // Pour déplacer la ligne entière, on calcule le delta (plus complexe en temps/prix)
                // Ici version simple : déplace le point le plus proche vers la souris
                const dist1 = Math.abs(param.point.x - window.chart.timeScale().timeToCoordinate(d.start.time));
                const dist2 = Math.abs(param.point.x - window.chart.timeScale().timeToCoordinate(d.end.time));
                if (dist1 < dist2) d.start = { time, price }; else d.end = { time, price };
            }

            d.series.setData([
                { time: d.start.time, value: d.start.price },
                { time: d.end.time, value: d.end.price }
            ]);
        }
    });
};