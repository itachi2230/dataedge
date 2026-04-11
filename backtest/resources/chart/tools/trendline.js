// tools/trendline.js
window.DrawingManager.registerTool('trendline', {
    name: 'Ligne',
    icon: '🖊️',

    render: (ctx, p1, p2, isDark, isTemp, isSelected) => {
        ctx.save();
        
        // Couleur et épaisseur
        ctx.strokeStyle = isSelected ? '#FFD700' : (isDark ? '#00FFFF' : '#2196F3');
        ctx.lineWidth = isSelected ? 3 : 2;
        if (isTemp) ctx.setLineDash([5, 5]);

        // Dessin de la ligne
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.stroke();

        // Poignées de redimensionnement si sélectionné
        if (isSelected && !isTemp) {
            ctx.fillStyle = "white";
            ctx.strokeStyle = "#FFD700";
            [p1, p2].forEach(p => {
                ctx.beginPath();
                ctx.arc(p.x, p.y, 6, 0, Math.PI * 2);
                ctx.fill();
                ctx.stroke();
            });
        }
        ctx.restore();
    },

    // CETTE FONCTION EST INDISPENSABLE POUR LE DRAG/DROP
    isPointNear: (mouse, p1, p2) => {
        const threshold = 12; // Zone de tolérance en pixels

        // 1. Check des extrémités (Redimensionner)
        const d1 = Math.hypot(mouse.x - p1.x, mouse.y - p1.y);
        const d2 = Math.hypot(mouse.x - p2.x, mouse.y - p2.y);

        if (d1 < threshold) return 'start';
        if (d2 < threshold) return 'end';

        // 2. Check du corps de la ligne (Déplacer)
        const L2 = Math.pow(p2.x - p1.x, 2) + Math.pow(p2.y - p1.y, 2);
        if (L2 === 0) return d1 < threshold ? 'move' : null;

        let t = ((mouse.x - p1.x) * (p2.x - p1.x) + (mouse.y - p1.y) * (p2.y - p1.y)) / L2;
        t = Math.max(0, Math.min(1, t));

        const projX = p1.x + t * (p2.x - p1.x);
        const projY = p1.y + t * (p2.y - p1.y);
        const dist = Math.hypot(mouse.x - projX, mouse.y - projY);

        return dist < threshold ? 'move' : null;
    }
});