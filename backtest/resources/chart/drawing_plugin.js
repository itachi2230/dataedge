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
                        ctx.save();
                        
                        this.manager.drawings.forEach((d, index) => {
                            const isSelected = (index === this.manager.selectedIdx);
                            
                            // On utilise les instances stockées dans le constructeur
                            const timeScale = this.chart.timeScale();
                            const x1 = timeScale.timeToCoordinate(d.data.start.time);
                            const x2 = timeScale.timeToCoordinate(d.data.end.time);
                            const y1 = this.series.priceToCoordinate(d.data.start.price);
                            const y2 = this.series.priceToCoordinate(d.data.end.price);

                            if (x1 === null || y1 === null || x2 === null || y2 === null) return;

                            ctx.strokeStyle = isSelected ? '#FFD700' : '#00FFFF';
                            ctx.lineWidth = isSelected ? 3 : 2;

                            ctx.beginPath();
                            if (d.data.type === 'rectangle') {
                                ctx.fillStyle = isSelected ? 'rgba(255, 215, 0, 0.2)' : 'rgba(0, 255, 255, 0.1)';
                                ctx.rect(
                                    Math.min(x1, x2), 
                                    Math.min(y1, y2), 
                                    Math.abs(x2 - x1), 
                                    Math.abs(y2 - y1)
                                );
                                ctx.fill();
                            } else {
                                ctx.moveTo(x1, y1);
                                ctx.lineTo(x2, y2);
                            }
                            ctx.stroke();

                            if (isSelected) {
                                this._drawAnchor(ctx, x1, y1);
                                this._drawAnchor(ctx, x2, y2);
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