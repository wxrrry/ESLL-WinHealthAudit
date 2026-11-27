(function () {
  const { t, esc, api, toast } = WHA;

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Automation & Backups', 'Автоматизация и бэкапы')}</div>
          <div class="mod-sub">${t('Scheduled Windows maintenance tasks, restore points, driver backups & quarantine', 'Планировщик обслуживания Windows, точки восстановления, бэкапы драйверов и карантин')}</div>
        </div>
        <div class="mod-actions">
          <button class="pill small" id="aut-rp-now">${t('+ Create Restore Point', '+ Точка восстановления')}</button>
        </div>
      </div>

      <div class="grid-4" id="aut-kpis" style="margin-bottom:16px"></div>

      <div class="grid-2" style="margin-bottom:16px">
        <div class="esll-card">
          <h4>${t('Quick Presets & New Task', 'Быстрые сценарии и новая задача')}</h4>
          <div class="mod-actions" style="margin-bottom:14px">
            <button class="pill small" data-preset="audit-daily">${t('+ Daily Audit (09:00)', '+ Ежедневный аудит (09:00)')}</button>
            <button class="pill small" data-preset="clean-weekly">${t('+ Weekly Temp Clean', '+ Еженедельная очистка')}</button>
            <button class="pill small" data-preset="audit-logon">${t('+ Audit at Logon', '+ Аудит при входе')}</button>
          </div>
          <div style="border-top:1px solid var(--hair);padding-top:12px">
            <div class="folder-row"><input id="aut-name" placeholder="Task name" value="CustomAudit"></div>
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
        </div>

        <div class="esll-card">
          <h4>${t('Scheduled Tasks', 'Запланированные задачи')}</h4>
          <div id="aut-tasks"><div class="esll-meta">${t('Loading...', 'Загрузка...')}</div></div>
        </div>
      </div>

      <div class="grid-2">
        <div class="esll-card">
          <h4>${t('System Restore Points', 'Точки восстановления Windows')}</h4>
          <div id="aut-rps"></div>
        </div>
        <div class="esll-card">
          <h4>${t('Driver Backups & Quarantine', 'Бэкапы драйверов и карантин')}</h4>
          <div id="aut-baks"></div>
        </div>
      </div>
    `;

    async function load() {
      const [tasks, baks] = await Promise.all([
        api('automation', 'tasks_list'),
        api('automation', 'backups_list')
      ]);

      const tItems = tasks.items || [];
      const rps = baks.restorePoints || [];
      const drvs = baks.driverBackups || [];
      const quars = baks.quarantine || [];

      root.querySelector('#aut-kpis').innerHTML = `
        <div class="esll-card"><h4>${t('Scheduled Tasks', 'Задачи')}</h4><div class="esll-big">${tItems.length}</div><div class="esll-meta">${t('Active in Task Scheduler', 'В планировщике Windows')}</div></div>
        <div class="esll-card"><h4>${t('Restore Points', 'Точки восст.')}</h4><div class="esll-big">${rps.length}</div><div class="esll-meta">${t('Windows VSS snapshots', 'Снимки системы VSS')}</div></div>
        <div class="esll-card"><h4>${t('Driver Backups', 'Бэкапы драйверов')}</h4><div class="esll-big">${drvs.length}</div><div class="esll-meta">${t('Exported INF packages', 'Экспортированные пакеты')}</div></div>
        <div class="esll-card"><h4>${t('Quarantine', 'Карантин')}</h4><div class="esll-big">${quars.length}</div><div class="esll-meta">${t('Restorable leftovers', 'Восстановимые остатки')}</div></div>
      `;

      const tEl = root.querySelector('#aut-tasks');
      tEl.innerHTML = tItems.length ? `<table class="esll-table"><thead><tr><th>${t('Name', 'Имя')}</th><th>${t('State', 'Статус')}</th><th>${t('Next Run', 'След. запуск')}</th><th></th></tr></thead><tbody>${
        tItems.map(x => `<tr><td><b>${esc(x.name)}</b></td><td><span class="badge solid">${esc(x.state)}</span></td><td class="mono">${esc(x.nextRun)}</td><td style="text-align:right;white-space:nowrap">
          <button class="hidebtn" data-trun="${esc(x.name)}">${t('Run', 'Запуск')}</button>
          <button class="hidebtn" data-tdel="${esc(x.name)}">${t('Delete', 'Удалить')}</button>
        </td></tr>`).join('')
      }</tbody></table>` : `<div class="esll-meta">${t('No scheduled tasks yet. Use a preset on the left to add one.', 'Задач пока нет. Выберите готовый сценарий слева.')}</div>`;

      tEl.querySelectorAll('[data-trun]').forEach(b => b.onclick = async () => {
        await api('automation', 'task_run', { name: b.getAttribute('data-trun') });
        toast(t('Task started', 'Задача запущена'));
        load();
      });
      tEl.querySelectorAll('[data-tdel]').forEach(b => b.onclick = async () => {
        await api('automation', 'task_delete', { name: b.getAttribute('data-tdel') });
        toast(t('Task deleted', 'Задача удалена'));
        load();
      });

      root.querySelector('#aut-rps').innerHTML = rps.length ? `<table class="esll-table"><thead><tr><th>#</th><th>${t('Date', 'Дата')}</th><th>${t('Description', 'Описание')}</th></tr></thead><tbody>${
        rps.slice(0, 8).map(r => `<tr><td class="mono">${r.seq}</td><td class="mono">${esc(r.date)}</td><td>${esc(r.desc)}</td></tr>`).join('')
      }</tbody></table>` : `<div class="esll-meta">${t('No restore points found (or requires Admin).', 'Точки восстановления не найдены (или нужны права администратора).')}</div>`;

      root.querySelector('#aut-baks').innerHTML = `
        <div class="esll-meta"><b>${t('Driver Backups', 'Бэкапы драйверов')}:</b> ${drvs.length}</div>
        ${drvs.slice(0, 5).map(d => `<div class="esll-meta mono">${esc(d.date)} · ${esc(d.name)}</div>`).join('')}
        <div class="esll-meta" style="margin-top:12px"><b>${t('Quarantine Sessions', 'Сессии карантина')}:</b> ${quars.length}</div>
        ${quars.slice(0, 5).map(q => `<div class="esll-meta mono">${esc(q.date)} · ${esc(q.id)}</div>`).join('')}
      `;
    }

    root.querySelector('#aut-rp-now').onclick = async () => {
      const r = await api('drivers', 'restore_point', { desc: 'WinHealthAudit Manual Checkpoint' });
      toast(r.ok ? t('Restore point created', 'Точка восстановления создана') : (r.error || 'Admin required'));
      load();
    };

    root.querySelectorAll('[data-preset]').forEach(b => {
      b.onclick = async () => {
        const p = b.getAttribute('data-preset');
        const cfg = p === 'audit-daily'
          ? { name: 'DailyHealthAudit', kind: 'audit', schedule: 'daily', time: '09:00' }
          : p === 'clean-weekly'
          ? { name: 'WeeklyTempClean', kind: 'clean', schedule: 'weekly', time: '12:00' }
          : { name: 'LogonHealthAudit', kind: 'audit', schedule: 'logon', time: '09:00' };
        const r = await api('automation', 'task_create', cfg);
        toast(r.ok ? t('Task created', 'Задача создана') : t('Failed (try Admin)', 'Ошибка (нужны права админа)'));
        load();
      };
    });

    root.querySelector('#aut-add').onclick = async () => {
      const r = await api('automation', 'task_create', {
        name: root.querySelector('#aut-name').value,
        kind: root.querySelector('#aut-kind').value,
        schedule: root.querySelector('#aut-sched').value,
        time: root.querySelector('#aut-time').value
      });
      toast(r.ok ? t('Task created', 'Задача создана') : t('Failed', 'Ошибка'));
      load();
    };

    load();
  }

  WHA.registerModule('automation', { render });
})();
