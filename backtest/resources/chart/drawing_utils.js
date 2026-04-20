window.DrawingUtils = {
    // Vérifie si la souris est sur un point (ancre)
    isOverPoint(px, py, x, y, radius = 12) {
        return Math.hypot(px - x, py - y) < radius;
    },

    getDistanceToSegment(px, py, x1, y1, x2, y2) {
        const l2 = Math.pow(x2 - x1, 2) + Math.pow(y2 - y1, 2);
        if (l2 === 0) return Math.hypot(px - x1, py - y1);
        let t = Math.max(0, Math.min(1, ((px - x1) * (x2 - x1) + (py - y1) * (y2 - y1)) / l2));
        return Math.hypot(px - (x1 + t * (x2 - x1)), py - (y1 + t * (y2 - y1)));
    },

    updatePreview(mode, p1, p2) {
        let svg = document.getElementById('drawing-svg');
        if (!svg) {
            document.getElementById('chart-container').insertAdjacentHTML('beforeend', 
                `<svg id="drawing-svg" style="position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:100"></svg>`);
            svg = document.getElementById('drawing-svg');
        }
        svg.innerHTML = '';
        if (!p1 || !p2 || !mode || !window.DrawingConfigs[mode]) return;
        svg.innerHTML = window.DrawingConfigs[mode].preview(p1, p2);
    }
};