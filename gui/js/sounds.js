window.WHASounds = (function () {
  const pool = {};
  const lastAt = {};
  let lastAny = 0;
  let enabled = localStorage.getItem('wha_sounds') !== '0';

  function play(name, vol) {
    if (!enabled) return;
    const now = performance.now();
    if (now - lastAny < 40) return;
    if (lastAt[name] && now - lastAt[name] < 110) return;
    lastAny = now;
    lastAt[name] = now;

    try {
      let a = pool[name];
      if (!a) {
        a = new Audio('/sounds/' + name + '.ogg');
        a.preload = 'auto';
        pool[name] = a;
      }
      a.volume = vol != null ? vol : 0.24;
      a.currentTime = 0;
      a.play().catch(function () {});
    } catch (e) {}
  }

  function isEnabled() { return enabled; }
  function setEnabled(v) {
    enabled = !!v;
    localStorage.setItem('wha_sounds', enabled ? '1' : '0');
    if (enabled) play('toggle_on', 0.22);
  }

  document.addEventListener('click', function (e) {
    if (!e.isTrusted) return;
    const t = e.target;
    if (!t || !t.closest) return;
    if (t.closest('.mod-btn, .tab, .subtab')) {
      play('tab_switch', 0.2);
    } else if (t.closest('#run')) {
      play('scan_start', 0.26);
    } else if (t.closest('#copy')) {
      play('copy', 0.22);
    } else if (t.closest('.pill, .filter, .hidebtn')) {
      play('click', 0.18);
    }
  });

  document.addEventListener('change', function (e) {
    if (!e.isTrusted) return;
    const t = e.target;
    if (t && t.type === 'checkbox') {
      play(t.checked ? 'toggle_on' : 'toggle_off', 0.2);
    }
  });

  return { play, isEnabled, setEnabled };
})();
