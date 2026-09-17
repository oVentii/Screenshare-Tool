/* ==========================================================================
   iRis Screenshare Tool — frontend logic
   Rewritten: modular, dependency-free, fixes leftover BAM references.
   Talks to the .NET host through window.pywebview.api.*
   ========================================================================== */
(function () {
    'use strict';

    /* ---------------------------------------------------------------- *
     *  SoundFX — tiny WebAudio synth (no audio files needed)
     * ---------------------------------------------------------------- */
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

        function noise(c, vol, duration, start = 0) {
            const size = Math.floor(c.sampleRate * duration);
            const buffer = c.createBuffer(1, size, c.sampleRate);
            const data = buffer.getChannelData(0);
            for (let i = 0; i < size; i++) {
                data[i] = (Math.random() * 2 - 1) * Math.pow(1 - i / size, 1.8);
            }
            const src = c.createBufferSource();
            const gain = c.createGain();
            src.buffer = buffer;
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

    /* ---------------------------------------------------------------- *
     *  Shared helpers
     * ---------------------------------------------------------------- */
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
        const rect = btn.getBoundingClientRect();
        const size = Math.max(rect.width, rect.height) * 1.4;
        const el = document.createElement('span');
        el.className = 'ripple';
        el.style.width = size + 'px';
        el.style.height = size + 'px';
        el.style.left = (x - rect.left - size / 2) + 'px';
        el.style.top = (y - rect.top - size / 2) + 'px';
        btn.style.position = btn.style.position || 'relative';
        btn.appendChild(el);
        el.addEventListener('animationend', () => el.remove());
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

    // Aborts whatever scan is currently running on the backend. Called when the
    // user leaves a tool screen (back / Escape) so heavy disk work — Prefetch
    // parsing, USN recovery, raw-hive reads — stops immediately instead of
    // churning in the background unseen.
    const stopCurrentScan = () => {
        try { window.pywebview.api.cancel_scan(); } catch (e) { /* bridge not ready */ }
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

    const riskClass = (score) => score >= 70 ? 'bad' : score >= 40 ? 'warn' : 'ok';

    /* App-icon loading state shared by every tool: the app logo while a
       scan runs. */
    const loadingState = (msg, sub) => `
        <div class="scanning-state">
            <div class="app-loader" role="status" aria-label="Loading">
                <img src="giflogo.gif" class="app-loader-logo" alt="">
            </div>
            <p>${escapeHtml(msg)}</p>
            ${sub ? `<p class="scan-msg">${escapeHtml(sub)}</p>` : ''}
        </div>`;

    /* ---------------------------------------------------------------- *
     *  Theme & mute
     * ---------------------------------------------------------------- */
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

    /* ---------------------------------------------------------------- *
     *  Generic modal
     * ---------------------------------------------------------------- */
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
        prefetchBackBtn: $('prefetchBackBtn'),
        pfReload: $('pfReload'),
        pfMissing: $('pfMissing'),
        pfExport: $('pfExport'),
        pfToolbar: $('pfToolbar'),
        altBackBtn: $('altBackBtn'),
        altRescanBtn: $('altRescanBtn'),
        altClearBtn: $('altClearBtn'),
        altExportBtn: $('altExportBtn'),
        bamBackBtn: $('bamBackBtn'),
        bamRescanBtn: $('bamRescanBtn'),
        bamToolbar: $('bamToolbar'),
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

    /* ---------------------------------------------------------------- *
     *  Mouse glow + card spotlight
     * ---------------------------------------------------------------- */
    if (ui.mouseGlow && !document.documentElement.classList.contains('perf-low')) {
        document.addEventListener('mousemove', (e) => {
            ui.mouseGlow.style.left = e.clientX + 'px';
            ui.mouseGlow.style.top = e.clientY + 'px';
            ui.mouseGlow.style.opacity = '1';
        }, { passive: true });
        document.addEventListener('mouseleave', () => {
            ui.mouseGlow.style.opacity = '0';
        }, { passive: true });
    }

    document.addEventListener('mousemove', e => {
        $$('.tool-card').forEach(c => {
            const r = c.getBoundingClientRect();
            c.style.setProperty('--mx', ((e.clientX - r.left) / r.width * 100) + '%');
            c.style.setProperty('--my', ((e.clientY - r.top) / r.height * 100) + '%');
        });
    }, { passive: true });

    /* ================================================================ *
     *  SERVICE CHECKER
     * ================================================================ */
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
        // Re-entering the tool shows the last scan instead of rescanning,
        // so navigating away mid-run never wastes a finished result.
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

    /* ================================================================ *
     *  PREFETCH PARSER
     * ================================================================ */
    const SIG_LABELS = { 0: 'Signed', 1: 'Unsigned', 2: 'Not Found', 3: 'Cheat', 4: 'Fake', 5: 'NotMZ' };
    const SIG_CLASS = { 0: 'signed', 1: 'unsigned', 2: 'missing', 3: 'cheat', 4: 'fake', 5: 'notmz' };
    const PRESENCE_LABELS = { 0: '', 1: 'Missing file', 2: 'Unresolved path', 3: 'Deleted .pf', 4: 'Leftover (no .pf)' };
    const PRESENCE_SHORT = { 1: 'Missing', 2: 'Unresolved', 3: 'Deleted PF', 4: 'Ghost' };
    const PRESENCE_CLASS = { 1: 'missing', 2: 'missing', 3: 'deleted', 4: 'ghost' };

    const isCompactCard = (e) =>
        !!(e.fileMissing || Number(e.presenceKind) > 0 || Number(e.mainSignatureStatus) === 2);

    const sigBadge = (status) => {
        const s = Number(status);
        return `<span class="pf-badge ${SIG_CLASS[s] ?? 'warn'}">${SIG_LABELS[s] ?? 'Unknown'}</span>`;
    };

    const presenceBadge = (e, short) => {
        const k = Number(e.presenceKind);
        if (!k) return '';
        const label = short ? (PRESENCE_SHORT[k] || 'Missing') : (PRESENCE_LABELS[k] || 'Missing');
        return `<span class="pf-badge ${PRESENCE_CLASS[k] ?? 'missing'}">${label}</span>`;
    };

    const entryTone = (e) => {
        const k = Number(e.presenceKind);
        if (k === 4) return 'ghost';
        if (k === 3 || e.wasDeleted) return 'deleted';
        if (k === 1 || k === 2 || e.fileMissing) return 'missing';
        const s = Number(e.mainSignatureStatus);
        if (s === 3 || s === 4) return 'cheat';
        if (s === 1 || s === 5) return 'unsigned';
        return '';
    };

    const UNTRUSTED_SIGS = new Set([1, 3, 4, 5]);

    let pfResult = null;
    let pfBusy = false;
    let pfState = { search: '', untrusted: false, risky: false, postlogon: false, yara: false, multi: false };
    let pfDisplayed = [];
    let pfMissingKind = 'all';
    let pfMissingSearch = '';
    let pfMissingPrevFocus = null;
    let pfMissingDisplayed = [];

    const setPfBusy = (busy) => {
        pfBusy = busy;
        const ready = !busy && !!pfResult && !pfResult.requiresAdmin;
        if (ui.pfReload) {
            ui.pfReload.disabled = busy;
            ui.pfReload.hidden = busy;
        }
        if (ui.pfMissing) {
            ui.pfMissing.disabled = busy;
            ui.pfMissing.hidden = !ready;
        }
        if (ui.pfExport) {
            ui.pfExport.disabled = busy || !ready;
            ui.pfExport.hidden = !ready;
        }
        if (ui.pfToolbar) ui.pfToolbar.hidden = !ready;
    };

    const pfBanner = (title, sub, tone = 'warn') => `
        <div class="svc-banner${tone === 'info' ? ' info' : ''}">
            ${ICON.warning}
            <div class="svc-banner-copy">
                <strong>${title}</strong>
                <p>${sub}</p>
            </div>
        </div>`;

    const renderPrefetchSummary = (r) => {
        const findings = r.highRisk || 0;
        const missing = (r.notFound || 0) + (r.deletedCount || 0);
        const leftovers = r.ghostCount || 0;
        const shown = r.entries ? r.entries.length : 0;
        const banners = [];
        if (r.error && !r.requiresAdmin && r.error !== 'Scan cancelled.') {
            banners.push(pfBanner('Scan warning', escapeHtml(r.error), 'info'));
        }
        if (r.parseFailures > 0) {
            banners.push(pfBanner('Parse failures', `${r.parseFailures} Prefetch file${r.parseFailures === 1 ? '' : 's'} could not be parsed.`, 'info'));
        }
        if (r.deletedCount > 0) {
            banners.push(pfBanner(
                `${r.deletedCount} deleted Prefetch ${r.deletedCount === 1 ? 'entry' : 'entries'} recovered`,
                'Pulled from the NTFS USN journal by file reference — hiding a .pf no longer hides execution.',
                'info'
            ));
        }
        if (r.usnJournalRecreated) {
            banners.push(pfBanner('USN Journal recreated after boot', 'The change journal was created after boot — likely deleted and rebuilt to hide .pf activity.'));
        }
        if (r.evidenceGap) {
            banners.push(pfBanner('Execution artifacts missing', 'Prefetch shows programs have run, but ShimCache and Amcache are both empty — a common evidence-wipe fingerprint.'));
        }
        if (r.usnJournalDisabled) {
            banners.push(pfBanner('USN Journal disabled this session', 'Deleted Prefetch recovery and wipe correlation will be incomplete until the journal is recreated.'));
        }
        if (r.eventLogCleared && r.eventLogClears && r.eventLogClears.length) {
            banners.push(pfBanner(
                'Event log cleared this session',
                escapeHtml(r.eventLogClears.map(c => (c.label || '') + (c.when ? ' at ' + c.when : '')).join(' · ')) + '.'
            ));
        }
        if (r.ghostCount > 0) {
            banners.push(pfBanner(
                `${r.ghostCount} leftover execution${r.ghostCount === 1 ? '' : 's'} with no Prefetch`,
                'Listed in ShimCache, Amcache with no matching .pf. Open Missing to review them.',
                'info'
            ));
        }
        if (r.timestompCount > 0) {
            banners.push(pfBanner(
                `${r.timestompCount} possible timestomped PE${r.timestompCount === 1 ? '' : 's'}`,
                'Last-write time is in the future, pre-1995, or a round unsigned drop in a suspicious folder.'
            ));
        }
        if (r.fakeSig > 0) {
            banners.push(pfBanner(
                `${r.fakeSig} fake or hash-mismatched signature${r.fakeSig === 1 ? '' : 's'}`,
                'Authenticode is present but untrusted, revoked, or the digest does not match.'
            ));
        }

        return `
            <div class="svc-summary">
                <div class="svc-stat">
                    <span class="svc-stat-value">${r.total || 0}<small>/${shown}</small></span>
                    <span class="svc-stat-label">Prefetch</span>
                </div>
                ${r.scanSeconds ? `<div class="svc-stat"><span class="svc-stat-value">${r.scanSeconds < 10 ? r.scanSeconds.toFixed(1) : Math.round(r.scanSeconds)}<small>s</small></span><span class="svc-stat-label">Scan time</span></div>` : ''}
                <div class="svc-stat ${findings ? 'bad' : 'ok'}">
                    <span class="svc-stat-value">${findings}</span>
                    <span class="svc-stat-label">Findings</span>
                </div>
                <div class="svc-stat ${missing ? 'warn' : ''}">
                    <span class="svc-stat-value">${missing}</span>
                    <span class="svc-stat-label">Missing</span>
                </div>
                <div class="svc-stat ${leftovers ? 'bad' : ''}">
                    <span class="svc-stat-value">${leftovers}</span>
                    <span class="svc-stat-label">Leftovers</span>
                </div>
            </div>
            ${banners.join('')}`;
    };

    const renderPfVersion = (e) => {
        const parts = [];
        if (e.fileDescription) parts.push(escapeHtml(e.fileDescription));
        if (e.productName) parts.push(escapeHtml(e.productName));
        if (e.companyName) parts.push(escapeHtml(e.companyName));
        if (e.fileVersion) parts.push('v' + escapeHtml(e.fileVersion));
        if (!parts.length) return '';
        return `<div class="pf-row pf-ver-row"><span class="pf-key">Version</span><span class="pf-val">${parts.join(' &middot; ')}</span></div>`;
    };

    const YARA_META = (() => {
        const DEFS = [
            ['KNOWN_CHEAT_HASH',          'hash',    5, 'Exact known-cheat hash match (non-bypassable)'],
            ['DYNAMIC_API_RESOLUTION',    'deep',    4, 'Import table stripped; APIs resolved at runtime'],
            ['NO_IMPORT_PE',              'deep',    4, 'Executable image with no import directory'],
            ['PE_OVERLAY_PAYLOAD',        'deep',    4, 'High-entropy payload appended past last section'],
            ['PACKED_CODE_SECTION',       'deep',    4, 'Compressed / high-entropy executable code section'],
            ['VIRTUALIZATION_DEBUGGING',  'deep',    4, 'Anti-debug primitives + VM/sandbox evasion'],
            ['ENCODED_CHEAT_STRING',      'deep',    4, 'Known cheat brand present only in obfuscated form'],
            ['KNOWN_CHEAT_NAME',          'deep',    4, 'File path matches a known cheat/injector artifact'],
            ['NTDLL_UNHOOK',              'deep',    4, 'ntdll unhooking / direct-syscall evasion'],
            ['LOW_LEVEL_INPUT_HOOK',      'clicker', 3, 'Low-level keyboard/mouse hook for input forging'],
            ['AIMBOT_HINTS',              'aim',     3, 'SetCursorPos + input polling + aiming vocabulary'],
            ['PE_RWX_SECTION',            'deep',    3, 'Executable section is writable (RWX)'],
            ['INJECTOR_API',              'inject',  3, 'Process-injection API combination'],
            ['PE_INJECT_COMBO',           'inject',  3, 'Open/read/write/virtual-alloc remote-process combo'],
            ['MANUAL_MAP_HINTS',          'inject',  3, 'Manual-mapping / reflective-loader markers'],
            ['THREAD_HIJACK_HINTS',       'inject',  3, 'Thread suspension + context manipulation'],
            ['MEMORY_MODULE_HINTS',       'inject',  3, 'Memory-module / reflective loader strings'],
            ['GAME_OVERLAY_ABUSE',        'inject',  3, 'Game-overlay hijacked to inject/bypass'],
            ['KDMAPPER_LIKE',             'mapper',  3, 'Known vulnerable-driver / mapper markers'],
            ['DRIVER_LOAD_ABUSE',         'mapper',  3, 'Service/driver-loading abuse'],
            ['TOKEN_STEAL_PRIV',          'mapper',  3, 'Token-stealing privilege adjustment'],
            ['NTDLL_UNDOCUMENTED',        'mapper',  3, 'Undocumented ntdll usage + open-process'],
            ['AUTOCLICKER',               'clicker', 2, 'Autoclicker UI/markers'],
            ['CSHARP_CLICKER',            'clicker', 2, '.NET autoclicker heuristics'],
            ['CLICK_INPUT_COMBO',         'clicker', 2, 'Mouse/input injection combo'],
            ['NULL_FORKED_RECOVERY',      'clicker', 2, 'Recovery-clicker marker'],
            ['HIGH_ENTROPY_UNSIGNED_PE',  'entropy', 2, 'Unsigned, very high-entropy image'],
            ['HIGH_ENTROPY_SECTION',      'entropy', 2, 'High-entropy section in unsigned image'],
            ['PE_HIGH_ENTROPY_NO_SIG_HINT', 'entropy', 2, 'High-entropy, unsigned, no signature hint'],
            ['STRING_CLEANER_PACKER',     'entropy', 2, 'Packer / string-cleaner markers'],
            ['CHEAT',                     'ioc',     1, 'Known cheat indicator string'],
            ['JAVA_AGENT_CHEAT',          'ioc',     1, 'Java agent / JVMTI cheat loader'],
            ['DLL_SIDELOAD_NAMES',        'ioc',     1, 'Sideloadable DLL name set'],
            ['SUSPICIOUS_MUTEX',          'ioc',     1, 'Known cheat mutex / event names'],
            ['PROCESS_DOPPELGANGING',     'inject',  4, 'Transacted-section process injection'],
            ['SCREENSHARE_EVASION',       'ioc',     4, 'Panic key / screenshare-hiding vocabulary'],
            ['NETWORK_C2',                'ioc',     3, 'Cheat CDN / C2 / webhook endpoints'],
            ['REGISTRY_PERSISTENCE',      'ioc',     3, 'Registry Run-key persistence'],
            ['WMI_PERSISTENCE',           'ioc',     3, 'WMI event-subscription persistence'],
            ['DOTNET_CHEAT_FRAMEWORK',    'ioc',     3, '.NET cheat framework / hooking libs'],
            ['KEYSTROKE_LOGGER',          'ioc',     3, 'Keystroke polling + window-title capture'],
            ['WINDOW_CAPTURE_EVASION',    'ioc',     3, 'SetWindowDisplayAffinity capture exclusion'],
            ['VEH_INJECTION_HINTS',       'inject',  3, 'VEH + thread-context injection pattern'],
            ['POWERSHELL_STAGER',         'ioc',     3, 'Encoded PowerShell download-and-execute'],
            ['ANTI_AMSI_ETW',             'ioc',     3, 'AMSI / ETW neutralization vocabulary'],
            ['DLL_SIDELOAD_TECHNIQUE',    'ioc',     2, 'Sideloadable DLL name outside OS dirs'],
            ['MC_ESP_HINTS',              'aim',     2, 'MC packet classes + ESP/wallhack vocabulary'],
        ];
        const map = {};
        for (const [n, c, s, l] of DEFS) map[n] = { cat: c, sev: s, label: l };
        return { get: (n) => map[n] || { cat: 'ioc', sev: 1, label: n } };
    })();

    const renderYaraTags = (rules) => {
        if (!rules || !rules.length) return '';
        const sorted = [...rules].sort((a, b) => YARA_META.get(b).sev - YARA_META.get(a).sev);
        const items = sorted.map(r => {
            const m = YARA_META.get(r);
            return `<span class="pf-yara ${m.cat}" data-sev="${m.sev}" title="${escapeHtml(m.label)}">` +
                   `<span class="pf-yara-dot"></span>${escapeHtml(r)}` +
                   `</span>`;
        }).join('');
        return `<div class="pf-yara-row"><span class="pf-key">YARA</span><div class="pf-yara-tags">${items}</div></div>`;
    };

    const renderPrefetchEntry = (e) => {
        const hot = Number(e.mainSignatureStatus) === 3 || Number(e.mainSignatureStatus) === 4;
        return `
            <article class="pf-entry ${entryTone(e)}${hot ? ' is-open' : ''}">
                <button type="button" class="pf-entry-toggle" aria-expanded="${hot ? 'true' : 'false'}">
                    <span class="svc-dot ${riskClass(e.riskScore)}"></span>
                    <div class="pf-entry-title">
                        <span class="pf-name">${escapeHtml(e.pfFileName)}</span>
                        <span class="pf-entry-path">${escapeHtml(e.mainExecutablePath || e.lastSeenPath || '')}</span>
                    </div>
                    ${presenceBadge(e, true)}${sigBadge(e.mainSignatureStatus)}
                    <span class="pf-score ${riskClass(e.riskScore)}">${e.riskScore}</span>
                </button>
                <div class="pf-entry-body">
                    ${e.riskSummary ? `<div class="pf-row"><span class="pf-key">Risk</span><span class="pf-val">${escapeHtml(e.riskSummary)}</span></div>` : ''}
                    <div class="pf-row"><span class="pf-key">Executable</span><span class="pf-val">${escapeHtml(e.mainExecutablePath)}</span>${presenceBadge(e, false)}${sigBadge(e.mainSignatureStatus)}</div>
                    ${e.signatureDetail && (e.fileMissing || Number(e.presenceKind) > 0 || Number(e.mainSignatureStatus) !== 0) ? `<div class="pf-row pf-detail-row"><span class="pf-key">Why</span><span class="pf-val">${escapeHtml(e.signatureDetail)}</span></div>` : ''}
                    ${e.lastSeenPath && e.lastSeenPath !== e.mainExecutablePath ? `<div class="pf-row"><span class="pf-key">Last seen</span><span class="pf-val">${escapeHtml(e.lastSeenPath)}</span></div>` : ''}
                    ${e.lastSeenSource || e.lastSeenTime ? `<div class="pf-row"><span class="pf-key">Evidence</span><span class="pf-val">${escapeHtml([e.lastSeenSource, e.lastSeenTime].filter(Boolean).join(' · '))}</span></div>` : ''}
                    ${e.amcachePublisher ? `<div class="pf-row"><span class="pf-key">Publisher</span><span class="pf-val">${escapeHtml(e.amcachePublisher)} <span class="pf-hint">(Amcache metadata, not Authenticode)</span></span></div>` : ''}
                    ${e.resolvedFrom ? `<div class="pf-row pf-ver-row"><span class="pf-key">Resolved</span><span class="pf-val">${escapeHtml(e.resolvedFrom)}</span></div>` : ''}
                    ${e.sha256 ? `<div class="pf-row pf-ver-row"><span class="pf-key">SHA-256</span><span class="pf-val">${escapeHtml(e.sha256)}</span></div>` : ''}
                    ${e.pfPath && String(e.pfPath).startsWith('(') ? `<div class="pf-row pf-ver-row"><span class="pf-key">.pf</span><span class="pf-val">${escapeHtml(e.pfPath)}</span></div>` : ''}
                    ${renderPfVersion(e)}
                    ${renderYaraTags(e.matchedRules)}
                    <div class="pf-meta">
                        <span class="pf-chip">v${e.version}</span>
                        <button type="button" class="pf-chip pf-runs-chip" data-runsidx="${e.__idx}" ${e.runCount > 0 ? 'title="View run history"' : ''}>Runs: ${e.runCount}</button>
                        ${e.lastExecutionTimes && e.lastExecutionTimes.length ? `<span class="pf-chip">Last: ${escapeHtml(e.lastExecutionTimes[0])}</span>` : ''}
                        ${e.inShimCache ? `<span class="pf-chip ok">In ShimCache</span>` : ''}
                        ${e.inAmcache ? `<span class="pf-chip ok">In Amcache</span>` : ''}
                        ${e.inBam ? `<span class="pf-chip ok">In BAM</span>` : ''}
                        ${e.timestomped ? `<span class="pf-chip bad">Timestomp</span>` : ''}
                        ${e.artifactLeftover ? `<span class="pf-chip bad">Artifact leftover</span>` : ''}
                        ${e.unsignedInMinecraftPath ? `<span class="pf-chip bad">Unsigned in MC path</span>` : ''}
                        ${e.unsignedReferenced >= 3 ? `<span class="pf-chip warn">${e.unsignedReferenced} unsigned refs</span>` : ''}
                        ${e.suspiciousReferenced > 0 ? `<span class="pf-chip bad">${e.suspiciousReferenced} suspicious refs</span>` : ''}
                        ${e.multiVolume ? `<span class="pf-chip warn">Multi-volume</span>` : ''}
                        ${e.wasDeleted ? `<span class="pf-chip deleted" title=".pf was deleted; execution recovered from USN">DELETED (recovered)</span>` : ''}
                        ${Number(e.presenceKind) === 4 ? `<span class="pf-chip ghost">Ghost leftover</span>` : ''}
                        ${e.integrityMismatch ? `<span class="pf-chip bad" title="Referenced-file count does not match file-metrics — the .pf may have been rebuilt">Integrity mismatch</span>` : ''}
                        ${e.directoryCount ? `<span class="pf-chip">${e.directoryCount} dirs</span>` : ''}
                        ${e.volumeCount ? `<span class="pf-chip">${e.volumeCount} vol${e.volumeCount > 1 ? 's' : ''}</span>` : ''}
                        ${e.isHidden || e.isSystem ? `<span class="pf-chip warn" title="File attributes tampered">Hidden/System</span>` : ''}
                        ${e.isReadOnly ? `<span class="pf-chip warn" title="File set read-only">Read-only</span>` : ''}
                        ${e.duplicateOf ? `<span class="pf-chip bad" title="Identical content to: ${escapeHtml(e.duplicateOf)}">Duplicate</span>` : ''}
                        ${e.renamedFile ? `<span class="pf-chip bad" title="Header exe name (${escapeHtml(e.headerExeName || '')}) differs from the .pf filename — the .pf was renamed after creation">Renamed .pf</span>` : ''}
                        ${e.prefetchHashMismatch ? `<span class="pf-chip bad" title="Stored prefetch hash (0x4C) does not match the hash string Windows hashed (${escapeHtml(e.hashString || 'n/a')}) — the .pf header or filename was altered after creation">Hash mismatch</span>` : ''}
                        ${e.isBootPrefetch ? `<span class="pf-chip" title="Boot-time prefetch entry (OS-generated, not user activity)">Boot</span>` : ''}
                        ${e.executableLoadedRefs > 0 ? `<span class="pf-chip warn" title="${e.executableLoadedRefs} referenced file(s) outside OS dirs were loaded as executable images with invalid signatures — possible injection marker">${e.executableLoadedRefs} exec-loaded ref</span>` : ''}
                        ${e.traceChainCount ? `<span class="pf-chip" title="${e.traceChainCount} trace chains · ${Number(e.totalBlockLoads || 0).toLocaleString()} total block loads · max chain depth ${e.maxChainDepth || 0}">${e.traceChainCount} chains</span>` : ''}
                        ${e.directoryNames && e.directoryNames.length ? `<span class="pf-chip" title="${escapeHtml(e.directoryNames.join(' · '))}">${e.directoryNames.length} dir${e.directoryNames.length > 1 ? 's' : ''}</span>` : ''}
                    </div>
                    ${hasReferencedFiles(e) ? `<button type="button" class="pf-ref-btn" data-refidx="${e.__idx}">Referenced files (${referencedFileCount(e)})</button>` : ''}
                </div>
            </article>`;
    };

    const renderCompactPrefetchEntry = (e) => {
        const path = e.lastSeenPath || e.mainExecutablePath || e.pfPath || '';
        const when = e.lastSeenTime || (e.lastExecutionTimes && e.lastExecutionTimes[0]) || '';
        const src = e.lastSeenSource || '';
        const meta = [src, when].filter(Boolean).join(' · ');
        return `
            <section class="pf-entry pf-compact ${entryTone(e)}" data-missidx="${e.__missidx}" tabindex="0" role="button">
                <div class="pf-compact-line">
                    <span class="pf-compact-name" title="${escapeHtml(e.pfFileName || '')}">${escapeHtml(e.pfFileName || '')}</span>
                    ${e.renamedFile ? `<span class="pf-chip bad" title="Header exe name (${escapeHtml(e.headerExeName || '')}) differs from the filename — renamed after creation">Renamed</span>` : ''}
                    ${presenceBadge(e, true)}${sigBadge(e.mainSignatureStatus)}
                    <span class="pf-score pf-compact-score ${riskClass(e.riskScore)}">${e.riskScore}</span>
                </div>
                <div class="pf-compact-line pf-compact-sub">
                    <span class="pf-compact-path" title="${escapeHtml(path)}">${escapeHtml(path)}</span>
                    ${meta ? `<span class="pf-compact-meta" title="${escapeHtml(meta)}">${escapeHtml(meta)}</span>` : ''}
                </div>
            </section>`;
    };

    const getFilteredEntries = () => {
        if (!pfResult) return [];
        const q = pfState.search.toLowerCase();
        const isUntrusted = (s) => UNTRUSTED_SIGS.has(Number(s));
        let base = pfResult.entries || [];
        if (pfResult.recoveredDeleted && pfResult.recoveredDeleted.length)
            base = base.concat(pfResult.recoveredDeleted);
        if (pfResult.ghostEntries && pfResult.ghostEntries.length)
            base = base.concat(pfResult.ghostEntries);

        let list = base.filter(e => {
            if (isCompactCard(e)) return false;
            if (pfState.risky && e.riskScore < 50) return false;
            if (pfState.untrusted && !isUntrusted(e.mainSignatureStatus)) return false;
            if (pfState.postlogon && !((e.lastExecUnix || e.firstExecUnix || 0) > pfResult.logonTime)) return false;
            if (pfState.yara && !(e.matchedRules && e.matchedRules.length)) return false;
            if (pfState.multi && !(e.riskFlags && (e.riskFlags & 0x800))) return false;

            if (q) {
                const hay = [
                    e.pfFileName, e.mainExecutablePath, e.lastSeenPath, e.pfPath,
                    e.signatureDetail, e.amcachePublisher, e.resolvedFrom,
                    SIG_LABELS[Number(e.mainSignatureStatus)] || '',
                    PRESENCE_LABELS[Number(e.presenceKind)] || '',
                    e.riskSummary || '', e.sha256 || '',
                    (e.matchedRules && e.matchedRules.length) ? e.matchedRules.join(' ') : ''
                ].join(' ').toLowerCase();
                if (!hay.includes(q)) return false;
            }
            return true;
        });

        const sigRank = (s) => {
            s = Number(s);
            if (s === 5) return 5;
            if (s === 3 || s === 4) return 4;
            if (s === 1) return 3;
            if (s === 2) return 2;
            return 1;
        };
        list.sort((a, b) =>
            (sigRank(b.mainSignatureStatus) - sigRank(a.mainSignatureStatus)) ||
            ((b.riskScore || 0) - (a.riskScore || 0)) ||
            (a.pfFileName || '').localeCompare(b.pfFileName || ''));
        return list;
    };

    const collectMissingEntries = () => {
        if (!pfResult) return [];
        let base = pfResult.entries || [];
        if (pfResult.recoveredDeleted && pfResult.recoveredDeleted.length)
            base = base.concat(pfResult.recoveredDeleted);
        if (pfResult.ghostEntries && pfResult.ghostEntries.length)
            base = base.concat(pfResult.ghostEntries);
        return base.filter(isCompactCard);
    };

    const missingKindOf = (e) => {
        const k = Number(e.presenceKind);
        if (k === 4) return 'ghost';
        if (k === 3 || e.wasDeleted) return 'deleted';
        if (k === 2) return 'unresolved';
        return 'missing';
    };

    const updateMissingCount = () => {
        const n = collectMissingEntries().length;
        const el = $('pfMissingCount');
        if (!el) return;
        el.textContent = String(n);
        el.classList.toggle('is-empty', n === 0);
    };

    const renderMissingPopupList = () => {
        const body = $('pfMissingBody');
        if (!body) return;
        const q = pfMissingSearch.toLowerCase();
        let list = collectMissingEntries();
        if (pfMissingKind !== 'all')
            list = list.filter(e => missingKindOf(e) === pfMissingKind);
        if (q) {
            list = list.filter(e => {
                const hay = [
                    e.pfFileName, e.mainExecutablePath, e.lastSeenPath, e.pfPath,
                    e.signatureDetail, e.lastSeenSource, e.amcachePublisher
                ].join(' ').toLowerCase();
                return hay.includes(q);
            });
        }
        list.sort((a, b) =>
            ((b.riskScore || 0) - (a.riskScore || 0)) ||
            (a.pfFileName || '').localeCompare(b.pfFileName || ''));
        list.forEach((e, i) => { e.__missidx = i; });
        pfMissingDisplayed = list;
        if (!list.length) {
            pfMissingDisplayed = [];
            body.innerHTML = `<div class="scanning-state"><p>${collectMissingEntries().length ? 'No entries match this filter.' : 'No missing, not-found, or leftover Prefetch evidence.'}</p></div>`;
            return;
        }
        const groups = { missing: [], unresolved: [], deleted: [], ghost: [] };
        list.forEach(e => groups[missingKindOf(e)].push(e));
        const titles = {
            missing: 'Missing file',
            unresolved: 'Unresolved path',
            deleted: 'Deleted .pf',
            ghost: 'Leftover (no .pf)'
        };
        let html = '';
        for (const key of ['ghost', 'deleted', 'missing', 'unresolved']) {
            if (!groups[key].length) continue;
            html += `<div class="pf-compact-block">` +
                `<div class="pf-compact-head">${titles[key]} — ${groups[key].length}</div>` +
                `<div class="pf-compact-list">${groups[key].map(renderCompactPrefetchEntry).join('')}</div>` +
                `</div>`;
        }
        body.innerHTML = html;
        body.querySelectorAll('.pf-entry').forEach(el => el.classList.add('revealed'));
    };

    const openMissingModal = () => {
        const modal = $('pf-missing-modal');
        if (!modal) return;
        modal.classList.add('active');
        modal.setAttribute('aria-hidden', 'false');
        ui.pfMissing?.setAttribute('aria-expanded', 'true');
        pfMissingPrevFocus = document.activeElement;
        SoundFX.modalOpen();
        renderMissingPopupList();
        const search = $('pfMissingSearch');
        if (search) {
            search.value = pfMissingSearch;
            setTimeout(() => search.focus(), 30);
        }
    };

    const closeMissingModal = () => {
        const modal = $('pf-missing-modal');
        if (!modal) return;
        modal.classList.remove('active');
        modal.setAttribute('aria-hidden', 'true');
        ui.pfMissing?.setAttribute('aria-expanded', 'false');
        SoundFX.modalClose();
        if (pfMissingPrevFocus && typeof pfMissingPrevFocus.focus === 'function') {
            try { pfMissingPrevFocus.focus(); } catch { }
        }
        pfMissingPrevFocus = null;
    };

    const renderPrefetch = () => {
        const container = $('prefetch-results');
        const hasAny = pfResult && (
            (pfResult.entries && pfResult.entries.length) ||
            (pfResult.recoveredDeleted && pfResult.recoveredDeleted.length) ||
            (pfResult.ghostEntries && pfResult.ghostEntries.length) ||
            (pfResult.ghostArtifacts && pfResult.ghostArtifacts.length)
        );
        updateMissingCount();
        if (!hasAny) {
            container.innerHTML = `<div class="scanning-state"><p>No Prefetch files found.</p></div>`;
            return;
        }
        const list = getFilteredEntries();
        const displayResult = { ...pfResult, entries: list };
        if (!list.length) {
            container.innerHTML = renderPrefetchSummary(displayResult) +
                `<div class="scanning-state"><p>No on-disk Prefetch entries match the current filters. Open Missing for deleted, leftover, or not-found evidence.</p></div>`;
            revealSections(container);
            return;
        }
        pfDisplayed = list.map((e, i) => ({ ...e, __idx: i }));
        container.innerHTML = renderPrefetchSummary(displayResult) +
            `<section class="service-section">
                <div class="service-section-header">
                    <span class="service-section-icon"><svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.6"><circle cx="10.5" cy="10.5" r="6.5"/><path d="M20.5 20.5l-4-4"/></svg></span>
                    <h3>Executions</h3>
                    <span class="svc-section-meta">${list.length} shown</span>
                </div>
                <div class="service-section-body pf-list">${pfDisplayed.map(renderPrefetchEntry).join('')}</div>
            </section>`;
        revealSections(container);
        container.querySelectorAll('.pf-entry').forEach((el, i) => {
            setTimeout(() => el.classList.add('revealed'), Math.min(24 * i, 280));
        });
    };

    const bindPrefetchResults = () => {
        const container = $('prefetch-results');
        if (!container || container.dataset.pfBound === '1') return;
        container.dataset.pfBound = '1';
        container.addEventListener('click', (e) => {
            const toggle = e.target.closest('.pf-entry-toggle');
            if (toggle && container.contains(toggle)) {
                const card = toggle.closest('.pf-entry');
                if (!card) return;
                const open = card.classList.toggle('is-open');
                toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
                return;
            }
            const refBtn = e.target.closest('.pf-ref-btn');
            if (refBtn && container.contains(refBtn)) {
                SoundFX.tool();
                addRipple(refBtn, e.clientX, e.clientY);
                const entry = pfDisplayed[Number(refBtn.dataset.refidx)];
                if (entry) openReferencedFiles(entry);
                return;
            }
            const runChip = e.target.closest('.pf-runs-chip');
            if (runChip && container.contains(runChip)) {
                SoundFX.tool();
                addRipple(runChip, e.clientX, e.clientY);
                const entry = pfDisplayed[Number(runChip.dataset.runsidx)];
                if (entry) openRunsModal(entry);
            }
        });
    };

    const openPrefetchParser = async (force = false) => {
        showScreen('prefetch-screen');
        if (pfBusy) return;
        if (!force && pfResult) {
            renderPrefetch();
            return;
        }
        const container = $('prefetch-results');
        pfResult = null;
        setPfBusy(true);
        container.innerHTML = loadingState(
            'Parsing Prefetch files…',
            'Signatures, USN recovery, ShimCache, Amcache and YARA');

        try {
            const result = await window.pywebview.api.prefetch_parser_run();
            if (!result) throw new Error('No output returned.');

            const hasAny = (result.entries && result.entries.length)
                || (result.recoveredDeleted && result.recoveredDeleted.length)
                || (result.ghostEntries && result.ghostEntries.length)
                || (result.ghostArtifacts && result.ghostArtifacts.length);

            if (result.error && result.requiresAdmin) {
                container.innerHTML = `
                    <div class="scanning-state">
                        <p class="scan-msg">${ICON.lock} ${escapeHtml(result.error)}</p>
                        <button class="pf-admin-btn" id="pfRelaunchAdmin">
                            <svg viewBox="0 0 24 24" width="16" height="16" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round"><path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10Z"/></svg>
                            Restart as Administrator
                        </button>
                    </div>`;
                const btn = $('pfRelaunchAdmin');
                if (btn) {
                    btn.addEventListener('mouseenter', () => SoundFX.hover());
                    btn.addEventListener('click', async (e) => {
                        SoundFX.tool();
                        addRipple(btn, e.clientX, e.clientY);
                        await window.pywebview.api.relaunch_as_admin();
                    });
                }
                return;
            }

            if (result.error && !hasAny) {
                container.innerHTML = `
                    <div class="scanning-state">
                        <p>${escapeHtml(result.error)}</p>
                    </div>`;
                return;
            }

            pfResult = result;
            renderPrefetch();
        } catch (err) {
            container.innerHTML = isCancellation(err)
                ? '<div class="scanning-state"><p>Scan stopped.</p></div>'
                : `<div class="scanning-state"><p>Error running scan: ${escapeHtml(err.message || err)}</p></div>`;
        } finally {
            setPfBusy(false);
        }
    };

    /* --- Prefetch info modal --- */
    const showPfInfo = (title, iconHtml, bodyHtml) => {
        const modal = $('pf-info-modal');
        $('pfInfoIcon').innerHTML = iconHtml || '';
        $('pfInfoTitle').textContent = title;
        $('pfInfoBody').innerHTML = bodyHtml;
        modal.classList.add('active');
        modal.setAttribute('aria-hidden', 'false');
        SoundFX.modalOpen();
    };

    const closePfInfo = () => {
        const modal = $('pf-info-modal');
        modal.classList.remove('active');
        modal.setAttribute('aria-hidden', 'true');
        SoundFX.modalClose();
    };

    const renderUsnRows = (q, events) => {
        const unique = new Map();
        (events || []).forEach(e => {
            const key = `${e.action}|${e.oldName}|${e.newName}|${e.timestamp}|${e.reason}`;
            if (!unique.has(key)) unique.set(key, e);
        });
        const list = [...unique.values()].reverse();
        const rowHtml = (e) => `
            <div class="usn-row">
                <span class="usn-action ${e.isPrefetchDir ? 'dir' : ''}">${escapeHtml(e.action)}</span>
                <span class="usn-name">${escapeHtml(e.oldName || '')}${e.newName ? ' → ' + escapeHtml(e.newName) : ''}</span>
                <span class="usn-time">${escapeHtml(e.timestamp || '')}</span>
                ${e.reason ? `<span class="usn-reason" title="${escapeHtml(e.reason)}">${escapeHtml(e.reason)}</span>` : ''}
            </div>`;
        const query = (q || '').toLowerCase();
        const filtered = list.filter(e =>
            !query ||
            (e.action || '').toLowerCase().includes(query) ||
            (e.oldName || '').toLowerCase().includes(query) ||
            (e.newName || '').toLowerCase().includes(query) ||
            (e.timestamp || '').toLowerCase().includes(query) ||
            (e.reason || '').toLowerCase().includes(query));
        if (!filtered.length) return `<div class="scanning-state"><p>No matching USN events.</p></div>`;
        return filtered.map(rowHtml).join('');
    };

    const renderUsnModal = (payload) => {
        const events = Array.isArray(payload) ? payload : (payload && payload.events) || [];
        const error = Array.isArray(payload) ? null : (payload && payload.error);
        const truncated = !Array.isArray(payload) && !!payload?.truncated;
        if (error && !events.length) {
            return `<div class="scanning-state"><p>${escapeHtml(error)}</p></div>`;
        }
        if (!events.length) {
            return `<div class="scanning-state"><p>No Prefetch (.pf) delete, rename, or security activity since boot.</p></div>`;
        }
        const count = new Set((events || []).map(e => `${e.action}|${e.oldName}|${e.newName}|${e.timestamp}|${e.reason}`)).size;
        return `
            ${truncated ? `<p class="pf-warn-note">${ICON.warning}<span>Journal window was truncated — older events may be missing.</span></p>` : ''}
            <div class="pf-ref-toolbar">
                <input id="pfUsnSearch" class="pf-search" type="search" placeholder="Search USN events…" spellcheck="false" autocomplete="off">
                <span class="pf-ref-count">${count}</span>
            </div>
            <div class="usn-list pf-ref-list">${renderUsnRows('', events)}</div>`;
    };

    const renderSysMainModal = (sm) => {
        const val = (k) => escapeHtml(sm[k] != null ? String(sm[k]) : '-');
        return `
            <div class="pf-kv">
                <div class="kv-row"><span class="kv-key">Service</span><span class="kv-val">${escapeHtml(sm.serviceName || 'SysMain')}</span></div>
                <div class="kv-row"><span class="kv-key">Status</span><span class="kv-val ${sm.status === 'Running' ? 'ok' : 'warn'}">${val('status')}</span></div>
                <div class="kv-row"><span class="kv-key">PID</span><span class="kv-val">${val('pid')}</span></div>
                <div class="kv-row"><span class="kv-key">Uptime</span><span class="kv-val">${val('uptime')}</span></div>
                <div class="kv-row"><span class="kv-key">Logon Time</span><span class="kv-val">${val('logonTime')}</span></div>
                <div class="kv-row"><span class="kv-key">Started after logon</span><span class="kv-val ${sm.delayedStart ? 'bad' : 'ok'}">${sm.delayedStart ? 'Yes' : 'No'}</span></div>
            </div>
            ${sm.delayedStart ? `<p class="pf-warn-note">${ICON.warning}<span>SysMain started more than two minutes after user logon — a common sign of tampering.</span></p>` : ''}`;
    };

    const renderUsnStatusModal = (u) => {
        const val = (k) => escapeHtml(u[k] != null ? String(u[k]) : '-');
        const recreated = !!u.journalRecreated;
        const disabled = u.journalEnabled === false;
        const journalFile = u.journalFileExists ? 'Present' : (u.journalEnabled ? 'Unknown' : 'Missing');
        const journalFileClass = u.journalFileExists ? 'ok' : (u.journalEnabled ? '' : 'bad');
        return `
            <div class="pf-kv">
                <div class="kv-row"><span class="kv-key">Journal Enabled</span><span class="kv-val ${disabled ? 'bad' : 'ok'}">${u.journalEnabled ? 'Yes' : 'No'}</span></div>
                <div class="kv-row"><span class="kv-key">Journal File ($UsnJrnl)</span><span class="kv-val ${journalFileClass}">${journalFile}</span></div>
                <div class="kv-row"><span class="kv-key">Journal Created</span><span class="kv-val">${val('journalCreationTime')}</span></div>
                <div class="kv-row"><span class="kv-key">System Boot Time</span><span class="kv-val">${val('bootTime')}</span></div>
                <div class="kv-row"><span class="kv-key">Journal recreated after boot</span><span class="kv-val ${recreated ? 'bad' : 'ok'}">${recreated ? 'Yes' : 'No'}</span></div>
                ${u.journalId ? `<div class="kv-row"><span class="kv-key">Journal ID</span><span class="kv-val">${escapeHtml(String(u.journalId))}</span></div>` : ''}
                ${u.statusDetail ? `<div class="kv-row"><span class="kv-key">Detail</span><span class="kv-val">${val('statusDetail')}</span></div>` : ''}
            </div>
            ${recreated ? `<p class="pf-warn-note">${ICON.warning}<span>USN Journal was created after system boot — the journal was likely deleted and rebuilt to hide .pf deletions/renames.</span></p>` : ''}
            ${disabled || !u.journalEnabled ? `<p class="pf-warn-note">${ICON.warning}<span>USN Journal is disabled or inaccessible — prefetch tampering cannot be audited.</span></p>` : ''}
            ${u.error ? `<p class="pf-warn-note">${escapeHtml(u.error)}</p>` : ''}`;
    };

    const renderArtifactsModal = (data) => {
        const shim = data && data.shimCache ? data.shimCache : [];
        const am = data && data.amcache ? data.amcache : [];
        const shimLoaded = !!(data && data.shimLoaded);
        const amLoaded = !!(data && data.amLoaded);
        const isAdmin = !!(data && data.isAdmin);
        const bad = (p) => /\\temp\\|\\tmp\\|\\downloads\\|\\public\\|\\recycle/i.test(p || '');

        const artRow = (key, path, keyClass, metaHtml) => `
            <div class="art-row">
                <div class="art-row-main">
                    <span class="art-row-key ${keyClass}" title="${key}">${key}</span>
                    <span class="art-row-path" title="${path}">${path}</span>
                </div>
                ${metaHtml ? `<span class="art-row-meta">${metaHtml}</span>` : ''}
            </div>`;

        const shimRows = (q) => {
            const query = (q || '').toLowerCase();
            const list = shim.filter(e => !query || (e.path || '').toLowerCase().includes(query));
            if (!list.length) {
                return `<div class="scanning-state"><p>${query
                    ? 'No matching ShimCache entries.'
                    : (shimLoaded ? 'ShimCache is empty on this machine.' : 'ShimCache could not be read.')}</p></div>`;
            }
            return list.map(e => artRow(
                escapeHtml(e.modified || 'no-ts'),
                escapeHtml(e.path || ''),
                bad(e.path) ? 'bad' : 'dir',
                `${e.execFlag ? '<span class="shim-exec">ran</span>' : ''}${e.hasTimestamp ? '' : '<span class="shim-nots">no-ts</span>'}<span class="shim-fmt">${escapeHtml(e.format || '')}</span>`
            )).join('');
        };

        const amRows = (q) => {
            const query = (q || '').toLowerCase();
            const fmtSize = (s) => {
                if (!s) return '';
                return s >= 1048576 ? (s / 1048576).toFixed(1) + ' MB'
                    : s >= 1024 ? Math.round(s / 1024) + ' KB'
                    : s + ' B';
            };
            const list = am.filter(e => !query ||
                (e.name || '').toLowerCase().includes(query) ||
                (e.path || '').toLowerCase().includes(query) ||
                (e.publisher || '').toLowerCase().includes(query));
            if (!list.length) {
                return `<div class="scanning-state"><p>${query
                    ? 'No matching Amcache entries.'
                    : (amLoaded ? 'Amcache loaded, but no application-file records were found.'
                        : (isAdmin ? 'Amcache.hve could not be read (backup privilege or hive lock).'
                            : 'Amcache requires Administrator.'))}</p></div>`;
            }
            return list.map(e => artRow(
                escapeHtml(e.name || e.path || ''),
                escapeHtml(e.path || ''),
                e.isDriver ? 'sys' : e.badPath ? 'bad' : 'dir',
                `${e.publisher ? '<span class="shim-fmt" title="Publisher">' + escapeHtml(e.publisher) + '</span>' : ''}${e.isDriver ? '<span class="shim-exec">driver</span>' : ''}${e.hasInstallRecord ? '<span class="shim-exec">installed</span>' : ''}${e.badPath ? '<span class="shim-nots">bad-path</span>' : ''}${e.size ? '<span class="shim-fmt" title="Size">' + fmtSize(e.size) + '</span>' : ''}${e.sha1 ? '<span class="shim-fmt shim-hash" title="SHA-1">' + escapeHtml(e.sha1) + '</span>' : ''}`
            )).join('');
        };

        const pane = (key, title, count, rowsHtml) => `
            <div class="art-pane" data-artpane="${key}"${key === 'shim' ? '' : ' hidden'}>
                <div class="usn-head">${title} (${count} shown)</div>
                <div class="usn-list art-list">${rowsHtml}</div>
            </div>`;

        const tabs = `
            <div class="art-shell">
                <div class="art-toolbar">
                    <input id="pfArtSearch" class="pf-search" type="search" placeholder="Search ShimCache and Amcache…" spellcheck="false" autocomplete="off">
                    <span class="pf-ref-count" id="pfArtCount"></span>
                </div>
                <div class="art-tabs">
                    <button type="button" class="art-tab on" data-arttab="shim">ShimCache (${shim.length})</button>
                    <button type="button" class="art-tab" data-arttab="am">Amcache (${am.length})</button>
                </div>
                ${pane('shim', 'ShimCache entries', shim.length, shimRows(''))}
                ${pane('am', 'Amcache entries', am.length, amRows(''))}
            </div>`;
        return { html: tabs, shim, am, shimRows, amRows };
    };

    const hasReferencedFiles = (e) =>
        (e.referencedFiles && e.referencedFiles.length) || Number(e.referencedFileCount) > 0;

    const referencedFileCount = (e) =>
        (e.referencedFiles && e.referencedFiles.length) || Number(e.referencedFileCount) || 0;

    const openReferencedModal = (entry, filesOverride) => {
        const files = (filesOverride && filesOverride.length) ? filesOverride : (entry.referencedFiles || []);
        const fmtSize = (s) => {
            if (s == null) return '';
            const b = Number(s);
            if (!b) return '';
            return b >= 1048576 ? (b / 1048576).toFixed(1) + ' MB'
                : b >= 1024 ? Math.round(b / 1024) + ' KB'
                : b + ' B';
        };
        const rowHtml = (f) => {
            const meta = [];
            if (f.fileSize != null) meta.push(fmtSize(f.fileSize));
            if (f.modified) meta.push(escapeHtml(f.modified));
            if (f.mftReference) meta.push('MFT 0x' + (Number(f.mftReference) >>> 0).toString(16).toUpperCase());
            const metaHtml = meta.length ? `<span class="pf-file-meta">${meta.join(' · ')}</span>` : '';
            return `
                <div class="pf-file-row ${f.suspicious ? 'susp' : ''}">
                    <span class="pf-file-path">${escapeHtml(f.path)}</span>${sigBadge(f.signatureStatus)}
                    ${metaHtml}
                </div>`;
        };
        const rows = (q) => {
            const query = (q || '').toLowerCase();
            const filtered = files.filter(f => !query || (f.path || '').toLowerCase().includes(query));
            if (!filtered.length) return `<div class="scanning-state"><p>No matching referenced files.</p></div>`;
            return filtered.map(rowHtml).join('');
        };
        showPfInfo(`Referenced Files — ${entry.pfFileName}`, '', `
            <div class="pf-ref-toolbar">
                <input id="pfRefSearch" class="pf-search" type="text" placeholder="Search referenced files...">
                <span class="pf-ref-count">${files.length}</span>
            </div>
            <div class="usn-list pf-ref-list">${rows('')}</div>
        `);
        const input = $('pfRefSearch');
        if (input) {
            input.addEventListener('input', () => {
                const listEl = document.querySelector('.pf-ref-list');
                if (listEl) listEl.innerHTML = rows(input.value);
            });
            input.focus();
        }
    };

    // Opens the referenced-files modal, fetching the list on demand when the
    // scan response was slimmed past the size limit and the entry no longer
    // carries its referencedFiles array.
    const openReferencedFiles = async (entry) => {
        if (entry.referencedFiles && entry.referencedFiles.length) {
            openReferencedModal(entry);
            return;
        }
        const name = entry.pfFileName || 'Prefetch entry';
        const modal = $('pf-info-modal');
        showPfInfo(`Referenced Files — ${name}`, '', `
            ${loadingState('Loading referenced files…')}`);
        const stillOpen = () => modal && modal.classList.contains('active');
        try {
            const res = await window.pywebview.api.prefetch_refs(name);
            if (!stillOpen()) return;
            const files = (res && res.referencedFiles) ? res.referencedFiles : [];
            if (!files.length) {
                $('pfInfoBody').innerHTML = `<div class="scanning-state"><p>No referenced files found for this entry.</p></div>`;
                return;
            }
            openReferencedModal(entry, files);
        } catch (err) {
            if (!stillOpen()) return;
            $('pfInfoBody').innerHTML =
                `<div class="scanning-state"><p>Failed to load referenced files: ${escapeHtml(err.message || err)}</p></div>`;
        }
    };

    const openRunsModal = (entry) => {
        const times = entry.lastExecutionTimes || [];
        const rows = times.length
            ? times.map((t, i) => `
                <div class="usn-row">
                    <span class="usn-action dir">#${times.length - i}</span>
                    <span class="usn-name">${escapeHtml(t)}</span>
                </div>`).join('')
            : `<div class="scanning-state"><p>No recorded execution times for this entry.</p></div>`;
        showPfInfo(`Run History — ${entry.pfFileName}`, '', `
            <div class="pf-ref-toolbar">
                <span class="pf-ref-count" style="margin:0 auto 0 0;background:rgba(255,255,255,0.05);border-color:rgba(255,255,255,0.1);color:var(--color-fg);min-width:auto;padding:5px 12px;">Run count: ${entry.runCount}</span>
            </div>
            <div class="usn-head">${times.length} recorded execution time${times.length === 1 ? '' : 's'}</div>
            <div class="usn-list pf-ref-list">${rows}</div>
        `);
    };

    const pfTools = {
        search: $('pfSearch'),
        filters: $$('#pfFilters .pf-chip'),
        reload: $('pfReload'),
        exportBtn: $('pfExport'),
        usn: $('pfUsn'),
        sysmain: $('pfSysMain'),
        usnStatus: $('pfUsnStatus'),
        artifacts: $('pfArtifacts'),
        missing: $('pfMissing'),
        exportArtifacts: $('pfExportArtifacts'),
        exportStatus: $('pfExportStatus'),
        infoClose: $('pfInfoCloseBtn')
    };

    const bindPfToolbar = () => {
        if (!pfTools.search) return;
        pfTools.search.addEventListener('input', () => {
            pfState.search = pfTools.search.value;
            renderPrefetch();
        });

        pfTools.filters.forEach(chip => {
            chip.addEventListener('mouseenter', () => SoundFX.hover());
            chip.addEventListener('click', (e) => {
                SoundFX.click();
                addRipple(chip, e.clientX, e.clientY);
                const key = chip.dataset.filter;
                pfState[key] = !pfState[key];
                chip.classList.toggle('on', pfState[key]);
                renderPrefetch();
            });
        });

        pfTools.reload.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.reload.addEventListener('click', async (e) => {
            SoundFX.tool();
            addRipple(pfTools.reload, e.clientX, e.clientY);
            if (pfBusy) return;
            const container = $('prefetch-results');
            pfResult = null;
            setPfBusy(true);
            container.innerHTML = loadingState(
                'Reloading Prefetch…',
                'Signature cache cleared, then a full rescan');
            try {
                await window.pywebview.api.prefetch_clear_cache();
                const result = await window.pywebview.api.prefetch_parser_run();
                if (!result) throw new Error('No output returned.');
                const hasAny = (result.entries && result.entries.length)
                    || (result.recoveredDeleted && result.recoveredDeleted.length)
                    || (result.ghostEntries && result.ghostEntries.length)
                    || (result.ghostArtifacts && result.ghostArtifacts.length);
                if (result.error && result.requiresAdmin) {
                    pfResult = null;
                    container.innerHTML = `<div class="scanning-state"><p>${escapeHtml(result.error)}</p></div>`;
                    return;
                }
                if (result.error && !hasAny) {
                    pfResult = null;
                    container.innerHTML = `<div class="scanning-state"><p>${escapeHtml(result.error)}</p></div>`;
                    return;
                }
                pfResult = result;
                renderPrefetch();
            } catch (err) {
                container.innerHTML = isCancellation(err)
                    ? '<div class="scanning-state"><p>Scan stopped.</p></div>'
                    : `<div class="scanning-state"><p>Reload failed: ${escapeHtml(err.message || err)}</p></div>`;
            } finally {
                setPfBusy(false);
            }
        });

        pfTools.exportBtn.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.exportBtn.addEventListener('click', async (e) => {
            SoundFX.click();
            addRipple(pfTools.exportBtn, e.clientX, e.clientY);
            const seen = new Set();
            const entries = [];
            for (const e of [...getFilteredEntries(), ...collectMissingEntries()]) {
                const key = (e.pfPath || '') + '|' + (e.pfFileName || '') + '|' + (e.mainExecutablePath || '');
                if (seen.has(key)) continue;
                seen.add(key);
                entries.push(e);
            }
            if (!entries.length) {
                pfTools.exportStatus.textContent = 'Nothing to export';
                return;
            }
            pfTools.exportStatus.textContent = 'Exporting...';
            try {
                const res = await window.pywebview.api.prefetch_export_csv(entries);
                pfTools.exportStatus.textContent = res || 'Exported';
            } catch (err) {
                pfTools.exportStatus.textContent = 'Export failed';
            }
        });

        pfTools.usn.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.usn.addEventListener('click', async (e) => {
            SoundFX.tool();
            addRipple(pfTools.usn, e.clientX, e.clientY);
            showPfInfo('USN Journal', '', loadingState('Reading USN Journal…'));
            try {
                const payload = await window.pywebview.api.prefetch_usn();
                const events = Array.isArray(payload) ? payload : (payload && payload.events) || [];
                $('pfInfoBody').innerHTML = renderUsnModal(payload || []);
                const input = $('pfUsnSearch');
                if (input) {
                    input.addEventListener('input', () => {
                        const body = $('pfInfoBody');
                        const listEl = body.querySelector('.pf-ref-list');
                        if (listEl) listEl.innerHTML = renderUsnRows(input.value, events);
                    });
                }
            } catch (err) {
                $('pfInfoBody').innerHTML =
                    `<div class="scanning-state"><p>USN scan failed: ${escapeHtml(err.message || err)}</p></div>`;
            }
        });

        pfTools.sysmain.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.sysmain.addEventListener('click', async (e) => {
            SoundFX.tool();
            addRipple(pfTools.sysmain, e.clientX, e.clientY);
            showPfInfo('SysMain Info', '', loadingState('Reading SysMain…'));
            try {
                const sm = await window.pywebview.api.prefetch_sysmain();
                $('pfInfoBody').innerHTML = renderSysMainModal(sm || {});
            } catch (err) {
                $('pfInfoBody').innerHTML =
                    `<div class="scanning-state"><p>SysMain query failed: ${escapeHtml(err.message || err)}</p></div>`;
            }
        });

        pfTools.usnStatus.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.usnStatus.addEventListener('click', async (e) => {
            SoundFX.tool();
            addRipple(pfTools.usnStatus, e.clientX, e.clientY);
            showPfInfo('USN Integrity', '', loadingState('Checking USN Journal…'));
            try {
                const u = await window.pywebview.api.prefetch_usn_status();
                $('pfInfoBody').innerHTML = renderUsnStatusModal(u || {});
            } catch (err) {
                $('pfInfoBody').innerHTML =
                    `<div class="scanning-state"><p>USN integrity check failed: ${escapeHtml(err.message || err)}</p></div>`;
            }
        });

        if (pfTools.missing) {
            pfTools.missing.addEventListener('mouseenter', () => SoundFX.hover());
            pfTools.missing.addEventListener('click', (e) => {
                SoundFX.tool();
                addRipple(pfTools.missing, e.clientX, e.clientY);
                openMissingModal();
            });
        }
        const missingClose = $('pfMissingCloseBtn');
        const missingModal = $('pf-missing-modal');
        missingClose?.addEventListener('mouseenter', () => SoundFX.hover());
        missingClose?.addEventListener('click', (e) => {
            SoundFX.close();
            addRipple(missingClose, e.clientX, e.clientY);
            closeMissingModal();
        });
        missingModal?.addEventListener('click', (e) => {
            if (e.target === missingModal) closeMissingModal();
            const card = e.target.closest('.pf-compact');
            if (!card || !missingModal.contains(card)) return;
            const entry = pfMissingDisplayed[Number(card.dataset.missidx)];
            if (!entry) return;
            SoundFX.tool();
            const refBtn = hasReferencedFiles(entry)
                ? `<button type="button" class="pf-ref-btn" data-missref="${Number(card.dataset.missidx)}">Referenced files (${referencedFileCount(entry)})</button>`
                : '';
            showPfInfo(entry.pfFileName || 'Missing evidence', '', `
                <div class="pf-kv">
                    <div class="kv-row"><span class="kv-key">Path</span><span class="kv-val">${escapeHtml(entry.mainExecutablePath || entry.lastSeenPath || entry.pfPath || '')}</span></div>
                    ${entry.lastSeenPath && entry.lastSeenPath !== entry.mainExecutablePath ? `<div class="kv-row"><span class="kv-key">Last seen</span><span class="kv-val">${escapeHtml(entry.lastSeenPath)}</span></div>` : ''}
                    ${entry.lastSeenSource || entry.lastSeenTime ? `<div class="kv-row"><span class="kv-key">Evidence</span><span class="kv-val">${escapeHtml([entry.lastSeenSource, entry.lastSeenTime].filter(Boolean).join(' · '))}</span></div>` : ''}
                    ${entry.signatureDetail ? `<div class="kv-row"><span class="kv-key">Why</span><span class="kv-val">${escapeHtml(entry.signatureDetail)}</span></div>` : ''}
                    ${entry.riskSummary ? `<div class="kv-row"><span class="kv-key">Risk</span><span class="kv-val">${escapeHtml(entry.riskSummary)}</span></div>` : ''}
                    ${entry.sha256 ? `<div class="kv-row"><span class="kv-key">SHA-256</span><span class="kv-val">${escapeHtml(entry.sha256)}</span></div>` : ''}
                </div>
                <div class="pf-meta" style="border:0;margin-top:10px;padding-top:0">${presenceBadge(entry, false)}${sigBadge(entry.mainSignatureStatus)}</div>
                ${refBtn}
            `);
        });
        missingModal?.addEventListener('keydown', (e) => {
            if (e.key !== 'Tab' || !missingModal.classList.contains('active')) return;
            const nodes = [...missingModal.querySelectorAll(
                'button:not([disabled]), input:not([disabled]), [href], select, textarea, [tabindex]:not([tabindex="-1"])'
            )].filter(el => el.offsetParent !== null || el === document.activeElement);
            if (!nodes.length) return;
            const first = nodes[0];
            const last = nodes[nodes.length - 1];
            if (e.shiftKey && document.activeElement === first) {
                e.preventDefault();
                last.focus();
            } else if (!e.shiftKey && document.activeElement === last) {
                e.preventDefault();
                first.focus();
            }
        });
        $('pfMissingSearch')?.addEventListener('input', (e) => {
            pfMissingSearch = e.target.value || '';
            renderMissingPopupList();
        });
        $$('#pfMissingFilters .pf-chip').forEach(chip => {
            chip.addEventListener('mouseenter', () => SoundFX.hover());
            chip.addEventListener('click', (ev) => {
                SoundFX.click();
                addRipple(chip, ev.clientX, ev.clientY);
                pfMissingKind = chip.dataset.miss || 'all';
                $$('#pfMissingFilters .pf-chip').forEach(c =>
                    c.classList.toggle('on', c === chip));
                renderMissingPopupList();
            });
        });

        pfTools.artifacts.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.artifacts.addEventListener('click', async (e) => {
            SoundFX.tool();
            addRipple(pfTools.artifacts, e.clientX, e.clientY);
            showPfInfo('Artifacts', '', loadingState('Reading ShimCache and Amcache…'));
            try {
                const data = await window.pywebview.api.prefetch_artifacts();
                const art = renderArtifactsModal(data || {});
                $('pfInfoBody').innerHTML = art.html;
                const body = $('pfInfoBody');

                const reapply = () => {
                    const q = ($('pfArtSearch')?.value || '').trim();
                    const ql = q.toLowerCase();
                    const onTab = body.querySelector('.art-tab.on')?.dataset.arttab || 'shim';
                    const shimShown = art.shim.filter(e => !ql || (e.path || '').toLowerCase().includes(ql));
                    const amShown = art.am.filter(e => !ql ||
                        (e.name || '').toLowerCase().includes(ql) ||
                        (e.path || '').toLowerCase().includes(ql) ||
                        (e.publisher || '').toLowerCase().includes(ql));
                    body.querySelector('[data-artpane="shim"] .usn-list').innerHTML = art.shimRows(q);
                    body.querySelector('[data-artpane="shim"] .usn-head').textContent = `ShimCache entries (${shimShown.length} shown)`;
                    body.querySelector('[data-artpane="am"] .usn-list').innerHTML = art.amRows(q);
                    body.querySelector('[data-artpane="am"] .usn-head').textContent = `Amcache entries (${amShown.length} shown)`;
                    const shown = onTab === 'am' ? amShown.length : shimShown.length;
                    const tabTotal = onTab === 'am' ? art.am.length : art.shim.length;
                    const countEl = $('pfArtCount');
                    if (countEl) countEl.textContent = `${shown} / ${tabTotal}`;
                };

                const search = $('pfArtSearch');
                if (search) {
                    search.addEventListener('mouseenter', () => SoundFX.hover());
                    search.addEventListener('input', reapply);
                }
                reapply();

                body.querySelectorAll('.art-tab').forEach(tab => {
                    tab.addEventListener('mouseenter', () => SoundFX.hover());
                    tab.addEventListener('click', () => {
                        SoundFX.click();
                        body.querySelectorAll('.art-tab').forEach(t => t.classList.toggle('on', t === tab));
                        body.querySelectorAll('.art-pane').forEach(p => {
                            p.hidden = p.dataset.artpane !== tab.dataset.arttab;
                        });
                        reapply();
                    });
                });
            } catch (err) {
                $('pfInfoBody').innerHTML =
                    `<div class="scanning-state"><p>Artifacts scan failed: ${escapeHtml(err.message || err)}</p></div>`;
            }
        });

        pfTools.exportArtifacts.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.exportArtifacts.addEventListener('click', async (e) => {
            SoundFX.click();
            addRipple(pfTools.exportArtifacts, e.clientX, e.clientY);
            pfTools.exportStatus.textContent = 'Exporting...';
            try {
                const res = await window.pywebview.api.prefetch_export_artifacts();
                pfTools.exportStatus.textContent = res || 'Exported';
            } catch (err) {
                pfTools.exportStatus.textContent = 'Export failed';
            }
        });

        pfTools.infoClose.addEventListener('mouseenter', () => SoundFX.hover());
        pfTools.infoClose.addEventListener('click', (e) => {
            SoundFX.close();
            addRipple(pfTools.infoClose, e.clientX, e.clientY);
            closePfInfo();
        });
        $('pf-info-modal').addEventListener('click', (e) => {
            const missRef = e.target.closest('.pf-ref-btn[data-missref]');
            if (missRef) {
                SoundFX.tool();
                const entry = pfMissingDisplayed[Number(missRef.dataset.missref)];
                if (entry) openReferencedFiles(entry);
                return;
            }
            if (e.target === $('pf-info-modal')) {
                SoundFX.modalClose();
                closePfInfo();
            }
        });
    };

    /* ================================================================ *
     *  BAM PARSER
     * ================================================================ */
    let bamResult = null;
    let bamBusy = false;
    let bamState = { search: '', untrusted: false, postlogon: false, yara: false };
    let bamDisplayed = [];

    const setBamBusy = (busy) => {
        bamBusy = busy;
        if (ui.bamRescanBtn) {
            ui.bamRescanBtn.disabled = busy;
            ui.bamRescanBtn.hidden = busy;
        }
        const ready = !busy && !!bamResult && !bamResult.requiresAdmin;
        if (ui.bamToolbar) ui.bamToolbar.hidden = !ready;
    };

    const bamSigLabel = (s) => ({ 0: 'Signed', 1: 'Unsigned', 2: 'Not Found', 3: 'Cheat', 4: 'Fake' })[Number(s)] || 'Unknown';
    const bamSigClass = (s) => ({ 0: 'signed', 1: 'unsigned', 2: 'missing', 3: 'cheat', 4: 'fake' })[Number(s)] || 'warn';

    const renderBamSummary = (r) => {
        const findings = (r.cheatSig || 0) + (r.yaraMatch || 0) + (r.fakeSig || 0) + (r.unsigned || 0);
        const banners = [];
        if (r.requiresAdmin) {
            banners.push(pfBanner(
                'Limited scan',
                'BAM values are readable, but signature verification, deleted BAM recovery and registry ACL checks need Administrator.'
            ));
        }
        if (r.deletedReadFailed) {
            banners.push(pfBanner('Deleted BAM read failed', escapeHtml(r.deletedReadError || 'Unknown error'), 'info'));
        }
        if (r.deletedCount > 0) {
            banners.push(pfBanner(
                `${r.deletedCount} deleted BAM ${r.deletedCount === 1 ? 'path' : 'paths'} recovered`,
                'Found in the raw SYSTEM hive — programs that executed but whose BAM value was later deleted.',
                'info'
            ));
        }
        if (r.deniedCount > 0) {
            banners.push(pfBanner(
                `${r.deniedCount} denied registry ${r.deniedCount === 1 ? 'entry' : 'entries'}`,
                'The bam subtree carries deny ACEs — usually a hardening/anti-cheat fingerprint.'
            ));
        }
        if (r.yaraMatch > 0) {
            banners.push(pfBanner(
                `${r.yaraMatch} YARA match${r.yaraMatch === 1 ? '' : 'es'}`,
                'Unsigned executed files that hit cheat rules are shown as Cheat.'
            ));
        }

        return `
            <div class="svc-summary">
                <div class="svc-stat">
                    <span class="svc-stat-value">${r.total || 0}</span>
                    <span class="svc-stat-label">BAM entries</span>
                </div>
                <div class="svc-stat ${findings ? 'bad' : 'ok'}">
                    <span class="svc-stat-value">${findings}</span>
                    <span class="svc-stat-label">Findings</span>
                </div>
                <div class="svc-stat ${(r.cheatSig || 0) + (r.yaraMatch || 0) ? 'bad' : ''}">
                    <span class="svc-stat-value">${(r.cheatSig || 0) + (r.yaraMatch || 0)}</span>
                    <span class="svc-stat-label">Cheat</span>
                </div>
                <div class="svc-stat ${r.deletedCount ? 'warn' : ''}">
                    <span class="svc-stat-value">${r.deletedCount || 0}</span>
                    <span class="svc-stat-label">Deleted</span>
                </div>
                <div class="svc-stat ${r.deniedCount ? 'warn' : ''}">
                    <span class="svc-stat-value">${r.deniedCount || 0}</span>
                    <span class="svc-stat-label">Denied keys</span>
                </div>
                ${r.scanSeconds ? `<div class="svc-stat"><span class="svc-stat-value">${r.scanSeconds}<small>s</small></span><span class="svc-stat-label">Scan time</span></div>` : ''}
            </div>
            ${banners.join('')}`;
    };

    const renderBamEntry = (e, idx) => {
        const tone = e.signature === 3 || e.signature === 4 ? 'cheat'
            : e.signature === 1 ? 'unsigned' : '';
        return `
            <article class="pf-entry ${tone} anim-in" style="animation-delay:${Math.min(24 * idx, 280)}ms">
                <button type="button" class="pf-entry-toggle" aria-expanded="false">
                    <span class="svc-dot ${e.signature === 3 || e.signature === 4 || (e.matchedRules && e.matchedRules.length) ? 'bad' : e.signature === 1 ? 'warn' : 'ok'}"></span>
                    <div class="pf-entry-title">
                        <span class="pf-name">${escapeHtml(e.path.split('\\').pop() || e.path)}</span>
                        <span class="pf-entry-path">${escapeHtml(e.path)}</span>
                    </div>
                    ${e.fileExists ? '' : '<span class="pf-badge missing">Missing file</span>'}
                    <span class="pf-badge ${bamSigClass(e.signature)}">${bamSigLabel(e.signature)}</span>
                </button>
                <div class="pf-entry-body">
                    <div class="pf-row"><span class="pf-key">Path</span><span class="pf-val">${escapeHtml(e.path)}</span></div>
                    ${e.lastExecution ? `<div class="pf-row"><span class="pf-key">Last execution</span><span class="pf-val">${escapeHtml(e.lastExecution)}${e.inLogonWindow ? ' <span class="pf-chip ok">Post-logon</span>' : ''}</span></div>` : ''}
                    ${e.signatureDetail ? `<div class="pf-row pf-detail-row"><span class="pf-key">Why</span><span class="pf-val">${escapeHtml(e.signatureDetail)}</span></div>` : ''}
                    ${renderYaraTags(e.matchedRules)}
                    ${e.isSystemEntry ? '<div class="pf-row"><span class="pf-key">Note</span><span class="pf-val">Windows/system-owned path</span></div>' : ''}
                </div>
            </article>`;
    };

    const renderBamDeleted = (r) => {
        if (!r.deletedPaths || !r.deletedPaths.length) return '';
        return `
            <section class="service-section">
                <div class="service-section-header">
                    <span class="service-section-icon">${SVC_ICONS.recycle}</span>
                    <h3>Deleted BAM paths</h3>
                    <span class="svc-section-meta">${r.deletedPaths.length} recovered</span>
                </div>
                <div class="service-section-body">
                    <p class="svc-empty">Programs that executed but whose BAM value was later deleted.</p>
                    <button type="button" class="pf-ref-btn" id="bamDeletedOpen" data-bam-deleted="open">View deleted paths (${r.deletedPaths.length})</button>
                </div>
            </section>`;
    };

    const renderDeletedBamRows = (q, paths) => {
        const query = (q || '').toLowerCase().trim();
        const list = (paths || []).filter(p =>
            !query || (p.path || '').toLowerCase().includes(query));
        if (!list.length) return '<p class="svc-empty">No deleted paths match the search.</p>';
        return `<div class="alt-list">${list.map(p => `
            <div class="alt-row">
                <span class="svc-dot warn"></span>
                <div class="alt-row-copy">
                    <span class="alt-row-val">${escapeHtml(p.path)}</span>
                </div>
            </div>`).join('')}</div>`;
    };

    const openDeletedBamModal = () => {
        if (!bamResult || !bamResult.deletedPaths || !bamResult.deletedPaths.length) return;
        const paths = bamResult.deletedPaths;
        showPfInfo('Deleted BAM paths', '', `
            <div class="pf-missing-toolbar">
                <input id="bamDeletedSearch" class="pf-missing-search" type="search" placeholder="Search deleted paths…" spellcheck="false" autocomplete="off">
            </div>
            <div class="pf-ref-list" id="bamDeletedList">${renderDeletedBamRows('', paths)}</div>`);
        const input = $('bamDeletedSearch');
        if (input) {
            input.focus();
            input.addEventListener('input', () => {
                $('bamDeletedList').innerHTML = renderDeletedBamRows(input.value, paths);
            });
        }
    };

    const renderBamDenied = (r) => {
        if (!r.deniedEntries || !r.deniedEntries.length) return '';
        return `
            <section class="service-section">
                <div class="service-section-header">
                    <span class="service-section-icon">${ICON.lock}</span>
                    <h3>Denied registry keys</h3>
                    <span class="svc-section-meta">${r.deniedEntries.length} entries</span>
                </div>
                <div class="service-section-body">
                    <div class="alt-list">${r.deniedEntries.map(d => `
                        <div class="alt-row">
                            <span class="svc-dot warn"></span>
                            <div class="alt-row-copy">
                                <span class="alt-row-val">${escapeHtml(d.keyPath)}</span>
                                <span class="alt-row-sub">Denied: ${escapeHtml(d.permission)}</span>
                            </div>
                        </div>`).join('')}</div>
                </div>
            </section>`;
    };

    const applyBamFilters = () => {
        const q = bamState.search.trim().toLowerCase();
        const src = bamResult.entries || [];
        bamDisplayed = src.filter(e => {
            if (bamState.untrusted && ![1, 3, 4].includes(Number(e.signature))) return false;
            if (bamState.postlogon && !e.inLogonWindow) return false;
            if (bamState.yara && !(e.matchedRules && e.matchedRules.length)) return false;
            if (q) {
                const hay = ((e.path || '') + ' ' + (e.signatureDetail || '') + ' ' + (e.matchedRules || []).join(' ')).toLowerCase();
                if (!hay.includes(q)) return false;
            }
            return true;
        });
    };

    const renderBam = () => {
        const container = $('bam-results');
        if (!bamResult) return;
        applyBamFilters();
        const list = bamDisplayed.map(renderBamEntry).join('');
        container.innerHTML = renderBamSummary(bamResult)
            // Deleted BAM first — the most actionable finding of the scan.
            + renderBamDeleted(bamResult)
            + `<section class="service-section">
                <div class="service-section-header">
                    <span class="service-section-icon">${SVC_ICONS.services}</span>
                    <h3>BAM entries</h3>
                    <span class="svc-section-meta">${bamDisplayed.length} / ${bamResult.entries.length} shown</span>
                </div>
                <div class="service-section-body pf-list">${list || '<p class="svc-empty">No BAM entries match the filters.</p>'}</div>
            </section>`
            + renderBamDenied(bamResult);
        revealSections(container);
        // Cards start hidden (opacity 0) for the reveal animation; stagger them
        // like the Prefetch screen does.
        container.querySelectorAll('.pf-entry').forEach((el, i) => {
            setTimeout(() => el.classList.add('revealed'), Math.min(24 * i, 280));
        });
    };

    const openBamParser = async (force = false) => {
        showScreen('bam-screen');
        if (bamBusy) return;
        if (!force && bamResult) {
            renderBam();
            return;
        }

        const container = $('bam-results');
        setBamBusy(true);
        bamResult = null;
        container.innerHTML = loadingState(
            'Parsing BAM entries…',
            'Registry traces, signatures, YARA, deleted SYSTEM-hive recovery and registry ACLs');

        try {
            const result = await window.pywebview.api.bam_parser_run();
            if (!result) throw new Error('No output returned.');
            bamResult = result;
            renderBam();
        } catch (err) {
            container.innerHTML = isCancellation(err)
                ? '<div class="scanning-state"><p>Scan stopped.</p></div>'
                : `<div class="scanning-state"><p>Error running scan: ${escapeHtml(err.message || err)}</p></div>`;
        } finally {
            setBamBusy(false);
        }
    };

    const bindBamToolbar = () => {
        const search = $('bamSearch');
        if (!search) return;
        search.addEventListener('input', () => {
            bamState.search = search.value;
            renderBam();
        });
        $$('#bamFilters .pf-chip').forEach(chip => {
            chip.addEventListener('mouseenter', () => SoundFX.hover());
            chip.addEventListener('click', (e) => {
                SoundFX.click();
                addRipple(chip, e.clientX, e.clientY);
                bamState[chip.dataset.filter] = !bamState[chip.dataset.filter];
                chip.classList.toggle('on', bamState[chip.dataset.filter]);
                renderBam();
            });
        });
        bindOnce(ui.bamRescanBtn, () => openBamParser(true), 'tool');
        ui.bamBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
        ui.bamBackBtn?.addEventListener('click', (e) => {
            SoundFX.close();
            addRipple(ui.bamBackBtn, e.clientX, e.clientY);
            stopCurrentScan();
            showScreen('main-screen');
        });
        const results = $('bam-results');
        results?.addEventListener('click', (e) => {
            const deletedBtn = e.target.closest('[data-bam-deleted]');
            if (deletedBtn) {
                SoundFX.tool();
                addRipple(deletedBtn, e.clientX, e.clientY);
                openDeletedBamModal();
                return;
            }
            const toggle = e.target.closest('.pf-entry-toggle');
            if (!toggle || !results.contains(toggle)) return;
            SoundFX.click();
            const entry = toggle.closest('.pf-entry');
            const open = entry.classList.toggle('is-open');
            toggle.setAttribute('aria-expanded', open ? 'true' : 'false');
        });
    };

    /* ================================================================ *
     *  ALT CHECKER
     * ================================================================ */
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
        navigator.clipboard?.writeText(lines.join('\n')).then(() => {
            flashCopied(btn, `Copied ${lines.length} ${label}`);
        }).catch(() => flashCopied(btn, 'Copy failed'));
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
        const a = document.createElement('a');
        a.href = URL.createObjectURL(blob);
        a.download = `AltDetection_${new Date().toISOString().replace(/[:.]/g, '-')}.json`;
        a.click();
        URL.revokeObjectURL(a.href);
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

    /* ================================================================ *
     *  Wire-up
     * ================================================================ */
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
            } else if (card.dataset.tool === 'prefetch-parser') {
                openPrefetchParser();
            } else if (card.dataset.tool === 'bam-parser') {
                openBamParser();
            } else if (card.dataset.tool === 'alt-detector') {
                openAltDetector();
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

    ui.prefetchBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.prefetchBackBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.prefetchBackBtn, e.clientX, e.clientY);
        stopCurrentScan();
        showScreen('main-screen');
    });

    ui.altBackBtn?.addEventListener('mouseenter', () => SoundFX.hover());
    ui.altBackBtn?.addEventListener('click', (e) => {
        SoundFX.close();
        addRipple(ui.altBackBtn, e.clientX, e.clientY);
        stopCurrentScan();
        showScreen('main-screen');
    });

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
            const pfMissing = $('pf-missing-modal');
            const pfInfo = $('pf-info-modal');
            if (pfInfo && pfInfo.classList.contains('active')) {
                closePfInfo();
            } else if (pfMissing && pfMissing.classList.contains('active')) {
                closeMissingModal();
            } else if (ui.modal.classList.contains('active')) {
                closeModal();
            } else if (ui.serviceBackBtn && $('service-screen').classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.prefetchBackBtn && $('prefetch-screen').classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.altBackBtn && $('alt-screen').classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            } else if (ui.bamBackBtn && $('bam-screen').classList.contains('active')) {
                stopCurrentScan();
                showScreen('main-screen');
            }
        }
    });

    $$('.island-brand, .footer-text, .rank-badge').forEach(el => {
        el.addEventListener('mouseenter', () => SoundFX.softHover());
    });

    bindPfToolbar();
    bindPrefetchResults();
    bindBamToolbar();
    bindAltToolbar();

    /* ---------------------------------------------------------------- *
     *  Splash lifecycle
     * ---------------------------------------------------------------- */
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
    // failsafe: never stuck - close after 2.5s even if load never fires (WebView2 virtual host)
    setTimeout(closeSplash, 2500);
    window.addEventListener('load', closeSplash);
    document.addEventListener('DOMContentLoaded', () => setTimeout(closeSplash, 400));
    if (document.readyState === 'complete' || document.readyState === 'interactive') {
        setTimeout(closeSplash, 600);
    }
    window.addEventListener('pywebviewready', () => closeSplash());
})();
