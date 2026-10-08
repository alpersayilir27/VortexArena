// Shared viewer chrome, icon set and sample data for the UI mockups.
(function () {
  const PAGES = [
    ['admin-hud.html', 'Admin HUD'],
    ['istatistik.html', 'İstatistikler'],
    ['tercihler.html', 'Tercihler'],
    ['mac-sonu.html', 'Maç sonu (oyuncu)'],
    ['oyuncu-hud.html', 'Oyuncu HUD (VR)'],
    ['asci.html', 'Aşçı (Burger)'],
    ['kit.html', 'Tema kiti'],
  ];

  // 24x24 stroke icons; filled shapes carry their own fill.
  const F = ' fill="currentColor" stroke="none"';
  const P = {
    sliders: '<path d="M4 6h9M17 6h3M4 12h3M11 12h9M4 18h11M19 18h1"/><circle cx="15" cy="6" r="2"/><circle cx="9" cy="12" r="2"/><circle cx="17" cy="18" r="2"/>',
    chart: '<path d="M5 20V11M12 20V4M19 20v-6"/>',
    eye: '<path d="M2 12s3.6-7 10-7 10 7 10 7-3.6 7-10 7S2 12 2 12z"/><circle cx="12" cy="12" r="3"/>',
    target: '<circle cx="12" cy="12" r="6"/><path d="M12 2v5M12 17v5M2 12h5M17 12h5"/>',
    scale: '<path d="M12 3v18M8 7l4-4 4 4M8 17l4 4 4-4"/>',
    kick: '<path d="M10 4H5v16h5M15 8l4 4-4 4M19 12H9"/>',
    pencil: '<path d="M4 20l1-4L16 5l3 3L8 19l-4 1zM14 7l3 3"/>',
    trash: '<path d="M4 7h16M10 7V4h4v3M6 7l1 13h10l1-13"/>',
    battery: '<rect x="2" y="8" width="17" height="9" rx="1.5"/><path d="M22 11v3"/>',
    body: '<circle cx="12" cy="5" r="2.5"/><path d="M12 8v7M6 11h12M8 21l4-6 4 6"/>',
    skull: '<path d="M5 11a7 7 0 0114 0v4l-2 1v3H7v-3l-2-1v-4z"/><circle cx="9.5" cy="11.5" r="1.3"' + F + '/><circle cx="14.5" cy="11.5" r="1.3"' + F + '/>',
    warn: '<path d="M12 4l9.5 16h-19L12 4z"/><path d="M12 10v4.5M12 17.4v.1"/>',
    play: '<path d="M7 4l13 8-13 8V4z"' + F + '/>',
    pause: '<path d="M6.5 4h4v16h-4zM13.5 4h4v16h-4z"' + F + '/>',
    stop: '<rect x="5" y="5" width="14" height="14" rx="1"' + F + '/>',
    x: '<path d="M6 6l12 12M18 6L6 18"/>',
    left: '<path d="M15 5l-7 7 7 7"/>',
    right: '<path d="M9 5l7 7-7 7"/>',
    down: '<path d="M5 9l7 7 7-7"/>',
    prev: '<path d="M19 5L9 12l10 7V5z"' + F + '/><path d="M5 5v14"/>',
    next: '<path d="M5 5l10 7-10 7V5z"' + F + '/><path d="M19 5v14"/>',
    vol: '<path d="M4 10v4h4l5 4V6L8 10H4z"/><path d="M16.5 9a4.5 4.5 0 010 6"/>',
    mute: '<path d="M4 10v4h4l5 4V6L8 10H4z"/><path d="M17 10l4 4M21 10l-4 4"/>',
    refresh: '<path d="M20 12a8 8 0 11-2.4-5.7"/><path d="M20 4v5h-5"/>',
    power: '<path d="M12 3v9"/><path d="M6.3 6.8a8 8 0 1011.4 0"/>',
    // kalibrasyon kipleri (tercihler)
    anchor: '<circle cx="12" cy="5" r="3"/><path d="M12 22V8"/><path d="M5 12H2a10 10 0 0020 0h-3"/>',
    history: '<path d="M3 12a9 9 0 109-9 9.75 9.75 0 00-6.74 2.74L3 8"/><path d="M3 3v5h5"/><path d="M12 7v5l4 2"/>',
    cloud: '<path d="M17.5 19H9a7 7 0 116.71-9h1.79a4.5 4.5 0 110 9Z"/>',
    // oyuncu HUD (VR)
    heart: '<path d="M19 14c1.49-1.46 3-3.21 3-5.5A5.5 5.5 0 0 0 16.5 3c-1.76 0-3 .5-4.5 2-1.5-1.5-2.74-2-4.5-2A5.5 5.5 0 0 0 2 8.5c0 2.3 1.5 4.05 3 5.5l7 7Z"/>',
    clock: '<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3 2"/>',
    shield: '<path d="M12 22s8-4 8-10V5l-8-3-8 3v7c0 6 8 10 8 10z"/>',
    timer: '<path d="M10 2h4M12 14l3-3"/><circle cx="12" cy="14" r="8"/>',
    home: '<path d="M3 11l9-8 9 8v10a1 1 0 01-1 1h-5v-7h-6v7H4a1 1 0 01-1-1V11z"/>',
    burger: '<path d="M4 10c0-3.6 3.6-6 8-6s8 2.4 8 6H4z"/><path d="M3 13.5h18"/><path d="M4 17h16v1a2 2 0 01-2 2H6a2 2 0 01-2-2v-1z"/>',
    flame: '<path d="M8.5 14.5A2.5 2.5 0 0011 12c0-1.38-.5-2-1-3-1.07-2.14-.22-4.05 2-6 .5 2.5 2 4.9 4 6.5 2 1.6 3 3.5 3 5.5a7 7 0 11-14 0c0-1.15.43-2.29 1-3a2.5 2.5 0 002.5 2.5z"/>',
    check: '<path d="M5 12l5 5L20 7"/>',
  };
  function ic(n) {
    return '<svg class="ic" viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="2" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">' + (P[n] || '') + '</svg>';
  }

  // Sample match: KIRMIZI 14 — 11 MAVİ, 04:37 remaining.
  const RED = [
    { no: 1, id: 12, name: 'Ayşe', hp: 87, k: 6, d: 2, bat: 78, ping: 24, state: 'HAZIR', st: 'good', s2: 'hazır', sel: true },
    { no: 2, id: 7, name: 'Mehmet', hp: 34, k: 3, d: 3, bat: 42, ping: 38, ctrl: ['ok', 'warn'], body: 'warn', state: 'DUVAR', st: 'bad', stIcon: 'warn', s2: 'hazır', alert: true, v: [['DUVAR', 2, 'bad'], ['ALAN DIŞI', 1, 'warn']] },
    { no: 3, id: 15, name: 'Can', hp: 0, k: 2, d: 3, bat: 61, ping: 31, state: 'ÖLÜ 4 sn', st: 'dead', stIcon: 'skull', s2: 'ölü (4 sn)', dead: true },
    { no: 4, id: 9, name: 'Kerem', hp: 100, k: 2, d: 2, bat: 91, ping: 27, state: 'BEKLİYOR', st: '', s2: 'bekliyor', uncal: true },
    { no: 5, id: 21, name: 'Ece', hp: 12, k: 1, d: 1, bat: 18, ping: 45, state: 'HAZIR', st: 'good', s2: 'hazır', v: [['ALAN DIŞI', 1, 'warn']] },
  ];
  const BLUE = [
    { no: 6, id: 3, name: 'Deniz', hp: 100, k: 4, d: 3, bat: 66, ping: 22, state: 'HAZIR', st: 'good', s2: 'hazır' },
    { no: 7, id: 18, name: 'Burak', hp: 0, k: 2, d: 4, bat: 53, ping: 29, state: 'TABANDA BEKLENİYOR', st: 'dead', s2: 'tabanda bekliyor', dead: true, v: [['DUVAR', 1, 'bad']] },
    { no: 8, id: 5, name: 'Elif', hp: 64, k: 3, d: 2, bat: 84, ping: 26, kat: 1, state: 'HAZIR', st: 'good', s2: 'hazır' },
    { no: 9, id: 11, name: 'Emre', hp: 58, k: 1, d: 3, bat: 37, ping: null, ctrl: ['bad', 'unk'], state: 'YENİDEN BAĞLANIYOR · 12 sn', st: '', s2: 'yeniden bağlanıyor (12 sn)', away: true },
    { no: 10, id: 14, name: 'Zeynep', hp: 45, k: 1, d: 2, bat: 29, ping: 52, state: 'HAZIR', st: 'good', s2: 'hazır' },
  ];
  // Newest first.
  const FEED = [
    { k: ['Deniz', 'blue'], w: 'SCAR-L', v: ['Can', 'red'] },
    { k: ['Ayşe', 'red'], w: 'AK-47', v: ['Burak', 'blue'] },
    { v: ['Zeynep', 'blue'], t: 'engelde kaldı' },
    { k: ['Elif', 'blue'], w: 'UMP-45', v: ['Kerem', 'red'] },
    { v: ['Emre', 'blue'], t: 'kendini havaya uçurdu' },
    { k: ['Ayşe', 'red'], w: 'AK-47', v: ['Emre', 'blue'] },
  ];

  const hpc = (hp) => (hp > 50 ? '' : hp > 20 ? ' warn' : ' bad');
  const batc = (b) => (b < 25 ? ' bad' : b < 50 ? ' warn' : '');
  const kd = (k, d) => (d === 0 ? k : k / d).toFixed(2);
  const ctrl = (p) => '<span class="ctrl">' + (p.ctrl || ['ok', 'ok']).map((c) => '<i class="' + c + '"></i>').join('') + '</span>';

  // Side-column player card. p.btn overrides button label/class per state.
  function pcard(p, team) {
    const other = team === 'red' ? ['MAVİ', 'to-blue'] : ['KIRMIZI', 'to-red'];
    const b = p.btn || {};
    const chip = p.uncal
      ? `<span class="chip warn">${ic('warn')}KALİBRESİZ</span>`
      : `<span class="chip ${p.st || 'plain'}">${p.stIcon ? ic(p.stIcon) : ''}${p.state}</span>`;
    const olc = b.olc || ['ÖLÇ', ''];
    const rst = b.rst || ['SIFIRLA', 't-bad'];
    const at = b.at || ['AT', 't-bad'];
    const olcOff = (p.uncal || p.away) && !b.olc ? ' disabled' : '';
    const cls = ['pcard', team, p.sel && 'sel', p.dead && 'dead', p.alert && 'alert', p.uncal && 'uncal', p.away && 'away'].filter(Boolean).join(' ');
    return `<article class="${cls}">
      <div class="num">${p.no}</div>
      <div class="body">
        <div class="pc-top"><span class="pc-name">${p.name}</span><span class="pc-id">#${p.id}</span>${chip}</div>
        <div class="pc-hp"><b class="${p.hp > 0 && p.hp <= 20 ? 'bad' : ''}">${p.hp}</b><div class="hp${hpc(p.hp)}"><i style="width:${p.hp}%"></i></div></div>
        <div class="pc-tele">
          <span class="tele"><em>K/D</em>${p.k}/${p.d}</span>
          <span class="tele${batc(p.bat)}">${ic('battery')}%${p.bat}</span>
          <span class="tele">${ctrl(p)}</span>
          <span class="tele ${p.body || ''}">${ic('body')}</span>
        </div>
        <div class="pc-act">
          <button class="btn sm${p.sel ? ' on' : ''}">POV</button>
          <button class="btn sm ${olc[1]}"${olcOff}>${olc[0]}</button>
          <button class="btn sm ${other[1]}">${other[0]}</button>
          <button class="btn sm ${rst[1]}"${rst[2] ? ` style="${rst[2]}"` : ''}>${rst[0]}</button>
          <button class="btn sm ${at[1]}">${at[0]}</button>
        </div>
      </div>
    </article>`;
  }

  // Kill feed plate: killer · weapon · victim, or victim + sentence.
  function kf(e, i) {
    const who = (x, dead) => `<span class="${x[1]}${dead ? ' dead' : ''}">${dead ? ic('skull') : ''}${x[0]}</span>`;
    const inner = e.k ? who(e.k) + `<span class="w">${e.w}</span>` + who(e.v, true) : who(e.v, true) + `<span class="t">${e.t}</span>`;
    return `<div class="kf${i === 0 ? ' new' : ''}">${inner}</div>`;
  }

  function fit() {
    const tam = document.body.classList.contains('tam');
    const s = (tam ? window.innerWidth : document.documentElement.clientWidth - 48) / 1920;
    document.querySelectorAll('.frame').forEach((f) => {
      const st = f.firstElementChild;
      const h = +(st.dataset.h || 1080);
      st.style.height = h + 'px';
      st.style.transform = 'scale(' + s + ')';
      f.style.width = 1920 * s + 'px';
      f.style.height = h * s + 'px';
    });
  }

  const here = location.pathname.split('/').pop() || 'admin-hud.html';
  const nav = document.createElement('nav');
  nav.className = 'vnav';
  nav.innerHTML = '<b>VORTEXARENA · ARAYÜZ</b>' +
    PAGES.map((p) => `<a href="${p[0]}"${p[0] === here ? ' class="on"' : ''}>${p[1]}</a>`).join('') +
    '<span class="vhint">T: çerçevesiz tam genişlik (F11 ile 1:1)</span>';
  document.body.prepend(nav);

  document.addEventListener('DOMContentLoaded', () => {
    document.querySelectorAll('i[data-ic]').forEach((e) => { e.outerHTML = ic(e.dataset.ic); });
    // "#2" in the URL shows only the second screen of the page.
    const only = parseInt(location.hash.slice(1), 10);
    if (only) {
      document.querySelectorAll('.frame').forEach((f, i) => { f.hidden = i !== only - 1; });
      document.querySelectorAll('.vcap').forEach((c, i) => { c.hidden = i !== only - 1; });
    }
    fit();
  });
  window.addEventListener('resize', fit);
  window.addEventListener('keydown', (e) => {
    if (e.key === 't' || e.key === 'T') { document.body.classList.toggle('tam'); fit(); }
  });

  window.VA = { ic, pcard, kf, kd, hpc, batc, ctrl, RED, BLUE, FEED };
})();
