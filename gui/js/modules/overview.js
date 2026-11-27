(function () {
  const { t, esc, api, switchModule } = WHA;

  async function render(root) {
    root.innerHTML = `<div class="mod-head"><div><div class="mod-title">${t('System Overview', 'Обзор системы')}</div><div class="mod-sub">${t('Loading hardware profile...', 'Загрузка профиля оборудования...')}</div></div></div>`;
    const res = await api('system', 'overview');
    if (!res.ok) {
      root.innerHTML = `<div class="esll-card">${esc(res.error || 'Error')}</div>`;
      return;
    }

    const gpuHtml = (res.gpus || []).map(g =>
      `<div class="esll-meta">${esc(g.name)} ${g.vramMB ? '(' + g.vramMB + ' MB)' : ''} ${g.driver ? '· ' + esc(g.driver) : ''}</div>`
    ).join('') || `<div class="esll-meta">—</div>`;

    const dispHtml = (res.displays || []).map(d =>
      `<div class="esll-meta">${esc(d.name)} · ${d.width}×${d.height} ${d.primary ? '<span class="badge">Primary</span>' : ''}</div>`
    ).join('') || `<div class="esll-meta">—</div>`;

    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${esc(res.manufacturer || '')} ${esc(res.model || res.hostname)}</div>
          <div class="mod-sub">${esc(res.os)} · ${t('Uptime', 'Аптайм')}: ${res.uptimeHours} ${t('h', 'ч')} · ${res.isLaptop ? t('Laptop', 'Ноутбук') : t('Desktop', 'ПК')}</div>
        </div>
        <div class="mod-actions">
          <button class="pill" onclick="WHA.switchModule('monitor')">${t('Live Monitor', 'Монитор')}</button>
          <button class="pill" onclick="WHA.switchModule('audit')">${t('Health Audit', 'Аудит')}</button>
        </div>
      </div>

      <div class="grid-4" style="margin-bottom:16px">
        <div class="esll-card">
          <h4>${t('Processor', 'Процессор')}</h4>
          <div class="esll-big">${res.cpu.load}<small>%</small></div>
          <div class="esll-meta">${esc(res.cpu.name)}</div>
          <div class="esll-meta">${res.cpu.cores}C / ${res.cpu.threads}T · ${res.cpu.maxMhz} MHz</div>
          <div class="meter"><i style="width:${res.cpu.load}%"></i></div>
        </div>

        <div class="esll-card">
          <h4>${t('Memory', 'Память')}</h4>
          <div class="esll-big">${res.ram.pct}<small>%</small></div>
          <div class="esll-meta">${res.ram.usedGB} / ${res.ram.totalGB} GB</div>
          <div class="esll-meta">${t('Free', 'Свободно')}: ${res.ram.freeGB} GB</div>
          <div class="meter"><i style="width:${res.ram.pct}%"></i></div>
        </div>

        <div class="esll-card">
          <h4>${t('Graphics', 'Графика')}</h4>
          <div class="esll-big">${(res.gpus || []).length}<small>GPU</small></div>
          ${gpuHtml}
        </div>

        <div class="esll-card">
          <h4>${t('Displays', 'Экраны')}</h4>
          <div class="esll-big">${(res.displays || []).length}<small>${t('mon', 'мон')}</small></div>
          ${dispHtml}
        </div>
      </div>

      <div class="grid-2">
        <div class="esll-card">
          <h4>${t('Motherboard & BIOS', 'Материнская плата и BIOS')}</h4>
          <div class="esll-meta">${t('Board', 'Плата')}: ${esc(res.board)}</div>
          <div class="esll-meta">BIOS: ${esc(res.bios)}</div>
          <div class="esll-meta">${t('User', 'Пользователь')}: ${esc(res.user)} @ ${esc(res.hostname)}</div>
        </div>
        <div class="esll-card">
          <h4>${t('Quick Modules', 'Быстрые модули')}</h4>
          <div class="mod-actions" style="margin-top:6px">
            <button class="pill small" onclick="WHA.switchModule('storage')">${t('Storage', 'Диски')}</button>
            <button class="pill small" onclick="WHA.switchModule('drivers')">${t('Drivers', 'Драйверы')}</button>
            <button class="pill small" onclick="WHA.switchModule('programs')">${t('Programs', 'Программы')}</button>
            <button class="pill small" onclick="WHA.switchModule('optimizer')">${t('Optimizer', 'Оптимизация')}</button>
            <button class="pill small" onclick="WHA.switchModule('privacy')">${t('Privacy', 'Приватность')}</button>
            <button class="pill small" onclick="WHA.switchModule('benchmark')">${t('Benchmark', 'Тест ПК')}</button>
            <button class="pill small" onclick="WHA.switchModule('tester')">${t('Device Tester', 'Тест устройств')}</button>
          </div>
        </div>
      </div>
    `;
  }

  WHA.registerModule('overview', { render });
})();
