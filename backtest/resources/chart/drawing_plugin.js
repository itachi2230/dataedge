class DrawingPlugin {
    constructor(manager, chart, series) {
        this.manager = manager;
        this.chart = chart;
        this.series = series;
    }

    paneViews() {
        return [{
            renderer: () => ({
                draw: (target) => { 
                    const ctx = target._context;
                    if (!ctx || !this.manager.drawings.length) return;

                    target.useMediaCoordinateSpace((scope) => {
                        const ctx = scope.context;
                        const width = scope.mediaSize.width; // Récupère la largeur réelle du canvas
                        ctx.save();
                        
                        this.manager.drawings.forEach((d, index) => {
                            const isSelected = (index === this.manager.selectedIdx);
                            
                            const timeScale = this.chart.timeScale();
                            const x1 = timeScale.timeToCoordinate(d.data.start.time);
                            const x2 = timeScale.timeToCoordinate(d.data.end.time);
                            const y1 = this.series.priceToCoordinate(d.data.start.price);
                            const y2 = this.series.priceToCoordinate(d.data.end.price);

                            // Pour le rayon horizontal, seul x1 et y1 sont critiques
                            if (x1 === null || y1 === null) return;

                            ctx.strokeStyle = isSelected ? '#FFD700' : '#00FFFF';
                            ctx.lineWidth = isSelected ? 3 : 2;

                            ctx.beginPath();
                            if (d.data.type === 'rectangle') {
                                if (x2 === null || y2 === null) return;
                                ctx.fillStyle = isSelected ? 'rgba(255, 215, 0, 0.2)' : 'rgba(0, 255, 255, 0.1)';
                                ctx.rect(
                                    Math.min(x1, x2), 
                                    Math.min(y1, y2), 
                                    Math.abs(x2 - x1), 
                                    Math.abs(y2 - y1)
                                );
                                ctx.fill();
                            } 
                            else if (d.data.type === 'horz_ray') {
                                // Dessine du point de départ jusqu'au bord droit (width)
                                ctx.moveTo(x1, y1);
                                ctx.lineTo(width, y1);
                            } 
                            else {
                                // Logique par défaut (Trendline)
                                if (x2 === null || y2 === null) return;
                                ctx.moveTo(x1, y1);
                                ctx.lineTo(x2, y2);
                            }
                            ctx.stroke();

                            if (isSelected) {
                                this._drawAnchor(ctx, x1, y1);
                                // On ne dessine la deuxième ancre que si elle est pertinente (pas pour le rayon infini)
                                if (d.data.type !== 'horz_ray' && x2 !== null && y2 !== null) {
                                    this._drawAnchor(ctx, x2, y2);
                                }
                            }
                        });
                        ctx.restore();
                    });
                }
            })
        }];
    }

    _drawAnchor(ctx, x, y) {
        ctx.fillStyle = "white";
        ctx.strokeStyle = "#FFD700";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.arc(x, y, 4, 0, Math.PI * 2);
        ctx.fill();
        ctx.stroke();
    }

    updateAllViews() {}
    priceAxisViews() { return []; }
    timeAxisViews() { return []; }
}