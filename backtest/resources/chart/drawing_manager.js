// drawing_manager.js
window.currentSymbol = "Default";

window.DrawingManager = {
    mode: null,
    drawings: [],
    tempStart: null,
    selectedIdx: null,
    isDragging: false,
    dragPart: null,
    tools: {},

    registerTool(id, config) {
        this.tools[id] = config;
        const btn = document.createElement('button');
        btn.id = `btn-${id}`;
        btn.innerHTML = `${config.icon} ${config.name}`;
        btn.onclick = () => this.setMode(id);
        const toolsGroup = document.getElementById('drawing-tools');
        if (toolsGroup) toolsGroup.prepend(btn);
    },

    setMode(id) {
        this.mode = (this.mode === id) ? null : id;
        const canvas = document.getElementById('drawing-canvas');
        canvas.className = this.mode ? 'active' : '';
        document.querySelectorAll('#drawing-tools button').forEach(b => b.classList.remove('active'));
        if (this.mode) document.getElementById(`btn-${id}`).classList.add('active');
        this.selectedIdx = null;
        this.redraw();
    },

    saveDrawings() {
        if (!window.currentSymbol) return;
        const data = JSON.stringify(this.drawings);
        localStorage.setItem('Draw_' + window.currentSymbol, data);
    },

    loadDrawings() {
        const saved = localStorage.getItem('Draw_' + window.currentSymbol);
        this.drawings = saved ? JSON.parse(saved) : [];
        this.redraw();
    },

    getPix(pt) {
        if (!pt || !window.chart || !window.candleSeries) return null;
        
        // Coordonnée X basée sur le temps
        const x = window.chart.timeScale().timeToCoordinate(pt.time);
        // Coordonnée Y basée sur le prix
        const y = window.candleSeries.priceToCoordinate(pt.price);
        
        if (x === null || y === null) return null;
        return { x, y };
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
                if (isSelected) this.drawHandles(ctx, p1, p2);
            }
        });
    },

    drawHandles(ctx, p1, p2) {
        ctx.fillStyle = "white";
        ctx.strokeStyle = "black";
        ctx.lineWidth = 1;
        [p1, p2].forEach(p => {
            ctx.beginPath();
            ctx.arc(p.x, p.y, 5, 0, Math.PI * 2);
            ctx.fill();
            ctx.stroke();
        });
    }
};

// --- INITIALISATION ET SYNCHRONISATION ---

function syncDrawingWithChart() {
    if (window.chart) {
        // Synchronisation horizontale (Scroll/Zoom temps)
        window.chart.timeScale().subscribeVisibleTimeRangeChange(() => {
            window.DrawingManager.redraw();
        });

        // Synchronisation verticale (Scroll/Zoom prix) - FIX POUR LE FLOTTEMENT VERTICAL
        window.chart.priceScale('right').subscribeVisiblePriceRangeChange(() => {
            window.DrawingManager.redraw();
        });
        
        window.cyberLog("Synchronisation Chart/Canvas active");
    }
}

function resizeCanvas() {
    const container = document.getElementById('chart-container');
    const canvas = document.getElementById('drawing-canvas');
    if (!container || !canvas) return;

    const w = container.clientWidth;
    const h = container.clientHeight;

    // Mise à jour de la résolution interne
    canvas.width = w;
    canvas.height = h;
    
    // Mise à jour de la taille d'affichage CSS (Fix zone morte Chromium)
    canvas.style.width = w + "px";
    canvas.style.height = h + "px";

    if (window.chart) {
        window.chart.applyOptions({ width: w, height: h });
    }
    window.DrawingManager.redraw();
}

// Gestion des événements de chargement
window.addEventListener('load', () => {
    setTimeout(() => {
        resizeCanvas();
        syncDrawingWithChart();
    }, 500);
});

window.addEventListener('resize', resizeCanvas);

// --- GESTION DES ÉVÉNEMENTS SOURIS ---

const canvas = document.getElementById('drawing-canvas');

canvas.addEventListener('mousedown', e => {
    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;
    
    const time = window.chart.timeScale().coordinateToTime(x);
    const series = (window.currentChartType === 'candles') ? window.candleSeries : window.lineSeries;
    const price = series.coordinateToPrice(y);

    if (!time || price === null) return;

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
    } else {
        let found = false;
        window.DrawingManager.drawings.forEach((d, i) => {
            const p1 = window.DrawingManager.getPix(d.start);
            const p2 = window.DrawingManager.getPix(d.end);
            
            if (p1 && Math.abs(x - p1.x) < 15 && Math.abs(y - p1.y) < 15) {
                window.DrawingManager.selectedIdx = i;
                window.DrawingManager.isDragging = true;
                window.DrawingManager.dragPart = 'start';
                found = true;
            } else if (p2 && Math.abs(x - p2.x) < 15 && Math.abs(y - p2.y) < 15) {
                window.DrawingManager.selectedIdx = i;
                window.DrawingManager.isDragging = true;
                window.DrawingManager.dragPart = 'end';
                found = true;
            }
        });
        if (!found) window.DrawingManager.selectedIdx = null;
    }
    window.DrawingManager.redraw();
});

canvas.addEventListener('mousemove', e => {
    if (!window.DrawingManager.isDragging || window.DrawingManager.selectedIdx === null) return;

    const rect = canvas.getBoundingClientRect();
    const x = e.clientX - rect.left;
    const y = e.clientY - rect.top;

    const time = window.chart.timeScale().coordinateToTime(x);
    const series = (window.currentChartType === 'candles') ? window.candleSeries : window.lineSeries;
    const price = series.coordinateToPrice(y);

    if (time && price !== null) {
        const drawing = window.DrawingManager.drawings[window.DrawingManager.selectedIdx];
        if (window.DrawingManager.dragPart === 'start') drawing.start = { time, price };
        else drawing.end = { time, price };
        window.DrawingManager.redraw();
    }
});

window.addEventListener('mouseup', () => {
    if (window.DrawingManager.isDragging) window.DrawingManager.saveDrawings();
    window.DrawingManager.isDragging = false;
    window.DrawingManager.dragPart = null;
});

window.addEventListener('keydown', e => {
    if ((e.key === "Delete" || e.key === "Backspace") && window.DrawingManager.selectedIdx !== null) {
        window.DrawingManager.drawings.splice(window.DrawingManager.selectedIdx, 1);
        window.DrawingManager.selectedIdx = null;
        window.DrawingManager.saveDrawings();
        window.DrawingManager.redraw();
    }
});

// --- UTILITAIRES ---

window.clearDrawings = () => { 
    window.DrawingManager.drawings = []; 
    window.DrawingManager.saveDrawings();
    window.DrawingManager.redraw(); 
    window.cyberLog("Tous les dessins supprimés");
};