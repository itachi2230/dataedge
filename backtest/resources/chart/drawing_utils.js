//point fonctionnel avec rectangle
window.DrawingUtils = {
    types: {
        trendline: {
            create: (chart) => chart.addLineSeries({
                lineWidth: 2,
                priceLineVisible: false,
                lastPriceAnimation: 0,
                crosshairMarkerVisible: false,
                autoscaleInfoProvider: () => null,
            }),
            update: (series, d) => {
                // On vérifie que les points existent avant de setData
                if (!d.start || !d.end || d.start.time === null || d.end.time === null) return;
                series.setData([
                    { time: d.start.time, value: d.start.price },
                    { time: d.end.time, value: d.end.price }
                ]);
            }
        },
        rectangle: {
            create: (chart) => chart.addAreaSeries({
                lineWidth: 2,
                priceLineVisible: false,
                lastPriceAnimation: 0,
                autoscaleInfoProvider: () => null,
            }),
            update: (series, d) => {
                if (!d.start || !d.end || d.start.time === null || d.end.time === null) return;
                series.setData([
                    { time: d.start.time, value: d.start.price },
                    { time: d.end.time, value: d.start.price },
                    { time: d.end.time, value: d.end.price },
                    { time: d.start.time, value: d.end.price },
                    { time: d.start.time, value: d.start.price },
                ]);
            }
        }
    },

    updatePreview(mode, p1, p2) {
        let svg = document.getElementById('drawing-svg');
        if (!svg) {
            const container = document.getElementById('chart-container');
            container.insertAdjacentHTML('beforeend', `
                <svg id="drawing-svg" style="position:absolute;top:0;left:0;width:100%;height:100%;pointer-events:none;z-index:1000;">
                    <line id="temp-line" stroke-width="2" stroke-dasharray="5,5" style="display:none"/>
                    <rect id="temp-rect" stroke-width="2" stroke-dasharray="5,5" fill="rgba(33, 150, 243, 0.2)" style="display:none"/>
                </svg>`);
            svg = document.getElementById('drawing-svg');
        }

        const line = document.getElementById('temp-line');
        const rect = document.getElementById('temp-rect');
        const color = window.isDarkMode ? '#00FFFF' : '#2196F3';

        if (!p1 || !p2 || !mode) {
            line.style.display = 'none';
            rect.style.display = 'none';
            return;
        }

        if (mode === 'rectangle') {
            const x = Math.min(p1.x, p2.x), y = Math.min(p1.y, p2.y);
            const w = Math.abs(p2.x - p1.x), h = Math.abs(p2.y - p1.y);
            rect.setAttribute('x', x); rect.setAttribute('y', y);
            rect.setAttribute('width', w); rect.setAttribute('height', h);
            rect.setAttribute('stroke', color);
            rect.style.display = 'block'; line.style.display = 'none';
        } else {
            line.setAttribute('x1', p1.x); line.setAttribute('y1', p1.y);
            line.setAttribute('x2', p2.x); line.setAttribute('y2', p2.y);
            line.setAttribute('stroke', color);
            line.style.display = 'block'; rect.style.display = 'none';
        }
    }
};