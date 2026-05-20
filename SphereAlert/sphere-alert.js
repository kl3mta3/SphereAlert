/**
 * site-alert.js — DNS-driven alert banner
 *
 * Reads a TXT record at alert.<your-domain> via DNS-over-HTTPS and renders
 * a dismissible banner at the top of the page. Edit one DNS record to push
 * an alert to every visitor within the TTL window. Delete it to clear.
 *
 * TXT record value format:
 *   ""                           → no banner
 *   "::none::"                   → no banner (draft mode: keep text, hide)
 *   "::none:: anything"          → no banner (level wins)
 *   "message"                    → banner shown at default level (info)
 *   "::level:: message"          → banner shown at named level
 *
 * Valid levels: none, info, low, medium, high, critical
 * Invalid level names fall back to default (info), not hidden.
 *
 * Drop-in usage:
 *   <script src="/site-alert.js" defer></script>
 *
 * Optional config via data attributes on the script tag:
 *   data-subdomain="alert"              (default: "alert")
 *   data-domain="kennethlasyone.org"    (default: auto-detect from location)
 *   data-resolver="cloudflare"          (cloudflare | google, default: cloudflare)
 *   data-dismissible="true"             (default: true)
 *   data-default-level="info"           (default: "info")
 *   data-z-index="9999"                 (default: 9999)
 */
(function () {
  'use strict';

  // --- Config -----------------------------------------------------------------

  const script = document.currentScript;
  const cfg = {
    subdomain:    script?.dataset.subdomain    ?? 'alert',
    domain:       script?.dataset.domain       ?? autoDetectDomain(),
    resolver:     script?.dataset.resolver     ?? 'cloudflare',
    dismissible:  script?.dataset.dismissible  !== 'false',
    defaultLevel: script?.dataset.defaultLevel ?? 'info',
    zIndex:       parseInt(script?.dataset.zIndex ?? '9999', 10),
  };

  const RESOLVERS = {
    cloudflare: 'https://cloudflare-dns.com/dns-query',
    google:     'https://dns.google/resolve',
  };

  const LEVELS = {
    none:     { display: false },
    info:     { display: true, bg: '#E6F1FB', border: '#185FA5', text: '#042C53', label: 'Info' },
    low:      { display: true, bg: '#EAF3DE', border: '#3B6D11', text: '#173404', label: 'Notice' },
    medium:   { display: true, bg: '#FAEEDA', border: '#854F0B', text: '#412402', label: 'Warning' },
    high:     { display: true, bg: '#FAECE7', border: '#993C1D', text: '#4A1B0C', label: 'Alert' },
    critical: { display: true, bg: '#FCEBEB', border: '#A32D2D', text: '#501313', label: 'Critical' },
  };

  const MAX_LEN = 280;
  const STORAGE_KEY_PREFIX = '__site_alert_dismissed__:';

  // --- Domain auto-detection --------------------------------------------------

  function autoDetectDomain() {
    let host = window.location.hostname;
    // Strip leading www. so www.example.com → example.com → alert.example.com
    if (host.startsWith('www.')) host = host.slice(4);
    return host;
  }

  // --- Parser -----------------------------------------------------------------

  function parseAlert(raw) {
    if (!raw || typeof raw !== 'string') return { level: 'none', msg: '' };
    const clean = raw.trim().slice(0, MAX_LEN);
    if (clean === '') return { level: 'none', msg: '' };

    const match = clean.match(/^::(\w+)::\s*(.*)$/);
    if (match) {
      const rawLevel = match[1].toLowerCase();
      const level = LEVELS[rawLevel] ? rawLevel : cfg.defaultLevel;
      return { level, msg: match[2].trim() };
    }
    return { level: cfg.defaultLevel, msg: clean };
  }

  // --- DoH fetch --------------------------------------------------------------

  async function fetchAlertTxt() {
    const name = `${cfg.subdomain}.${cfg.domain}`;
    const url = `${RESOLVERS[cfg.resolver] || RESOLVERS.cloudflare}?name=${encodeURIComponent(name)}&type=TXT`;
    const res = await fetch(url, {
      headers: { 'Accept': 'application/dns-json' },
      // Don't send credentials, don't follow weird redirects
      credentials: 'omit',
      redirect: 'follow',
    });
    if (!res.ok) throw new Error(`DoH HTTP ${res.status}`);
    const data = await res.json();
    // data.Answer is an array of records. TXT data comes as a quoted string,
    // possibly with multiple concatenated strings: "part1""part2"
    if (!Array.isArray(data.Answer) || data.Answer.length === 0) return '';
    const raw = data.Answer[0].data ?? '';
    // Strip outer quotes and join multi-string TXT records.
    return raw.replace(/^"|"$/g, '').replace(/""/g, '');
  }

  // --- Render -----------------------------------------------------------------

  function escapeHtml(str) {
    return String(str).replace(/[&<>"']/g, c => ({
      '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;'
    }[c]));
  }

  function hashMessage(msg) {
    // Tiny non-crypto hash so dismissing "alert A" doesn't dismiss "alert B"
    let h = 0;
    for (let i = 0; i < msg.length; i++) {
      h = ((h << 5) - h) + msg.charCodeAt(i);
      h |= 0;
    }
    return h.toString(36);
  }

  function isDismissed(msg) {
    if (!cfg.dismissible) return false;
    try {
      return localStorage.getItem(STORAGE_KEY_PREFIX + hashMessage(msg)) === '1';
    } catch {
      return false;
    }
  }

  function markDismissed(msg) {
    try {
      localStorage.setItem(STORAGE_KEY_PREFIX + hashMessage(msg), '1');
    } catch {
      // localStorage disabled / full / private mode — no-op
    }
  }

  function render(alert) {
    const lvl = LEVELS[alert.level];
    if (!lvl?.display || !alert.msg) return;
    if (isDismissed(alert.msg)) return;

    const banner = document.createElement('div');
    banner.setAttribute('role', 'status');
    banner.setAttribute('aria-live', 'polite');
    banner.style.cssText = `
      position: relative;
      z-index: ${cfg.zIndex};
      display: flex;
      align-items: flex-start;
      gap: 12px;
      padding: 12px 20px;
      background: ${lvl.bg};
      color: ${lvl.text};
      border-bottom: 1px solid ${lvl.border};
      border-left: 4px solid ${lvl.border};
      font: 14px/1.5 -apple-system, BlinkMacSystemFont, "Segoe UI", system-ui, sans-serif;
      box-sizing: border-box;
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

    const msg = document.createElement('div');
    msg.style.cssText = 'flex: 1; min-width: 0;';
    msg.innerHTML = escapeHtml(alert.msg);

    banner.appendChild(label);
    banner.appendChild(msg);

    if (cfg.dismissible) {
      const close = document.createElement('button');
      close.setAttribute('aria-label', 'Dismiss alert');
      close.innerHTML = '&times;';
      close.style.cssText = `
        flex-shrink: 0;
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
      close.addEventListener('click', () => {
        markDismissed(alert.msg);
        banner.remove();
      });
      banner.appendChild(close);
    }

    document.body.insertBefore(banner, document.body.firstChild);
  }

  // --- Boot -------------------------------------------------------------------

  async function boot() {
    try {
      const raw = await fetchAlertTxt();
      const alert = parseAlert(raw);
      render(alert);
    } catch (err) {
      // Fail silent. A broken banner is worse than no banner.
      if (window.console && console.debug) {
        console.debug('[site-alert] fetch failed:', err.message);
      }
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', boot);
  } else {
    boot();
  }
})();
