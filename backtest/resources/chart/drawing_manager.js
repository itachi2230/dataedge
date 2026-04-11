window.currentSymbol = "Default";

window.DrawingManager = {
    mode: null,
    drawings: [],
    tempStart: null,
    selectedIdx: null,
    isDragging: false,
    dragPart: null, // 'start', 'end', ou 'move'
    dragOffset: null, // Pour le déplacement global
    tools: {},

    registerTool(id, config) {
        this.tools[id] = config;
        const btn = document.createElement('button');
        btn.id = `btn-${id}`;
        btn.innerHTML = `${config.icon} ${config.name}`;
        btn.onclick = () => this.setMode(id);
        document.getElementById('drawing-tools')?.prepend(btn);
    },

    setMode(id) {
        this.mode = (this.mode === id) ? null : id;
        const canvas = document.getElementById('drawing-canvas');
        if (canvas) canvas.className = this.mode ? 'active' : '';
        
        document.querySelectorAll('#drawing-tools button').forEach(b => b.classList.remove('active'));
        if (this.mode) document.getElementById(`btn-${id}`)?.classList.add('active');
        
        this.selectedIdx = null;
        this.tempStart = null;
        this.redraw();
    },

    saveDrawings() {
        if (window.currentSymbol) {
            localStorage.setItem('Draw_' + window.currentSymbol, JSON.stringify(this.drawings));
        }
    },

    loadDrawings() {
        const saved = localStorage.getItem('Draw_' + window.currentSymbol);
        this.drawings = saved ? JSON.parse(saved) : [];
        this.redraw();
    },

    getActiveSeries() {
        return (window.currentChartType === 'candles') ? window.candleSeries : window.lineSeries;
    },

    // Convertit les coordonnées logiques (Time/Price) en Pixels (X/Y)
    getPix(pt) {
        const series = this.getActiveSeries();
        if (!pt || !window.chart || !series) return null;
        const x = window.chart.timeScale().timeToCoordinate(pt.time);
        const y = series.priceToCoordinate(pt.price);
        return (x === null || y === null) ? null : { x, y };
    },

    redraw() {
        const canvas = document.getElementById('drawing-canvas');
        if (!canvas) return;
        const ctx = canvas.getContext('2d');
        ctx.clearRect(0, 0, canvas.width, canvas.height);

        this.drawings.forEach((d, i) => {
            const p1 = this.getPix(d.start);
            const p2 = this.getPix(d.end);
            if (p1 && p2 && this.tools[d.type]) {
                const isSelected = (this.selectedIdx === i);
                this.tools[d.type].render(ctx, p1, p2, window.isDarkMode, false, isSelected);
            }
        });

        // Dessiner le trait temporaire pendant la création
        if (this.mode && this.tempStart) {
            const p1 = this.getPix(this.tempStart);
            const p2 = this.lastMousePos; // Position live de la souris
            if (p1 && p2 && this.tools[this.mode]) {
                this.tools[this.mode].render(ctx, p1, p2, window.isDarkMode, true, false);
            }
        }
    }
};

// --- LOGIQUE DE SYNCHRONISATION ET REDIMENSIONNEMENT ---

function syncDrawingWithChart() {
    if (!window.chart) return;
    window.chart.timeScale().subscribeVisibleTimeRangeChange(() => window.DrawingManager.redraw());
    window.chart.subscribeCrosshairMove(() => window.DrawingManager.redraw());
    try {
        window.chart.priceScale('right').subscribeVisiblePriceRangeChange(() => window.DrawingManager.redraw());
    } catch(e) {}
}

function resizeCanvas() {
    const container = document.getElementById('chart-container');
    const canvas = document.getElementById('drawing-canvas');
    if (!container || !canvas) return;
    canvas.width = container.clientWidth;
    canvas.height = container.clientHeight;
    window.DrawingManager.redraw();
}

window.addEventListener('resize', resizeCanvas);
window.addEventListener('load', () => setTimeout(resizeCanvas, 100));

// --- GESTION DES ÉVÉNEMENTS SOURIS ---

const canvas = document.getElementById('drawing-canvas');

