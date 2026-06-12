class DrawingPlugin {
    constructor(manager, chart, series) {
        this.manager = manager;
        this.chart = chart;
        this.series = series;
        // Cache du pas temporel pour le snap
        this._stepCache = null;
        this._stepCacheTime = 0;
    }

    // ─────────────────────────────────────────────────────────────────
    // Calcule le pas de la timeframe courante (avec cache de 2s)
    // ─────────────────────────────────────────────────────────────────
    _getCurrentStep(timeScale) {
        const now = Date.now();
        if (this._stepCache && now - this._stepCacheTime < 2000) {
            return this._stepCache;
        }

        try {
            const logicalRange = timeScale.getVisibleLogicalRange();
            if (!logicalRange) return null;

            // On itère pour trouver deux bougies réelles consécutives (pas un gap)
            const from = Math.ceil(logicalRange.from);
            for (let i = 0; i < 20; i++) {
                const t1 = timeScale.coordinateToTime(timeScale.logicalToCoordinate(from + i));
                const t2 = timeScale.coordinateToTime(timeScale.logicalToCoordinate(from + i + 1));
                if (t1 && t2 && t1 !== t2) {
                    this._stepCache = Math.abs(t2 - t1);
                    this._stepCacheTime = now;
                    return this._stepCache;
                }
            }
        } catch (e) {}

        return this._stepCache; // Retourne le dernier connu si échec
    }

    // ─────────────────────────────────────────────────────────────────
    // Résolution robuste d'un timestamp → coordonnée X
    // Stratégie : timeToCoordinate → snap sur step → snap binaire
    // ─────────────────────────────────────────────────────────────────
    _resolveX(time, timeScale, step) {
        if (!time) return null;

        // 1. Tentative directe (bougie exacte)
        let x = timeScale.timeToCoordinate(time);
        if (x !== null && !isNaN(x)) return x;

        // 2. Snap sur le multiple de step le plus proche
        if (step) {
            for (const offset of [0, 1, -1, 2, -2, 3, -3]) {
                const snapped = Math.round(time / step) * step + offset * step;
                x = timeScale.timeToCoordinate(snapped);
                if (x !== null && !isNaN(x)) return x;
            }
        }

        // 3. Recherche binaire sur les données de la série
        // On cherche la bougie la plus proche dans les données chargées
        try {
            const data = this.series.data();
            if (data && data.length > 0) {
                // Recherche dichotomique du timestamp le plus proche
                let lo = 0, hi = data.length - 1;
                while (lo < hi) {
                    const mid = (lo + hi) >> 1;
                    if (data[mid].time < time) lo = mid + 1;
                    else hi = mid;
                }

                // On teste les voisins (±3 bougies) pour trouver une coordonnée valide
                for (let i = -3; i <= 3; i++) {
                    const idx = Math.max(0, Math.min(data.length - 1, lo + i));
                    if (data[idx] && data[idx].time) {
                        x = timeScale.timeToCoordinate(data[idx].time);
                        if (x !== null && !isNaN(x)) return x;
                    }
                }
            }
        } catch (e) {}

        return null; // Vraiment introuvable
    }

    // ─────────────────────────────────────────────────────────────────
    // Détermine si un dessin doit être rendu selon ses coordonnées
    // On autorise le rendu si AU MOINS UN point est visible,
    // sauf pour les types qui nécessitent TOUS leurs points (ex: trendline)
    // ─────────────────────────────────────────────────────────────────
    _shouldRender(coords, type) {
        const nullCount = coords.filter(c => c.x === null).length;
        const total = coords.length;

        // Ces types peuvent être rendus partiellement hors écran
        const partialOk = ['horz_line', 'horz_ray', 'vert_line', 'fibo', 'rectangle', 'long_pos', 'short_pos', 'text'];
        if (partialOk.includes(type)) return nullCount < total;

        // Pour les autres : tous les points doivent être résolus
        return nullCount === 0;
    }

    // ─────────────────────────────────────────────────────────────────
    // Convertit un point {time, price} en coordonnées canvas
    // Retourne {x, y, text} avec x pouvant être null (hors écran mais OK)
    // ─────────────────────────────────────────────────────────────────
    _pointToCoord(p, timeScale, step, width) {
        let x = this._resolveX(p.time, timeScale, step);

        // Pour les types qui s'étendent sur toute la largeur (lignes infinies),
        // on peut laisser x à null — le render s'en occupe.
        // Pour les autres, on clamp hors écran plutôt que de planter.
        if (x === null) {
            x = null; // on laisse null, _shouldRender décide
        }

        const y = this.series.priceToCoordinate(p.price);
        return { x, y, text: p.text };
    }

    paneViews() {
        return [{
            renderer: () => ({
                draw: (target) => {
                    if (!this.manager.drawings.length) return;

                    target.useMediaCoordinateSpace((scope) => {
                        const canvasCtx = scope.context;
                        const { width, height } = scope.mediaSize;
                        const timeScale = this.chart.timeScale();

                        const step = this._getCurrentStep(timeScale);

                        canvasCtx.save();

                        this.manager.drawings.forEach((d, index) => {
                            const config = window.DrawingConfigs[d.data.type];
                            if (!config) return;

                            const isSelected = (index === this.manager.selectedIdx);

                            const coords = d.data.points.map(p =>
                                this._pointToCoord(p, timeScale, step, width)
                            );

                            // ── CACHE PIXEL COORDS pour la sélection ──────────────
                            // Le manager lit _cachedCoords au lieu de recalculer
                            d._cachedCoords = coords;

                            // Vérification : doit-on rendre ce dessin ?
                            if (!this._shouldRender(coords, d.data.type)) return;

                            // Pour les renders qui ont besoin de coordonnées X,
                            // on remplace les null par des valeurs de bord sécurisées
                            const safeCoords = coords.map(c => ({
                                ...c,
                                x: c.x !== null ? c.x : (c.isLeft ? -10 : width + 10)
                            }));

                            // Mesure du texte
                            if (d.data.type === 'text') {
                                canvasCtx.font = "14px Arial";
                                d.lastMeasuredWidth = canvasCtx.measureText(
                                    d.data.points[0].text || ""
                                ).width;
                            }

                            // --- RENDU ---
                            canvasCtx.strokeStyle = '#00FFFF';
                            canvasCtx.lineWidth = 2;
                            canvasCtx.lineJoin = "round";
                            canvasCtx.lineCap = "round";
                            canvasCtx.setLineDash([]); // Reset dash

                            // Preview de dessin en cours (une seule fois, pas dans la boucle)
                            // Déplacé hors de la boucle — voir plus bas

                            canvasCtx.beginPath();
                            config.render(canvasCtx, ...safeCoords, width, height, isSelected, d);

                            if (config.fill) {
                                canvasCtx.fillStyle = 'rgba(0, 255, 255, 0.1)';
                                canvasCtx.fill();
                            }
                            canvasCtx.stroke();

                            // --- ANCRES (points de contrôle) ---
                            if (isSelected) {
                                safeCoords.forEach(c => {
                                    // On n'affiche les ancres que si le point est vraiment résolu
                                    const realCoord = coords[safeCoords.indexOf(c)];
                                    if (realCoord.x === null) return;

                                    canvasCtx.fillStyle = "#1e222d";
                                    canvasCtx.strokeStyle = "#00FFFF";
                                    canvasCtx.lineWidth = 2;
                                    canvasCtx.setLineDash([]);
                                    canvasCtx.beginPath();
                                    canvasCtx.arc(c.x, c.y, 5, 0, Math.PI * 2);
                                    canvasCtx.fill();
                                    canvasCtx.stroke();
                                });
                            }
                        });

                        // Preview du dessin en cours (hors boucle drawings)
                        this._renderPreviews(canvasCtx, timeScale, step);

                        canvasCtx.restore();
                    });
                }
            })
        }];
    }

    _renderPreviews(ctx, timeScale, step) {
        if (!this.manager.mode || this.manager.points.length === 0) return;

        const pts = this.manager.points.map(p => {
            const x = this._resolveX(p.time, timeScale, step);
            const y = this.series.priceToCoordinate(p.price);
            return { x, y };
        });

        if (!pts[0] || pts[0].x === null) return;

        ctx.save();
        ctx.beginPath();
        ctx.setLineDash([5, 5]);
        ctx.strokeStyle = '#00FFFF';
        ctx.moveTo(pts[0].x, pts[0].y);

        pts.forEach((p, i) => {
            if (i > 0 && p.x !== null) ctx.lineTo(p.x, p.y);
        });

        ctx.stroke();
        ctx.restore();
    }
}