(function () {
  const { t, esc, api, runJob } = WHA;

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Programs Hub', 'Программы и пакеты')}</div>
          <div class="mod-sub">${t('Uninstaller with leftover quarantine, winget updater & App Hangar catalog', 'Деинсталлятор с карантином остатков, обновления winget и каталог App Hangar')}</div>
        </div>
      </div>

      <div id="prg-log" class="job-log hidden"></div>

      <div class="subtabs">
        <button class="subtab active" data-ptab="rem">${t('Remover', 'Удаление')}</button>
        <button class="subtab" data-ptab="quar">${t('Quarantine', 'Карантин')}</button>
        <button class="subtab" data-ptab="upd">${t('Updater', 'Обновления')}</button>
        <button class="subtab" data-ptab="hangar">${t('App Hangar', 'Каталог приложений')}</button>
      </div>

      <div id="prg-body"></div>
    `;

    const body = root.querySelector('#prg-body');
    const logEl = root.querySelector('#prg-log');

    async function loadTab(tab) {
      root.querySelectorAll('.subtab').forEach(b => b.classList.toggle('active', b.getAttribute('data-ptab') === tab));
      body.innerHTML = `<div class="esll-meta">${t('Loading...', 'Загрузка...')}</div>`;

      if (tab === 'rem') {
        const res = WHA.cache.programs || await api('programs', 'installed');
        WHA.cache.programs = res;
        const items = res.items || [];
        body.innerHTML = `
          <div class="findbar" style="padding:0 0 12px">
            <input class="find-input" id="prg-q" placeholder="${t('Search installed programs...', 'Поиск программ...')}">
            <button class="pill small" id="prg-refresh">${t('Refresh', 'Обновить')}</button>
            <div class="find-count" id="prg-cnt">${items.length}</div>
          </div>
          <div id="prg-leftovers" class="esll-card hidden" style="margin-bottom:14px"></div>
          <table class="esll-table">
            <thead><tr><th>${t('Name', 'Название')}</th><th>${t('Publisher', 'Издатель')}</th><th>${t('Version', 'Версия')}</th><th>${t('Size', 'Размер')}</th><th></th></tr></thead>
            <tbody id="prg-rows"></tbody>
          </table>`;

        const renderRows = (q) => {
          const fl = items.filter(x => !q || (x.name + ' ' + x.publisher).toLowerCase().includes(q));
          body.querySelector('#prg-cnt').textContent = fl.length;
          body.querySelector('#prg-rows').innerHTML = fl.slice(0, 150).map((p, idx) => {
            const letter = esc((p.name || '?').trim().charAt(0).toUpperCase());
            const imgTag = p.icon
              ? `<img src="/api/app-icon?p=${encodeURIComponent(p.icon)}" alt="" onload="this.style.opacity=1" onerror="this.remove()" style="position:absolute;inset:0;width:100%;height:100%;object-fit:contain;opacity:0;transition:opacity .15s;background:#0a0a0a;border-radius:5px">`
              : '';
            const iconHtml = `<span style="position:relative;display:inline-flex;width:24px;height:24px;align-items:center;justify-content:center;border:1px solid var(--hair);border-radius:5px;font-size:10px;margin-right:10px;color:var(--dim);vertical-align:middle;flex-shrink:0;overflow:hidden">${letter}${imgTag}</span>`;
            return `
            <tr data-pidx="${idx}" style="cursor:pointer">
              <td><div style="display:flex;align-items:center">${iconHtml}<span>${esc(p.name)}</span></div></td>
              <td>${esc(p.publisher)}</td>
              <td class="mono">${esc(p.version)}</td>
              <td class="mono">${p.sizeMB ? p.sizeMB + ' MB' : '—'}</td>
              <td style="text-align:right;white-space:nowrap" onclick="event.stopPropagation()">
                <button class="hidebtn" data-left="${idx}">${t('Leftovers', 'Остатки')}</button>
                <button class="hidebtn" data-uninst="${idx}">${t('Uninstall', 'Удалить')}</button>
              </td>
            </tr>`;
          }).join('');

          body.querySelectorAll('[data-pidx]').forEach(tr => {
            tr.onclick = () => openProgramModal(fl[Number(tr.getAttribute('data-pidx'))]);
          });
          body.querySelectorAll('[data-uninst]').forEach(b => {
            b.onclick = async () => {
              const p = fl[Number(b.getAttribute('data-uninst'))];
              await api('programs', 'uninstall', { uninstall: p.uninstall, quietUninstall: p.quietUninstall, silent: false });
              const left = await api('programs', 'leftovers', { name: p.name, publisher: p.publisher });
              showLeftovers(p.name, left.items || []);
            };
          });
          body.querySelectorAll('[data-left]').forEach(b => {
            b.onclick = async () => {
              const p = fl[Number(b.getAttribute('data-left'))];
              const left = await api('programs', 'leftovers', { name: p.name, publisher: p.publisher });
              showLeftovers(p.name, left.items || []);
            };
          });
        };

        function openProgramModal(p) {
          if (!p) return;
          const old = document.getElementById('prg-detail-modal');
          if (old) old.remove();
          const modal = document.createElement('div');
          modal.id = 'prg-detail-modal';
          modal.className = 'wha-modal-back';
          const letter = esc((p.name || '?').trim().charAt(0).toUpperCase());
          const imgTag = p.icon
            ? `<img src="/api/app-icon?p=${encodeURIComponent(p.icon)}" alt="" onerror="this.remove()" style="position:absolute;inset:0;width:100%;height:100%;object-fit:contain;background:#0a0a0a;border-radius:10px">`
            : '';
          modal.innerHTML = `
            <div class="wha-modal">
              <div style="display:flex;align-items:center;gap:14px;margin-bottom:18px">
                <div style="position:relative;width:44px;height:44px;border-radius:10px;border:1px solid var(--hair);display:flex;align-items:center;justify-content:center;font-size:18px;flex-shrink:0">${letter}${imgTag}</div>
                <div style="min-width:0;flex:1">
                  <div style="font-size:18px;font-weight:300">${esc(p.name)}</div>
                  <div class="esll-meta">${esc(p.publisher || '—')} · v${esc(p.version || '—')}</div>
                </div>
                <button class="pill small" id="pm-close">✕</button>
              </div>
              <div class="dline"><div class="dk">${t('Install path', 'Путь установки')}</div><div class="dv mono">${esc(p.installLocation || '—')}</div></div>
              <div class="dline"><div class="dk">${t('Size / Date', 'Размер / Дата')}</div><div class="dv mono">${p.sizeMB ? p.sizeMB + ' MB' : '—'} · ${esc(p.installDate || '—')}</div></div>
              <div class="dline"><div class="dk">${t('Icon source', 'Источник иконки')}</div><div class="dv mono">${esc(p.icon || '—')}</div></div>
              <div class="dline"><div class="dk">${t('Uninstall cmd', 'Команда удаления')}</div><div class="dv mono" style="word-break:break-all">${esc(p.uninstall || '—')}</div></div>
              <div class="mod-actions" style="margin-top:18px">
                ${p.installLocation ? `<button class="pill small" id="pm-open">${t('Open Folder', 'Открыть папку')}</button>` : ''}
                <button class="pill small" id="pm-left">${t('Scan Leftovers', 'Найти остатки')}</button>
                <button class="pill small solid" id="pm-uninst">${t('Uninstall', 'Удалить')}</button>
                ${p.quietUninstall ? `<button class="pill small" id="pm-quiet">${t('Silent Uninstall', 'Тихое удаление')}</button>` : ''}
              </div>
            </div>`;
          document.body.appendChild(modal);
          modal.onclick = (e) => { if (e.target === modal) modal.remove(); };
          modal.querySelector('#pm-close').onclick = () => modal.remove();
          const openBtn = modal.querySelector('#pm-open');
          if (openBtn) openBtn.onclick = () => api('system', 'open_location', { path: p.installLocation });
          modal.querySelector('#pm-left').onclick = async () => {
            modal.remove();
            const left = await api('programs', 'leftovers', { name: p.name, publisher: p.publisher });
            showLeftovers(p.name, left.items || []);
          };
          modal.querySelector('#pm-uninst').onclick = async () => {
            modal.remove();
            await api('programs', 'uninstall', { uninstall: p.uninstall, quietUninstall: p.quietUninstall, silent: false });
          };
          const qBtn = modal.querySelector('#pm-quiet');
          if (qBtn) qBtn.onclick = async () => {
            modal.remove();
            await api('programs', 'uninstall', { uninstall: p.uninstall, quietUninstall: p.quietUninstall, silent: true });
          };
        }

        body.querySelector('#prg-refresh').onclick = async () => {
          delete WHA.cache.programs;
          loadTab('rem');
        };

        function showLeftovers(appName, litems) {
          const box = body.querySelector('#prg-leftovers');
          box.classList.remove('hidden');
          if (!litems.length) {
            box.innerHTML = `<h4>${t('Leftovers for', 'Остатки для')}: ${esc(appName)}</h4><div class="esll-meta">${t('No leftover folders found.', 'Остаточных папок не найдено.')}</div>`;
            return;
          }
          box.innerHTML = `
            <div style="display:flex;justify-content:space-between;align-items:center;margin-bottom:8px">
              <h4 style="margin:0">${t('Leftovers for', 'Остатки для')}: ${esc(appName)} (${litems.length})</h4>
              <button class="pill small solid" id="prg-qmove">${t('Move All to Quarantine', 'Переместить в карантин')}</button>
            </div>
            ${litems.map(x => `<div class="esll-meta mono">${esc(x.path)} (${x.sizeMB} MB · ${esc(x.reason)})</div>`).join('')}
          `;
          box.querySelector('#prg-qmove').onclick = async () => {
            await api('programs', 'quarantine_move', { appName, paths: litems.map(x => x.path) });
            box.innerHTML = `<div class="esll-meta">${t('Moved to Quarantine. You can restore anytime in Quarantine tab.', 'Перемещено в карантин. Можно восстановить во вкладке Карантин.')}</div>`;
          };
        }

        renderRows('');
        body.querySelector('#prg-q').oninput = (e) => renderRows(e.target.value.toLowerCase());
      } else if (tab === 'quar') {
        const res = await api('programs', 'quarantine_list');
        const items = res.items || [];
        if (!items.length) {
          body.innerHTML = `<div class="esll-card">${t('Quarantine is empty.', 'Карантин пуст.')}</div>`;
          return;
        }
        body.innerHTML = `<table class="esll-table"><thead><tr><th>${t('Program', 'Программа')}</th><th>${t('Date', 'Дата')}</th><th>${t('Items', 'Объектов')}</th><th></th></tr></thead><tbody>${
          items.map(q => `<tr><td>${esc(q.app)}</td><td class="mono">${esc(q.date)}</td><td class="mono">${(q.items || []).length}</td><td style="text-align:right">
            <button class="hidebtn" data-qres="${esc(q.id)}">${t('Restore', 'Восстановить')}</button>
            <button class="hidebtn" data-qdel="${esc(q.id)}">${t('Delete', 'Удалить')}</button>
          </td></tr>`).join('')
        }</tbody></table>`;
        body.querySelectorAll('[data-qres]').forEach(b => b.onclick = async () => { await api('programs', 'quarantine_restore', { id: b.getAttribute('data-qres') }); loadTab('quar'); });
        body.querySelectorAll('[data-qdel]').forEach(b => b.onclick = async () => { await api('programs', 'quarantine_delete', { id: b.getAttribute('data-qdel') }); loadTab('quar'); });
      } else if (tab === 'upd') {
        body.innerHTML = `
          <div class="mod-actions" style="margin-bottom:14px">
            <button class="pill solid" id="upd-scan">${t('Check winget updates', 'Проверить обновления winget')}</button>
            <button class="pill" id="upd-all">${t('Update all', 'Обновить все')}</button>
          </div>
          <div id="upd-list" class="esll-card">${t('Click "Check winget updates" to scan installed packages.', 'Нажмите «Проверить обновления winget» для поиска обновлений.')}</div>
        `;
        body.querySelector('#upd-scan').onclick = () => {
          runJob('programs', 'winget_updates', {}, logEl, (res) => {
            const box = body.querySelector('#upd-list');
            const items = (res && res.items) ? res.items : [];
            if (!items.length) {
              box.innerHTML = t('All winget packages are up to date (or winget unavailable).', 'Все пакеты актуальны (или winget недоступен).');
              return;
            }
            box.innerHTML = `<table class="esll-table"><thead><tr><th>${t('App', 'Приложение')}</th><th>ID</th><th>${t('Current', 'Текущая')}</th><th>${t('Available', 'Доступна')}</th><th></th></tr></thead><tbody>${
              items.map(u => `<tr><td>${esc(u.name)}</td><td class="mono">${esc(u.id)}</td><td class="mono">${esc(u.version)}</td><td class="mono">${esc(u.available)}</td><td style="text-align:right"><button class="hidebtn" data-uid="${esc(u.id)}">${t('Update', 'Обновить')}</button></td></tr>`).join('')
            }</tbody></table>`;
            box.querySelectorAll('[data-uid]').forEach(btn => {
              btn.onclick = () => runJob('programs', 'winget_upgrade', { id: btn.getAttribute('data-uid') }, logEl, () => {});
            });
          });
        };
        body.querySelector('#upd-all').onclick = () => runJob('programs', 'winget_upgrade', { id: '__ALL__' }, logEl, () => {});
      } else if (tab === 'hangar') {
        const res = await api('programs', 'hangar_catalog');
        const catalog = res.items || [];
        const selected = new Set();

        body.innerHTML = `
          <div class="findbar" style="padding:0 0 12px">
            <input class="find-input" id="hng-q" placeholder="${t('Filter catalog or search winget...', 'Фильтр каталога или поиск winget...')}">
            <button class="pill small" id="hng-wsearch">${t('Search winget', 'Поиск в winget')}</button>
            <button class="pill small" data-kit="Essentials">Kit: Essentials</button>
            <button class="pill small" data-kit="Developer">Kit: Developer</button>
            <button class="pill small" data-kit="Gamer">Kit: Gamer</button>
            <button class="pill solid small" id="hng-install">${t('Install selected (0)', 'Установить (0)')}</button>
          </div>
          <div class="grid-3" id="hng-grid"></div>
        `;

        const updateCount = () => {
          body.querySelector('#hng-install').textContent = t('Install selected', 'Установить') + ' (' + selected.size + ')';
        };
        const renderGrid = (list) => {
          const grid = body.querySelector('#hng-grid');
          grid.innerHTML = list.map(a => {
            const abbr = esc((a.name || '?').slice(0, 2).toUpperCase());
            return `
            <div class="esll-card" style="cursor:pointer;border-color:${selected.has(a.id) ? '#fff' : 'var(--hair)'}" data-hid="${esc(a.id)}">
              <div style="display:flex;justify-content:space-between;align-items:center;gap:10px">
                <div style="display:flex;align-items:center;gap:10px">
                  <span style="width:28px;height:28px;border-radius:7px;border:1px solid var(--hair);background:rgba(255,255,255,.06);display:inline-flex;align-items:center;justify-content:center;font-size:11px;letter-spacing:.08em">${abbr}</span>
                  <b>${esc(a.name)}</b>
                </div>
                <span class="badge">${esc(a.cat || a.version || 'winget')}</span>
              </div>
              <div class="esll-meta">${esc(a.desc || a.id)}</div>
              <div class="esll-meta mono" style="opacity:.6">${esc(a.id)}</div>
            </div>`;
          }).join('');
          grid.querySelectorAll('[data-hid]').forEach(card => {
            card.onclick = () => {
              const id = card.getAttribute('data-hid');
              if (selected.has(id)) selected.delete(id); else selected.add(id);
              updateCount();
              renderGrid(list);
            };
          });
        };

        renderGrid(catalog);
        body.querySelector('#hng-q').oninput = (e) => {
          const q = e.target.value.toLowerCase();
          renderGrid(catalog.filter(x => !q || (x.name + ' ' + x.cat + ' ' + x.desc + ' ' + x.id).toLowerCase().includes(q)));
        };
        body.querySelectorAll('[data-kit]').forEach(b => {
          b.onclick = () => {
            const kit = b.getAttribute('data-kit');
            catalog.filter(x => x.kit === kit).forEach(x => selected.add(x.id));
            updateCount();
            renderGrid(catalog);
          };
        });
        body.querySelector('#hng-wsearch').onclick = async () => {
          const q = body.querySelector('#hng-q').value.trim();
          if (!q) return;
          const sr = await api('programs', 'hangar_search', { query: q });
          if (sr.ok) renderGrid(sr.items || []);
        };
        body.querySelector('#hng-install').onclick = () => {
          if (!selected.size) return;
          runJob('programs', 'hangar_install', { ids: Array.from(selected) }, logEl, () => {});
        };
      }
    }

    root.querySelectorAll('.subtab').forEach(b => b.onclick = () => loadTab(b.getAttribute('data-ptab')));
    loadTab('rem');
  }

  WHA.registerModule('programs', { render });
})();