canvas.addEventListener('mousedown', e => {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    
    const time = window.chart.timeScale().coordinateToTime(x);
    const series = window.DrawingManager.getActiveSeries();
    const price = series ? series.coordinateToPrice(y) : null;
    if (!time || price === null) return;

    // MODE CRÉATION
    if (window.DrawingManager.mode) {
        if (!window.DrawingManager.tempStart) {
            window.DrawingManager.tempStart = { time, price };
        } else {
            window.DrawingManager.drawings.push({ 
                type: window.DrawingManager.mode, 
                start: window.DrawingManager.tempStart, 
                end: { time, price } 
            });
            window.DrawingManager.tempStart = null;
            window.DrawingManager.setMode(null);
            window.DrawingManager.saveDrawings();
        }
    } 
    // MODE SÉLECTION / DRAG
    else {
        let foundIdx = -1;
        let foundPart = null;

        // On boucle à l'envers pour sélectionner le dernier dessiné (au dessus)
        for (let i = window.DrawingManager.drawings.length - 1; i >= 0; i--) {
            const d = window.DrawingManager.drawings[i];
            const p1 = window.DrawingManager.getPix(d.start);
            const p2 = window.DrawingManager.getPix(d.end);
            const tool = window.DrawingManager.tools[d.type];

            if (p1 && p2 && tool && tool.isPointNear) {
                const hit = tool.isPointNear({ x, y }, p1, p2);
                if (hit) {
                    foundIdx = i;
                    foundPart = hit;
                    break;
                }
            }
        }

        window.DrawingManager.selectedIdx = (foundIdx !== -1) ? foundIdx : null;
        
        if (foundIdx !== -1) {
            window.DrawingManager.isDragging = true;
            window.DrawingManager.dragPart = foundPart;
            
            // Si on déplace tout l'objet, on stocke l'offset initial
            if (foundPart === 'move') {
                const d = window.DrawingManager.drawings[foundIdx];
                window.DrawingManager.dragOffset = {
                    startTime: d.start.time - time,
                    startPrice: d.start.price - price,
                    endTime: d.end.time - time,
                    endPrice: d.end.price - price
                };
            }
        }
    }
    window.DrawingManager.redraw();
});

canvas.addEventListener('mousemove', e => {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    
    window.DrawingManager.lastMousePos = { x, y };

    // Gestion des curseurs visuels
    if (!window.DrawingManager.isDragging && !window.DrawingManager.mode) {
        let hover = false;
        window.DrawingManager.drawings.forEach(d => {
            const p1 = window.DrawingManager.getPix(d.start);
            const p2 = window.DrawingManager.getPix(d.end);
            const tool = window.DrawingManager.tools[d.type];
            if (p1 && p2 && tool?.isPointNear) {
                const hit = tool.isPointNear({ x, y }, p1, p2);
                if (hit) {
                    canvas.style.cursor = (hit === 'move') ? 'move' : 'pointer';
                    hover = true;
                }
            }
        });
        if (!hover) canvas.style.cursor = 'crosshair';
    }

    // Si on est en train de dessiner ou de glisser
    if (window.DrawingManager.mode && window.DrawingManager.tempStart) {
        window.DrawingManager.redraw();
    }

    if (window.DrawingManager.isDragging && window.DrawingManager.selectedIdx !== null) {
        const time = window.chart.timeScale().coordinateToTime(x);
        const series = window.DrawingManager.getActiveSeries();
        const price = series.coordinateToPrice(y);
        
        if (!time || price === null) return;

        const d = window.DrawingManager.drawings[window.DrawingManager.selectedIdx];

        if (window.DrawingManager.dragPart === 'start') {
            d.start = { time, price };
        } else if (window.DrawingManager.dragPart === 'end') {
            d.end = { time, price };
        } else if (window.DrawingManager.dragPart === 'move') {
            const off = window.DrawingManager.dragOffset;
            d.start = { time: time + off.startTime, price: price + off.startPrice };
            d.end = { time: time + off.endTime, price: price + off.endPrice };
        }
        window.DrawingManager.redraw();
    }
});

window.addEventListener('mouseup', () => {
    if (window.DrawingManager.isDragging) {
        window.DrawingManager.saveDrawings();
    }
    window.DrawingManager.isDragging = false;
    window.DrawingManager.dragPart = null;
});

// Suppression avec la touche Delete ou Backspace
window.addEventListener('keydown', e => {
    if ((e.key === 'Delete' || e.key === 'Backspace') && window.DrawingManager.selectedIdx !== null) {
        window.DrawingManager.drawings.splice(window.DrawingManager.selectedIdx, 1);
        window.DrawingManager.selectedIdx = null;
        window.DrawingManager.saveDrawings();
        window.DrawingManager.redraw();
    }
});