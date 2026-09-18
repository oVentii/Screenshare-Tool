(function () {
    'use strict';

    const SoundFX = (() => {
        let ctx = null;
        let master = null;
        let unlocked = false;
        let muted = document.documentElement.getAttribute('data-muted') === '1';

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
                ctx.resume().catch(() => {});
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
            if (noiseCache.size > 8) noiseCache.clear();
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

        const hover = () => play(c => {
            const t = c.currentTime;
            tone(c, 880, 'sine', 0.035, t, 0.07, 1180);
            tone(c, 1320, 'sine', 0.018, t + 0.01, 0.05, 1550);
        });

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

        const softHover = () => play(c => {
            const t = c.currentTime;
            tone(c, 1040, 'sine', 0.022, t, 0.06, 1280);
        });

        return {
            hover, softHover, click, tool, minimize, close,
            modalOpen, modalClose, success, error,
            setMuted(value) {
                muted = !!value;
                if (master) {
                    master.gain.cancelScheduledValues(0);
                    master.gain.value = muted ? 0 : 1;
                }
            },
            isMuted() { return muted; }
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
        warning: `<svg class="inline-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M12 3L22 20H2L12 3z"/><path d="M12 9v5"/><path d="M12 17h.01"/></svg>`,
        info: `<svg class="inline-icon" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><circle cx="12" cy="12" r="10"/><path d="M12 11v5"/><path d="M12 8h.01"/></svg>`
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

    const showScreen = (id) => {
        $$('.screen').forEach(s => s.classList.remove('active'));
        const next = document.getElementById(id);
        next.classList.add('active');
        const scroller = next.querySelector('.content');
        if (scroller) scroller.scrollTop = 0;
    };

    const stopCurrentScan = () => {
        try { window.pywebview.api.cancel_scan(); } catch (e) {  }
    };

    const isCancellation = (err) => /cancell/i.test(String(err?.message || err || ''));

    const revealSections = (container) => {
        container.querySelectorAll('.service-section, .svc-summary, .svc-banner').forEach((el, i) => {
            setTimeout(() => el.classList.add('revealed'), 70 * i);
        });
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

        actions.forEach(({ label, variant, onClick }) => {
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
        boot: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/></svg>',
        drives: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><rect x="3" y="4" width="18" height="7" rx="2"/><rect x="3" y="13" width="18" height="7" rx="2"/><path d="M7 7.5h.01M7 16.5h.01"/></svg>',
        services: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><circle cx="12" cy="12" r="3"/><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 1 1-2.83 2.83l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 1 1-4 0v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 1 1-2.83-2.83l.06-.06A1.65 1.65 0 0 0 4.68 15 1.65 1.65 0 0 0 3.17 14H3a2 2 0 1 1 0-4h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 1 1 2.83-2.83l.06.06A1.65 1.65 0 0 0 9 4.68 1.65 1.65 0 0 0 10 3.17V3a2 2 0 1 1 4 0v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 1 1 2.83 2.83l-.06.06A1.65 1.65 0 0 0 19.4 9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 1 1 0 4h-.09a1.65 1.65 0 0 0-1.51 1z"/></svg>',
        events: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"/><path d="M14 2v6h6"/><path d="M8 13h8M8 17h5"/></svg>',
        recycle: '<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true"><path d="M3 6h18"/><path d="M8 6V4a1 1 0 0 1 1-1h6a1 1 0 0 1 1 1v2"/><path d="M19 6l-1 14a2 2 0 0 1-2 2H8a2 2 0 0 1-2-2L5 6"/><path d="M10 11v6M14 11v6"/></svg>'
    };

    let svcResult = null;
    let svcBusy = false;
    const setSvcBusy = (busy) => {
        svcBusy = busy;
        if (ui.svcRescanBtn) {
            ui.svcRescanBtn.disabled = busy;
            ui.svcRescanBtn.hidden = busy;
        }
    };

    const renderBootSection = (boot) => {
        const rows = [
            svcSignal('', 'Last boot', boot.lastBoot || 'Unavailable'),
            svcSignal('', 'Uptime', boot.uptime || '—')
        ];
        if (boot.tickMismatch) {
            rows.push(svcSignal('warn', 'Tick-count estimate', `${boot.tickBootEstimate || '—'} · ${boot.tickNote || 'Differs from registry'}`));
        }
        return renderServiceSection(SVC_ICONS.boot, 'Boot time', rows.join(''));
    };

    const renderDriveSection = (drives) => {
        const list = Array.isArray(drives) ? drives : [];
        if (!list.length) {
            return renderServiceSection(SVC_ICONS.drives, 'Drives', '<p class="svc-empty">No ready volumes found.</p>');
        }
        const body = `<div class="svc-drive-grid">${list.map(d => `
            <article class="svc-drive">
                <div class="svc-drive-top">
                    <span class="svc-drive-letter">${escapeHtml(d.letter)}</span>
                    <span class="svc-drive-media">${escapeHtml(d.media || 'Unknown')}</span>
                </div>
                <div class="svc-drive-fs">${escapeHtml(d.fileSystem || 'Unknown')}${d.label ? ` · ${escapeHtml(d.label)}` : ''}</div>
                ${d.size ? `<div class="svc-drive-size">${escapeHtml(d.size)}</div>` : ''}
            </article>`).join('')}</div>`;
        return renderServiceSection(SVC_ICONS.drives, 'Drives', body, `${list.length} volume${list.length === 1 ? '' : 's'}`);
    };

    const renderServiceRows = (services) => {
        const list = Array.isArray(services) ? services : [];
        if (!list.length) {
            return renderServiceSection(SVC_ICONS.services, 'Services', '<p class="svc-empty">No services returned.</p>');
        }
        const body = list.map(s => {
            const bits = [];
            if (s.startType) bits.push(s.startType);
            if (s.startedAt) bits.push(`Started ${s.startedAt}`);
            if (s.pid) bits.push(`PID ${s.pid}${s.sharedHost ? ' · shared' : ''}`);
            return `
                <div class="svc-row">
                    <div class="svc-row-main">
                        <span class="svc-name">${escapeHtml(s.name)}</span>
                        <span class="svc-display">${escapeHtml(s.display)}</span>
                    </div>
                    ${bits.length ? `<div class="svc-info">${escapeHtml(bits.join(' · '))}</div>` : ''}
                    <span class="svc-status ${escapeHtml(s.severity || '')}">${escapeHtml(s.status || 'Unknown')}</span>
                </div>`;
        }).join('');
        const running = list.filter(s => s.status === 'Running').length;
        return renderServiceSection(SVC_ICONS.services, 'Services', body, `${running}/${list.length} running`);
    };

    const renderEventSection = (events) => {
        if (!events || events.skipped) {
            return renderServiceSection(
                SVC_ICONS.events,
                'Event logs & USN',
                `<div class="svc-skip">${ICON.lock}<p>Skipped without Administrator. Restart elevated for log clears, USN integrity, clock changes and shutdowns.</p></div>`
            );
        }

        const rows = [];
        (events.clears || []).forEach(c => {
            rows.push(svcSignal(
                c.severity || (c.detected ? 'bad' : 'ok'),
                c.key,
                c.detected ? `Cleared at ${c.when}` : 'Not detected'
            ));
        });
        (events.usn || []).forEach(u => {
            const label = u.volume ? `USN Journal ${u.volume}:` : 'USN Journal';
            const value = u.when ? `${u.state} at ${u.when}` : (u.state || 'Unknown');
            rows.push(svcSignal(u.severity, label, value));
        });
        rows.push(svcSignal('', 'Last shutdown', events.lastShutdown || 'Not detected'));
        rows.push(svcSignal(
            events.unexpectedShutdown ? 'bad' : 'ok',
            'Unexpected shutdown this session',
            events.unexpectedShutdown ? `Detected at ${events.unexpectedShutdownAt}` : 'Not detected'
        ));
        rows.push(svcSignal(
            events.clockChanged ? 'bad' : 'ok',
            'System clock change this session',
            events.clockChanged ? `Detected at ${events.clockChangedAt}` : 'Not detected'
        ));
        if (events.eventLogStartAt) {
            rows.push(svcSignal(
                events.eventLogRestarted ? 'bad' : 'ok',
                'Event Log service start',
                `${events.eventLogStartAt}${events.eventLogStartNote ? ` · ${events.eventLogStartNote}` : ''}`
            ));
        } else {
            rows.push(svcSignal('warn', 'Event Log service start', 'Not detected'));
        }
        rows.push(svcSignal('', 'Last device change this session', events.lastDeviceChange || 'Not detected'));

        const hits = (events.clears || []).filter(c => c.detected).length
            + (events.usn || []).filter(u => u.severity === 'bad').length
            + (events.unexpectedShutdown ? 1 : 0)
            + (events.clockChanged ? 1 : 0)
            + (events.eventLogRestarted ? 1 : 0);
        return renderServiceSection(SVC_ICONS.events, 'Event logs & USN', rows.join(''), hits ? `${hits} finding${hits === 1 ? '' : 's'}` : 'Clean');
    };

    const renderRecycleSection = (recycle) => {
        if (!recycle || recycle.skipped) {
            return renderServiceSection(
                SVC_ICONS.recycle,
                'Recycle Bin',
                `<div class="svc-skip">${ICON.lock}<p>Skipped without Administrator.</p></div>`
            );
        }
        const stats = `
            <div class="svc-mini-stats">
                <div class="svc-mini"><span>${recycle.volumes ?? 0}</span>volumes</div>
                <div class="svc-mini"><span>${recycle.items ?? 0}</span>items</div>
                <div class="svc-mini"><span>${escapeHtml(recycle.totalSizeLabel || '0 B')}</span>size</div>
                <div class="svc-mini ${recycle.deletedThisSession ? 'warn' : ''}"><span>${recycle.deletedThisSession ?? 0}</span>this session</div>
            </div>`;
        const rows = [];
        if (recycle.newestAt) {
            rows.push(svcSignal('', 'Most recently deleted', `${recycle.newestAt}${recycle.newestPath ? ` · ${recycle.newestPath}` : ''}`));
        }
        if (recycle.oldestAt) {
            rows.push(svcSignal('', 'Oldest deleted item', `${recycle.oldestAt}${recycle.oldestPath ? ` · ${recycle.oldestPath}` : ''}`));
        }
        if (recycle.inaccessible) {
            rows.push(svcSignal('warn', 'Inaccessible items', String(recycle.inaccessible)));
        }
        if (!recycle.items && !recycle.inaccessible) {
            rows.push(svcSignal('ok', 'Status', 'Recycle Bin is empty'));
        }
        return renderServiceSection(SVC_ICONS.recycle, 'Recycle Bin', stats + rows.join(''), recycle.items ? `${recycle.items} item${recycle.items === 1 ? '' : 's'}` : 'Empty');
    };

    const renderServiceCheck = (container, data) => {
        const findings = Number(data.findingCount) || 0;
        const services = Array.isArray(data.services) ? data.services : [];
        const running = services.filter(s => s.status === 'Running').length;
        const boot = data.boot || {};

        const banner = data.admin ? '' : `
            <div class="svc-banner">
                ${ICON.lock}
                <div class="svc-banner-copy">
                    <strong>Limited scan</strong>
                    <p>Event logs, USN journal and Recycle Bin need Administrator.</p>
                </div>
                <button type="button" class="pf-admin-btn" id="svcRelaunchAdmin">Restart as Administrator</button>
            </div>`;

        const summary = `
            <div class="svc-summary">
                <div class="svc-stat ${findings ? 'bad' : 'ok'}">
                    <span class="svc-stat-value">${findings}</span>
                    <span class="svc-stat-label">Findings</span>
                </div>
                <div class="svc-stat">
                    <span class="svc-stat-value">${running}<small>/${services.length || 0}</small></span>
                    <span class="svc-stat-label">Services up</span>
                </div>
                <div class="svc-stat">
                    <span class="svc-stat-value">${escapeHtml(boot.uptime || '—')}</span>
                    <span class="svc-stat-label">Uptime</span>
                </div>
                <div class="svc-stat ${data.admin ? 'ok' : 'warn'}">
                    <span class="svc-stat-value">${data.admin ? 'Admin' : 'User'}</span>
                    <span class="svc-stat-label">Access</span>
                </div>
            </div>`;

        container.innerHTML = banner + summary
            + renderBootSection(boot)
            + renderDriveSection(data.drives)
            + renderServiceRows(services)
            + renderEventSection(data.events)
            + renderRecycleSection(data.recycle);

        revealSections(container);

        const relaunchBtn = container.querySelector('#svcRelaunchAdmin');
        bindOnce(relaunchBtn, async () => {
            await window.pywebview.api.relaunch_as_admin();
        }, 'tool');
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

    const revealAltSections = (container) => {
        container.querySelectorAll('.service-section, .svc-summary, .svc-banner').forEach((el, i) => {
            setTimeout(() => el.classList.add('revealed'), 70 * i);
        });
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

        revealAltSections(container);
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
        const text = lines.join('\n');
        const done = (ok) => flashCopied(btn, ok ? `Copied ${lines.length} ${label}` : 'Copy failed');
        if (navigator.clipboard?.writeText) {
            navigator.clipboard.writeText(text).then(() => done(true), () => fallbackCopy(text, done));
        } else {
            fallbackCopy(text, done);
        }
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
        const payload = {
            scanDate: new Date().toISOString(),
            minecraftAccounts: altResult.minecraftAccounts || [],
            discordIds: dc.map(a => a.id),
            discordAccounts: dc,
            launcherFilesScanned: altResult.launcherFilesScanned || 0,
            logFilesScanned: altResult.logFilesScanned || 0,
            discordDirectoriesScanned: altResult.discordDirectoriesScanned || 0,
            browserDirectoriesScanned: altResult.browserDirectoriesScanned || 0
        };
        const blob = new Blob([JSON.stringify(payload, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `AltDetection_${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 5000);
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
        wrap.innerHTML = renderPfTable();
        const meta = wrap.closest('.service-section')?.querySelector('.svc-section-meta');
        if (meta) {
            const n = pfVisibleEntries().length;
            meta.textContent = `${n} shown`;
        }
    };

    const renderPfTable = () => {
        const rows = pfVisibleEntries();
        if (!rows.length) {
            return '<p class="svc-empty">No prefetch entries match the current search and filters.</p>';
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
        return `
        <div class="pf-table" role="table" aria-label="Prefetch entries">
            <div class="pf-head" role="row">
                ${th('path', 'Binary', false)}
                ${th('time', 'Last exec', false)}
                ${th('signed', 'Signature', false)}
                ${th('present', 'Present', true)}
                ${th('rules', 'Generics', false)}
            </div>
            ${body}
        </div>`;
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
            await window.pywebview.api.relaunch_as_admin();
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
                copyPfLines(flagged, copyBtn, 'items');
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
            paintPfTable();
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
                copyPfLines(files, copyBtn, 'paths');
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
                copyPfLines(lines, bamCopyBtn, 'items');
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

    const copyPfLines = (lines, btn, label) => {
        if (!lines.length) {
            flashCopied(btn, 'Nothing to copy');
            return;
        }
        const text = lines.join('\n');
        const done = (ok) => flashCopied(btn, ok ? `Copied ${lines.length} ${label}` : 'Copy failed');
        if (navigator.clipboard?.writeText) {
            navigator.clipboard.writeText(text).then(() => done(true), () => fallbackCopy(text, done));
        } else {
            fallbackCopy(text, done);
        }
    };

    const exportPfResult = () => {
        if (!pfResult) return;
        const blob = new Blob([JSON.stringify(pfResult, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `Prefetch_${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 5000);
    };

    document.addEventListener('visibilitychange', () => {
        document.documentElement.classList.toggle('bg-paused', document.hidden);
    });
    document.documentElement.classList.toggle('bg-paused', document.hidden);

    ui.toolCards.forEach(card => {
        card.addEventListener('pointermove', (e) => {
            const r = card.getBoundingClientRect();
            card.style.setProperty('--mouse-x', (e.clientX - r.left) + 'px');
            card.style.setProperty('--mouse-y', (e.clientY - r.top) + 'px');
        }, { passive: true });

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
            } else if (ui.serviceBackBtn && $('service-screen').classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.altBackBtn && $('alt-screen').classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.pfBackBtn && $('prefetch-screen').classList.contains('active')) {
                closePfModal();
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.bamBackBtn && $('bam-screen').classList.contains('active')) {
                closePfModal();
                stopCurrentScan();
                showScreen('main-screen');
            }
        }
    });

    $$('.island-brand, .footer-text, .rank-badge').forEach(el => {
        el.addEventListener('mouseenter', () => SoundFX.softHover());
    });

    bindAltToolbar();
    bindPfModal();

    bindAltToolbar();
    bindPfModal();

    /* ================= BAM Parser ================= */

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
        wrap.innerHTML = renderBamTable();
        const meta = wrap.closest('.service-section')?.querySelector('.svc-section-meta');
        if (meta) {
            const n = bamVisibleEntries().length;
            const skipped = bamResult?.drivesSkipped || 0;
            meta.textContent = `${n} shown${skipped ? ` · ${skipped} USN drive${skipped === 1 ? '' : 's'} skipped` : ''}`;
        }
    };

    const renderBamTable = () => {
        const rows = bamVisibleEntries();
        if (!rows.length) {
            return '<p class="svc-empty">No BAM entries match the current search and filters.</p>';
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
        return `
        <div class="pf-table" role="table" aria-label="BAM entries">
            <div class="pf-head bam4" role="row">
                ${th('path', 'Binary')}
                ${th('time', 'Last exec')}
                ${th('signed', 'Signature')}
                ${th('rules', 'Generics')}
            </div>
            ${body}
        </div>`;
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
            await window.pywebview.api.relaunch_as_admin();
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
                copyPfLines(flagged, copyBtn, 'items');
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
            paintBamTable();
        });
    };

    const exportBamResult = () => {
        if (!bamResult) return;
        const blob = new Blob([JSON.stringify(bamResult, null, 2)], { type: 'application/json' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = `BAM_${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(() => URL.revokeObjectURL(url), 5000);
    };

    /* ================= BAM Parser end ================= */

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
