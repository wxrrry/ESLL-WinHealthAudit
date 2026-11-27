(function () {
  const { t, esc, fmtBytes, api, runJob } = WHA;

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Super Optimizer', 'Оптимизация системы')}</div>
          <div class="mod-sub">${t('Cleanup, startup apps, services, performance, debloat tweaks & Windows repairs', 'Очистка, автозагрузка, службы, производительность, деблоат и восстановление Windows')}</div>
        </div>
      </div>

      <div id="opt-log" class="job-log hidden"></div>

      <div class="subtabs">
        <button class="subtab active" data-otab="clean">${t('Cleanup', 'Очистка')}</button>
        <button class="subtab" data-otab="start">${t('Startup', 'Автозагрузка')}</button>
        <button class="subtab" data-otab="svc">${t('Services', 'Службы')}</button>
        <button class="subtab" data-otab="perf">${t('Performance & Debloat', 'Производительность')}</button>
        <button class="subtab" data-otab="rep">${t('Repairs', 'Восстановление')}</button>
      </div>

      <div id="opt-body"></div>
    `;

    const body = root.querySelector('#opt-body');
    const logEl = root.querySelector('#opt-log');

    async function loadTab(tab) {
      root.querySelectorAll('.subtab').forEach(b => b.classList.toggle('active', b.getAttribute('data-otab') === tab));
      body.innerHTML = `<div class="esll-meta">${t('Loading...', 'Загрузка...')}</div>`;

      if (tab === 'clean') {
        const res = await api('optimizer', 'clean_scan');
        const items = res.items || [];
        const total = items.reduce((s, x) => s + Number(x.bytes || 0), 0);
        body.innerHTML = `
          <div class="mod-actions" style="margin-bottom:14px">
            <span class="badge">${t('Reclaimable', 'Можно очистить')}: ${fmtBytes(total)}</span>
            <button class="pill solid small" id="opt-clean-go">${t('Clean Selected', 'Очистить выбранное')}</button>
          </div>
          <div class="esll-card">
            ${items.map(x => `
              <label class="check">
                <input type="checkbox" class="opt-chk" value="${esc(x.id)}" checked>
                <span><b>${esc(x.name)}</b> (${x.mb} MB)<span class="hintline">${esc(x.desc)} · ${esc(x.path)}</span></span>
              </label>`).join('')}
          </div>`;
        body.querySelector('#opt-clean-go').onclick = async () => {
          const ids = Array.from(body.querySelectorAll('.opt-chk:checked')).map(c => c.value);
          const r = await api('optimizer', 'clean_run', { ids });
          logEl.classList.remove('hidden');
          logEl.textContent = t('Cleaned items: ', 'Удалено объектов: ') + (r.removed || 0);
          loadTab('clean');
        };
      } else if (tab === 'start') {
        const res = await api('optimizer', 'startup_list');
        const items = res.items || [];
        body.innerHTML = `<table class="esll-table"><thead><tr><th>${t('App', 'Приложение')}</th><th>${t('Scope', 'Область')}</th><th>${t('Command', 'Команда')}</th><th>${t('Status', 'Статус')}</th><th></th></tr></thead><tbody>${
          items.map((s, i) => `<tr>
            <td>${esc(s.name)}</td>
            <td class="mono">${esc(s.scope)}</td>
            <td class="mono" style="max-width:360px;overflow:hidden;text-overflow:ellipsis">${esc(s.command)}</td>
            <td><span class="badge ${s.enabled ? 'solid' : ''}">${s.enabled ? t('Enabled', 'Вкл') : t('Disabled', 'Выкл')}</span></td>
            <td style="text-align:right"><button class="hidebtn" data-st="${i}">${s.enabled ? t('Disable', 'Откл') : t('Enable', 'Вкл')}</button></td>
          </tr>`).join('')
        }</tbody></table>`;
        body.querySelectorAll('[data-st]').forEach(b => {
          b.onclick = async () => {
            const s = items[Number(b.getAttribute('data-st'))];
            await api('optimizer', 'startup_toggle', { name: s.name, scope: s.scope, enabled: !s.enabled });
            loadTab('start');
          };
        });
      } else if (tab === 'svc') {
        const res = await api('optimizer', 'services_list');
        const items = res.items || [];
        body.innerHTML = `
          <div class="findbar" style="padding:0 0 12px">
            <input class="find-input" id="svc-q" placeholder="${t('Filter services...', 'Фильтр служб...')}">
          </div>
          <table class="esll-table"><thead><tr><th>${t('Service', 'Служба')}</th><th>${t('Status', 'Статус')}</th><th>${t('Startup', 'Тип запуска')}</th><th></th></tr></thead><tbody id="svc-rows"></tbody></table>`;
        const draw = (q) => {
          const fl = items.filter(x => !q || (x.display + ' ' + x.name).toLowerCase().includes(q));
          body.querySelector('#svc-rows').innerHTML = fl.slice(0, 100).map(s => `
            <tr>
              <td><b>${esc(s.display)}</b> <span class="mono" style="opacity:.6">(${esc(s.name)})</span></td>
              <td><span class="badge ${s.status === 'Running' ? 'solid' : ''}">${esc(s.status)}</span></td>
              <td class="mono">${esc(s.startType)}</td>
              <td style="text-align:right;white-space:nowrap">
                <button class="hidebtn" data-sop="start" data-sname="${esc(s.name)}">${t('Start', 'Пуск')}</button>
                <button class="hidebtn" data-sop="stop" data-sname="${esc(s.name)}">${t('Stop', 'Стоп')}</button>
              </td>
            </tr>`).join('');
          body.querySelectorAll('[data-sop]').forEach(b => {
            b.onclick = async () => {
              await api('optimizer', 'services_set', { name: b.getAttribute('data-sname'), op: b.getAttribute('data-sop') });
              loadTab('svc');
            };
          });
        };
        draw('');
        body.querySelector('#svc-q').oninput = (e) => draw(e.target.value.toLowerCase());
      } else if (tab === 'perf') {
        const [perf, deb] = await Promise.all([api('optimizer', 'perf_read'), api('optimizer', 'debloat_list')]);
        body.innerHTML = `
          <div class="grid-2">
            <div class="esll-card">
              <h4>${t('Performance & Power', 'Производительность и питание')}</h4>
              <div class="esll-meta">${t('Active Power Plan', 'Схема питания')}: <b>${esc(perf.powerPlan || '—')}</b></div>
              <div class="mod-actions" style="margin:12px 0">
                <button class="pill small" data-pp="balanced">${t('Balanced', 'Сбалансированная')}</button>
                <button class="pill small" data-pp="high">${t('High Performance', 'Высокая')}</button>
                <button class="pill small" data-pp="power">${t('Power Saver', 'Экономия')}</button>
              </div>
              <label class="check"><input type="checkbox" id="perf-gm" ${perf.gameMode ? 'checked' : ''}><span>Windows Game Mode</span></label>
              <label class="check"><input type="checkbox" id="perf-hags" ${perf.hags ? 'checked' : ''}><span>Hardware-Accelerated GPU Scheduling (HAGS)</span></label>
            </div>

            <div class="esll-card">
              <h4>${t('Windows Debloat & UI Tweaks', 'Деблоат и настройки интерфейса')}</h4>
              ${(deb.items || []).map(tw => `
                <label class="check">
                  <input type="checkbox" data-tw="${esc(tw.id)}" ${tw.active ? 'checked' : ''}>
                  <span><b>${esc(tw.name)}</b><span class="hintline">${esc(tw.desc)}</span></span>
                </label>`).join('')}
            </div>
          </div>`;

        body.querySelectorAll('[data-pp]').forEach(b => b.onclick = async () => {
          await api('optimizer', 'perf_set', { key: 'powerPlan', value: b.getAttribute('data-pp') });
          loadTab('perf');
        });
        body.querySelector('#perf-gm').onchange = (e) => api('optimizer', 'perf_set', { key: 'gameMode', value: e.target.checked });
        body.querySelector('#perf-hags').onchange = (e) => api('optimizer', 'perf_set', { key: 'hags', value: e.target.checked });
        body.querySelectorAll('[data-tw]').forEach(c => {
          c.onchange = () => api('optimizer', 'debloat_apply', { id: c.getAttribute('data-tw'), enable: c.checked });
        });
      } else if (tab === 'rep') {
        body.innerHTML = `
          <div class="grid-2">
            <div class="esll-card">
              <h4>SFC /scannow</h4>
              <div class="esll-meta">${t('Scans and repairs corrupted Windows system files.', 'Проверка и восстановление системных файлов Windows.')}</div>
              <div style="margin-top:12px"><button class="pill small" data-rep="sfc">${t('Run SFC', 'Запустить SFC')}</button></div>
            </div>
            <div class="esll-card">
              <h4>DISM RestoreHealth</h4>
              <div class="esll-meta">${t('Repairs the Windows component store image.', 'Восстановление образа компонентов Windows.')}</div>
              <div style="margin-top:12px"><button class="pill small" data-rep="dism">${t('Run DISM', 'Запустить DISM')}</button></div>
            </div>
            <div class="esll-card">
              <h4>${t('Network Reset', 'Сброс сети')}</h4>
              <div class="esll-meta">${t('Flushes DNS cache and resets Winsock / IP stack.', 'Очистка DNS-кэша и сброс стека Winsock / IP.')}</div>
              <div style="margin-top:12px"><button class="pill small" data-rep="net">${t('Reset Network', 'Сбросить сеть')}</button></div>
            </div>
            <div class="esll-card">
              <h4>${t('Windows Update Reset', 'Перезапуск Windows Update')}</h4>
              <div class="esll-meta">${t('Restarts Windows Update and BITS services.', 'Перезапуск служб Центра обновления Windows и BITS.')}</div>
              <div style="margin-top:12px"><button class="pill small" data-rep="wu">${t('Restart WU', 'Перезапустить WU')}</button></div>
            </div>
          </div>`;
        body.querySelectorAll('[data-rep]').forEach(b => {
          b.onclick = () => runJob('optimizer', 'repair', { kind: b.getAttribute('data-rep') }, logEl, () => {});
        });
      }
    }

    root.querySelectorAll('.subtab').forEach(b => b.onclick = () => loadTab(b.getAttribute('data-otab')));
    loadTab('clean');
  }

  WHA.registerModule('optimizer', { render });
})();
