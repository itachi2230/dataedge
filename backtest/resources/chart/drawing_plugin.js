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
                                y: this.series.priceToCoordinate(p.price),
								text: p.text
                            }));
							if (d.data.type === 'text') {
								const text = d.data.points[0].text || "Cliquez pour modifier";
								canvasCtx.font = "14px Arial";
								// On mesure et on stocke la largeur dans l'objet pour le manager
								d.lastMeasuredWidth = canvasCtx.measureText(text).width;
							}
							if (this.manager.mode === 'path' && this.manager.points.length > 0) {
								const tempPts = this.manager.points.map(p => ({
									x: this.chart.timeScale().timeToCoordinate(p.time),
									y: this.series.priceToCoordinate(p.price)
								}));

								canvasCtx.beginPath();
								canvasCtx.lineCap = 'round';
								canvasCtx.strokeStyle = '#00FFFF'; // Couleur de ton choix
								ctx.setLineDash([5, 5]); // Optionnel : mettre en pointillé le tracé en cours
    
								canvasCtx.moveTo(tempPts[0].x, tempPts[0].y);
								for (let i = 1; i < tempPts.length; i++) {
									canvasCtx.lineTo(tempPts[i].x, tempPts[i].y);
								}
								canvasCtx.stroke();
								ctx.setLineDash([]); // On remet en ligne pleine pour la suite
							}
							// Dans DrawingPlugin.draw(), après le bloc du 'path' :
							if (this.manager.mode === 'curve' && this.manager.points.length === 2) {
								const p1 = {
									x: this.chart.timeScale().timeToCoordinate(this.manager.points[0].time),
									y: this.series.priceToCoordinate(this.manager.points[0].price)
								};
								const p2 = {
									x: this.chart.timeScale().timeToCoordinate(this.manager.points[1].time),
									y: this.series.priceToCoordinate(this.manager.points[1].price)
								};

								canvasCtx.beginPath();
								canvasCtx.setLineDash([5, 5]);
								canvasCtx.strokeStyle = 'rgba(0, 255, 255, 0.5)';
								canvasCtx.moveTo(p1.x, p1.y);
								canvasCtx.lineTo(p2.x, p2.y); // Affiche la corde de l'arc pendant qu'on ajuste la courbe
								canvasCtx.stroke();
								canvasCtx.setLineDash([]);
							}
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