/* iRis background — pure 2D canvas particle constellation (B&W, no dependencies).
 * Replaces the previous three.js scene: lighter, faster, offline-friendly. */
(function () {
    'use strict';

    const canvas = document.getElementById('bg-canvas');
    if (!canvas) return;
    const ctx = canvas.getContext('2d', { alpha: true });
    if (!ctx) return;

    const reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
    const html = document.documentElement;
    const perfLow = html.classList.contains('perf-low');
    const veryLow = html.classList.contains('perf-very-low');

    let W = 0, H = 0, DPR = 1;
    let particles = [];
    let links = [];
    const mouse = { x: -9999, y: -9999, tx: -9999, ty: -9999 };
    let running = true;
    let light = false;

    const isLight = () => html.getAttribute('data-theme') === 'light';

    // Density tuned by hardware + reduced-motion preferences.
    const targetCount = veryLow ? 0 : reducedMotion ? 70 : perfLow ? 110 : 190;

    function resize() {
        DPR = Math.min(window.devicePixelRatio || 1, 2);
        W = window.innerWidth;
        H = window.innerHeight;
        canvas.width = Math.max(1, Math.floor(W * DPR));
        canvas.height = Math.max(1, Math.floor(H * DPR));
        canvas.style.width = W + 'px';
        canvas.style.height = H + 'px';
        ctx.setTransform(DPR, 0, 0, DPR, 0, 0);
        seed();
    }

    function seed() {
        particles = [];
        for (let i = 0; i < targetCount; i++) {
            particles.push({
                x: Math.random() * W,
                y: Math.random() * H,
                vx: (Math.random() - 0.5) * 0.22,
                vy: (Math.random() - 0.5) * 0.22,
                r: Math.random() * 1.4 + 0.5,
                tw: Math.random() * Math.PI * 2
            });
        }
        // Static link grid for perf-friendly modes.
        links = [];
        if (reducedMotion || veryLow) {
            for (let i = 0; i < particles.length; i++) {
                for (let j = i + 1; j < particles.length; j++) {
                    const a = particles[i], b = particles[j];
                    const dx = a.x - b.x, dy = a.y - b.y;
                    if (dx * dx + dy * dy < 120 * 120) {
                        links.push([i, j, Math.hypot(dx, dy)]);
                    }
                }
            }
        }
    }

    function drawLinks(dt) {
        if (reducedMotion || veryLow) {
            for (const [i, j, dist] of links) {
                const a = particles[i], b = particles[j];
                const alpha = Math.max(0, 1 - dist / 120) * 0.10;
                ctx.strokeStyle = light
                    ? 'rgba(23,32,42,' + alpha.toFixed(3) + ')'
                    : 'rgba(255,255,255,' + alpha.toFixed(3) + ')';
                ctx.lineWidth = 1;
                ctx.beginPath();
                ctx.moveTo(a.x, a.y);
                ctx.lineTo(b.x, b.y);
                ctx.stroke();
            }
            return;
        }

        // Dynamic nearest-neighbour links, capped for performance.
        const maxDist = 130;
        const cap = veryLow ? 0 : perfLow ? 40 : 80;
        let drawn = 0;
        for (let i = 0; i < particles.length && drawn < cap; i++) {
            const a = particles[i];
            let best = null, bestD = maxDist * maxDist;
            for (let j = i + 1; j < particles.length; j++) {
                const b = particles[j];
                const dx = a.x - b.x, dy = a.y - b.y;
                const d2 = dx * dx + dy * dy;
                if (d2 < bestD) { bestD = d2; best = b; }
            }
            if (best) {
                const alpha = Math.max(0, 1 - Math.sqrt(bestD) / maxDist) * 0.14;
                ctx.strokeStyle = light
                    ? 'rgba(23,32,42,' + alpha.toFixed(3) + ')'
                    : 'rgba(255,255,255,' + alpha.toFixed(3) + ')';
                ctx.lineWidth = 1;
                ctx.beginPath();
                ctx.moveTo(a.x, a.y);
                ctx.lineTo(best.x, best.y);
                ctx.stroke();
                drawn++;
            }
        }
    }

    let last = performance.now();
    function frame(now) {
        if (!running) return;
        requestAnimationFrame(frame);
        const dt = Math.min(0.05, (now - last) / 1000);
        last = now;

        ctx.clearRect(0, 0, W, H);

        // Ease mouse.
        mouse.x += (mouse.tx - mouse.x) * 0.06;
        mouse.y += (mouse.ty - mouse.y) * 0.06;

        const mRad = 170;

        for (const p of particles) {
            p.tw += dt * 1.4;

            // Gentle repulsion from the cursor.
            const dxm = p.x - mouse.x, dym = p.y - mouse.y;
            const dm2 = dxm * dxm + dym * dym;
            if (dm2 < mRad * mRad && dm2 > 0.01) {
                const dm = Math.sqrt(dm2);
                const force = ((mRad - dm) / mRad) * 26 * dt;
                p.vx += (dxm / dm) * force;
                p.vy += (dym / dm) * force;
            }

            p.vx *= 0.985;
            p.vy *= 0.985;
            p.x += p.vx;
            p.y += p.vy;

            // Wrap around edges.
            if (p.x < -12) p.x = W + 12;
            else if (p.x > W + 12) p.x = -12;
            if (p.y < -12) p.y = H + 12;
            else if (p.y > H + 12) p.y = -12;

            const pulse = 0.6 + 0.4 * Math.sin(p.tw);
            const alpha = 0.28 + 0.5 * pulse;
            ctx.fillStyle = light
                ? 'rgba(23,32,42,' + alpha.toFixed(3) + ')'
                : 'rgba(255,255,255,' + alpha.toFixed(3) + ')';
            ctx.beginPath();
            ctx.arc(p.x, p.y, p.r * (0.8 + 0.3 * pulse), 0, Math.PI * 2);
            ctx.fill();
        }

        drawLinks(dt);
    }

    function applyTheme() {
        light = isLight();
    }

    function onMouse(e) {
        mouse.tx = e.clientX;
        mouse.ty = e.clientY;
    }
    function onLeave() {
        mouse.tx = -9999;
        mouse.ty = -9999;
    }

    window.addEventListener('resize', resize, { passive: true });
    window.addEventListener('mousemove', onMouse, { passive: true });
    document.addEventListener('mouseleave', onLeave, { passive: true });
    document.addEventListener('visibilitychange', () => {
        if (document.hidden) { running = false; }
        else { running = true; requestAnimationFrame(frame); }
    });
    new MutationObserver(() => {
        if (light !== isLight()) applyTheme();
    }).observe(html, { attributes: true, attributeFilter: ['data-theme'] });

    applyTheme();
    resize();
    if (!veryLow) requestAnimationFrame(frame);
})();
