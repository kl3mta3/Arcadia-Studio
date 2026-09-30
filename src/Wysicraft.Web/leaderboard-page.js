/* Arcadia Studio leaderboard page. Builds the board designed in Arcadia Studio (the JSON in
   #arcadia-studio-leaderboard) and fills it from the arcade's leaderboard with Arcadia.board. Opened anywhere else
   (a file, Preview), it shows sample data so the design can be seen. Everything a player typed (names) is written as
   text, never as HTML. */
(function () {
  'use strict';
  const page = JSON.parse(document.getElementById('arcadia-studio-leaderboard').textContent);
  const board = page.board, images = page.images || {}, fonts = page.fonts || {};
  const W = Math.max(1, board.size.width), H = Math.max(1, board.size.height);
  const TIER = { 1: '#F4C744', 2: '#C9D1D9', 3: '#D08A4E' };
  // The rank icons, the same outlines the editor draws (24 × 24).
  const ICONS = {
    crown: 'M3 8 L7.5 12 L12 5 L16.5 12 L21 8 L19 18 L5 18 Z M5 19.5 H19 V21 H5 Z',
    medal: 'M7 2 H10.5 L12 6 L13.5 2 H17 L14.2 8.4 A7 7 0 1 1 9.8 8.4 Z M12 11 A4.6 4.6 0 1 0 12.01 11 Z',
    trophy: 'M6 3 H18 V5 H21 V8 A4 4 0 0 1 17.4 12 A6 6 0 0 1 13 15.8 V18 H16 V21 H8 V18 H11 V15.8 A6 6 0 0 1 6.6 12 A4 4 0 0 1 3 8 V5 H6 Z M5 7 V8 A2 2 0 0 0 6.2 9.8 A10 10 0 0 1 6 7 Z M19 7 H18 A10 10 0 0 1 17.8 9.8 A2 2 0 0 0 19 8 Z',
    star: 'M12 2 L14.9 8.6 L22 9.3 L16.6 14 L18.2 21 L12 17.3 L5.8 21 L7.4 14 L2 9.3 L9.1 8.6 Z'
  };
  const PERIOD_NAMES = { all: 'All time', week: 'This week', day: 'Today' };

  // ---- Data: the arcade's board, or sample data outside the arcade ----
  const sdk = window.Arcadia && window.Arcadia.board ? window.Arcadia : null;
  const api = sdk ? {
    info: () => sdk.board.info(),
    top: (n, period) => sdk.board.top(n, { period }),
    range: (from, to, period) => sdk.board.range(from, to, { period }),
    around: (who, n, period) => sdk.board.around(who, n, { period }),
    aroundMe: (n, period) => sdk.board.aroundMe(n, { period }),
    format: (value, format) => (typeof sdk.format === 'function' ? sdk.format(value, format) : String(value))
  } : sample();
  function sample() {
    const first = ['Ada', 'Bex', 'Cato', 'Dune', 'Echo', 'Fenn', 'Gale', 'Hux', 'Iris', 'Jax', 'Kit', 'Lark', 'Moss', 'Nova', 'Orin', 'Pip', 'Quill', 'Rook', 'Sol', 'Tamsin'];
    const rows = [];
    for (let i = 0; i < 137; i++) {
      const name = i === 41 ? 'You' : first[i % first.length] + (i >= first.length ? ' ' + (Math.floor(i / first.length) + 1) : '');
      rows.push({ rank: i + 1, name, playerNo: 1000 + i * 7, score: Math.round(250000 / (1 + i * 0.35)), runs: 3 + (i * 7) % 40, at: Date.now() - i * 3600e3, stats: { level: Math.max(1, 30 - Math.floor(i / 5)) } });
    }
    const scale = { all: 1, week: 0.42, day: 0.11 };
    const on = period => rows.map(r => Object.assign({}, r, { score: Math.round(r.score * (scale[period] || 1)) }));
    const find = (list, who) => list.findIndex(r => r.name.toLowerCase() === String(who).toLowerCase() || String(r.playerNo) === String(who));
    const around = (list, i, n) => i < 0 ? [] : list.slice(Math.max(0, i - n), i + n + 1);
    return {
      sample: true,
      info: async () => ({ label: 'Score', format: 'points', aggregate: 'best', order: 'desc', stats: [{ key: 'level', label: 'Level', format: 'number' }], players: rows.length, title: board.title || 'Leaderboard' }),
      top: async (n, period) => on(period).slice(0, n),
      range: async (from, to, period) => on(period).slice(from - 1, to),
      around: async (who, n, period) => { const list = on(period); return around(list, find(list, who), n); },
      aroundMe: async (n, period) => { const list = on(period); return around(list, 41, n); },
      format: v => Number(v).toLocaleString('en-US')
    };
  }
  let info = { label: 'Score', format: 'points', stats: [], players: 0, title: board.title || '' };
  const tops = {};
  const topOf = period => (tops[period] = tops[period] || api.top(100, period).catch(() => []));
  const mine = {};
  const meOf = (n, period) => { const k = period + ':' + n; return (mine[k] = mine[k] || api.aroundMe(n, period).catch(() => null)); };
  let youName = null;

  // ---- Text: {tokens} in templates ----
  function statFormat(key) { const s = (info.stats || []).find(x => x.key === key); return s && s.format; }
  function when(at) { try { return new Date(at).toLocaleDateString(); } catch (e) { return ''; } }
  function fill(template, row, period) {
    return String(template || '').replace(/\{([a-zA-Z.]+)\}/g, (all, key) => {
      if (key === 'title') return info.title || board.title || '';
      if (key === 'label') return info.label || 'Score';
      if (key === 'players') return String(info.players || 0);
      if (key === 'period') return PERIOD_NAMES[period] || '';
      if (key === 'medal') return '';
      if (!row) return '';
      if (key === 'rank') return String(row.rank);
      if (key === 'name') return row.name || '';
      if (key === 'score') return api.format(row.score, info.format);
      if (key === 'runs') return String(row.runs == null ? '' : row.runs);
      if (key === 'playerNo') return String(row.playerNo == null ? '' : row.playerNo);
      if (key === 'when') return row.at ? when(row.at) : '';
      if (key.startsWith('stat.')) { const k = key.slice(5), v = row.stats ? row.stats[k] : undefined; return v == null ? '' : (statFormat(k) ? api.format(v, statFormat(k)) : String(v)); }
      return all;
    });
  }

  // ---- Building the page ----
  const stage = document.getElementById('stage');
  stage.style.width = W + 'px'; stage.style.height = H + 'px';
  function fit() {
    const s = Math.min(window.innerWidth / W, window.innerHeight / H);
    stage.style.transform = 'translate(' + Math.max(0, (window.innerWidth - W * s) / 2) + 'px,' + Math.max(0, (window.innerHeight - H * s) / 2) + 'px) scale(' + s + ')';
  }
  window.addEventListener('resize', fit); fit();
  if (board.pixelArt) document.body.classList.add('pixel');

  for (const [id, url] of Object.entries(fonts)) {
    try { const face = new FontFace(familyName(id), 'url("' + url + '")'); face.load().then(f => document.fonts.add(f), () => { }); } catch (e) { }
  }
  function familyName(id) { return 'lb_' + String(id).replace(/[^A-Za-z0-9_]/g, '_'); }
  const WEB_FONTS = {
    'web:sans': 'system-ui, "Segoe UI", Roboto, Arial, sans-serif', 'web:serif': 'Georgia, "Times New Roman", serif',
    'web:mono': '"Cascadia Mono", Consolas, monospace', 'web:rounded': '"Segoe UI Variable", "Nunito", "Trebuchet MS", sans-serif',
    'web:condensed': '"Arial Narrow", "Roboto Condensed", sans-serif', 'web:display': 'Impact, "Arial Black", sans-serif',
    'web:handwriting': '"Segoe Script", "Comic Sans MS", cursive', 'minecraft:alt': '"Courier New", monospace'
  };
  const fontOf = f => WEB_FONTS[f] || (fonts[f] ? familyName(f) + ', ' : '') + '"Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';

  function el(tag, cls, parent) { const d = document.createElement(tag); if (cls) d.className = cls; if (parent) parent.appendChild(d); return d; }
  function icon(kind, color, size) {
    const ns = 'http://www.w3.org/2000/svg', svg = document.createElementNS(ns, 'svg'), path = document.createElementNS(ns, 'path');
    svg.setAttribute('viewBox', '0 0 24 24'); svg.setAttribute('width', size); svg.setAttribute('height', size);
    path.setAttribute('d', ICONS[kind] || ICONS.crown); path.setAttribute('fill', color); path.setAttribute('fill-rule', 'evenodd');
    svg.appendChild(path); return svg;
  }
  // A control's own look: fill or picture, border, corners, text.
  function style(d, e) {
    const s = d.style;
    s.left = e.bounds.x + 'px'; s.top = e.bounds.y + 'px'; s.width = e.bounds.width + 'px'; s.height = e.bounds.height + 'px';
    s.opacity = e.opacity == null ? 1 : e.opacity;
    if (e.fillEnabled && e.background) s.background = css(e.background);
    if (e.texture && images[e.texture]) { s.backgroundImage = 'url("' + images[e.texture] + '")'; s.backgroundSize = '100% 100%'; }
    if (e.borderWidth > 0) s.border = e.borderWidth + 'px solid ' + css(e.borderColor);
    if (e.cornerRadius > 0) s.borderRadius = e.cornerRadius + 'px';
    s.color = css(e.foreground); s.fontFamily = fontOf(e.font); s.fontSize = (9 * (e.fontScale || 1)) + 'px';
    s.fontWeight = e.bold ? '700' : '400'; s.fontStyle = e.italic ? 'italic' : 'normal'; if (e.underline) s.textDecoration = 'underline';
    s.textAlign = e.alignment || 'left';
    if (e.textShadow) s.textShadow = (e.shadowOffsetX || 1) + 'px ' + (e.shadowOffsetY || 1) + 'px ' + (e.shadowBlur || 0) + 'px ' + css(e.shadowColor, e.shadowOpacity == null ? 0.75 : e.shadowOpacity);
  }
  const w = e => e.board || {};
  // Colours as the editor stores them: #RRGGBB, or #AARRGGBB with the opacity first (as the game runtime reads them).
  function css(value, opacity) {
    const v = String(value || '');
    if (!/^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/.test(v)) return v;
    const n = parseInt(v.slice(1), 16), a = v.length === 9 ? ((n >>> 24) & 255) / 255 : 1;
    return 'rgba(' + ((n >> 16) & 255) + ',' + ((n >> 8) & 255) + ',' + (n & 255) + ',' + (a * (opacity == null ? 1 : opacity)) + ')';
  }

  // A row of a list: the template's "|" cells become columns (the first and last hug their text, the middle ones share
  // the rest, the last is right-aligned); {medal} puts the rank's icon at the start.
  function row(parent, e, r, period, height, odd, template) {
    const b = w(e), line = el('div', 'row', parent);
    line.style.height = height + 'px';
    if (odd && b.altColor) line.style.background = css(b.altColor);
    if (r && youName && r.name === youName && b.highlightColor) { line.style.background = css(b.highlightColor); line.classList.add('you'); }
    const cells = (template || e.text || '{rank}|{name}|{score}').split('|');
    cells.forEach((cell, i) => {
      const c = el('span', 'cell', line);
      if (cells.length > 1 && i === cells.length - 1) c.classList.add('last');
      else if (cells.length > 2 && i === 0) c.classList.add('first');
      else c.classList.add('grow');
      if (r && cell.includes('{medal}') && r.rank <= 3) c.appendChild(icon(b.icon || 'medal', TIER[r.rank], Math.round(height * 0.7)));
      c.appendChild(document.createTextNode(fill(cell.replace('{medal}', ''), r, period)));
    });
    return line;
  }
  function empty(parent, e) { const m = el('div', 'empty', parent); m.textContent = w(e).empty || 'No scores yet.'; }

  let period = 'all';
  const widgets = [];
  const periodTabs = [];

  for (const e of board.elements) {
    if (e.visible === false) continue;
    const d = el('div', 'el ' + e.type, stage); style(d, e);
    const b = w(e);
    switch (e.type) {
      case 'label': { const t = el('div', 'text', d); widgets.push(() => { t.textContent = fill(e.text, null, period); }); break; }
      case 'image': case 'panel': break;
      case 'shape': d.classList.add('shape-' + (e.shape || 'rectangle')); break;
      case 'lb_icon': {
        if (!e.texture || !images[e.texture]) d.appendChild(icon(b.icon || 'crown', TIER[b.rank] || TIER[1], '100%'));
        d.style.background = e.texture && images[e.texture] ? d.style.background : 'none';
        break;
      }
      case 'lb_periods': {
        const list = (b.periods || 'all,week,day').split(',').map(x => x.trim()).filter(x => PERIOD_NAMES[x]);
        const labels = (b.periodLabels || '').split('|');
        if (list.length) period = list[0];
        list.forEach((p, i) => {
          const tab = el('button', 'tab', d); tab.type = 'button'; tab.textContent = (labels[['all', 'week', 'day'].indexOf(p)] || PERIOD_NAMES[p]).trim();
          tab.style.color = css(e.foreground); tab.style.font = 'inherit';
          tab.addEventListener('click', () => { period = p; refresh(); });
          periodTabs.push({ tab, p, e });
        });
        break;
      }
      case 'lb_table': case 'lb_me': case 'lb_range': {
        let header = null;
        if (b.header) { header = el('div', 'header', d); header.style.height = (b.rowHeight || 16) + 'px'; }
        let controls = null, from = null, to = null, find = null;
        if (e.type === 'lb_range') {
          controls = el('div', 'controls', d);
          const add = (label, input) => { const l = el('label', '', controls); l.textContent = label; l.appendChild(input); return input; };
          from = add('From ', Object.assign(document.createElement('input'), { type: 'number', min: 1, value: Math.max(1, Math.round(e.minimum || 1)) }));
          to = add(' to ', Object.assign(document.createElement('input'), { type: 'number', min: 1, value: Math.max(1, Math.round(e.maximum || 20)) }));
          if (b.search) find = add(' Player ', Object.assign(document.createElement('input'), { type: 'text', placeholder: 'Name or number' }));
          for (const input of [from, to, find]) if (input) input.addEventListener('change', () => draw());
        }
        const list = el('div', 'list', d);
        const draw = async () => {
          const rh = b.rowHeight || 16;
          if (header) { header.textContent = ''; row(header, e, null, period, rh, false, b.header); }
          let rows = [];
          if (e.type === 'lb_table') rows = (await topOf(period)).slice(0, Math.max(1, Math.min(100, b.count || 10)));
          else if (e.type === 'lb_me') rows = (await meOf(Math.max(0, Math.min(10, b.around == null ? 2 : b.around)), period)) || [];
          else {
            const who = find && find.value.trim();
            if (who) rows = (await api.around(who, 5, period).catch(() => null)) || [];
            else { let a = Math.max(1, parseInt(from.value, 10) || 1), z = Math.max(a, parseInt(to.value, 10) || a); z = Math.min(z, a + 99); rows = (await api.range(a, z, period).catch(() => [])) || []; }
          }
          list.textContent = '';
          if (!rows.length) { empty(list, e); return; }
          rows.forEach((r, i) => row(list, e, r, period, rh, i % 2 === 1));
        };
        widgets.push(draw);
        break;
      }
      case 'lb_podium': {
        const cols = [2, 1, 3].map(rank => {
          const col = el('div', 'step', d), who = el('div', 'who', col), block = el('div', 'block', col);
          block.style.height = ({ 1: 62, 2: 46, 3: 34 })[rank] + '%';
          block.style.background = TIER[rank];
          const n = el('div', 'n', block); n.textContent = String(rank);
          return { rank, who };
        });
        widgets.push(async () => {
          const top = await topOf(period);
          for (const c of cols) {
            const r = top[c.rank - 1]; c.who.textContent = '';
            if (!r) continue;
            (e.text || '{name}\n{score}').split('\\n').join('\n').split('\n').forEach(line => { const l = el('div', '', c.who); l.textContent = fill(line, r, period); });
          }
        });
        break;
      }
      case 'lb_rank': {
        const box = el('div', 'text', d);
        widgets.push(async () => {
          const r = (await topOf(period))[Math.max(1, b.rank || 1) - 1];
          box.textContent = '';
          if (!r) { box.textContent = b.empty || 'No scores yet.'; return; }
          (e.text || '#{rank} {name}\n{score}').split('\\n').join('\n').split('\n').forEach(line => { const l = el('div', '', box); l.textContent = fill(line, r, period); });
        });
        break;
      }
      case 'lb_slider': {
        const track = el('div', 'track', d);
        const per = Math.max(1, Math.min(5, b.visible || 3));
        let timer = 0;
        widgets.push(async () => {
          const rows = (await topOf(period)).slice(0, Math.max(1, Math.min(100, b.count || 5)));
          track.textContent = '';
          if (!rows.length) { empty(track, e); return; }
          for (const r of rows) {
            const card = el('div', 'card', track); card.style.width = (100 / per) + '%';
            if (r.rank <= 3) card.appendChild(icon(b.icon || 'medal', TIER[r.rank], Math.round(9 * (e.fontScale || 1) * 1.6)));
            (e.text || '#{rank}\n{name}\n{score}').split('\\n').join('\n').split('\n').forEach(line => { const l = el('div', '', card); l.textContent = fill(line, r, period); });
          }
          clearInterval(timer);
          if ((b.seconds || 0) > 0 && rows.length > per)
            timer = setInterval(() => {
              const step = track.clientWidth / per, end = track.scrollWidth - track.clientWidth - 1;
              track.scrollTo({ left: track.scrollLeft >= end ? 0 : track.scrollLeft + step, behavior: 'smooth' });
            }, b.seconds * 1000);
        });
        break;
      }
    }
  }

  function refresh() {
    for (const t of periodTabs) t.tab.classList.toggle('on', t.p === period);
    for (const draw of widgets) Promise.resolve().then(draw).catch(() => { });
  }
  if (api.sample) { const tag = el('div', 'sample', document.body); tag.textContent = 'PREVIEW · SAMPLE DATA'; }
  Promise.resolve(api.info()).then(i => { if (i) info = i; }).catch(() => { }).then(async () => {
    try { const me = await meOf(0, period); youName = me && me.length ? me[0].name : null; } catch (e) { }
    refresh();
  });
})();
