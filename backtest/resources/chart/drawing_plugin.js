class DrawingPlugin {
    constructor(manager, chart, series) {
        this.manager = manager;
        this.chart = chart;
        this.series = series;
    }

    _getCurrentStep(timeScale) {
        const visibleRange = timeScale.getVisibleRange();
        if (!visibleRange) return null;

     
        const logicalRange = timeScale.getVisibleLogicalRange();
        if (!logicalRange) return null;

        const time1 = timeScale.coordinateToTime(timeScale.logicalToCoordinate(logicalRange.from));
        const time2 = timeScale.coordinateToTime(timeScale.logicalToCoordinate(logicalRange.from + 1));
        
        if (time1 && time2) {
            return Math.abs(time2 - time1);
        }
        return null;
    }

    paneViews() {
        return [{
            renderer: () => ({
                draw: (target) => {
                    const ctx = target._context;
                    if (!ctx || !this.manager.drawings.length) return;

                    target.useMediaCoordinateSpace((scope) => {
                        const canvasCtx = scope.context;
                        const { width, height } = scope.mediaSize;
                        const timeScale = this.chart.timeScale();
                        
                        // Calcul du pas de la TF actuelle
                        const step = this._getCurrentStep(timeScale);
                        
                        canvasCtx.save();

                        this.manager.drawings.forEach((d, index) => {
                            const config = window.DrawingConfigs[d.data.type];
                            if (!config) return;

                            const isSelected = (index === this.manager.selectedIdx);

                            const coords = d.data.points.map(p => {
                                // 1. TENTATIVE NORMALE
                                let x = timeScale.timeToCoordinate(p.time);

                                // 2. SNAP MATHÉMATIQUE SI ÉCHEC (Changement de TF)
                                if (x === null && p.time && step) {
                                    // On force le timestamp sur un multiple exact du début d'une bougie
                                    const snappedTime = Math.floor(p.time / step) * step;
                                    x = timeScale.timeToCoordinate(snappedTime);

                                    // 3. ULTIME RECOURS : INDEX LOGIQUE
                                    if (x === null) {
                                        const logical = timeScale.coordinateToLogical(p.time);
                                        if (logical !== null) {
                                            x = timeScale.logicalToCoordinate(Math.round(logical));
                                        }
                                    }
                                }

                                // 4. SÉCURITÉ ANTI-TRAIT HORIZONTAL (Bord gauche)
                                // Si x est toujours null ou NaN, on le sort de l'écran visible
                                if (x === null || isNaN(x)) {
                                    x = -20000; 
                                }

                                const y = this.series.priceToCoordinate(p.price);
                                return { x, y, text: p.text };
                            });

                            // PROTECTION : Si un des points cruciaux est invalide, on n'appelle pas le render
                            // Cela évite que le moteur de rendu tente un lineTo(0, y)
                            if (coords.some(c => c.x < -10000)) return;

                            // Mesure du texte
                            if (d.data.type === 'text') {
                                canvasCtx.font = "14px Arial";
                                d.lastMeasuredWidth = canvasCtx.measureText(d.data.points[0].text || "").width;
                            }

                            // --- RENDU ---
                            canvasCtx.strokeStyle = '#00FFFF';
                            canvasCtx.lineWidth = 2;
                            canvasCtx.lineJoin = "round";
                            canvasCtx.lineCap = "round";

                            this._renderPreviews(canvasCtx, timeScale);

                            canvasCtx.beginPath();
                            // Appel au fichier de configuration (drawing_configs.js)
                            config.render(canvasCtx, ...coords, width, height, isSelected, d);
                            
                            if (config.fill) {
                                canvasCtx.fillStyle = 'rgba(0, 255, 255, 0.1)';
                                canvasCtx.fill();
                            }
                            canvasCtx.stroke();

                            // --- ANCRES ---
                            if (isSelected) {
                                coords.forEach(c => {
                                    if (c.x < 0) return;
                                    canvasCtx.fillStyle = "#1e222d";
                                    canvasCtx.strokeStyle = "#00FFFF";
                                    canvasCtx.lineWidth = 2;
                                    canvasCtx.beginPath();
                                    canvasCtx.arc(c.x, c.y, 5, 0, Math.PI * 2);
                                    canvasCtx.fill();
                                    canvasCtx.stroke();
                                });
                            }
                        });
                        canvasCtx.restore();
                    });
                }
            })
        }];
    }

    _renderPreviews(ctx, timeScale) {
        if (!this.manager.mode || this.manager.points.length === 0) return;

        const pts = this.manager.points.map(p => {
            let x = timeScale.timeToCoordinate(p.time);
            if (x === null) {
                const log = timeScale.coordinateToLogical(p.time);
                if (log !== null) x = timeScale.logicalToCoordinate(Math.round(log));
            }
            return { x, y: this.series.priceToCoordinate(p.price) };
        });

        if (pts[0].x === null || pts[0].x < -5000) return;

        ctx.save();
        ctx.beginPath();
        ctx.setLineDash([5, 5]);
        ctx.strokeStyle = '#00FFFF';
        ctx.moveTo(pts[0].x, pts[0].y);
        
        pts.forEach((p, i) => {
            if (i > 0 && p.x !== null && p.x > -10000) ctx.lineTo(p.x, p.y);
        });
        
        ctx.stroke();
        ctx.restore();
    }
}