class DrawingPlugin {
    constructor(manager, chart, series) {
        this.manager = manager; this.chart = chart; this.series = series;
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
                        canvasCtx.save();

                        this.manager.drawings.forEach((d, index) => {
                            const config = window.DrawingConfigs[d.data.type];
                            if (!config) return;

                            const isSelected = (index === this.manager.selectedIdx);
                            const coords = d.data.points.map(p => ({
                                x: this.chart.timeScale().timeToCoordinate(p.time),
                                y: this.series.priceToCoordinate(p.price)
                            }));

                            if (coords[0].x === null) return;

                            canvasCtx.strokeStyle =  '#00FFFF';
                            canvasCtx.lineWidth =  2;
                            canvasCtx.lineJoin = "round"; canvasCtx.lineCap = "round";

                            canvasCtx.beginPath();
                            // On passe tous les points au render (utile pour le chemin)
                            config.render(canvasCtx, ...coords, width, height,isSelected, d);
                            
                            if (config.fill) {
                                canvasCtx.fillStyle = 'rgba(0, 255, 255, 0.1)';
                                canvasCtx.fill();
                            }
                            canvasCtx.stroke();

                            if (isSelected) {
                                coords.forEach(c => {
                                    canvasCtx.fillStyle = "#00FFFF";
									canvasCtx.beginPath();
                                    canvasCtx.arc(c.x, c.y, 5, 0, Math.PI * 2); canvasCtx.fill();
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
}