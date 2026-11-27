(function () {
  const { t, esc, api } = WHA;

  function runGpuBench(canvas) {
    return new Promise((resolve) => {
      const gl = canvas.getContext('webgl2') || canvas.getContext('webgl');
      if (!gl) { resolve({ ok: false, score: 0, fps: 0 }); return; }

      const vs = gl.createShader(gl.VERTEX_SHADER);
      gl.shaderSource(vs, 'attribute vec2 p;void main(){gl_Position=vec4(p,0.0,1.0);}');
      gl.compileShader(vs);

      const fs = gl.createShader(gl.FRAGMENT_SHADER);
      gl.shaderSource(fs, `precision mediump float;uniform float u_t;void main(){vec2 uv=gl_FragCoord.xy/400.0;float v=0.0;for(int i=0;i<32;i++){float fi=float(i);v+=sin(uv.x*8.0+u_t+fi*0.3)*cos(uv.y*8.0-u_t+fi*0.2);}float g=clamp(v*0.05+0.5,0.0,1.0);gl_FragColor=vec4(g,g,g,1.0);}`);
      gl.compileShader(fs);

      const prog = gl.createProgram();
      gl.attachShader(prog, vs);
      gl.attachShader(prog, fs);
      gl.linkProgram(prog);
      gl.useProgram(prog);

      const buf = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, buf);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([-1,-1, 1,-1, -1,1, 1,1]), gl.STATIC_DRAW);
      const loc = gl.getAttribLocation(prog, 'p');
      gl.enableVertexAttribArray(loc);
      gl.vertexAttribPointer(loc, 2, gl.FLOAT, false, 0, 0);
      const uT = gl.getUniformLocation(prog, 'u_t');

      let frames = 0;
      const start = performance.now();
      function step(now) {
        gl.uniform1f(uT, (now - start) * 0.002);
        gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
        frames++;
        if (now - start < 2200) {
          requestAnimationFrame(step);
        } else {
          const fps = Math.round((frames / ((now - start) / 1000)));
          resolve({ ok: true, fps, score: fps * 25 });
        }
      }
      requestAnimationFrame(step);
    });
  }

  async function render(root) {
    root.innerHTML = `
      <div class="mod-head">
        <div>
          <div class="mod-title">${t('Benchmark Testing', 'Тестирование производительности')}</div>
          <div class="mod-sub">${t('CPU, GPU WebGL shader, memory bandwidth & disk read/write benchmarks', 'Тест процессора, GPU WebGL, пропускной способности памяти и скорости диска')}</div>
        </div>
        <div class="mod-actions">
          <button class="pill solid" id="bnc-all">${t('Run Full Suite', 'Запустить все тесты')}</button>
        </div>
      </div>

      <div class="grid-4" style="margin-bottom:16px">
        <div class="esll-card">
          <h4>CPU</h4>
          <div class="esll-big" id="bnc-cpu-v">—</div>
          <div class="esll-meta" id="bnc-cpu-d">${t('Single & multi-core score', 'Однопоточный и многопоточный тест')}</div>
          <div style="margin-top:10px"><button class="pill small" id="bnc-cpu">${t('Test CPU', 'Тест CPU')}</button></div>
        </div>
        <div class="esll-card">
          <h4>GPU (WebGL)</h4>
          <div class="esll-big" id="bnc-gpu-v">—</div>
          <div class="esll-meta" id="bnc-gpu-d">${t('Fragment shader throughput', 'Нагрузка фрагментного шейдера')}</div>
          <canvas id="bnc-canvas" width="240" height="72" style="width:100%;height:54px;border-radius:6px;border:1px solid var(--hair);margin-top:8px;display:block"></canvas>
          <div style="margin-top:10px"><button class="pill small" id="bnc-gpu">${t('Test GPU', 'Тест GPU')}</button></div>
        </div>
        <div class="esll-card">
          <h4>${t('Memory', 'Память')}</h4>
          <div class="esll-big" id="bnc-mem-v">—</div>
          <div class="esll-meta" id="bnc-mem-d">${t('Copy bandwidth (MB/s)', 'Пропускная способность (МБ/с)')}</div>
          <div style="margin-top:10px"><button class="pill small" id="bnc-mem">${t('Test RAM', 'Тест ОЗУ')}</button></div>
        </div>
        <div class="esll-card">
          <h4>${t('Disk', 'Диск')}</h4>
          <div class="esll-big" id="bnc-dsk-v">—</div>
          <div class="esll-meta" id="bnc-dsk-d">${t('128 MB seq read/write', 'Последовательное чтение/запись 128 МБ')}</div>
          <div style="margin-top:10px"><button class="pill small" id="bnc-dsk">${t('Test Disk C:', 'Тест C:')}</button></div>
        </div>
      </div>

      <div class="esll-card">
        <h4>${t('Saved Benchmark History', 'История результатов')}</h4>
        <div id="bnc-hist"></div>
      </div>
    `;

    const scores = { cpu: 0, gpu: 0, memory: 0, disk: 0 };

    async function loadHistory() {
      const h = await api('benchmark', 'history');
      const items = h.items || [];
      root.querySelector('#bnc-hist').innerHTML = items.length ? `<table class="esll-table"><thead><tr><th>${t('Date', 'Дата')}</th><th>CPU</th><th>GPU</th><th>RAM</th><th>Disk</th><th>${t('Overall', 'Итог')}</th></tr></thead><tbody>${
        items.map(r => `<tr><td class="mono">${esc(r.at)}</td><td class="mono">${r.cpu}</td><td class="mono">${r.gpu}</td><td class="mono">${r.memory}</td><td class="mono">${r.disk}</td><td><b>${r.overall}</b></td></tr>`).join('')
      }</tbody></table>` : `<div class="esll-meta">${t('No saved benchmark runs yet.', 'Сохранённых результатов пока нет.')}</div>`;
    }

    async function testCpu() {
      root.querySelector('#bnc-cpu-v').textContent = '...';
      const r = await api('benchmark', 'cpu');
      if (r.ok) {
        scores.cpu = r.multiScore;
        root.querySelector('#bnc-cpu-v').textContent = r.multiScore;
        root.querySelector('#bnc-cpu-d').textContent = `1T: ${r.singleScore} · ${r.threads}T · ${r.elapsedMs} ms`;
      }
    }
    async function testGpu() {
      root.querySelector('#bnc-gpu-v').textContent = '...';
      const r = await runGpuBench(root.querySelector('#bnc-canvas'));
      scores.gpu = r.score;
      root.querySelector('#bnc-gpu-v').textContent = r.score;
      root.querySelector('#bnc-gpu-d').textContent = `${r.fps} FPS`;
    }
    async function testMem() {
      root.querySelector('#bnc-mem-v').textContent = '...';
      const r = await api('benchmark', 'memory');
      if (r.ok) {
        scores.memory = r.score;
        root.querySelector('#bnc-mem-v').textContent = r.score;
        root.querySelector('#bnc-mem-d').textContent = `${r.bandwidthMBs} MB/s`;
      }
    }
    async function testDisk() {
      root.querySelector('#bnc-dsk-v').textContent = '...';
      const r = await api('benchmark', 'disk', { drive: 'C:' });
      if (r.ok) {
        scores.disk = r.score;
        root.querySelector('#bnc-dsk-v').textContent = r.score;
        root.querySelector('#bnc-dsk-d').textContent = `R: ${r.readMBs} MB/s · W: ${r.writeMBs} MB/s`;
      }
    }

    root.querySelector('#bnc-cpu').onclick = testCpu;
    root.querySelector('#bnc-gpu').onclick = testGpu;
    root.querySelector('#bnc-mem').onclick = testMem;
    root.querySelector('#bnc-dsk').onclick = testDisk;
    root.querySelector('#bnc-all').onclick = async () => {
      await testCpu();
      await testGpu();
      await testMem();
      await testDisk();
      const overall = Math.round((scores.cpu + scores.gpu + scores.memory + scores.disk) / 4);
      await api('benchmark', 'save', { ...scores, overall });
      loadHistory();
    };

    loadHistory();
  }

  WHA.registerModule('benchmark', { render });
})();
