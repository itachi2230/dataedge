window.DrawingManager = {
    mode: null,
    drawings: [],
    points: [], // Stocke les points temporaires (multi-clics)
    selectedIdx: null,
    dragState: null,
    series: null,
    chart: null,

    init(chart, series) {
        this.chart = chart; this.series = series;
        const plugin = new DrawingPlugin(this, chart, series);
        series.attachPrimitive(plugin);
        this.load();
    },

    setMode(id) {
        this.mode = (this.mode === id) ? null : id;
        this.points = [];
        this.selectedIdx = null;
        document.body.style.cursor = this.mode ? 'crosshair' : 'default';
        this.updateSidebarUI();
        if (window.DrawingUtils) window.DrawingUtils.updatePreview(null);
        this.series.applyOptions({});
    },

    updateSidebarUI() {
        document.querySelectorAll('.side-btn').forEach(btn => {
            btn.classList.remove('active');
            if (!this.mode && btn.title === "Cross") btn.classList.add('active');
            else if (this.mode && btn.id === `btn-${this.mode}`) btn.classList.add('active');
        });
    },

    addPoint(time, price) {
        if (!this.mode) return;
        const conf = window.DrawingConfigs[this.mode];
        if (!conf) return;

        this.points.push({ time, price });

        // Vérification du nombre de clics requis par la config
        if (this.points.length === conf.clicks) {
            this.finishDrawing();
        }
    },

    finishDrawing() {
    if (this.points.length > 0) {
        const type = this.mode;
        this.drawings.push({ 
            data: { type: type, points: [...this.points] } 
        });
        this.save();
        
        const lastIdx = this.drawings.length - 1;
        
        // On attend que l'événement de clic soit totalement terminé
        if (type === 'text') {
            setTimeout(() => this.editText(lastIdx), 100);
        }
    }
    this.setMode(null);
},
	editText(index) {
    const dr = this.drawings[index];
    if (!dr || dr.data.type !== 'text') return;

    // Supprime un ancien éditeur s'il existe
    const old = document.getElementById('temp-text-editor');
    if (old) old.remove();

    const container = document.getElementById('chart-container');
    
    // Création d'un mini-champ en haut du graphique
    const input = document.createElement('input');
    input.id = 'temp-text-editor';
    input.type = 'text';
    input.value = dr.data.points[0].text || "";
    input.placeholder = "Tapez votre texte ici...";
    
    input.style.cssText = `
        position: absolute;
        top: 10px;
        left: 50%;
        transform: translateX(-50%);
        z-index: 2000;
        background: #1e222d;
        color: #00FFFF;
        border: 2px solid #00FFFF;
        padding: 8px 15px;
        border-radius: 4px;
        outline: none;
        box-shadow: 0 4px 15px rgba(0,0,0,0.5);
        min-width: 200px;
    `;

    container.appendChild(input);
    input.focus();
    input.select();

    const saveAndClose = () => {
        if (input.value.trim() !== "") {
            dr.data.points[0].text = input.value;
        }
        input.remove();
        this.save();
        this.series.applyOptions({}); // Rafraîchit le texte sur le canvas
    };

    // Events
    input.onblur = saveAndClose;
    input.onkeydown = (e) => {
        if (e.key === 'Enter') saveAndClose();
        if (e.key === 'Escape') { input.value = dr.data.points[0].text || ""; input.remove(); }
    };
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
        if (confirm("Supprimer tous les dessins ?")) {
            this.drawings = []; this.selectedIdx = null;
            this.save(); this.series.applyOptions({});
        }
    },

    save() {
        if (!window.currentSymbol) return;
        localStorage.setItem('Drawings_' + window.currentSymbol, JSON.stringify(this.drawings.map(d => d.data)));
    },

    load() {
        if (!window.currentSymbol) return;
        const saved = localStorage.getItem('Drawings_' + window.currentSymbol);
        this.drawings = saved ? JSON.parse(saved).map(d => ({ data: d })) : [];
        if (this.series) this.series.applyOptions({});
    }
};

