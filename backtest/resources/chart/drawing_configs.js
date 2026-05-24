window.DrawingConfigs = {
    'trendline': { clicks: 2, render: (ctx, p1, p2) => { ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y); }, 
        preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` },
    
    'rectangle': { clicks: 2, fill: true, render: (ctx, p1, p2) => { ctx.rect(Math.min(p1.x, p2.x), Math.min(p1.y, p2.y), Math.abs(p2.x - p1.x), Math.abs(p2.y - p1.y)); },
        preview: (p1, p2) => `<rect x="${Math.min(p1.x, p2.x)}" y="${Math.min(p1.y, p2.y)}" width="${Math.abs(p2.x - p1.x)}" height="${Math.abs(p2.y - p1.y)}" fill="rgba(0,255,255,0.1)" stroke="#00FFFF" stroke-dasharray="5,5" />` },
    
   'long_pos': {
    clicks: 1,
    render: (ctx, p1, p2, p3) => {
        if (!p1 || !p2 || !p3) return;
        const entry = p1;
        const target = p2;
        const stop = p3;
        const width = target.x - entry.x;

        // 1. Zones de couleur
        ctx.fillStyle = "rgba(0, 255, 187, 0.3)";
        ctx.fillRect(entry.x, target.y, width, entry.y - target.y);
        ctx.fillStyle = "rgba(255, 82, 82, 0.3)";
        ctx.fillRect(entry.x, entry.y, width, stop.y - entry.y);

        // 2. Ligne d'entrée
        ctx.strokeStyle = "#FFFFFF";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(entry.x, entry.y);
        ctx.lineTo(entry.x + width, entry.y);
        ctx.stroke();

        // 3. Calcul et Affichage du Ratio R:R
        const risk = Math.abs(entry.y - stop.y);
        const reward = Math.abs(entry.y - target.y);
        const rr = risk !== 0 ? (reward / risk).toFixed(2) : "0.00";

        // Dessin du badge RR 
        const badgeW = 70;
        const badgeH = 20;
        const badgeX = entry.x + (width / 2) - (badgeW / 2);
        const badgeY = entry.y - (badgeH / 2);

        //ctx.fillStyle = "rgba(0, 0, 0, 0.4)";
        //ctx.fillRect(badgeX, badgeY, badgeW, badgeH);
        
        ctx.fillStyle = "#FFFFFF";
        ctx.font = "bold 12px Arial";
        ctx.textAlign = "center";
        ctx.fillText(`R/R: ${rr}`, entry.x + width+30, badgeY + 10);
        ctx.textAlign = "start"; // Reset pour les autres dessins
    }
},

'short_pos': {
    clicks: 1,
    render: (ctx, p1, p2, p3) => {
        if (!p1 || !p2 || !p3) return;
        const entry = p1;
        const target = p2;
        const stop = p3;
        const width = target.x - entry.x;

        // Zones (Inversées pour Short)
        ctx.fillStyle = "rgba(0, 255, 187, 0.3)";
        ctx.fillRect(entry.x, entry.y, width, target.y - entry.y);
        ctx.fillStyle = "rgba(255, 82, 82, 0.3)";
        ctx.fillRect(entry.x, stop.y, width, entry.y - stop.y);

        // Ligne d'entrée
        ctx.strokeStyle = "#FFFFFF";
        ctx.lineWidth = 1;
        ctx.beginPath();
        ctx.moveTo(entry.x, entry.y);
        ctx.lineTo(entry.x + width, entry.y);
        ctx.stroke();

        // Calcul R:R
        const risk = Math.abs(entry.y - stop.y);
        const reward = Math.abs(entry.y - target.y);
        const rr = risk !== 0 ? (reward / risk).toFixed(2) : "0.00";

        // Badge RR
        const badgeW = 70;
        const badgeH = 20;
        const badgeX = entry.x + (width / 2) - (badgeW / 2);
        const badgeY = entry.y - (badgeH / 2);	

        //ctx.fillStyle = "rgba(0, 0, 0, 0.4)";
        //ctx.fillRect(badgeX, badgeY, badgeW, badgeH);
        
        ctx.fillStyle = "#FFFFFF";
        ctx.font = "bold 12px Arial";
        ctx.textAlign = "center";
        ctx.fillText(`R/R: ${rr}`, entry.x + width+30, badgeY + 10);
        ctx.textAlign = "start";
    }
},
    'curve': { 
    clicks: 3, 
    render: (ctx, p1, p2, p3) => {
        if (!p1 || !p2 || !p3) return;
        
        ctx.beginPath();
        ctx.moveTo(p1.x, p1.y);
        // p1 = Départ
        // p2 = Courbure (cliqué en 3ème, mais utilisé comme point de contrôle)
        // p3 = Fin (cliqué en 2ème)
        
        // pts[0] = clic 1, pts[1] = clic 2, pts[2] = clic 3
        ctx.quadraticCurveTo(p3.x, p3.y, p2.x, p2.y); 
        ctx.stroke();
    },
    preview: (p1, p2) => {
        const mgr = window.DrawingManager;
        
        // ÉTAPE 1 : Entre le 1er et le 2ème clic (Ligne droite vers le bout)
        if (mgr.points.length === 1) {
            return `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />`;
        }
        
        // ÉTAPE 2 : Entre le 2ème et le 3ème clic (Arc élastique)
        if (mgr.points.length === 2) {
            const start = p1; // Premier clic
            const end = { // Deuxième clic (le bout)
                x: window.chart.timeScale().timeToCoordinate(mgr.points[1].time),
                y: window.candleSeries.priceToCoordinate(mgr.points[1].price)
            };
            const control = p2; // Position actuelle de la souris (la courbure)
            
            return `<path d="M ${start.x} ${start.y} Q ${control.x} ${control.y} ${end.x} ${end.y}" fill="none" stroke="#00FFFF" stroke-dasharray="5,5" />`;
        }
        return '';
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
    'horz_ray': { 
		clicks: 1, 
		render: (ctx, p1, width, height) => { 
			ctx.moveTo(p1.x, p1.y); 
			ctx.lineTo(width, p1.y); // Rayon : du point vers le bord droit
		},
		preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="5000" y2="${p1.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` 
	},

	'horz_line': { 
		clicks: 1, 
		render: (ctx, p1, width, height) => { 
			ctx.moveTo(0, p1.y); 
			ctx.lineTo(width, p1.y); // Ligne infinie : de gauche à droite
		},
		preview: (p1, p2) => `<line x1="0" y1="${p1.y}" x2="5000" y2="${p1.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` 
	},

	'vert_line': { 
		clicks: 1, 
		render: (ctx, p1, width, height) => { 
			ctx.moveTo(p1.x, 0); 
			ctx.lineTo(p1.x, height); // Ligne verticale : de haut en bas
		},
		preview: (p1, p2) => `<line x1="${p1.x}" y1="0" x2="${p1.x}" y2="5000" stroke="#00FFFF" stroke-dasharray="5,5" />` 
	},
    'path': { 
    clicks: Infinity, 
    render: (ctx, ...pts) => {
        const coords = pts.filter(p => typeof p === 'object' && p.x !== undefined);
        if (coords.length < 2) return;

        ctx.beginPath();
        ctx.moveTo(coords[0].x, coords[0].y);
        for (let i = 1; i < coords.length; i++) {
            ctx.lineTo(coords[i].x, coords[i].y);
        }
        ctx.stroke();

        // Flèche finale
        const last = coords[coords.length - 1];
        const prev = coords[coords.length - 2];
        const angle = Math.atan2(last.y - prev.y, last.x - prev.x);
        const headLen = 15;

        ctx.beginPath();
        ctx.moveTo(last.x, last.y);
        ctx.lineTo(last.x - headLen * Math.cos(angle - Math.PI/6), last.y - headLen * Math.sin(angle - Math.PI/6));
        ctx.moveTo(last.x, last.y);
        ctx.lineTo(last.x - headLen * Math.cos(angle + Math.PI/6), last.y - headLen * Math.sin(angle + Math.PI/6));
        ctx.stroke();
    },
    preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />`
},
    'arrow': { clicks: 2, render: (ctx, p1, p2) => {
        const angle = Math.atan2(p2.y - p1.y, p2.x - p1.x);
        ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y);
        ctx.lineTo(p2.x - 12 * Math.cos(angle - Math.PI/6), p2.y - 12 * Math.sin(angle - Math.PI/6));
        ctx.moveTo(p2.x, p2.y); ctx.lineTo(p2.x - 12 * Math.cos(angle + Math.PI/6), p2.y - 12 * Math.sin(angle + Math.PI/6));
    }, preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />` },

