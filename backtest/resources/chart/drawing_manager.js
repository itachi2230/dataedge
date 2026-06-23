window.DrawingManager = {
    mode: null,
    drawings: [],
    points: [], 
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
		this.series.applyOptions({});
        // Vérification du nombre de clics requis par la config
        if (this.points.length === conf.clicks) {
            this.finishDrawing();
        }
    },

   finishDrawing() {
    if (this.points.length > 0) {
        const type = this.mode;
        let finalPoints = [...this.points];

        // LOGIQUE SPÉCIFIQUE POUR POSITION LONG/SHORT
        if (type === 'long_pos' || type === 'short_pos') {
            const p = this.points[0];
            const timeScale = this.chart.timeScale();

            // ── LARGEUR : 20% des bougies visibles ──────────────────────────
            const logicalRange = timeScale.getVisibleLogicalRange();
            const visibleBars = logicalRange ? (logicalRange.to - logicalRange.from) : 100;
            const currentX = window.resolveChartX(p.time) ?? timeScale.logicalToCoordinate(logicalRange ? logicalRange.from : 0);
            const containerWidth = document.getElementById('chart-container').clientWidth;
            const pxPerBar = containerWidth / visibleBars;
            const widthPx = Math.round(visibleBars * 0.20) * pxPerBar; // 20% de la vue
            const futureTime = timeScale.coordinateToTime(currentX + widthPx);

            // ── HAUTEUR : 12% de la plage de prix visible ───────────────────
            const containerHeight = document.getElementById('chart-container').clientHeight;
            const priceTop    = this.series.coordinateToPrice(0);
            const priceBottom = this.series.coordinateToPrice(containerHeight);
            const visiblePriceRange = Math.abs(priceTop - priceBottom);
            const priceOffset = visiblePriceRange * 0.06; // TP et SL à 6% chacun (12% total)

            if (type === 'long_pos') {
                finalPoints = [
                    { time: p.time, price: p.price },
                    { time: futureTime, price: p.price + priceOffset }, // TP
                    { time: futureTime, price: p.price - priceOffset }  // SL
                ];
            } else {
                // short_pos
                finalPoints = [
                    { time: p.time, price: p.price },
                    { time: futureTime, price: p.price - priceOffset }, // TP (bas pour short)
                    { time: futureTime, price: p.price + priceOffset }  // SL (haut pour short)
                ];
            }
        } else if (type === 'fibo') {
			// 1. On prépare les données par défaut
			const fiboData = {
				type: 'fibo',
				points: finalPoints,
				settings: {
					levels: [0, 0.382, 0.5, 0.618, 1],
					showFill: false
				}
			};
    
			// 2. On ajoute aux dessins
			this.drawings.push({ data: fiboData });
			this.save();
    
			// 3. On ouvre l'éditeur visuel (non-bloquant)
			const lastIdx = this.drawings.length - 1;
			setTimeout(() => this.editFibo(lastIdx), 100);
			
			// IMPORTANT: on sort ici pour éviter le code commun ci-dessous
			// qui ajouterait un second dessin en double
			this.setMode(null);
			this.series.applyOptions({});
			return;
        }

        // On ajoute le dessin avec les points finaux (soit 1, soit 3 pour les positions)
		const newDrawing = { data: { type: type, points: finalPoints }, id: Date.now() };
        this.drawings.push(newDrawing);
		if (type === 'long_pos' || type === 'short_pos') {
            this.lastActiveSetup = newDrawing;
        }	
        
        this.save();
        const lastIdx = this.drawings.length - 1;
        
        if (type === 'text') {
            setTimeout(() => this.editText(lastIdx), 100);
        }
    }
    this.setMode(null);
    this.series.applyOptions({}); // Rafraîchit le graphique pour afficher l'objet
},
editFibo(index) {
    const dr = this.drawings[index];
    if (!dr || dr.data.type !== 'fibo') return;

    const old = document.getElementById('temp-fibo-editor');
    if (old) old.remove();

    const container = document.getElementById('chart-container');
    const panel = document.createElement('div');
    panel.id = 'temp-fibo-editor';
    
    panel.style.cssText = `
        position: absolute; top: 10px; left: 50%; transform: translateX(-50%);
        z-index: 2000; background: #1e222d; color: #00FFFF;
        border: 2px solid #00FFFF; padding: 10px 15px; border-radius: 6px;
        display: flex; flex-direction: column; gap: 8px; min-width: 280px;
        box-shadow: 0 4px 15px rgba(0,0,0,0.5); font-family: sans-serif;
    `;

    const allLevels = [0, 0.236, 0.382, 0.5, 0.618, 0.786, 1];
    const currentLevels = dr.data.settings?.levels || [0, 0.382, 0.5, 0.618, 1];
    const currentFill = dr.data.settings?.showFill || false;

    // Construction de la grille de checkboxes
    let levelsHtml = `<div style="display: grid; grid-template-columns: repeat(4, 1fr); gap: 8px;">`;
    allLevels.forEach(lvl => {
        const isChecked = currentLevels.includes(lvl) ? 'checked' : '';
        levelsHtml += `
            <label style="font-size: 11px; display: flex; align-items: center; gap: 4px; cursor: pointer;">
                <input type="checkbox" class="fibo-lvl-cb" value="${lvl}" ${isChecked}> ${lvl}
            </label>`;
    });
    levelsHtml += `</div>`;

    panel.innerHTML = `
        <div style="font-size: 12px; font-weight: bold; border-bottom: 1px solid #333; padding-bottom: 5px;">Niveaux Fibonacci</div>
        ${levelsHtml}
        <div style="display: flex; justify-content: space-between; align-items: center; border-top: 1px solid #333; pt: 5px; margin-top: 5px;">
            <label style="font-size: 12px; display: flex; align-items: center; gap: 5px; cursor: pointer;">
                <input type="checkbox" id="fibo-fill-checkbox" ${currentFill ? 'checked' : ''}> Remplissage
            </label>
            <button id="fibo-ok-btn" style="background: #00FFFF; color: #1e222d; border: none; padding: 3px 10px; border-radius: 3px; cursor: pointer; font-weight: bold; font-size: 11px;">OK</button>
        </div>
    `;

    container.appendChild(panel);

    const saveSettings = () => {
        const selectedLevels = Array.from(panel.querySelectorAll('.fibo-lvl-cb:checked'))
            .map(cb => parseFloat(cb.value));

        // On injecte les settings dans l'objet source du dessin
        dr.data.settings = {
            levels: selectedLevels,
            showFill: panel.querySelector('#fibo-fill-checkbox').checked
        };
        
        this.save(); 
        this.series.applyOptions({}); // Déclenche le rafraîchissement du plugin
    };

    // Events pour mise à jour immédiate
    panel.querySelectorAll('input').forEach(input => {
        input.onchange = saveSettings;
    });

    panel.querySelector('#fibo-ok-btn').onclick = () => panel.remove();

    const closeOnOutside = (e) => {
        if (!panel.contains(e.target)) {
            panel.remove();
            document.removeEventListener('mousedown', closeOnOutside);
        }
    };
    setTimeout(() => document.addEventListener('mousedown', closeOnOutside), 100);
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
        const drawingToDelete = this.drawings[this.selectedIdx];

        // On vérifie si un setup actif est en mémoire et si c'est celui qu'on supprime
        if (this.lastActiveSetup && this.lastActiveSetup.id === drawingToDelete.id) {
            this.lastActiveSetup = null;
        }

        // Logique existante de suppression
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

// ── Fonction globale de résolution timestamp → coordonnée X ────────
// Définie GLOBALEMENT pour être accessible depuis DrawingManager.finishDrawing()
// et depuis syncDrawingWithChart(). Utilise drawing_plugin._resolveX en fallback.
window.resolveChartX = function(time) {
    if (!time) return null;
    const timeScale = window.chart.timeScale();
    let x = timeScale.timeToCoordinate(time);
    if (x !== null && !isNaN(x)) return x;

    const logRange = timeScale.getVisibleLogicalRange();
    if (logRange) {
        for (let k = 0; k < 10; k++) {
            const t  = timeScale.coordinateToTime(timeScale.logicalToCoordinate(Math.ceil(logRange.from) + k));
            const t2 = timeScale.coordinateToTime(timeScale.logicalToCoordinate(Math.ceil(logRange.from) + k + 1));
            if (t && t2 && t !== t2) {
                const step = Math.abs(t2 - t);
                for (const off of [0, 1, -1, 2, -2, 3, -3]) {
                    x = timeScale.timeToCoordinate(Math.round(time / step) * step + off * step);
                    if (x !== null && !isNaN(x)) return x;
                }
                break;
            }
        }
    }
    return null;
};

window.syncDrawingWithChart = function() {
    const mgr = window.DrawingManager;
    const container = document.getElementById('chart-container');

    // Alias local pour éviter de casser les closures existantes
    const resolveX = window.resolveChartX;

    window.chart.subscribeClick(param => {
        // 1. Debugging de base
        if (!param || !param.point) {
            mgr.selectedIdx = null;
            mgr.series.applyOptions({});
            return;
        }
			
        const price = window.candleSeries.coordinateToPrice(param.point.y);

        // 2. Si on est en train de déplacer/redimensionner, on ne fait rien
        if (mgr.dragState) {
            mgr.selectedIdx = null;
            mgr.series.applyOptions({});
            return;
        }

        if (mgr.mode) {
            // MODE DESSIN
            mgr.addPoint(param.time, price);
        } else {
            // MODE SÉLECTION

let found = null;
const mouseX = param.point.x;
const mouseY = param.point.y;

// ── Seuil adaptatif selon la taille pixel du dessin ────────────────
// Garantit MIN_HIT px cliquables même quand le dessin est minuscule en HTF
const MIN_HIT = 14;
const adaptiveThreshold = (pts) => {
    const valid = pts.filter(p => p.x !== null && p.y !== null);
    if (valid.length < 2) return MIN_HIT;
    const w = Math.max(...valid.map(p => p.x)) - Math.min(...valid.map(p => p.x));
    const h = Math.max(...valid.map(p => p.y)) - Math.min(...valid.map(p => p.y));
    const size = Math.max(w, h);
    return size < MIN_HIT * 2 ? MIN_HIT : 10;
};

mgr.drawings.forEach((dr, i) => {
    const type = dr.data.type;

    // ── SOURCE DE VÉRITÉ : coordonnées pixel du dernier rendu ──────
    // Le plugin les met à jour à chaque frame → toujours justes quelle
    // que soit la TF, même si resolveX échoue.
    // Fallback sur resolveX si le cache n'existe pas encore.
    let pts;
    if (dr._cachedCoords && dr._cachedCoords.length > 0) {
        pts = dr._cachedCoords; // coordonnées pixel exactes du rendu
    } else {
        pts = dr.data.points.map(p => ({
            x: resolveX(p.time),
            y: window.candleSeries.priceToCoordinate(p.price)
        }));
    }

    // Skip si vraiment aucun point résolvable
    if (!pts || pts.every(p => p.x === null)) return;

    const thr = adaptiveThreshold(pts);
    const anchorR = Math.max(MIN_HIT, thr + 4);

    // ── 1. Ancres ──────────────────────────────────────────────────
    const isOverAnyPoint = pts.some(pt =>
        pt.x !== null && window.DrawingUtils.isOverPoint(mouseX, mouseY, pt.x, pt.y, anchorR)
    );

    // ── 2. Détection par type ──────────────────────────────────────
    let isOverText = false;
    let isOverLine = false;

    if (type === 'text') {
        const tw = dr.lastMeasuredWidth || 100;
        if (pts[0].x !== null) {
            isOverText = mouseX >= pts[0].x - 5 && mouseX <= pts[0].x + tw + 5 &&
                         mouseY >= pts[0].y - 20  && mouseY <= pts[0].y + 5;
        }
    }
    else if (type === 'horz_line') {
        if (Math.abs(mouseY - pts[0].y) < thr) found = i;
    }
    else if (type === 'horz_ray') {
        if (pts[0].x !== null && Math.abs(mouseY - pts[0].y) < thr && mouseX >= pts[0].x - 5) found = i;
    }
    else if (type === 'vert_line') {
        if (pts[0].x !== null && Math.abs(mouseX - pts[0].x) < thr) found = i;
    }
    else if (pts.length >= 2) {
        const valid = pts.filter(p => p.x !== null);

        if (type === 'rectangle') {
            const xs = valid.map(p => p.x), ys = valid.map(p => p.y);
            const xMin = Math.min(...xs), xMax = Math.max(...xs);
            const yMin = Math.min(...ys), yMax = Math.max(...ys);
            const dEdge = Math.min(
                window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMin, yMin, xMax, yMin),
                window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMin, yMax, xMax, yMax),
                window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMin, yMin, xMin, yMax),
                window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, xMax, yMin, xMax, yMax)
            );
            if (dEdge < thr || (mouseX >= xMin && mouseX <= xMax && mouseY >= yMin && mouseY <= yMax))
                isOverLine = true;
        }
        else if (type === 'long_pos' || type === 'short_pos') {
            const allX = pts.map(p => p.x).filter(x => x !== null);
            const allY = pts.map(p => p.y).filter(y => y !== null);
            if (allX.length && allY.length) {
                const xMin = Math.min(...allX) - thr, xMax = Math.max(...allX) + thr;
                const yMin = Math.min(...allY) - thr, yMax = Math.max(...allY) + thr;
                if (mouseX >= xMin && mouseX <= xMax && mouseY >= yMin && mouseY <= yMax)
                    isOverLine = true;
            }
        }
        else if (type === 'fibo') {
            const ys = valid.map(p => p.y);
            if (mouseY >= Math.min(...ys) - thr && mouseY <= Math.max(...ys) + thr)
                isOverLine = true;
        }
        else if (type === 'path') {
            for (let j = 0; j < pts.length - 1; j++) {
                if (pts[j].x === null || pts[j+1].x === null) continue;
                if (window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[j].x, pts[j].y, pts[j+1].x, pts[j+1].y) < thr) {
                    isOverLine = true; break;
                }
            }
        }
        else if (type === 'curve' && pts.length >= 3) {
            const d1 = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[0].x, pts[0].y, pts[1].x, pts[1].y);
            const d2 = pts[2]?.x != null
                ? window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[1].x, pts[1].y, pts[2].x, pts[2].y)
                : Infinity;
            if (d1 < thr || d2 < thr) isOverLine = true;
        }
        else {
            // trendline, arrow, et tout le reste
            if (pts[0].x !== null && pts[1].x !== null)
                isOverLine = window.DrawingUtils.getDistanceToSegment(
                    mouseX, mouseY, pts[0].x, pts[0].y, pts[1].x, pts[1].y) < thr;
        }
    }

    if (isOverAnyPoint || isOverText || isOverLine) found = i;
});

            if (found!== null) {
            } else {
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
				if (dr.data.type === 'fibo' && mgr.selectedIdx === found) {
					mgr.editFibo(found);
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

        // Détection resize sur les ancres — utilise les coords pixel du dernier rendu
        const cachedPts = dr._cachedCoords;
        dr.data.points.forEach((p, i) => {
            // Coordonnée pixel depuis le cache du plugin, sinon fallback resolveX
            const px = cachedPts?.[i]?.x ?? resolveX(p.time);
            const py = cachedPts?.[i]?.y ?? window.candleSeries.priceToCoordinate(p.price);
            if (px !== null && window.DrawingUtils.isOverPoint(x, y, px, py, 14)) {
                mgr.dragState = { 
                    type: 'resize', 
                    index: i, 
                    currentText: p.text
                };
            }
        });

        // Sinon move
        if (!mgr.dragState) {
            mgr.dragState = { type: 'move', lastX: x, lastY: y };
        }
        
        window.chart.applyOptions({ handleScroll: false, handleScale: false });
    });

    window.addEventListener('mousemove', e => {
        const rect = container.getBoundingClientRect();
        const x = e.clientX - rect.left, y = e.clientY - rect.top;

        if (mgr.mode && mgr.points.length > 0) {
            const p1 = { 
                x: resolveX(mgr.points[0].time), 
                y: window.candleSeries.priceToCoordinate(mgr.points[0].price) 
            };
            if (p1.x !== null) window.DrawingUtils.updatePreview(mgr.mode, p1, { x, y });
        }

        if (!mgr.dragState || mgr.selectedIdx === null) return;
        
        const dr = mgr.drawings[mgr.selectedIdx];
        const timeScale = window.chart.timeScale();

       if (mgr.dragState.type === 'resize') {
    const newTime = timeScale.coordinateToTime(x);
    const newPrice = window.candleSeries.coordinateToPrice(y);

    if (dr.data.type === 'long_pos' || dr.data.type === 'short_pos') {
        const idx = mgr.dragState.index;

        if (idx === 1 || idx === 2) {
            // SYNCHRONISATION DE LA LARGEUR (Temps)
            // On applique le nouveau temps aux deux points (TP et SL)
            dr.data.points[1].time = newTime;
            dr.data.points[2].time = newTime;
            
            // MISE À JOUR DU PRIX SPECIFIQUE
            // Seul le point cliqué change de hauteur (prix)
            dr.data.points[idx].price = newPrice;
        } else if (idx === 0) {
            // Si on bouge le point d'entrée, on ne change que son prix/temps
            dr.data.points[0].time = newTime;
            dr.data.points[0].price = newPrice;
        }
    } else {
        // LOGIQUE STANDARD pour les autres dessins
        dr.data.points[mgr.dragState.index] = { 
            time: newTime, 
            price: newPrice,
            text: mgr.dragState.currentText
        };
    }
} else if (mgr.dragState.type === 'move') {
            const dx = x - mgr.dragState.lastX;
            const dy = y - mgr.dragState.lastY;
            
            dr.data.points = dr.data.points.map(p => {
                const rawX = resolveX(p.time);
                if (rawX === null) return p; // Point non résolvable → on le laisse intact
                const nx = rawX + dx;
                const ny = window.candleSeries.priceToCoordinate(p.price) + dy;
                const newTime = timeScale.coordinateToTime(nx);
                if (!newTime) return p; // Coordonnée hors plage → on protège
                return { 
                    time: newTime, 
                    price: window.candleSeries.coordinateToPrice(ny),
                    text: p.text
                };
            });
            mgr.dragState.lastX = x; 
            mgr.dragState.lastY = y;
        }
        mgr.series.applyOptions({});
    });

    window.addEventListener('mouseup', () => {
        if (mgr.dragState) {
            mgr.dragState = null; 
            mgr.save();
            window.chart.applyOptions({ handleScroll: true, handleScale: true });
        }
    });

   container.addEventListener('dblclick', (e) => { 
        // 1. Si un dessin est sélectionné (Logique existante)
        if (mgr.selectedIdx !== null) {
            const dr = mgr.drawings[mgr.selectedIdx];
        } 
        // 2. Si on est en train de tracer un chemin/polyline, on termine le dessin
        else if (mgr.mode === 'path' || mgr.mode === 'polyline') {
            mgr.finishDrawing(); 
        } 
        // 3 STYLE TRADINGVIEW : Uniquement si le mode REPLAY est actif
        else if (window.replayState && window.replayState.isActive) {
            if (window.chart && window.candleSeries) {
                const rect = container.getBoundingClientRect();
                const localX = e.clientX - rect.left;
                
                const timeScale = window.chart.timeScale();
                const clickedTime = timeScale.coordinateToTime(localX);

                if (clickedTime) {
                    // On cherche l'index de la bougie correspondante dans le cache global allData
                    const targetIdx = window.replayState.allData.findIndex(d => d.time >= clickedTime);

                    if (targetIdx !== -1) {
                        // Comme sur TradingView, si le replay défile en mode "Play", on le met en pause
                        if (window.replayState.isPlaying) {
                            window.togglePlayReplay();
                        }

                        // Conversion du timestamp au format string attendu par applyJump (YYYY-MM-DD)
                        const dDate = typeof clickedTime === 'string' 
                            ? clickedTime 
                            : new Date(clickedTime * 1000).toISOString().split('T')[0];
                        // applyJump se charge de couper le tableau, appliquer getExtendedTimeline et recalibrer l'autoScale
                        applyJump(targetIdx, dDate);
                    }
                }
            }
        }
    });

    window.addEventListener('keydown', e => { 
        if (e.key === 'Delete' && mgr.selectedIdx !== null) {
            mgr.deleteSelected();
        }
        if (e.key === 'Escape') {
            mgr.setMode(null);
            mgr.selectedIdx = null;
            mgr.series.applyOptions({});
        }
        // ESPACE : pause/play en mode replay (uniquement si aucun input n'est focusé)
        if (e.key === ' ' && window.replayState && window.replayState.isActive) {
            const tag = document.activeElement?.tagName;
            if (tag !== 'INPUT' && tag !== 'TEXTAREA' && tag !== 'SELECT') {
                e.preventDefault();
                window.togglePlayReplay();
            }
        }
    });
};