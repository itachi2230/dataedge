window.DrawingConfigs = {
    'trendline': { clicks: 2, render: (ctx, p1, p2) => { ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y); }, 
        preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` },
    
    'rectangle': { clicks: 2, fill: true, render: (ctx, p1, p2) => { ctx.rect(Math.min(p1.x, p2.x), Math.min(p1.y, p2.y), Math.abs(p2.x - p1.x), Math.abs(p2.y - p1.y)); },
        preview: (p1, p2) => `<rect x="${Math.min(p1.x, p2.x)}" y="${Math.min(p1.y, p2.y)}" width="${Math.abs(p2.x - p1.x)}" height="${Math.abs(p2.y - p1.y)}" fill="rgba(0,255,255,0.1)" stroke="#00FFFF" stroke-dasharray="5,5" />` },
    
    'long_pos': { clicks: 1, fill: true, render: (ctx, p1, p2) => {
        const h = Math.abs(p2.y - p1.y) || 40; 
        ctx.fillStyle = "rgba(0, 255, 0, 0.2)"; ctx.fillRect(p1.x, p1.y - h, (p2.x - p1.x) || 100, h);
        ctx.fillStyle = "rgba(255, 0, 0, 0.2)"; ctx.fillRect(p1.x, p1.y, (p2.x - p1.x) || 100, h);
        ctx.moveTo(p1.x, p1.y); ctx.lineTo(p1.x + 100, p1.y);
    }, preview: (p1, p2) => `<rect x="${p1.x}" y="${p1.y-40}" width="100" height="40" fill="rgba(0,255,0,0.2)"/><rect x="${p1.x}" y="${p1.y}" width="100" height="40" fill="rgba(255,0,0,0.2)"/>` },

    'short_pos': { clicks: 1, fill: true, render: (ctx, p1, p2) => {
        const h = Math.abs(p2.y - p1.y) || 40;
        ctx.fillStyle = "rgba(255, 0, 0, 0.2)"; ctx.fillRect(p1.x, p1.y - h, (p2.x - p1.x) || 100, h);
        ctx.fillStyle = "rgba(0, 255, 0, 0.2)"; ctx.fillRect(p1.x, p1.y, (p2.x - p1.x) || 100, h);
        ctx.moveTo(p1.x, p1.y); ctx.lineTo(p1.x + 100, p1.y);
    }, preview: (p1, p2) => `<rect x="${p1.x}" y="${p1.y-40}" width="100" height="40" fill="rgba(255,0,0,0.2)"/><rect x="${p1.x}" y="${p1.y}" width="100" height="40" fill="rgba(0,255,0,0.2)"/>` },

    'curve': { 
		clicks: 3, 
		render: (ctx, p1, p2, p3) => {
			if (!p1 || !p2 || !p3) return;
			ctx.moveTo(p1.x, p1.y);
			ctx.quadraticCurveTo(p3.x, p3.y, p2.x, p2.y);
		},
		preview: (p1, p2, p3) => {
			if (!p3) {
				// Entre clic 1 et 2 : ligne droite pour montrer la direction
				return `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />`;
			}
			// Entre clic 2 et 3 : aperçu de la courbe avec la position actuelle de la souris
			return `<path d="M ${p1.x} ${p1.y} Q ${p3.x} ${p3.y} ${p2.x} ${p2.y}" fill="none" stroke="#00FFFF" stroke-dasharray="5,5" />`;
		}
	},
	'text': { 
    clicks: 1, 
    render: (ctx, p1) => { 
        ctx.font = "14px Arial"; 
        ctx.fillStyle = "#00FFFF";
        
        // On vérifie p1.text qui vient maintenant du plugin
        const content = (p1.text && p1.text.trim() !== "") ? p1.text : "Cliquer ici pour modifier";
        
        ctx.fillText(content, p1.x, p1.y); 
    },
    preview: (p1) => `<text x="${p1.x}" y="${p1.y}" fill="#00FFFF" font-size="14">Saisie...</text>` 
},
    'horz_ray': { clicks: 1, render: (ctx, p1, p2, w) => { ctx.moveTo(p1.x, p1.y); ctx.lineTo(w, p1.y); },
        preview: (p1) => `<line x1="${p1.x}" y1="${p1.y}" x2="3000" y2="${p1.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` },

    'horz_line': { clicks: 1, render: (ctx, p1, p2, w) => { ctx.moveTo(0, p1.y); ctx.lineTo(w, p1.y); },
        preview: (p1) => `<line x1="0" y1="${p1.y}" x2="3000" y2="${p1.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` },

    'vert_line': { clicks: 1, render: (ctx, p1, p2, w, h) => { ctx.moveTo(p1.x, 0); ctx.lineTo(p1.x, h); },
        preview: (p1) => `<line x1="${p1.x}" y1="0" x2="${p1.x}" y2="2000" stroke="#00FFFF" stroke-dasharray="5,5" />` },

    'path': { clicks: Infinity, render: (ctx, ...pts) => {
        if(pts.length < 2) return; ctx.moveTo(pts[0].x, pts[0].y);
        for(let i=1; i<pts.length; i++) ctx.lineTo(pts[i].x, pts[i].y);
    }, preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` },

    'arrow': { clicks: 2, render: (ctx, p1, p2) => {
        const angle = Math.atan2(p2.y - p1.y, p2.x - p1.x);
        ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y);
        ctx.lineTo(p2.x - 12 * Math.cos(angle - Math.PI/6), p2.y - 12 * Math.sin(angle - Math.PI/6));
        ctx.moveTo(p2.x, p2.y); ctx.lineTo(p2.x - 12 * Math.cos(angle + Math.PI/6), p2.y - 12 * Math.sin(angle + Math.PI/6));
    }, preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` },

    'fibo': { clicks: 2, render: (ctx, p1, p2, w) => {
        const levels = [0, 0.236, 0.382, 0.5, 0.618, 0.786, 1];
        levels.forEach(l => { 
            const y = p1.y + (p2.y - p1.y) * l;
            ctx.moveTo(0, y); ctx.lineTo(w, y);
            ctx.fillText(l.toString(), 10, y - 2);
        });
    }, preview: (p1, p2) => `<line x1="0" y1="${p1.y}" x2="2000" y2="${p1.y}" stroke="#00FFFF" opacity="0.3"/><line x1="0" y1="${p2.y}" x2="2000" y2="${p2.y}" stroke="#00FFFF" opacity="0.3"/>` }
};