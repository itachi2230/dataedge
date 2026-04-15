//point fonctionnel avec rectangle

class DrawingPlugin {
    constructor(manager, drawingData) {
        this.manager = manager;
        this.data = drawingData; // { type, start, end }
    }

    updatePoints(s, e) {
        this.data.start = s;
        this.data.end = e;
    }

    renderer() {
        return {
            draw: (ctx, target, vert, horz) => {
                const d = this.data;
                const isSelected = (this.manager.drawings.indexOf(this) === this.manager.selectedIdx);
                
                const x1 = horz.timeToCoordinate(d.start.time);
                const y1 = target.priceToCoordinate(d.start.price);
                const x2 = horz.timeToCoordinate(d.end.time);
                const y2 = target.priceToCoordinate(d.end.price);

                if (x1 === null || x2 === null || y1 === null || y2 === null) return;

                ctx.save();
                // Couleurs basées sur la sélection et le thème
                ctx.strokeStyle = isSelected ? '#FFD700' : (window.isDarkMode ? '#00FFFF' : '#2196F3');
                ctx.lineWidth = isSelected ? 3 : 2;

                ctx.beginPath();
                ctx.moveTo(x1, y1);
                ctx.lineTo(x2, y2);
                ctx.stroke();

                // Points d'ancrage si sélectionné
                if (isSelected) {
                    ctx.fillStyle = "white";
                    ctx.strokeStyle = "#FFD700";
                    ctx.lineWidth = 2;
                    [{x:x1, y:y1}, {x:x2, y:y2}].forEach(p => {
                        ctx.beginPath();
                        ctx.arc(p.x, p.y, 5, 0, Math.PI * 2);
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