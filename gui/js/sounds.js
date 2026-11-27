window.WHASounds = (function () {
  const cache = {};
  let enabled = localStorage.getItem('wha_sounds') !== '0';

  function play(name, vol) {
    if (!enabled) return;
    try {
      let audio = cache[name];
      if (!audio) {
        audio = new Audio('/sounds/' + name + '.ogg');
        cache[name] = audio;
      }
      const clone = audio.cloneNode();
      clone.volume = vol != null ? vol : 0.28;
      clone.play().catch(function () {});
    } catch (e) {}
  }

  function isEnabled() { return enabled; }
  function setEnabled(v) {
    enabled = !!v;
    localStorage.setItem('wha_sounds', enabled ? '1' : '0');
    if (enabled) play('toggle_on');
  }

  document.addEventListener('click', function (e) {
    const t = e.target;
    if (!t || !t.closest) return;
    if (t.closest('.mod-btn, .tab, .subtab')) {
      play('tab_switch', 0.24);
    } else if (t.closest('#run')) {
      play('scan_start', 0.3);
    } else if (t.closest('#copy')) {
      play('copy', 0.26);
    } else if (t.closest('.pill, .filter, .hidebtn')) {
      play('click', 0.22);
    }
  });

  document.addEventListener('change', function (e) {
    const t = e.target;
    if (t && t.type === 'checkbox') {
      play(t.checked ? 'toggle_on' : 'toggle_off', 0.22);
    }
  });

  return { play, isEnabled, setEnabled };
})();