window.syncDrawingWithChart = function() {
    const mgr = window.DrawingManager;
    const container = document.getElementById('chart-container');

    window.chart.subscribeClick(param => {
        // 1. Debugging de base
        if (!param || !param.point) {
            window.cyberLog("Clic hors zone graphique.");
            mgr.selectedIdx = null;
            mgr.series.applyOptions({});
            return;
        }

        const price = window.candleSeries.coordinateToPrice(param.point.y);
        window.cyberLog(`Clic détecté - Mode: ${mgr.mode || 'Sélection'} | Prix: ${price.toFixed(2)}`);

        // 2. Si on est en train de déplacer/redimensionner, on ne fait rien
        if (mgr.dragState) {
            window.cyberLog("relache mode");
            mgr.selectedIdx = null;
            mgr.series.applyOptions({});
            return;
        }

        if (mgr.mode) {
            // MODE DESSIN
            window.cyberLog(`Ajout point pour l'outil: ${mgr.mode}`);
            mgr.addPoint(param.time, price);
        } else {
            // MODE SÉLECTION
            let found = null;
            
            mgr.drawings.forEach((dr, i) => {
                const pts = dr.data.points.map(p => ({
                    x: window.chart.timeScale().timeToCoordinate(p.time),
                    y: window.candleSeries.priceToCoordinate(p.price)
                }));
                
                const isOverStart = pts[0] && window.DrawingUtils.isOverPoint(param.point.x, param.point.y, pts[0].x, pts[0].y);
                const isOverSegment = pts[0] && pts[1] && window.DrawingUtils.getDistanceToSegment(param.point.x, param.point.y, pts[0].x, pts[0].y, pts[1].x, pts[1].y) < 8;

                if (isOverStart || isOverSegment) {
                    found = i;
                }
            });

            if (found !== null) {
                window.cyberLog(`Dessin trouvé à l'index: ${found}`);
            } else {
                window.cyberLog("Aucun dessin touché. Désélection.");
            }

            // Mise à jour de la sélection
            mgr.selectedIdx = found;
            mgr.series.applyOptions({});
        }
    });

    container.addEventListener('mousedown', e => {
        if (mgr.mode || mgr.selectedIdx === null) return;
        
        const rect = container.getBoundingClientRect();
        const x = e.clientX - rect.left;
        const y = e.clientY - rect.top;
        const dr = mgr.drawings[mgr.selectedIdx];

        window.cyberLog(`Tentative de modification sur l'index: ${mgr.selectedIdx}`);

        // Détection resize
        dr.data.points.forEach((p, i) => {
            const px = window.chart.timeScale().timeToCoordinate(p.time);
            const py = window.candleSeries.priceToCoordinate(p.price);
            if (window.DrawingUtils.isOverPoint(x, y, px, py)) {
                window.cyberLog(`Resize activé sur point index: ${i}`);
                mgr.dragState = { type: 'resize', index: i };
            }
        });

        // Sinon move
        if (!mgr.dragState) {
            window.cyberLog("Déplacement de l'objet entier activé.");
            mgr.dragState = { type: 'move', lastX: x, lastY: y };
        }
        
        window.chart.applyOptions({ handleScroll: false, handleScale: false });
    });

    window.addEventListener('mousemove', e => {
        const rect = container.getBoundingClientRect();
        const x = e.clientX - rect.left, y = e.clientY - rect.top;

        if (mgr.mode && mgr.points.length > 0) {
            const p1 = { 
                x: window.chart.timeScale().timeToCoordinate(mgr.points[0].time), 
                y: window.candleSeries.priceToCoordinate(mgr.points[0].price) 
            };
            window.DrawingUtils.updatePreview(mgr.mode, p1, { x, y });
        }

        if (!mgr.dragState || mgr.selectedIdx === null) return;
        
        const dr = mgr.drawings[mgr.selectedIdx];
        const timeScale = window.chart.timeScale();

        if (mgr.dragState.type === 'resize') {
            dr.data.points[mgr.dragState.index] = { 
                time: timeScale.coordinateToTime(x), 
                price: window.candleSeries.coordinateToPrice(y) 
            };
        } else if (mgr.dragState.type === 'move') {
            const dx = x - mgr.dragState.lastX;
            const dy = y - mgr.dragState.lastY;
            
            dr.data.points = dr.data.points.map(p => {
                const nx = timeScale.timeToCoordinate(p.time) + dx;
                const ny = window.candleSeries.priceToCoordinate(p.price) + dy;
                return { 
                    time: timeScale.coordinateToTime(nx), 
                    price: window.candleSeries.coordinateToPrice(ny) 
                };
            });
            mgr.dragState.lastX = x; 
            mgr.dragState.lastY = y;
        }
        mgr.series.applyOptions({});
    });

    window.addEventListener('mouseup', () => {
        if (mgr.dragState) {
            window.cyberLog("Modification terminée. Sauvegarde.");
            mgr.dragState = null; 
            mgr.save();
            window.chart.applyOptions({ handleScroll: true, handleScale: true });
        }
    });

   container.addEventListener('dblclick', () => { 
		if (mgr.selectedIdx !== null) {
			const dr = mgr.drawings[mgr.selectedIdx];
			if (dr.data.type === 'text') {
				mgr.editText(mgr.selectedIdx);
			}
		} else if (mgr.mode === 'path' || mgr.mode === 'polyline') {
			mgr.finishDrawing(); 
		}
	});

    window.addEventListener('keydown', e => { 
        if (e.key === 'Delete' && mgr.selectedIdx !== null) {
            window.cyberLog(`Suppression du dessin index: ${mgr.selectedIdx}`);
            mgr.deleteSelected();
        }
        if (e.key === 'Escape') {
            window.cyberLog("Touche Echap: Réinitialisation complète.");
            mgr.setMode(null);
            mgr.selectedIdx = null;
            mgr.series.applyOptions({});
        }
    });
};