(function () {
  const { t, esc } = WHA;
  let cleanupFns = [];

  function cleanup() {
    cleanupFns.forEach(fn => { try { fn(); } catch (e) { } });
    cleanupFns = [];
  }

  function render(root) {
    cleanup();
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Device Tester', 'Тестер устройств')}</div>
          <div class="mod-sub">${t('Keyboard, dead pixels, speakers, microphone, camera, mouse CPS & controller', 'Клавиатура, битые пиксели, звук, микрофон, камера, мышь (CPS) и геймпад')}</div>
        </div>
      </div>

      <div class="subtabs">
        <button class="subtab active" data-ttab="kbd">${t('Keyboard', 'Клавиатура')}</button>
        <button class="subtab" data-ttab="scr">${t('Display', 'Экран')}</button>
        <button class="subtab" data-ttab="aud">${t('Audio & Mic', 'Звук и микрофон')}</button>
        <button class="subtab" data-ttab="cam">${t('Camera', 'Камера')}</button>
        <button class="subtab" data-ttab="mse">${t('Mouse & CPS', 'Мышь и CPS')}</button>
        <button class="subtab" data-ttab="pad">${t('Controller', 'Геймпад')}</button>
      </div>

      <div id="tst-body"></div>
    `;

    const body = root.querySelector('#tst-body');

    function loadTab(tab) {
      cleanup();
      root.querySelectorAll('.subtab').forEach(b => b.classList.toggle('active', b.getAttribute('data-ttab') === tab));

      if (tab === 'kbd') {
        const rows = [
          ['Escape','F1','F2','F3','F4','F5','F6','F7','F8','F9','F10','F11','F12'],
          ['Backquote','Digit1','Digit2','Digit3','Digit4','Digit5','Digit6','Digit7','Digit8','Digit9','Digit0','Minus','Equal','Backspace'],
          ['Tab','KeyQ','KeyW','KeyE','KeyR','KeyT','KeyY','KeyU','KeyI','KeyO','KeyP','BracketLeft','BracketRight','Backslash'],
          ['CapsLock','KeyA','KeyS','KeyD','KeyF','KeyG','KeyH','KeyJ','KeyK','KeyL','Semicolon','Quote','Enter'],
          ['ShiftLeft','KeyZ','KeyX','KeyC','KeyV','KeyB','KeyN','KeyM','Comma','Period','Slash','ShiftRight'],
          ['ControlLeft','MetaLeft','AltLeft','Space','AltRight','ContextMenu','ControlRight']
        ];
        const label = (c) => c.replace('Key','').replace('Digit','').replace('Left',' L').replace('Right',' R').replace('Control','Ctrl').replace('Escape','Esc').replace('Backquote','`').replace('BracketLeft','[').replace('BracketRight',']').replace('Backslash','\\').replace('Semicolon',';').replace('Quote',"'").replace('Comma',',').replace('Period','.').replace('Slash','/').replace('Minus','-').replace('Equal','=');
        body.innerHTML = `
          <div class="esll-meta" style="margin-bottom:10px">${t('Press any key to verify actuation.', 'Нажимайте клавиши для проверки отклика.')} <span id="kbd-last" class="badge"></span> <button class="hidebtn" id="kbd-rst">${t('Reset', 'Сброс')}</button></div>
          <div class="kbd-wrap">${rows.map(r => `<div class="kbd-row">${r.map(c => `<div class="kbd-key" data-k="${c}" style="${c==='Space'?'min-width:180px':''}">${esc(label(c))}</div>`).join('')}</div>`).join('')}</div>
        `;
        const onDown = (e) => {
          const el = body.querySelector(`[data-k="${e.code}"]`);
          if (el) { el.classList.add('down', 'hit'); }
          body.querySelector('#kbd-last').textContent = e.code;
        };
        const onUp = (e) => {
          const el = body.querySelector(`[data-k="${e.code}"]`);
          if (el) el.classList.remove('down');
        };
        window.addEventListener('keydown', onDown);
        window.addEventListener('keyup', onUp);
        cleanupFns.push(() => { window.removeEventListener('keydown', onDown); window.removeEventListener('keyup', onUp); });
        body.querySelector('#kbd-rst').onclick = () => body.querySelectorAll('.kbd-key').forEach(k => k.classList.remove('hit', 'down'));
      } else if (tab === 'scr') {
        const colors = ['#000000', '#ffffff', '#ff0000', '#00ff00', '#0000ff', '#808080'];
        body.innerHTML = `
          <div class="esll-card">
            <h4>${t('Dead Pixel & Uniformity Test', 'Проверка битых пикселей и равномерности')}</h4>
            <div class="esll-meta" style="margin-bottom:12px">${t('Click Launch Fullscreen Test, then click anywhere to cycle colors or press Esc to exit.', 'Нажмите Запуск во весь экран, кликайте для смены цвета, Esc — выход.')}</div>
            <button class="pill solid" id="scr-go">${t('Launch Fullscreen Test', 'Запуск во весь экран')}</button>
          </div>`;
        body.querySelector('#scr-go').onclick = () => {
          let idx = 0;
          const ov = document.createElement('div');
          ov.className = 'pixel-overlay';
          ov.style.background = colors[0];
          ov.innerHTML = `<div class="pixel-hint">${t('Click: next color · Esc: exit', 'Клик: следующий цвет · Esc: выход')}</div>`;
          document.body.appendChild(ov);
          ov.onclick = () => { idx = (idx + 1) % colors.length; ov.style.background = colors[idx]; };
          const onEsc = (e) => { if (e.key === 'Escape') { ov.remove(); window.removeEventListener('keydown', onEsc); } };
          window.addEventListener('keydown', onEsc);
        };
      } else if (tab === 'aud') {
        body.innerHTML = `
          <div class="grid-2">
            <div class="esll-card">
              <h4>${t('Speakers / Headphones', 'Динамики / Наушники')}</h4>
              <div class="mod-actions" style="margin-top:10px">
                <button class="pill small" data-pan="-1">${t('Left Channel', 'Левый канал')}</button>
                <button class="pill small" data-pan="0">${t('Stereo (440 Hz)', 'Стерео (440 Гц)')}</button>
                <button class="pill small" data-pan="1">${t('Right Channel', 'Правый канал')}</button>
              </div>
            </div>
            <div class="esll-card">
              <h4>${t('Microphone Level', 'Уровень микрофона')}</h4>
              <div class="mod-actions" style="margin:10px 0"><button class="pill small" id="mic-start">${t('Start Mic Test', 'Включить микрофон')}</button></div>
              <div class="meter"><i id="mic-bar" style="width:0%"></i></div>
              <div class="esll-meta" id="mic-st">${t('Idle', 'Ожидание')}</div>
            </div>
          </div>`;
        body.querySelectorAll('[data-pan]').forEach(b => {
          b.onclick = () => {
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
            const osc = ctx.createOscillator();
            const pan = ctx.createStereoPanner();
            const gain = ctx.createGain();
            osc.frequency.value = 440;
            pan.pan.value = Number(b.getAttribute('data-pan'));
            gain.gain.value = 0.15;
            osc.connect(pan).connect(gain).connect(ctx.destination);
            osc.start();
            setTimeout(() => { osc.stop(); ctx.close(); }, 600);
          };
        });
        body.querySelector('#mic-start').onclick = async () => {
          try {
            const stream = await navigator.mediaDevices.getUserMedia({ audio: true });
            const ctx = new (window.AudioContext || window.webkitAudioContext)();
            const src = ctx.createMediaStreamSource(stream);
            const an = ctx.createAnalyser();
            an.fftSize = 256;
            src.connect(an);
            const data = new Uint8Array(an.frequencyBinCount);
            let run = true;
            cleanupFns.push(() => { run = false; stream.getTracks().forEach(tr => tr.stop()); ctx.close(); });
            body.querySelector('#mic-st').textContent = t('Listening...', 'Прослушивание...');
            function tick() {
              if (!run) return;
              an.getByteFrequencyData(data);
              const avg = data.reduce((a, b) => a + b, 0) / data.length;
              body.querySelector('#mic-bar').style.width = Math.min(100, Math.round(avg * 1.8)) + '%';
              requestAnimationFrame(tick);
            }
            tick();
          } catch (e) {
            body.querySelector('#mic-st').textContent = String(e);
          }
        };
      } else if (tab === 'cam') {
        body.innerHTML = `
          <div class="esll-card">
            <h4>${t('Camera Preview', 'Проверка камеры')}</h4>
            <div class="mod-actions" style="margin:10px 0"><button class="pill small" id="cam-go">${t('Start Camera', 'Включить камеру')}</button></div>
            <video id="cam-vid" autoplay playsinline style="max-width:480px;width:100%;border-radius:10px;border:1px solid var(--hair);background:#000"></video>
            <div class="esll-meta" id="cam-info"></div>
          </div>`;
        body.querySelector('#cam-go').onclick = async () => {
          try {
            const stream = await navigator.mediaDevices.getUserMedia({ video: true });
            const vid = body.querySelector('#cam-vid');
            vid.srcObject = stream;
            cleanupFns.push(() => stream.getTracks().forEach(tr => tr.stop()));
            vid.onloadedmetadata = () => {
              body.querySelector('#cam-info').textContent = `${vid.videoWidth} × ${vid.videoHeight}`;
            };
          } catch (e) {
            body.querySelector('#cam-info').textContent = String(e);
          }
        };
      } else if (tab === 'mse') {
        let clicks = 0, running = false, t0 = 0;
        body.innerHTML = `
          <div class="esll-card">
            <h4>${t('Mouse & CPS Test (5 seconds)', 'Тест мыши и CPS (5 секунд)')}</h4>
            <div id="cps-pad" style="margin:14px 0;height:150px;border:1px dashed var(--hair);border-radius:12px;display:flex;align-items:center;justify-content:center;cursor:pointer;user-select:none;font-size:18px">
              ${t('Click here to start 5s CPS test', 'Кликните здесь для 5-сек теста CPS')}
            </div>
            <div class="esll-meta" id="cps-res"></div>
          </div>`;
        const pad = body.querySelector('#cps-pad');
        pad.onmousedown = () => {
          if (!running) {
            running = true; clicks = 0; t0 = performance.now();
            setTimeout(() => {
              running = false;
              const cps = (clicks / 5).toFixed(1);
              pad.textContent = `${clicks} clicks · ${cps} CPS`;
              body.querySelector('#cps-res').textContent = t('Click again to retry.', 'Кликните снова для повтора.');
            }, 5000);
          }
          if (running) {
            clicks++;
            pad.textContent = `${clicks} (${((performance.now() - t0) / 1000).toFixed(1)}s)`;
          }
        };
      } else if (tab === 'pad') {
        body.innerHTML = `<div class="esll-card"><h4>${t('Gamepad / Controller', 'Геймпад / Контроллер')}</h4><div id="pad-out" class="esll-meta">${t('Connect a controller and press any button...', 'Подключите геймпад и нажмите любую кнопку...')}</div></div>`;
        let active = true;
        cleanupFns.push(() => { active = false; });
        function poll() {
          if (!active) return;
          const pads = navigator.getGamepads ? Array.from(navigator.getGamepads()).filter(Boolean) : [];
          const out = body.querySelector('#pad-out');
          if (out && pads.length) {
            const p = pads[0];
            const pressed = p.buttons.map((b, i) => b.pressed ? 'B' + i : '').filter(Boolean).join(' ');
            const axes = p.axes.map(a => a.toFixed(2)).join(', ');
            out.innerHTML = `<div><b>${esc(p.id)}</b></div><div class="mono" style="margin-top:6px">Axes: [${axes}]</div><div class="mono" style="margin-top:4px">Pressed: ${pressed || '—'}</div>`;
          }
          requestAnimationFrame(poll);
        }
        poll();
      }
    }

    root.querySelectorAll('.subtab').forEach(b => b.onclick = () => loadTab(b.getAttribute('data-ttab')));
    loadTab('kbd');
  }

  WHA.registerModule('tester', { render, leave: cleanup });
})();
