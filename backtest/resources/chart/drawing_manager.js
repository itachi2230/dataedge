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
            
            // On calcule un décalage horizontal (largeur) de 20 bougies environ
            const currentX = timeScale.timeToCoordinate(p.time);
            const futureTime = timeScale.coordinateToTime(currentX + 150); 

            // On calcule un décalage vertical par défaut (ex: 1% du prix actuel)
            const priceOffset = p.price * 0.01; 

            if (type === 'long_pos') {
                finalPoints = [
                    { time: p.time, price: p.price },            // 0: Entrée (Milieu Gauche)
                    { time: futureTime, price: p.price + priceOffset }, // 1: TP (Haut Droite)
                    { time: futureTime, price: p.price - priceOffset }  // 2: SL (Bas Droite)
                ];
            }
			
		else if (type === 'fibo') {
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
		}else {
                finalPoints = [	
                    { time: p.time, price: p.price },            // 0: Entrée
                    { time: futureTime, price: p.price - priceOffset }, // 1: TP (Bas Droite)
                    { time: futureTime, price: p.price + priceOffset }  // 2: SL (Haut Droite)
                ];
            }
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

window.syncDrawingWithChart = function() {
    const mgr = window.DrawingManager;
    const container = document.getElementById('chart-container');

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
			}
		else if (dr.data.type === 'long_pos' || dr.data.type === 'short_pos') {
				const entry = pts[0];
				const target = pts[1];
				const stop = pts[2];

				// Calcul des limites du rectangle total
				const xMin = entry.x;
				const xMax = target.x; // Rappel: TP et SL ont le même X (time)
    
				// On trouve le point le plus haut et le plus bas pour couvrir tout le setup
				const yMin = Math.min(entry.y, target.y, stop.y);
				const yMax = Math.max(entry.y, target.y, stop.y);

				// Vérification : est-ce que la souris est à l'intérieur de cette zone ?
				if (mouseX >= xMin && mouseX <= xMax && mouseY >= yMin && mouseY <= yMax) {
					isOverLine = true; 
				}
			}
		else if (type === 'fibo') {
			// On permet la sélection si on clique entre le niveau 0 et le niveau 1
			const yMin = Math.min(pts[0].y, pts[1].y);
			const yMax = Math.max(pts[0].y, pts[1].y);
    
			// Si on clique dans la zone verticale couverte par la fibo
			if (mouseY >= yMin && mouseY <= yMax) {
				isOverLine = true;
			}
		}
		else if (dr.data.type === 'path') {
		for (let j = 0; j < pts.length - 1; j++) {
			const d = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[j].x, pts[j].y, pts[j+1].x, pts[j+1].y);
			if (d < 10) { isOverLine = true; break; }
		}
	}
	else if (type === 'curve') {
		// On teste la proximité avec la courbe (simplifié par distance aux segments p1-p2 et p2-p3)
		const d1 = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[0].x, pts[0].y, pts[1].x, pts[1].y);
		const d2 = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[1].x, pts[1].y, pts[2].x, pts[2].y);
		if (d1 < 15 || d2 < 15) {
			isOverLine = true;
		}
	}
		
		else {
			// Détection standard pour les lignes simples (trendline, etc.)
			isOverLine = window.DrawingUtils.getDistanceToSegment(mouseX, mouseY, pts[0].x, pts[0].y, pts[1].x, pts[1].y) < 10;
		}
	}

    if (isOverAnyPoint || isOverText || isOverLine) {
        found = i;
    }
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


        // Détection resize
        dr.data.points.forEach((p, i) => {
            const px = window.chart.timeScale().timeToCoordinate(p.time);
            const py = window.candleSeries.priceToCoordinate(p.price);
            if (window.DrawingUtils.isOverPoint(x, y, px, py)) {

                mgr.dragState = { 
                type: 'resize', 
                index: i, 
                currentText: p.text // <--- SAUVEGARDE DU TEXTE ICI
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
                x: window.chart.timeScale().timeToCoordinate(mgr.points[0].time), 
                y: window.candleSeries.priceToCoordinate(mgr.points[0].price) 
            };
            window.DrawingUtils.updatePreview(mgr.mode, p1, { x, y });
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
    });
};