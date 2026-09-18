(function () {
    'use strict';

    var canvas = document.getElementById('bg-canvas');
    if (!canvas) return;
    var ctx = canvas.getContext('2d', { alpha: true });
    if (!ctx) return;

    var html = document.documentElement;
    var reducedMotion = false;
    var perfLow = false;
    var veryLow = false;
    try {
        reducedMotion = window.matchMedia('(prefers-reduced-motion: reduce)').matches;
        perfLow = html.classList.contains('perf-low');
        veryLow = html.classList.contains('perf-very-low');
    } catch (e) {}

    if (veryLow) {
        canvas.style.display = 'none';
        return;
    }

    var W = 0, H = 0;
    var particles = [];
    var running = true;
    var light = false;
    var rafId = 0;
    var lastFrame = 0;
    var frameInterval = perfLow ? 66 : 33;
    var resizeTimer = 0;
    var mx = -9999, my = -9999, tmx = -9999, tmy = -9999;

    var COUNT = reducedMotion ? 0 : perfLow ? 45 : 90;
    var MAX_DIST = 130;
    var MAX_DIST2 = MAX_DIST * MAX_DIST;
    var LINK_CAP = perfLow ? 24 : 48;
    var MOUSE_RAD = 170;
    var MOUSE_RAD2 = MOUSE_RAD * MOUSE_RAD;

    function isLight() {
        return html.getAttribute('data-theme') === 'light';
    }

    function isPaused() {
        return document.hidden || html.classList.contains('bg-paused');
    }

    function resize() {
        var dpr = window.devicePixelRatio || 1;
        if (dpr > 1.25) dpr = 1.25;
        W = window.innerWidth;
        H = window.innerHeight;
        canvas.width = Math.max(1, Math.floor(W * dpr));
        canvas.height = Math.max(1, Math.floor(H * dpr));
        canvas.style.width = W + 'px';
        canvas.style.height = H + 'px';
        ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
        seed();
        if (reducedMotion) renderStatic();
    }

    function seed() {
        particles = [];
        for (var i = 0; i < COUNT; i++) {
            particles.push({
                x: Math.random() * W,
                y: Math.random() * H,
                vx: (Math.random() - 0.5) * 0.3,
                vy: (Math.random() - 0.5) * 0.3,
                r: Math.random() * 1.3 + 0.5,
                tw: Math.random() * 6.283
            });
        }
    }

    function renderStatic() {
        ctx.clearRect(0, 0, W, H);
        light = isLight();
        var dot = light ? 'rgba(23,32,42,0.35)' : 'rgba(255,255,255,0.4)';
        ctx.fillStyle = dot;
        ctx.beginPath();
        for (var i = 0; i < particles.length; i++) {
            var p = particles[i];
            ctx.moveTo(p.x + p.r, p.y);
            ctx.arc(p.x, p.y, p.r, 0, 6.283);
        }
        ctx.fill();
    }

    function frame(now) {
        rafId = 0;
        if (!running) return;
        if (isPaused()) {
            schedule();
            return;
        }
        if (now - lastFrame < frameInterval) {
            schedule();
            return;
        }
        lastFrame = now;
        var dt = frameInterval / 1000;

        ctx.clearRect(0, 0, W, H);

        mx += (tmx - mx) * 0.08;
        my += (tmy - my) * 0.08;

        var i, p, dxm, dym, dm2, dm, force;
        for (i = 0; i < particles.length; i++) {
            p = particles[i];
            p.tw += dt * 1.4;
            dxm = p.x - mx;
            dym = p.y - my;
            dm2 = dxm * dxm + dym * dym;
            if (dm2 < MOUSE_RAD2 && dm2 > 0.01) {
                dm = Math.sqrt(dm2);
                force = ((MOUSE_RAD - dm) / MOUSE_RAD) * 26 * dt;
                p.vx += (dxm / dm) * force;
                p.vy += (dym / dm) * force;
            }
            p.vx *= 0.985;
            p.vy *= 0.985;
            p.x += p.vx;
            p.y += p.vy;
            if (p.x < -12) p.x = W + 12;
            else if (p.x > W + 12) p.x = -12;
            if (p.y < -12) p.y = H + 12;
            else if (p.y > H + 12) p.y = -12;
        }

        var prefix = light ? 'rgba(23,32,42,' : 'rgba(255,255,255,';
        ctx.fillStyle = prefix + '0.55)';
        ctx.beginPath();
        for (i = 0; i < particles.length; i++) {
            p = particles[i];
            ctx.moveTo(p.x + p.r, p.y);
            ctx.arc(p.x, p.y, p.r, 0, 6.283);
        }
        ctx.fill();

        ctx.lineWidth = 1;
        ctx.strokeStyle = light ? 'rgba(23,32,42,0.10)' : 'rgba(255,255,255,0.10)';
        ctx.beginPath();
        var drawn = 0;
        for (i = 0; i < particles.length && drawn < LINK_CAP; i++) {
            var a = particles[i];
            var best = null, bestD = MAX_DIST2;
            for (var j = i + 1; j < particles.length; j++) {
                var b = particles[j];
                var dx = a.x - b.x, dy = a.y - b.y;
                var d2 = dx * dx + dy * dy;
                if (d2 < bestD) { bestD = d2; best = b; }
            }
            if (best) {
                ctx.moveTo(a.x, a.y);
                ctx.lineTo(best.x, best.y);
                drawn++;
            }
        }
        ctx.stroke();

        schedule();
    }

    function schedule() {
        if (!rafId && running && !reducedMotion) {
            rafId = requestAnimationFrame(frame);
        }
    }

    function start() {
        if (reducedMotion || !COUNT) return;
        running = true;
        lastFrame = performance.now();
        schedule();
    }

    function stop() {
        running = false;
        if (rafId) {
            try { cancelAnimationFrame(rafId); } catch (e) {}
            rafId = 0;
        }
    }

    window.addEventListener('resize', function () {
        clearTimeout(resizeTimer);
        resizeTimer = setTimeout(resize, 150);
    }, { passive: true });
    window.addEventListener('mousemove', function (e) {
        tmx = e.clientX;
        tmy = e.clientY;
    }, { passive: true });
    document.addEventListener('mouseleave', function () {
        tmx = -9999;
        tmy = -9999;
    }, { passive: true });
    document.addEventListener('visibilitychange', function () {
        if (document.hidden) stop();
        else {
            light = isLight();
            running = true;
            lastFrame = performance.now();
            schedule();
        }
    }, { passive: true });
    window.addEventListener('blur', stop, { passive: true });
    window.addEventListener('focus', function () {
        if (!document.hidden) {
            running = true;
            lastFrame = performance.now();
            schedule();
        }
    }, { passive: true });
    try {
        new MutationObserver(function () {
            var v = isLight();
            if (v !== light) light = v;
        }).observe(html, { attributes: true, attributeFilter: ['data-theme'] });
    } catch (e) {}

    light = isLight();
    resize();
    start();
})();
