(function () {
  const { t, esc, api } = WHA;
  let timer = null;
  let procSort = 'ram';

  async function refresh(root) {
    const [tel, procs] = await Promise.all([
      api('system', 'telemetry'),
      api('system', 'processes', { sort: procSort })
    ]);
    if (!tel.ok) return;

    const topEl = root.querySelector('#mon-top');
    if (topEl) {
      const gpuLoad = tel.gpu && tel.gpu.load != null ? tel.gpu.load + '%' : '—';
      const gpuSub = tel.gpu ? `${esc(tel.gpu.name)}${tel.gpu.tempC != null ? ' · ' + tel.gpu.tempC + '°C' : ''}${tel.gpu.vramTotalMB ? ' · ' + (tel.gpu.vramUsedMB != null ? tel.gpu.vramUsedMB + '/' : '') + tel.gpu.vramTotalMB + ' MB' : ''}` : '—';
      const batHtml = tel.battery ? `<div class="esll-card"><h4>${t('Battery', 'Батарея')}</h4><div class="esll-big">${tel.battery.pct}<small>%</small></div><div class="esll-meta">${esc(tel.battery.status)}${tel.battery.runtimeMin ? ' · ' + tel.battery.runtimeMin + ' min' : ''}</div><div class="meter"><i style="width:${tel.battery.pct}%"></i></div></div>` : '';

      topEl.innerHTML = `
        <div class="esll-card">
          <h4>CPU</h4>
          <div class="esll-big">${tel.cpuLoad}<small>%</small></div>
          <div class="esll-meta">${tel.cpuClock} MHz${tel.cpuTempC != null ? ' · ' + tel.cpuTempC + '°C' : ''}</div>
          <div class="meter"><i style="width:${tel.cpuLoad}%"></i></div>
        </div>
        <div class="esll-card">
          <h4>RAM</h4>
          <div class="esll-big">${tel.ram.pct}<small>%</small></div>
          <div class="esll-meta">${tel.ram.usedMB} / ${tel.ram.totalMB} MB</div>
          <div class="meter"><i style="width:${tel.ram.pct}%"></i></div>
        </div>
        <div class="esll-card">
          <h4>GPU</h4>
          <div class="esll-big">${gpuLoad}</div>
          <div class="esll-meta">${gpuSub}</div>
          <div class="meter"><i style="width:${tel.gpu && tel.gpu.load != null ? tel.gpu.load : 0}%"></i></div>
        </div>
        ${batHtml || `<div class="esll-card"><h4>${t('Network', 'Сеть')}</h4><div class="esll-big">${(tel.net || []).length}<small>UP</small></div><div class="esll-meta">${tel.wifi ? 'Wi-Fi: ' + esc(tel.wifi.ssid) + ' (' + esc(tel.wifi.signal) + ')' : ((tel.net && tel.net[0]) ? esc(tel.net[0].name) + ' · ' + esc(tel.net[0].ip || '') : '—')}</div></div>`}
      `;
    }

    const disksEl = root.querySelector('#mon-disks');
    if (disksEl) {
      const vols = (tel.disks || []).map(d =>
        `<div class="esll-meta"><b>${esc(d.drive)}</b> ${esc(d.label || '')} (${esc(d.fs)}) — ${d.freeGB} GB ${t('free of', 'свободно из')} ${d.totalGB} GB (${d.usedPct}%)</div>`
      ).join('');
      const phys = (tel.physDisks || []).map(p =>
        `<div class="esll-meta">${esc(p.name)} [${esc(p.media)}] · ${p.sizeGB} GB · <span class="badge">${esc(p.health)}</span>${p.tempC ? ' · ' + p.tempC + '°C' : ''}${p.wear != null ? ' · Wear ' + p.wear + '%' : ''}</div>`
      ).join('');
      disksEl.innerHTML = `<h4>${t('Disks & SMART Health', 'Диски и SMART')}</h4>${vols}${phys ? '<div style="margin-top:8px;border-top:1px solid var(--hair);padding-top:8px">' + phys + '</div>' : ''}`;
    }

    const procEl = root.querySelector('#mon-procs');
    if (procEl && procs.ok) {
      procEl.innerHTML = (procs.items || []).slice(0, 35).map(p => `
        <tr>
          <td>${esc(p.name)}</td>
          <td class="mono">${p.pid}</td>
          <td class="mono">${p.ramMB} MB</td>
          <td class="mono">${p.cpu}s</td>
          <td class="mono">${p.threads}</td>
          <td style="text-align:right">
            ${p.path ? `<button class="hidebtn" data-open="${esc(p.path)}">${t('Folder', 'Папка')}</button>` : ''}
            <button class="hidebtn" data-kill="${p.pid}">${t('End', 'Снять')}</button>
          </td>
        </tr>
      `).join('');

      procEl.querySelectorAll('[data-kill]').forEach(btn => {
        btn.onclick = async () => {
          const pid = Number(btn.getAttribute('data-kill'));
          await api('system', 'end_process', { pid });
          refresh(root);
        };
      });
      procEl.querySelectorAll('[data-open]').forEach(btn => {
        btn.onclick = () => api('system', 'open_location', { path: btn.getAttribute('data-open') });
      });
    }
  }

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Live Monitor', 'Монитор ресурсов')}</div>
          <div class="mod-sub">${t('Real-time telemetry, SMART disks, network & processes', 'Телеметрия в реальном времени, SMART, сеть и процессы')}</div>
        </div>
        <div class="mod-actions">
          <button class="pill small" id="mon-dns-test">${t('DNS Ping', 'Тест DNS')}</button>
          <button class="pill small" id="mon-sort-ram">${t('Sort: RAM', 'Сорт: ОЗУ')}</button>
          <button class="pill small" id="mon-sort-cpu">${t('Sort: CPU', 'Сорт: ЦП')}</button>
        </div>
      </div>

      <div class="grid-4" id="mon-top" style="margin-bottom:16px"></div>

      <div class="grid-2" style="margin-bottom:16px">
        <div class="esll-card" id="mon-disks"></div>
        <div class="esll-card" id="mon-dns-box">
          <h4>${t('DNS & Network Presets', 'DNS и сеть')}</h4>
          <div class="esll-meta" id="mon-dns-res">${t('Click DNS Ping to measure latency or switch DNS.', 'Нажмите Тест DNS для проверки задержки или смены DNS.')}</div>
          <div class="mod-actions" style="margin-top:10px">
            <button class="pill small" data-dns="cloudflare">Cloudflare 1.1.1.1</button>
            <button class="pill small" data-dns="google">Google 8.8.8.8</button>
            <button class="pill small" data-dns="quad9">Quad9 9.9.9.9</button>
            <button class="pill small" data-dns="auto">${t('Auto (DHCP)', 'Авто (DHCP)')}</button>
          </div>
        </div>
      </div>

      <div class="esll-card">
        <h4>${t('Top Processes', 'Активные процессы')}</h4>
        <table class="esll-table">
          <thead>
            <tr>
              <th>${t('Process', 'Процесс')}</th>
              <th>PID</th>
              <th>RAM</th>
              <th>CPU</th>
              <th>${t('Threads', 'Потоки')}</th>
              <th></th>
            </tr>
          </thead>
          <tbody id="mon-procs"></tbody>
        </table>
      </div>
    `;

    root.querySelector('#mon-sort-ram').onclick = () => { procSort = 'ram'; refresh(root); };
    root.querySelector('#mon-sort-cpu').onclick = () => { procSort = 'cpu'; refresh(root); };
    root.querySelector('#mon-dns-test').onclick = async () => {
      const el = root.querySelector('#mon-dns-res');
      el.textContent = t('Pinging DNS servers...', 'Проверка DNS...');
      const r = await api('system', 'dns_test');
      if (r.ok) {
        el.innerHTML = (r.results || []).map(x => `<span class="badge" style="margin-right:6px">${esc(x.name)}: ${x.ms != null ? x.ms + ' ms' : 'timeout'}</span>`).join('');
      }
    };
    root.querySelectorAll('[data-dns]').forEach(b => {
      b.onclick = async () => {
        const preset = b.getAttribute('data-dns');
        const r = await api('system', 'dns_set', { preset });
        root.querySelector('#mon-dns-res').textContent = r.ok ? t('DNS updated: ', 'DNS обновлён: ') + preset : (r.error || 'Error (try Admin)');
      };
    });

    await refresh(root);
    if (timer) clearInterval(timer);
    timer = setInterval(() => refresh(root), 2500);
  }

  function leave() {
    if (timer) { clearInterval(timer); timer = null; }
  }

  WHA.registerModule('monitor', { render, leave });
})();
