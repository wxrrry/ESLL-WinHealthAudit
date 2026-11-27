(function () {
  const { t, esc, api } = WHA;

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Automation & Backups', 'Автоматизация и бэкапы')}</div>
          <div class="mod-sub">${t('Scheduled Windows maintenance tasks, restore points, driver backups & quarantine', 'Планировщик обслуживания Windows, точки восстановления, бэкапы драйверов и карантин')}</div>
        </div>
      </div>

      <div class="grid-2" style="margin-bottom:16px">
        <div class="esll-card">
          <h4>${t('Create Scheduled Task', 'Создать задачу по расписанию')}</h4>
          <div class="folder-row"><input id="aut-name" placeholder="Name (e.g. DailyAudit)" value="DailyAudit"></div>
          <div class="mod-actions" style="margin-top:10px">
            <select id="aut-kind" class="pill small" style="background:#000;color:#fff">
              <option value="audit">${t('Health Audit', 'Аудит системы')}</option>
              <option value="clean">${t('Temp Cleanup', 'Очистка временных файлов')}</option>
            </select>
            <select id="aut-sched" class="pill small" style="background:#000;color:#fff">
              <option value="daily">${t('Daily', 'Ежедневно')}</option>
              <option value="weekly">${t('Weekly', 'Еженедельно')}</option>
              <option value="logon">${t('At Logon', 'При входе')}</option>
            </select>
            <input id="aut-time" value="09:00" class="find-input" style="width:84px;padding:4px 10px">
            <button class="pill small solid" id="aut-add">${t('Create', 'Создать')}</button>
          </div>
        </div>

        <div class="esll-card">
          <h4>${t('Scheduled Tasks', 'Активные задачи')}</h4>
          <div id="aut-tasks"></div>
        </div>
      </div>

      <div class="esll-card">
        <h4>${t('Restore Points & Backups', 'Точки восстановления и резервные копии')}</h4>
        <div id="aut-baks"></div>
      </div>
    `;

    async function load() {
      const [tasks, baks] = await Promise.all([
        api('automation', 'tasks_list'),
        api('automation', 'backups_list')
      ]);

      const tEl = root.querySelector('#aut-tasks');
      const tItems = tasks.items || [];
      tEl.innerHTML = tItems.length ? `<table class="esll-table"><thead><tr><th>${t('Name', 'Имя')}</th><th>${t('State', 'Статус')}</th><th>${t('Next Run', 'След. запуск')}</th><th></th></tr></thead><tbody>${
        tItems.map(x => `<tr><td>${esc(x.name)}</td><td><span class="badge">${esc(x.state)}</span></td><td class="mono">${esc(x.nextRun)}</td><td style="text-align:right">
          <button class="hidebtn" data-trun="${esc(x.name)}">${t('Run', 'Запуск')}</button>
          <button class="hidebtn" data-tdel="${esc(x.name)}">${t('Delete', 'Удалить')}</button>
        </td></tr>`).join('')
      }</tbody></table>` : `<div class="esll-meta">${t('No WinHealthAudit scheduled tasks yet.', 'Запланированных задач пока нет.')}</div>`;

      tEl.querySelectorAll('[data-trun]').forEach(b => b.onclick = async () => { await api('automation', 'task_run', { name: b.getAttribute('data-trun') }); load(); });
      tEl.querySelectorAll('[data-tdel]').forEach(b => b.onclick = async () => { await api('automation', 'task_delete', { name: b.getAttribute('data-tdel') }); load(); });

      const bEl = root.querySelector('#aut-baks');
      const rps = baks.restorePoints || [];
      const drvs = baks.driverBackups || [];
      const quars = baks.quarantine || [];
      bEl.innerHTML = `
        <div class="esll-meta"><b>${t('System Restore Points', 'Точки восстановления')}:</b> ${rps.length}</div>
        ${rps.slice(0, 6).map(r => `<div class="esll-meta mono">#${r.seq} · ${esc(r.date)} · ${esc(r.desc)}</div>`).join('')}
        <div class="esll-meta" style="margin-top:10px"><b>${t('Driver Backups', 'Бэкапы драйверов')}:</b> ${drvs.length} · <b>${t('Quarantine Sessions', 'Сессии карантина')}:</b> ${quars.length}</div>
      `;
    }

    root.querySelector('#aut-add').onclick = async () => {
      await api('automation', 'task_create', {
        name: root.querySelector('#aut-name').value,
        kind: root.querySelector('#aut-kind').value,
        schedule: root.querySelector('#aut-sched').value,
        time: root.querySelector('#aut-time').value
      });
      load();
    };

    load();
  }

  WHA.registerModule('automation', { render });
})();
