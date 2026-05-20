/**
 * sphere-alert.js — DNS-driven alert banner
 *
 * Reads TXT records at alert.<your-domain>, alert2.<your-domain>, and
 * alert3.<your-domain> via DNS-over-HTTPS and renders banners at the top
 * of the page in slot order.
 *
 * TXT record format (JSON, outer-quoted with escaped inner quotes when
 * pasted into Cloudflare dashboard):
 *
 *   "{\"l\":2,\"m\":\"Maintenance Sat 7am-9am\",\"d\":1,\"s\":0}"
 *
 * Fields:
 *   l (int, required):  level — 0=info, 1=low, 2=medium, 3=high, 4=critical
 *   m (string, required): message text (max 240 chars; longer truncated with …)
 *   d (int, optional):  dismissable — 1=yes (default), 0=no
 *   s (int, optional):  force scroll-on-hover — 1=yes, 0=auto (default).
 *                       Auto means scroll only kicks in when message overflows
 *                       the banner. Force means always scroll on hover.
 *
 * Hover scroll behavior:
 *   - Only the message text scrolls; the level pill and X button stay fixed
 *   - Hover triggers one end-to-end scroll
 *   - Click the message to restart from the beginning
 *   - Mouse leave stops and resets
 *
 * To clear an alert: delete the TXT record entirely, or set it to empty.
 *
 * Slot ordering: alert (top), alert2 (middle), alert3 (bottom).
 *
 * Drop-in usage:
 *   <script src="/js/sphere-alert.js" defer></script>
 *
 * Optional config via data attributes on the script tag:
 *   data-subdomain="alert"              (default: "alert")
 *   data-domain="example.com"           (default: auto-detect from location)
 *   data-resolver="cloudflare"          (cloudflare | google)
 *   data-z-index="9999"                 (default: 9999)
 */
