(function () {
  const { t, esc, api, runJob } = WHA;

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Drivers & Devices', 'Драйверы и устройства')}</div>
          <div class="mod-sub">${t('Signed driver inventory, Windows Update driver scan, problem devices & backups', 'Инвентаризация драйверов, поиск обновлений Windows Update, проблемные устройства и бэкапы')}</div>
        </div>
        <div class="mod-actions">
          <button class="pill small" id="drv-rp">${t('Create Restore Point', 'Точка восстановления')}</button>
          <button class="pill small" id="drv-export">${t('Export Backup', 'Экспорт бэкапа')}</button>
          <button class="pill solid" id="drv-wuscan">${t('Scan Windows Update', 'Поиск в Windows Update')}</button>
        </div>
      </div>

      <div id="drv-job-log" class="job-log hidden"></div>

      <div class="subtabs">
        <button class="subtab active" data-dtab="inv">${t('Driver Inventory', 'Все драйверы')}</button>
        <button class="subtab" data-dtab="prob">${t('Problem Devices', 'Проблемные')}</button>
        <button class="subtab" data-dtab="wu">${t('Update Results', 'Обновления')}</button>
        <button class="subtab" data-dtab="bak">${t('Backups', 'Резервные копии')}</button>
      </div>

      <div id="drv-body"></div>
    `;

    const body = root.querySelector('#drv-body');
    const logEl = root.querySelector('#drv-job-log');
    let wuUpdates = [];

    async function loadTab(tab) {
      root.querySelectorAll('.subtab').forEach(b => b.classList.toggle('active', b.getAttribute('data-dtab') === tab));
      body.innerHTML = `<div class="esll-meta">${t('Loading...', 'Загрузка...')}</div>`;

      if (tab === 'inv') {
        const res = await api('drivers', 'list');
        const items = res.items || [];
        body.innerHTML = `
          <div class="findbar" style="padding:0 0 12px">
            <input class="find-input" id="drv-q" placeholder="${t('Filter drivers...', 'Фильтр драйверов...')}">
            <div class="find-count" id="drv-cnt">${items.length}</div>
          </div>
          <table class="esll-table">
            <thead><tr><th>${t('Device', 'Устройство')}</th><th>${t('Class', 'Класс')}</th><th>${t('Provider', 'Издатель')}</th><th>${t('Version', 'Версия')}</th><th>${t('Date', 'Дата')}</th><th>${t('Signed', 'Подпись')}</th></tr></thead>
            <tbody id="drv-rows"></tbody>
          </table>`;
        const renderRows = (q) => {
          const fl = items.filter(x => !q || (x.name + ' ' + x.provider + ' ' + x.class).toLowerCase().includes(q));
          body.querySelector('#drv-cnt').textContent = fl.length;
          body.querySelector('#drv-rows').innerHTML = fl.slice(0, 150).map(d => `
            <tr>
              <td>${esc(d.name)}</td>
              <td>${esc(d.class)}</td>
              <td>${esc(d.provider)}</td>
              <td class="mono">${esc(d.version)}</td>
              <td class="mono">${esc(d.date)}</td>
              <td><span class="badge">${d.signed ? 'Microsoft/WHQL' : 'Unsigned'}</span></td>
            </tr>`).join('');
        };
        renderRows('');
        body.querySelector('#drv-q').oninput = (e) => renderRows(e.target.value.toLowerCase());
      } else if (tab === 'prob') {
        const res = await api('drivers', 'problems');
        const items = res.items || [];
        if (!items.length) {
          body.innerHTML = `<div class="esll-card">${t('No problem devices detected.', 'Проблемных устройств не обнаружено.')}</div>`;
          return;
        }
        body.innerHTML = `<table class="esll-table"><thead><tr><th>${t('Device', 'Устройство')}</th><th>${t('Class', 'Класс')}</th><th>${t('Error Code', 'Код ошибки')}</th><th></th></tr></thead><tbody>${
          items.map(d => `<tr><td>${esc(d.name)}</td><td>${esc(d.class)}</td><td class="mono">${d.errorCode}</td><td><button class="hidebtn" data-en="${esc(d.deviceId)}">${t('Enable', 'Включить')}</button></td></tr>`).join('')
        }</tbody></table>`;
        body.querySelectorAll('[data-en]').forEach(b => {
          b.onclick = async () => {
            await api('drivers', 'toggle_device', { deviceId: b.getAttribute('data-en'), enable: true });
            loadTab('prob');
          };
        });
      } else if (tab === 'wu') {
        if (!wuUpdates.length) {
          body.innerHTML = `<div class="esll-card">${t('Click "Scan Windows Update" above to check for Microsoft-signed driver updates.', 'Нажмите «Поиск в Windows Update» выше для проверки обновлений драйверов.')}</div>`;
          return;
        }
        body.innerHTML = `<table class="esll-table"><thead><tr><th>${t('Update', 'Обновление')}</th><th>${t('Manufacturer', 'Производитель')}</th><th>${t('Date', 'Дата')}</th><th>${t('Size', 'Размер')}</th></tr></thead><tbody>${
          wuUpdates.map(u => `<tr><td>${esc(u.title)}</td><td>${esc(u.manufacturer)}</td><td class="mono">${esc(u.driverVerDate)}</td><td class="mono">${u.sizeMB} MB</td></tr>`).join('')
        }</tbody></table>`;
      } else if (tab === 'bak') {
        const res = await api('drivers', 'backups');
        const items = res.items || [];
        body.innerHTML = items.length ? `<table class="esll-table"><thead><tr><th>${t('Backup', 'Копия')}</th><th>${t('Date', 'Дата')}</th><th>INF</th><th>${t('Path', 'Путь')}</th></tr></thead><tbody>${
          items.map(b => `<tr><td>${esc(b.name)}</td><td class="mono">${esc(b.date)}</td><td class="mono">${b.infCount}</td><td class="mono">${esc(b.path)}</td></tr>`).join('')
        }</tbody></table>` : `<div class="esll-card">${t('No driver backups yet. Click Export Backup.', 'Резервных копий пока нет.')}</div>`;
      }
    }

    root.querySelectorAll('.subtab').forEach(b => b.onclick = () => loadTab(b.getAttribute('data-dtab')));
    root.querySelector('#drv-wuscan').onclick = () => {
      runJob('drivers', 'wu_scan', {}, logEl, (res) => {
        if (res && res.items) { wuUpdates = res.items; loadTab('wu'); }
      });
    };
    root.querySelector('#drv-export').onclick = () => {
      runJob('drivers', 'export', {}, logEl, () => loadTab('bak'));
    };
    root.querySelector('#drv-rp').onclick = async () => {
      const r = await api('drivers', 'restore_point', {});
      logEl.classList.remove('hidden');
      logEl.textContent = r.ok ? t('Restore point created.', 'Точка восстановления создана.') : (r.error || 'Admin required');
    };

    loadTab('inv');
  }

  WHA.registerModule('drivers', { render });
})();
