// drawing_plugin.js
class DrawingPlugin {
    constructor(type, start, end, opts = {}) {
        this.type = type;
        this.start = start;
        this.end = end || start;
        this.opts = opts;
    }

    updatePoints(s, e) {
        this.start = s;
        this.end = e;
    }

    renderer() {
        return {
            draw: (ctx, target, vert, horz) => {
                if (!this.start || !this.end) return;
                const x1 = horz.timeToCoordinate(this.start.time);
                const y1 = target.priceToCoordinate(this.start.price);
                const x2 = horz.timeToCoordinate(this.end.time);
                const y2 = target.priceToCoordinate(this.end.price);
                
                if (x1 === null || x2 === null || y1 === null || y2 === null) return;

                ctx.save();
                // Style de la ligne
                ctx.strokeStyle = this.opts.isSelected ? '#FFD700' : (this.opts.isDark ? '#00FFFF' : '#2196F3');
                ctx.lineWidth = this.opts.isSelected ? 3 : 2;
                
                if (this.opts.isTemp) {
                    ctx.setLineDash([5, 5]); // Pointillés pour l'aperçu
                }

                ctx.beginPath();
                ctx.moveTo(x1, y1);
                ctx.lineTo(x2, y2);
                ctx.stroke();

                // Petits cercles aux extrémités si sélectionné
                if (this.opts.isSelected && !this.opts.isTemp) {
                    ctx.fillStyle = "white";
                    ctx.strokeStyle = "#FFD700";
                    [{x:x1, y:y1}, {x:x2, y:y2}].forEach(p => {
                        ctx.beginPath();
                        ctx.arc(p.x, p.y, 4, 0, Math.PI * 2);
                        ctx.fill();
                        ctx.stroke();
                    });
                }
                ctx.restore();
            },
            zOrder: () => 'pane'
        };
    }
}