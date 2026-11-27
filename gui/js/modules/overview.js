(function () {
  const { t, esc, api, cache } = WHA;
  let liveTimer = null;

  async function render(root) {
    let ov = cache.ov;
    let tel = cache.tel;
    let cnt = cache.ovCounts;
    let sec = cache.sec;

    if (!ov || !tel) {
      root.innerHTML = `<div class="mod-head"><div><div class="mod-title">${t('Loading PC profile...', 'Загрузка профиля ПК...')}</div></div></div>`;
      const [rOv, rTel, rCnt, rSec] = await Promise.all([
        api('system', 'overview'),
        api('system', 'telemetry'),
        api('system', 'overview_counts'),
        api('privacy', 'overview')
      ]);
      ov = cache.ov = rOv;
      tel = cache.tel = rTel;
      cnt = cache.ovCounts = rCnt;
      sec = cache.sec = rSec;
    } else {
      // Instant render from cache; refresh live telemetry quietly in background
      api('system', 'telemetry').then(t2 => {
        if (t2 && t2.ok) { cache.tel = t2; drawLiveBars(t2); }
      });
    }

    const drv = { count: (cnt && cnt.drivers) || 0 };
    const prg = { count: (cnt && cnt.programs) || 0 };

    if (!ov.ok) {
      root.innerHTML = `<div class="esll-card">${esc(ov.error || 'Error')}</div>`;
      return;
    }

    const art = window.WHAHardwareArt || {};
    const cDrive = (tel.disks || []).find(d => (d.drive || '').toUpperCase().startsWith('C')) || (tel.disks || [])[0];
    const gpu0 = (ov.gpus || [])[0] || {};
    const gpuTel = tel.gpu || {};
    const dispText = (ov.displays || []).map(d => `${d.width}×${d.height}`).join(' · ') || '—';

    root.innerHTML = `
      <div class="bento-hero">
        <div>
          <div class="bento-model">${esc(ov.board || ov.model || ov.hostname)}</div>
          <div class="mod-sub">${ov.isLaptop ? t('Laptop', 'Ноутбук') : t('Desktop', 'ПК')}, ${esc(ov.manufacturer || '')} ${esc(ov.model || '')}. ${esc(ov.os)}</div>
        </div>
        <div class="bento-kpis">
          <div class="bento-kpi" onclick="WHA.switchModule('drivers')"><b>${drv.count || 0}</b><span>${t('drivers', 'драйверов')}</span></div>
          <div class="bento-kpi" onclick="WHA.switchModule('programs')"><b>${prg.count || 0}</b><span>${t('programs', 'программ')}</span></div>
          <div class="bento-kpi" onclick="WHA.switchModule('storage')"><b>${cDrive ? Number(cDrive.freeGB).toFixed(1) : '—'}</b><span>${t('GB free on C:', 'ГБ свободно C:')}</span></div>
        </div>
      </div>

      <div class="bento-layout">
        <!-- Left: Right now live -->
        <div class="esll-card bento-live">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:12px">
            <h4 style="margin:0">${t('Right now', 'Прямо сейчас')}</h4>
            <span class="badge">LIVE</span>
          </div>
          <div id="ov-live-bars"></div>
        </div>

        <!-- Center: Components Bento -->
        <div class="bento-center">
          <div class="bento-top2">
            <div class="esll-card bento-comp" data-comp="gpu">
              <div class="bento-art">${art.gpu || ''}</div>
              <h4>${t('Graphics', 'Видеокарта')}</h4>
              <div class="bento-cname">${esc(gpuTel.name || gpu0.name || '—')}</div>
              <div class="bento-cval" id="ov-gpu-val">${gpuTel.load != null ? gpuTel.load + '%' : '—'} <small>${gpuTel.tempC != null ? gpuTel.tempC + '°C' : ''}${gpuTel.powerW ? ' · ' + gpuTel.powerW + 'W' : ''}</small></div>
              <div class="esll-meta" id="ov-vram-txt">${gpuTel.vramTotalMB ? ((gpuTel.vramUsedMB != null ? gpuTel.vramUsedMB + ' / ' : '') + gpuTel.vramTotalMB + ' MB VRAM') : ''}</div>
              <div class="meter"><i id="ov-vram-bar" style="width:${gpuTel.vramTotalMB && gpuTel.vramUsedMB ? Math.round((gpuTel.vramUsedMB / gpuTel.vramTotalMB) * 100) : 0}%"></i></div>
            </div>

            <div class="esll-card bento-comp" data-comp="cpu">
              <div class="bento-art cpu-art">${art.cpu || ''}</div>
              <h4>${t('Processor', 'Процессор')}</h4>
              <div class="bento-cname">${esc(ov.cpu.name)}</div>
              <div class="bento-cval" id="ov-cpu-val">${tel.cpuLoad}% <small>${(tel.cpuClock / 1000).toFixed(2)} GHz${tel.cpuTempC != null ? ' · ' + tel.cpuTempC + '°C' : ''}</small></div>
              <div class="esll-meta">${ov.cpu.cores} ${t('cores', 'ядер')} / ${ov.cpu.threads} ${t('threads', 'потоков')}</div>
              <div class="meter"><i id="ov-cpu-bar" style="width:${tel.cpuLoad}%"></i></div>
            </div>
          </div>

          <div class="bento-subgrid">
            <div class="esll-card bento-comp" data-comp="ram">
              <div class="bento-mini-art">${art.ram || ''}</div>
              <h4>${t('Memory', 'Память')}</h4>
              <div class="bento-cname">${ov.ram.totalGB} GB RAM</div>
              <div class="esll-meta" id="ov-ram-txt">${Number(ov.ram.usedGB).toFixed(1)} / ${Number(ov.ram.totalGB).toFixed(1)} GB (${ov.ram.pct}%)</div>
              <div class="meter"><i id="ov-ram-bar" style="width:${ov.ram.pct}%"></i></div>
            </div>

            <div class="esll-card bento-comp" data-comp="storage">
              <div class="bento-mini-art">${art.storage || ''}</div>
              <h4>${t('Storage', 'Накопители')}</h4>
              <div class="bento-cname">${(tel.physDisks && tel.physDisks[0]) ? esc(tel.physDisks[0].name) : (cDrive ? esc(cDrive.drive) : 'Disk')}</div>
              <div class="esll-meta">${cDrive ? Number(cDrive.freeGB).toFixed(1) + ' GB ' + t('free of', 'свободно из') + ' ' + Number(cDrive.totalGB).toFixed(1) + ' GB' : '—'}</div>
              <div class="meter"><i style="width:${cDrive ? cDrive.usedPct : 0}%"></i></div>
            </div>

            <div class="esll-card bento-comp" data-comp="displays">
              <div class="bento-mini-art">${art.displays || ''}</div>
              <h4>${t('Displays', 'Экраны')}</h4>
              <div class="bento-cname">${(ov.displays || []).length} ${t('display(s)', 'экр.')}</div>
              <div class="esll-meta">${esc(dispText)}</div>
            </div>

            <div class="esll-card bento-comp" data-comp="network">
              <div class="bento-mini-art">${art.network || ''}</div>
              <h4>${t('Network', 'Сеть')}</h4>
              <div class="bento-cname">${tel.wifi ? esc(tel.wifi.ssid) : ((tel.net && tel.net[0]) ? esc(tel.net[0].name) : 'Ethernet')}</div>
              <div class="esll-meta">${(tel.net && tel.net[0]) ? esc(tel.net[0].speed || tel.net[0].ip || 'Connected') : '—'}</div>
            </div>

            <div class="esll-card bento-comp" onclick="WHA.switchModule('tester')">
              <div class="bento-mini-art">${art.audio || ''}</div>
              <h4>${t('Audio & Input', 'Аудио и ввод')}</h4>
              <div class="bento-cname">${t('Device Tester', 'Тестер устройств')}</div>
              <div class="esll-meta">${t('Speakers, mic, camera, keyboard', 'Звук, микрофон, камера, клавиши')}</div>
            </div>

            <div class="esll-card bento-comp" onclick="WHA.switchModule('audit')">
              <div class="bento-mini-art">${art.system || ''}</div>
              <h4>${t('System', 'Система')}</h4>
              <div class="bento-cname">${esc(ov.os.split('(')[0])}</div>
              <div class="esll-meta">${t('Uptime', 'Аптайм')}: ${ov.uptimeHours} ${t('h', 'ч')}</div>
            </div>
          </div>
        </div>

        <!-- Right: Attention & System Health -->
        <div class="bento-right">
          <div class="esll-card">
            <h4>${t('Attention', 'Требует внимания')}</h4>
            <div class="bento-act" onclick="WHA.switchModule('audit')"><span>${t('Health Audit', 'Аудит здоровья')}</span><span>›</span></div>
            <div class="bento-act" onclick="WHA.switchModule('drivers')"><span>${t('Drivers', 'Драйверы')} (${drv.count || 0})</span><span>›</span></div>
            <div class="bento-act" onclick="WHA.switchModule('programs')"><span>${t('Program updates', 'Обновления программ')}</span><span>›</span></div>
            <div class="bento-act" onclick="WHA.switchModule('optimizer')"><span>${t('Scan what can be cleaned', 'Сканировать очистку')}</span><span>›</span></div>
          </div>

          <div class="esll-card">
            <h4>${t('System Health', 'Состояние системы')}</h4>
            <div style="display:flex;align-items:center;gap:14px;margin-bottom:10px">
              <div class="health-ring"><span>${sec.ok ? sec.score : 100}</span></div>
              <div>
                <div style="font-size:16px">${sec.score >= 85 ? t('Good', 'Хорошее') : t('Check needed', 'Есть замечания')}</div>
                <div class="esll-meta">${t('Security & hardware status', 'Безопасность и оборудование')}</div>
              </div>
            </div>
            <div class="esll-meta">${t('Uptime', 'Аптайм')}: ${ov.uptimeHours} ${t('h', 'ч')}</div>
            <div class="esll-meta">${t('BIOS', 'BIOS')}: ${esc(ov.bios)}</div>
          </div>

          <div class="esll-card" id="ov-trends-card">
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
              <h4 style="margin:0">${t('Trends', 'Тренды')}</h4>
              <span class="badge" style="cursor:pointer" onclick="WHA.switchModule('audit')">${t('History', 'История')}</span>
            </div>
            <div id="ov-trends-body" class="esll-meta">${t('Loading...', 'Загрузка...')}</div>
          </div>
        </div>
      </div>
    `;

    function drawLiveBars(tData) {
      const box = root.querySelector('#ov-live-bars');
      if (!box || !tData || !tData.ok) return;
      const cd = (tData.disks || []).find(d => (d.drive || '').toUpperCase().startsWith('C')) || (tData.disks || [])[0];
      const rows = [
        { k: t('Processor', 'Процессор'), v: tData.cpuLoad + '%', p: tData.cpuLoad },
        { k: t('Graphics', 'Графика'), v: (tData.gpu && tData.gpu.load != null ? tData.gpu.load + '%' : '—'), p: (tData.gpu && tData.gpu.load) || 0 },
        { k: t('Memory', 'Память'), v: `${(tData.ram.usedMB / 1024).toFixed(1)} / ${(tData.ram.totalMB / 1024).toFixed(0)} GB`, p: tData.ram.pct },
        { k: t('Drive C:', 'Диск C:'), v: cd ? `${(cd.totalGB - cd.freeGB).toFixed(1)} / ${Number(cd.totalGB).toFixed(1)} GB` : '—', p: cd ? cd.usedPct : 0 },
        { k: t('Network', 'Сеть'), v: (tData.net && tData.net[0]) ? (tData.net[0].speed || 'Up') : '—', p: (tData.net && tData.net.length) ? 35 : 0 }
      ];
      box.innerHTML = rows.map(r => `
        <div style="margin-bottom:14px">
          <div style="display:flex;justify-content:space-between;font-size:12px">
            <span style="color:var(--dim)">${r.k}</span>
            <span class="mono">${esc(r.v)}</span>
          </div>
          <div class="meter" style="margin-top:6px"><i style="width:${Math.min(100, Math.max(0, r.p))}%"></i></div>
        </div>
      `).join('');

      const cpuEl = root.querySelector('#ov-cpu-val');
      const cpuBar = root.querySelector('#ov-cpu-bar');
      if (cpuEl) cpuEl.innerHTML = `${tData.cpuLoad}% <small>${(tData.cpuClock / 1000).toFixed(2)} GHz${tData.cpuTempC != null ? ' · ' + tData.cpuTempC + '°C' : ''}</small>`;
      if (cpuBar) cpuBar.style.width = tData.cpuLoad + '%';
    }

    function openCompModal(kind) {
      const old = document.getElementById('ov-comp-modal');
      if (old) old.remove();
      const tCur = cache.tel || tel;
      let title = '', rows = [], goMod = 'monitor';

      if (kind === 'gpu') {
        title = t('Graphics', 'Видеокарта');
        const g = tCur.gpu || {};
        rows = [
          [t('Model', 'Модель'), g.name || gpu0.name || '—'],
          [t('Driver', 'Драйвер'), gpu0.driver || '—'],
          [t('Load / Temp', 'Нагрузка / Темп.'), (g.load != null ? g.load + '%' : '—') + (g.tempC != null ? ' · ' + g.tempC + '°C' : '')],
          ['VRAM', g.vramTotalMB ? ((g.vramUsedMB != null ? g.vramUsedMB + ' / ' : '') + g.vramTotalMB + ' MB') : '—'],
          [t('Resolution', 'Разрешение'), gpu0.res || '—']
        ];
      } else if (kind === 'cpu') {
        title = t('Processor', 'Процессор');
        rows = [
          [t('Model', 'Модель'), ov.cpu.name],
          [t('Cores / Threads', 'Ядра / Потоки'), `${ov.cpu.cores} / ${ov.cpu.threads}`],
          [t('Current Clock', 'Текущая частота'), `${(tCur.cpuClock / 1000).toFixed(2)} GHz (max ${(ov.cpu.maxMhz / 1000).toFixed(2)} GHz)`],
          [t('Load / Temp', 'Нагрузка / Темп.'), `${tCur.cpuLoad}%${tCur.cpuTempC != null ? ' · ' + tCur.cpuTempC + '°C' : ''}`]
        ];
      } else if (kind === 'ram') {
        title = t('Memory', 'Память');
        rows = [
          [t('Total', 'Всего'), `${Number(ov.ram.totalGB).toFixed(1)} GB`],
          [t('Used', 'Занято'), `${(tCur.ram.usedMB / 1024).toFixed(1)} GB (${tCur.ram.pct}%)`],
          [t('Free', 'Свободно'), `${(tCur.ram.freeMB / 1024).toFixed(1)} GB`]
        ];
      } else if (kind === 'storage') {
        title = t('Storage', 'Накопители');
        goMod = 'storage';
        rows = (tCur.disks || []).map(d => [`${d.drive} ${d.label || ''}`, `${Number(d.freeGB).toFixed(1)} GB ${t('free of', 'из')} ${Number(d.totalGB).toFixed(1)} GB (${d.fs})`]);
        (tCur.physDisks || []).forEach(p => rows.push([p.name, `${p.sizeGB} GB · ${p.media} · ${p.health}`]));
      } else if (kind === 'displays') {
        title = t('Displays', 'Экраны');
        goMod = 'tester';
        rows = (ov.displays || []).map((d, i) => [`#${i + 1} ${d.name}`, `${d.width}×${d.height}${d.primary ? ' (Primary)' : ''}`]);
      } else if (kind === 'network') {
        title = t('Network', 'Сеть');
        rows = (tCur.net || []).map(n => [n.name, `${n.ip || '—'} · ${n.speed || ''} · ${n.mac || ''}`]);
        if (tCur.wifi) rows.unshift(['Wi-Fi', `${tCur.wifi.ssid} (${tCur.wifi.signal})`]);
      }

      const modal = document.createElement('div');
      modal.id = 'ov-comp-modal';
      modal.className = 'wha-modal-back';
      modal.innerHTML = `
        <div class="wha-modal" style="width:min(540px,100%)">
          <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:16px">
            <div style="font-size:18px;font-weight:300">${esc(title)}</div>
            <button class="pill small" id="ovc-close">✕</button>
          </div>
          ${rows.map(r => `<div class="dline"><div class="dk">${esc(r[0])}</div><div class="dv mono">${esc(r[1])}</div></div>`).join('')}
          <div class="mod-actions" style="margin-top:18px">
            <button class="pill small solid" id="ovc-go">${t('Open Module', 'Открыть модуль')}</button>
          </div>
        </div>`;
      document.body.appendChild(modal);
      modal.onclick = (e) => { if (e.target === modal) modal.remove(); };
      modal.querySelector('#ovc-close').onclick = () => modal.remove();
      modal.querySelector('#ovc-go').onclick = () => { modal.remove(); WHA.switchModule(goMod); };
    }

    root.querySelectorAll('[data-comp]').forEach(el => {
      el.onclick = () => openCompModal(el.getAttribute('data-comp'));
    });

    function drawTrends() {
      const tb = root.querySelector('#ov-trends-body');
      if (!tb) return;
      fetch('/api/state', { cache: 'no-store' }).then(r => r.json()).then(st => {
        const hist = (st && st.history) ? [].concat(st.history).reverse() : [];
        const metrics = [
          { label: t('Health', 'Здоровье'), unit: '/100', get: r => r.health },
          { label: t('C: free', 'C: свободно'), unit: '%', get: r => r.signals && r.signals.cFree },
          { label: 'WHEA/min', unit: '', get: r => r.signals && r.signals.wheaPerMin },
          { label: t('Boot', 'Загрузка'), unit: 's', get: r => r.signals && r.signals.bootSec }
        ];

        const rowsHtml = [];
        metrics.forEach(m => {
          const pts = [];
          hist.forEach((r, idx) => {
            const v = m.get(r);
            if (v !== null && v !== undefined) pts.push([idx, Number(v)]);
          });
          if (pts.length === 0) return;
          const last = Math.round(pts[pts.length - 1][1] * 10) / 10;
          let spark = '';
          let deltaTxt = '';
          if (pts.length >= 2) {
            const vals = pts.map(p => p[1]);
            const min = Math.min.apply(null, vals);
            const max = Math.max.apply(null, vals);
            const span = (max - min) || 1;
            const W = 72, H = 18, P = 2;
            const coords = pts.map(p => {
              const x = hist.length > 1 ? (p[0] / (hist.length - 1)) * W : W / 2;
              const y = P + (1 - (p[1] - min) / span) * (H - 2 * P);
              return x.toFixed(1) + ',' + y.toFixed(1);
            });
            spark = `<svg width="${W}" height="${H}" viewBox="0 0 ${W} ${H}" fill="none" stroke="rgba(255,255,255,.55)" stroke-width="1.3"><polyline points="${coords.join(' ')}"/></svg>`;
            const d = Math.round((last - vals[0]) * 10) / 10;
            deltaTxt = d > 0 ? `+${d}` : (d < 0 ? `${d}` : '0');
          }
          rowsHtml.push(`
            <div style="display:flex;align-items:center;justify-content:space-between;gap:8px;padding:5px 0;border-bottom:1px solid rgba(255,255,255,.05);font-size:12px">
              <span style="color:var(--dim);min-width:68px">${esc(m.label)}</span>
              ${spark}
              <span class="mono" style="color:var(--fg)">${last}${m.unit} ${deltaTxt ? `<span style="color:var(--faint)">(${deltaTxt})</span>` : ''}</span>
            </div>`);
        });

        if (!rowsHtml.length) {
          const cFreeNow = cDrive ? Math.max(0, 100 - cDrive.usedPct) : '—';
          tb.innerHTML = `
            <div style="display:flex;justify-content:space-between;padding:4px 0"><span>${t('C: free', 'C: свободно')}</span><b class="mono">${cFreeNow}%</b></div>
            <div class="esll-meta" style="margin-top:4px">${t('Run 2+ audits to see WHEA, boot & disk trends.', 'Запустите 2+ аудита для графиков WHEA, загрузки и диска.')}</div>
          `;
        } else {
          tb.innerHTML = rowsHtml.join('');
        }
      }).catch(() => {});
    }

    drawTrends();
    drawLiveBars(tel);
    if (liveTimer) clearInterval(liveTimer);
    liveTimer = setInterval(async () => {
      const t2 = await api('system', 'telemetry');
      if (t2 && t2.ok) cache.tel = t2;
      drawLiveBars(t2);
    }, 2500);
  }

  function leave() {
    if (liveTimer) { clearInterval(liveTimer); liveTimer = null; }
  }

  WHA.registerModule('overview', { render, leave });
})();
