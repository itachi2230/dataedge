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
        // On crée l'objet avec les points
        this.drawings.push({ 
            data: { type: type, points: [...this.points] } 
        });
        
        this.save();
        const lastIdx = this.drawings.length - 1;
        
        if (type === 'text') {
            // On force l'édition immédiate après la création
            setTimeout(() => this.editText(lastIdx), 100);
        }
    }
    this.setMode(null);
},
	editText(index) {
    const dr = this.drawings[index];
    if (!dr || dr.data.type !== 'text') return;

    // Suppression de l'ancien s'il existe
    const old = document.getElementById('temp-text-editor');
    if (old) old.remove();

    const container = document.getElementById('chart-container');
    const input = document.createElement('input');
    input.id = 'temp-text-editor';
    input.type = 'text';
    // On charge le texte actuel ou rien s'il n'y a pas encore de texte
    input.value = dr.data.points[0].text || "";
    
    input.style.cssText = `
        position: absolute; top: 10px; left: 50%; transform: translateX(-50%);
        z-index: 2000; background: #1e222d; color: #00FFFF;
        border: 2px solid #00FFFF; padding: 8px 15px; border-radius: 4px;
        outline: none; min-width: 200px;
    `;

    container.appendChild(input);
    input.focus();
    if(input.value) input.select();

    const saveAndClose = () => {
		const newValue = input.value.trim();
		if (newValue !== "") {
			// On enregistre dans le point SOURCE (celui qui est sauvegardé en JSON)
			this.drawings[index].data.points[0].text = newValue;
			window.cyberLog(`Texte enregistré dans la source : ${newValue}`);
		}
    
		if (input.parentNode) input.remove();
		this.save(); // Sauvegarde dans le localStorage
		this.series.applyOptions({}); // Force le plugin à relire les sources
	};

    input.onblur = saveAndClose;
    input.onkeydown = (e) => {
        if (e.key === 'Enter') saveAndClose();
        if (e.key === 'Escape') { input.remove(); }
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
            // Dans window.chart.subscribeClick(param => { ... })
// Remplace la logique de détection 'found' par celle-ci :

let found = null;
mgr.drawings.forEach((dr, i) => {
    const pts = dr.data.points.map(p => ({
        x: window.chart.timeScale().timeToCoordinate(p.time),
        y: window.candleSeries.priceToCoordinate(p.price)
    }));

    const mouseX = param.point.x;
    const mouseY = param.point.y;

    // 1. Détection sur les points d'ancrage (pour tous les objets)
    const isOverAnyPoint = pts.some(pt => window.DrawingUtils.isOverPoint(mouseX, mouseY, pt.x, pt.y, 15)); // Rayon plus large (15px)

    // 2. Détection spécifique au texte (boîte de collision autour du texte)
    let isOverText = false;
	const type = dr.data.type;
    if (type === 'text') {
        const textWidth = dr.lastMeasuredWidth || 100; // Estimation de la largeur du texte
        const textHeight = 20;
       isOverText = (mouseX >= pts[0].x - 5 && mouseX <= pts[0].x + textWidth + 5 &&
                  mouseY >= pts[0].y - textHeight && mouseY <= pts[0].y + 5);
    }
	
	else if (type === 'horz_line' || type === 'horz_ray') {
		// Si la souris est à la même hauteur (Y) que la ligne (marge de 10px)
		const isAtCorrectHeight = Math.abs(mouseY - pts[0].y) < 10;
    
		if (type === 'horz_line' && isAtCorrectHeight) {
			found = i;
		} else if (type === 'horz_ray' && isAtCorrectHeight && mouseX >= pts[0].x - 5) {
			// Pour le rayon, il faut aussi être à droite du point d'origine
			found = i;
		}
	} 

	else if (type === 'vert_line') {
		// Si la souris est à la même position horizontale (X) que la ligne
		if (Math.abs(mouseX - pts[0].x) < 10) {
			found = i;
		}
	}
	else{}

    // 3. Détection sur les segments (Trendlines, Rectangles, etc.)
    let isOverLine = false;
	   if (pts.length >= 2) {
		if (dr.data.type === 'rectangle') {
			// Définition des 4 coins à partir des 2 points diagonaux
			const xMin = Math.min(pts[0].x, pts[1].x);
			const xMax = Math.max(pts[0].x, pts[1].x);
			const yMin = Math.min(pts[0].y, pts[1].y);
			const yMax = Math.max(pts[0].y, pts[1].y);

			// On vérifie la proximité avec l'un des 4 bords
			const dTop = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMin, yMin, xMax, yMin);
			const dBottom = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMin, yMax, xMax, yMax);
			const dLeft = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMin, yMin, xMin, yMax);
			const dRight = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMax, yMin, xMax, yMax);

			if (Math.min(dTop, dBottom, dLeft, dRight) < 10) {
				isOverLine = true;
			}
        
			// Optionnel : Sélection par l'intérieur du rectangle
			if (mouseX >= xMin && mouseX <= xMax && mouseY >= yMin && mouseY <= yMax) {
				isOverLine = true;
			}
		} else {
			// Détection standard pour les lignes simples (trendline, etc.)
			isOverLine = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[0].x, pts[0].y, pts[1].x, pts[1].y) < 10;
		}
	}

    if (isOverAnyPoint || isOverText || isOverLine) {
        found = i;
    }
});

            if (found!== null) {
                window.cyberLog(`Dessin trouvé à l'index: ${found}`);
            } else {
                window.cyberLog("Aucun dessin touché. Désélection.");
            }

            // Mise à jour de la sélection
            mgr.selectedIdx = found;
            mgr.series.applyOptions({});
			if (found !== null) {
				const dr = mgr.drawings[found];
				if (dr.data.type === 'text' && !mgr.dragState) { // <--- Ajout !mgr.dragState
					setTimeout(() => {
						// On vérifie qu'on n'est pas en train de draguer avant d'ouvrir
						if (!mgr.dragState) mgr.editText(found);
					}, 150); 
				}
			}
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

                mgr.dragState = { 
                type: 'resize', 
                index: i, 
                currentText: p.text // <--- SAUVEGARDE DU TEXTE ICI
            };
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
                price: window.candleSeries.coordinateToPrice(y),
				text: mgr.dragState.currentText
            };
        } else if (mgr.dragState.type === 'move') {
            const dx = x - mgr.dragState.lastX;
            const dy = y - mgr.dragState.lastY;
            
            dr.data.points = dr.data.points.map(p => {
                const nx = timeScale.timeToCoordinate(p.time) + dx;
                const ny = window.candleSeries.priceToCoordinate(p.price) + dy;
                return { 
                time: timeScale.coordinateToTime(nx), 
                price: window.candleSeries.coordinateToPrice(ny),
                text: p.text // <--- PROTECTION ICI
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
			window.cyberLog(`dbclick`);
			
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