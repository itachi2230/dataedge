// tools/trendline.js
window.DrawingManager.registerTool('trendline', {
    name: 'Ligne',
    icon: '🖊️',
    render: (ctx, p1, p2, isDark, isTemp, isSelected) => {
        ctx.save(); // Bonne pratique
        ctx.strokeStyle = isSelected ? '#FFD700' : (isDark ? '#00FFFF' : '#2196F3');
        ctx.lineWidth = isSelected ? 4 : 2;
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        ctx.lineTo(p2.x, p2.y);
        ctx.stroke();
        ctx.restore();
    }
});