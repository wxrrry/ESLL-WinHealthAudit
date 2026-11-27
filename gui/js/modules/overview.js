(function () {
  const { t, esc, api, switchModule } = WHA;
  let liveTimer = null;

  async function render(root) {
    root.innerHTML = `<div class="mod-head"><div><div class="mod-title">${t('Loading PC profile...', 'Загрузка профиля ПК...')}</div></div></div>`;

    const [ov, tel, drv, prg, sec] = await Promise.all([
      api('system', 'overview'),
      api('system', 'telemetry'),
      api('drivers', 'list'),
      api('programs', 'installed'),
      api('privacy', 'overview')
    ]);

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
          <div class="bento-kpi" onclick="WHA.switchModule('storage')"><b>${cDrive ? cDrive.freeGB : '—'}</b><span>${t('GB free on C:', 'ГБ свободно C:')}</span></div>
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
            <div class="esll-card bento-comp" onclick="WHA.switchModule('monitor')">
              <div class="bento-art">${art.gpu || ''}</div>
              <h4>${t('Graphics', 'Видеокарта')}</h4>
              <div class="bento-cname">${esc(gpuTel.name || gpu0.name || '—')}</div>
              <div class="bento-cval" id="ov-gpu-val">${gpuTel.load != null ? gpuTel.load + '%' : '—'} <small>${gpuTel.tempC != null ? gpuTel.tempC + '°C' : ''}${gpuTel.powerW ? ' · ' + gpuTel.powerW + 'W' : ''}</small></div>
              <div class="esll-meta" id="ov-vram-txt">${gpuTel.vramTotalMB ? ((gpuTel.vramUsedMB != null ? gpuTel.vramUsedMB + ' / ' : '') + gpuTel.vramTotalMB + ' MB VRAM') : ''}</div>
              <div class="meter"><i id="ov-vram-bar" style="width:${gpuTel.vramTotalMB && gpuTel.vramUsedMB ? Math.round((gpuTel.vramUsedMB / gpuTel.vramTotalMB) * 100) : 0}%"></i></div>
            </div>

            <div class="esll-card bento-comp" onclick="WHA.switchModule('monitor')">
              <div class="bento-art cpu-art">${art.cpu || ''}</div>
              <h4>${t('Processor', 'Процессор')}</h4>
              <div class="bento-cname">${esc(ov.cpu.name)}</div>
              <div class="bento-cval" id="ov-cpu-val">${tel.cpuLoad}% <small>${(tel.cpuClock / 1000).toFixed(2)} GHz${tel.cpuTempC != null ? ' · ' + tel.cpuTempC + '°C' : ''}</small></div>
              <div class="esll-meta">${ov.cpu.cores} ${t('cores', 'ядер')} / ${ov.cpu.threads} ${t('threads', 'потоков')}</div>
              <div class="meter"><i id="ov-cpu-bar" style="width:${tel.cpuLoad}%"></i></div>
            </div>
          </div>

          <div class="bento-subgrid">
            <div class="esll-card bento-comp" onclick="WHA.switchModule('monitor')">
              <div class="bento-mini-art">${art.ram || ''}</div>
              <h4>${t('Memory', 'Память')}</h4>
              <div class="bento-cname">${ov.ram.totalGB} GB RAM</div>
              <div class="esll-meta" id="ov-ram-txt">${ov.ram.usedGB} / ${ov.ram.totalGB} GB (${ov.ram.pct}%)</div>
              <div class="meter"><i id="ov-ram-bar" style="width:${ov.ram.pct}%"></i></div>
            </div>

            <div class="esll-card bento-comp" onclick="WHA.switchModule('storage')">
              <div class="bento-mini-art">${art.storage || ''}</div>
              <h4>${t('Storage', 'Накопители')}</h4>
              <div class="bento-cname">${(tel.physDisks && tel.physDisks[0]) ? esc(tel.physDisks[0].name) : (cDrive ? esc(cDrive.drive) : 'Disk')}</div>
              <div class="esll-meta">${cDrive ? cDrive.freeGB + ' GB ' + t('free of', 'свободно из') + ' ' + cDrive.totalGB + ' GB' : '—'}</div>
              <div class="meter"><i style="width:${cDrive ? cDrive.usedPct : 0}%"></i></div>
            </div>

            <div class="esll-card bento-comp" onclick="WHA.switchModule('tester')">
              <div class="bento-mini-art">${art.displays || ''}</div>
              <h4>${t('Displays', 'Экраны')}</h4>
              <div class="bento-cname">${(ov.displays || []).length} ${t('display(s)', 'экр.')}</div>
              <div class="esll-meta">${esc(dispText)}</div>
            </div>

            <div class="esll-card bento-comp" onclick="WHA.switchModule('monitor')">
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
        { k: t('Drive C:', 'Диск C:'), v: cd ? `${cd.totalGB - cd.freeGB} / ${cd.totalGB} GB` : '—', p: cd ? cd.usedPct : 0 },
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

    drawLiveBars(tel);
    if (liveTimer) clearInterval(liveTimer);
    liveTimer = setInterval(async () => {
      const t2 = await api('system', 'telemetry');
      drawLiveBars(t2);
    }, 2500);
  }

  function leave() {
    if (liveTimer) { clearInterval(liveTimer); liveTimer = null; }
  }

  WHA.registerModule('overview', { render, leave });
})();
