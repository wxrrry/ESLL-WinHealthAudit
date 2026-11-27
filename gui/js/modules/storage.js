(function () {
  const { t, esc, fmtBytes, api, runJob } = WHA;

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Storage Checker', 'Анализатор дисков')}</div>
          <div class="mod-sub">${t('Folder space breakdown, largest files, file types & duplicate finder', 'Карта занятого места, крупные файлы, типы файлов и поиск дубликатов')}</div>
        </div>
        <div class="mod-actions">
          <input class="find-input" id="stg-path" value="C:\\" style="width:240px">
          <button class="pill solid" id="stg-scan">${t('Scan Path', 'Сканировать')}</button>
          <button class="pill" id="stg-dups">${t('Find Duplicates', 'Дубликаты')}</button>
        </div>
      </div>

      <div id="stg-drives" class="grid-4" style="margin-bottom:16px"></div>
      <div id="stg-log" class="job-log hidden"></div>
      <div id="stg-result"></div>
    `;

    const logEl = root.querySelector('#stg-log');
    const resEl = root.querySelector('#stg-result');
    const pathInput = root.querySelector('#stg-path');

    const dr = await api('storage', 'drives');
    if (dr.ok) {
      root.querySelector('#stg-drives').innerHTML = (dr.items || []).map(d => `
        <div class="esll-card" style="cursor:pointer" data-drv="${esc(d.root)}">
          <h4>${esc(d.drive)} ${esc(d.label || '')}</h4>
          <div class="esll-big">${d.usedPct}<small>%</small></div>
          <div class="esll-meta">${fmtBytes(d.freeBytes)} ${t('free of', 'свободно из')} ${fmtBytes(d.totalBytes)}</div>
          <div class="meter"><i style="width:${d.usedPct}%"></i></div>
        </div>
      `).join('');
      root.querySelectorAll('[data-drv]').forEach(c => {
        c.onclick = () => {
          pathInput.value = c.getAttribute('data-drv');
          startScan(pathInput.value);
        };
      });
    }

    function startScan(target) {
      runJob('storage', 'scan', { path: target }, logEl, (data) => {
        if (!data || !data.ok) {
          resEl.innerHTML = `<div class="esll-card">${esc(data && data.error || 'Scan failed')}</div>`;
          return;
        }
        const total = Math.max(1, data.totalBytes);
        const topSegs = (data.folders || []).slice(0, 8);
        const shades = ['#ffffff', '#cccccc', '#a3a3a3', '#808080', '#636363', '#4a4a4a', '#363636', '#262626'];

        resEl.innerHTML = `
          <div class="treemap-bar">
            ${topSegs.map((f, i) => `<div class="treemap-seg" title="${esc(f.name)} (${fmtBytes(f.bytes)})" data-go="${f.isDir ? esc(f.path) : ''}" style="width:${Math.max(2, (f.bytes / total) * 100)}%;background:${shades[i % shades.length]}"></div>`).join('')}
          </div>

          <div class="grid-2" style="margin-bottom:16px">
            <div class="esll-card">
              <h4>${t('Folders & Items in', 'Папки и файлы в')} ${esc(data.path)}</h4>
              <table class="esll-table">
                <thead><tr><th>${t('Name', 'Имя')}</th><th>${t('Size', 'Размер')}</th><th>%</th><th></th></tr></thead>
                <tbody>
                  ${(data.folders || []).slice(0, 25).map(f => `
                    <tr>
                      <td>${f.isDir ? '▸ ' : ''}${esc(f.name)}</td>
                      <td class="mono">${fmtBytes(f.bytes)}</td>
                      <td class="mono">${((f.bytes / total) * 100).toFixed(1)}%</td>
                      <td style="text-align:right;white-space:nowrap">
                        ${f.isDir ? `<button class="hidebtn" data-go="${esc(f.path)}">${t('Open', 'Открыть')}</button>` : ''}
                        <button class="hidebtn" data-rev="${esc(f.path)}">${t('Explorer', 'Проводник')}</button>
                      </td>
                    </tr>`).join('')}
                </tbody>
              </table>
            </div>

            <div class="esll-card">
              <h4>${t('Largest Files (>10 MB)', 'Крупнейшие файлы (>10 МБ)')}</h4>
              <table class="esll-table">
                <thead><tr><th>${t('File', 'Файл')}</th><th>${t('Size', 'Размер')}</th><th></th></tr></thead>
                <tbody>
                  ${(data.largest || []).slice(0, 20).map(f => `
                    <tr>
                      <td title="${esc(f.path)}">${esc(f.name)}</td>
                      <td class="mono">${fmtBytes(f.bytes)}</td>
                      <td style="text-align:right;white-space:nowrap">
                        <button class="hidebtn" data-rev="${esc(f.path)}">${t('Show', 'Показать')}</button>
                        <button class="hidebtn" data-del="${esc(f.path)}">${t('Trash', 'В корзину')}</button>
                      </td>
                    </tr>`).join('')}
                </tbody>
              </table>
            </div>
          </div>
        `;

        resEl.querySelectorAll('[data-go]').forEach(el => {
          const p = el.getAttribute('data-go');
          if (p) el.onclick = () => { pathInput.value = p; startScan(p); };
        });
        resEl.querySelectorAll('[data-rev]').forEach(el => {
          el.onclick = () => api('storage', 'reveal', { path: el.getAttribute('data-rev') });
        });
        resEl.querySelectorAll('[data-del]').forEach(el => {
          el.onclick = async () => {
            await api('storage', 'recycle', { path: el.getAttribute('data-del') });
            el.closest('tr').remove();
          };
        });
      });
    }

    root.querySelector('#stg-scan').onclick = () => startScan(pathInput.value.trim());
    root.querySelector('#stg-dups').onclick = () => {
      runJob('storage', 'duplicates', { path: pathInput.value.trim() }, logEl, (data) => {
        const groups = (data && data.groups) ? data.groups : [];
        if (!groups.length) {
          resEl.innerHTML = `<div class="esll-card">${t('No duplicate files found.', 'Дубликаты не найдены.')}</div>`;
          return;
        }
        resEl.innerHTML = `<div class="esll-card"><h4>${t('Duplicate Groups', 'Группы дубликатов')} (${groups.length})</h4>` +
          groups.slice(0, 30).map(g => `<div style="padding:8px 0;border-bottom:1px solid var(--hair)">
            <div><b>${fmtBytes(g.bytes)}</b> × ${g.count} <span class="badge">${esc(g.hash)}</span></div>
            ${g.files.map(fp => `<div class="esll-meta mono">${esc(fp)} <button class="hidebtn" data-del="${esc(fp)}">${t('Trash', 'В корзину')}</button></div>`).join('')}
          </div>`).join('') + `</div>`;
        resEl.querySelectorAll('[data-del]').forEach(el => {
          el.onclick = async () => {
            await api('storage', 'recycle', { path: el.getAttribute('data-del') });
            el.parentElement.remove();
          };
        });
      });
    };
  }

  WHA.registerModule('storage', { render });
})();
