window.WHA = (function () {
  const modules = {};
  const cache = {};
  let currentMod = 'audit';
  let activeJobTimer = null;

  function toast(msg) {
    if (!msg) return;
    let box = document.getElementById('wha-toast');
    if (!box) {
      box = document.createElement('div');
      box.id = 'wha-toast';
      box.className = 'wha-toast';
      document.body.appendChild(box);
    }
    box.textContent = msg;
    box.classList.add('show');
    clearTimeout(box._t);
    box._t = setTimeout(function () { box.classList.remove('show'); }, 2600);
  }

  function lang() {
    return window.WHA_LANG === 'ru' ? 'ru' : 'en';
  }

  function t(en, ru) {
    return lang() === 'ru' ? (ru || en) : en;
  }

  function esc(s) {
    return String(s == null ? '' : s).replace(/[&<>"']/g, function (c) {
      return { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c];
    });
  }

  function fmtBytes(bytes) {
    const b = Number(bytes || 0);
    if (b < 1024) return b + ' B';
    if (b < 1048576) return (b / 1024).toFixed(1) + ' KB';
    if (b < 1073741824) return (b / 1048576).toFixed(1) + ' MB';
    return (b / 1073741824).toFixed(2) + ' GB';
  }

  async function api(mod, action, args) {
    const r = await fetch('/api/mod/' + encodeURIComponent(mod) + '/' + encodeURIComponent(action), {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(args || {})
    });
    return await r.json();
  }

  async function runJob(mod, action, args, logEl, onDone) {
    if (activeJobTimer) { clearInterval(activeJobTimer); activeJobTimer = null; }
    if (logEl) {
      logEl.classList.remove('hidden');
      logEl.textContent = t('Starting job...', 'Запуск задачи...');
    }
    const start = await fetch('/api/job/start', {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ mod: mod, action: action, args: args || {} })
    }).then(function (r) { return r.json(); });

    if (!start.ok) {
      if (logEl) logEl.textContent = 'Error: ' + (start.error || 'failed');
      if (onDone) onDone({ ok: false, error: start.error });
      return;
    }

    const jid = start.id;
    activeJobTimer = setInterval(async function () {
      try {
        const st = await fetch('/api/job/status?id=' + encodeURIComponent(jid)).then(function (r) { return r.json(); });
        if (logEl && st.logs && st.logs.length) {
          logEl.textContent = st.logs.join('\n');
          logEl.scrollTop = logEl.scrollHeight;
        }
        if (st.done) {
          clearInterval(activeJobTimer);
          activeJobTimer = null;
          if (onDone) onDone(st.result || { ok: !st.error, error: st.error });
        }
      } catch (e) {
        clearInterval(activeJobTimer);
        activeJobTimer = null;
        if (onDone) onDone({ ok: false, error: String(e) });
      }
    }, 650);
  }

  function registerModule(id, modObj) {
    modules[id] = modObj;
  }

  function switchModule(id) {
    if (modules[currentMod] && modules[currentMod].leave) {
      try { modules[currentMod].leave(); } catch (e) { }
    }
    currentMod = id;
    document.querySelectorAll('.mod-btn').forEach(function (b) {
      b.classList.toggle('active', b.getAttribute('data-mod') === id);
    });

    const isAudit = (id === 'audit');
    const auditEls = [
      document.querySelector('.stats'),
      document.querySelector('.bar'),
      document.querySelector('main.content'),
      document.querySelector('footer')
    ];
    auditEls.forEach(function (el) {
      if (el) el.classList.toggle('hidden', !isAudit);
    });
    const crit = document.getElementById('audit-criteria');
    if (crit && !isAudit) crit.classList.add('hidden');

    const modView = document.getElementById('mod-view');
    if (modView) {
      modView.classList.toggle('hidden', isAudit);
      if (!isAudit && modules[id] && modules[id].render) {
        modules[id].render(modView);
      }
    }
  }

  function refreshActiveModule() {
    if (currentMod !== 'audit' && modules[currentMod] && modules[currentMod].render) {
      const modView = document.getElementById('mod-view');
      if (modView) modules[currentMod].render(modView);
    }
  }

  return {
    t: t,
    esc: esc,
    fmtBytes: fmtBytes,
    api: api,
    runJob: runJob,
    cache: cache,
    toast: toast,
    registerModule: registerModule,
    switchModule: switchModule,
    refreshActiveModule: refreshActiveModule
  };
})();
