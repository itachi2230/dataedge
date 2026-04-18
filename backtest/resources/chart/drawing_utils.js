window.DrawingUtils = {
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
                `<svg id="drawing-svg" style="position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:10"></svg>`);
            svg = document.getElementById('drawing-svg');
        }
        svg.innerHTML = '';
        if (!p1 || !p2 || !mode) return;

        const color = '#00FFFF';
        if (mode === 'rectangle') {
            const rect = document.createElementNS("http://www.w3.org/2000/svg", "rect");
            rect.setAttribute('x', Math.min(p1.x, p2.x));
            rect.setAttribute('y', Math.min(p1.y, p2.y));
            rect.setAttribute('width', Math.abs(p2.x - p1.x));
            rect.setAttribute('height', Math.abs(p2.y - p1.y));
            rect.setAttribute('fill', 'rgba(0, 255, 255, 0.1)');
            rect.setAttribute('stroke', color);
            rect.setAttribute('stroke-dasharray', '5,5');
            svg.appendChild(rect);
        } else {
            const line = document.createElementNS("http://www.w3.org/2000/svg", "line");
            line.setAttribute('x1', p1.x); line.setAttribute('y1', p1.y);
            line.setAttribute('x2', p2.x); line.setAttribute('y2', p2.y);
            line.setAttribute('stroke', color);
            line.setAttribute('stroke-dasharray', '5,5');
            svg.appendChild(line);
        }
    }
};