// Dans drawing_configs.js
'fibo': { 
    clicks: 2, 
    // On remplace 'settings' par 'isSelected' et 'd' pour matcher l'appel du plugin
    render: (ctx, p1, p2, w, h, isSelected, d) => {
        if(!p1 || !p2) return;

        // ON RÉCUPÈRE LES SETTINGS DEPUIS L'OBJET 'd'
        const settings = d.data.settings;
        const activeLevels = settings?.levels || [0, 0.382, 0.5, 0.618, 1];
        const showFill = settings?.showFill || false;

        const xMin = Math.min(p1.x, p2.x);
        const xMax = Math.max(p1.x, p2.x);
        const price1 = window.candleSeries.coordinateToPrice(p1.y);
        const price2 = window.candleSeries.coordinateToPrice(p2.y);
        const priceDiff = price1 - price2; 

        ctx.save();
        
        // Style de ligne pour la Fibo
        ctx.strokeStyle = isSelected ? "#00FFFF" : "rgba(0, 255, 255, 0.5)";
        ctx.lineWidth = 1;

        activeLevels.forEach((l) => {
            const y = p2.y + (p1.y - p2.y) * l;
            const currentPrice = (price2 + priceDiff * l).toFixed(2);

            ctx.beginPath();
            ctx.moveTo(xMin, y);
            ctx.lineTo(xMax, y);
            ctx.stroke();

            ctx.fillStyle = "#00FFFF";
            ctx.font = "10px Arial";
            ctx.fillText(`${l} (${currentPrice})`, xMax + 5, y + 3);
        });

        // Remplissage (Fill)
        if (showFill && activeLevels.length >= 2) {
            const sorted = [...activeLevels].sort((a,b) => a-b);
            ctx.fillStyle = "rgba(0, 255, 255, 0.1)";
            const yStart = p2.y + (p1.y - p2.y) * sorted[0];
            const yEnd = p2.y + (p1.y - p2.y) * sorted[sorted.length - 1];
            ctx.fillRect(xMin, Math.min(yStart, yEnd), xMax - xMin, Math.abs(yEnd - yStart));
        }

        // Diagonale de contrôle
        ctx.setLineDash([5, 5]);
        ctx.beginPath(); ctx.moveTo(p1.x, p1.y); ctx.lineTo(p2.x, p2.y); ctx.stroke();
        ctx.restore();
    },
    preview: (p1, p2) => `<line x1="${p1.x}" y1="${p1.y}" x2="${p2.x}" y2="${p2.y}" stroke="#00FFFF" stroke-dasharray="5,5" />`
},

};