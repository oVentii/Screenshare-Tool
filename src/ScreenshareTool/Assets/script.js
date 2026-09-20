(function () {
    'use strict';

    const SoundFX = (() => {
        let ctx = null;
        let master = null;
        let unlocked = false;
        let muted = document.documentElement.getAttribute('data-muted') === '1';
        let lastHoverT = 0;

        function unlock() {
            if (unlocked) return;
            try {
                const AC = window.AudioContext || window.webkitAudioContext;
                if (!AC) return;
                ctx = new AC();
                master = ctx.createGain();
                master.gain.value = muted ? 0 : 1;
                master.connect(ctx.destination);
                unlocked = true;
            } catch (e) {
                return;
            }
            if (ctx.state === 'suspended') {
                try {
                    const resumed = ctx.resume();
                    if (resumed && resumed.catch) resumed.catch(() => {});
                } catch (e) {}
            }
        }

        const gestureEvents = ['pointerdown', 'keydown', 'mousedown', 'touchstart'];
        const tryUnlock = () => {
            unlock();
            gestureEvents.forEach(ev =>
                window.removeEventListener(ev, tryUnlock, { capture: true }));
        };
        gestureEvents.forEach(ev =>
            window.addEventListener(ev, tryUnlock, { capture: true }));

        function getContext() {
            if (!unlocked) unlock();
            return ctx;
        }

        function tone(c, freq, type, vol, start, duration, freqEnd = null) {
            const osc = c.createOscillator();
            const gain = c.createGain();
            osc.type = type;
            osc.frequency.setValueAtTime(freq, start);
            if (freqEnd && freqEnd !== freq) {
                osc.frequency.exponentialRampToValueAtTime(Math.max(freqEnd, 30), start + duration);
            }
            gain.gain.setValueAtTime(0, start);
            gain.gain.linearRampToValueAtTime(vol, start + 0.01);
            gain.gain.exponentialRampToValueAtTime(0.001, start + duration);
            osc.connect(gain);
            gain.connect(master || c.destination);
            osc.start(start);
            osc.stop(start + duration + 0.05);
        }

        const noiseCache = new Map();
        function noiseBuffer(c, duration) {
            const key = c.sampleRate + ':' + duration;
            let buffer = noiseCache.get(key);
            if (buffer) return buffer;
            const size = Math.max(1, Math.floor(c.sampleRate * duration));
            buffer = c.createBuffer(1, size, c.sampleRate);
            const data = buffer.getChannelData(0);
            for (let i = 0; i < size; i++) {
                data[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / size, 1.8);
            }
            if (noiseCache.size > 8) {
                const oldest = noiseCache.keys().next().value;
                noiseCache.delete(oldest);
            }
            noiseCache.set(key, buffer);
            return buffer;
        }

        function noise(c, vol, duration, start = 0) {
            const src = c.createBufferSource();
            const gain = c.createGain();
            src.buffer = noiseBuffer(c, duration);
            gain.gain.setValueAtTime(vol, start);
            gain.gain.exponentialRampToValueAtTime(0.001, start + duration);
            src.connect(gain);
            gain.connect(master || c.destination);
            src.start(start);
            src.stop(start + duration + 0.02);
        }

        function play(fn) {
            if (muted) return;
            const c = getContext();
            if (!c || muted) return;
            try { fn(c); } catch (e) {}
        }

        const hover = () => {
            if (performance.now() - lastHoverT < 60) return;
            lastHoverT = performance.now();
            play(c => {
                const t = c.currentTime;
                tone(c, 880, 'sine', 0.035, t, 0.07, 1180);
                tone(c, 1320, 'sine', 0.018, t + 0.01, 0.05, 1550);
            });
        };

        const click = () => play(c => {
            const t = c.currentTime;
            tone(c, 620, 'sine', 0.09, t, 0.11, 180);
            tone(c, 240, 'triangle', 0.05, t, 0.09, 90);
            tone(c, 980, 'sine', 0.025, t + 0.015, 0.05, 420);
            noise(c, 0.035, 0.025, t);
        });

        const tool = () => play(c => {
            const t = c.currentTime;
            tone(c, 520, 'sine', 0.07, t, 0.16, 780);
            tone(c, 780, 'sine', 0.05, t + 0.04, 0.14, 1100);
            tone(c, 1100, 'sine', 0.03, t + 0.08, 0.1, 1400);
            tone(c, 310, 'triangle', 0.04, t, 0.12, 160);
            noise(c, 0.025, 0.04, t + 0.02);
        });

        const minimize = () => play(c => {
            const t = c.currentTime;
            tone(c, 740, 'sine', 0.07, t, 0.13, 320);
            tone(c, 480, 'triangle', 0.04, t + 0.02, 0.1, 180);
            tone(c, 960, 'sine', 0.02, t, 0.06, 600);
        });

        const close = () => play(c => {
            const t = c.currentTime;
            tone(c, 580, 'sine', 0.08, t, 0.14, 140);
            tone(c, 290, 'sawtooth', 0.035, t + 0.02, 0.12, 70);
            tone(c, 870, 'sine', 0.02, t, 0.05, 300);
            noise(c, 0.03, 0.04, t + 0.01);
        });

        const modalOpen = () => play(c => {
            const t = c.currentTime;
            tone(c, 380, 'sine', 0.06, t, 0.2, 620);
            tone(c, 620, 'sine', 0.045, t + 0.06, 0.18, 920);
            tone(c, 920, 'sine', 0.03, t + 0.12, 0.14, 1200);
        });

        const modalClose = () => play(c => {
            const t = c.currentTime;
            tone(c, 780, 'sine', 0.06, t, 0.13, 340);
            tone(c, 490, 'triangle', 0.035, t + 0.02, 0.11, 180);
        });

        const success = () => play(c => {
            const t = c.currentTime;
            [523.25, 659.25, 783.99, 1046.50].forEach((freq, i) => {
                tone(c, freq, 'sine', 0.07 - i * 0.01, t + i * 0.09, 0.32);
            });
        });

        const error = () => play(c => {
            const t = c.currentTime;
            tone(c, 280, 'sawtooth', 0.06, t, 0.22, 90);
            tone(c, 160, 'sawtooth', 0.045, t + 0.04, 0.26, 50);
            noise(c, 0.04, 0.12, t + 0.02);
        });

        const softHover = () => {
            if (performance.now() - lastHoverT < 60) return;
            lastHoverT = performance.now();
            play(c => {
                const t = c.currentTime;
                tone(c, 1040, 'sine', 0.022, t, 0.06, 1280);
            });
        };

        return {
            hover, softHover, click, tool, minimize, close,
            modalOpen, modalClose, success, error,
            setMuted(value) {
                muted = !!value;
                if (master) {
                    master.gain.cancelScheduledValues(0);
                    master.gain.value = muted ? 0 : 1;
                }
            }
        };
    })();

    const $ = (id) => document.getElementById(id);
    const $$ = (sel, root) => (root || document).querySelectorAll(sel);

    const escapeHtml = (s) => String(s ?? '').replace(/[&<>"']/g, c => ({
        '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));

    const APP_ICON = `<svg viewBox="0 0 24 24" fill="currentColor" stroke="none" aria-hidden="true"><path d="M21 12.79A9 9 0 1 1 11.21 3 7 7 0 0 0 21 12.79z"/></svg>`;

    const ICON = {
        lock: `<svg class="inline-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="5" y="11" width="14" height="10" rx="2"/><path d="M8 11V8a4 4 0 0 1 8 0v3"/></svg>`,
        warning: `<svg class="inline-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3L22 20H2L12 3z"/><path d="M12 9v5"/><path d="M12 17h.01"/></svg>`
    };

    const TYPE_ICONS = {
        success: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M20 6L9 17L4 12"/></svg>`,
        error: `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><line x1="12" y1="8" x2="12" y2="12"/><line x1="12" y1="16" x2="12.01" y2="16"/></svg>`,
        info: APP_ICON
    };

    function addRipple(btn, x, y) {
        if (!btn || document.documentElement.classList.contains('perf-very-low')) return;
        if (typeof x !== 'number' || typeof y !== 'number') return;
        const rect = btn.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;
        const size = Math.max(rect.width, rect.height) * 1.4;
        const el = document.createElement('span');
        el.className = 'ripple';
        el.style.width = size + 'px';
        el.style.height = size + 'px';
        el.style.left = (x - rect.left - size / 2) + 'px';
        el.style.top = (y - rect.top - size / 2) + 'px';
        if (!btn.style.position) btn.style.position = 'relative';
        btn.appendChild(el);
        el.addEventListener('animationend', () => el.remove(), { once: true });
        setTimeout(() => { if (el.isConnected) el.remove(); }, 1200);
    }

    const bindOnce = (el, onClick, sound = 'click') => {
        if (!el || el.dataset.bound === '1') return;
        el.dataset.bound = '1';
        el.addEventListener('mouseenter', () => SoundFX.hover());
        el.addEventListener('click', (e) => {
            if (sound === 'tool') SoundFX.tool();
            else SoundFX.click();
            addRipple(el, e.clientX, e.clientY);
            onClick(e);
        });
    };

    const debounce = (fn, ms) => {
        let t = 0;
        return (...args) => {
            clearTimeout(t);
            t = setTimeout(() => fn(...args), ms);
        };
    };

    const showScreen = (id) => {
        $$('.screen').forEach(s => s.classList.remove('active'));
        const next = document.getElementById(id);
        if (!next) return;
        next.classList.add('active');
        const scroller = next.querySelector('.content');
        if (scroller) scroller.scrollTop = 0;
    };

    const stopCurrentScan = () => {
        try { window.pywebview.api.cancel_scan(); } catch (e) {  }
    };

    const isCancellation = (err) => /cancell/i.test(String(err?.message || err || ''));

    const revealIn = (container, selector, cls, step = 70) => {
        container.querySelectorAll(selector).forEach((el, i) => {
            setTimeout(() => el.classList.add(cls), step * i);
        });
    };

    const revealSections = (container) => {
        revealIn(container, '.service-section, .svc-summary, .svc-banner', 'revealed');
    };

    const exportJson = (payload, filename) => {
        const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 5000);
    };

    const copyText = (text, done) => {
        if (navigator.clipboard?.writeText) {
            navigator.clipboard.writeText(text).then(() => done(true), () => fallbackCopy(text, done));
        } else {
            fallbackCopy(text, done);
        }
    };

    const svcSignal = (severity, key, value) => `
        <div class="svc-signal">
            <span class="svc-dot ${severity || ''}"></span>
            <span class="svc-signal-key">${escapeHtml(key)}</span>
            <span class="svc-signal-val ${severity || ''}">${escapeHtml(value)}</span>
        </div>`;

    const renderServiceSection = (icon, title, body, meta = '') => `
        <section class="service-section">
            <div class="service-section-header">
                <span class="service-section-icon">${icon}</span>
                <h3>${escapeHtml(title)}</h3>
                ${meta ? `<span class="svc-section-meta">${escapeHtml(meta)}</span>` : ''}
            </div>
            <div class="service-section-body">${body}</div>
        </section>`;

    const loadingState = (msg, sub) => `
        <div class="scanning-state">
            <div class="app-loader" role="status" aria-label="Loading">
                <img src="giflogo.gif" class="app-loader-logo" alt="">
            </div>
            <p>${escapeHtml(msg)}</p>
            ${sub ? `<p class="scan-msg">${escapeHtml(sub)}</p>` : ''}
        </div>`;

    (function bindThemeAndMute() {
        const root = document.documentElement;

        const currentTheme = () => root.getAttribute('data-theme') === 'light' ? 'light' : 'dark';

        function setTheme(next) {
            const value = next === 'light' ? 'light' : 'dark';
            root.setAttribute('data-theme', value);
            root.style.colorScheme = value;
            try { localStorage.setItem('iris-theme', value); } catch (e) {}
            window.dispatchEvent(new CustomEvent('iris-theme', { detail: value }));
            syncThemeButtons();
        }

        function syncThemeButtons() {
            const light = currentTheme() === 'light';
            $$('.js-theme-btn').forEach(btn => {
                btn.setAttribute('aria-pressed', light ? 'true' : 'false');
                btn.setAttribute('aria-label', light ? 'Switch to dark theme' : 'Switch to light theme');
                btn.title = light ? 'Dark theme' : 'Light theme';
            });
        }

        const isMuted = () => root.getAttribute('data-muted') === '1';

        function setMuted(value) {
            if (value) root.setAttribute('data-muted', '1');
            else root.removeAttribute('data-muted');
            try { localStorage.setItem('iris-mute', value ? '1' : '0'); } catch (e) {}
            SoundFX.setMuted(value);
            syncMuteButtons();
        }

        function syncMuteButtons() {
            const muted = isMuted();
            $$('.js-mute-btn').forEach(btn => {
                btn.setAttribute('aria-pressed', muted ? 'true' : 'false');
                btn.setAttribute('aria-label', muted ? 'Unmute sounds' : 'Mute sounds');
                btn.title = muted ? 'Unmute sounds' : 'Mute sounds';
            });
        }

        SoundFX.setMuted(isMuted());
        syncThemeButtons();
        syncMuteButtons();

        $$('.js-theme-btn').forEach(btn => {
            btn.addEventListener('click', () => setTheme(currentTheme() === 'light' ? 'dark' : 'light'));
        });
        $$('.js-mute-btn').forEach(btn => {
            btn.addEventListener('click', () => setMuted(!isMuted()));
        });
    })();

    const ui = {
        modal: $('modal'),
        modalIcon: $('modalIcon'),
        modalTitle: $('modalTitle'),
        modalMessage: $('modalMessage'),
        modalCloseBtn: $('modalCloseBtn'),
        modalActions: $('modalActions'),
        minimizeMainBtn: $('minimizeMainBtn'),
        closeMainBtn: $('closeMainBtn'),
        serviceBackBtn: $('serviceBackBtn'),
        svcRescanBtn: $('svcRescanBtn'),
        svcExportBtn: $('svcExportBtn'),
        altBackBtn: $('altBackBtn'),
        altRescanBtn: $('altRescanBtn'),
        altClearBtn: $('altClearBtn'),
        altExportBtn: $('altExportBtn'),
        pfBackBtn: $('pfBackBtn'),
        pfRescanBtn: $('pfRescanBtn'),
        pfExportBtn: $('pfExportBtn'),
        bamBackBtn: $('bamBackBtn'),
        bamRescanBtn: $('bamRescanBtn'),
        bamExportBtn: $('bamExportBtn'),
        siBackBtn: $('siBackBtn'),
        pfModal: $('pf-modal'),
        pfModalTabs: $('pfModalTabs'),
        pfModalBody: $('pfModalBody'),
        pfModalSub: $('pfModalSub'),
        pfModalIcon: $('pfModalIcon'),
        pfModalCloseBtn: $('pfModalCloseBtn'),
        toolCards: $$('.tool-card'),
        mouseGlow: $('mouseGlow')
    };

    const closeModal = () => {
        ui.modal.classList.remove('active');
        ui.modalActions.innerHTML = '';
        ui.modalIcon.innerHTML = '';
        ui.modalIcon.className = 'modal-icon';
        SoundFX.modalClose();
    };

    function showModal(title, message, type = 'info', actions = []) {
        closeModal();
        if (type === 'success') SoundFX.success();
        else if (type === 'error') SoundFX.error();
        else SoundFX.modalOpen();

        ui.modalTitle.textContent = title;
        ui.modalMessage.innerHTML = message;
        ui.modalIcon.className = `modal-icon ${type}`;
        ui.modalIcon.innerHTML = TYPE_ICONS[type] || APP_ICON;

        actions.forEach((a) => {
            const { label, variant, onClick } = a || {};
            if (!label) return;
            const btn = document.createElement('button');
            btn.className = `modal-action-btn ${variant || ''}`;
            btn.textContent = label;
            btn.addEventListener('mouseenter', () => SoundFX.hover());
            btn.addEventListener('click', (e) => {
                SoundFX.click();
                addRipple(btn, e.clientX, e.clientY);
                onClick?.();
                closeModal();
            });
            ui.modalActions.appendChild(btn);
        });

        ui.modal.classList.add('active');
    }

    if (ui.mouseGlow && !document.documentElement.classList.contains('perf-low')) {
        let glowRaf = 0;
        let glowX = 0, glowY = 0;
        const paintGlow = () => {
            glowRaf = 0;
            ui.mouseGlow.style.transform = 'translate(' + (glowX - 260) + 'px,' + (glowY - 260) + 'px)';
            ui.mouseGlow.style.opacity = '1';
        };
        document.addEventListener('mousemove', (e) => {
            glowX = e.clientX;
            glowY = e.clientY;
            if (!glowRaf) glowRaf = requestAnimationFrame(paintGlow);
        }, { passive: true });
        document.addEventListener('mouseleave', () => {
            ui.mouseGlow.style.opacity = '0';
        }, { passive: true });
    }

    if (!document.documentElement.classList.contains('perf-very-low')) {
        let cardRaf = 0;
        let cardX = 0, cardY = 0;
        document.addEventListener('mousemove', e => {
            cardX = e.clientX;
            cardY = e.clientY;
            if (cardRaf) return;
            cardRaf = requestAnimationFrame(() => {
                cardRaf = 0;
                const card = document.elementFromPoint(cardX, cardY)?.closest?.('.tool-card');
                if (!card) return;
                const r = card.getBoundingClientRect();
                if (r.width > 0 && r.height > 0) {
                    card.style.setProperty('--mx', ((cardX - r.left) / r.width * 100) + '%');
                    card.style.setProperty('--my', ((cardY - r.top) / r.height * 100) + '%');
                }
            });
        }, { passive: true });
    }

    const SVC_ICONS = {
        events: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/><path d="M8 13h8M8 17h5"/></svg>'
    };

    let svcResult = null;
    let svcBusy = false;
    const setSvcBusy = (busy) => {
        svcBusy = busy;
        if (ui.svcRescanBtn) {
            ui.svcRescanBtn.disabled = busy;
            ui.svcRescanBtn.hidden = busy;
        }
        if (ui.svcExportBtn) {
            const ready = !busy && !!svcResult;
            ui.svcExportBtn.disabled = !ready;
            ui.svcExportBtn.hidden = !ready;
        }
    };


    let svcScanAt = '';
    const svcFilters = { q: '', stopped: false };

    const SC_ICONS = {
        gear: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="3"/><path d="M12 2v3M12 19v3M2 12h3M19 12h3M4.9 4.9l2.1 2.1M17 17l2.1 2.1M19.1 4.9L17 7M7 17l-2.1 2.1"/></svg>',
        drive: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" aria-hidden="true"><rect x="3" y="4" width="18" height="7" rx="2"/><rect x="3" y="13" width="18" height="7" rx="2"/><path d="M7 7.5h.01M7 16.5h.01"/></svg>',
        clock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
        doc: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/></svg>',
        bin: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18"/><path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/></svg>',
        shield: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 2l8 3v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V5z"/><path d="M8.5 12l2.5 2.5 4.5-5"/></svg>',
        alert: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3L22 20H2L12 3z"/><path d="M12 9v5"/><path d="M12 17h.01"/></svg>',
        search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/></svg>'
    };

    const scRow = (tone, key, val) => `
        <div class="sc-row ${tone ? `t-${tone}` : ''}">
            <span class="sc-dot ${tone || ''}"></span>
            <span class="sc-key">${escapeHtml(key)}</span>
            <span class="sc-val ${tone || ''}">${escapeHtml(val)}</span>
        </div>`;

    const scCard = (icon, title, meta, body, extra = '') => `
        <section class="sc-card ${extra}">
            <div class="sc-card-head">
                <span class="sc-card-ic">${icon}</span>
                <h3>${escapeHtml(title)}</h3>
                ${meta ? `<span class="sc-meta">${escapeHtml(meta)}</span>` : ''}
            </div>
            <div class="sc-card-body">${body}</div>
        </section>`;

    const revealSc = (container) => {
        revealIn(container, '.sc-verdict, .sc-tiles, .sc-card, .sc-limit', 'on', 65);
    };

    const renderBootSection = (boot) => {
        const rows = [
            scRow('', 'Last boot', boot.lastBoot || 'Unavailable'),
            scRow('', 'Uptime', boot.uptime || '—')
        ];
        if (boot.tickMismatch) {
            rows.push(scRow('warn', 'Tick-count estimate', `${boot.tickBootEstimate || '—'} · ${boot.tickNote || 'Differs from registry'}`));
        }
        return scCard(SC_ICONS.clock, 'Boot integrity', boot.tickMismatch ? 'Mismatch' : 'Consistent', rows.join(''));
    };

    const renderDriveSection = (drives) => {
        const list = Array.isArray(drives) ? drives : [];
        if (!list.length) {
            return scCard(SC_ICONS.drive, 'Drives', '', '<p class="sc-empty">No ready volumes found.</p>');
        }
        const body = `<div class="sc-drives">${list.map(d => `
            <article class="sc-drive">
                <div class="sc-drive-top">
                    <span class="sc-drive-letter">${escapeHtml(d.letter)}</span>
                    <span class="sc-drive-media">${escapeHtml(d.media || 'Unknown')}</span>
                </div>
                <div class="sc-drive-fs">${escapeHtml(d.fileSystem || 'Unknown')}${d.label ? ` · ${escapeHtml(d.label)}` : ''}</div>
                ${d.size ? `<div class="sc-drive-size">${escapeHtml(d.size)}</div>` : ''}
            </article>`).join('')}</div>`;
        return scCard(SC_ICONS.drive, 'Drives', `${list.length} volume${list.length === 1 ? '' : 's'}`, body);
    };

    const svcLevelOf = (s) => s.status === 'Running' ? 'ok' : (s.severity || 'warn');

    const paintSvcList = () => {
        const host = $('scSvcList');
        if (!host || !svcResult) return;
        const list = Array.isArray(svcResult.services) ? svcResult.services : [];
        const q = (svcFilters.q || '').trim().toLowerCase();
        const rows = list.filter(s => {
            if (svcFilters.stopped && s.status === 'Running') return false;
            if (q && !`${s.name || ''} ${s.display || ''}`.toLowerCase().includes(q)) return false;
            return true;
        });
        host.innerHTML = rows.length ? rows.map(s => {
            const lvl = svcLevelOf(s);
            const bits = [];
            if (s.startType) bits.push(s.startType);
            if (s.startedAt) bits.push(`since ${s.startedAt}`);
            if (s.pid) bits.push(`PID ${s.pid}${s.sharedHost ? ' · shared host' : ''}`);
            return `
            <div class="sc-svc lvl-${lvl}">
                <span class="sc-svc-ic lvl-${lvl}">${SC_ICONS.gear}</span>
                <span class="sc-svc-main">
                    <b>${escapeHtml(s.name)}${s.critical ? ' <span class="sc-crit">Critical</span>' : ''}</b>
                    <span>${escapeHtml(s.display)}</span>
                </span>
                ${bits.length ? `<span class="sc-svc-meta">${escapeHtml(bits.join(' · '))}</span>` : ''}
                <span class="pf-flag ${lvl === 'ok' ? 'ok' : lvl === 'bad' ? 'bad' : 'warn'}">${escapeHtml(s.status || 'Unknown')}</span>
            </div>`;
        }).join('') : '<p class="sc-empty">No services match the current search and filters.</p>';
        const meta = $('scSvcMeta');
        if (meta) {
            const running = list.filter(s => s.status === 'Running').length;
            meta.textContent = `${rows.length} shown · ${running}/${list.length} running`;
        }
    };

    const renderServiceRows = (services) => {
        const list = Array.isArray(services) ? services : [];
        if (!list.length) {
            return scCard(SC_ICONS.gear, 'Forensic services', '', '<p class="sc-empty">No services returned.</p>');
        }
        const running = list.filter(s => s.status === 'Running').length;
        const critStopped = list.filter(s => s.status !== 'Running' && s.critical).length;
        const toolbar = `
            <div class="sc-toolbar">
                <div class="pf-search-wrap">
                    ${SC_ICONS.search}
                    <input type="search" class="pf-search" id="scSvcSearch" placeholder="Search services…" value="${escapeHtml(svcFilters.q)}" spellcheck="false" autocomplete="off">
                </div>
                <label class="pf-check ${svcFilters.stopped ? 'on' : ''}"><input type="checkbox" id="scSvcStopped" ${svcFilters.stopped ? 'checked' : ''}>Stopped only</label>
            </div>
            <div id="scSvcList"></div>`;
        return scCard(SC_ICONS.gear, 'Forensic services',
            `${running}/${list.length} running${critStopped ? ` · ${critStopped} critical stopped` : ''}`, toolbar, critStopped ? 'has-bad' : '');
    };

    const renderEventSection = (events) => {
        if (!events || events.skipped) {
            return scCard(SC_ICONS.doc, 'Event logs & USN', 'Locked',
                `<div class="sc-lock">${ICON.lock}<p>Skipped without Administrator. Restart elevated for log clears, USN integrity, clock changes and shutdowns.</p></div>`);
        }

        const rows = [];
        (events.clears || []).forEach(c => {
            rows.push({ tone: c.severity || (c.detected ? 'bad' : 'ok'), key: c.key, val: c.detected ? `Cleared at ${c.when}` : 'Not detected' });
        });
        (events.usn || []).forEach(u => {
            rows.push({
                tone: u.severity,
                key: u.volume ? `USN Journal ${u.volume}` : 'USN Journal',
                val: u.when ? `${u.state} at ${u.when}` : (u.state || 'Unknown')
            });
        });
        rows.push({ tone: '', key: 'Last shutdown', val: events.lastShutdown || 'Not detected' });
        rows.push({
            tone: events.unexpectedShutdown ? 'bad' : 'ok', key: 'Unexpected shutdown this session',
            val: events.unexpectedShutdown ? `Detected at ${events.unexpectedShutdownAt}` : 'Not detected'
        });
        rows.push({
            tone: events.clockChanged ? 'bad' : 'ok', key: 'System clock change this session',
            val: events.clockChanged ? `Detected at ${events.clockChangedAt}` : 'Not detected'
        });
        if (events.eventLogStartAt) {
            rows.push({
                tone: events.eventLogRestarted ? 'bad' : 'ok', key: 'Event Log service start',
                val: `${events.eventLogStartAt}${events.eventLogStartNote ? ` · ${events.eventLogStartNote}` : ''}`
            });
        } else {
            rows.push({ tone: 'warn', key: 'Event Log service start', val: 'Not detected' });
        }
        rows.push({ tone: '', key: 'Last device change this session', val: events.lastDeviceChange || 'Not detected' });

        const isAttention = (r) => r.tone === 'bad' || r.tone === 'warn' || /detected at/i.test(r.val);
        const attn = rows.filter(isAttention);
        const rest = rows.filter(r => !isAttention(r));
        const hits = attn.filter(r => r.tone === 'bad' || /detected at/i.test(r.val)).length;

        const body = (attn.length
            ? `<div class="sc-attn"><div class="sc-attn-title">${hits ? `${hits} finding${hits === 1 ? '' : 's'}` : 'Worth a look'}</div>${attn.map(r => scRow(r.tone, r.key, r.val)).join('')}</div>`
            : '')
            + rest.map(r => scRow(r.tone, r.key, r.val)).join('');
        return scCard(SC_ICONS.doc, 'Event logs & USN', hits ? `${hits} finding${hits === 1 ? '' : 's'}` : 'Clean', body, hits ? 'has-bad' : '');
    };

    const renderRecycleSection = (recycle) => {
        if (!recycle || recycle.skipped) {
            return scCard(SC_ICONS.bin, 'Recycle Bin', 'Locked',
                `<div class="sc-lock">${ICON.lock}<p>Skipped without Administrator.</p></div>`);
        }
        const tiles = `
            <div class="sc-minis">
                <div class="sc-mini"><span>${recycle.volumes ?? 0}</span>volumes</div>
                <div class="sc-mini"><span>${recycle.items ?? 0}</span>items</div>
                <div class="sc-mini"><span>${escapeHtml(recycle.totalSizeLabel || '0 B')}</span>size</div>
                <div class="sc-mini ${(recycle.deletedThisSession ?? 0) > 0 ? 'flag' : ''}"><span>${recycle.deletedThisSession ?? 0}</span>this session</div>
            </div>`;
        const rows = [];
        if (recycle.newestAt) {
            rows.push(scRow('', 'Most recently deleted', `${recycle.newestAt}${recycle.newestPath ? ` · ${recycle.newestPath}` : ''}`));
        }
        if (recycle.oldestAt) {
            rows.push(scRow('', 'Oldest deleted item', `${recycle.oldestAt}${recycle.oldestPath ? ` · ${recycle.oldestPath}` : ''}`));
        }
        if (recycle.inaccessible) {
            rows.push(scRow('warn', 'Inaccessible items', String(recycle.inaccessible)));
        }
        if (!recycle.items && !recycle.inaccessible) {
            rows.push(scRow('ok', 'Status', 'Recycle Bin is empty'));
        }
        const meta = recycle.items ? `${recycle.items} item${recycle.items === 1 ? '' : 's'}` : 'Empty';
        return scCard(SC_ICONS.bin, 'Recycle Bin', meta, tiles + rows.join(''));
    };

    const renderServiceCheck = (container, data) => {
        const findings = Number(data.findingCount) || 0;
        const services = Array.isArray(data.services) ? data.services : [];
        const running = services.filter(s => s.status === 'Running').length;
        const critStopped = services.filter(s => s.status !== 'Running' && s.critical).length;
        const boot = data.boot || {};
        const lvl = !data.admin ? 'warn' : (findings ? 'bad' : 'ok');

        const verdictTitle = !data.admin ? 'Limited scan'
            : findings ? `${findings} finding${findings === 1 ? '' : 's'} need review` : 'System looks clean';
        const verdictSub = !data.admin
            ? 'Event logs, USN journal and Recycle Bin need Administrator — results below are partial.'
            : findings
            ? `${running}/${services.length || 0} services running${critStopped ? ` · ${critStopped} critical stopped` : ''} · boot ${boot.tickMismatch ? 'mismatch' : 'consistent'}`
            : `All ${services.length || 0} watched services running · boot and logs clean`;

        const limit = data.admin ? '' : `
            <div class="sc-limit">
                ${ICON.lock}
                <div class="sc-limit-copy">
                    <strong>Limited scan</strong>
                    <p>Event logs, USN journal and Recycle Bin need Administrator.</p>
                </div>
                <button type="button" class="pf-admin-btn" id="svcRelaunchAdmin">Restart as Administrator</button>
            </div>`;

        const verdict = `
            <div class="sc-verdict lvl-${lvl}">
                <span class="sc-verdict-ic">${lvl === 'ok' ? SC_ICONS.shield : SC_ICONS.alert}</span>
                <div class="sc-verdict-copy">
                    <strong>${escapeHtml(verdictTitle)}</strong>
                    <p>${escapeHtml(verdictSub)}</p>
                </div>
                <div class="sc-verdict-count">
                    <span class="pf-num" data-n="${!data.admin && !findings ? services.length - running : findings}">0</span>
                    <small>${!data.admin && !findings ? 'stopped' : 'findings'}</small>
                </div>
            </div>`;

        const tile = (icon, n, label, hot, num, pos) => `
            <div class="sc-tile ${hot}" style="--d:${pos * 60}ms">
                <span class="sc-tile-ic">${icon}</span>
                <span class="sc-tile-copy">
                    ${num ? `<span class="pf-num" data-n="${n}">0</span>` : `<span class="sc-tile-str">${escapeHtml(n)}</span>`}
                    <span class="sc-tile-label">${escapeHtml(label)}</span>
                </span>
            </div>`;
        const tiles = `
            <div class="sc-tiles">
                ${tile(lvl === 'ok' ? SC_ICONS.shield : SC_ICONS.alert, findings, 'Findings', findings ? 'hot' : '', true, 0)}
                ${tile(SC_ICONS.gear, running, `Services up / ${services.length || 0}`, critStopped ? 'warm' : '', true, 1)}
                ${tile(SC_ICONS.clock, boot.uptime || '—', 'Uptime', '', false, 2)}
                ${tile(SC_ICONS.shield, data.admin ? 'Admin' : 'User', 'Access', data.admin ? '' : 'warm', false, 3)}
            </div>`;

        container.innerHTML = limit + verdict + tiles
            + renderBootSection(boot)
            + renderDriveSection(data.drives)
            + renderServiceRows(services)
            + renderEventSection(data.events)
            + renderRecycleSection(data.recycle);

        const search = container.querySelector('#scSvcSearch');
        if (search) {
            const debouncedSvcPaint = debounce(() => paintSvcList(), 150);
            search.addEventListener('input', () => {
                svcFilters.q = search.value;
                debouncedSvcPaint();
            });
        }
        const chk = container.querySelector('#scSvcStopped');
        if (chk) {
            chk.addEventListener('change', () => {
                svcFilters.stopped = chk.checked;
                chk.closest('.pf-check')?.classList.toggle('on', chk.checked);
                paintSvcList();
            });
        }
        paintSvcList();
        revealSc(container);
        countUp(container);

        const scanLine = $('svcScanLine');
        if (scanLine) {
            scanLine.hidden = false;
            scanLine.textContent = `Last scan ${svcScanAt} · ${services.length} services · ${(data.drives || []).length} volumes`;
        }

        const relaunchBtn = container.querySelector('#svcRelaunchAdmin');
        bindOnce(relaunchBtn, async () => {
            try { await window.pywebview?.api?.relaunch_as_admin?.(); } catch (e) { }
        }, 'tool');
    };

    const exportSvcResult = () => {
        if (!svcResult) return;
        exportJson({ scanDate: new Date().toISOString(), ...svcResult }, `ServiceCheck_${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
    };

    const openServiceChecker = async (force = false) => {
        showScreen('service-screen');
        if (svcBusy) return;

        const container = $('service-results');

        if (!force && svcResult) {
            renderServiceCheck(container, svcResult);
            return;
        }
        setSvcBusy(true);
        container.innerHTML = loadingState(
            'Scanning forensic services…',
            'Boot time, drives, SCM status, event logs and Recycle Bin');

        try {
            const result = await window.pywebview.api.service_checker_run();
            if (!result) throw new Error('No output returned.');
            if (result.error) {
                container.innerHTML = `
                    <div class="scanning-state">
                        <p>${escapeHtml(result.error)}</p>
                    </div>`;
                return;
            }
            svcResult = result;
            svcFilters.q = '';
            svcFilters.stopped = false;
            svcScanAt = new Date().toLocaleString();
            renderServiceCheck(container, result);
        } catch (err) {
            container.innerHTML = isCancellation(err)
                ? '<div class="scanning-state"><p>Scan stopped.</p></div>'
                : `<div class="scanning-state"><p>Error running scan: ${escapeHtml(err.message || err)}</p></div>`;
        } finally {
            setSvcBusy(false);
        }
    };

    let altResult = null;
    let altBusy = false;

    const ALT_ICONS = {
        mc: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><rect x="3" y="3" width="18" height="18" rx="2"/><path d="M3 9h18M9 21V9"/></svg>',
        dc: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round"><path d="M8 9a4 4 0 0 1 8 0c0 4-4 7-4 7s-4-3-4-7z"/><circle cx="12" cy="9" r="1.2"/><path d="M5 19h14"/></svg>'
    };

    const altDiscordAccounts = (r) => {
        if (Array.isArray(r?.discordAccounts) && r.discordAccounts.length)
            return r.discordAccounts.filter(a => a && a.id);
        return (r?.discordIds || []).map(id => ({ id, username: '' }));
    };

    const setAltBusy = (busy) => {
        altBusy = busy;
        if (ui.altRescanBtn) {
            ui.altRescanBtn.disabled = busy;
            ui.altRescanBtn.hidden = busy;
        }
        if (ui.altClearBtn) {
            ui.altClearBtn.disabled = busy;
            ui.altClearBtn.hidden = busy;
        }
        if (ui.altExportBtn) {
            const ready = !busy && !!altResult;
            ui.altExportBtn.disabled = !ready;
            ui.altExportBtn.hidden = !ready;
        }
    };

    const renderAltList = (kind, rows, emptyText) => {
        if (!rows.length) return `<p class="svc-empty">${escapeHtml(emptyText)}</p>`;
        const filter = rows.length >= 8
            ? `<input type="search" class="alt-filter" data-alt-filter="${kind}" placeholder="Filter…" spellcheck="false" autocomplete="off">`
            : '';
        const items = rows.map(row => {
            const secondary = row.secondary ? `<span class="alt-row-sub">${escapeHtml(row.secondary)}</span>` : '';
            return `<div class="alt-row">
                <span class="svc-dot ok"></span>
                <div class="alt-row-copy">
                    <span class="alt-row-val">${escapeHtml(row.primary)}</span>
                    ${secondary}
                </div>
            </div>`;
        }).join('');
        return `${filter}<div class="alt-list">${items}</div>`;
    };

    const renderAltCheck = (container, data) => {
        const mc = Array.isArray(data.minecraftAccounts) ? data.minecraftAccounts : [];
        const dc = altDiscordAccounts(data);
        const launchers = Number(data.launcherFilesScanned) || 0;
        const logs = Number(data.logFilesScanned) || 0;
        const discordDirs = Number(data.discordDirectoriesScanned) || 0;
        const browserDirs = Number(data.browserDirectoriesScanned) || 0;
        const cached = (Number(data.cachedMcCount) || 0) + (Number(data.cachedDcCount) || 0);
        const sources = launchers + logs + discordDirs + browserDirs;

        const mcBody = renderAltList('mc', mc.map(name => ({ primary: name })), 'No Minecraft accounts found.');
        const dcBody = renderAltList('dc', dc.map(a => ({
            primary: a.username || a.id,
            secondary: a.username ? a.id : ''
        })), 'No Discord accounts found.');

        const mcCopy = `<button type="button" class="pf-btn alt-copy-btn" data-alt-copy="mc">Copy</button>`;
        const dcCopy = `<button type="button" class="pf-btn alt-copy-btn" data-alt-copy="dc">Copy</button>`;
        const allCopy = `<button type="button" class="pf-btn alt-copy-btn" data-alt-copy="all">Copy all</button>`;

        container.innerHTML = `
            ${data.error ? `<div class="svc-banner revealed">
                ${ICON.warning}
                <div class="svc-banner-copy">
                    <strong>Partial scan</strong>
                    <p>${escapeHtml(data.error)} Showing everything found before the scan stopped.</p>
                </div>
            </div>` : ''}
            <div class="svc-summary">
                <div class="svc-stat ${mc.length ? 'ok' : ''}">
                    <span class="svc-stat-value">${mc.length}</span>
                    <span class="svc-stat-label">Minecraft</span>
                </div>
                <div class="svc-stat ${dc.length ? 'ok' : ''}">
                    <span class="svc-stat-value">${dc.length}</span>
                    <span class="svc-stat-label">Discord</span>
                </div>
                <div class="svc-stat">
                    <span class="svc-stat-value">${sources}</span>
                    <span class="svc-stat-label">Sources</span>
                </div>
                <div class="svc-stat">
                    <span class="svc-stat-value">${cached}</span>
                    <span class="svc-stat-label">From cache</span>
                </div>
            </div>
            <section class="service-section">
                <div class="service-section-header">
                    <span class="service-section-icon">${ALT_ICONS.mc}</span>
                    <h3>Minecraft accounts</h3>
                    <span class="svc-section-meta">${mc.length} found</span>
                    ${mcCopy}
                </div>
                <div class="service-section-body">${mcBody}</div>
            </section>
            <section class="service-section">
                <div class="service-section-header">
                    <span class="service-section-icon">${ALT_ICONS.dc}</span>
                    <h3>Discord accounts</h3>
                    <span class="svc-section-meta">${dc.length} found</span>
                    ${dcCopy}${allCopy}
                </div>
                <div class="service-section-body">${dcBody}</div>
            </section>`;

        revealSections(container);
    };

    const flashCopied = (btn, message) => {
        if (btn) {
            btn.classList.remove('is-copied');
            void btn.offsetWidth;
            btn.classList.add('is-copied');
            clearTimeout(btn._copiedTimer);
            btn._copiedTimer = setTimeout(() => btn.classList.remove('is-copied'), 1500);
        }
        if (message) {
            const meta = btn?.closest('.service-section')?.querySelector('.svc-section-meta');
            if (meta) {
                const prev = meta.dataset.label || meta.textContent;
                meta.dataset.label = prev;
                meta.textContent = message;
                clearTimeout(meta._flashTimer);
                meta._flashTimer = setTimeout(() => {
                    meta.textContent = meta.dataset.label || prev;
                }, 1500);
            }
        }
    };

    const copyAltText = (lines, btn, label) => {
        if (!lines.length) {
            flashCopied(btn, 'Nothing to copy');
            return;
        }
        copyText(lines.join('\n'), (ok) => flashCopied(btn, ok ? `Copied ${lines.length} ${label}` : 'Copy failed'));
    };

    const fallbackCopy = (text, done) => {
        try {
            const ta = document.createElement('textarea');
            ta.value = text;
            ta.setAttribute('readonly', '');
            ta.style.position = 'fixed';
            ta.style.opacity = '0';
            document.body.appendChild(ta);
            ta.select();
            const ok = document.execCommand('copy');
            ta.remove();
            done(!!ok);
        } catch (e) {
            done(false);
        }
    };

    const exportAltResult = () => {
        if (!altResult) return;
        const dc = altDiscordAccounts(altResult);
        exportJson({
            scanDate: new Date().toISOString(),
            minecraftAccounts: altResult.minecraftAccounts || [],
            discordIds: dc.map(a => a.id),
            discordAccounts: dc,
            launcherFilesScanned: altResult.launcherFilesScanned || 0,
            logFilesScanned: altResult.logFilesScanned || 0,
            discordDirectoriesScanned: altResult.discordDirectoriesScanned || 0,
            browserDirectoriesScanned: altResult.browserDirectoriesScanned || 0
        }, `AltDetection_${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
    };

    const openAltDetector = async (force = false) => {
        showScreen('alt-screen');
        if (altBusy) return;

        const container = $('alt-results');
        if (!force && altResult) {
            renderAltCheck(container, altResult);
            return;
        }
        setAltBusy(true);
        altResult = null;
        container.innerHTML = loadingState(
            'Scanning for alt accounts…',
            'Launchers, Discord client data, browser storage and Minecraft logs on every fixed drive');

        try {
            const result = await window.pywebview.api.alt_detector_run();
            if (!result) throw new Error('No output returned.');
            if (result.error && !(result.minecraftAccounts?.length || result.discordIds?.length || result.discordAccounts?.length)) {
                container.innerHTML = `
                    <div class="scanning-state">
                        <p>${escapeHtml(result.error)}</p>
                    </div>`;
                return;
            }
            altResult = result;
            renderAltCheck(container, result);
        } catch (err) {
            container.innerHTML = isCancellation(err)
                ? '<div class="scanning-state"><p>Scan stopped.</p></div>'
                : `<div class="scanning-state"><p>Error running scan: ${escapeHtml(err.message || err)}</p></div>`;
        } finally {
            setAltBusy(false);
        }
    };

    const bindAltToolbar = () => {
        bindOnce(ui.altRescanBtn, () => openAltDetector(true), 'tool');
        bindOnce(ui.altClearBtn, async () => {
            if (altBusy) return;
            altResult = null;
            setAltBusy(true);
            const container = $('alt-results');
            container.innerHTML = loadingState('Clearing cached results…');
            try {
                await window.pywebview.api.alt_detector_clear();
                altResult = null;
                container.innerHTML = `
                    <div class="scanning-state">
                        <p>Cache cleared. Run a rescan to search this machine again.</p>
                    </div>`;
            } catch (err) {
                container.innerHTML = `
                    <div class="scanning-state">
                        <p>Failed to clear: ${escapeHtml(err.message || err)}</p>
                    </div>`;
            } finally {
                setAltBusy(false);
            }
        }, 'tool');
        bindOnce(ui.altExportBtn, () => {
            if (!altResult) return;
            exportAltResult();
        }, 'tool');

        const container = $('alt-results');
        if (container && container.dataset.bound !== '1') {
            container.dataset.bound = '1';
            container.addEventListener('click', (e) => {
                const btn = e.target.closest('[data-alt-copy]');
                if (!btn || !container.contains(btn) || !altResult) return;
                SoundFX.click();
                addRipple(btn, e.clientX, e.clientY);
                const dc = altDiscordAccounts(altResult);
                const kind = btn.getAttribute('data-alt-copy');
                if (kind === 'mc') {
                    copyAltText(altResult.minecraftAccounts || [], btn, 'usernames');
                } else if (kind === 'dc') {
                    copyAltText(dc.map(a => a.username ? `${a.username} (${a.id})` : a.id), btn, 'IDs');
                } else if (kind === 'all') {
                    const lines = [
                        '=== MINECRAFT ACCOUNTS ===',
                        ...(altResult.minecraftAccounts || []),
                        '',
                        '=== DISCORD ACCOUNTS ===',
                        ...dc.map(a => a.username ? `${a.username}  ${a.id}` : a.id)
                    ];
                    copyAltText(lines, btn, 'items');
                }
            });
            container.addEventListener('input', (e) => {
                const input = e.target.closest('[data-alt-filter]');
                if (!input || !container.contains(input)) return;
                const q = input.value.trim().toLowerCase();
                const list = input.parentElement?.querySelector('.alt-list');
                if (!list) return;
                list.querySelectorAll('.alt-row').forEach(row => {
                    const hay = (row.textContent || '').toLowerCase();
                    row.hidden = q !== '' && !hay.includes(q);
                });
            });
            container.addEventListener('mouseenter', (e) => {
                const btn = e.target.closest('.alt-copy-btn, .alt-filter');
                if (btn) SoundFX.hover();
            }, true);
        }
    };

    let pfResult = null;
    let pfBusy = false;
    let pfSelected = -1;
    let pfTab = 'related';
    let pfSigToken = 0;
    let pfSort = { key: 'time', dir: -1 };
    const pfFilters = { unsigned: false, flagged: false, instance: false, q: '' };

    const UI_ICONS = {
        shieldOk: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 2l8 3v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V5z"/><path d="M8.5 12l2.5 2.5 4.5-5"/></svg>',
        alert: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3L22 20H2L12 3z"/><path d="M12 9v5"/><path d="M12 17h.01"/></svg>',
        doc: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/></svg>',
        layers: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 2L2 7l10 5 10-5-10-5z"/><path d="M2 17l10 5 10-5"/><path d="M2 12l10 5 10-5"/></svg>',
        zap: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polygon points="13 2 3 14 12 14 11 22 21 10 12 10 13 2"/></svg>',
        clock: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
        search: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="11" cy="11" r="7"/><path d="M21 21l-4.3-4.3"/></svg>'
    };

    const pfEntryLevel = (e) => pfRuleFlagged(e) ? 'bad' : (!e.isSigned ? 'warn' : 'ok');

    const relTime = (unix) => {
        if (!unix) return '—';
        const s = Math.floor(Date.now() / 1000) - unix;
        if (s < 0) return '—';
        if (s < 60) return 'just now';
        if (s < 3600) {
            const m = Math.floor(s / 60);
            return `${m}m ago`;
        }
        if (s < 86400) {
            const h = Math.floor(s / 3600);
            return `${h}h ago`;
        }
        const d = Math.floor(s / 86400);
        if (d === 1) return 'yesterday';
        if (d < 30) return `${d}d ago`;
        const mo = Math.floor(d / 30);
        if (mo === 1) return '1mo ago';
        if (mo < 12) return `${mo}mo ago`;
        return `${Math.floor(mo / 12)}y ago`;
    };

    const weekdayOf = (unix) => {
        if (!unix) return '';
        return ['Sun', 'Mon', 'Tue', 'Wed', 'Thu', 'Fri', 'Sat'][new Date(unix * 1000).getDay()];
    };

    const toHex = (n) => '0x' + Number(n || 0).toString(16).toUpperCase();

    const pfHitTitle = (h) => {
        const bits = [];
        if (h.sample) bits.push(`"${h.sample}"`);
        bits.push(`@ ${toHex(h.offset)}`);
        if (h.encoding) bits.push(`(${h.encoding})`);
        if (h.count > 1) bits.push(`×${h.count}`);
        return bits.join(' ');
    };

    const countUp = (container) => {
        const reduced = document.documentElement.classList.contains('perf-reduced-motion');
        container.querySelectorAll('.pf-num').forEach(el => {
            const target = Number(el.dataset.n) || 0;
            if (reduced || target <= 0) {
                el.textContent = String(target);
                return;
            }
            const t0 = performance.now();
            const dur = 750;
            const step = (t) => {
                const p = Math.min(1, (t - t0) / dur);
                el.textContent = String(Math.round(target * (1 - Math.pow(1 - p, 3))));
                if (p < 1) requestAnimationFrame(step);
            };
            requestAnimationFrame(step);
        });
    };

    const setPfBusy = (busy) => {
        pfBusy = busy;
        if (ui.pfRescanBtn) {
            ui.pfRescanBtn.disabled = busy;
            ui.pfRescanBtn.hidden = busy;
        }
        if (ui.pfExportBtn) {
            const ready = !busy && !!pfResult;
            ui.pfExportBtn.disabled = !ready;
            ui.pfExportBtn.hidden = !ready;
        }
    };

    const pfRuleFlagged = (e) => (e.matchedRules || []).some(r => r && r !== 'none');

    const pfVisibleEntries = () => {
        const list = Array.isArray(pfResult?.entries) ? pfResult.entries : [];
        const q = (pfFilters.q || '').trim().toLowerCase();
        const out = [];
        list.forEach((e, i) => {
            if (pfFilters.unsigned && e.isSigned) return;
            if (pfFilters.flagged && !pfRuleFlagged(e)) return;
            if (pfFilters.instance && !e.isInInstance) return;
            if (q) {
                const hay = [
                    e.properPath || '', e.filename || '', e.readableTime || '',
                    ((e.matchedDetails || []).map(h => `${h.name || ''} ${h.id || ''} ${h.sample || ''}`).join(' '))
                ].join(' ').toLowerCase();
                if (!hay.includes(q)) return;
            }
            out.push({ e, i });
        });
        const dir = pfSort.dir;
        const byTime = (a, b) => (a.e.executedUnix || 0) - (b.e.executedUnix || 0);
        const byPath = (a, b) => String(a.e.properPath || a.e.filename || '').localeCompare(
            String(b.e.properPath || b.e.filename || ''));
        const bySigned = (a, b) => (a.e.isSigned === b.e.isSigned) ? 0 : (a.e.isSigned ? 1 : -1);
        const byPresent = (a, b) => (a.e.isPresent === b.e.isPresent) ? 0 : (a.e.isPresent ? 1 : -1);
        const byRules = (a, b) => String((a.e.matchedRules || []).join(',')).localeCompare(
            String((b.e.matchedRules || []).join(',')));
        const cmp = pfSort.key === 'path' ? byPath
            : pfSort.key === 'signed' ? bySigned
            : pfSort.key === 'present' ? byPresent
            : pfSort.key === 'rules' ? byRules : byTime;
        out.sort((a, b) => cmp(a, b) * dir);
        return out;
    };

    const paintPfTable = () => {
        const wrap = $('pfTableWrap');
        if (!wrap) return;
        const rendered = renderPfTable();
        wrap.innerHTML = rendered.html;
        const meta = wrap.closest('.service-section')?.querySelector('.svc-section-meta');
        if (meta) {
            meta.textContent = `${rendered.count} shown`;
        }
    };

    const debouncedPfPaint = debounce(() => paintPfTable(), 150);

    const renderPfTable = () => {
        const rows = pfVisibleEntries();
        if (!rows.length) {
            return { html: '<p class="svc-empty">No prefetch entries match the current search and filters.</p>', count: 0 };
        }
        const arrow = (key) => pfSort.key === key
            ? `<span class="pf-arrow">${pfSort.dir === 1 ? '▲' : '▼'}</span>` : '';
        const th = (key, label, hideMd) => `
            <button type="button" class="pf-sort ${pfSort.key === key ? 'sorted' : ''} ${hideMd ? 'pf-hide-md' : ''}" data-pf-sort="${key}">
                ${escapeHtml(label)} ${arrow(key)}
            </button>`;
        const body = rows.map(({ e, i }, pos) => {
            const lvl = pfEntryLevel(e);
            const hits = Array.isArray(e.matchedDetails) && e.matchedDetails.length
                ? e.matchedDetails
                : (e.matchedRules || []).filter(r => r && r !== 'none')
                    .map(r => ({ id: r, name: r, sample: '' }));
            const rulesHtml = hits.length
                ? `<div class="pf-rules-cell">${hits.map(h => `<span class="pf-rule" title="${escapeHtml(pfHitTitle(h))}">${escapeHtml(h.name || h.id)}${h.count > 1 ? ` <b>×${h.count}</b>` : ''}</span>`).join('')}</div>`
                : '<span class="pf-rule-none">—</span>';
            const subFile = escapeHtml(e.filename || '');
            const sub = e.isInInstance ? `${subFile} <span class="in-inst">· in instance</span>` : subFile;
            return `
            <div class="pf-row lvl-${lvl}" data-pf-row="${i}" role="button" tabindex="0" title="Open details" style="--d:${Math.min(pos, 60) * 16}ms">
                <span class="pf-main">
                    <span class="pf-fileicon lvl-${lvl}">${UI_ICONS.doc}</span>
                    <span class="pf-row-main">
                        <span class="pf-path" title="${escapeHtml(e.properPath || e.filename || '')}">${escapeHtml(e.properPath || e.filename || '—')}</span>
                        <span class="pf-sub">${sub}</span>
                    </span>
                </span>
                <span class="pf-timecol" title="${escapeHtml(e.readableTime || '')}">
                    <b>${escapeHtml(relTime(e.executedUnix))}</b>
                    <span>${escapeHtml((e.readableTime || '').slice(0, 16) || '—')}</span>
                </span>
                <span><span class="pf-flag ${e.isSigned ? 'ok' : 'bad'}">${escapeHtml(e.isSigned ? 'Signed' : 'Unsigned')}</span></span>
                <span class="pf-hide-md"><span class="pf-present ${e.isPresent ? '' : 'no'}"><span class="svc-dot ${e.isPresent ? 'ok' : 'warn'}"></span>${escapeHtml(e.isPresent ? 'Yes' : 'No')}</span></span>
                <span>${rulesHtml}</span>
            </div>`;
        }).join('');
        return {
            html: `
        <div class="pf-table" role="table" aria-label="Prefetch entries">
            <div class="pf-head" role="row">
                ${th('path', 'Binary', false)}
                ${th('time', 'Last exec', false)}
                ${th('signed', 'Signature', false)}
                ${th('present', 'Present', true)}
                ${th('rules', 'Generics', false)}
            </div>
            ${body}
        </div>`,
            count: rows.length
        };
    };

    const pfModalOpen = () => ui.pfModal?.classList.contains('active');

    const openPfModal = (idx) => {
        const list = Array.isArray(pfResult?.entries) ? pfResult.entries : [];
        if (!Number.isInteger(idx) || idx < 0 || idx >= list.length) return;
        pfModalKind = 'pf';
        pfSelected = idx;
        pfTab = 'related';
        paintPfModal();
        ui.pfModal?.classList.add('active');
        SoundFX.modalOpen();
        ui.pfModalBody?.scrollTo?.(0, 0);
    };

    const closePfModal = () => {
        if (!pfModalOpen()) return;
        ui.pfModal.classList.remove('active');
        SoundFX.modalClose();
    };

    const paintPfModal = () => {
        const list = Array.isArray(pfResult?.entries) ? pfResult.entries : [];
        if (pfSelected < 0 || pfSelected >= list.length) return;
        const e = list[pfSelected];
        const lvl = pfEntryLevel(e);

        const title = $('pfModalTitle');
        if (title) title.textContent = e.filename || 'Entry details';
        if (ui.pfModalIcon) {
            ui.pfModalIcon.className = `pf-modal-icon lvl-${lvl}`;
            ui.pfModalIcon.innerHTML = lvl === 'ok' ? UI_ICONS.shieldOk : UI_ICONS.alert;
        }
        if (ui.pfModalSub) {
            const verdict = lvl === 'bad' ? 'Flagged' : lvl === 'warn' ? 'Unsigned' : 'Signed';
            ui.pfModalSub.textContent = [e.properPath || '', e.readableTime || '', verdict]
                .filter(Boolean).join('  ·  ');
        }
        if (ui.pfModalTabs) {
            ui.pfModalTabs.innerHTML = [
                ['related', 'Related Files'],
                ['rules', 'Rules'],
                ['history', 'Execution History'],
                ['file', 'PF File Info']
            ].map(([key, label]) => `
                <button type="button" class="pf-tab ${pfTab === key ? 'active' : ''}" data-pf-tab="${key}" role="tab">${escapeHtml(label)}</button>
            `).join('');
        }
        if (ui.pfModalBody) {
            ui.pfModalBody.innerHTML = pfModalBodyHtml(e);
            ui.pfModalBody.scrollTop = 0;
        }
        if (pfTab === 'related') requestPfSigs();
    };

    const requestPfSigs = () => {
        const list = Array.isArray(pfResult?.entries) ? pfResult.entries : [];
        if (pfSelected < 0 || pfSelected >= list.length || !ui.pfModalBody) return;
        const files = (list[pfSelected].relatedFiles || [])
            .filter(f => f && f.present && f.path)
            .map(f => f.path);
        if (!files.length) return;
        const token = ++pfSigToken;
        const paths = files.slice(0, 200);
        const apply = (map) => {
            if (token !== pfSigToken || !pfModalOpen() || !ui.pfModalBody) return;
            ui.pfModalBody.querySelectorAll('[data-pf-sig]').forEach(el => {
                const key = (el.getAttribute('data-pf-sig') || '').toLowerCase();
                if (key && Object.prototype.hasOwnProperty.call(map, key)) {
                    const ok = map[key];
                    el.className = 'pf-sig ' + (ok ? 'ok' : 'bad');
                    el.textContent = ok ? 'Signed' : 'Unsigned';
                } else {
                    el.className = 'pf-sig muted';
                    el.textContent = '—';
                }
            });
        };
        try {
            window.pywebview.api.prefetch_related_signatures(paths).then(
                (res) => {
                    const map = {};
                    (res || []).forEach(r => {
                        if (r && r.path) map[String(r.path).toLowerCase()] = !!r.signed;
                    });
                    apply(map);
                },
                () => apply({}));
        } catch (err) {
            apply({});
        }
    };

    const pfModalBodyHtml = (e) => {
        let detail = '';
        if (pfTab === 'related') {
            const files = Array.isArray(e.relatedFiles) ? e.relatedFiles : [];
            detail = files.length
                ? `<div class="pf-related-bar">
                       <button type="button" class="pf-btn alt-copy-btn" data-pf-copy="related">Copy all</button>
                       <span class="pf-related-note">${files.length} file${files.length === 1 ? '' : 's'}${(e.relatedTotal ?? files.length) > files.length ? ` of ${e.relatedTotal}` : ''} · signatures load on demand${files.length >= 200 ? ' (first 200)' : ''}</span>
                   </div>
                   <input type="search" class="alt-filter" data-pf-filter placeholder="Filter…" spellcheck="false" autocomplete="off">
                   <div class="alt-list">${files.map(f => `
                    <div class="alt-row">
                        <span class="svc-dot ${f.present ? 'ok' : 'bad'}"></span>
                        <div class="alt-row-copy">
                            <span class="alt-row-val">${escapeHtml(f.path)}</span>
                            <span class="alt-row-sub">${escapeHtml(f.present ? 'Present' : 'Missing')}</span>
                        </div>
                        ${f.present ? `<span class="pf-sig" data-pf-sig="${escapeHtml(f.path)}">…</span>` : '<span class="pf-sig muted">—</span>'}
                    </div>`).join('')}</div>`
                : '<p class="svc-empty">No related files recorded.</p>';
        } else if (pfTab === 'rules') {
            const hits = Array.isArray(e.matchedDetails) ? e.matchedDetails : [];
            detail = hits.length
                ? hits.map(h => `
                    <div class="pf-rule-card">
                        <div class="pf-rule-head">
                            <span class="pf-rule-name">${escapeHtml(h.name || h.id)}</span>
                            <span class="pf-rule-id">${escapeHtml(h.id)}</span>
                            ${h.count > 1 ? `<span class="pf-rule-id">×${h.count}</span>` : ''}
                        </div>
                        <div class="pf-rule-meta">
                            <span class="pf-off">${escapeHtml(toHex(h.offset))}</span>
                            ${h.encoding ? `<span class="pf-enc">${escapeHtml(h.encoding)}</span>` : ''}
                        </div>
                        <code class="pf-rule-sample">${escapeHtml(h.sample || h.id)}</code>
                        ${h.context ? `<code class="pf-ctx">${escapeHtml(h.context)}</code>` : ''}
                    </div>`).join('')
                : '<p class="svc-empty">No rules matched this binary.</p>';
        } else if (pfTab === 'history') {
            const unixs = Array.isArray(e.lastRunsUnix) ? e.lastRunsUnix : [];
            const exacts = Array.isArray(e.lastRunsExact) ? e.lastRunsExact : [];
            const items = [];
            for (let s = 0; s < 8; s++) {
                const u = unixs[s] || 0;
                if (!u) continue;
                items.push({ slot: s, unix: u, text: exacts[s] || '' });
            }
            detail = items.length
                ? `${items.length === 1 ? '<p class="scan-msg" style="margin:0 0 8px 2px">Single recorded execution — this trace captured one run; older runs were never recorded here or the trace was recreated.</p>' : ''}
                   <div class="pf-hist-head">${items.length} of 8 slots${items.length > 1 ? ` · newest ${escapeHtml(relTime(items[0].unix))} · oldest ${escapeHtml(relTime(items[items.length - 1].unix))}` : ''}</div>
                   <div class="pf-timeline">${items.map((it, idx) => `
                    <div class="pf-tl-item ${idx === 0 ? 'latest' : ''}">
                        <span class="pf-tl-dot"></span>
                        <div class="pf-tl-copy">
                            <b>Run ${idx + 1}${idx === 0 ? '<span class="pf-tl-tag">latest</span>' : ''}<span class="pf-tl-rel">${escapeHtml(relTime(it.unix))}</span></b>
                            <span>${escapeHtml(it.text || '')}${it.text ? ` · ${escapeHtml(weekdayOf(it.unix))} · slot ${it.slot}` : ''}</span>
                        </div>
                    </div>`).join('')}</div>`
                : '<p class="svc-empty">No execution timestamps recorded.</p>';
        } else {
            const kb = e.pfSizeBytes ? (e.pfSizeBytes / 1024).toFixed(2) + ' KB' : '—';
            detail = `
                ${svcSignal('', 'PF name', e.filename || '—')}
                ${svcSignal('', 'File size', kb)}
                ${svcSignal('', 'Creation time', e.pfCreated || '—')}
                ${svcSignal('', 'Last access time', e.pfAccessed || '—')}
                ${svcSignal('', 'Last modified time', e.pfModified || '—')}`;
        }

        return detail;
    };

    const renderPrefetch = (container, data) => {
        const entries = Array.isArray(data.entries) ? data.entries : [];
        const unsigned = entries.filter(e => !e.isSigned).length;
        const flagged = entries.filter(pfRuleFlagged).length;
        const inInstance = entries.filter(e => e.isInInstance).length;
        const lvl = flagged ? 'bad' : (unsigned ? 'warn' : 'ok');

        const vTitle = lvl === 'bad' ? 'Threat traces detected'
            : lvl === 'warn' ? 'Review recommended' : 'System looks clean';
        let vSub = lvl === 'bad'
            ? `${flagged} ${flagged === 1 ? 'binary matches' : 'binaries match'} cheat-trace rules · ${unsigned} unsigned`
            : lvl === 'warn'
            ? `${unsigned} unsigned ${unsigned === 1 ? 'binary needs' : 'binaries need'} review · rule checks clean`
            : `${entries.length} traced ${entries.length === 1 ? 'binary' : 'binaries'} · all signed and accounted for`;
        const skipped = Math.max(0, (data.filesFound ?? entries.length) - entries.length);
        if (skipped > 0) vSub += ` · ${skipped} skipped`;
        const vCount = lvl === 'ok' ? entries.length : (data.findingCount ?? 0);

        const banner = data.admin ? '' : `
            <div class="svc-banner">
                ${ICON.lock}
                <div class="svc-banner-copy">
                    <strong>Limited scan</strong>
                    <p>Reading C:\\Windows\\Prefetch needs Administrator. Results may be incomplete.</p>
                </div>
                <button type="button" class="pf-admin-btn" id="pfRelaunchAdmin">Restart as Administrator</button>
            </div>`;

        const verdict = `
            <div class="pf-verdict lvl-${lvl}">
                <span class="pf-verdict-icon">${lvl === 'ok' ? UI_ICONS.shieldOk : UI_ICONS.alert}</span>
                <div class="pf-verdict-copy">
                    <strong>${escapeHtml(vTitle)}</strong>
                    <p>${escapeHtml(vSub)}</p>
                </div>
                <div class="pf-verdict-count">
                    <span class="pf-num" data-n="${vCount}">0</span>
                    <small>${lvl === 'ok' ? 'entries' : 'findings'}</small>
                </div>
            </div>`;

        const stat = (icon, n, label, hot, pos) => `
            <div class="pf-stat ${hot}" style="--d:${pos * 60}ms">
                <span class="pf-stat-ic">${icon}</span>
                <span class="pf-stat-copy">
                    <span class="pf-num" data-n="${n}">0</span>
                    <span class="pf-stat-label">${escapeHtml(label)}</span>
                </span>
            </div>`;
        const summary = `
            <div class="pf-stats">
                ${stat(UI_ICONS.layers, entries.length, 'Entries', '', 0)}
                ${stat(UI_ICONS.shieldOk, unsigned, 'Unsigned', unsigned ? 'warm' : '', 1)}
                ${stat(UI_ICONS.zap, flagged, 'Flagged', flagged ? 'hot' : '', 2)}
                ${stat(UI_ICONS.clock, inInstance, 'In instance', '', 3)}
            </div>`;

        const filters = `
            <div class="pf-toolbar">
                <div class="pf-search-wrap">
                    ${UI_ICONS.search}
                    <input type="search" class="pf-search" data-pf-search placeholder="Search binary, rule or time…" value="${escapeHtml(pfFilters.q)}" spellcheck="false" autocomplete="off">
                </div>
                <div class="pf-filters">
                    <label class="pf-check ${pfFilters.unsigned ? 'on' : ''}"><input type="checkbox" data-pf-check="unsigned" ${pfFilters.unsigned ? 'checked' : ''}>Unsigned only</label>
                    <label class="pf-check ${pfFilters.flagged ? 'on' : ''}"><input type="checkbox" data-pf-check="flagged" ${pfFilters.flagged ? 'checked' : ''}>Flagged only</label>
                    <label class="pf-check ${pfFilters.instance ? 'on' : ''}"><input type="checkbox" data-pf-check="instance" ${pfFilters.instance ? 'checked' : ''}>Only in instance</label>
                    <button type="button" class="pf-btn alt-copy-btn" data-pf-copy="flagged">Copy flagged</button>
                </div>
            </div>`;

        container.innerHTML = banner + verdict + summary
            + renderServiceSection(SVC_ICONS.events, 'Prefetch entries', filters + '<div id="pfTableWrap"></div>', `${entries.length} files`);

        revealSections(container);
        paintPfTable();
        countUp(container);

        const relaunchBtn = container.querySelector('#pfRelaunchAdmin');
        bindOnce(relaunchBtn, async () => {
            try { await window.pywebview?.api?.relaunch_as_admin?.(); } catch (e) { }
        }, 'tool');
    };

    const openPrefetch = async (force = false) => {
        showScreen('prefetch-screen');
        if (pfBusy) return;

        const container = $('pf-results');
        if (!force && pfResult) {
            renderPrefetch(container, pfResult);
            bindPfContainer(container);
            return;
        }
        setPfBusy(true);
        pfResult = null;
        pfSelected = -1;
        container.innerHTML = loadingState(
            'Parsing Prefetch traces…',
            'Execution history, binary signatures and cheat-trace rules');

        try {
            const result = await window.pywebview.api.prefetch_run();
            if (!result) throw new Error('No output returned.');
            if (result.error && !(result.entries?.length)) {
                container.innerHTML = `
                    <div class="scanning-state">
                        <p>${escapeHtml(result.error)}</p>
                    </div>`;
                return;
            }
            pfResult = result;
            pfSelected = -1;
            renderPrefetch(container, result);
            bindPfContainer(container);
        } catch (err) {
            container.innerHTML = isCancellation(err)
                ? '<div class="scanning-state"><p>Scan stopped.</p></div>'
                : `<div class="scanning-state"><p>Error running scan: ${escapeHtml(err.message || err)}</p></div>`;
        } finally {
            setPfBusy(false);
        }
    };

    const bindPfContainer = (container) => {
        if (!container || container.dataset.pfBound === '1') return;
        container.dataset.pfBound = '1';

        container.addEventListener('click', (e) => {
            const sortBtn = e.target.closest('[data-pf-sort]');
            if (sortBtn && container.contains(sortBtn)) {
                const key = sortBtn.getAttribute('data-pf-sort');
                if (pfSort.key === key) pfSort.dir *= -1;
                else pfSort = { key, dir: key === 'time' ? -1 : 1 };
                SoundFX.click();
                paintPfTable();
                return;
            }
            const copyBtn = e.target.closest('[data-pf-copy]');
            if (copyBtn && container.contains(copyBtn)) {
                SoundFX.click();
                addRipple(copyBtn, e.clientX, e.clientY);
                const flagged = (pfResult?.entries || [])
                    .filter(pfRuleFlagged)
                    .map(x => {
                        const det = (x.matchedDetails || []).map(h =>
                            `${h.name || h.id} "${h.sample || ''}" @${toHex(h.offset)}${h.encoding ? ` ${h.encoding}` : ''}${h.count > 1 ? ` ×${h.count}` : ''}`);
                        return `${x.readableTime || ''}  ${x.properPath || x.filename || ''}  [${det.join('; ') || (x.matchedRules || []).join(',')}]`;
                    });
                copyAltText(flagged, copyBtn, 'items');
                return;
            }
            const row = e.target.closest('[data-pf-row]');
            if (row && container.contains(row)) {
                const idx = Number(row.getAttribute('data-pf-row'));
                if (Number.isInteger(idx)) {
                    SoundFX.tool();
                    addRipple(row, e.clientX, e.clientY);
                    openPfModal(idx);
                }
            }
        });

        container.addEventListener('keydown', (e) => {
            if (e.key !== 'Enter' && e.key !== ' ') return;
            const row = e.target.closest?.('[data-pf-row]');
            if (row && container.contains(row)) {
                e.preventDefault();
                const idx = Number(row.getAttribute('data-pf-row'));
                if (Number.isInteger(idx)) {
                    SoundFX.tool();
                    openPfModal(idx);
                }
            }
        });

        container.addEventListener('change', (e) => {
            const check = e.target.closest?.('[data-pf-check]');
            if (!check || !container.contains(check)) return;
            const key = check.getAttribute('data-pf-check');
            pfFilters[key] = check.checked;
            check.closest('.pf-check')?.classList.toggle('on', check.checked);
            paintPfTable();
        });

        container.addEventListener('input', (e) => {
            const search = e.target.closest?.('[data-pf-search]');
            if (!search || !container.contains(search)) return;
            pfFilters.q = search.value;
            debouncedPfPaint();
        });
    };

    let pfModalKind = 'pf';

    const bindPfModal = () => {
        if (!ui.pfModal || ui.pfModal.dataset.bound === '1') return;
        ui.pfModal.dataset.bound = '1';

        ui.pfModalCloseBtn?.addEventListener('mouseenter', () => SoundFX.hover());
        ui.pfModalCloseBtn?.addEventListener('click', (e) => {
            SoundFX.close();
            addRipple(ui.pfModalCloseBtn, e.clientX, e.clientY);
            closePfModal();
        });

        ui.pfModal.addEventListener('click', (e) => {
            if (e.target === ui.pfModal) {
                closePfModal();
                return;
            }
            const copyBtn = e.target.closest('[data-pf-copy]');
            if (copyBtn && ui.pfModal.contains(copyBtn) && copyBtn.getAttribute('data-pf-copy') === 'related') {
                SoundFX.click();
                addRipple(copyBtn, e.clientX, e.clientY);
                const list = Array.isArray(pfResult?.entries) ? pfResult.entries : [];
                const files = (pfSelected >= 0 && pfSelected < list.length && Array.isArray(list[pfSelected].relatedFiles))
                    ? list[pfSelected].relatedFiles.map(f => f.path).filter(Boolean)
                    : [];
                copyAltText(files, copyBtn, 'paths');
                return;
            }
            const bamCopyBtn = e.target.closest('[data-bam-copy]');
            if (bamCopyBtn && ui.pfModal.contains(bamCopyBtn)) {
                SoundFX.click();
                addRipple(bamCopyBtn, e.clientX, e.clientY);
                const list = Array.isArray(bamResult?.entries) ? bamResult.entries : [];
                const lines = (bamSelected >= 0 && bamSelected < list.length && Array.isArray(list[bamSelected].replaceResults))
                    ? list[bamSelected].replaceResults.map(r => `[${r.replaceType || ''}] ${r.filename || ''}\n${r.details || ''}`)
                    : [];
                copyAltText(lines, bamCopyBtn, 'items');
                return;
            }
            const tab = e.target.closest('[data-pf-tab]');
            if (tab && ui.pfModal.contains(tab)) {
                const key = tab.getAttribute('data-pf-tab');
                if (key !== pfTab) {
                    pfTab = key;
                    SoundFX.click();
                    if (pfModalKind === 'bam') paintBamModal();
                    else paintPfModal();
                }
            }
        });

        ui.pfModal.addEventListener('mouseenter', (e) => {
            if (e.target.closest?.('[data-pf-tab]')) SoundFX.hover();
        }, true);

        ui.pfModal.addEventListener('input', (e) => {
            const input = e.target.closest?.('[data-pf-filter]');
            if (!input || !ui.pfModal.contains(input)) return;
            const q = input.value.trim().toLowerCase();
            const list = input.parentElement?.querySelector('.alt-list');
            if (!list) return;
            list.querySelectorAll('.alt-row').forEach(row => {
                const hay = (row.textContent || '').toLowerCase();
                row.hidden = q !== '' && !hay.includes(q);
            });
        });
    };

    const exportPfResult = () => {
        if (!pfResult) return;
        exportJson(pfResult, `Prefetch_${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
    };

    document.addEventListener('visibilitychange', () => {
        document.documentElement.classList.toggle('bg-paused', document.hidden);
    });
    document.documentElement.classList.toggle('bg-paused', document.hidden);

    ui.toolCards.forEach(card => {
        card.addEventListener('mouseenter', () => SoundFX.hover());

        card.addEventListener('click', (e) => {
            SoundFX.tool();
            addRipple(card, e.clientX, e.clientY);

            if (card.dataset.tool === 'service-checker') {
                openServiceChecker();
            } else if (card.dataset.tool === 'alt-detector') {
                openAltDetector();
            } else if (card.dataset.tool === 'prefetch') {
                openPrefetch();
            } else if (card.dataset.tool === 'bam') {
                openBam();
            } else if (card.dataset.tool === 'system-informer') {
                openSi();
            }
        });
    });

    $$('.nav-item').forEach(btn => {
        btn.addEventListener('mouseenter', () => SoundFX.hover());
    });

    ui.minimizeMainBtn?.addEventListener('click', (e) => {
        SoundFX.minimize();
        addRipple(ui.minimizeMainBtn, e.clientX, e.clientY);
        window.pywebview?.api?.minimize_window();
    });

    ui.closeMainBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.closeMainBtn, e.clientX, e.clientY);
        window.pywebview?.api?.close_window();
    });

    ui.serviceBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.serviceBackBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.serviceBackBtn, e.clientX, e.clientY);
        stopCurrentScan();
        showScreen('main-screen');
    });
    bindOnce(ui.svcRescanBtn, () => openServiceChecker(true), 'tool');
    bindOnce(ui.svcExportBtn, () => {
        if (!svcResult) return;
        exportSvcResult();
    }, 'tool');

    ui.altBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.altBackBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.altBackBtn, e.clientX, e.clientY);
        stopCurrentScan();
        showScreen('main-screen');
    });

    ui.pfBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.pfBackBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.pfBackBtn, e.clientX, e.clientY);
        closePfModal();
        stopCurrentScan();
        showScreen('main-screen');
    });
    bindOnce(ui.pfRescanBtn, () => openPrefetch(true), 'tool');
    bindOnce(ui.pfExportBtn, () => {
        if (!pfResult) return;
        exportPfResult();
    }, 'tool');

    ui.bamBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.bamBackBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.bamBackBtn, e.clientX, e.clientY);
        closePfModal();
        stopCurrentScan();
        showScreen('main-screen');
    });
    ui.siBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.siBackBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.siBackBtn, e.clientX, e.clientY);
        stopSiPoll();
        showScreen('main-screen');
    });
    bindOnce(ui.bamRescanBtn, () => openBam(true), 'tool');
    bindOnce(ui.bamExportBtn, () => {
        if (!bamResult) return;
        exportBamResult();
    }, 'tool');

    ui.modalCloseBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.modalCloseBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.modalCloseBtn, e.clientX, e.clientY);
        closeModal();
    });

    ui.modal?.addEventListener('click', (e) => {
        if (e.target === ui.modal) {
            SoundFX.modalClose();
            closeModal();
        }
    });

    window.addEventListener('keydown', (e) => {
        if (e.key === 'Escape') {
            if (ui.modal.classList.contains('active')) {
                closeModal();
            } else if (pfModalOpen()) {
                closePfModal();
            } else if (ui.serviceBackBtn && $('service-screen')?.classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.altBackBtn && $('alt-screen')?.classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.pfBackBtn && $('prefetch-screen')?.classList.contains('active')) {
                closePfModal();
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.bamBackBtn && $('bam-screen')?.classList.contains('active')) {
                closePfModal();
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.siBackBtn && $('si-screen')?.classList.contains('active')) {
                stopSiPoll();
                showScreen('main-screen');
            }
        }
    });

    $$('.island-brand, .footer-text, .rank-badge').forEach(el => {
        el.addEventListener('mouseenter', () => SoundFX.softHover());
    });

    bindAltToolbar();
    bindPfModal();


    let bamResult = null;
    let bamBusy = false;
    let bamSelected = -1;
    let bamSort = { key: 'time', dir: -1 };
    const bamFilters = { unsigned: false, flagged: false, instance: false, q: '' };

    const bamFileName = (p) => {
        const s = String(p || '');
        const i = Math.max(s.lastIndexOf('\\'), s.lastIndexOf('/'));
        return i >= 0 ? s.slice(i + 1) : s;
    };

    const bamFlagged = (e) => (e.matchedRules || []).some(r => r && r !== 'none');
    const bamReplaced = (e) => (e.replaceResults || []).length > 0;

    const bamLevel = (e) => {
        if (bamFlagged(e) || bamReplaced(e)) return 'bad';
        if (e.signature !== 'Signed') return 'warn';
        return 'ok';
    };

    const bamSigPill = (sig) => {
        if (sig === 'Signed') return '<span class="pf-flag ok">Signed</span>';
        if (sig === 'Deleted') return '<span class="pf-flag muted">Deleted</span>';
        return `<span class="pf-flag bad">${escapeHtml(sig || 'Unknown')}</span>`;
    };

    const setBamBusy = (busy) => {
        bamBusy = busy;
        if (ui.bamRescanBtn) {
            ui.bamRescanBtn.disabled = busy;
            ui.bamRescanBtn.hidden = busy;
        }
        if (ui.bamExportBtn) {
            const ready = !busy && !!bamResult;
            ui.bamExportBtn.disabled = !ready;
            ui.bamExportBtn.hidden = !ready;
        }
    };

    const bamVisibleEntries = () => {
        const list = Array.isArray(bamResult?.entries) ? bamResult.entries : [];
        const q = (bamFilters.q || '').trim().toLowerCase();
        const out = [];
        list.forEach((e, i) => {
            if (bamFilters.unsigned && e.signature === 'Signed') return;
            if (bamFilters.flagged && !bamFlagged(e)) return;
            if (bamFilters.instance && !e.isInInstance) return;
            if (q) {
                const hay = [
                    e.path || '', e.readableTime || '', e.signature || '',
                    ((e.matchedDetails || []).map(h => `${h.name || ''} ${h.id || ''} ${h.sample || ''}`).join(' ')),
                    ((e.replaceResults || []).map(r => `${r.replaceType || ''} ${r.filename || ''}`).join(' '))
                ].join(' ').toLowerCase();
                if (!hay.includes(q)) return;
            }
            out.push({ e, i });
        });
        const dir = bamSort.dir;
        const byTime = (a, b) => (a.e.executedUnix || 0) - (b.e.executedUnix || 0);
        const byPath = (a, b) => String(a.e.path || '').localeCompare(String(b.e.path || ''));
        const bySigned = (a, b) => (a.e.signature || '').localeCompare(b.e.signature || '');
        const byRules = (a, b) => String((a.e.matchedRules || []).join(',')).localeCompare(
            String((b.e.matchedRules || []).join(',')));
        const cmp = bamSort.key === 'path' ? byPath
            : bamSort.key === 'signed' ? bySigned
            : bamSort.key === 'rules' ? byRules : byTime;
        out.sort((a, b) => cmp(a, b) * dir);
        return out;
    };

    const paintBamTable = () => {
        const wrap = $('bamTableWrap');
        if (!wrap) return;
        const rendered = renderBamTable();
        wrap.innerHTML = rendered.html;
        const meta = wrap.closest('.service-section')?.querySelector('.svc-section-meta');
        if (meta) {
            const skipped = bamResult?.drivesSkipped || 0;
            meta.textContent = `${rendered.count} shown${skipped ? ` · ${skipped} USN drive${skipped === 1 ? '' : 's'} skipped` : ''}`;
        }
    };

    const debouncedBamPaint = debounce(() => paintBamTable(), 150);

    const renderBamTable = () => {
        const rows = bamVisibleEntries();
        if (!rows.length) {
            return { html: '<p class="svc-empty">No BAM entries match the current search and filters.</p>', count: 0 };
        }
        const arrow = (key) => bamSort.key === key
            ? `<span class="pf-arrow">${bamSort.dir === 1 ? '▲' : '▼'}</span>` : '';
        const th = (key, label) => `
            <button type="button" class="pf-sort ${bamSort.key === key ? 'sorted' : ''}" data-bam-sort="${key}">
                ${escapeHtml(label)} ${arrow(key)}
            </button>`;
        const body = rows.map(({ e, i }, pos) => {
            const lvl = bamLevel(e);
            const hits = Array.isArray(e.matchedDetails) && e.matchedDetails.length
                ? e.matchedDetails
                : (e.matchedRules || []).filter(r => r && r !== 'none')
                    .map(r => ({ id: r, name: r, sample: '' }));
            const rulesHtml = hits.length
                ? `<div class="pf-rules-cell">${hits.map(h => `<span class="pf-rule" title="${escapeHtml(pfHitTitle(h))}">${escapeHtml(h.name || h.id)}${h.count > 1 ? ` <b>×${h.count}</b>` : ''}</span>`).join('')}</div>`
                : '<span class="pf-rule-none">—</span>';
            const subBits = [escapeHtml(bamFileName(e.path))];
            if (e.isInInstance) subBits.push('<span class="in-inst">· in instance</span>');
            if (bamReplaced(e)) subBits.push(`<span class="pf-warn">· ${(e.replaceResults || []).length} replace hit${(e.replaceResults || []).length === 1 ? '' : 's'}</span>`);
            return `
            <div class="pf-row bam4 lvl-${lvl}" data-bam-row="${i}" role="button" tabindex="0" title="Open details" style="--d:${Math.min(pos, 60) * 16}ms">
                <span class="pf-main">
                    <span class="pf-fileicon lvl-${lvl}">${UI_ICONS.doc}</span>
                    <span class="pf-row-main">
                        <span class="pf-path" title="${escapeHtml(e.path || '')}">${escapeHtml(e.path || '—')}</span>
                        <span class="pf-sub">${subBits.join(' ')}</span>
                    </span>
                </span>
                <span class="pf-timecol" title="${escapeHtml(e.readableTime || '')}">
                    <b>${escapeHtml(relTime(e.executedUnix))}</b>
                    <span>${escapeHtml((e.readableTime || '').slice(0, 16) || '—')}</span>
                </span>
                <span>${bamSigPill(e.signature)}</span>
                <span>${rulesHtml}</span>
            </div>`;
        }).join('');
        return {
            html: `
        <div class="pf-table" role="table" aria-label="BAM entries">
            <div class="pf-head bam4" role="row">
                ${th('path', 'Binary')}
                ${th('time', 'Last exec')}
                ${th('signed', 'Signature')}
                ${th('rules', 'Generics')}
            </div>
            ${body}
        </div>`,
            count: rows.length
        };
    };

    const renderBam = (container, data) => {
        const entries = Array.isArray(data.entries) ? data.entries : [];
        const unsigned = entries.filter(e => e.signature !== 'Signed').length;
        const flagged = entries.filter(bamFlagged).length;
        const replaced = entries.filter(bamReplaced).length;
        const inInstance = entries.filter(e => e.isInInstance).length;
        const lvl = (flagged || replaced) ? 'bad' : (unsigned ? 'warn' : 'ok');

        const vTitle = lvl === 'bad' ? 'Threat traces detected'
            : lvl === 'warn' ? 'Review recommended' : 'System looks clean';
        let vSub = lvl === 'bad'
            ? `${flagged} rule match${flagged === 1 ? '' : 'es'} · ${replaced} replace hit${replaced === 1 ? '' : 's'} · ${unsigned} unsigned`
            : lvl === 'warn'
            ? `${unsigned} unsigned ${unsigned === 1 ? 'binary needs' : 'binaries need'} review · rule checks clean`
            : `${entries.length} traced ${entries.length === 1 ? 'binary' : 'binaries'} · all signed and accounted for`;
        const skipped = Math.max(0, (data.filesFound ?? entries.length) - entries.length);
        if (skipped > 0) vSub += ` · ${skipped} skipped`;
        const vCount = lvl === 'ok' ? entries.length : (data.findingCount ?? 0);

        const banner = data.admin ? '' : `
            <div class="svc-banner">
                ${ICON.lock}
                <div class="svc-banner-copy">
                    <strong>Limited scan</strong>
                    <p>Reading the BAM registry hive and USN journals needs Administrator. Results may be incomplete.</p>
                </div>
                <button type="button" class="pf-admin-btn" id="bamRelaunchAdmin">Restart as Administrator</button>
            </div>`;

        const verdict = `
            <div class="pf-verdict lvl-${lvl}">
                <span class="pf-verdict-icon">${lvl === 'ok' ? UI_ICONS.shieldOk : UI_ICONS.alert}</span>
                <div class="pf-verdict-copy">
                    <strong>${escapeHtml(vTitle)}</strong>
                    <p>${escapeHtml(vSub)}</p>
                </div>
                <div class="pf-verdict-count">
                    <span class="pf-num" data-n="${vCount}">0</span>
                    <small>${lvl === 'ok' ? 'entries' : 'findings'}</small>
                </div>
            </div>`;

        const stat = (icon, n, label, hot, pos) => `
            <div class="pf-stat ${hot}" style="--d:${pos * 60}ms">
                <span class="pf-stat-ic">${icon}</span>
                <span class="pf-stat-copy">
                    <span class="pf-num" data-n="${n}">0</span>
                    <span class="pf-stat-label">${escapeHtml(label)}</span>
                </span>
            </div>`;
        const summary = `
            <div class="pf-stats">
                ${stat(UI_ICONS.layers, entries.length, 'Entries', '', 0)}
                ${stat(UI_ICONS.shieldOk, unsigned, 'Unsigned', unsigned ? 'warm' : '', 1)}
                ${stat(UI_ICONS.zap, flagged, 'Flagged', flagged ? 'hot' : '', 2)}
                ${stat(UI_ICONS.search, replaced, 'Replaced', replaced ? 'hot' : '', 3)}
                ${stat(UI_ICONS.clock, inInstance, 'In instance', '', 4)}
            </div>`;

        const filters = `
            <div class="pf-toolbar">
                <div class="pf-search-wrap">
                    ${UI_ICONS.search}
                    <input type="search" class="pf-search" data-bam-search placeholder="Search binary, rule or time…" value="${escapeHtml(bamFilters.q)}" spellcheck="false" autocomplete="off">
                </div>
                <div class="pf-filters">
                    <label class="pf-check ${bamFilters.unsigned ? 'on' : ''}"><input type="checkbox" data-bam-check="unsigned" ${bamFilters.unsigned ? 'checked' : ''}>Unsigned only</label>
                    <label class="pf-check ${bamFilters.flagged ? 'on' : ''}"><input type="checkbox" data-bam-check="flagged" ${bamFilters.flagged ? 'checked' : ''}>Flagged only</label>
                    <label class="pf-check ${bamFilters.instance ? 'on' : ''}"><input type="checkbox" data-bam-check="instance" ${bamFilters.instance ? 'checked' : ''}>Only in instance</label>
                    <button type="button" class="pf-btn alt-copy-btn" data-bam-copy="flagged">Copy flagged</button>
                </div>
            </div>`;

        container.innerHTML = banner + verdict + summary
            + renderServiceSection(SVC_ICONS.events, 'BAM entries', filters + '<div id="bamTableWrap"></div>', `${entries.length} entries${data.drivesScanned ? ` · ${data.drivesScanned} USN drives` : ''}`);

        revealSections(container);
        paintBamTable();
        countUp(container);

        const relaunchBtn = container.querySelector('#bamRelaunchAdmin');
        bindOnce(relaunchBtn, async () => {
            try { await window.pywebview?.api?.relaunch_as_admin?.(); } catch (e) { }
        }, 'tool');
    };

    const openBam = async (force = false) => {
        showScreen('bam-screen');
        if (bamBusy) return;

        const container = $('bam-results');
        if (!force && bamResult) {
            renderBam(container, bamResult);
            bindBamContainer(container);
            return;
        }
        setBamBusy(true);
        bamResult = null;
        bamSelected = -1;
        container.innerHTML = loadingState(
            'Reading BAM traces…',
            'Registry execution times, binary signatures and USN replace patterns');

        try {
            const result = await window.pywebview.api.bam_run();
            if (!result) throw new Error('No output returned.');
            if (result.error && !(result.entries?.length)) {
                container.innerHTML = `
                    <div class="scanning-state">
                        <p>${escapeHtml(result.error)}</p>
                    </div>`;
                return;
            }
            bamResult = result;
            bamSelected = -1;
            renderBam(container, result);
            bindBamContainer(container);
        } catch (err) {
            container.innerHTML = isCancellation(err)
                ? '<div class="scanning-state"><p>Scan stopped.</p></div>'
                : `<div class="scanning-state"><p>Error running scan: ${escapeHtml(err.message || err)}</p></div>`;
        } finally {
            setBamBusy(false);
        }
    };

    const openBamModal = (idx) => {
        const list = Array.isArray(bamResult?.entries) ? bamResult.entries : [];
        if (!Number.isInteger(idx) || idx < 0 || idx >= list.length) return;
        pfModalKind = 'bam';
        bamSelected = idx;
        pfTab = 'details';
        paintBamModal();
        ui.pfModal?.classList.add('active');
        SoundFX.modalOpen();
        ui.pfModalBody?.scrollTo?.(0, 0);
    };

    const paintBamModal = () => {
        const list = Array.isArray(bamResult?.entries) ? bamResult.entries : [];
        if (bamSelected < 0 || bamSelected >= list.length) return;
        const e = list[bamSelected];
        const lvl = bamLevel(e);

        const title = $('pfModalTitle');
        if (title) title.textContent = bamFileName(e.path) || 'Entry details';
        if (ui.pfModalIcon) {
            ui.pfModalIcon.className = `pf-modal-icon lvl-${lvl}`;
            ui.pfModalIcon.innerHTML = lvl === 'ok' ? UI_ICONS.shieldOk : UI_ICONS.alert;
        }
        if (ui.pfModalSub) {
            ui.pfModalSub.textContent = [e.path || '', e.readableTime || '', e.signature || '']
                .filter(Boolean).join('  ·  ');
        }
        if (ui.pfModalTabs) {
            ui.pfModalTabs.innerHTML = [
                ['details', 'Details'],
                ['rules', 'Rules'],
                ['replace', 'Replace']
            ].map(([key, label]) => `
                <button type="button" class="pf-tab ${pfTab === key ? 'active' : ''}" data-pf-tab="${key}" role="tab">${escapeHtml(label)}</button>
            `).join('');
        }
        if (ui.pfModalBody) {
            ui.pfModalBody.innerHTML = bamModalBodyHtml(e);
            ui.pfModalBody.scrollTop = 0;
        }
    };

    const bamModalBodyHtml = (e) => {
        if (pfTab === 'rules') {
            const hits = Array.isArray(e.matchedDetails) ? e.matchedDetails : [];
            return hits.length
                ? hits.map(h => `
                    <div class="pf-rule-card">
                        <div class="pf-rule-head">
                            <span class="pf-rule-name">${escapeHtml(h.name || h.id)}</span>
                            <span class="pf-rule-id">${escapeHtml(h.id)}</span>
                            ${h.count > 1 ? `<span class="pf-rule-id">×${h.count}</span>` : ''}
                        </div>
                        <div class="pf-rule-meta">
                            <span class="pf-off">${escapeHtml(toHex(h.offset))}</span>
                            ${h.encoding ? `<span class="pf-enc">${escapeHtml(h.encoding)}</span>` : ''}
                        </div>
                        <code class="pf-rule-sample">${escapeHtml(h.sample || h.id)}</code>
                        ${h.context ? `<code class="pf-ctx">${escapeHtml(h.context)}</code>` : ''}
                    </div>`).join('')
                : '<p class="svc-empty">No rules matched this binary.</p>';
        }
        if (pfTab === 'replace') {
            const reps = Array.isArray(e.replaceResults) ? e.replaceResults : [];
            return reps.length
                ? `<div class="pf-related-bar">
                       <button type="button" class="pf-btn alt-copy-btn" data-bam-copy="replace">Copy all</button>
                       <span class="pf-related-note">${reps.length} hit${reps.length === 1 ? '' : 's'}</span>
                   </div>` + reps.map(r => `
                    <div class="pf-rule-card">
                        <div class="pf-rule-head">
                            <span class="pf-rule-name">${escapeHtml(r.replaceType || 'Replace')} replacement</span>
                            <span class="pf-rule-id">${escapeHtml(r.filename || '')}</span>
                        </div>
                        <code class="pf-ctx">${escapeHtml(r.details || '')}</code>
                    </div>`).join('')
                : '<p class="svc-empty">No USN replace patterns recorded for this file.</p>';
        }
        return `
            ${svcSignal('', 'Path', e.path || '—')}
            ${svcSignal('', 'Last execution', e.readableTime ? `${e.readableTime} (${relTime(e.executedUnix)})` : '—')}
            ${svcSignal('', 'Signature', e.signature || '—')}
            ${svcSignal('', 'In current instance', e.isInInstance ? 'Yes' : 'No')}
            ${svcSignal('', 'Present on disk', e.signature === 'Deleted' ? 'No — file is missing' : 'Yes')}`;
    };

    const bindBamContainer = (container) => {
        if (!container || container.dataset.bamBound === '1') return;
        container.dataset.bamBound = '1';

        container.addEventListener('click', (ev) => {
            const sortBtn = ev.target.closest('[data-bam-sort]');
            if (sortBtn && container.contains(sortBtn)) {
                const key = sortBtn.getAttribute('data-bam-sort');
                if (bamSort.key === key) bamSort.dir *= -1;
                else bamSort = { key, dir: key === 'time' ? -1 : 1 };
                SoundFX.click();
                paintBamTable();
                return;
            }
            const copyBtn = ev.target.closest('[data-bam-copy]');
            if (copyBtn && container.contains(copyBtn)) {
                SoundFX.click();
                addRipple(copyBtn, ev.clientX, ev.clientY);
                const flagged = (bamResult?.entries || [])
                    .filter(x => bamFlagged(x) || bamReplaced(x))
                    .map(x => {
                        const det = (x.matchedDetails || []).map(h => `${h.name || h.id}`).join(',');
                        const rep = (x.replaceResults || []).map(r => r.replaceType || '').join(',');
                        return `${x.readableTime || ''}  ${x.path || ''}  [${x.signature || ''}]  rules:[${det}]  replace:[${rep}]`;
                    });
                copyAltText(flagged, copyBtn, 'items');
                return;
            }
            const row = ev.target.closest('[data-bam-row]');
            if (row && container.contains(row)) {
                const idx = Number(row.getAttribute('data-bam-row'));
                if (Number.isInteger(idx)) {
                    SoundFX.tool();
                    addRipple(row, ev.clientX, ev.clientY);
                    openBamModal(idx);
                }
            }
        });

        container.addEventListener('keydown', (ev) => {
            if (ev.key !== 'Enter' && ev.key !== ' ') return;
            const row = ev.target.closest?.('[data-bam-row]');
            if (row && container.contains(row)) {
                ev.preventDefault();
                const idx = Number(row.getAttribute('data-bam-row'));
                if (Number.isInteger(idx)) {
                    SoundFX.tool();
                    openBamModal(idx);
                }
            }
        });

        container.addEventListener('change', (ev) => {
            const check = ev.target.closest?.('[data-bam-check]');
            if (!check || !container.contains(check)) return;
            const key = check.getAttribute('data-bam-check');
            bamFilters[key] = check.checked;
            check.closest('.pf-check')?.classList.toggle('on', check.checked);
            paintBamTable();
        });

        container.addEventListener('input', (ev) => {
            const search = ev.target.closest?.('[data-bam-search]');
            if (!search || !container.contains(search)) return;
            bamFilters.q = search.value;
            debouncedBamPaint();
        });
    };

    const exportBamResult = () => {
        if (!bamResult) return;
        exportJson(bamResult, `BAM_${new Date().toISOString().replace(/[:.]/g, '-')}.json`);
    };




    const SI_GUIDE = [
        {
            title: 'System Informer setup (Nightly + kernel driver)',
            level: 'high',
            desc: 'System Informer is the Process Hacker successor — a free, powerful tool to monitor resources, debug software and detect malware. It is the only build with a Kernel Driver toggle on Windows 11, and reads service memory live with no memory sample needed.',
            value: 'Without the Nightly build and an active driver, the thread and memory cards silently miss evidence.',
            steps: [
                'Install System Informer from the Nightly build and open it as Administrator.',
                'Enable the kernel driver: Options → toggle Kernel Driver → close SI and restart it.',
                '!WARNING! Some driver-level actions can bluescreen the PC — only run the steps listed on each card.',
                'Work mainly in Processes; use the Disk tab for live recordings/VPNs and Devices for plugged or once-recognized drives.',
                'For every memory check: Properties → Memory → check Hide free pages + Hide reserved pages → Strings, length 4, Extended Unicode + Mapped + Image.'
            ],
            filters: []
        },
        {
            title: 'DPS process',
            level: 'high',
            desc: 'The DPS service detects renamed extensions and executions. Its memory records executed paths — sweep it for modified-extension and non-standard executions.',
            value: 'Finds renamed and hidden executions — especially modified-extension and localhost runs.',
            steps: [
                'Locate the DPS svchost (dps.dll) in System Informer.',
                'Right-click → Properties → Memory → Strings (minimum length 4, Extended Unicode, Mapped, Image).',
                'Start with the Wiki modified-extension filter, then run the three exclusion sweeps.',
                'Investigate every non-.exe / non-.dll hit and every localhost path.'
            ],
            filters: [
                { kind: 'Regex', note: 'Wiki · modified extension in DPS', text: '^!![A-Z]((?!Exe).)*$' },
                { kind: 'Regex', note: 'Excludes .exe and .dll', text: '^\\\\device\\\\harddiskvolume((?!Exe|dll).)*$' },
                { kind: 'Regex', note: 'Modified extension — non-.exe from a volume path', text: '^\\\\device\\\\harddiskvolume[0-99]\\\\((?!exe).)*$' },
                { kind: 'Regex', note: 'Localhost executions', text: '^\\\\device\\\\mup\\\\localhost\\\\*' }
            ]
        },
        {
            title: 'CSRSS process',
            level: 'high',
            desc: 'CSRSS tracks all process creation on the system. There are two CSRSS instances — use the correct one for each check.',
            value: 'Catches process creation, hollowing traces and renamed-extension bypasses.',
            steps: [
                'Locate both csrss.exe instances and compare their Private Bytes.',
                'For .exe paths — use the CSRSS with lower private bytes. Filter, then save the dump to a folder and run it through Paths Parser.',
                'For .dll paths — use the CSRSS with higher private bytes. Filter, then save the dump and run it through Paths Parser.',
                'Renamed Extension — finds files with non-standard extensions that were executed (modified extension bypass). Use the CSRSS with most bytes.',
                'Unsigned-scan prep (Maceta flow): dump the highest-memory csrss with strings :\\ and .exe, save results as .txt, then execute the scanner as Admin.'
            ],
            filters: [
                { kind: 'Regex', note: '.exe paths — lower private bytes CSRSS', text: '^[A-Z]:\\\\.+\\.(exe)$' },
                { kind: 'Regex', note: '.dll paths — higher private bytes CSRSS', text: '^[A-Z]:\\\\.+\\.(dll)$' },
                { kind: 'Regex', note: 'Renamed extension — most-bytes CSRSS', text: '^[a-z]:.+\\.((?!exe|pyd|manifest|dll|config|\\\\|cpl|microsoft-|shell).)*$' }
            ]
        },
        {
            title: 'PcaSvc process',
            level: '',
            desc: 'PcaSvc (Program Compatibility Assistant) also records application execution paths. Inspect its memory strings for paths to files in non-standard locations (Downloads, Temp, hidden directories).',
            value: 'Confirms executions from abnormal locations. Cross-reference paths found here against BAM and DPS findings.',
            steps: [
                'Locate the PcaSvc process in System Informer.',
                'Right-click → Properties → Memory → Strings (minimum length 4, Extended Unicode, Mapped, Image).',
                'Look for .exe paths referencing folders outside standard install paths (Program Files, Windows, minecraft directories).',
                'Cross-reference every suspicious path against BAM and DPS findings.'
            ],
            filters: [
                { kind: 'Regex', note: '.exe paths in memory', text: '^[A-Z]:\\\\.+\\.(exe)$' },
                { kind: 'Contains', note: 'Case-insensitive · Downloads focus', text: 'Contains (Case-insensitive) > \\Downloads\\' },
                { kind: 'Contains', note: 'Case-insensitive · Temp focus', text: 'Contains (Case-insensitive) > \\Temp\\' }
            ]
        },
        {
            title: 'Thread suspension (frozen services)',
            level: 'high',
            desc: 'Bannable stopped services: Pcasvc, SysMain, DPS, CDPU_[numbers], EventLog, DcomLaunch, Activities Cache. Worse — users only suspend threads so services show Running but log nothing. A suspect running Process Hacker / System Informer themselves is itself a ban sign.',
            value: 'Critical. Catches service kills, thread freezes and pre-SS service restarts that defeat all logging.',
            steps: [
                'Confirm none of the 7 bannable services above is stopped or disabled — then check for restarts right before the SS.',
                'In System Informer, open each service-hosting svchost → Properties → Threads.',
                'Check the start-address column for the service core DLL listed below.',
                'If any thread with a start address in the service core DLL is Suspended, the service is silently frozen — flag it.',
                'Thread-to-DLL mappings: SysMain → sechost.dll · PcaSvc → pcasvc.dll · DPS → dps.dll · EventLog → wevtsvc.dll · dusmsvc → wlanapi.dll · cdpusersvc_[numbers] → ucrtbase.dll.'
            ],
            filters: [
                { kind: 'Contains', note: 'SysMain thread DLL', text: 'sechost.dll' },
                { kind: 'Contains', note: 'PcaSvc thread DLL', text: 'pcasvc.dll' },
                { kind: 'Contains', note: 'DPS thread DLL', text: 'dps.dll' },
                { kind: 'Contains', note: 'EventLog thread DLL', text: 'wevtsvc.dll' },
                { kind: 'Contains', note: 'dusmsvc thread DLL', text: 'wlanapi.dll' },
                { kind: 'Contains', note: 'cdpusersvc thread DLL', text: 'ucrtbase.dll' }
            ]
        },
        {
            title: 'Memory clearing artifact (wiped strings)',
            level: 'high',
            desc: 'If someone used System Informer or a similar tool to manually wipe cheat strings from memory before the screenshare, they leave a trace.',
            value: 'Strong ban evidence. Proves manual memory tampering in dwm.exe, explorer.exe or taskhostw.exe.',
            steps: [
                'In System Informer, inspect dwm.exe, explorer.exe and taskhostw.exe memory strings.',
                'Search for the pattern below.',
                'A result like "name.exe (4684) (0x195c66a3000 - 0x195c66c7000)" confirms a memory region of that process was manually accessed.',
                'Record the process name, PID and memory range as evidence.'
            ],
            filters: [
                { kind: 'Contains', note: 'Manual memory-access residue', text: ') (0x' }
            ]
        },
        {
            title: 'Explorer — other disks',
            level: '',
            desc: 'Hunt executions and files on every disk except C:. Standard opening move alongside Search Everything sorted by Date Modified.',
            value: 'Finds cheats run from D:, E:, USB or virtual disks that C:-only checks miss.',
            steps: [
                'Dump the target process in System Informer (or open Explorer memory strings).',
                'Apply the filter below (search in Explorer).',
                'Investigate every non-C: hit — external and virtual-disk executions are prime cheat hiding spots.'
            ],
            filters: [
                { kind: 'Regex', note: 'Wiki · disks other than C: (Explorer)', text: '^(?!C:)[A-Z]:[\\\\](?!\\+|(u){FFFF}|rkr)' }
            ]
        },
        {
            title: 'Search Indexer — Skript configs',
            level: '',
            desc: 'The Search Indexer keeps traces that can reveal cheat configs and scripts by name pattern.',
            value: 'Catches Skript .json cheats the normal sweeps miss.',
            steps: [
                'Open Search Indexer memory strings in System Informer.',
                'Apply the filter below.',
                'Review every 6-character .json hit for cheat configs.'
            ],
            filters: [
                { kind: 'Regex', note: 'Wiki · 6-char Skript .json', text: '^[0-9a-zA-Z]{6}\\.json$' }
            ]
        },
        {
            title: 'Task Scheduler in SI',
            level: 'high',
            desc: 'Tasks fired at boot/logon bypass string logging. Catch them via Scheduler memory: XML artifacts from task creation, executed-file paths, and deleted-task names from the journal.',
            value: 'Catches boot-persistence cheats invisible to normal execution logs.',
            steps: [
                'Filter the Scheduler process for computername= and read the PC value; get the Windows username from Task Manager or C:\\Users.',
                'Hunt task-creation XML with the COMPUTERNAME string below (fill in the real PC and user values).',
                'Sweep executed files with the regex below (contains, case-insensitive) — flag .exe / .dll outside C:\\Windows\\System32.',
                'For deleted tasks: read the task name from JournalTool (C:\\Windows\\System32\\Tasks), filter Scheduler in SI by that name, copy results to .txt and read </Command>.'
            ],
            filters: [
                { kind: 'Contains', note: 'Wiki · task XML artifacts (append PC\\user)', text: 'COMPUTERNAME=' },
                { kind: 'Regex', note: 'Wiki · files executed by Schedule', text: '([A-Z]:\\\\.+\\.(dll|exe)|"[A-Z]:\\\\.+\\.(dll|exe)")$' }
            ]
        },
        {
            title: 'Devices & live disk activity',
            level: '',
            desc: 'The Devices tab lists plugged devices plus traces of once-recognized ones (virtual disks, USBs). The Disk tab shows live tasks such as recordings or background VPNs.',
            value: 'Exposes USB / virtual-disk cheat loading and recording or VPN interference.',
            steps: [
                'Open the Devices tab and review plugged plus previously recognized devices.',
                'Flag unknown virtual disks or USBs mounted around the freeze and cross-check USB history.',
                'Open the Disk tab — unexplained recordings or VPN activity during the SS needs an answer.'
            ],
            filters: []
        },
    ];

    const SI_EXTRA = [
        {
            tag: 'Nightly + driver baseline',
            apply: ['System Informer Nightly (Admin)', 'Options', 'Kernel Driver', 'Restart SI', 'Processes'],
            redflags: 'Stable-release SI, driver off, or wrong Strings options — every later card loses evidence.',
            clean: 'Nightly SI, driver on after restart, length 4 with Extended Unicode + Mapped + Image.',
            tip: 'Record the whole session. Disable Defender and third-party AV first; run commands in CMD, never PowerShell directly.',
            extra: []
        },
        {
            tag: 'Executed-path recording',
            apply: ['DPS svchost (dps.dll)', 'Properties', 'Memory', 'Strings'],
            redflags: 'Modified-extension hits from the Wiki filter, non-.exe / non-.dll runs, localhost paths, binaries in Downloads / Temp / hidden folders.',
            clean: 'Standard .exe / .dll under Program Files, Windows and game folders.',
            tip: 'Start with the Wiki modified-extension filter, then run the three exclusion sweeps. Cross-check every hit against BAM and Prefetch before calling it.',
            extra: []
        },
        {
            tag: 'Process creation',
            apply: ['csrss.exe (both instances)', 'Properties', 'Memory', 'Strings'],
            redflags: 'Only ONE csrss.exe, a csrss.exe outside System32, userland .exe strings, or renamed-extension hits (e.g. .txt / .dat that actually ran code).',
            clean: 'Two instances in System32, no user .exe strings, no renamed-extension hits.',
            tip: 'Lower Private Bytes ≈ .exe activity, higher ≈ .dll. Save each filtered dump and run it through Paths Parser; keep the .txt for the unsigned scan.',
            extra: []
        },
        {
            tag: 'Compat executions',
            apply: ['PcaSvc process', 'Properties', 'Memory', 'Strings'],
            redflags: 'Any .exe path in Downloads, Temp, hidden directories or outside Program Files / Windows / minecraft folders.',
            clean: 'Only standard install paths, no user-folder executables.',
            tip: 'Cross-reference paths found here against BAM and DPS findings — a path confirmed in two sources is strong evidence.',
            extra: []
        },
        {
            tag: 'Suspended service threads',
            apply: ['Service svchost', 'Properties', 'Threads', 'Start-address DLL'],
            redflags: 'Any of the 7 bannable services stopped/disabled, any service thread Suspended in its core DLL, services restarted just before the SS, or the suspect running Process Hacker / SI themselves.',
            clean: 'All 7 services Running with no suspended threads and stable uptimes.',
            tip: 'A service can pass the service check and still be dead. This card is the confirmation the service check cannot do alone.',
            extra: []
        },
        {
            tag: 'Wiped memory residue',
            apply: ['dwm.exe / explorer.exe / taskhostw.exe', 'Memory', 'Strings'],
            redflags: 'Any "name.exe (PID) (0x... - 0x...)" hit — a memory region was manually accessed (strings wiped pre-SS).',
            clean: 'No ") (0x" hits in dwm, explorer or taskhostw.',
            tip: 'Screenshot the full line: process name, PID and memory range are all recorded as residual evidence. This alone is strong grounds for a ban.',
            extra: []
        },
        {
            tag: 'Off-system disks',
            apply: ['System Informer', 'Explorer dump', 'Memory', 'Strings'],
            redflags: 'Executables or loaders on D: / E: / USB / virtual disks — especially around the freeze time.',
            clean: 'Only C: system and program paths.',
            tip: 'Pair with Search Everything sorted by Date Modified to spot renamed binaries hiding on those disks.',
            extra: []
        },
        {
            tag: 'Indexed configs',
            apply: ['Search Indexer', 'Memory', 'Strings'],
            redflags: '6-character .json hits that map to cheat configs or scripts.',
            clean: 'No matching .json traces.',
            tip: 'Copy the filename and pivot to Search Everything to find where it lived.',
            extra: []
        },
        {
            tag: 'Boot persistence',
            apply: ['Scheduler process', 'Memory', 'Strings', '</Command> review'],
            redflags: '.exe / .dll outside System32 executed by Schedule, task XML with odd commands, deleted tasks re-appearing near SS time.',
            clean: 'Only Windows maintenance tasks with system paths.',
            tip: 'Only boot/logon-time tasks matter — ignore hourly noise. The </Command> line is the verdict.',
            extra: []
        },
        {
            tag: 'External media',
            apply: ['System Informer', 'Devices tab', 'Disk tab'],
            redflags: 'Unknown virtual disks or USBs mounted around the freeze; live recordings or VPNs during the SS.',
            clean: 'Known drives only, no background recording or VPN.',
            tip: 'FAT32 media has no USN journal — a cheat run purely from FAT32 leaves almost no disk trace, so the Devices tab is the net.',
            extra: []
        },
    ];


    let siStatusCache = null;
    let siPollTimer = 0;
    let siDownloading = false;
    const siOpenCards = new Set();

    const siHighlight = (raw) => {
        const span = (cls, s) => `<span class="tok-${cls}">${escapeHtml(s)}</span>`;
        let out = '';
        for (let i = 0; i < raw.length;) {
            const c = raw[i];
            if (c === '\\' && i + 1 < raw.length) {
                out += span('esc', raw.slice(i, i + 2));
                i += 2;
                continue;
            }
            if (c === '^' || c === '$') {
                out += span('anchor', c);
                i++;
                continue;
            }
            if (c === '[') {
                const j = raw.indexOf(']', i + 1);
                const end = j < 0 ? raw.length - 1 : j;
                out += span('class', raw.slice(i, end + 1));
                i = end + 1;
                continue;
            }
            if (c === '(' || c === ')') {
                out += span('group', c);
                i++;
                continue;
            }
            if (c === '{') {
                const j = raw.indexOf('}', i + 1);
                if (j > i) {
                    out += span('quant', raw.slice(i, j + 1));
                    i = j + 1;
                    continue;
                }
            }
            if (c === '*' || c === '+' || c === '?' || c === '|' || c === '.') {
                let j = i + 1;
                if ((c === '*' || c === '+' || c === '?') && raw[j] === '?') j++;
                out += span('quant', raw.slice(i, j));
                i = j;
                continue;
            }
            out += escapeHtml(c);
            i++;
        }
        return out;
    };

    const siCopyFilter = (text, btn) => {
        const done = (ok) => {
            if (!btn) return;
            const label = btn.querySelector('.si-copy-label');
            if (label) {
                const prev = label.dataset.label || label.textContent;
                label.dataset.label = prev;
                label.textContent = ok ? 'Copied!' : 'Copy failed';
                btn.classList.toggle('is-copied', ok);
                clearTimeout(btn._copiedTimer);
                btn._copiedTimer = setTimeout(() => {
                    label.textContent = label.dataset.label || prev;
                    btn.classList.remove('is-copied');
                }, 1500);
            }
        };
        copyText(text, done);
    };

    const renderSiGuide = (query) => {
        const host = $('siGuide');
        if (!host) return;
        const q = (query || '').trim().toLowerCase();
        const merged = SI_GUIDE.map((g, idx) => {
            const x = SI_EXTRA[idx] || {};
            return {
                g,
                idx,
                tag: x.tag || '',
                apply: x.apply || [],
                redflags: x.redflags || '',
                clean: x.clean || '',
                tip: x.tip || '',
                filters: [...(g.filters || []), ...((x.extra || []).map(f => ({ ...f })))]
            };
        });
        const items = merged.filter(({ g, tag, redflags, clean, tip, filters }) => {
            if (!q) return true;
            const hay = [g.title, tag, g.desc, g.value, redflags, clean, tip,
                ...filters.map(f => `${f.kind} ${f.note} ${f.text}`)]
                .join(' ').toLowerCase();
            return q.split(/\s+/).every(tok => hay.includes(tok));
        });
        if (!items.length) {
            host.innerHTML = '<div class="si-empty">No methods match this filter. Clear the search to see all methods.</div>';
            return;
        }
        host.innerHTML = items.map(({ g, idx, tag, apply, redflags, clean, tip, filters }) => `
            <article class="si-card ${siOpenCards.has(idx) ? 'open' : ''}" data-si-card="${idx}">
                <button type="button" class="si-card-head" data-si-toggle="${idx}" aria-expanded="${siOpenCards.has(idx) ? 'true' : 'false'}">
                    <span class="si-num">${idx + 1}</span>
                    <span class="si-card-title">${escapeHtml(g.title)}</span>
                    ${tag ? `<span class="svc-section-meta">${escapeHtml(tag)}</span>` : ''}
                    ${g.level === 'high' ? '<span class="si-badge high">High value</span>' : ''}
                    <span class="si-chev"><svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><polyline points="6 9 12 15 18 9"/></svg></span>
                </button>
                <div class="si-card-body">
                    <div class="si-card-body-inner">
                        <div class="si-card-content">
                            <p class="si-desc">${escapeHtml(g.desc)}</p>
                            <div class="si-value ${g.level}"><strong>Forensic value:</strong><span>${escapeHtml(g.value)}</span></div>
                            ${apply.length ? `<div class="si-apply"><span>Click path:</span>${apply.map((a, i) => `${i ? '<span class="si-path-sep">→</span>' : ''}<span class="si-path-chip">${escapeHtml(a)}</span>`).join('')}</div>` : ''}
                            <ol class="si-steps">${(g.steps || []).map((s, n) => `<li data-step="${n + 1}">${escapeHtml(s)}</li>`).join('')}</ol>
                            ${(redflags || clean || tip) ? `<div class="si-analyst">
                                ${redflags ? `<div class="si-analyst-row red"><strong>Red flags</strong><span>${escapeHtml(redflags)}</span></div>` : ''}
                                ${clean ? `<div class="si-analyst-row clean"><strong>Normal</strong><span>${escapeHtml(clean)}</span></div>` : ''}
                                ${tip ? `<div class="si-analyst-row tip"><strong>Analyst tip</strong><span>${escapeHtml(tip)}</span></div>` : ''}
                            </div>` : ''}
                            <div class="si-filter-head">
                                <h4>Filters · ${filters.length}</h4>
                                <button type="button" class="si-copy-all" data-si-copyall="${idx}">
                                    <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15V5a2 2 0 0 1 2-2h10"/></svg>
                                    <span class="si-copy-label">Copy all</span>
                                </button>
                            </div>
                            ${filters.map(f => `
                                <div class="si-filter ${f.extra ? 'si-filter-extra' : ''}">
                                    <div class="si-filter-top">
                                        <span class="si-filter-kind">${escapeHtml(f.kind)}</span>
                                        ${f.extra ? '<span class="si-extra-tag">Bonus</span>' : ''}
                                        ${f.note ? `<span class="si-filter-note">${escapeHtml(f.note)}</span>` : ''}
                                        <button type="button" class="si-copy-btn" data-si-copy="${idx}" data-si-text="${escapeHtml(f.text)}">
                                            <svg viewBox="0 0 24 24" width="12" height="12" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><rect x="9" y="9" width="12" height="12" rx="2"/><path d="M5 15V5a2 2 0 0 1 2-2h10"/></svg>
                                            <span class="si-copy-label">Copy Filter</span>
                                        </button>
                                    </div>
                                    <code class="si-code">${siHighlight(f.text)}</code>
                                </div>`).join('')}
                        </div>
                    </div>
                </div>
            </article>`).join('');
        host.querySelectorAll('.si-card').forEach((el, i) => {
            setTimeout(() => el.classList.add('revealed'), 60 * i);
        });
        const meta = $('siGuideMeta');
        if (meta) meta.textContent = `${items.length} method${items.length === 1 ? '' : 's'}`;
    };

    const SI_STATE_ICON = {
        fresh: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 2l8 3v6c0 5-3.5 8.5-8 10-4.5-1.5-8-5-8-10V5z"/><path d="M8.5 12l2.5 2.5 4.5-5"/></svg>',
        stale: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
        update: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3L22 20H2L12 3z"/><path d="M12 9v5"/><path d="M12 17h.01"/></svg>',
        none: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 4v12M8.5 12.5L12 16l3.5-3.5"/><path d="M5 20h14"/></svg>'
    };

    const paintSiStatus = (st) => {
        const banner = $('siBanner');
        const icon = $('siBannerIcon');
        const title = $('siBannerTitle');
        const sub = $('siBannerSub');
        const pill = $('siStatePill');
        const meta = $('siStatusMeta');
        const launchBtn = $('siLaunchBtn');
        const removeBtn = $('siRemoveBtn');
        const setStat = (id, text, cls) => {
            const el = $(id);
            if (!el) return;
            el.textContent = text || '—';
            el.classList.remove('good', 'flag');
            if (cls) el.classList.add(cls);
        };
        if (!st) {
            if (banner) banner.dataset.state = 'stale';
            if (icon) icon.innerHTML = SI_STATE_ICON.stale;
            if (title) title.textContent = 'Status unavailable';
            if (sub) sub.textContent = 'The backend did not answer. Reopen the page to retry.';
            if (pill) { pill.textContent = 'Offline'; pill.className = 'si-pill stale'; }
            if (meta) meta.textContent = 'Unknown';
            if (launchBtn) launchBtn.disabled = true;
            if (removeBtn) removeBtn.disabled = true;
            return;
        }
        if (!st.installed) {
            if (banner) banner.dataset.state = 'none';
            if (icon) icon.innerHTML = SI_STATE_ICON.none;
            if (title) title.textContent = 'Not installed';
            if (sub) sub.textContent = st.folder
                ? `No portable System Informer found in ${st.folder}. Download the latest Canary to begin.`
                : 'No portable System Informer found. Download the latest Canary to begin.';
            if (pill) { pill.textContent = 'Not installed'; pill.className = 'si-pill stale'; }
            if (meta) meta.textContent = 'Not installed';
            setStat('siStatInstalled', 'Not installed');
            setStat('siStatLatest', st.latestVersion ? `Canary ${st.latestVersion}` : 'Unknown');
            setStat('siStatDownloaded', '—');
            setStat('siStatSize', '—');
            setStat('siStatPath', st.folder || '—');
            setStat('siStatChecked', st.lastCheckedAt ? `Checked ${st.lastCheckedAt}` : 'Never — press “Check for Updates”');
            if (launchBtn) launchBtn.disabled = true;
            if (removeBtn) removeBtn.disabled = true;
            return;
        }
        const state = st.archMismatch ? 'update' : (st.updateAvailable ? 'update' : (st.latestVersion ? 'fresh' : 'stale'));
        if (banner) banner.dataset.state = state;
        if (icon) icon.innerHTML = SI_STATE_ICON[state] || SI_STATE_ICON.stale;
        if (title) title.textContent = st.archMismatch
            ? `Wrong build for this PC (${st.exeArch || '?'} on ${st.osArch || 'Windows'})`
            : st.updateAvailable
            ? `Update available — Canary ${st.latestVersion}`
            : (st.latestVersion ? `Ready — Canary ${st.version || st.installedTag || 'installed'}` : 'Installed');
        if (sub) sub.textContent = st.checkNote || (st.exePath || '');
        if (pill) {
            pill.textContent = st.archMismatch ? 'Wrong build' : st.updateAvailable ? 'Update available' : (st.latestVersion ? 'Up to date' : 'Installed');
            pill.className = `si-pill ${state}`;
        }
        if (meta) meta.textContent = st.archMismatch ? 'Wrong build' : st.updateAvailable ? 'Update available' : (st.version ? `v${st.version}` : 'Installed');
        const archTag = st.exeArch ? ` · ${st.exeArch}` : '';
        setStat('siStatInstalled', st.version ? `Canary ${st.version}${archTag}` : `Installed${archTag}`, st.archMismatch ? 'flag' : (st.verified ? 'good' : ''));
        setStat('siStatLatest', st.latestVersion
            ? `Canary ${st.latestVersion}${st.latestPublishedAt ? ` · ${st.latestPublishedAt}` : ''}`
            : 'Unknown — check for updates', st.updateAvailable ? 'flag' : '');
        setStat('siStatDownloaded', st.lastDownload || '—');
        setStat('siStatSize', st.fileSizeLabel || '—');
        setStat('siStatPath', st.exePath || st.folder || '—');
        setStat('siStatChecked', st.lastCheckedAt ? `Checked ${st.lastCheckedAt}` : 'Never — press “Check for Updates”');
        if (launchBtn) launchBtn.disabled = false;
        if (removeBtn) removeBtn.disabled = false;
    };

    const paintSiProgress = (p) => {
        const wrap = $('siProgress');
        const fill = $('siProgressFill');
        const stage = $('siProgressStage');
        const pct = $('siProgressPct');
        const cancel = $('siCancelBtn');
        if (!wrap || !fill || !stage || !pct) return;
        if (!p || (!p.active && !p.done)) {
            wrap.hidden = true;
            if (cancel) cancel.hidden = true;
            return;
        }
        wrap.hidden = false;
        if (cancel) cancel.hidden = !p.active;
        fill.style.width = `${p.percent || 0}%`;
        stage.textContent = p.active && p.totalBytes > 0
            ? `${p.stage || ''} · ${(p.downloadedBytes / 1048576).toFixed(1)} / ${(p.totalBytes / 1048576).toFixed(1)} MB`
            : (p.stage || '');
        pct.textContent = `${p.percent || 0}%`;
    };

    const paintSiError = (msg) => {
        const el = $('siError');
        if (!el) return;
        if (!msg) {
            el.hidden = true;
            el.textContent = '';
            return;
        }
        el.hidden = false;
        el.textContent = msg;
    };

    const stopSiPoll = () => {
        if (siPollTimer) {
            clearInterval(siPollTimer);
            siPollTimer = 0;
        }
        siDownloading = false;
        const btn = $('siDownloadBtn');
        if (btn) {
            btn.disabled = false;
            btn.innerHTML = 'Download &amp; Launch Latest Canary';
        }
        const cancel = $('siCancelBtn');
        if (cancel) cancel.hidden = true;
    };

    const startSiDownload = async () => {
        const dlBtn = $('siDownloadBtn');
        if (siDownloading) return;
        siDownloading = true;
        paintSiError('');
        if (dlBtn) {
            dlBtn.disabled = true;
            dlBtn.textContent = 'Downloading…';
        }
        try {
            await window.pywebview.api.system_informer_download_start();
            paintSiProgress({ active: true, done: false, stage: 'Starting…', percent: 2 });
            stopSiPollSilent();
            siDownloading = true;
            siPollTimer = setInterval(pollSiProgress, 500);
            setTimeout(pollSiProgress, 300);
        } catch (err) {
            siDownloading = false;
            if (dlBtn) {
                dlBtn.disabled = false;
                dlBtn.innerHTML = 'Download &amp; Launch Latest Canary';
            }
            paintSiError(`Could not start the download: ${err.message || err}`);
        }
    };

    const pollSiProgress = async () => {
        let p = null;
        try {
            p = await window.pywebview.api.system_informer_progress();
        } catch (err) {
            return;
        }
        if (!p) return;
        paintSiProgress(p);
        if (!p.active) {
            stopSiPoll();
            if (p.done && p.ok) {
                paintSiError('');
                try {
                    siStatusCache = await window.pywebview.api.system_informer_status();
                    paintSiStatus(siStatusCache);
                } catch (err) { }
                SoundFX.success();
                if (p.exePath) {
                    showModal(
                        'System Informer ready',
                        `Portable Canary <b>${escapeHtml(p.version || 'latest')}</b> was extracted and launched.<br>Run it as Administrator, then work through the forensic filters below.`,
                        'success',
                        [{ label: 'Open guide', variant: '', onClick: () => scrollSiGuide() }]);
                }
            } else if (p.done && !p.ok) {
                paintSiError(p.error || 'Download failed.');
                SoundFX.error();
            }
        }
    };

    const refreshSiStatus = async () => {
        try {
            siStatusCache = await window.pywebview.api.system_informer_status();
            paintSiStatus(siStatusCache);
        } catch (err) {
            paintSiStatus(null);
        }
    };

    const scrollSiGuide = () => {
        const anchor = $('siGuideSearch') || $('siGuide');
        if (!anchor) return;
        try {
            anchor.scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch (err) {
            const scroller = $('si-screen')?.querySelector('.content');
            if (scroller) scroller.scrollTop = scroller.scrollHeight;
        }
    };

    let siChecking = false;

    const runSiCheckUpdate = async () => {
        const checkBtn = $('siCheckBtn');
        if (siChecking) return;
        siChecking = true;
        paintSiError('');
        if (checkBtn) {
            checkBtn.disabled = true;
            checkBtn.textContent = 'Checking…';
        }
        const banner = $('siBanner');
        const pill = $('siStatePill');
        if (banner) banner.dataset.state = 'stale';
        if (pill) { pill.textContent = 'Checking'; pill.className = 'si-pill stale'; }
        try {
            const r = await window.pywebview.api.system_informer_check_update();
            if (r && r.ok) {
                await refreshSiStatus();
                if (r.updateAvailable) {
                    SoundFX.modalOpen();
                    showModal(
                        'Update available',
                        `Installed: <b>${escapeHtml(r.currentVersion || 'none')}</b><br>Latest Canary: <b>${escapeHtml(r.latestVersion || 'unknown')}</b>${r.publishedAt ? `<br>Published: ${escapeHtml(r.publishedAt)}` : ''}`,
                        'info',
                        [{ label: 'Download now', variant: '', onClick: () => startSiDownload() }]);
                } else {
                    SoundFX.success();
                }
            } else {
                paintSiError((r && r.error) || 'Update check failed.');
                SoundFX.error();
                await refreshSiStatus();
            }
        } catch (err) {
            paintSiError(`Update check failed: ${err.message || err}`);
            SoundFX.error();
        } finally {
            siChecking = false;
            if (checkBtn) {
                checkBtn.disabled = false;
                checkBtn.textContent = 'Check for Updates';
            }
        }
    };

    const runSiRemove = () => {
        showModal(
            'Remove System Informer?',
            'This deletes the portable install from the app data folder. You can re-download the latest Canary at any time.',
            'info',
            [
                { label: 'Keep it', variant: '', onClick: () => {} },
                {
                    label: 'Remove', variant: '', onClick: async () => {
                        paintSiError('');
                        try {
                            await window.pywebview.api.system_informer_remove();
                            siStatusCache = null;
                            await refreshSiStatus();
                            SoundFX.success();
                        } catch (err) {
                            paintSiError(`Remove failed: ${err.message || err}`);
                            SoundFX.error();
                        }
                    }
                }
            ]);
    };

    const openSi = async () => {
        showScreen('si-screen');
        $$('#si-screen .service-section').forEach(el => el.classList.add('revealed'));
        renderSiGuide($('siGuideSearch')?.value || '');
        paintSiError('');
        if (!siDownloading) paintSiProgress(null);
        const meta = $('siStatusMeta');
        if (meta && !siStatusCache) meta.textContent = 'Checking…';
        await refreshSiStatus();
    };

    const bindSi = () => {
        const launchBtn = $('siLaunchBtn');
        const folderBtn = $('siFolderBtn');
        const checkBtn = $('siCheckBtn');
        const removeBtn = $('siRemoveBtn');
        const cancelBtn = $('siCancelBtn');
        const search = $('siGuideSearch');
        const guide = $('siGuide');

        bindOnce($('siDownloadBtn'), () => startSiDownload(), 'tool');
        bindOnce(checkBtn, () => runSiCheckUpdate(), 'tool');
        bindOnce(removeBtn, () => runSiRemove(), 'tool');

        const setAllSiCards = (open) => {
            const guideEl = $('siGuide');
            if (!guideEl) return;
            guideEl.querySelectorAll('.si-card').forEach(card => {
                const idx = Number(card.getAttribute('data-si-card'));
                card.classList.toggle('open', open);
                card.querySelector('[data-si-toggle]')?.setAttribute('aria-expanded', open ? 'true' : 'false');
                if (Number.isInteger(idx) && idx >= 0) {
                    if (open) siOpenCards.add(idx);
                    else siOpenCards.delete(idx);
                }
            });
            if (!open) siOpenCards.clear();
            SoundFX.click();
        };
        bindOnce($('siExpandBtn'), () => setAllSiCards(true), 'tool');
        bindOnce($('siCollapseBtn'), () => setAllSiCards(false), 'tool');

        document.addEventListener('keydown', (e) => {
            if (e.key !== '/' || e.ctrlKey || e.metaKey || e.altKey) return;
            const siActive = $('si-screen')?.classList.contains('active');
            if (!siActive) return;
            const t = e.target;
            if (t && (t.tagName === 'INPUT' || t.tagName === 'TEXTAREA' || t.isContentEditable)) return;
            const box = $('siGuideSearch');
            if (box) {
                e.preventDefault();
                box.focus();
            }
        });

        bindOnce(cancelBtn, async () => {
            try { await window.pywebview.api.system_informer_cancel(); } catch (err) { }
            setTimeout(pollSiProgress, 400);
        }, 'click');

        bindOnce(launchBtn, async () => {
            paintSiError('');
            try {
                await window.pywebview.api.system_informer_launch();
            } catch (err) {
                paintSiError(`Could not launch System Informer: ${err.message || err}`);
            }
        }, 'tool');

        bindOnce(folderBtn, async () => {
            try {
                await window.pywebview.api.system_informer_open_folder();
            } catch (err) {
                paintSiError(`Could not open the folder: ${err.message || err}`);
            }
        }, 'tool');

        if (search && search.dataset.bound !== '1') {
            search.dataset.bound = '1';
            const debouncedGuideRender = debounce(() => renderSiGuide(search.value), 150);
            search.addEventListener('input', debouncedGuideRender);
        }

        if (guide && guide.dataset.bound !== '1') {
            guide.dataset.bound = '1';
            guide.addEventListener('click', (e) => {
                const copyBtn = e.target.closest?.('[data-si-copy]');
                if (copyBtn && guide.contains(copyBtn)) {
                    SoundFX.click();
                    addRipple(copyBtn, e.clientX, e.clientY);
                    siCopyFilter(copyBtn.getAttribute('data-si-text') || '', copyBtn);
                    return;
                }
                const copyAll = e.target.closest?.('[data-si-copyall]');
                if (copyAll && guide.contains(copyAll)) {
                    SoundFX.click();
                    addRipple(copyAll, e.clientX, e.clientY);
                    const idx = Number(copyAll.getAttribute('data-si-copyall'));
                    const card = SI_GUIDE[idx];
                    const extra = (SI_EXTRA[idx] && SI_EXTRA[idx].extra) || [];
                    const lines = [...(card ? card.filters : []), ...extra].map(f => f.text).filter(Boolean);
                    if (!lines.length) {
                        const label = copyAll.querySelector('.si-copy-label');
                        if (label) {
                            label.textContent = 'Nothing to copy';
                            setTimeout(() => { label.textContent = 'Copy all'; }, 1500);
                        }
                        return;
                    }
                    const header = card ? `# ${(card.title || '').toUpperCase()} — System Informer filters` : '# System Informer filters';
                    siCopyFilter([header, ...lines].join('\n'), copyAll);
                    return;
                }
                const toggle = e.target.closest?.('[data-si-toggle]');
                if (toggle && guide.contains(toggle)) {
                    const card = toggle.closest('.si-card');
                    if (!card) return;
                    const isOpen = card.classList.toggle('open');
                    toggle.setAttribute('aria-expanded', isOpen ? 'true' : 'false');
                    const idx = Number(card.getAttribute('data-si-card'));
                    if (Number.isInteger(idx) && idx >= 0) {
                        if (isOpen) siOpenCards.add(idx);
                        else siOpenCards.delete(idx);
                    }
                    SoundFX.click();
                }
            });
            guide.addEventListener('mouseenter', (e) => {
                if (e.target.closest?.('.si-copy-btn, .si-copy-all, .si-card-head')) SoundFX.hover();
            }, true);
        }
    };

    const stopSiPollSilent = () => {
        if (siPollTimer) {
            clearInterval(siPollTimer);
            siPollTimer = 0;
        }
    };




    bindSi();

    const splashEl = $('splash');
    const MIN_SPLASH_MS = 1600;
    let splashStarted = Date.now();
    let splashClosed = false;
    function closeSplash() {
        if (!splashEl || splashClosed) return;
        splashClosed = true;
        const elapsed = Date.now() - splashStarted;
        const delay = Math.max(0, MIN_SPLASH_MS - elapsed);
        setTimeout(() => splashEl.classList.add('done'), delay);
    }

    setTimeout(closeSplash, 2500);
    window.addEventListener('load', closeSplash);
    document.addEventListener('DOMContentLoaded', () => setTimeout(closeSplash, 400));
    if (document.readyState === 'complete' || document.readyState === 'interactive') {
        setTimeout(closeSplash, 600);
    }
    window.addEventListener('pywebviewready', () => closeSplash());
})();
