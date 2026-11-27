(function () {
  const { t, esc, api } = WHA;

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Privacy & Security', 'Приватность и безопасность')}</div>
          <div class="mod-sub">${t('Protection audit, open ports, app permissions, tracking toggles & persistence', 'Аудит защиты, открытые порты, права приложений, телеметрия и автозапуск')}</div>
        </div>
      </div>

      <div class="subtabs">
        <button class="subtab active" data-prtab="ov">${t('Security Score', 'Оценка защиты')}</button>
        <button class="subtab" data-prtab="trk">${t('Tracking Controls', 'Телеметрия')}</button>
        <button class="subtab" data-prtab="ports">${t('Open Ports', 'Порты')}</button>
        <button class="subtab" data-prtab="perm">${t('Permissions', 'Доступы')}</button>
        <button class="subtab" data-prtab="pers">${t('Persistence', 'Задачи')}</button>
      </div>

      <div id="pr-body"></div>
    `;

    const body = root.querySelector('#pr-body');

    async function loadTab(tab) {
      root.querySelectorAll('.subtab').forEach(b => b.classList.toggle('active', b.getAttribute('data-prtab') === tab));
      body.innerHTML = `<div class="esll-meta">${t('Loading...', 'Загрузка...')}</div>`;

      if (tab === 'ov') {
        const res = await api('privacy', 'overview');
        body.innerHTML = `
          <div class="esll-card" style="margin-bottom:16px">
            <h4>${t('Security Score', 'Оценка безопасности')}</h4>
            <div class="esll-big">${res.score}<small>/ 100</small></div>
            <div class="meter"><i style="width:${res.score}%"></i></div>
          </div>
          <div class="esll-card">
            ${(res.findings || []).map(f => `
              <div class="row" style="cursor:default">
                <div class="sev">${esc(f.sev)}</div>
                <div><div class="msg">${esc(f.title)}</div><div class="note">${esc(f.detail)}</div></div>
                <div></div>
              </div>`).join('')}
          </div>`;
      } else if (tab === 'trk') {
        const res = await api('privacy', 'tracking_list');
        body.innerHTML = `<div class="esll-card"><h4>${t('Windows Tracking & Telemetry', 'Отслеживание и телеметрия Windows')}</h4>` +
          (res.items || []).map(it => `
            <label class="check">
              <input type="checkbox" data-trk="${esc(it.id)}" ${it.protected ? 'checked' : ''}>
              <span><b>${esc(it.name)}</b><span class="hintline">${esc(it.desc)}</span></span>
            </label>`).join('') + `</div>`;
        body.querySelectorAll('[data-trk]').forEach(c => {
          c.onchange = () => api('privacy', 'tracking_set', { id: c.getAttribute('data-trk'), protected: c.checked });
        });
      } else if (tab === 'ports') {
        const res = await api('privacy', 'ports');
        body.innerHTML = `<table class="esll-table"><thead><tr><th>${t('Port', 'Порт')}</th><th>${t('Address', 'Адрес')}</th><th>${t('Process', 'Процесс')}</th><th>PID</th><th>${t('Scope', 'Зона')}</th></tr></thead><tbody>${
          (res.items || []).map(p => `<tr><td class="mono">${p.port}</td><td class="mono">${esc(p.address)}</td><td>${esc(p.process)}</td><td class="mono">${p.pid}</td><td><span class="badge">${esc(p.scope)}</span></td></tr>`).join('')
        }</tbody></table>`;
      } else if (tab === 'perm') {
        const res = await api('privacy', 'permissions');
        body.innerHTML = `<table class="esll-table"><thead><tr><th>${t('Capability', 'Доступ')}</th><th>${t('App', 'Приложение')}</th><th>${t('Status', 'Статус')}</th><th>${t('Last Used', 'Последнее использование')}</th></tr></thead><tbody>${
          (res.items || []).map(p => `<tr><td><span class="badge">${esc(p.capability)}</span></td><td>${esc(p.app)}</td><td>${esc(p.value)}</td><td class="mono">${esc(p.lastUsed || '—')}</td></tr>`).join('')
        }</tbody></table>`;
      } else if (tab === 'pers') {
        const res = await api('privacy', 'persistence');
        body.innerHTML = `<table class="esll-table"><thead><tr><th>${t('Kind', 'Тип')}</th><th>${t('Name', 'Имя')}</th><th>${t('Path', 'Путь')}</th><th>${t('State', 'Состояние')}</th></tr></thead><tbody>${
          (res.items || []).map(p => `<tr><td>${esc(p.kind)}</td><td>${esc(p.name)}</td><td class="mono">${esc(p.path)}</td><td><span class="badge">${esc(p.state)}</span></td></tr>`).join('')
        }</tbody></table>`;
      }
    }

    root.querySelectorAll('.subtab').forEach(b => b.onclick = () => loadTab(b.getAttribute('data-prtab')));
    loadTab('ov');
  }

  WHA.registerModule('privacy', { render });
})();