(function () {
  'use strict';

  // --- Config -----------------------------------------------------------------

  const script = document.currentScript;
  const cfg = {
    subdomain: script?.dataset.subdomain ?? 'alert',
    domain:    script?.dataset.domain    ?? autoDetectDomain(),
    resolver:  script?.dataset.resolver  ?? 'cloudflare',
    zIndex:    parseInt(script?.dataset.zIndex ?? '9999', 10),
  };

  const RESOLVERS = {
    cloudflare: 'https://cloudflare-dns.com/dns-query',
    google:     'https://dns.google/resolve',
  };

  // Levels indexed 0-4. Position in array = level number.
  const LEVELS = [
    { name: 'info',     bg: '#E6F1FB', border: '#185FA5', text: '#042C53', label: 'Info' },
    { name: 'low',      bg: '#EAF3DE', border: '#3B6D11', text: '#173404', label: 'Notice' },
    { name: 'medium',   bg: '#FAEEDA', border: '#854F0B', text: '#412402', label: 'Warning' },
    { name: 'high',     bg: '#FAECE7', border: '#993C1D', text: '#4A1B0C', label: 'Alert' },
    { name: 'critical', bg: '#FCEBEB', border: '#A32D2D', text: '#501313', label: 'Critical' },
  ];

  const SLOT_COUNT = 3;
  const MAX_LEN = 1000;           // hard cap on raw TXT length before parse
  const MAX_MSG_LEN = 240;        // truncation cap on the rendered message
  const SCROLL_MS_PER_CHAR = 50;  // animation pacing — ~50ms per character

  // --- Helpers ----------------------------------------------------------------

  function autoDetectDomain() {
    let host = window.location.hostname;
    if (host.startsWith('www.')) host = host.slice(4);
    return host;
  }

  function getSlotNames() {
    const base = cfg.subdomain;
    const slots = [base];
    for (let i = 2; i <= SLOT_COUNT; i++) slots.push(base + i);
    return slots;
  }

  function escapeHtml(str) {
    return String(str).replace(/[&<>"']/g, c => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
  }

  /**
   * Unwrap DoH TXT response into raw content.
   * Walks each "..."-quoted chunk, honors \" as an escaped quote inside,
   * concatenates chunk contents. Preserves any character that was inside
   * the quoted region — including JSON's structural quotes when properly
   * escaped at the DNS level.
   */
  function unwrapTxtData(raw) {
    if (!raw) return '';
    const s = String(raw);
    let out = '';
    let i = 0;
    let foundAnyChunk = false;

    while (i < s.length) {
      while (i < s.length && /\s/.test(s[i])) i++;
      if (i >= s.length) break;

      if (s[i] === '"') {
        i++;
        let chunk = '';
        while (i < s.length && s[i] !== '"') {
          if (s[i] === '\\' && i + 1 < s.length) {
            chunk += s[i + 1];
            i += 2;
          } else {
            chunk += s[i];
            i++;
          }
        }
        if (i < s.length && s[i] === '"') i++;
        out += chunk;
        foundAnyChunk = true;
      } else {
        out += s[i];
        i++;
      }
    }

    return foundAnyChunk ? out : s.trim();
  }

  // --- Parser -----------------------------------------------------------------

  function parseAlert(raw) {
    if (!raw || typeof raw !== 'string') return null;
    const clean = raw.trim().slice(0, MAX_LEN);
    if (clean === '' || !clean.startsWith('{')) return null;

    let obj;
    try {
      obj = JSON.parse(clean);
    } catch {
      return null;
    }

    let msg = String(obj.m ?? '').trim();
    if (!msg) return null;
    if (msg.length > MAX_MSG_LEN) {
      msg = msg.slice(0, MAX_MSG_LEN - 1).trimEnd() + '…';
    }

    const levelIdx = Number.isInteger(obj.l) && obj.l >= 0 && obj.l < LEVELS.length
      ? obj.l
      : 0;

    const dismissible = obj.d !== 0;
    const forceScroll = obj.s === 1;

    return { level: levelIdx, msg, dismissible, forceScroll };
  }

  // --- DoH fetch --------------------------------------------------------------

  async function fetchAlertTxt(slotName) {
    const name = `${slotName}.${cfg.domain}`;
    const url = `${RESOLVERS[cfg.resolver] || RESOLVERS.cloudflare}?name=${encodeURIComponent(name)}&type=TXT`;
    const res = await fetch(url, {
      headers: { 'Accept': 'application/dns-json' },
      credentials: 'omit',
      redirect: 'follow',
    });
    if (!res.ok) throw new Error(`DoH HTTP ${res.status}`);
    const data = await res.json();
    if (!Array.isArray(data.Answer) || data.Answer.length === 0) return '';
    return unwrapTxtData(data.Answer[0].data ?? '');
  }

  async function fetchAllAlerts() {
    const slots = getSlotNames();
    const results = await Promise.allSettled(slots.map(s => fetchAlertTxt(s)));
    return results.map(r => r.status === 'fulfilled' ? r.value : '');
  }

  // --- Render -----------------------------------------------------------------

  function attachScrollBehavior(msgContainer, msgInner, alert) {
    // Decide if scrolling should be enabled at all.
    // Auto: only if content overflows. Forced: always.
    const overflowAmount = msgInner.scrollWidth - msgContainer.clientWidth;
    const hasOverflow = overflowAmount > 0;
    if (!alert.forceScroll && !hasOverflow) return;

    msgContainer.style.cursor = 'pointer';

    // Compute scroll distance and duration.
    // If forced but not overflowing, scroll by content width as a "show off"
    // animation. Otherwise scroll by the actual overflow amount.
    const distance = hasOverflow ? overflowAmount : msgInner.scrollWidth;
    const duration = Math.max(2000, alert.msg.length * SCROLL_MS_PER_CHAR);

    function startScroll() {
      msgInner.style.transition = `transform ${duration}ms linear`;
      msgInner.style.transform = `translateX(-${distance}px)`;
    }

    function resetScroll() {
      msgInner.style.transition = 'none';
      msgInner.style.transform = 'translateX(0)';
    }

    msgContainer.addEventListener('mouseenter', startScroll);
    msgContainer.addEventListener('mouseleave', resetScroll);
    msgContainer.addEventListener('click', () => {
      resetScroll();
      // Force reflow so the next transition takes effect from the reset state.
      void msgInner.offsetWidth;
      startScroll();
    });
  }

  function buildBanner(alert) {
    const lvl = LEVELS[alert.level];
    if (!lvl) return null;

    const banner = document.createElement('div');
    banner.setAttribute('role', 'status');
    banner.setAttribute('aria-live', 'polite');
    banner.style.cssText = `
      position: relative !important;
      z-index: 1 !important;
      display: flex !important;
      align-items: center !important;
      justify-content: center !important;
      gap: 12px !important;
      padding: 12px 20px !important;
      margin: 0 !important;
      background: ${lvl.bg} !important;
      color: ${lvl.text} !important;
      border-bottom: 1px solid ${lvl.border} !important;
      border-left: 4px solid ${lvl.border} !important;
      font: 14px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif !important;
      box-sizing: border-box !important;
      width: 100% !important;
      float: none !important;
      clear: both !important;
    `;

    const label = document.createElement('span');
    label.textContent = lvl.label;
    label.style.cssText = `
      flex-shrink: 0;
      font-weight: 600;
      font-size: 12px;
      text-transform: uppercase;
      letter-spacing: 0.05em;
      padding: 2px 8px;
      border: 1px solid ${lvl.border};
      border-radius: 4px;
      margin-top: 1px;
    `;

    // Message gets a clipping container + inner sliding element.
    // The container is the "window"; the inner translates left on hover.
    const msgContainer = document.createElement('div');
    msgContainer.style.cssText = `
      flex: 1 1 auto;
      min-width: 0;
      max-width: 75%;
      overflow: hidden;
      white-space: nowrap;
    `;

    const msgInner = document.createElement('div');
    msgInner.style.cssText = `
      display: inline-block;
      white-space: nowrap;
      will-change: transform;
      transform: translateX(0);
    `;
    msgInner.innerHTML = escapeHtml(alert.msg);

    msgContainer.appendChild(msgInner);
    banner.appendChild(label);
    banner.appendChild(msgContainer);

    if (alert.dismissible) {
      const close = document.createElement('button');
      close.setAttribute('aria-label', 'Dismiss alert');
      close.innerHTML = '&times;';
      close.style.cssText = `
        position: absolute;
        right: 12px;
        top: 50%;
        transform: translateY(-50%);
        background: transparent;
        border: 0;
        color: ${lvl.text};
        font-size: 22px;
        line-height: 1;
        padding: 0 4px;
        cursor: pointer;
        opacity: 0.7;
      `;
      close.addEventListener('mouseenter', () => close.style.opacity = '1');
      close.addEventListener('mouseleave', () => close.style.opacity = '0.7');
      close.addEventListener('click', () => banner.remove());
      banner.appendChild(close);
    }

    // Stash refs so we can wire up scroll after insertion (when sizes are real).
    banner._msgContainer = msgContainer;
    banner._msgInner = msgInner;
    banner._alert = alert;

    return banner;
  }

  function renderAll(slotRaws) {
    const banners = [];
    for (const raw of slotRaws) {
      const alert = parseAlert(raw);
      if (!alert) continue;
      const banner = buildBanner(alert);
      if (banner) banners.push(banner);
    }
    if (banners.length === 0) return;

    const wrap = document.createElement('div');
    wrap.id = 'sphere-alert-stack';
	wrap.style.cssText = `
	  position: fixed !important;
	  top: 0 !important;
	  left: 0 !important;
	  right: 0 !important;
	  z-index: ${cfg.zIndex} !important;
	  display: block !important;
	  width: 100% !important;
	  margin: 0 !important;
	  padding: 0 !important;
	`;
    for (const banner of banners) wrap.appendChild(banner);

    document.body.insertBefore(wrap, document.body.firstChild);

    // Now that banners are in the DOM and have real layout dimensions,
    // wire up scroll behavior on those that need it.
    for (const banner of banners) {
      attachScrollBehavior(banner._msgContainer, banner._msgInner, banner._alert);
    }
  }

  // --- Boot -------------------------------------------------------------------

  async function boot() {
    try {
      const slotRaws = await fetchAllAlerts();
      renderAll(slotRaws);
    } catch (err) {
      if (window.console && console.debug) {
        console.debug('[sphere-alert] boot failed:', err.message);
      }
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();