/* Wysicraft web runtime: plays Wysicraft screens in a browser, WebView2 or Electron.
 * It follows the Minecraft runtime (DynamicScreen/ElementRenderers/ServerRuntime) so exported apps look and
 * behave like the game: same layout, conditions, actions, client script API, Tick/Key events and animations.
 * There is no Minecraft here: server-side commands and functions go to the host hooks in host.js. */
(function () {
  'use strict';
  const DEFAULT_ELEMENT = {
    rowTemplate: '', rowAction: '', rowElements: [], horizontalAnchor: 'left', verticalAnchor: 'top', minWidth: 1, minHeight: 1,
    rowTemplateWidth: 0, rowHeight: 30, primaryLabel: '', secondaryLabel: '', showItemId: true, id: '', name: '', type: 'button',
    text: '', tooltip: '', foreground: '#FFFFFF', background: '#40464F', alignment: 'left', texture: '', item: 'minecraft:stone',
    value: '0', visibleIf: '', enabledIf: '', parent: '', bounds: { x: 0, y: 0, width: 100, height: 20 }, visible: true, enabled: true,
    opacity: 1, fontScale: 1, minimum: 0, maximum: 100, font: 'minecraft:default', bold: false, italic: false, underline: false,
    textShadow: false, fillEnabled: true, borderColor: '#697382', shadowColor: '#000000', borderWidth: 0, shadowOpacity: 0.75,
    shadowOffsetX: 1, shadowOffsetY: 1, shadowBlur: 0, cornerRadius: 0, textureX: 0, textureY: 0, textureWidth: 256, textureHeight: 256,
    options: [], events: {}, sound: '', autoplay: true, delay: 0, volume: 1, repeat: 1, loop: false, frameWidth: 16, frameHeight: 16, clips: '', shape: 'rectangle', body: '', collider: 'box', colliderPoints: [], bounce: 0, friction: 0.2, trigger: false, input: '', tileWidth: 16, tileHeight: 16, columns: 20, rows: 12, tiles: '', solid: ''
  };
  const DEFAULT_UI = { tickInterval: 0, keyRepeat: 150, responsive: false, showFrame: false, dimBackground: false, fitToScreen: true, clipToScreen: false,
    id: '', title: '', size: { width: 320, height: 200 }, variables: {}, events: {}, elements: [], gravity: 0, animations: [], stateGraphs: [], shader: '' };

  // ---- WebGL2 batch layer ----
  // Canvas2D draws one thing at a time, which is fine for controls and hopeless for twenty thousand particles. This
  // draws every quad sharing a texture in one instanced call, into an offscreen canvas that is then composited into
  // the 2D canvas at the point in the draw order where it belongs — so nothing changes places, and canvas2D stays
  // the fallback wherever WebGL2 is missing.
  const GL_VERTEX = '#version 300 es\n' + [
    'layout(location=0) in vec2 a_corner;',   // the unit quad, 0..1
    'layout(location=1) in vec4 a_dst;',      // x, y, width, height in device pixels
    'layout(location=2) in vec4 a_uv;',       // u0, v0, u1, v1
    'layout(location=3) in vec4 a_color;',    // tint and alpha
    'layout(location=4) in float a_rot;',     // radians, around the quad centre
    'uniform vec2 u_screen;',
    'out vec2 v_uv; out vec4 v_color;',
    'void main() {',
    '  vec2 local = (a_corner - 0.5) * a_dst.zw;',
    '  float s = sin(a_rot), c = cos(a_rot);',
    '  vec2 turned = a_rot == 0.0 ? local : vec2(local.x * c - local.y * s, local.x * s + local.y * c);',
    '  vec2 at = a_dst.xy + a_dst.zw * 0.5 + turned;',
    '  vec2 clip = at / u_screen * 2.0 - 1.0;',
    '  gl_Position = vec4(clip.x, -clip.y, 0.0, 1.0);',
    '  v_uv = mix(a_uv.xy, a_uv.zw, a_corner);',
    '  v_color = a_color;',
    '}'
  ].join('\n');
  const GL_FRAGMENT = '#version 300 es\n' + [
    'precision mediump float;',
    'in vec2 v_uv; in vec4 v_color;',
    'uniform sampler2D u_texture;',
    'out vec4 outColor;',
    'void main() {',
    '  vec4 t = texture(u_texture, v_uv);',
    '  if (t.a <= 0.0) discard;',
    '  outColor = vec4(t.rgb * v_color.rgb, t.a * v_color.a);',
    '}'
  ].join('\n');
  // The post-process pass: the batched scene as a texture, through the project's own fragment shader.
  const GL_POST_VERTEX = '#version 300 es\n' + [
    'layout(location=0) in vec2 a_corner;',
    'out vec2 v_uv;',
    'void main() { v_uv = vec2(a_corner.x, 1.0 - a_corner.y); gl_Position = vec4(a_corner * 2.0 - 1.0, 0.0, 1.0); }'
  ].join('\n');
  const GL_FLOATS = 13;              // dst 4, uv 4, colour 4, rotation 1
  const GL_MAX_QUADS = 65536;

  class GLLayer {
    constructor() {
      this.ok = false; this.total = 0;
      try {
        this.canvas = document.createElement('canvas');
        this.gl = this.canvas.getContext('webgl2', { alpha: true, premultipliedAlpha: false, antialias: false, depth: false, stencil: false });
        if (!this.gl) return;
        this.build();
        this.ok = true;
        // A lost context drops the whole layer back to canvas2D rather than drawing nothing at all.
        this.canvas.addEventListener('webglcontextlost', ev => { ev.preventDefault(); this.ok = false; });
      } catch (ex) { this.ok = false; }
    }
    program(vertex, fragment) {
      const gl = this.gl;
      const compile = (type, source) => {
        const shader = gl.createShader(type); gl.shaderSource(shader, source); gl.compileShader(shader);
        if (!gl.getShaderParameter(shader, gl.COMPILE_STATUS)) { const log = gl.getShaderInfoLog(shader); gl.deleteShader(shader); throw new Error(log || 'shader did not compile'); }
        return shader;
      };
      const v = compile(gl.VERTEX_SHADER, vertex), f = compile(gl.FRAGMENT_SHADER, fragment);
      const p = gl.createProgram(); gl.attachShader(p, v); gl.attachShader(p, f); gl.linkProgram(p);
      gl.deleteShader(v); gl.deleteShader(f);
      if (!gl.getProgramParameter(p, gl.LINK_STATUS)) { const log = gl.getProgramInfoLog(p); gl.deleteProgram(p); throw new Error(log || 'shader did not link'); }
      return p;
    }
    build() {
      const gl = this.gl;
      this.batchProgram = this.program(GL_VERTEX, GL_FRAGMENT);
      this.screenAt = gl.getUniformLocation(this.batchProgram, 'u_screen');
      this.corners = gl.createBuffer();
      gl.bindBuffer(gl.ARRAY_BUFFER, this.corners);
      gl.bufferData(gl.ARRAY_BUFFER, new Float32Array([0, 0, 1, 0, 0, 1, 1, 1]), gl.STATIC_DRAW);
      this.instances = gl.createBuffer(); this.capacity = 0;
      this.vao = gl.createVertexArray();
      gl.bindVertexArray(this.vao);
      gl.bindBuffer(gl.ARRAY_BUFFER, this.corners);
      gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
      gl.bindBuffer(gl.ARRAY_BUFFER, this.instances);
      const stride = GL_FLOATS * 4;
      const attr = (index, size, offset) => { gl.enableVertexAttribArray(index); gl.vertexAttribPointer(index, size, gl.FLOAT, false, stride, offset * 4); gl.vertexAttribDivisor(index, 1); };
      attr(1, 4, 0); attr(2, 4, 4); attr(3, 4, 8); attr(4, 1, 12);
      gl.bindVertexArray(null);
      this.refused = new WeakSet();
      // One white pixel, so a quad with no texture takes the same path as a textured one.
      this.white = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, this.white);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, 1, 1, 0, gl.RGBA, gl.UNSIGNED_BYTE, new Uint8Array([255, 255, 255, 255]));
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      this.textures = new Map();      // image element -> GL texture
      // Quads in the order they were pushed, as runs that share a texture and blend: a run is one draw call.
      // Keeping submission order is what keeps the draw order; grouping by texture instead once let a floor
      // picture first seen on floor 2 be painted over every sprite already known.
      this.data = new Float32Array(GL_FLOATS * 1024);
      this.runs = [];
      this.values = new Map();        // uniform name -> number, from scripts
      this.postProgram = null; this.postSource = ''; this.postUniforms = new Map(); this.postVao = null;
      this.frame = null; this.frameTexture = null; this.shaderError = '';
    }
    resize(width, height) {
      width = Math.max(1, Math.round(width)); height = Math.max(1, Math.round(height));
      if (this.canvas.width === width && this.canvas.height === height) return;
      this.canvas.width = width; this.canvas.height = height;
      if (this.frame) { this.gl.deleteFramebuffer(this.frame); this.gl.deleteTexture(this.frameTexture); this.frame = null; this.frameTexture = null; }
    }
    // An image becomes a texture once and is kept. Nearest neighbour, like the rest of the renderer.
    // Whether this picture can go through the batch: uploads it on first sight, so the answer is known before drawing.
    usable(img) { if (!img) return true; if (this.refused.has(img)) return false; this.textureFor(img); return !this.refused.has(img); }
    textureFor(img) {
      if (!img) return this.white;
      let tex = this.textures.get(img);
      if (tex) return tex;
      const gl = this.gl;
      tex = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, tex);
      // A picture the browser will not hand to WebGL (a page opened from disk, or one from another site without
      // CORS) is remembered, so the control using it is drawn on canvas2D instead of as a white quad.
      try { gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, gl.RGBA, gl.UNSIGNED_BYTE, img); }
      catch (ex) { gl.deleteTexture(tex); this.refused.add(img); return this.white; }
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.NEAREST); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.NEAREST);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      this.textures.set(img, tex);
      return tex;
    }
    begin() {
      this.runs.length = 0;
      this.total = 0;
    }
    // One quad. Source coordinates are the image's own pixels; destination coordinates are device pixels.
    quad(img, sx, sy, sw, sh, x, y, w, h, r, g, b, a, rot, additive) {
      if (this.total >= GL_MAX_QUADS) return;
      const tex = this.textureFor(img); additive = !!additive;
      let run = this.runs[this.runs.length - 1];
      if (!run || run.tex !== tex || run.additive !== additive) { run = { tex, additive, start: this.total, count: 0 }; this.runs.push(run); }
      if ((this.total + 1) * GL_FLOATS > this.data.length) {
        const grown = new Float32Array(Math.min(GL_FLOATS * GL_MAX_QUADS, this.data.length * 2));
        grown.set(this.data); this.data = grown;
      }
      const iw = img ? (img.naturalWidth || img.width || 1) : 1, ih = img ? (img.naturalHeight || img.height || 1) : 1;
      const d = this.data, at = this.total * GL_FLOATS;
      d[at] = x; d[at + 1] = y; d[at + 2] = w; d[at + 3] = h;
      d[at + 4] = sx / iw; d[at + 5] = sy / ih; d[at + 6] = (sx + sw) / iw; d[at + 7] = (sy + sh) / ih;
      d[at + 8] = r; d[at + 9] = g; d[at + 10] = b; d[at + 11] = a;
      d[at + 12] = rot || 0;
      run.count++; this.total++;
    }
    // Draws everything pushed since begin(), in the order it was pushed: one call per run of quads that share a
    // texture and blend, then the project's fragment shader over the result if it named one.
    flush(shaderSource) {
      const gl = this.gl;
      if ((shaderSource || '') !== this.postSource) this.setShader(shaderSource || '');
      const post = this.postProgram;
      if (post && !this.frame) this.makeFrame();
      gl.bindFramebuffer(gl.FRAMEBUFFER, post ? this.frame : null);
      gl.viewport(0, 0, this.canvas.width, this.canvas.height);
      gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
      gl.enable(gl.BLEND);
      gl.useProgram(this.batchProgram);
      gl.uniform2f(this.screenAt, this.canvas.width, this.canvas.height);
      gl.bindVertexArray(this.vao);
      gl.activeTexture(gl.TEXTURE0);
      gl.bindBuffer(gl.ARRAY_BUFFER, this.instances);
      let blending = null;
      for (const run of this.runs) {
        if (!run.count) continue;
        if (run.additive !== blending) {
          blending = run.additive;
          if (blending) gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE, gl.ONE, gl.ONE);
          else gl.blendFuncSeparate(gl.SRC_ALPHA, gl.ONE_MINUS_SRC_ALPHA, gl.ONE, gl.ONE_MINUS_SRC_ALPHA);
        }
        gl.bindTexture(gl.TEXTURE_2D, run.tex);
        const bytes = run.count * GL_FLOATS * 4;
        if (bytes > this.capacity) { this.capacity = bytes * 2; gl.bufferData(gl.ARRAY_BUFFER, this.capacity, gl.DYNAMIC_DRAW); }
        gl.bufferSubData(gl.ARRAY_BUFFER, 0, this.data, run.start * GL_FLOATS, run.count * GL_FLOATS);
        gl.drawArraysInstanced(gl.TRIANGLE_STRIP, 0, 4, run.count);
      }
      gl.bindVertexArray(null);
      if (post) this.drawPost();
    }
    makeFrame() {
      const gl = this.gl;
      this.frameTexture = gl.createTexture();
      gl.bindTexture(gl.TEXTURE_2D, this.frameTexture);
      gl.texImage2D(gl.TEXTURE_2D, 0, gl.RGBA, this.canvas.width, this.canvas.height, 0, gl.RGBA, gl.UNSIGNED_BYTE, null);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MIN_FILTER, gl.LINEAR); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_MAG_FILTER, gl.LINEAR);
      gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_S, gl.CLAMP_TO_EDGE); gl.texParameteri(gl.TEXTURE_2D, gl.TEXTURE_WRAP_T, gl.CLAMP_TO_EDGE);
      this.frame = gl.createFramebuffer();
      gl.bindFramebuffer(gl.FRAMEBUFFER, this.frame);
      gl.framebufferTexture2D(gl.FRAMEBUFFER, gl.COLOR_ATTACHMENT0, gl.TEXTURE_2D, this.frameTexture, 0);
    }
    drawPost() {
      const gl = this.gl;
      gl.bindFramebuffer(gl.FRAMEBUFFER, null);
      gl.viewport(0, 0, this.canvas.width, this.canvas.height);
      gl.clearColor(0, 0, 0, 0); gl.clear(gl.COLOR_BUFFER_BIT);
      gl.disable(gl.BLEND);
      gl.useProgram(this.postProgram);
      gl.activeTexture(gl.TEXTURE0); gl.bindTexture(gl.TEXTURE_2D, this.frameTexture);
      const set = (name, fn) => { const at = this.postUniforms.get(name); if (at) fn(at); };
      set('u_scene', at => gl.uniform1i(at, 0));
      set('u_resolution', at => gl.uniform2f(at, this.canvas.width, this.canvas.height));
      set('u_time', at => gl.uniform1f(at, performance.now() / 1000));
      for (const [name, value] of this.values) set(name, at => gl.uniform1f(at, value));
      gl.bindVertexArray(this.postVao);
      gl.drawArrays(gl.TRIANGLE_STRIP, 0, 4);
      gl.bindVertexArray(null);
      gl.enable(gl.BLEND);
    }
    // Swaps in the project's fragment shader. One that won't compile is reported once and dropped, so a typo costs
    // the effect rather than the screen.
    setShader(source) {
      const gl = this.gl;
      this.postSource = source;
      if (this.postProgram) { gl.deleteProgram(this.postProgram); this.postProgram = null; }
      this.postUniforms.clear(); this.shaderError = '';
      if (!source) return;
      try { this.postProgram = this.program(GL_POST_VERTEX, source); }
      catch (ex) { this.shaderError = String(ex.message || ex); return; }
      if (!this.postVao) {
        this.postVao = gl.createVertexArray();
        gl.bindVertexArray(this.postVao);
        gl.bindBuffer(gl.ARRAY_BUFFER, this.corners);
        gl.enableVertexAttribArray(0); gl.vertexAttribPointer(0, 2, gl.FLOAT, false, 0, 0);
        gl.bindVertexArray(null);
      }
      const count = gl.getProgramParameter(this.postProgram, gl.ACTIVE_UNIFORMS);
      for (let i = 0; i < count; i++) {
        const info = gl.getActiveUniform(this.postProgram, i);
        this.postUniforms.set(info.name, gl.getUniformLocation(this.postProgram, info.name));
      }
    }
  }
  // A soft round dot, for particles drawn as circles: one texture instead of a path per particle.
  function dotTexture() {
    const c = document.createElement('canvas'); c.width = c.height = 32;
    const g = c.getContext('2d');
    g.fillStyle = '#FFFFFF'; g.beginPath(); g.arc(16, 16, 15.5, 0, Math.PI * 2); g.fill();
    return c;
  }

  // Typefaces. Minecraft draws its own and has only three; a web or desktop app is an ordinary canvas, so it can use
  // the families every browser already has, or a font file carried in the project. Core/Fonts.cs lists the same ones.
  const WEB_FONTS = {
    'web:sans': 'system-ui, "Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif',
    'web:serif': 'Georgia, "Times New Roman", serif',
    'web:mono': '"Cascadia Mono", Consolas, "DejaVu Sans Mono", monospace',
    'web:rounded': '"Segoe UI Variable", "Nunito", "Trebuchet MS", system-ui, sans-serif',
    'web:condensed': '"Arial Narrow", "Roboto Condensed", "Segoe UI", sans-serif',
    'web:display': 'Impact, "Haettenschweiler", "Arial Black", sans-serif',
    'web:handwriting': '"Segoe Script", "Comic Sans MS", cursive'
  };
  // The CSS family name a project font is registered under; the same rule as Fonts.FamilyName.
  function fontFamilyName(id) { return 'wys_' + String(id).replace(/[:/.]/g, '_').replace(/[^A-Za-z0-9_]/g, ''); }
  const clone = v => JSON.parse(JSON.stringify(v));
  // One-shot sounds larger than this (about half a minute of Ogg) play from a small pool of audio elements, which
  // stream, instead of being decoded whole into memory: a minute of stereo sound is about 21 MB once decoded.
  const SOUND_DECODE_MAX = 512 * 1024;
  // Tilemap data. "-1*240 5 5 5 12*3" is a screen of nothing, three of tile 5 then three of 12: a map is mostly
  // repetition, so a 100x100 grid is a few hundred bytes instead of 30 KB. Core/Tilemaps.cs reads and writes the same.
  const TILE_EMPTY = -1;
  function readTiles(tiles, columns, rows) {
    const grid = new Int32Array(Math.max(0, columns * rows)).fill(TILE_EMPTY);
    let at = 0;
    for (const run of String(tiles || '').split(' ')) {
      if (!run) continue;
      const star = run.indexOf('*'), index = parseInt(star < 0 ? run : run.slice(0, star), 10);
      let count = star < 0 ? 1 : parseInt(run.slice(star + 1), 10);
      if (!Number.isFinite(index) || !Number.isFinite(count) || count < 1) continue; // Validate already rejected this
      for (let i = 0; i < count && at < grid.length; i++) grid[at++] = index;
    }
    return grid;
  }
  function writeTiles(grid) {
    let last = grid.length - 1;
    while (last >= 0 && grid[last] === TILE_EMPTY) last--;
    const parts = [];
    for (let i = 0; i <= last;) {
      const value = grid[i]; let run = 1;
      while (i + run <= last && grid[i + run] === value) run++;
      parts.push(run > 1 ? value + '*' + run : String(value));
      i += run;
    }
    return parts.join(' ');
  }
  // Which tile indices stop a body, from "1,3,5-9".
  function solidTiles(solid) {
    const set = new Set();
    for (const part of String(solid || '').split(',')) {
      const span = part.trim(); if (!span) continue;
      const dash = span.indexOf('-', 1);
      if (dash < 0) { const one = parseInt(span, 10); if (Number.isFinite(one)) set.add(one); continue; }
      let from = parseInt(span.slice(0, dash), 10), to = parseInt(span.slice(dash + 1), 10);
      if (!Number.isFinite(from) || !Number.isFinite(to)) continue;
      if (to < from) { const t = from; from = to; to = t; }
      for (let i = from; i <= to && i - from < 65536; i++) set.add(i);
    }
    return set;
  }
  function normalizeHandler(h) { h = Object.assign({ permissionLevel: 0, cooldownTicks: 4, actions: [], script: '', function: '' }, h || {}); h.actions = (h.actions || []).map(a => Object.assign({ type: '', target: '', value: '' }, a)); return h; }
  function normalizeEvents(events) { const out = {}; for (const [k, v] of Object.entries(events || {})) out[k] = { client: normalizeHandler(v && v.client), server: normalizeHandler(v && v.server) }; return out; }
  function normalizeElement(e) {
    const n = Object.assign(clone(DEFAULT_ELEMENT), e || {});
    n.bounds = Object.assign({ x: 0, y: 0, width: 100, height: 20 }, n.bounds || {});
    n.events = normalizeEvents(n.events); n.rowElements = (n.rowElements || []).map(normalizeElement); n.options = n.options || [];
    return n;
  }
  function normalizeUi(ui) {
    const n = Object.assign(clone(DEFAULT_UI), ui || {});
    n.size = Object.assign({ width: 320, height: 200 }, n.size || {}); n.variables = Object.assign({}, n.variables || {});
    n.events = normalizeEvents(n.events); n.elements = (n.elements || []).map(normalizeElement);
    return n;
  }

  // ---- Expressions (model/Expressions.java) ----
  function bind(text, state) { return String(text).replace(/\$\{([a-zA-Z_][a-zA-Z0-9_]*)\}/g, (_, name) => state[name] ?? ''); }
  function evaluate(text, state) {
    if (!text || !text.trim()) return true;
    if (text.length > 1024) throw new Error('Condition too long');
    const tokens = []; const re = /\s*(>=|<=|==|!=|&&|\|\||[()!<>]|"[^"]*"|'[^']*'|-?\d+(?:\.\d+)?|[a-zA-Z_][a-zA-Z0-9_]*)/y;
    let offset = 0;
    while (offset < text.length) { if (!text.slice(offset).trim()) break; re.lastIndex = offset; const m = re.exec(text); if (!m) throw new Error('Invalid condition'); tokens.push(m[1]); offset = re.lastIndex; }
    let pos = 0; const peek = () => pos < tokens.length ? tokens[pos] : '';
    const eat = (...c) => { if (c.includes(peek())) { pos++; return true; } return false; };
    const num = s => (/^\s*-?\d+(\.\d+)?([eE][-+]?\d+)?\s*$/.test(s) ? Number(s) : NaN);
    function value() { const t = peek(); if (!t || t === ')' || t === '(') throw new Error('Expected value'); pos++; if (t[0] === '"' || t[0] === "'") return t.slice(1, -1); return t in state ? state[t] : t; }
    function unary() {
      if (eat('NOT', '!')) return !unary();
      if (eat('(')) { const v = or(); if (!eat(')')) throw new Error('Missing )'); return v; }
      const left = String(value()), op = peek();
      if (!eat('==', '!=', '>', '<', '>=', '<=')) { const n = num(left); return left.toLowerCase() === 'true' || (!isNaN(n) && n !== 0); }
      const right = String(value()); const a = num(left), b = num(right);
      const c = !isNaN(a) && !isNaN(b) ? (a < b ? -1 : a > b ? 1 : 0) : (left < right ? -1 : left > right ? 1 : 0);
      return op === '==' ? c === 0 : op === '!=' ? c !== 0 : op === '>' ? c > 0 : op === '<' ? c < 0 : op === '>=' ? c >= 0 : c <= 0;
    }
    function and() { let v = unary(); while (eat('AND', '&&')) { const r = unary(); v = v && r; } return v; }
    function or() { let v = and(); while (eat('OR', '||')) { const r = and(); v = v || r; } return v; }
    const result = or(); if (peek()) throw new Error('Unexpected token'); return result;
  }
  const INPUT_CONDITION = /\binput:([a-zA-Z_][a-zA-Z0-9_]*)/g;
  function conditionScope(app) {
    const scope = Object.assign({}, app.state);
    for (const name of app.inputsDown || []) scope['input_' + name] = 'true';
    return scope;
  }
  function safeEvaluate(text, state) { try { return evaluate(text, state); } catch (ex) { return false; } }

  // ---- Key names (model/KeyNames.java) ----
  function keyName(ev) {
    const k = ev.key;
    if (/^[a-zA-Z]$/.test(k)) return k.toLowerCase();
    if (/^[0-9]$/.test(k)) return k;
    const map = { ' ': 'space', Enter: 'enter', Tab: 'tab', Backspace: 'backspace', ArrowLeft: 'left', ArrowRight: 'right', ArrowUp: 'up', ArrowDown: 'down', Escape: 'escape' };
    if (map[k]) return map[k];
    if (/^F([1-9]|1[0-2])$/.test(k)) return k.toLowerCase();
    return null;
  }

  // ---- Animated textures (model/TextureAnimation.java) ----
  function parseAnimation(mcmeta, width, height) {
    if (!mcmeta || width < 1 || height < 1) return null;
    let root; try { root = JSON.parse(mcmeta); } catch (ex) { return null; }
    const a = root && root.animation; if (!a || typeof a !== 'object') return null;
    let fw = Number.isInteger(a.width) ? a.width : -1, fh = Number.isInteger(a.height) ? a.height : -1;
    if (fw < 1 && fh < 1) fw = fh = Math.min(width, height); else if (fw < 1) fw = fh; else if (fh < 1) fh = fw;
    if (fw > width || fh > height) return null;
    const columns = Math.floor(width / fw), rows = Math.floor(height / fh), count = columns * rows, frametime = Math.max(1, a.frametime | 0 || 1);
    const frames = [];
    if (Array.isArray(a.frames)) for (const f of a.frames) { const index = typeof f === 'object' ? f.index : f; const ticks = typeof f === 'object' && f.time ? Math.max(1, f.time) : frametime; if (index >= 0 && index < count) frames.push({ index, ticks }); if (frames.length >= 1024) break; }
    else for (let i = 0; i < Math.min(count, 1024); i++) frames.push({ index: i, ticks: frametime });
    if (!frames.length) return null;
    const total = frames.reduce((s, f) => s + f.ticks, 0);
    return { fw, fh, columns, rows, frameAt(ms) { let tick = Math.floor(ms / 50) % total; for (const f of frames) { if (tick < f.ticks) return f.index; tick -= f.ticks; } return frames[frames.length - 1].index; } };
  }

  // ---- Colors (ElementRenderers.color) ----
  function color(value, opacity) {
    const v = String(value || '');
    if (!/^#([0-9a-fA-F]{6}|[0-9a-fA-F]{8})$/.test(v)) return `rgba(255,0,255,${opacity})`;
    const n = parseInt(v.slice(1), 16), alpha = v.length === 9 ? ((n >>> 24) & 255) / 255 : 1;
    return `rgba(${(n >> 16) & 255},${(n >> 8) & 255},${n & 255},${alpha * opacity})`;
  }

  // ---- Layout (model/ContainerTree.java, model/ResponsiveLayout.java) ----
  // An ID -> control index, rebuilt when the list of controls changes (spawned objects come and go), so a lookup
  // doesn't scan every control. The first control with an ID wins, as a scan would.
  function elementOf(ui, id) {
    let index = ui._index;
    if (!index || ui._indexCount !== ui.elements.length) {
      index = new Map(); for (const e of ui.elements) if (!index.has(e.id)) index.set(e.id, e);
      ui._index = index; ui._indexCount = ui.elements.length;
    }
    return index.get(id) || null;
  }
  function ancestors(ui, child) {
    const result = [], seen = new Set([child.id]); let id = child.parent;
    while (id) { if (seen.has(id) || seen.size > 33) break; seen.add(id); const p = elementOf(ui, id); if (!p || (p.type !== 'panel' && p.type !== 'scroll_panel' && p.type !== 'camera')) break; result.push(p); id = p.parent; }
    return result;
  }
  function resolveLayout(design, width, height) {
    const result = new Map(), visiting = new Set();
    function place(e) {
      if (result.has(e.id)) return result.get(e.id);
      if (visiting.has(e.id)) throw new Error('Container cycle'); visiting.add(e.id);
      const parent = elementOf(design, e.parent);
      let old = { x: 0, y: 0, width: design.size.width, height: design.size.height }, next = { x: 0, y: 0, width, height };
      if (parent) { old = parent.bounds; next = place(parent); }
      const dw = next.width - old.width, dh = next.height - old.height;
      const b = { x: e.bounds.x + next.x - old.x, y: e.bounds.y + next.y - old.y, width: e.bounds.width, height: e.bounds.height };
      if (e.horizontalAnchor === 'right') b.x += dw; else if (e.horizontalAnchor === 'center') b.x += dw / 2; else if (e.horizontalAnchor === 'stretch') b.width = Math.max(e.minWidth, b.width + dw);
      if (e.verticalAnchor === 'bottom') b.y += dh; else if (e.verticalAnchor === 'center') b.y += dh / 2; else if (e.verticalAnchor === 'stretch') b.height = Math.max(e.minHeight, b.height + dh);
      visiting.delete(e.id); result.set(e.id, b); return b;
    }
    design.elements.forEach(place); return result;
  }
  function applyLayout(target, design, width, height) {
    width = design.responsive ? Math.min(4096, Math.max(16, width)) : design.size.width;
    height = design.responsive ? Math.min(4096, Math.max(16, height)) : design.size.height;
    const bounds = resolveLayout(design, width, height);
    for (const e of target.elements) {
      e.bounds = Object.assign({}, bounds.get(e.id)); const source = elementOf(design, e.id);
      if (source && source.rowElements.length) {
        const layout = { size: { width: source.rowTemplateWidth > 0 ? source.rowTemplateWidth : source.bounds.width, height: source.rowHeight }, elements: source.rowElements };
        const rowBounds = resolveLayout(layout, e.bounds.width, e.rowHeight);
        e.rowElements = clone(source.rowElements); for (const child of e.rowElements) child.bounds = Object.assign({}, rowBounds.get(child.id));
      }
    }
    target.size = { width, height };
  }

  // ---- Item rows (model/ItemRows.java, model/RowTemplates.java) ----
  function parseRows(json) { try { const rows = JSON.parse(json || '[]'); return Array.isArray(rows) ? rows.slice(0, 128).filter(r => r && typeof r.item === 'string') : []; } catch (ex) { return []; } }
  function bindRow(source, row, index) {
    const e = clone(source); const b = s => String(s).split('${row.item}').join(row.item).split('${row.name}').join(row.name || '').split('${row.count}').join(String(row.count ?? 0)).split('${row.index}').join(String(index));
    e.text = b(e.text); e.item = b(e.item); e.value = b(e.value); e.tooltip = b(e.tooltip); return e;
  }

  // ---- The app ----
  class App {
    constructor(container, project, host, options) {
      this.project = project; this.host = host || {}; this.container = container;
      this.screens = {}; for (const [id, ui] of Object.entries(project.screens)) this.screens[id] = normalizeUi(ui);
      // Components (reusable groups of controls) that scripts can spawn while the game runs.
      this.components = {}; for (const [id, ui] of Object.entries(project.components || {})) this.components[id] = normalizeUi(ui);
      this.images = new Map(); this.animations = new Map(); this.loadImages(); this.loadFonts();
      // A touch device: a game can ask, and put its own buttons on screen only where they are needed.
      this.isTouch = (typeof navigator !== 'undefined' && navigator.maxTouchPoints > 0) || 'ontouchstart' in window;
      this.stick = null; this.touchTaps = new Map();
      this.canvas = document.createElement('canvas'); this.canvas.tabIndex = 0; this.canvas.className = 'wysicraft-canvas';
      // WebGL2 if this browser has it; every path below falls back to canvas2D when it doesn't.
      this.glLayer = new GLLayer(); this.dot = this.glLayer.ok ? dotTexture() : null;
      this.batched = false; this.glFlushes = 0; this.glT = [1, 1, 0, 0];
      // Canvas2D managed 2000 at about 0.9 ms a frame; the batch draws them in one call, so it carries far more.
      this.maxParticles = this.glLayer.ok ? MAX_PARTICLES_GL : MAX_PARTICLES;
      this.canvas.setAttribute('role', 'application'); container.appendChild(this.canvas);
      this.g = this.canvas.getContext('2d');
      this.mouse = { x: -1, y: -1, down: false }; this.heldKeys = new Set(); this.lastKey = 0;
      this.scroll = new Map(); this.focused = null; this.hovered = null; this.dragging = null; this.lastHover = 0;
      this.messages = []; this.closed = false; this.queue = []; this.busy = false; this.idleWaiters = []; this.navigations = 0;
      this.limits = Object.assign({ scriptOps: 128, tickMin: 50 }, project.limits || {});
      this.scripts = new ScriptRunner();
      this.bindInput();
      // Sound effects are decoded ahead, so the first play of each one is already on Web Audio. Sounds that Sound
      // controls play (music) are left out: they stream through the control's own audio element, and decoding them as
      // well kept every song whole in memory as raw audio (19 one-minute songs came to 526 MB).
      if (project && project.sounds) { const streamed = this.streamedSounds(); for (const [id, url] of Object.entries(project.sounds)) if (!streamed.has(id)) this.soundBuffer(url); }
      window.addEventListener('resize', () => this.layout());
      this.switchTo((options && options.screen) || project.main || Object.keys(this.screens)[0]);
      this.prof = null; this.physicsCounts = { bodies: 0, pairs: 0 };
      const frame = t => { if (this.prof) this.profiledFrame(t); else { this.tick(); this.render(); } requestAnimationFrame(frame); }; requestAnimationFrame(frame);
      if (this.host.profiler) this.setProfiler(true);
      // Keeps ticking in background tabs, where animation frames pause. While the page is visible the frame loop
      // already ticks, so this stays out of the way instead of doubling the work.
      setInterval(() => { if (document.hidden) this.tick(); }, 16);
    }
    // Project font files, registered with the browser under a stable family name. A font that will not load is
    // reported once and the text falls back to the default family, rather than the screen failing to draw.
    loadFonts() {
      this.fonts = new Set();
      const fonts = this.project.fonts || {};
      if (typeof FontFace !== 'function' || !document.fonts) return;
      for (const [id, url] of Object.entries(fonts)) {
        const family = fontFamilyName(id);
        try {
          const face = new FontFace(family, 'url("' + url + '")');
          face.load().then(loaded => { document.fonts.add(loaded); this.fonts.add(id); }, err => this.log('warn', 'Font ' + id + ' did not load: ' + err));
        } catch (ex) { this.log('warn', 'Font ' + id + ' did not load: ' + ex.message); }
      }
    }
    // Texture resource -> loaded image. Mirrors PackRepository.textureFile's fallbacks for project images.
    loadImages() {
      for (const [path, url] of Object.entries(this.project.assets || {})) {
        if (!path.endsWith('.png')) continue;
        const img = new Image(); img.decoding = 'async'; img.src = url;
        const meta = this.project.animations && this.project.animations[path];
        if (meta) img.onload = () => { const a = parseAnimation(meta, img.naturalWidth, img.naturalHeight); if (a) this.animations.set(path, a); };
        this.images.set(path, img);
      }
    }
    texturePath(resource) {
      const m = /^([a-z0-9_.-]+):(.+)$/.exec(resource || ''); if (!m) return null;
      const direct = 'assets/' + m[1] + '/' + m[2]; if (this.images.has(direct)) return direct;
      if (m[1] !== this.project.id) return null;
      let name = m[2].startsWith('textures/gui/') ? m[2].slice(13) : m[2]; if (name.startsWith('image/')) name = name.slice(6);
      for (const p of ['assets/' + m[1] + '/textures/gui/image/' + name, 'assets/' + m[1] + '/textures/gui/' + name, 'assets/textures/' + name]) if (this.images.has(p)) return p;
      return null;
    }
    texture(resource) { const p = this.texturePath(resource); const img = p && this.images.get(p); return img && img.complete && img.naturalWidth ? { img, anim: this.animations.get(p) || null } : null; }
    itemIcon(item) { const m = /^([a-z0-9_.-]+):(.+)$/.exec(item || ''); return m ? this.texture(m[1] + ':textures/item/' + m[2] + '.png') : null; }

    // ---- Screens ----
    screenId(id) { id = String(id || ''); const i = id.indexOf(':'); if (i >= 0 && id.slice(0, i) === this.project.id) id = id.slice(i + 1); return id; }
    // Shows a screen straight away (a fresh copy with its starting variables) and queues its open event.
    switchTo(id) {
      const design = this.screens[this.screenId(id)];
      if (!design) { this.log('warn', 'Unknown screen: ' + id); return false; }
      this.design = design; this.ui = clone(design); this.state = Object.assign({}, design.variables);
      this.scroll.clear(); this.focused = null; this.hovered = null; this.dragging = null; this.closed = false; this.queue.length = 0;
      this.lastTick = performance.now(); this.layout();
      document.title = this.ui.title || this.project.name || document.title;
      this.resetGame();
      this.enqueue(null, 'open', '');
      return true;
    }
    // Navigation from an action or script: the old screen's close event runs first, then the new screen opens.
    async open(id) {
      if (!this.screens[this.screenId(id)]) { this.log('warn', 'Unknown screen: ' + id); return; }
      if (++this.navigations > 16) { this.log('error', 'Navigation limit reached: screens keep opening each other.'); this.queue.length = 0; return; }
      if (this.ui && !this.closed) await this.process({ ui: this.ui, element: null, event: 'close', value: '' });
      this.switchTo(id);
    }
    async close() {
      if (this.closed) return;
      await this.process({ ui: this.ui, element: null, event: 'close', value: '' });
      this.closed = true; this.queue.length = 0; this.stopAllSounds();
      if (this.host.onClose) this.hook('onClose', this.ui.id); else this.messages.push({ text: 'Screen closed. Click to open it again.', until: Infinity });
    }
    layout() {
      const dpr = window.devicePixelRatio || 1, W = this.container.clientWidth || window.innerWidth, H = this.container.clientHeight || window.innerHeight;
      this.canvas.width = Math.max(1, Math.round(W * dpr)); this.canvas.height = Math.max(1, Math.round(H * dpr));
      this.canvas.style.width = W + 'px'; this.canvas.style.height = H + 'px'; this.dpr = dpr;
      if (this.glLayer.ok) this.glLayer.resize(this.canvas.width, this.canvas.height);
      const ui = this.design, frame = ui.showFrame ? 32 : 12;
      // GUI scale like Minecraft's "Auto": the largest whole number that fits the design size.
      let scale = Math.max(1, Math.floor(Math.min(W / (ui.size.width + 12), H / (ui.size.height + frame))));
      if (this.host.guiScale > 0) scale = this.host.guiScale;
      if (!ui.responsive && ui.fitToScreen) scale = Math.min(scale, Math.min(W / (ui.size.width + 12), H / (ui.size.height + frame)));
      this.scale = Math.max(0.25, scale);
      const gw = W / this.scale, gh = H / this.scale;
      if (this.viewport) applyLayout(this.ui, this.design, this.viewport.width, this.viewport.height);
      else applyLayout(this.ui, this.design, Math.floor(gw - 12), Math.floor(gh - frame));
      this.originX = Math.floor((gw - this.ui.size.width) / 2); this.originY = Math.floor((gh - this.ui.size.height + (ui.showFrame ? 14 : 0)) / 2);
    }
    // Parent chains are asked for several times per element per frame; they only change when the screen's
    // element list does, so they're worked out once and kept until then.
    parents(e) {
      if (this._parentsOf && this._parentsUi === this.ui && this._parentsCount === this.ui.elements.length) {
        let chain = this._parentsOf.get(e.id);
        if (!chain) { chain = ancestors(this.ui, e); this._parentsOf.set(e.id, chain); }
        return chain;
      }
      this._parentsUi = this.ui; this._parentsCount = this.ui.elements.length; this._parentsOf = new Map();
      const chain = ancestors(this.ui, e); this._parentsOf.set(e.id, chain); return chain;
    }
    visible(e) {
      if (e.touchOnly && !this.isTouch) return false; return e.visible && safeEvaluate(e.visibleIf, this.state) && this.parents(e).every(p => p.visible && safeEvaluate(p.visibleIf, this.state)); }
    enabled(e) { return e.enabled && safeEvaluate(e.enabledIf, this.state) && this.parents(e).every(p => p.enabled && safeEvaluate(p.enabledIf, this.state)); }
    x(e) { return this.originX + e.bounds.x; }
    y(e) { return this.originY + e.bounds.y - this.parents(e).filter(p => p.type === 'scroll_panel').reduce((s, p) => s + (this.scroll.get(p.id) || 0), 0); }
    inside(e, mx, my) {
      const cam = this._cam; if (cam && (mx < this.x(cam) || mx >= this.x(cam) + cam.bounds.width || my < this.y(cam) || my >= this.y(cam) + cam.bounds.height)) return false;
      if (this.ui.clipToScreen && (mx < this.originX || mx >= this.originX + this.ui.size.width || my < this.originY || my >= this.originY + this.ui.size.height)) return false;
      if (mx < this.x(e) || mx >= this.x(e) + e.bounds.width || my < this.y(e) || my >= this.y(e) + e.bounds.height) return false;
      return this.parents(e).every(p => mx >= this.x(p) && mx < this.x(p) + p.bounds.width && my >= this.y(p) && my < this.y(p) + p.bounds.height);
    }
    // Rows and their bound copies are kept until the list's value changes (DynamicScreen does the same),
    // instead of parsing the JSON and rebuilding every row on every frame.
    rowCache(e) {
      if (!this._rows) this._rows = new Map();
      let cache = this._rows.get(e.id);
      if (!cache || cache.json !== e.value) { cache = { json: e.value, rows: parseRows(e.value), display: new Map() }; this._rows.set(e.id, cache); }
      return cache;
    }
    rows(e) { return this.rowCache(e).rows; }
    rowDisplay(list, row, index) {
      const cache = this.rowCache(list); const key = index + '|' + list.rowElements.length;
      let display = cache.display.get(key);
      if (!display) { display = list.rowElements.map(s => bindRow(s, row, index)); cache.display.set(key, display); }
      return display;
    }
    listScroll(id) { return this.scroll.get(id) || 0; }

    // ---- Drawing ----
    roundPath(x, y, w, h, r) { const g = this.g; g.beginPath(); r = Math.max(0, Math.min(r, Math.min(w, h) / 2)); if (r > 0 && g.roundRect) g.roundRect(x, y, w, h, r); else g.rect(x, y, w, h); }
    fill(x, y, w, h, r, style) { if (w <= 0 || h <= 0) return; this.g.fillStyle = style; this.roundPath(x, y, w, h, r); this.g.fill(); }
    clip(x, y, w, h) { this.g.save(); this.g.beginPath(); this.g.rect(x, y, w, h); this.g.clip(); }
    unclip() { this.g.restore(); }
    drawTexture(tex, x, y, w, h, radius, opacity) {
      const g = this.g; let sx = 0, sy = 0, sw = tex.img.naturalWidth, sh = tex.img.naturalHeight;
      if (tex.anim) { const i = tex.anim.frameAt(performance.now()); sx = (i % tex.anim.columns) * tex.anim.fw; sy = Math.floor(i / tex.anim.columns) * tex.anim.fh; sw = tex.anim.fw; sh = tex.anim.fh; }
      g.save(); g.globalAlpha = opacity; if (radius > 0) { this.roundPath(x, y, w, h, radius); g.clip(); }
      this.pixelImage(tex.img, sx, sy, sw, sh, x, y, w, h); g.restore();
    }
    // Pictures are drawn with their edges on whole device pixels. At in-between scales (a 1.25 display, a fitted
    // window, a zoomed camera) neighbours then meet exactly, with no hairline seam between tiles or backdrop halves.
    pixelImage(img, sx, sy, sw, sh, x, y, w, h) {
      const g = this.g, t = g.getTransform();
      const x0 = Math.round(t.a * x + t.e), y0 = Math.round(t.d * y + t.f), x1 = Math.round(t.a * (x + w) + t.e), y1 = Math.round(t.d * (y + h) + t.f);
      if (x1 <= x0 || y1 <= y0) return;
      g.save(); g.setTransform(1, 0, 0, 1, 0, 0); g.drawImage(img, sx, sy, sw, sh, x0, y0, x1 - x0, y1 - y0); g.restore();
    }
    skin(e, x, y, w, h) {
      if (!e.fillEnabled || UNSEEN.has(e.type)) return; // sounds play, cameras frame the view and emitters throw particles: none is drawn
      if (e.type === 'shape' || e.type === 'collider') return; // they fill their own outline
      if (!e.texture || e.type === 'image' || e.type === 'texture_region' || e.type === 'sprite') { this.fill(x, y, w, h, e.cornerRadius, color(e.background, e.opacity)); return; }
      const tex = this.texture(e.texture);
      if (tex) this.drawTexture(tex, x, y, w, h, e.cornerRadius, e.opacity); else this.fill(x, y, w, h, e.cornerRadius, color(e.background, e.opacity));
    }
    border(e, x, y, w, h) {
      if (e.type === 'shape' || UNSEEN.has(e.type)) return;
      const t = Math.min(Math.ceil(e.borderWidth), Math.min(w, h) / 2); if (t <= 0) return;
      const g = this.g, r = Math.max(0, Math.min(e.cornerRadius, Math.min(w, h) / 2));
      g.fillStyle = color(e.borderColor, e.opacity); g.beginPath();
      if (g.roundRect) { g.roundRect(x, y, w, h, r); g.roundRect(x + t, y + t, w - 2 * t, h - 2 * t, Math.max(0, r - t)); } else { g.rect(x, y, w, h); g.rect(x + t, y + t, w - 2 * t, h - 2 * t); }
      g.fill('evenodd');
    }
    font(e, scale) {
      return `${e.italic ? 'italic ' : ''}${e.bold ? '700 ' : '400 '}${9 * scale}px ${this.fontFamily(e.font)}`;
    }
    // Which CSS family a control's font resolves to: a built-in family, a font file in the project, or the default.
    fontFamily(font) {
      if (WEB_FONTS[font]) return WEB_FONTS[font];
      if (font === 'minecraft:alt') return '"Courier New", monospace';
      const fallback = this.host.fontFamily || '"Segoe UI", Roboto, "Helvetica Neue", Arial, sans-serif';
      // A project font falls back until its file has finished loading, so the first frames still draw.
      if (this.project.fonts && this.project.fonts[font]) return fontFamilyName(font) + ', ' + fallback;
      return fallback;
    }
    // One line of text, clipped to the width (DynamicScreen.text).
    text(e, x, y, w, h, value) {
      const g = this.g, scale = e.fontScale; let text = String(value ?? '');
      g.font = this.font(e, scale); g.textBaseline = 'middle';
      const max = Math.max(1, w - 4 * scale); if (g.measureText(text).width > max) { while (text && g.measureText(text).width > max) text = text.slice(0, -1); }
      if (!text) return;
      const tw = g.measureText(text).width, dx = e.alignment === 'center' ? (w - tw) / 2 : e.alignment === 'right' ? w - tw - 2 : 2, cy = y + h / 2;
      if (e.textShadow && e.shadowOpacity > 0) {
        g.save(); g.fillStyle = color(e.shadowColor, e.opacity * e.shadowOpacity);
        if (e.shadowBlur > 0) { g.filter = `blur(${e.shadowBlur / 2 * this.scale * this.dpr}px)`; }
        g.fillText(text, x + dx + e.shadowOffsetX, cy + e.shadowOffsetY); g.restore();
      }
      g.fillStyle = color(e.foreground, e.opacity); g.fillText(text, x + dx, cy);
      if (e.underline) g.fillRect(x + dx, cy + 4.5 * scale, tw, Math.max(0.5, 0.75 * scale));
    }
    drawItem(item, x, y, size) {
      const icon = this.itemIcon(item), g = this.g;
      if (icon) { g.save(); g.imageSmoothingEnabled = false; g.drawImage(icon.img, 0, 0, icon.img.naturalWidth, Math.min(icon.img.naturalHeight, icon.img.naturalWidth), x, y, size, size); g.restore(); return; }
      // No Minecraft assets here: a neutral token with the item's initial (add textures/item/<name>.png to show a real icon).
      this.fill(x + size * 0.1, y + size * 0.1, size * 0.8, size * 0.8, size * 0.2, 'rgba(129,153,164,0.35)');
      g.fillStyle = '#E3ECEB'; g.font = `700 ${size * 0.5}px sans-serif`; g.textAlign = 'center'; g.textBaseline = 'middle';
      g.fillText((item.split(':').pop() || '?').charAt(0).toUpperCase(), x + size / 2, y + size / 2 + size * 0.03); g.textAlign = 'left';
    }
    draw(e, x, y, w, h, hover) {
      const g = this.g;
      switch (e.type) {
        case 'label': this.text(e, x, y, w, h, bind(e.text, this.state)); break;
        case 'button': if (hover) this.fill(x, y, w, h, e.cornerRadius, 'rgba(255,255,255,0.133)'); this.text(e, x, y, w, h, bind(e.text, this.state)); break;
        case 'textbox': {
          if (this.focused === e) { g.strokeStyle = '#91CFFF'; g.lineWidth = 1; g.strokeRect(x + 0.5, y + 0.5, w - 1, h - 1); }
          this.text(e, x + 3, y, w - 6, h, e.value + (this.focused === e && Math.floor(performance.now() / 500) % 2 === 0 ? '_' : '')); break;
        }
        case 'checkbox': {
          const size = Math.min(16, h); g.strokeStyle = '#B0B8C0'; g.lineWidth = 1; g.strokeRect(x + 0.5, y + 0.5, size - 1, size - 1);
          if (e.value === 'true') { g.fillStyle = '#69C8EE'; g.fillRect(x + 3, y + 3, size - 6, size - 6); }
          this.text(e, x + size + 4, y, w - size - 4, h, bind(e.text, this.state)); break;
        }
        case 'slider': case 'progress': {
          let v = parseFloat(e.value); if (isNaN(v)) v = e.minimum;
          const f = Math.floor(w * Math.min(1, Math.max(0, (v - e.minimum) / (e.maximum - e.minimum || 1))));
          g.save(); g.globalAlpha *= Math.min(1, Math.max(0, e.opacity)); g.fillStyle = '#318DB5'; g.fillRect(x, y, f, h); g.restore(); this.text(e, x, y, w, h, e.value); break;
        }
        case 'dropdown': {
          const i = parseInt(e.value, 10) || 0; const t = e.options.length ? e.options[Math.min(Math.max(i, 0), e.options.length - 1)] : '(empty)';
          this.text(e, x + 3, y, w - 14, h, t); this.text(Object.assign({}, e, { alignment: 'left', textShadow: false }), x + w - 12, y, 12, h, 'v'); break;
        }
        case 'item': { const s = Math.min(w, h); this.drawItem(e.item, x + (w - s) / 2, y + (h - s) / 2, s); break; }
        case 'image': case 'texture_region': {
          if (!e.texture) break; const tex = this.texture(e.texture); if (!tex) break;
          if (this.batched && e.type === 'image') {
            this.glMark();
            let sx = 0, sy = 0, sw = tex.img.naturalWidth, sh = tex.img.naturalHeight;
            if (tex.anim) { const i = tex.anim.frameAt(performance.now()); sx = (i % tex.anim.columns) * tex.anim.fw; sy = Math.floor(i / tex.anim.columns) * tex.anim.fh; sw = tex.anim.fw; sh = tex.anim.fh; }
            this.glQuad(tex.img, sx, sy, sw, sh, x, y, w, h, null, e.opacity, 0, false);
            break;
          }
          if (e.type === 'texture_region') {
            const kx = tex.img.naturalWidth / e.textureWidth, ky = tex.img.naturalHeight / e.textureHeight;
            g.save(); g.globalAlpha = e.opacity; this.pixelImage(tex.img, e.textureX * kx, e.textureY * ky, e.bounds.width * kx, e.bounds.height * ky, x, y, w, h); g.restore();
          } else this.drawTexture(tex, x, y, w, h, 0, e.opacity);
          break;
        }
        case 'item_list': this.drawList(e, x, y, w, h); break;
        case 'sprite': this.drawSprite(e, x, y, w, h); break;
        case 'shape': this.drawShape(e, x, y, w, h, hover); break;
        case 'particles': this.drawParticles(e); break;
        case 'tilemap': this.drawTilemap(e, x, y); break;
        case 'collider': this.drawCollider(e, x, y, w, h); break;
      }
    }
    drawTilemap(e, x, y) {
      const tex = e.texture && this.texture(e.texture); if (!tex) return;
      const tw = Math.max(1, e.tileWidth | 0), th = Math.max(1, e.tileHeight | 0);
      const sheet = Math.max(1, Math.floor(tex.img.naturalWidth / tw));
      const cache = this.tileGrid(e), grid = cache.grid, columns = cache.columns, rows = cache.rows;
      // The world rectangle on screen: the camera's view if there is one, otherwise the screen itself.
      const cam = this._cam;
      const vx = cam ? this.x(cam) : this.originX, vy = cam ? this.y(cam) : this.originY;
      const vw = cam ? cam.bounds.width : this.ui.size.width, vh = cam ? cam.bounds.height : this.ui.size.height;
      const c0 = Math.max(0, Math.floor((vx - x) / tw)), c1 = Math.min(columns - 1, Math.ceil((vx + vw - x) / tw));
      const r0 = Math.max(0, Math.floor((vy - y) / th)), r1 = Math.min(rows - 1, Math.ceil((vy + vh - y) / th));
      if (c1 < c0 || r1 < r0) return;
      if (this.batched) {
        this.glMark();
        // One instance per visible tile, all in one draw call.
        for (let r = r0; r <= r1; r++) {
          const line = r * columns;
          for (let c = c0; c <= c1; c++) {
            const index = grid[line + c]; if (index < 0) continue;
            this.glQuad(tex.img, (index % sheet) * tw, Math.floor(index / sheet) * th, tw, th, x + c * tw, y + r * th, tw, th, null, e.opacity, 0, false);
          }
        }
        return;
      }
      const g = this.g; g.save(); g.globalAlpha = e.opacity;
      for (let r = r0; r <= r1; r++) {
        const line = r * columns;
        for (let c = c0; c <= c1; c++) {
          const index = grid[line + c]; if (index < 0) continue;
          this.pixelImage(tex.img, (index % sheet) * tw, Math.floor(index / sheet) * th, tw, th, x + c * tw, y + r * th, tw, th);
        }
      }
      g.restore();
    }
    drawList(e, x, y, w, h) {
      const g = this.g, rows = this.rows(e), rh = Math.min(128, Math.max(24, e.rowHeight));
      const buttons = (e.primaryLabel ? 1 : 0) + (e.secondaryLabel ? 1 : 0), bw = Math.max(32, Math.min(62, Math.floor(w / 5))), right = w - 6 - buttons * bw;
      this.clip(x, y, w, h);
      try {
        for (let i = Math.max(0, Math.floor(this.listScroll(e.id) / rh)); i < rows.length; i++) {
          const top = y + i * rh - this.listScroll(e.id); if (top >= y + h) break; const row = rows[i];
          if (e.rowElements.length) { this.drawRowTemplate(e, row, i, x, top, w, rh); continue; }
          g.fillStyle = i % 2 === 0 ? 'rgba(44,78,88,0.2)' : 'rgba(44,78,88,0.133)'; g.fillRect(x, top, w, rh - 1);
          this.drawItem(row.item, x + 4, top + 4, 16);
          const base = { font: 'minecraft:default', fontScale: 1, alignment: 'left', opacity: 1, textShadow: false, bold: false, italic: false, underline: false };
          this.text(Object.assign({}, base, { foreground: '#E3ECEB' }), x + 24, top + 4 - 4, right - 26, 16, row.name || '');
          if (e.showItemId) this.text(Object.assign({}, base, { foreground: '#8199A4', fontScale: 0.65 }), x + 24, top + 14, right - 26, 8, row.item);
          let bx = x + w - 6 - buttons * bw;
          for (const label of [e.primaryLabel, e.secondaryLabel]) if (label) {
            this.fill(bx, top + 3, bw - 4, rh - 6, 3, '#325546');
            this.text(Object.assign({}, base, { foreground: '#C8F1AE' }), bx + 1, top + 3, bw - 6, rh - 6, label); bx += bw;
          }
          if (!buttons) this.text(Object.assign({}, base, { foreground: '#9CE7CD', alignment: 'right' }), x, top + 2, w - 2, 16, 'x' + (row.count ?? 0));
        }
      } finally { this.unclip(); }
      if (rows.length * rh > h) { const thumb = Math.max(8, h * h / (rows.length * rh)), off = this.listScroll(e.id) * (h - thumb) / Math.max(1, rows.length * rh - h); g.fillStyle = '#8199A4'; g.fillRect(x + w - 3, y + off, 3, thumb); }
    }
    rowActive(list, e, input) {
      const layout = { elements: list.rowElements }, parents = ancestors(layout, e);
      return e.visible && safeEvaluate(e.visibleIf, this.state) && (!input || e.enabled && safeEvaluate(e.enabledIf, this.state))
        && parents.every(p => p.visible && safeEvaluate(p.visibleIf, this.state) && (!input || p.enabled && safeEvaluate(p.enabledIf, this.state)));
    }
    drawRowTemplate(list, row, index, x, y, w, h) {
      this.clip(x, y, w, h);
      try {
        for (const source of this.rowDisplay(list, row, index)) {
          if (!this.rowActive(list, source, false)) continue;
          const parents = ancestors({ elements: list.rowElements }, source);
          for (const p of parents) this.clip(x + p.bounds.x, y + p.bounds.y, p.bounds.width, p.bounds.height);
          try { const ex = x + source.bounds.x, ey = y + source.bounds.y; this.skin(source, ex, ey, source.bounds.width, source.bounds.height); this.draw(source, ex, ey, source.bounds.width, source.bounds.height, false); this.border(source, ex, ey, source.bounds.width, source.bounds.height); }
          finally { for (const p of parents) this.unclip(); }
        }
      } finally { this.unclip(); }
    }
    // Whether a control is drawn entirely by the batch layer. Anything that also needs a 2D fill, border, shadow,
    // rounded clip, disabled dim or a parent's clipping rectangle stays on canvas2D, because those are 2D state and
    // the batch is composited later. A control that fails this simply draws the way it always did.
    glElement(e, enabled) {
      if (!this.glLayer.ok || !enabled) return false;
      if (e.fillEnabled || e.borderWidth > 0 || e.cornerRadius > 0) return false;
      if (this.parents(e).length) return false;
      const usable = resource => { const tex = this.texture(resource); return !!tex && this.glLayer.usable(tex.img); };
      if (e.type === 'tilemap') return !e.texture || !this.texture(e.texture) || usable(e.texture);
      if (e.type === 'particles') { const fx = this.effectOf(e); return !(fx && fx.shape === 'texture' && fx.texture && this.texture(fx.texture)) || usable(fx.texture); }
      if ((e.type === 'sprite' || e.type === 'image') && e.texture && usable(e.texture)) return true;
      return false;
    }
    // Reads the 2D context's current transform, which carries the display scale and the camera. Called once per
    // batched control rather than once per quad: getTransform allocates, and a particle burst is 20,000 quads.
    glMark() { const t = this.g.getTransform(); this.glT = [t.a, t.d, t.e, t.f]; }
    // A quad in those coordinates.
    glQuad(img, sx, sy, sw, sh, x, y, w, h, colour, alpha, rot, additive) {
      const t = this.glT;
      this.glLayer.quad(img, sx, sy, sw, sh, t[0] * x + t[2], t[1] * y + t[3], t[0] * w, t[1] * h,
        colour ? colour[0] / 255 : 1, colour ? colour[1] / 255 : 1, colour ? colour[2] / 255 : 1, alpha, rot, additive);
    }
    // The fragment shader this screen runs over the batch, if any.
    shaderSource() {
      const named = this.ui && this.ui.shader;
      return named ? ((this.project.shaders || {})[named] || '') : '';
    }
    // Draws what the batch is holding and composites it into the 2D canvas, in device pixels so it lands exactly
    // where the quads were placed. Called whenever something that isn't batched is about to draw, which is what
    // keeps the draw order identical to the canvas2D-only path.
    flushGL() {
      if (!this.glLayer.ok || !this.glLayer.total) return;
      this.glLayer.flush(this.shaderSource());
      const g = this.g;
      g.save();
      g.setTransform(1, 0, 0, 1, 0, 0);
      g.globalAlpha = 1; g.globalCompositeOperation = 'source-over'; g.filter = 'none';
      g.drawImage(this.glLayer.canvas, 0, 0);
      g.restore();
      this.glLayer.begin();
      this.glFlushes++;
    }
    render() {
      const g = this.g; if (!this.ui) return;
      g.setTransform(1, 0, 0, 1, 0, 0); g.clearRect(0, 0, this.canvas.width, this.canvas.height);
      g.setTransform(this.scale * this.dpr, 0, 0, this.scale * this.dpr, 0, 0); g.imageSmoothingEnabled = false;
      const W = this.canvas.width / (this.scale * this.dpr), H = this.canvas.height / (this.scale * this.dpr);
      if (this.ui.dimBackground) { g.fillStyle = 'rgba(16,20,27,0.69)'; g.fillRect(0, 0, W, H); }
      if (this.ui.showFrame) {
        g.fillStyle = '#242B34'; g.fillRect(this.originX - 6, this.originY - 20, this.ui.size.width + 12, this.ui.size.height + 26);
        this.text({ font: 'minecraft:default', fontScale: 1, alignment: 'left', opacity: 1, foreground: '#D9E6F1' }, this.originX - 2, this.originY - 18, this.ui.size.width, 10, this.ui.title);
      }
      const mx = this.mouse.x, my = this.mouse.y; let over = null;
      // Clip to screen: nothing outside the screen's own area is drawn (for games that scroll things in from off screen).
      const clipped = this.ui.clipToScreen && !this.closed; if (clipped) this.clip(this.originX, this.originY, this.ui.size.width, this.ui.size.height);
      const cam = this._cam = this.closed ? null : this.camera(), view = cam && this.view(cam);
      if (view) { g.save(); g.translate(view.ox, view.oy); g.scale(view.k, view.k); this.clip(this.x(cam), this.y(cam), cam.bounds.width, cam.bounds.height); }
      if (this.glLayer.ok) { this.glLayer.begin(); this.glFlushes = 0; }
      if (!this.closed) for (const e of this.ui.elements) {
        if (!this.visible(e)) continue; const hover = !UNSEEN.has(e.type) && this.inside(e, mx, my), en = this.enabled(e); if (hover && en) over = e;
        // Anything not going through the batch forces what is already batched to be drawn first, so a control
        // between two batched ones still appears between them.
        this.batched = this.glElement(e, en);
        if (!this.batched) this.flushGL();
        const parents = this.parents(e); for (const p of parents) this.clip(this.x(p), this.y(p), p.bounds.width, p.bounds.height);
        try {
          const x = this.x(e), y = this.y(e), w = e.bounds.width, h = e.bounds.height;
          this.skin(e, x, y, w, h); this.draw(e, x, y, w, h, hover && en); this.border(e, x, y, w, h);
          if (!en && !UNSEEN.has(e.type)) this.fill(x, y, w, h, e.cornerRadius, 'rgba(0,0,0,0.467)');
        } catch (ex) { this.batched = false; g.fillStyle = '#FF7070'; g.font = '8px sans-serif'; g.fillText('Invalid ' + e.type, this.x(e), this.y(e) + 8); }
        finally { for (const p of parents) this.unclip(); }
      }
      this.batched = false;
      this.flushGL();
      if (view) { this.unclip(); g.restore(); }
      if (clipped) this.unclip();
      if (over !== this.hovered) { if (this.hovered) this.fire(this.hovered, 'mouse_leave', ''); this.hovered = over; if (over) this.fire(over, 'mouse_enter', ''); }
      if (this.hovered && performance.now() - this.lastHover > 250) { this.lastHover = performance.now(); this.fire(this.hovered, 'hover', ''); }
      this.canvas.style.cursor = over && ['button', 'checkbox', 'slider', 'dropdown', 'item_list', 'textbox'].includes(over.type) ? (over.type === 'textbox' ? 'text' : 'pointer') : 'default';
      if (over && over.tooltip) this.tooltip(bind(over.tooltip, this.state), view ? view.ox + mx * view.k : mx, view ? view.oy + my * view.k : my, W, H);
      this.drawMessages(W, H);
    }
    tooltip(text, mx, my, W, H) {
      const g = this.g; g.font = `400 9px ${this.host.fontFamily || 'sans-serif'}`; const tw = g.measureText(text).width + 8;
      let x = mx + 10, y = my - 12; if (x + tw > W) x = mx - tw - 4; if (y < 2) y = my + 12;
      this.fill(x, y, tw, 14, 2, 'rgba(16,0,16,0.94)'); g.strokeStyle = 'rgba(80,0,255,0.5)'; g.lineWidth = 1; g.strokeRect(x + 0.5, y + 0.5, tw - 1, 13);
      g.fillStyle = '#FFFFFF'; g.textBaseline = 'middle'; g.fillText(text, x + 4, y + 7);
    }
    // Messages from "message" actions and ctx.message: shown briefly at the bottom (Minecraft shows them in chat).
    drawMessages(W, H) {
      const now = performance.now(); this.messages = this.messages.filter(m => m.until > now).slice(-5);
      const g = this.g; let y = H - 6;
      for (const m of this.messages.slice().reverse()) {
        g.font = `400 9px ${this.host.fontFamily || 'sans-serif'}`; const tw = Math.min(W - 8, g.measureText(m.text).width + 8);
        y -= 14; this.fill(4, y, tw, 13, 2, 'rgba(0,0,0,0.6)'); g.fillStyle = '#FFFFFF'; g.textBaseline = 'middle'; g.fillText(m.text, 8, y + 6.5, tw - 8);
      }
    }
    message(text) { if (this.host.onMessage && this.host.onMessage(String(text)) === false) return; this.messages.push({ text: String(text), until: performance.now() + 6000 }); }
    log(level, text) { if (typeof this.host.onLog === 'function') this.hook('onLog', level, text); else (console[level] || console.log)('[Wysicraft] ' + text); }

    // ---- Input (DynamicScreen mouse/keyboard) ----
    point(ev) {
      const r = this.canvas.getBoundingClientRect(), x = (ev.clientX - r.left) / this.scale, y = (ev.clientY - r.top) / this.scale;
      const v = this._cam && this.view(this._cam); return v ? { x: (x - v.ox) / v.k, y: (y - v.oy) / v.k } : { x, y };
    }
    // Camera: the first visible camera control is the view. Its rectangle is scaled (the same in both directions) to fill
    // the screen's area and centered; a smaller camera zooms in. Screen point = o + point * k.
    camera() { for (const e of this.ui.elements) if (e.type === 'camera' && this.visible(e)) return e; return null; }
    view(cam) {
      const sw = this.ui.size.width, sh = this.ui.size.height, cw = Math.max(1, cam.bounds.width), ch = Math.max(1, cam.bounds.height), k = Math.min(sw / cw, sh / ch);
      return { k, ox: this.originX + (sw - cw * k) / 2 - this.x(cam) * k, oy: this.originY + (sh - ch * k) / 2 - this.y(cam) * k };
    }
    bindInput() {
      const c = this.canvas;
      c.addEventListener('mousemove', ev => { const p = this.point(ev); this.mouse.x = p.x; this.mouse.y = p.y; if (this.dragging) this.slide(this.dragging, p.x); });
      c.addEventListener('mouseleave', () => { this.mouse.x = this.mouse.y = -1; });
      c.addEventListener('mousedown', ev => { c.focus(); if (ev.button !== 0) return; const p = this.point(ev); this.mouse.x = p.x; this.mouse.y = p.y; this.click(p.x, p.y); ev.preventDefault(); });
      window.addEventListener('mouseup', () => { this.dragging = null; if (this.virtualInputs) this.virtualInputs.clear(); });
      c.addEventListener('touchstart', ev => {
        c.focus();
        for (const t of ev.changedTouches) {
          const p = this.point(t);
          if (this.controlAt(p.x, p.y)) { this.mouse.x = p.x; this.mouse.y = p.y; this.touchTaps.set(t.identifier, true); this.click(p.x, p.y); }
          else if (this.stick === null) this.stick = { id: t.identifier, ox: p.x, oy: p.y, x: p.x, y: p.y };
        }
        ev.preventDefault();
      }, { passive: false });
      c.addEventListener('touchmove', ev => {
        for (const t of ev.changedTouches) {
          const p = this.point(t);
          if (this.stick && this.stick.id === t.identifier) { this.stick.x = p.x; this.stick.y = p.y; }
          else if (this.dragging) this.slide(this.dragging, p.x);
        }
        ev.preventDefault();
      }, { passive: false });
      const lift = ev => {
        for (const t of ev.changedTouches) {
          if (this.stick && this.stick.id === t.identifier) this.stick = null;
          if (this.touchTaps.delete(t.identifier)) { this.dragging = null; if (this.virtualInputs) this.virtualInputs.clear(); }
        }
      };
      c.addEventListener('touchend', lift); c.addEventListener('touchcancel', lift);
      c.addEventListener('wheel', ev => { const p = this.point(ev); if (this.wheel(p.x, p.y, -Math.sign(ev.deltaY))) ev.preventDefault(); }, { passive: false });
      c.addEventListener('keydown', ev => this.keyDown(ev));
      c.addEventListener('keyup', ev => { this.heldKeys.delete(ev.code); const name = keyName(ev); if (name) this.keyHeld(name, false); });
      c.addEventListener('paste', ev => { if (!this.focused) return; const v = (ev.clipboardData.getData('text') || '').replace(/[\x00-\x1f\x7f]/g, ''); this.focused.value = (this.focused.value + v).slice(0, 1024); this.fire(this.focused, 'text_changed', this.focused.value); ev.preventDefault(); });
      window.addEventListener('blur', () => { this.heldKeys.clear(); if (this.keyNamesDown) this.keyNamesDown.clear(); });
      setTimeout(() => c.focus(), 0);
    }
    // The topmost control under a point, or null. Used to tell a tap on a button from a drag on the floor.
    controlAt(mx, my) {
      if (this.closed || !this.ui) return null;
      for (const e of this.ui.elements.slice().reverse())
        if (!UNSEEN.has(e.type) && this.visible(e) && this.enabled(e) && this.inside(e, mx, my)) return e;
      return null;
    }
    click(mx, my) {
      if (this.closed) { if (!this.host.onClose) { this.messages = []; this.open(this.ui.id); } return; }
      this.focused = null;
      for (const e of this.ui.elements.slice().reverse()) if (!UNSEEN.has(e.type) && this.visible(e) && this.enabled(e) && this.inside(e, mx, my)) {
        if (e.input) this.virtualPress(e.input, true); // held until the mouse button is let go
        switch (e.type) {
          case 'shape': this.fire(e, 'click', ''); break;
          case 'item_list': {
            const rh = Math.min(128, Math.max(24, e.rowHeight)), index = Math.floor((my - this.y(e) + this.listScroll(e.id)) / rh), rows = this.rows(e);
            if (index < 0 || index >= rows.length) break;
            if (e.rowElements.length) { const action = this.rowHit(e, mx - this.x(e), (my - this.y(e) + this.listScroll(e.id)) % rh); if (action) this.fire(e, action, String(index)); break; }
            const count = (e.primaryLabel ? 1 : 0) + (e.secondaryLabel ? 1 : 0), bw = Math.max(32, Math.min(62, Math.floor(e.bounds.width / 5))), local = mx - this.x(e);
            let event = 'item_click'; if (e.secondaryLabel && local >= e.bounds.width - 6 - bw) event = 'item_secondary'; else if (e.primaryLabel && local >= e.bounds.width - 6 - count * bw) event = 'item_primary';
            this.fire(e, event, String(index)); break;
          }
          case 'button': this.fire(e, 'click', ''); break;
          case 'textbox': this.focused = e; break;
          case 'checkbox': e.value = String(e.value !== 'true'); this.fire(e, e.value === 'true' ? 'checked' : 'unchecked', e.value); break;
          case 'slider': this.dragging = e; this.slide(e, mx); break;
          case 'dropdown': if (e.options.length) { const i = parseInt(e.value, 10); e.value = String(((isNaN(i) ? -1 : i) + 1) % e.options.length); this.fire(e, 'value_changed', e.value); } break;
        }
        return;
      }
    }
    rowHit(list, x, y) {
      for (const e of list.rowElements.slice().reverse()) {
        const within = b => x >= b.x && y >= b.y && x < b.x + b.width && y < b.y + b.height;
        if (this.rowActive(list, e, true) && within(e.bounds) && ancestors({ elements: list.rowElements }, e).every(p => within(p.bounds)) && e.type === 'button') return e.rowAction;
      }
      return 'item_click';
    }
    slide(e, mx) { e.value = (e.minimum + Math.min(1, Math.max(0, (mx - this.x(e)) / e.bounds.width)) * (e.maximum - e.minimum)).toFixed(2); this.fire(e, 'value_changed', e.value); }
    wheel(mx, my, v) {
      for (const e of this.ui.elements) if (e.type === 'item_list' && this.visible(e) && this.enabled(e) && this.inside(e, mx, my)) {
        const max = Math.max(0, this.rows(e).length * Math.min(128, Math.max(24, e.rowHeight)) - e.bounds.height), next = Math.min(max, Math.max(0, this.listScroll(e.id) - v * 24));
        if (next === this.listScroll(e.id)) continue; this.scroll.set(e.id, next); return true;
      }
      const panels = this.ui.elements.filter(e => e.type === 'scroll_panel').sort((a, b) => this.parents(b).length - this.parents(a).length);
      for (const e of panels) if (this.visible(e) && this.enabled(e) && this.inside(e, mx, my)) {
        const kids = this.ui.elements.filter(c => c.parent === e.id), bottom = kids.length ? Math.max(...kids.map(c => c.bounds.y + c.bounds.height)) : e.bounds.y + e.bounds.height;
        const max = Math.max(0, bottom - e.bounds.y - e.bounds.height), next = Math.min(max, Math.max(0, (this.scroll.get(e.id) || 0) - v * 12));
        if (next === (this.scroll.get(e.id) || 0)) continue; this.scroll.set(e.id, next); return true;
      }
      return false;
    }
    keyDown(ev) {
      if (this.focused) {
        const f = this.focused;
        if (ev.key === 'Backspace') { f.value = f.value.slice(0, -1); this.fire(f, 'text_changed', f.value); ev.preventDefault(); return; }
        if (ev.key === 'Enter') { this.fire(f, 'submit', f.value); ev.preventDefault(); return; }
        if (ev.key === 'Escape') { this.focused = null; ev.preventDefault(); return; }
        if (ev.key.length === 1 && !ev.ctrlKey && !ev.metaKey && !ev.altKey) { if (f.value.length < 1024) { f.value += ev.key; this.fire(f, 'text_changed', f.value); } ev.preventDefault(); }
        return;
      }
      if (ev.key === 'Escape' && this.host.closeOnEscape) { this.close(); ev.preventDefault(); return; }
      const held = keyName(ev); if (held) { this.keyHeld(held, true); if ((this.project.inputs || []).some(i => i.keys.includes(held))) ev.preventDefault(); }
      if (!this.ui.events.key) return;
      const name = keyName(ev); if (!name) return;
      ev.preventDefault();
      // A fresh press fires at once; a held key repeats at most every keyRepeat ms (never when 0). Nothing queues.
      const repeat = ev.repeat || this.heldKeys.has(ev.code); this.heldKeys.add(ev.code);
      const now = performance.now();
      if (repeat && (this.ui.keyRepeat <= 0 || now - this.lastKey < this.ui.keyRepeat)) return;
      this.lastKey = now; this.fire(null, 'key', name, repeat);
    }
    tick() {
      this.simulate(performance.now());
      if (!this.ui || this.closed || this.ui.tickInterval < this.limits.tickMin || !this.ui.events.tick) return;
      const now = performance.now(); if (now - this.lastTick >= this.ui.tickInterval) { this.lastTick = now; this.fire(null, 'tick', ''); }
    }

    // ---- Events ----
    // Events run one at a time, in order. Tick, key and hover events that arrive while another event is still
    // running are dropped rather than queued, so held keys and timers can never build up a backlog.
    fire(element, event, value, repeat) { this.enqueue(element, event, value, repeat); }
    enqueue(element, event, value, repeat) {
      if (!this.ui) return;
      const ev = (element ? element.events : this.ui.events)[event];
      if (!ev) { this.hook('onEvent', { screen: this.ui.id, element: element ? element.id : '', event, value: String(value ?? ''), assigned: false }); return; }
      if (event === 'tick') { if (this.queue.some(q => q.event === 'tick')) return; }
      else if ((this.busy || this.queue.length) && (event === 'key' || event === 'hover' || event === 'collide_stay' || event === 'trigger_stay')) return;
      if (this.queue.length >= 32) { this.log('warn', 'Too many events waiting; some were skipped.'); return; }
      this.queue.push({ ui: this.ui, element, event, value: String(value ?? ''), repeat: !!repeat });
      this.pump();
    }
    request(item) { if (this.queue.length >= 32) return; this.queue.push(item); this.pump(); }
    async pump() {
      if (this.busy) return; this.busy = true;
      try {
        while (this.queue.length) {
          const item = this.queue.shift();
          if (!item.kind && item.ui !== this.ui) continue;
          try { await this.process(item); } catch (ex) { this.log('error', ex && ex.message ? ex.message : String(ex)); }
        }
      } finally {
        this.busy = false; this.navigations = 0;
        const waiters = this.idleWaiters; this.idleWaiters = []; for (const w of waiters) w();
      }
    }
    idle() { return this.busy || this.queue.length ? new Promise(resolve => this.idleWaiters.push(resolve)) : Promise.resolve(); }
    hook(name, ...args) { const f = this.host[name]; if (typeof f !== 'function') return undefined; try { return f.apply(this.host, args); } catch (ex) { console.error('[Wysicraft] host.' + name + ':', ex); return undefined; } }
    // One event: client actions and script, then the "server" side (ServerRuntime), then navigation.
    async process(item) {
      if (item.kind === 'open') { await this.open(item.id); return; }
      if (item.kind === 'close') { await this.close(); return; }
      if (item.kind === 'script') { await this.runScript({ script: '(scratchpad)', function: '', source: item.source }, null, '', false, false); return; }
      const { element, event, value, repeat } = item, screen = item.ui;
      const ev = (element ? element.events : screen.events)[event]; if (!ev) return;
      this.hook('onEvent', { screen: screen.id, element: element ? element.id : '', event, value, assigned: true });
      // Physics events only exist here (no server side), so their actions get ${event_value} before they run.
      // Other events keep Minecraft's behaviour, where it is set for the server side only.
      if (element && CONTACT_EVENTS.has(event)) { this.state.event_value = value; this.state.event_location = element.id + '/' + event; }
      for (const a of ev.client.actions) { try { this.clientAction(a); } catch (ex) { this.log('warn', 'Client action ' + a.type + ': ' + ex.message); } }
      if (ev.client.script) await this.runScript(ev.client, element, value, false, !!repeat);
      if (this.ui !== screen || event === 'close') return;
      // Server side: element events and open. Tick, key, hover and close run only on the player's screen.
      if (element ? !['hover', 'mouse_enter', 'mouse_leave'].includes(event) : event === 'open') {
        if (element) { this.state.event_value = value; this.state.event_location = element.id + '/' + event; }
        await this.serverActions(ev.server, element, event, value);
        if (this.ui !== screen) return;
        if (ev.server.script) await this.runScript(ev.server, element, value, true, false);
      }
      if (this.ui !== screen) return;
      for (const a of ev.client.actions) { if (a.type === 'open_ui') { await this.open(a.value); return; } if (a.type === 'close_ui') { await this.close(); return; } }
    }
    clientAction(a) {
      const target = elementOf(this.ui, a.target), value = bind(a.value, this.state);
      switch (a.type) {
        case 'set_text': if (target) target.text = value; break;
        case 'set_item': if (target && target.type === 'item') target.item = value; break;
        case 'set_visible': if (target) target.visible = value === 'true'; break;
        case 'set_enabled': if (target) target.enabled = value === 'true'; break;
        case 'set_value': if (target) { target.value = value; this.soundValue(target, value); } break;
        case 'change_texture': if (target) target.texture = value; break;
        case 'set_variable': this.state[a.target] = value; break;
        case 'toggle_variable': this.state[a.target] = String(this.state[a.target] !== 'true'); break;
        case 'message': this.message(value); break;
        case 'play_sound': this.playSound(value); break;
        case 'open_ui': case 'close_ui': break; // navigation happens after the event, as in Minecraft
        default: this.log('warn', 'Unknown client action ' + a.type);
      }
    }
    info(element, event, value) { return { screen: this.ui.id, element: element ? element.id : '', event, value, state: this.state, app: this.api() }; }
    async serverActions(handler, element, event, value) {
      const screen = this.ui;
      for (const a of handler.actions) {
        if (this.ui !== screen) return;
        switch (a.type) {
          case 'message': this.message(bind(a.value, this.state)); break;
          case 'set_variable': this.state[a.target] = bind(a.value, this.state); break;
          case 'toggle_variable': this.state[a.target] = String(this.state[a.target] !== 'true'); break;
          case 'open_ui': await this.open(a.value); return;
          case 'close_ui': await this.close(); return;
          case 'command': if (this.host.onCommand) this.hook('onCommand', bind(a.value, this.state), this.info(element, event, value)); else this.log('info', 'Command (add onCommand in host.js to handle it): ' + a.value); break;
          case 'server_function': if (this.host.onServerFunction) this.hook('onServerFunction', a.target, a.value, this.info(element, event, value)); else this.log('info', 'Server function (add onServerFunction in host.js): ' + a.target); break;
          default: if (this.host.onServerAction) this.hook('onServerAction', a, this.info(element, event, value)); else this.log('info', 'Server action not available outside Minecraft: ' + a.type);
        }
      }
    }
    // Scripts use the same API as in Minecraft (api/ClientJavaScript.java) and run in a separate worker with the same
    // 2-second limit, so a script stuck in a loop is stopped instead of freezing the app. Each call starts fresh, and
    // the UI changes it asks for are applied when it finishes (none if it fails).
    async runScript(handler, element, value, isServer, repeat) {
      const source = handler.source !== undefined ? handler.source : this.project.scripts[handler.script];
      if (source === undefined) { this.log('warn', 'Missing script ' + handler.script); return; }
      const name = handler.script + (handler.function ? ' :: ' + handler.function : ''), screen = this.ui;
      const player = isServer ? (this.host.player || { name: 'Player', uuid: '00000000-0000-0000-0000-000000000000', position: { x: 0, y: 0, z: 0, dimension: 'app' }, inventory: [], permission: 0 }) : null;
      this.hook('onScript', { script: handler.script, function: handler.function, server: isServer });
      const inputs = {}; for (const n of this.inputsDown || []) inputs[n] = true;
      // Only what changed since the script side last heard: it keeps its own copy of the screen (scriptDelta).
      this.scripts.prepare();
      const started = this.prof ? performance.now() : 0;
      const delta = this.scriptDelta();
      const result = await this.scripts.run(Object.assign(delta, { source, sourceKey: this.sourceKey(handler, source), fn: handler.function || '', isServer, element: element ? element.id : '', value: String(value ?? ''), repeat: !!repeat, player, maxOps: this.limits.scriptOps, spawnSeq: this.instanceSeq, inputs, axes: Object.assign({}, this.inputAxes), touch: !!this.isTouch, touching: this.touchingMap ? this.touchingMap() : {} }));
      for (const line of result.logs || []) this.log(line.level, '[' + handler.script + '] ' + line.text);
      if (result.error) { this.log('error', 'Script ' + name + ': ' + result.error); return; }
      if (started) queueMicrotask(() => this.profileScript(performance.now() - started, result.ops.length));
      for (const [type, target, v] of result.ops) {
        if (this.ui !== screen) break;
        const e = elementOf(this.ui, target);
        switch (type) {
          case 'set_text': if (e) e.text = v; break;
          case 'set_value': if (e) { e.value = v; this.soundValue(e, v); } break;
          case 'set_item': if (e && e.type === 'item') e.item = v; break;
          case 'set_visible': if (e) e.visible = v === 'true'; break;
          case 'set_enabled': if (e) e.enabled = v === 'true'; break;
          case 'change_texture': if (e) e.texture = v; break;
          case 'set_variable': this.state[target] = v; if (this.scriptSent && this.scriptSent.ui === screen) this.scriptSent.vars.set(target, v); break;
          case 'message': this.message(v); break;
          case 'play_sound': this.playSound(v, target === '' ? undefined : Number(target)); break;
          case 'close_ui': await this.close(); return;
          case 'open_ui': await this.open(v); return;
          case 'command': if (this.host.onCommand) this.hook('onCommand', v, this.info(element, '', value)); else this.log('info', 'Command (add onCommand in host.js to handle it): ' + v); break;
          default: this.extraOp(type, target, v, e);
        }
      }
    }
    extraOp(type, target, value, element) {
      const nums = value.split(',').map(Number);
      switch (type) {
        case 'set_velocity': if (element && nums.every(Number.isFinite)) this.velocity.set(element.id, { x: nums[0], y: nums[1] }); return;
        case 'set_position': if (element && nums.every(Number.isFinite)) this.moveElement(element, nums[0] - element.bounds.x, nums[1] - element.bounds.y); return;
        case 'set_volume': if (element && element.type === 'sound' && Number.isFinite(nums[0])) { element.volume = Math.min(1, Math.max(0, nums[0])); const a = this.playingSounds && this.playingSounds.get(element.id); if (a) a.volume = element.volume * (this.host.volume ?? 1); } return;
        case 'set_size': if (element && nums.every(Number.isFinite)) { element.bounds.width = Math.min(16384, Math.max(1, nums[0])); element.bounds.height = Math.min(16384, Math.max(1, nums[1])); } return;
        case 'spawn': { let spec = null; try { spec = JSON.parse(value); } catch (ex) { } if (spec) this.spawnInstance(target, spec); return; }
        case 'despawn': {
          const member = elementOf(this.ui, target);
          if (member && member._memberOf) { this.log('warn', 'despawn: ' + target + ' is part of the spawned component ' + member._memberOf + '; despawn that'); return; }
          if (!this.despawnInstance(target)) this.log('warn', 'despawn: ' + target + ' is not a spawned object');
          return;
        }
        case 'separate': {
          // A copy: its spacing. A template: all its live copies and the ones spawned later.
          const px = Math.max(0, Number(value) || 0), inst = this.instances.get(target);
          if (inst) inst.separate = px;
          else if (element || this.components[target]) { if (!this.separateDefaults) this.separateDefaults = new Map(); this.separateDefaults.set(target, px); for (const i of this.instances.values()) if (i.template === target) i.separate = px; }
          return;
        }
        case 'seek': {
          const inst = this.instances.get(target); if (!inst) return;
          const parts = value.split(','); inst.seek = parts[0] || ''; inst.speed = Number(parts[1]) || 0; inst.path = parts[2] || '';
          if (!inst.seek) this.velocity.set(target, { x: 0, y: 0 });
          return;
        }
        case 'emit_particles': this.emitParticles(target); return;
        case 'stop_particles': this.stopParticles(target, value === 'clear'); return;
        case 'play_animation': this.playAnimation(target); return;
        case 'stop_animation': this.playing.delete(target); return;
        case 'set_shader_value': { const number = Number(value); if (Number.isFinite(number)) this.glLayer.values.set(String(target), number); return; }
        case 'set_state': {
          const graph = (this.ui.stateGraphs || []).find(g => g.id === target);
          const to = graph && elementOf(this.ui, graph.target);
          if (graph && to && graph.states.some(st => st.name === value)) this.setGraphState(graph, to, value);
          else this.log('warn', 'setState: no state "' + value + '" in graph ' + target);
          return;
        }
        case 'set_tile': if (element && element.type === 'tilemap' && nums.length === 3 && nums.every(Number.isFinite)) this.setTileAt(element, nums[0], nums[1], nums[2]); return;
        case 'fill_tiles':
          if (element && element.type === 'tilemap' && nums.length === 5 && nums.every(Number.isFinite))
            for (let r = nums[1]; r < nums[1] + nums[3]; r++) for (let c = nums[0]; c < nums[0] + nums[2]; c++) this.setTileAt(element, c, r, nums[4]);
          return;
      }
      this.log('warn', 'Unsupported script action ' + type);
    }
    playSound(sound, volume) { this.hook('onSound', sound); this.playAudio(sound, volume); }
    // What host.js can use to drive the UI from outside (for example after a fetch), and what Preview uses.
    api() {
      const self = this;
      return {
        get screen() { return self.ui.id; }, get busy() { return self.busy || self.queue.length > 0; },
        open: id => self.request({ kind: 'open', id }), close: () => self.request({ kind: 'close' }),
        getVariable: n => self.state[n] ?? '', setVariable: (n, v) => { self.state[n] = String(v); },
        setText: (id, v) => { const e = elementOf(self.ui, id); if (e) e.text = String(v); },
        setValue: (id, v) => { const e = elementOf(self.ui, id); if (e) e.value = String(v); },
        setVisible: (id, v) => { const e = elementOf(self.ui, id); if (e) e.visible = !!v; },
        setEnabled: (id, v) => { const e = elementOf(self.ui, id); if (e) e.enabled = !!v; },
        message: t => self.message(t), fire: (id, event, value) => { const e = id ? elementOf(self.ui, id) : null; self.enqueue(e, event, value ?? ''); },
        idle: () => self.idle(),
        snapshot: () => ({ screen: self.ui.id, variables: Object.assign({}, self.state), elements: clone(self.ui.elements) }),
        // Like a player doing it: checkboxes, text boxes, sliders and dropdowns take the value first.
        testEvent: (id, event, value) => {
          const e = id ? elementOf(self.ui, id) : null; value = String(value ?? (e ? e.value : ''));
          if (e && e.type === 'checkbox' && (event === 'checked' || event === 'unchecked')) e.value = value = String(event === 'checked');
          else if (e && event === 'value_changed') e.value = value;
          else if (e && e.type === 'textbox' && (event === 'text_changed' || event === 'submit')) e.value = value;
          self.enqueue(e, event, value);
        },
        runScript: source => self.request({ kind: 'script', source: String(source) }),
        setViewport: (width, height) => { self.viewport = width > 0 && height > 0 ? { width, height } : null; self.layout(); },
        // The live profiler: an overlay of frame, engine, draw and script times and what's on screen. profile() gives
        // the latest figures (null while it's off or before its first quarter second).
        setProfiler: on => self.setProfiler(!!on),
        profile: () => (self.prof && self.prof.figures ? Object.assign({}, self.prof.figures) : null)
      };
    }
  }


  // ---- Sprites, shapes, sounds, inputs, animations and physics ----
  // Sprites and shapes also work in Minecraft; the rest (Advanced tools) are web and desktop only.
  const SHAPES = {
    rectangle: [[0, 0], [1, 0], [1, 1], [0, 1]], triangle: [[0.5, 0], [1, 1], [0, 1]], diamond: [[0.5, 0], [1, 0.5], [0.5, 1], [0, 0.5]],
    hexagon: [[0.25, 0], [0.75, 0], [1, 0.5], [0.75, 1], [0.25, 1], [0, 0.5]],
    ellipse: Array.from({ length: 48 }, (_, i) => [0.5 + 0.5 * Math.cos(i * Math.PI / 24), 0.5 + 0.5 * Math.sin(i * Math.PI / 24)]),
    star: Array.from({ length: 10 }, (_, i) => { const r = i % 2 === 0 ? 0.5 : 0.2, a = -Math.PI / 2 + i * Math.PI / 5; return [0.5 + r * Math.cos(a), 0.5 + r * Math.sin(a)]; })
  };
  // "idle: 0; run: 1-6 @12; jump: 7,8,9 @8 once" (SpriteClips.cs)
  // Sprite clip lists are re-read every frame while a sprite plays, so parsed lists are kept per clip text.
  const clipCache = new Map();
  function parseClips(text) {
    let cached = clipCache.get(text);
    if (!cached) { if (clipCache.size > 256) clipCache.clear(); cached = parseClipList(text); clipCache.set(text, cached); }
    return cached;
  }
  function parseClipList(text) {
    const clips = {}; if (!text) return clips;
    for (const part of String(text).split(';').map(s => s.trim()).filter(Boolean)) {
      const m = /^([A-Za-z_][A-Za-z0-9_]{0,63})\s*:\s*([0-9\s,-]+?)\s*(?:@\s*([0-9]{1,3}(?:\.[0-9]+)?))?\s*(once)?$/.exec(part); if (!m) continue;
      const frames = [];
      for (const piece of m[2].split(',').map(s => s.trim()).filter(Boolean)) {
        const range = piece.split('-').map(s => parseInt(s.trim(), 10)); const a = range[0], b = range.length > 1 ? range[1] : a;
        for (let f = a; a <= b ? f <= b : f >= b; f += a <= b ? 1 : -1) { frames.push(f); if (frames.length > 1024) break; }
      }
      if (frames.length) clips[m[1]] = { frames, fps: m[3] ? Math.min(120, Math.max(0.1, parseFloat(m[3]))) : 8, loop: !m[4] };
    }
    return clips;
  }
  // Collider points with curve handles become straight edges (Colliders.cs).
  function flattenPoints(points) {
    const out = [], n = points.length;
    for (let i = 0; i < n; i++) {
      const p = points[i]; if (p.curve) continue; out.push([p.x, p.y]);
      const next = points[(i + 1) % n];
      if (next && next.curve) {
        let end = p; for (let k = 1; k <= n; k++) { const v = points[(i + k) % n]; if (!v.curve) { end = v; break; } }
        for (let s = 1; s < 8; s++) { const t = s / 8, u = 1 - t; out.push([u * u * p.x + 2 * u * t * next.x + t * t * end.x, u * u * p.y + 2 * u * t * next.y + t * t * end.y]); }
      }
    }
    return out;
  }
  // Splits a simple polygon into triangles (ear clipping), so concave outlines collide correctly.
  function triangulate(poly) {
    const pts = poly.slice(); let area = 0; for (let i = 0; i < pts.length; i++) { const a = pts[i], b = pts[(i + 1) % pts.length]; area += a[0] * b[1] - b[0] * a[1]; }
    if (area < 0) pts.reverse();
    const cross = (a, b, c) => (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
    const inside = (p, a, b, c) => cross(a, b, p) >= 0 && cross(b, c, p) >= 0 && cross(c, a, p) >= 0;
    const tris = []; let guard = 0;
    while (pts.length > 3 && guard++ < 10000) {
      let clipped = false;
      for (let i = 0; i < pts.length; i++) {
        const a = pts[(i + pts.length - 1) % pts.length], b = pts[i], c = pts[(i + 1) % pts.length];
        if (cross(a, b, c) <= 0) continue;
        if (pts.some(p => p !== a && p !== b && p !== c && inside(p, a, b, c))) continue;
        tris.push([a, b, c]); pts.splice(i, 1); clipped = true; break;
      }
      if (!clipped) break;
    }
    if (pts.length === 3) tris.push(pts);
    return tris;
  }
  const PAD_BUTTONS = ['a', 'b', 'x', 'y', 'lb', 'rb', 'lt', 'rt', 'back', 'start', 'ls', 'rs', 'dpad_up', 'dpad_down', 'dpad_left', 'dpad_right', 'home'];
  const PAD_AXES = { left_x: 0, left_y: 1, right_x: 2, right_y: 3 };
  const EASE = { linear: t => t, ease_in: t => t * t, ease_out: t => 1 - (1 - t) * (1 - t), ease_in_out: t => t < 0.5 ? 2 * t * t : 1 - Math.pow(-2 * t + 2, 2) / 2, step: t => t < 1 ? 0 : 1 };
  function trackValue(track, time) {
    const keys = track.keys.slice().sort((a, b) => a.time - b.time); if (!keys.length) return null;
    if (time <= keys[0].time) return keys[0].value;
    for (let i = 1; i < keys.length; i++) if (time <= keys[i].time) { const a = keys[i - 1], b = keys[i]; const t = b.time === a.time ? 1 : (time - a.time) / (b.time - a.time); return a.value + (b.value - a.value) * (EASE[b.ease] || EASE.linear)(t); }
    return keys[keys.length - 1].value;
  }

  Object.assign(App.prototype, {
    resetGame() {
      // Music carries on across screens: a looping sound still playing continues, from where it is, into a new screen
      // that autoplays the same sound on a looping Sound control, instead of stopping and starting again.
      const carried = new Map();
      if (this.playingSounds && this.project.sounds) for (const e of this.ui.elements) {
        if (e.type !== 'sound' || !e.autoplay || !e.loop || !e.sound || e.delay > 0) continue;
        const url = this.project.sounds[e.sound];
        for (const [id, a] of this.playingSounds) if (a.loop && !a.paused && a.wysicraftUrl === url && ![...carried.values()].includes(a)) { carried.set(e.id, a); this.playingSounds.delete(id); break; }
      }
      this.stopAllSounds(); this.resetParticles();
      for (const [id, a] of carried) {
        const e = this.ui.elements.find(x => x.id === id);
        if (!this.controlAudio) this.controlAudio = new Map();
        this.controlAudio.set(id, a); this.playingSounds.set(id, a); a.volume = Math.min(1, Math.max(0, e.volume)) * (this.host.volume ?? 1);
      }
      for (const e of this.ui.elements) if (e.type === 'sound' && e.autoplay && e.sound && !carried.has(e.id)) this.soundTimers.push(setTimeout(() => this.startSound(e), Math.max(0, e.delay)));
      for (const e of this.ui.elements) if (e.type === 'particles' && e.autoplay && e.effect) this.emitParticles(e.id);
      this.separateDefaults = new Map(); this.instances = new Map(); this.instanceSeq = 1; this.instanceTime = performance.now(); this.instancesVersion = 0; this.instanceCapWarned = false;
      this.spriteClock = new Map(); this.graphStates = new Map(); this.velocity = new Map(); this.contacts = new Set(); this.overlaps = new Set(); this.stayTimes = new Map(); this.playing = new Map();
      this.virtualInputs = new Set(); this.inputsDown = new Set(); this.inputAxes = {}; this.stick = null; this.physicsTime = performance.now(); this.children = null;
      for (const a of this.ui.animations || []) if (a.autoplay) this.playing.set(a.id, { anim: a, start: performance.now() });
    },
    // Sprites: the clip in value plays from when it was chosen; frames count left to right, top to bottom.
    drawSprite(e, x, y, w, h) {
      const tex = e.texture && this.texture(e.texture); if (!tex) return;
      const clips = parseClips(e.clips), names = Object.keys(clips), clip = clips[e.value] || (e.value ? null : clips[names[0]]);
      const fw = Math.max(1, e.frameWidth), fh = Math.max(1, e.frameHeight), columns = Math.max(1, Math.floor(tex.img.naturalWidth / fw));
      const frame = this.spriteFrame(e), sx = (frame % columns) * fw, sy = Math.floor(frame / columns) * fh;
      // glElement counted this sprite as batched, so it must go into the batch: drawn on the 2D canvas instead, it
      // would land under whatever the batch still holds (a backdrop drawn before it) when that is composited.
      if (this.batched) { this.glMark(); this.glQuad(tex.img, sx, sy, fw, fh, x, y, w, h, null, e.opacity, 0, false); return; }
      const g = this.g; g.save(); g.globalAlpha = e.opacity; this.pixelImage(tex.img, sx, sy, fw, fh, x, y, w, h); g.restore();
    },
    spriteClockFor(e) {
      let clock = this.spriteClock.get(e.id); const now = performance.now();
      if (!clock || clock.value !== e.value) { clock = { value: e.value, start: now }; this.spriteClock.set(e.id, clock); }
      return clock;
    },
    // Which frame of its clip a sprite is on right now, or 0. The drawing uses this, and so does ctx.ui.getElement.
    spriteFrame(e) {
      if (e.type !== 'sprite') return 0;
      const tex = e.texture && this.texture(e.texture); if (!tex) return 0;
      const clips = parseClips(e.clips), names = Object.keys(clips), clip = clips[e.value] || (e.value ? null : clips[names[0]]);
      if (!clip) return 0;
      const step = Math.floor((performance.now() - this.spriteClockFor(e).start) * clip.fps / 1000);
      return clip.frames[clip.loop ? step % clip.frames.length : Math.min(step, clip.frames.length - 1)];
    },
    // Where a sprite is in its clip, 0 to 1, and whether a one-shot clip has finished.
    spriteProgress(e) {
      if (e.type !== 'sprite') return { step: 0, done: true };
      const clips = parseClips(e.clips), names = Object.keys(clips), clip = clips[e.value] || (e.value ? null : clips[names[0]]);
      if (!clip) return { step: 0, done: true };
      const step = Math.floor((performance.now() - this.spriteClockFor(e).start) * clip.fps / 1000);
      return { step, done: !clip.loop && step >= clip.frames.length - 1 };
    },
    shapePath(shape, x, y, w, h) { const g = this.g, pts = SHAPES[shape] || SHAPES.rectangle; g.beginPath(); pts.forEach((p, i) => i ? g.lineTo(x + p[0] * w, y + p[1] * h) : g.moveTo(x + p[0] * w, y + p[1] * h)); g.closePath(); },
    drawShape(e, x, y, w, h, hover) {
      const g = this.g; g.save();
      if (e.fillEnabled) {
        const tex = e.texture && this.texture(e.texture);
        this.shapePath(e.shape, x, y, w, h);
        if (tex) { g.clip(); this.drawTexture(tex, x, y, w, h, 0, e.opacity); } else { g.fillStyle = color(e.background, e.opacity); g.fill(); }
      }
      g.restore();
      if (hover && e.events.click) { g.save(); this.shapePath(e.shape, x, y, w, h); g.fillStyle = 'rgba(255,255,255,0.133)'; g.fill(); g.restore(); }
      if (e.borderWidth > 0) { g.save(); this.shapePath(e.shape, x, y, w, h); g.strokeStyle = color(e.borderColor, e.opacity); g.lineWidth = e.borderWidth; g.lineJoin = 'round'; g.stroke(); g.restore(); }
      if (e.text) this.text(e, x, y, w, h, bind(e.text, this.state));
    },
    drawCollider(e, x, y, w, h) {
      if (!this.host.showColliders) return; // invisible in apps; Preview can outline them
      const g = this.g; g.save(); g.beginPath();
      if (e.collider === 'circle') g.ellipse(x + w / 2, y + h / 2, w / 2, h / 2, 0, 0, Math.PI * 2);
      else if (e.collider === 'polygon' && e.colliderPoints.length >= 3) flattenPoints(e.colliderPoints).forEach((p, i) => i ? g.lineTo(x + p[0], y + p[1]) : g.moveTo(x + p[0], y + p[1]));
      else g.rect(x, y, w, h);
      g.closePath(); g.fillStyle = 'rgba(20,200,255,0.15)'; g.fill(); g.strokeStyle = 'rgba(20,200,255,0.8)'; g.setLineDash([3, 2]); g.lineWidth = 1; g.stroke(); g.restore();
    },

    // Sounds: the project's own (assets/<ns>/sounds/<name>.ogg|mp3|wav|m4a) play here; others go to host.onSound.
    // Sound controls: Delay after the screen opens (when Autoplay), Volume, Play × times and Loop. Setting the
    // control's value to "play" or "stop" (an action or ui.setValue) starts or stops it.
    startSound(e) {
      this.stopSound(e); const url = this.project.sounds && this.project.sounds[e.sound];
      if (!url) { this.hook('onSound', e.sound); return; }
      let left = Math.max(1, e.repeat | 0);
      // One element per Sound control, reused: a new one on every start is how a long run used to pile them up.
      if (!this.controlAudio) this.controlAudio = new Map();
      let audio = this.controlAudio.get(e.id);
      if (!audio || audio.wysicraftUrl !== url) {
        audio = new Audio(url); audio.wysicraftUrl = url; this.controlAudio.set(e.id, audio);
        audio.addEventListener('ended', () => { if (--audio.wysicraftLeft > 0 && this.playingSounds.get(e.id) === audio) { audio.currentTime = 0; audio.play().catch(() => { }); } });
      }
      audio.wysicraftLeft = left; audio.currentTime = 0;
      audio.volume = Math.min(1, Math.max(0, e.volume)) * (this.host.volume ?? 1); audio.loop = !!e.loop;
      this.playingSounds.set(e.id, audio);
      const p = audio.play();
      // Blocked because nobody has touched the page yet (the title theme): it starts at the first click, tap or key.
      if (p && p.catch) p.catch(err => { if (err && err.name === 'NotAllowedError') { (this.blockedSounds || (this.blockedSounds = [])).push({ id: e.id, audio }); this.unlockAudio(); } });
    },
    stopSound(e) { const a = this.playingSounds.get(e.id); if (a) { a.pause(); this.playingSounds.delete(e.id); } },
    stopAllSounds() { this.blockedSounds = []; if (!this.playingSounds) { this.playingSounds = new Map(); this.soundTimers = []; } for (const t of this.soundTimers) clearTimeout(t); this.soundTimers = []; for (const a of this.playingSounds.values()) a.pause(); this.playingSounds.clear(); },
    soundValue(e, v) {
      if (e && e.type === 'sound') { if (v === 'play') this.startSound(e); else if (v === 'stop') this.stopSound(e); }
      if (e && e.type === 'particles') { if (v === 'play') this.emitParticles(e.id); else if (v === 'stop') this.stopParticles(e.id, false); else if (v === 'clear') this.stopParticles(e.id, true); }
    },
    // One-shot sounds (play_sound actions, ctx.client.playSound) are decoded once and played through Web Audio, which
    // has no limit on how many play. A new audio element for every hit ran into the browser's cap on media players
    // (a few dozen), after which effects went silent for the rest of the run while music already playing carried on.
    // Where Web Audio cannot load a sound (a page opened from disk cannot fetch its files) a few elements per sound
    // are reused instead.
    playAudio(sound, level) {
      const url = this.project.sounds && this.project.sounds[sound];
      if (!url) return;
      const volume = (this.host.volume ?? 1) * (Number.isFinite(level) ? Math.min(1, Math.max(0, level)) : 1), entry = this.soundBuffer(url), ac = this.audioCtx;
      if (entry.state === 'ready' && ac) {
        if (ac.state !== 'running') { this.unlockAudio(); return; } // would only play late, all at once, after the first click
        const source = ac.createBufferSource(), gain = ac.createGain();
        source.buffer = entry.buffer; gain.gain.value = volume; source.connect(gain); gain.connect(ac.destination); source.start();
        return;
      }
      this.playPooled(url, volume); // still decoding, or Web Audio could not load it
    },
    audioContext() {
      if (this.audioCtx === undefined) { try { const C = window.AudioContext || window.webkitAudioContext; this.audioCtx = C ? new C() : null; } catch (ex) { this.audioCtx = null; } }
      return this.audioCtx;
    },
    // The sounds some Sound control plays, on any screen.
    streamedSounds() {
      const ids = new Set();
      for (const screen of Object.values(this.project.screens || {})) for (const e of (screen && screen.elements) || []) if (e.type === 'sound' && e.sound) ids.add(e.sound);
      return ids;
    },
    soundBuffer(url) {
      if (!this.soundBuffers) this.soundBuffers = new Map();
      let entry = this.soundBuffers.get(url);
      if (entry) return entry;
      entry = { state: 'loading', buffer: null }; this.soundBuffers.set(url, entry);
      const ac = this.audioContext();
      if (!ac || typeof fetch !== 'function') { entry.state = 'pooled'; return entry; }
      fetch(url).then(r => { if (!r.ok) throw new Error('HTTP ' + r.status); return r.arrayBuffer(); })
        .then(data => { if (data.byteLength > SOUND_DECODE_MAX) throw new Error('long'); return data; })
        .then(data => new Promise((ok, fail) => { const done = ac.decodeAudioData(data, ok, fail); if (done && done.then) done.then(ok, fail); }))
        .then(buffer => { entry.buffer = buffer; entry.state = 'ready'; })
        .catch(() => { entry.state = 'pooled'; });
      return entry;
    },
    playPooled(url, volume) {
      if (!this.soundPools) this.soundPools = new Map();
      let pool = this.soundPools.get(url); if (!pool) this.soundPools.set(url, pool = []);
      let audio = pool.find(a => a.paused || a.ended);
      if (!audio) { if (pool.length < 4) { audio = new Audio(url); pool.push(audio); } else { audio = pool.shift(); pool.push(audio); } }
      try { audio.volume = volume; audio.currentTime = 0; const p = audio.play(); if (p && p.catch) p.catch(err => { if (err && err.name === 'NotAllowedError') this.unlockAudio(); }); } catch (ex) { }
    },
    // Browsers keep a page silent until the person clicks, taps or presses a key on it. The first time that happens,
    // Web Audio is resumed and any Sound control that was blocked (a title theme) starts.
    unlockAudio() {
      if (this.audioUnlockWaiting) return; this.audioUnlockWaiting = true;
      const kinds = ['pointerdown', 'keydown', 'touchstart'];
      const go = () => {
        kinds.forEach(k => window.removeEventListener(k, go, true)); this.audioUnlockWaiting = false;
        if (this.audioCtx && this.audioCtx.state === 'suspended') this.audioCtx.resume().catch(() => { });
        const waiting = this.blockedSounds || []; this.blockedSounds = [];
        for (const { id, audio } of waiting) if (this.playingSounds && this.playingSounds.get(id) === audio) { const p = audio.play(); if (p && p.catch) p.catch(() => { }); }
      };
      kinds.forEach(k => window.addEventListener(k, go, true));
    },

    // Inputs: keys, gamepad buttons and sticks, and on-screen controls with "Presses input".
    keyHeld(name, down) { if (!this.keyNamesDown) this.keyNamesDown = new Set(); if (down) this.keyNamesDown.add(name); else this.keyNamesDown.delete(name); },
    virtualPress(input, down) { if (!input) return; if (down) this.virtualInputs.add(input); else this.virtualInputs.delete(input); },
    // How far the stick is pushed along one named direction, 0 to 1. STICK_REACH is how far the thumb travels for full
    // tilt; past that it stays at full rather than needing the whole screen.
    stickPush(action) {
      if (!this.stick || !action) return 0;
      const dx = this.stick.x - this.stick.ox, dy = this.stick.y - this.stick.oy;
      const d = action === 'drag_left' ? -dx : action === 'drag_right' ? dx : action === 'drag_up' ? -dy : action === 'drag_down' ? dy : 0;
      const s = d / STICK_REACH;
      return s > 0.12 ? Math.min(1, s) : 0;
    },
    pollInputs() {
      const inputs = this.project.inputs || []; if (!inputs.length || !this.ui) return;
      let pads = []; try { pads = navigator.getGamepads ? [...navigator.getGamepads()].filter(Boolean) : []; } catch (ex) { }
      const down = new Set(), axes = {}, keys = this.keyNamesDown || new Set();
      for (const input of inputs) {
        let pressed = input.keys.some(k => keys.has(k)) || this.virtualInputs.has(input.name), strength = pressed ? 1 : 0;
        // pad -1 (the default) listens to every controller; 0-3 listens to one, so two players do not share inputs.
        for (const pad of (input.pad === undefined || input.pad < 0 ? pads : pads.filter(g => g.index === input.pad))) {
          for (const b of input.buttons) { const i = PAD_BUTTONS.indexOf(b), btn = pad.buttons[i]; if (btn && (btn.pressed || btn.value > 0.5)) { pressed = true; strength = Math.max(strength, btn.value || 1); } }
          if (input.axis) { const m = /^(left|right)_(x|y)([+-])$/.exec(input.axis); if (m) { const v = pad.axes[PAD_AXES[m[1] + '_' + m[2]]] || 0; const s = m[3] === '-' ? -v : v; if (s > 0.15) strength = Math.max(strength, s); if (s > 0.5) pressed = true; } }
        }
        const push = this.stickPush(input.touch);
        if (push > 0) { strength = Math.max(strength, push); if (push > 0.35) pressed = true; }
        axes[input.name] = Math.min(1, strength); if (pressed) down.add(input.name);
      }
      for (const name of down) if (!this.inputsDown.has(name)) this.enqueueInput('input_pressed', name);
      for (const name of this.inputsDown) if (!down.has(name)) this.enqueueInput('input_released', name);
      this.inputsDown = down; this.inputAxes = axes;
      // A control that presses an input also fires its click, like a button.
    },
    enqueueInput(event, name) { if (!this.ui.events[event]) return; if (this.queue.length >= 32) return; this.queue.push({ ui: this.ui, element: null, event, value: name, repeat: false }); this.pump(); },

    // Animations: keyframe tracks applied every frame; non-looping ones fire animation_end when done.
    playAnimation(id) { const a = (this.ui.animations || []).find(x => x.id === id); if (a) this.playing.set(id, { anim: a, start: performance.now() }); else this.log('warn', 'Unknown animation ' + id); },
    // Sprite state graphs: which clip a sprite is playing, and what moves it to another. One transition per graph
// per frame, highest priority first, so a chain of conditions that are all true can't spin.
    stepStateGraphs() {
      const graphs = this.ui.stateGraphs; if (!graphs || !graphs.length) return;
      const scope = conditionScope(this);   // the variables and held inputs are the same for every graph this frame
      for (const graph of graphs) {
        const target = elementOf(this.ui, graph.target);
        if (!target || target.type !== 'sprite') continue;
        let current = this.graphStates.get(graph.id);
        if (current === undefined || !graph.states.some(st => st.name === current)) {
          current = graph.start || (graph.states[0] && graph.states[0].name) || '';
          this.graphStates.set(graph.id, current); this.playState(graph, target, current);
        }
        const state = graph.states.find(st => st.name === current); if (!state) continue;
        const progress = this.spriteProgress(target);
        scope.clipDone = progress.done ? 'true' : 'false';
        scope.clipStep = String(progress.step);
        // A copy, so sorting by priority doesn't reorder the author's list.
        const ways = (state.transitions || []).slice().sort((a, b) => (b.priority || 0) - (a.priority || 0));
        for (const way of ways) {
          if (!way.to || way.to === current || !graph.states.some(st => st.name === way.to)) continue;
          if (!safeEvaluate(String(way.when || '').replace(INPUT_CONDITION, 'input_$1'), scope)) continue;
          this.setGraphState(graph, target, way.to);
          break;
        }
      }
    },
    // Moves a graph to a state and starts its clip from the beginning, then tells the control it changed.
    setGraphState(graph, target, name) {
      if (this.graphStates.get(graph.id) === name) return;
      this.graphStates.set(graph.id, name);
      this.playState(graph, target, name);
      if (target.events && target.events.state_changed) this.enqueue(target, 'state_changed', name);
    },
    playState(graph, target, name) {
      const state = graph.states.find(st => st.name === name);
      if (!state || !state.clip) return;
      target.value = state.clip;
      this.spriteClock.delete(target.id);   // the new clip starts at its first frame, not part way through
    },
    // The state a control is in, from the first graph driving it.
    stateOf(id) {
      for (const graph of this.ui.stateGraphs || []) if (graph.target === id) return this.graphStates.get(graph.id) || '';
      return '';
    },
    stepAnimations(now) {
      for (const [id, p] of this.playing) {
        let t = now - p.start, done = false;
        if (t >= p.anim.duration) { if (p.anim.loop) t = t % p.anim.duration; else { t = p.anim.duration; done = true; } }
        for (const track of p.anim.tracks) {
          const e = elementOf(this.ui, track.target), v = e && trackValue(track, t); if (v === null || v === undefined || !e) continue;
          if (track.property === 'opacity') e.opacity = Math.min(1, Math.max(0, v));
          else if (track.property === 'width') e.bounds.width = Math.max(1, v); else if (track.property === 'height') e.bounds.height = Math.max(1, v);
          else { const dx = track.property === 'x' ? v - e.bounds.x : 0, dy = track.property === 'y' ? v - e.bounds.y : 0; this.moveElement(e, dx, dy); }
        }
        if (done) { this.playing.delete(id); if (this.ui.events.animation_end) { this.queue.push({ ui: this.ui, element: null, event: 'animation_end', value: id, repeat: false }); this.pump(); } }
      }
    },
    // ---- Particles ----
    // Effects are named on the manifest; a Particles control plays one. Particles live in screen coordinates, like
    // element bounds, so an emitter that moves leaves the ones it already threw behind it. They are kept per emitter
    // so drawing one costs only its own, and the whole screen shares one budget.
    effectOf(e) { return e && e.effect ? ((this.project.particles || []).find(f => f.id === e.effect) || null) : null; },
    // Do these two bodies collide at all? The project's matrix decides which layers meet; each body may then narrow
    // that with its own collidesWith. An empty matrix means every layer meets every layer, as it did before layers.
    layersMeet(a, b) {
      const matrix = this.project.collisionMatrix;
      if (matrix && matrix.length) {
        const la = a.layer || 0, lb = b.layer || 0;
        const ra = matrix[la] === undefined ? -1 : matrix[la], rb = matrix[lb] === undefined ? -1 : matrix[lb];
        if ((ra & (1 << lb)) === 0 || (rb & (1 << la)) === 0) return false;
      }
      return layersMeetMasks(a, b);
    },
    resetParticles() { this.particles = new Map(); this.streams = new Map(); this.particleCount = 0; this.particleTime = performance.now(); },
    spawnParticle(owner, fx, x, y) {
      if (this.particleCount >= this.maxParticles) return;
      let list = this.particles.get(owner); if (!list) { list = []; this.particles.set(owner, list); }
      const half = fx.spread / 2 * Math.PI / 180, mid = fx.direction * Math.PI / 180;
      const angle = mid + (Math.random() * 2 - 1) * half;
      const speed = fx.speed * (1 + (Math.random() * 2 - 1) * fx.speedVariance);
      const from = fx.radius > 0 ? Math.sqrt(Math.random()) * fx.radius : 0, around = Math.random() * Math.PI * 2;
      list.push({
        x: x + Math.cos(around) * from, y: y + Math.sin(around) * from,
        vx: Math.cos(angle) * speed, vy: Math.sin(angle) * speed, age: 0,
        life: Math.max(0.02, fx.life * (1 + (Math.random() * 2 - 1) * fx.lifeVariance)),
        size: 1 + (Math.random() * 2 - 1) * fx.sizeVariance, angle: Math.random() * Math.PI * 2, spin: fx.spin * Math.PI / 180
      });
      this.particleCount++;
    },
    // Fires a burst, or starts a stream running. A stream with no duration runs until something stops it.
    emitParticles(id) {
      const e = elementOf(this.ui, id);
      if (!e || e.type !== 'particles') { this.log('warn', 'Not a Particles control: ' + id); return; }
      const fx = this.effectOf(e);
      if (!fx) { this.log('warn', 'Unknown particle effect: ' + (e.effect || '(none)')); return; }
      if (fx.emission === 'burst') { const at = this.emitPoint(e); for (let i = 0; i < fx.count; i++) this.spawnParticle(id, fx, at.x, at.y); }
      else this.streams.set(id, { until: fx.duration > 0 ? performance.now() + fx.duration * 1000 : Infinity, carry: 0 });
    },
    stopParticles(id, clear) { this.streams.delete(id); if (clear) { const l = this.particles.get(id); if (l) { this.particleCount -= l.length; this.particles.delete(id); } } },
    emitPoint(e) { return { x: this.x(e) + e.bounds.width / 2, y: this.y(e) + e.bounds.height / 2 }; },
    stepParticles(now) {
      const dt = Math.min(0.1, (now - this.particleTime) / 1000); this.particleTime = now;
      if (dt <= 0) return;
      for (const [id, stream] of this.streams) {
        const e = elementOf(this.ui, id), fx = this.effectOf(e);
        if (!fx || !e || !this.visible(e) || now > stream.until) { this.streams.delete(id); continue; }
        stream.carry += fx.count * dt;
        const n = Math.min(Math.floor(stream.carry), this.maxParticles); stream.carry -= n;
        const at = this.emitPoint(e);
        for (let i = 0; i < n; i++) this.spawnParticle(id, fx, at.x, at.y);
      }
      for (const [id, list] of this.particles) {
        const fx = this.effectOf(elementOf(this.ui, id));
        if (!fx) { this.particleCount -= list.length; this.particles.delete(id); continue; }
        let n = 0;
        for (let i = 0; i < list.length; i++) {
          const q = list[i];
          q.age += dt; if (q.age >= q.life) continue;
          const keep = Math.max(0, 1 - fx.drag * dt);
          q.vx *= keep; q.vy = q.vy * keep + fx.gravity * dt;
          q.x += q.vx * dt; q.y += q.vy * dt; q.angle += q.spin * dt;
          list[n++] = q;
        }
        this.particleCount -= list.length - n; list.length = n;
        if (!n && !this.streams.has(id)) this.particles.delete(id);
      }
    },
    drawParticles(e) {
      const list = this.particles.get(e.id); if (!list || !list.length) return;
      const fx = this.effectOf(e); if (!fx) return;
      const g = this.g, ramp = rampOf(fx);
      const blend = g.globalCompositeOperation;
      if (fx.blend === 'add') g.globalCompositeOperation = 'lighter';
      const tex = fx.shape === 'texture' ? this.texture(fx.texture) : null;
      if (this.batched) { g.globalCompositeOperation = blend; this.glParticles(list, fx, ramp, tex); return; }
      for (let i = 0; i < list.length; i++) {
        const q = list[i], t = Math.min(1, q.age / q.life);
        const size = Math.max(0, (fx.sizeStart + (fx.sizeEnd - fx.sizeStart) * t) * q.size);
        const alpha = fx.opacityStart + (fx.opacityEnd - fx.opacityStart) * t;
        if (size <= 0.05 || alpha <= 0.004) continue;
        g.globalAlpha = Math.min(1, alpha);
        const rgb = colourAt(ramp, t);
        const colour = 'rgb(' + (rgb[0] | 0) + ',' + (rgb[1] | 0) + ',' + (rgb[2] | 0) + ')';
        if (tex) {
          g.save(); g.translate(q.x, q.y); if (q.spin) g.rotate(q.angle);
          g.drawImage(tex.img, -size / 2, -size / 2, size, size); g.restore();
        } else if (fx.shape === 'circle') {
          g.fillStyle = colour; g.beginPath(); g.arc(q.x, q.y, size / 2, 0, Math.PI * 2); g.fill();
        } else if (fx.shape === 'line') {
          // Drawn along its own travel, so a fast particle streaks and a slow one barely shows.
          g.strokeStyle = colour; g.lineWidth = Math.max(0.5, size / 2);
          g.beginPath(); g.moveTo(q.x, q.y); g.lineTo(q.x - q.vx * 0.02, q.y - q.vy * 0.02); g.stroke();
        } else if (q.spin) {
          g.save(); g.translate(q.x, q.y); g.rotate(q.angle);
          g.fillStyle = colour; g.fillRect(-size / 2, -size / 2, size, size); g.restore();
        } else { g.fillStyle = colour; g.fillRect(q.x - size / 2, q.y - size / 2, size, size); }
      }
      g.globalAlpha = 1; g.globalCompositeOperation = blend;
    },
    // Moving a panel carries everything attached inside it.
    moveElement(e, dx, dy) {
      if (!dx && !dy) return;
      if (!this.children) { this.children = new Map(); for (const c of this.ui.elements) if (c.parent) { if (!this.children.has(c.parent)) this.children.set(c.parent, []); this.children.get(c.parent).push(c); } }
      const stack = [e], seen = new Set();
      while (stack.length) { const n = stack.pop(); if (seen.has(n.id)) continue; seen.add(n.id); n.bounds.x += dx; n.bounds.y += dy; for (const c of this.children.get(n.id) || []) stack.push(c); }
    },

    // The same particles as drawParticles, as instanced quads: a round particle is one dot texture rather than a
    // path, and a line particle is a thin quad turned along its own travel. Twenty thousand of them are one or two
    // draw calls instead of twenty thousand.
    glParticles(list, fx, ramp, tex) {
      this.glMark();
      const additive = fx.blend === 'add', img = tex ? tex.img : (fx.shape === 'circle' ? this.dot : null);
      const iw = img ? (img.naturalWidth || img.width || 1) : 1, ih = img ? (img.naturalHeight || img.height || 1) : 1;
      for (let i = 0; i < list.length; i++) {
        const q = list[i], t = Math.min(1, q.age / q.life);
        const size = Math.max(0, (fx.sizeStart + (fx.sizeEnd - fx.sizeStart) * t) * q.size);
        const alpha = fx.opacityStart + (fx.opacityEnd - fx.opacityStart) * t;
        if (size <= 0.05 || alpha <= 0.004) continue;
        const rgb = tex ? null : colourAt(ramp, t);
        if (fx.shape === 'line') {
          const dx = q.vx * 0.02, dy = q.vy * 0.02, length = Math.max(size / 2, Math.hypot(dx, dy));
          this.glQuad(null, 0, 0, 1, 1, q.x - length / 2, q.y - Math.max(0.5, size / 4) / 2, length, Math.max(0.5, size / 4),
            rgb, Math.min(1, alpha), Math.atan2(-dy, -dx), additive);
        } else {
          this.glQuad(img, 0, 0, iw, ih, q.x - size / 2, q.y - size / 2, size, size, rgb, Math.min(1, alpha), q.spin ? q.angle : 0, additive);
        }
      }
    },
    // Physics ("arcade": no rotation). Dynamic bodies fall with the screen's gravity, collide with static and
    // kinematic ones (and each other), bounce and slide. Collision events fire when contacts start and end.
    tileGrid(e) {
      if (!this._tiles) this._tiles = new Map();
      let cache = this._tiles.get(e.id);
      const columns = Math.max(1, e.columns | 0), rows = Math.max(1, e.rows | 0);
      if (!cache || (!cache.dirty && cache.text !== e.tiles) || cache.columns !== columns || cache.rows !== rows || cache.solidText !== e.solid) {
        cache = { text: e.tiles, columns, rows, grid: readTiles(e.tiles, columns, rows), solid: solidTiles(e.solid), solidText: e.solid, dirty: false, version: this.tileVersion = (this.tileVersion || 0) + 1 };
        this._tiles.set(e.id, cache);
      }
      return cache;
    },
    // The text form, rebuilt only if a script has written tiles since it was last asked for.
    tilesText(e) {
      const cache = this._tiles && this._tiles.get(e.id);
      if (cache && cache.dirty) { e.tiles = cache.text = writeTiles(cache.grid); cache.dirty = false; }
      return e.tiles;
    },
    setTileAt(e, column, row, index) {
      const cache = this.tileGrid(e);
      if (column < 0 || row < 0 || column >= cache.columns || row >= cache.rows) return false;
      const at = row * cache.columns + column;
      if (cache.grid[at] === index) return true;
      cache.grid[at] = index; cache.dirty = true; cache.version = this.tileVersion = (this.tileVersion || 0) + 1;
      return true;
    },
    shapeOf(e) {
      const x = e.bounds.x, y = e.bounds.y, w = e.bounds.width, h = e.bounds.height;
      if (e.collider === 'circle') return { circle: true, cx: x + w / 2, cy: y + h / 2, r: Math.min(w, h) / 2 };
      if (e.collider === 'polygon' && e.colliderPoints.length >= 3) {
        const key = JSON.stringify(e.colliderPoints); if (!e._tris || e._trisKey !== key) { e._tris = triangulate(flattenPoints(e.colliderPoints)); e._trisKey = key; }
        return { polys: e._tris.map(t => t.map(p => [x + p[0], y + p[1]])) };
      }
      return { polys: [[[x, y], [x + w, y], [x + w, y + h], [x, y + h]]], rect: true };
    },
    // Triggers (element.trigger) notice overlaps but never push or get pushed: pickups, checkpoints, zones.
    // A trigger pair needs something that moves (dynamic or kinematic) so zones sitting still don't report
    // each other when the screen opens.
    stepPhysics(now) {
      const bodies = this.ui.elements.filter(e => (e.body || e.type === 'collider') && e.visible);
      this.physicsCounts.bodies = bodies.length; if (!bodies.length) this.physicsCounts.pairs = 0;
      const active = bodies.some(b => b.body === 'dynamic' || b.trigger);
      let elapsed = active ? Math.min(0.1, (now - this.physicsTime) / 1000) : 0; this.physicsTime = now;
      if (active && elapsed <= 0) return; // no time passed: keep the current contacts rather than ending them all
      if (!active && !this.contacts.size && !this.overlaps.size) return;
      const dt = 1 / 120, gravity = this.ui.gravity || 0, touching = new Set(), overlapping = new Set();
      const maps = this.ui.elements.filter(e => e.type === 'tilemap' && e.visible && e.solid);
      const moves = e => e.body === 'dynamic' || e.body === 'kinematic';
      while (elapsed > 0) {
        const step = Math.min(dt, elapsed); elapsed -= step;
        for (const b of bodies) if (b.body === 'dynamic') { const v = this.velocity.get(b.id) || { x: 0, y: 0 }; v.y += gravity * step; this.velocity.set(b.id, v); this.moveElement(b, v.x * step, v.y * step); }
        // Each body's shape is built once per step instead of once per pairing (it's rebuilt after a hit moves
        // anything), and pairs whose outlines don't overlap skip the full separating-axis test.
        const shapes = bodies.map(() => null);
        const shapeAt = i => shapes[i] || (shapes[i] = boxOf(this.shapeOf(bodies[i])));
        // One pair of bodies: overlap for triggers, a push apart and a bounce for solid bodies.
        const test = (i, j) => {
          const a = bodies[i], b = bodies[j];
          if (!this.layersMeet(a, b)) return;
          if (a.trigger || b.trigger) {
            if (!moves(a) && !moves(b)) return;
            const sa = shapeAt(i), sb = shapeAt(j);
            if (sa.box[0] > sb.box[2] || sb.box[0] > sa.box[2] || sa.box[1] > sb.box[3] || sb.box[1] > sa.box[3]) return;
            const pair = a.id < b.id ? a.id + '|' + b.id : b.id + '|' + a.id;
            if (!overlapping.has(pair) && collide(sa, sb)) overlapping.add(pair);
            return;
          }
          const ad = a.body === 'dynamic', bd = b.body === 'dynamic'; if (!ad && !bd) return;
          const sa = shapeAt(i), sb = shapeAt(j);
          if (sa.box[0] > sb.box[2] || sb.box[0] > sa.box[2] || sa.box[1] > sb.box[3] || sb.box[1] > sa.box[3]) return;
          const hit = collide(sa, sb); if (!hit) return;
          const pair = a.id < b.id ? a.id + '|' + b.id : b.id + '|' + a.id;
          // Resolving the hit moves the two bodies (and anything inside them): their shapes are built again.
          if (this.hasChildren(a) || this.hasChildren(b)) shapes.fill(null); else { shapes[i] = null; shapes[j] = null; }
          touching.add(pair);
          const [nx, ny] = hit.normal, depth = hit.depth; // normal points from a to b
          const share = ad && bd ? 0.5 : 1;
          if (ad) this.moveElement(a, -nx * depth * share, -ny * depth * share);
          if (bd) this.moveElement(b, nx * depth * share, ny * depth * share);
          const va = this.velocity.get(a.id) || { x: 0, y: 0 }, vb = this.velocity.get(b.id) || { x: 0, y: 0 };
          const rel = (vb.x - va.x) * nx + (vb.y - va.y) * ny; if (rel >= 0) return;
          const bounce = Math.max(a.bounce, b.bounce), friction = Math.max(a.friction, b.friction);
          const impulse = -(1 + bounce) * rel * (ad && bd ? 0.5 : 1);
          const slide = v => { const vn = v.x * nx + v.y * ny, tx = v.x - vn * nx, ty = v.y - vn * ny, keep = Math.max(0, 1 - friction * 0.25); return { x: vn * nx + tx * keep, y: vn * ny + ty * keep }; };
          if (ad) this.velocity.set(a.id, slide({ x: va.x - impulse * nx, y: va.y - impulse * ny }));
          if (bd) this.velocity.set(b.id, slide({ x: vb.x + impulse * nx, y: vb.y + impulse * ny }));
        };
        if (bodies.length <= BROADPHASE_MIN) { for (let i = 0; i < bodies.length; i++) for (let j = i + 1; j < bodies.length; j++) test(i, j); this.physicsCounts.pairs = bodies.length * (bodies.length - 1) / 2; }
        else { const near = this.nearPairs(bodies, shapeAt); for (let k = 0; k < near.length; k += 2) test(near[k], near[k + 1]); this.physicsCounts.pairs = near.length / 2; }
        for (const map of maps) for (const b of bodies) if (b.body === 'dynamic' && this.tileCollide(b, map)) touching.add(b.id < map.id ? b.id + '|' + map.id : map.id + '|' + b.id);
      }
      this.contacts = this.contactChanges(this.contacts, touching, 'collide', 'collide_end', 'collide_stay', now);
      this.overlaps = this.contactChanges(this.overlaps, overlapping, 'trigger_enter', 'trigger_exit', 'trigger_stay', now);
    },
    // Pairs of bodies whose outlines overlap, as a flat [i, j, i, j, ...] list with i < j. Each body goes into the
    // grid cells its outline covers; cells are about twice the size of a typical body, so a cell holds a handful. A
    // pair is listed only by the cell where the overlap of the two outlines begins, so a pair sharing several cells
    // is still listed once. A body covering a great many cells (a wide floor) is checked against every body instead.
    nearPairs(bodies, shapeAt) {
      const n = bodies.length, boxes = new Array(n), big = [], out = [];
      let size = 0, counted = 0;
      for (let i = 0; i < n; i++) { const bx = boxes[i] = shapeAt(i).box, span = Math.max(bx[2] - bx[0], bx[3] - bx[1]); if (span < 1024) { size += span; counted++; } }
      const cell = Math.min(256, Math.max(BROADPHASE_CELL / 4, counted ? 2 * size / counted : BROADPHASE_CELL));
      const grid = new Map();
      for (let i = 0; i < n; i++) {
        const bx = boxes[i], x0 = Math.floor(bx[0] / cell), x1 = Math.floor(bx[2] / cell), y0 = Math.floor(bx[1] / cell), y1 = Math.floor(bx[3] / cell);
        if ((x1 - x0 + 1) * (y1 - y0 + 1) > 64) { big.push(i); continue; }
        for (let cx = x0; cx <= x1; cx++) for (let cy = y0; cy <= y1; cy++) { const key = (cx + 32768) * 65536 + (cy + 32768); let list = grid.get(key); if (!list) grid.set(key, list = []); list.push(i); }
      }
      for (const [key, list] of grid) {
        const cx = Math.floor(key / 65536) - 32768, cy = key % 65536 - 32768;
        for (let p = 0; p < list.length; p++) {
          const i = list[p], bi = boxes[i];
          for (let q = p + 1; q < list.length; q++) {
            const j = list[q], bj = boxes[j];
            if (bi[0] > bj[2] || bj[0] > bi[2] || bi[1] > bj[3] || bj[1] > bi[3]) continue;
            // Only the cell holding the start of the overlap lists the pair.
            if (Math.floor(Math.max(bi[0], bj[0]) / cell) !== cx || Math.floor(Math.max(bi[1], bj[1]) / cell) !== cy) continue;
            if (i < j) out.push(i, j); else out.push(j, i);
          }
        }
      }
      if (big.length) {
        const isBig = new Set(big);
        for (const i of big) for (let j = 0; j < n; j++) {
          if (j === i || (isBig.has(j) && j < i)) continue; // two big bodies are listed once
          const bi = boxes[i], bj = boxes[j];
          if (bi[0] > bj[2] || bj[0] > bi[2] || bi[1] > bj[3] || bj[1] > bi[3]) continue;
          if (i < j) out.push(i, j); else out.push(j, i);
        }
      }
      return out;
    },
    hasChildren(e) {
      if (!this.children) { this.children = new Map(); for (const c of this.ui.elements) if (c.parent) { if (!this.children.has(c.parent)) this.children.set(c.parent, []); this.children.get(c.parent).push(c); } }
      return this.children.has(e.id);
    },
    // Pushes one body out of a tilemap's solid tiles. Only the tiles under the body's own box are looked at, and a
    // face with a solid tile against it is skipped, so a body sliding along a tiled floor doesn't catch on the seams.
    tileCollide(b, map) {
      const cache = this.tileGrid(map); if (!cache.solid.size) return false;
      const grid = cache.grid, columns = cache.columns, rows = cache.rows, solid = cache.solid;
      const tw = Math.max(1, map.tileWidth | 0), th = Math.max(1, map.tileHeight | 0);
      const mx = map.bounds.x, my = map.bounds.y;
      const box = boxOf(this.shapeOf(b)).box;
      const c0 = Math.max(0, Math.floor((box[0] - mx) / tw)), c1 = Math.min(columns - 1, Math.floor((box[2] - mx - 0.001) / tw));
      const r0 = Math.max(0, Math.floor((box[1] - my) / th)), r1 = Math.min(rows - 1, Math.floor((box[3] - my - 0.001) / th));
      if (c1 < c0 || r1 < r0) return false;
      const at = (c, r) => c >= 0 && r >= 0 && c < columns && r < rows && solid.has(grid[r * columns + c]);
      let hit = false;
      for (let r = r0; r <= r1; r++) for (let c = c0; c <= c1; c++) {
        if (!solid.has(grid[r * columns + c])) continue;
        // The body may have been pushed since the range was worked out, so overlap is measured against its box now.
        const live = boxOf(this.shapeOf(b)).box;
        const tx = mx + c * tw, ty = my + r * th;
        const dx = Math.min(live[2], tx + tw) - Math.max(live[0], tx), dy = Math.min(live[3], ty + th) - Math.max(live[1], ty);
        if (dx <= 0 || dy <= 0) continue;
        hit = true;
        const left = (live[0] + live[2]) / 2 < tx + tw / 2, up = (live[1] + live[3]) / 2 < ty + th / 2;
        // Prefer the shallower axis, unless that face is buried against a neighbouring solid tile.
        const openX = !at(c + (left ? -1 : 1), r), openY = !at(c, r + (up ? -1 : 1));
        const horizontal = openX && (dx <= dy || !openY);
        const v = this.velocity.get(b.id) || { x: 0, y: 0 };
        if (horizontal) {
          this.moveElement(b, left ? -dx : dx, 0);
          if (left ? v.x > 0 : v.x < 0) this.velocity.set(b.id, { x: -v.x * b.bounce, y: v.y });
        } else if (openY) {
          this.moveElement(b, 0, up ? -dy : dy);
          if (up ? v.y > 0 : v.y < 0) {
            const keep = Math.max(0, 1 - b.friction * 0.25); // a floor slows what slides along it, like a static body does
            this.velocity.set(b.id, { x: v.x * keep, y: -v.y * b.bounce });
          }
        }
      }
      return hit;
    },
    // Start and end events for pairs that began or stopped touching, and a stay event every 250 ms in between
    // (the first one 250 ms after the start). Returns the new set.
    contactChanges(before, after, start, end, stay, now) {
      for (const pair of after) if (!before.has(pair)) { this.stayTimes.set(stay + '|' + pair, now); this.contactEvent(pair, start); }
      for (const pair of before) if (!after.has(pair)) { this.stayTimes.delete(stay + '|' + pair); this.contactEvent(pair, end); }
      for (const pair of after) {
        const key = stay + '|' + pair, last = this.stayTimes.get(key);
        if (last !== undefined && now - last >= 250) { this.stayTimes.set(key, now); this.contactEvent(pair, stay, true); }
      }
      return after;
    },
    // Both controls in the pair get the event, each with the other's ID. Stay events are skipped while another
    // event is still running (like Tick), so a long contact can't build up a backlog.
    contactEvent(pair, event, droppable) {
      if (droppable && (this.busy || this.queue.length)) return;
      const [a, b] = pair.split('|'), ea = elementOf(this.ui, a), eb = elementOf(this.ui, b);
      for (const [e, other] of [[ea, b], [eb, a]]) if (e && e.events[event] && this.queue.length < 32) { this.queue.push({ ui: this.ui, element: e, event, value: other, repeat: false }); this.pump(); }
    },
    // What each control is touching (solid contacts and trigger overlaps), for ctx.physics in scripts.
    // What a script sees of the screen, as changes since the last run. The script side (worker, or the page when
    // workers are unavailable) keeps the rest from earlier runs, so a tick that moved six controls sends six bodies
    // rather than every control, every variable and every text. A new screen, a fresh worker or a changed element
    // list starts again from a full copy.
    scriptDelta() {
      const runner = this.scripts, elements = this.ui.elements;
      let sent = this.scriptSent, reset = false;
      if (!sent || sent.generation !== runner.generation || sent.ui !== this.ui) {
        sent = this.scriptSent = { generation: runner.generation, ui: this.ui, count: elements.length, instancesVersion: -1, texts: new Map(), bodies: new Map(), vars: new Map(), tags: new Map(), tilemaps: new Map() };
        reset = true;
      }
      let texts = null, bodies = null, tilemaps = null, tagsChanged = reset, gone = null, instances = null;
      // Controls that have gone since last time (spawned objects removed) are named, so the script side drops them;
      // new ones arrive below like any changed control.
      if (!reset && (sent.count !== elements.length || sent.instancesVersion !== this.instancesVersion)) {
        for (const id of [...sent.bodies.keys()]) if (!elementOf(this.ui, id)) { (gone = gone || []).push(id); sent.bodies.delete(id); sent.texts.delete(id); sent.tags.delete(id); sent.tilemaps.delete(id); }
        if (gone) tagsChanged = true;
      }
      sent.count = elements.length;
      if (reset || sent.instancesVersion !== this.instancesVersion) {
        instances = {}; for (const [id, inst] of this.instances) (instances[inst.template] = instances[inst.template] || []).push(id);
        sent.instancesVersion = this.instancesVersion;
      }
      for (const e of elements) {
        const id = e.id;
        if (reset || sent.texts.get(id) !== e.text) { (texts = texts || {})[id] = e.text; sent.texts.set(id, e.text); }
        const v = this.velocity && this.velocity.get(id), vx = v ? v.x : 0, vy = v ? v.y : 0;
        const sprite = e.type === 'sprite', clip = sprite ? this.spriteProgress(e) : null;
        const frame = sprite ? this.spriteFrame(e) : 0, clipName = e.value || '', step = clip ? clip.step : 0, done = clip ? clip.done : true, state = this.stateOf(id);
        const tags = e.tags || [], tagKey = tags.length ? tags.join('\u0001') : '';
        // For raycasts: the body kind ('' for none), whether it's a trigger, and a round collider.
        const kind = (e.body || (e.type === 'collider' ? 'static' : '')) + (e.trigger ? '/trigger' : '') + (e.collider === 'circle' ? '/circle' : '');
        if (!reset && sent.tags.get(id) !== tagKey) tagsChanged = true;
        sent.tags.set(id, tagKey);
        const b = sent.bodies.get(id), r = e.bounds;
        if (reset || !b || b.x !== r.x || b.y !== r.y || b.width !== r.width || b.height !== r.height || b.vx !== vx || b.vy !== vy || b.visible !== e.visible
            || b.frame !== frame || b.clip !== clipName || b.clipStep !== step || b.clipDone !== done || b.state !== state || b.tagKey !== tagKey || b.kind !== kind) {
          const body = { x: r.x, y: r.y, width: r.width, height: r.height, vx, vy, visible: e.visible, frame, clip: clipName, clipStep: step, clipDone: done, state, tags: tags.slice(), kind };
          (bodies = bodies || {})[id] = body; sent.bodies.set(id, Object.assign({ tagKey }, body));
        }
        if (e.type === 'tilemap') {
          const text = this.tilesText(e), m = sent.tilemaps.get(id);
          const columns = Math.max(1, e.columns | 0), rows = Math.max(1, e.rows | 0), tw = Math.max(1, e.tileWidth | 0), th = Math.max(1, e.tileHeight | 0);
          if (reset || !m || m.tiles !== text || m.x !== r.x || m.y !== r.y || m.columns !== columns || m.rows !== rows || m.tileWidth !== tw || m.tileHeight !== th || m.solid !== (e.solid || '') || m.visible !== e.visible) {
            const map = { columns, rows, tileWidth: tw, tileHeight: th, x: r.x, y: r.y, tiles: text, solid: e.solid || '', visible: e.visible };
            (tilemaps = tilemaps || {})[id] = map; sent.tilemaps.set(id, map);
          }
        }
      }
      let tagged;
      if (tagsChanged) { tagged = {}; for (const e of elements) for (const t of e.tags || []) (tagged[t] = tagged[t] || []).push(e.id); }
      let vars = null, varsGone = null, count = 0;
      for (const k in this.state) { count++; const v = this.state[k]; if (reset || sent.vars.get(k) !== v) { (vars = vars || {})[k] = v; sent.vars.set(k, v); } }
      if (sent.vars.size > count) for (const k of [...sent.vars.keys()]) if (!(k in this.state)) { (varsGone = varsGone || []).push(k); sent.vars.delete(k); }
      return { reset, texts, bodies, tagged, tilemaps, vars, varsGone, gone, instances };
    },
    // A name for a script's source that stays the same while the source does, so the script side compiles it once.
    // The scratchpad and other one-off sources get none and are compiled every time, as before.
    sourceKey(handler, source) {
      if (handler.source !== undefined) return '';
      if (!this.sourceKeys) { this.sourceKeys = new Map(); this.sourceVersion = 0; }
      const known = this.sourceKeys.get(handler.script);
      if (known && known.source === source) return known.key;
      const key = handler.script + '#' + (++this.sourceVersion);
      this.sourceKeys.set(handler.script, { source, key });
      return key;
    },
    touchingMap() {
      const map = {};
      for (const set of [this.contacts, this.overlaps]) for (const pair of set || []) { const [a, b] = pair.split('|'); (map[a] = map[a] || []).push(b); (map[b] = map[b] || []).push(a); }
      return map;
    },
    // ---- Spawned objects ----
    // Copies of a template control made while the game runs (ctx.ui.spawn). Each is an ordinary control: it is drawn,
    // clicked and collided like any other and has the template's events, with its own ID as ctx.elementId. The engine
    // also moves it (a velocity, or a control to seek at a speed) and removes it when its lifetime runs out.
    spawnInstance(templateId, spec) {
      const template = elementOf(this.ui, templateId);
      if (!template && this.components[templateId]) { this.spawnComponent(templateId, spec); return; }
      if (!template) { this.log('warn', 'spawn: there is no control or component ' + templateId); return; }
      if (template._instanceOf) { this.log('warn', 'spawn: ' + templateId + ' is itself a spawned copy; spawn from ' + template._instanceOf); return; }
      if (this.instances.size >= MAX_INSTANCES) { if (!this.instanceCapWarned) { this.instanceCapWarned = true; this.log('warn', 'spawn: at most ' + MAX_INSTANCES + ' spawned objects at once; the rest are skipped'); } return; }
      const id = String(spec.id || '');
      if (!id || elementOf(this.ui, id)) { this.log('warn', 'spawn: the ID "' + id + '" is already in use'); return; }
      const num = (v, d) => (typeof v === 'number' && Number.isFinite(v) ? v : d);
      const e = clone(template);
      e.id = id; e._instanceOf = template.id; e.visible = true;
      e.bounds = { x: num(spec.x, template.bounds.x), y: num(spec.y, template.bounds.y), width: template.bounds.width, height: template.bounds.height };
      if (spec.clip !== undefined && spec.clip !== null) e.value = String(spec.clip);
      if (spec.texture) e.texture = String(spec.texture);
      // Right after the template and its earlier copies, so copies draw where the template does in the draw order.
      const list = this.ui.elements; let at = list.indexOf(template) + 1;
      while (at < list.length && list[at]._instanceOf === template.id) at++;
      list.splice(at, 0, e);
      const spacing = num(spec.separate, this.separateDefaults ? this.separateDefaults.get(template.id) || 0 : 0);
      this.instances.set(id, { template: template.id, life: num(spec.life, 0) > 0 ? spec.life : Infinity, seek: spec.seek ? String(spec.seek) : '', speed: num(spec.speed, 0), separate: Math.max(0, spacing), path: spec.path ? String(spec.path) : '' });
      if (num(spec.vx, 0) || num(spec.vy, 0)) this.velocity.set(id, { x: num(spec.vx, 0), y: num(spec.vy, 0) });
      const n = Number(id.slice(id.lastIndexOf('~') + 1)); if (Number.isFinite(n) && n >= this.instanceSeq) this.instanceSeq = n + 1;
      this.elementsChanged();
    },
    // A component: its controls under a new transparent root the size of the component, placed at x, y. Each control
    // keeps its own look, body and events, with the ID root + '_' + its ID in the component, and actions aimed at other
    // controls of the component are aimed at this copy's ones (as the editor does when placing a component).
    spawnComponent(componentId, spec) {
      const source = this.components[componentId];
      if (this.instances.size >= MAX_INSTANCES) { if (!this.instanceCapWarned) { this.instanceCapWarned = true; this.log('warn', 'spawn: at most ' + MAX_INSTANCES + ' spawned objects at once; the rest are skipped'); } return; }
      if (source.elements.length > MAX_COMPONENT_CONTROLS) { this.log('warn', 'spawn: ' + componentId + ' has more than ' + MAX_COMPONENT_CONTROLS + ' controls'); return; }
      const id = String(spec.id || '');
      if (!id || elementOf(this.ui, id)) { this.log('warn', 'spawn: the ID "' + id + '" is already in use'); return; }
      const num = (v, d) => (typeof v === 'number' && Number.isFinite(v) ? v : d), x = num(spec.x, 0), y = num(spec.y, 0);
      const own = new Set(source.elements.map(e => e.id)), rename = old => id + '_' + old;
      const root = normalizeElement({ id, type: 'panel', fillEnabled: false, borderWidth: 0, text: '', visible: true, enabled: true, events: {}, tags: [], bounds: { x, y, width: source.size.width, height: source.size.height } });
      root._instanceOf = componentId; root._component = true;
      const members = source.elements.map(src => {
        const c = clone(src);
        c.id = rename(src.id); c.parent = src.parent ? rename(src.parent) : id; c._memberOf = id;
        c.bounds = { x: src.bounds.x + x, y: src.bounds.y + y, width: src.bounds.width, height: src.bounds.height };
        for (const ev of Object.values(c.events || {})) for (const side of [ev.client, ev.server]) for (const a of (side && side.actions) || [])
          if (['set_text', 'set_value', 'set_visible', 'set_enabled', 'change_texture', 'player_inventory'].includes(a.type) && own.has(a.target)) a.target = rename(a.target);
        return c;
      });
      // Drawn after the control named by options.after (and anything spawned after it), or on top of everything.
      const list = this.ui.elements, after = spec.after ? elementOf(this.ui, String(spec.after)) : null;
      let at = after ? list.indexOf(after) + 1 : list.length;
      if (after) while (at < list.length && (list[at]._instanceOf || list[at]._memberOf)) at++;
      list.splice(at, 0, root, ...members);
      for (const [k, v] of Object.entries(source.variables || {})) if (!(k in this.state)) this.state[k] = v;
      this.instances.set(id, { template: componentId, members: members.map(m => m.id), life: num(spec.life, 0) > 0 ? spec.life : Infinity, seek: spec.seek ? String(spec.seek) : '', speed: num(spec.speed, 0), separate: Math.max(0, num(spec.separate, this.separateDefaults ? this.separateDefaults.get(componentId) || 0 : 0)), path: spec.path ? String(spec.path) : '' });
      if (num(spec.vx, 0) || num(spec.vy, 0)) this.velocity.set(id, { x: num(spec.vx, 0), y: num(spec.vy, 0) });
      const n = Number(id.slice(id.lastIndexOf('~') + 1)); if (Number.isFinite(n) && n >= this.instanceSeq) this.instanceSeq = n + 1;
      this.elementsChanged();
    },
    despawnInstance(id) {
      const e = elementOf(this.ui, id); if (!e || !e._instanceOf) return false;
      // A spawned component goes with all its controls, in one pass over the list.
      const inst = this.instances.get(id), gone = new Set([id]); if (inst && inst.members) for (const m of inst.members) gone.add(m);
      const list = this.ui.elements; let w = 0; for (let i = 0; i < list.length; i++) if (!gone.has(list[i].id)) list[w++] = list[i]; list.length = w;
      for (const g of gone) { this.velocity.delete(g); this.spriteClock.delete(g); }
      // Contacts they were part of end quietly (a removed object gets no events).
      for (const set of [this.contacts, this.overlaps]) for (const pair of [...set]) { const k = pair.indexOf('|'); if (gone.has(pair.slice(0, k)) || gone.has(pair.slice(k + 1))) set.delete(pair); }
      for (const key of [...this.stayTimes.keys()]) { const parts = key.split('|'); if (gone.has(parts[1]) || gone.has(parts[2])) this.stayTimes.delete(key); }
      for (const f of ['hovered', 'focused', 'dragging']) if (this[f] && gone.has(this[f].id)) this[f] = null;
      this.instances.delete(id);
      this.elementsChanged();
      return true;
    },
    // A flow field over a tilemap toward one tile: every open tile's step count to it (-1: solid or cut off), by a
    // breadth-first walk of eight directions that never cuts a solid corner. Kept per map and rebuilt only when the
    // goal moves to another tile or a tile changes, so any number of seekers share one.
    flowField(map, gc, gr) {
      const cache = this.tileGrid(map); if (!this._fields) this._fields = new Map();
      let f = this._fields.get(map.id);
      if (f && f.version === cache.version && f.gc === gc && f.gr === gr) return f;
      const cols = cache.columns, rows = cache.rows, n = cols * rows, grid = cache.grid, solid = cache.solid;
      const dist = new Int32Array(n).fill(-1), queue = new Int32Array(n);
      const open = (c, r) => c >= 0 && r >= 0 && c < cols && r < rows && !solid.has(grid[r * cols + c]);
      if (open(gc, gr)) {
        let head = 0, tail = 0; const start = gr * cols + gc; dist[start] = 0; queue[tail++] = start;
        while (head < tail) {
          const at = queue[head++], c = at % cols, r = (at - c) / cols, d = dist[at] + 1;
          for (const [dc, dr] of STEPS8) {
            const nc = c + dc, nr = r + dr; if (!open(nc, nr)) continue;
            if (dc && dr && (!open(c + dc, r) || !open(c, r + dr))) continue;
            const k = nr * cols + nc; if (dist[k] < 0) { dist[k] = d; queue[tail++] = k; }
          }
        }
      }
      f = { version: cache.version, gc, gr, dist, cols, rows, solid, grid };
      this._fields.set(map.id, f);
      return f;
    },
    // Where a seeker following a map heads next: the centre of the neighbouring tile nearer the target, or null to go
    // straight (same tile as the target, off the map, or no way through).
    pathStep(map, e, t) {
      const tw = Math.max(1, map.tileWidth | 0), th = Math.max(1, map.tileHeight | 0);
      const ex = e.bounds.x + e.bounds.width / 2, ey = e.bounds.y + e.bounds.height / 2, tx = t.bounds.x + t.bounds.width / 2, ty = t.bounds.y + t.bounds.height / 2;
      const ec = Math.floor((ex - map.bounds.x) / tw), er = Math.floor((ey - map.bounds.y) / th), tc = Math.floor((tx - map.bounds.x) / tw), tr = Math.floor((ty - map.bounds.y) / th);
      const f = this.flowField(map, tc, tr);
      if (ec < 0 || er < 0 || ec >= f.cols || er >= f.rows || (ec === tc && er === tr)) return null;
      const here = f.dist[er * f.cols + ec]; if (here <= 0) return null;
      const open = (c, r) => c >= 0 && r >= 0 && c < f.cols && r < f.rows && f.dist[r * f.cols + c] >= 0;
      let best = null, bestD = here, bestLen = Infinity;
      for (const [dc, dr] of STEPS8) {
        const nc = ec + dc, nr = er + dr; if (!open(nc, nr)) continue;
        if (dc && dr && (!open(ec + dc, er) || !open(ec, er + dr))) continue;
        const d = f.dist[nr * f.cols + nc], len = Math.abs(dc) + Math.abs(dr);
        // Fewer steps first; between equals, the straight step (so paths don't zigzag).
        if (d < bestD || (d === bestD && best && len < bestLen)) { best = [nc, nr]; bestD = d; bestLen = len; }
      }
      return best ? { x: map.bounds.x + (best[0] + 0.5) * tw, y: map.bounds.y + (best[1] + 0.5) * th } : null;
    },
    // The control list changed: indexes and caches built from it start again.
    elementsChanged() { this.ui._index = null; this.children = null; this._parentsOf = null; this.instancesVersion++; },
    // Each frame: lifetimes count down, seekers turn toward their target, and copies that aren't dynamic bodies move by
    // their velocity (dynamic ones are moved by the physics step, with gravity and collisions).
    stepInstances(now) {
      const dt = Math.min(0.1, Math.max(0, (now - this.instanceTime) / 1000)); this.instanceTime = now;
      if (!this.instances.size || !dt) return;
      let gone = null;
      for (const [id, inst] of this.instances) {
        const e = elementOf(this.ui, id); if (!e) { (gone = gone || []).push(id); continue; }
        if (inst.life !== Infinity && (inst.life -= dt) <= 0) { (gone = gone || []).push(id); continue; }
        if (inst.seek) {
          const t = elementOf(this.ui, inst.seek);
          if (t) {
            // Following a map: head for the next tile on the way instead of straight at the target.
            const map = inst.path ? elementOf(this.ui, inst.path) : null, via = map && map.type === 'tilemap' ? this.pathStep(map, e, t) : null;
            const gx = via ? via.x : t.bounds.x + t.bounds.width / 2, gy = via ? via.y : t.bounds.y + t.bounds.height / 2;
            const dx = gx - e.bounds.x - e.bounds.width / 2, dy = gy - e.bounds.y - e.bounds.height / 2, d = Math.hypot(dx, dy);
            // Close enough counts as there, so a seeker settles on its target instead of shaking across it.
            this.velocity.set(id, via || d > Math.max(1, inst.speed * dt) ? (d > 0 ? { x: dx / d * inst.speed, y: dy / d * inst.speed } : { x: 0, y: 0 }) : { x: 0, y: 0 });
          }
        }
      }
      if (gone) for (const id of gone) { if (!this.despawnInstance(id)) this.instances.delete(id); }
      this.separateInstances();
      for (const id of this.instances.keys()) {
        const e = elementOf(this.ui, id); if (!e || e.body === 'dynamic') continue;
        const v = this.velocity.get(id); if (v && (v.x || v.y)) this.moveElement(e, v.x * dt, v.y * dt);
      }
    },
    // Copies with a spacing (spawn's separate, or ui.separate) keep that far apart, centre to centre: each frame,
    // any two closer than the larger of their spacings are pushed apart by part of the overlap, so a crowd spreads
    // out smoothly instead of stacking on one spot. Pairs come from a grid, so a crowd costs about the same per copy.
    separateInstances() {
      const list = [];
      for (const [id, inst] of this.instances) if (inst.separate > 0) { const e = elementOf(this.ui, id); if (e) list.push({ e, r: inst.separate, x: e.bounds.x + e.bounds.width / 2, y: e.bounds.y + e.bounds.height / 2, seeks: !!inst.seek }); }
      if (list.length < 2) return;
      let cell = 0; for (const a of list) if (a.r > cell) cell = a.r;
      const grid = new Map();
      for (let i = 0; i < list.length; i++) { const a = list[i], key = (Math.floor(a.x / cell) + 32768) * 65536 + (Math.floor(a.y / cell) + 32768); let l = grid.get(key); if (!l) grid.set(key, l = []); l.push(i); }
      const push = new Float64Array(list.length * 2);
      for (let i = 0; i < list.length; i++) {
        const a = list[i], cx = Math.floor(a.x / cell), cy = Math.floor(a.y / cell);
        for (let gx = cx - 1; gx <= cx + 1; gx++) for (let gy = cy - 1; gy <= cy + 1; gy++) {
          const l = grid.get((gx + 32768) * 65536 + (gy + 32768)); if (!l) continue;
          for (const j of l) {
            if (j <= i) continue;
            const b = list[j], want = Math.max(a.r, b.r); let dx = b.x - a.x, dy = b.y - a.y, d = Math.hypot(dx, dy);
            if (d >= want) continue;
            // Two on the very same spot part along a direction taken from their order, so they never stay stacked.
            if (d < 1e-6) { const ang = (i * 2.399 + j * 0.618) % (Math.PI * 2); dx = Math.cos(ang); dy = Math.sin(ang); d = 1; }
            const k = (want - Math.min(d, want)) * SEPARATION_SHARE / d;
            push[i * 2] -= dx * k; push[i * 2 + 1] -= dy * k; push[j * 2] += dx * k; push[j * 2 + 1] += dy * k;
            a.x -= dx * k; a.y -= dy * k; b.x += dx * k; b.y += dy * k; // later pairs this frame see where these two now are
            // A seeker doesn't walk into a neighbour it's pressed against: that part of its speed is dropped this frame
            // (seeking sets the speed again next frame), so a crowd around its target queues at its spacing.
            const nx = dx / d, ny = dy / d;
            if (a.seeks) { const v = this.velocity.get(a.e.id); if (v) { const into = v.x * nx + v.y * ny; if (into > 0) this.velocity.set(a.e.id, { x: v.x - nx * into, y: v.y - ny * into }); } }
            if (b.seeks) { const v = this.velocity.get(b.e.id); if (v) { const into = -(v.x * nx + v.y * ny); if (into > 0) this.velocity.set(b.e.id, { x: v.x + nx * into, y: v.y + ny * into }); } }
          }
        }
      }
      for (let i = 0; i < list.length; i++) if (push[i * 2] || push[i * 2 + 1]) this.moveElement(list[i].e, push[i * 2], push[i * 2 + 1]);
    },
    simulate(now) {
      if (!this.ui || this.closed) return;
      const p = this.prof;
      if (!p) {
        this.pollInputs(); this.stepStateGraphs(); if (this.playing.size) this.stepAnimations(now); this.stepInstances(now); this.stepPhysics(now);
        if (this.particleCount || this.streams.size) this.stepParticles(now); else this.particleTime = now;
        return;
      }
      let t = performance.now(); const mark = k => { const n = performance.now(); p.sum[k] += n - t; t = n; };
      this.pollInputs(); this.stepStateGraphs(); if (this.playing.size) this.stepAnimations(now); mark('logic');
      this.stepInstances(now); mark('objects');
      this.stepPhysics(now); mark('physics');
      if (this.particleCount || this.streams.size) this.stepParticles(now); else this.particleTime = now;
      mark('particles');
    },

    // ---- Profiler ----
    // An overlay over the game (never part of what it draws) and the figures behind it, averaged over a quarter second.
    setProfiler(on) {
      if (!on) { if (this.prof) { this.prof.box.remove(); this.prof = null; } return; }
      if (this.prof) return;
      const box = document.createElement('div');
      box.className = 'wysicraft-profiler';
      box.style.cssText = 'position:fixed;top:8px;right:8px;z-index:2147483647;pointer-events:none;background:rgba(21,24,29,0.88);color:#D9E6F1;border:1px solid #2C333C;border-radius:4px;padding:6px 8px;font:11px/1.45 Consolas,ui-monospace,monospace;white-space:pre;min-width:190px';
      const text = document.createElement('div'), graph = document.createElement('canvas');
      graph.width = 190; graph.height = 36; graph.style.cssText = 'display:block;margin-top:4px;width:190px;height:36px';
      box.append(text, graph); document.body.appendChild(box);
      this.prof = { box, text, graph, since: performance.now(), lastFrame: 0, frames: 0, history: new Float32Array(190), at: 0,
        sum: { logic: 0, objects: 0, physics: 0, particles: 0, draw: 0, frame: 0 }, worst: 0, scripts: 0, scriptSum: 0, scriptWorst: 0, ops: 0, figures: null };
    },
    profiledFrame(t) {
      const p = this.prof;
      this.tick(); const b = performance.now();
      this.render(); const c = performance.now();
      p.sum.draw += c - b;
      if (p.lastFrame) { const gap = t - p.lastFrame; p.sum.frame += gap; p.frames++; p.worst = Math.max(p.worst, gap); p.history[p.at] = gap; p.at = (p.at + 1) % p.history.length; }
      p.lastFrame = t;
      if (c - p.since >= 250 && p.frames) this.profilerUpdate(c);
    },
    // Called after each script run while the profiler is on: the whole round trip (sending, running, applying).
    profileScript(ms, ops) { const p = this.prof; if (!p) return; p.scripts++; p.scriptSum += ms; p.scriptWorst = Math.max(p.scriptWorst, ms); p.ops += ops; },
    profilerUpdate(now) {
      const p = this.prof, n = p.frames, span = (now - p.since) / 1000, avg = k => p.sum[k] / n, f = x => x.toFixed(2).padStart(6);
      const engine = avg('logic') + avg('objects') + avg('physics') + avg('particles');
      p.figures = {
        fps: Math.round(n / span), frameMs: +avg('frame').toFixed(2), worstFrameMs: +p.worst.toFixed(2),
        engineMs: +engine.toFixed(2), logicMs: +avg('logic').toFixed(2), objectsMs: +avg('objects').toFixed(2), physicsMs: +avg('physics').toFixed(2), particlesMs: +avg('particles').toFixed(2), drawMs: +avg('draw').toFixed(2),
        scriptsPerSecond: Math.round(p.scripts / span), scriptMs: p.scripts ? +(p.scriptSum / p.scripts).toFixed(2) : 0, worstScriptMs: +p.scriptWorst.toFixed(2), opsPerScript: p.scripts ? Math.round(p.ops / p.scripts) : 0,
        controls: this.ui ? this.ui.elements.length : 0, spawned: this.instances.size, bodies: this.physicsCounts.bodies, pairs: this.physicsCounts.pairs,
        particles: this.particleCount || 0, drawBatches: this.glLayer.ok ? this.glFlushes : 0, webgl: !!this.glLayer.ok
      };
      const g = p.figures;
      p.text.textContent =
        'FRAME  ' + f(g.frameMs) + ' ms  ' + String(g.fps).padStart(3) + ' fps\n' +
        'worst  ' + f(g.worstFrameMs) + ' ms\n' +
        'engine ' + f(g.engineMs) + ' ms\n' +
        '  logic     ' + f(g.logicMs) + '\n' +
        '  objects   ' + f(g.objectsMs) + '\n' +
        '  physics   ' + f(g.physicsMs) + '\n' +
        '  particles ' + f(g.particlesMs) + '\n' +
        'draw   ' + f(g.drawMs) + ' ms' + (g.webgl ? '  ' + g.drawBatches + ' batches' : '  canvas') + '\n' +
        'script ' + f(g.scriptMs) + ' ms  ' + g.scriptsPerSecond + '/s' + '\n' +
        '  worst ' + f(g.worstScriptMs) + ' ms  ' + g.opsPerScript + ' ops\n' +
        'controls ' + g.controls + '  spawned ' + g.spawned + '\n' +
        'bodies ' + g.bodies + '  pairs ' + g.pairs + '\n' +
        'particles ' + g.particles;
      // Frame times, newest on the right; the line is a 60 fps frame (16.7 ms), the top 33 ms.
      const c = p.graph.getContext('2d'), w = p.graph.width, h = p.graph.height, len = p.history.length;
      c.clearRect(0, 0, w, h);
      for (let i = 0; i < len; i++) {
        const ms = p.history[(p.at + i) % len]; if (!ms) continue;
        const bar = Math.min(h, ms / 33.3 * h);
        c.fillStyle = ms > 33.4 ? '#FF8C7A' : ms > 17.5 ? '#E0B050' : '#7FD08A';
        c.fillRect(i, h - bar, 1, bar);
      }
      c.fillStyle = 'rgba(217,230,241,0.35)'; c.fillRect(0, Math.round(h - 16.7 / 33.3 * h), w, 1);
      p.since = now; p.frames = 0; p.worst = 0; p.scripts = 0; p.scriptSum = 0; p.scriptWorst = 0; p.ops = 0;
      for (const k of Object.keys(p.sum)) p.sum[k] = 0;
    }
  });

  // Controls with no picture: never hovered or clicked.
  const UNSEEN = new Set(['collider', 'camera', 'sound', 'particles']);
  // Screen pixels of thumb travel for a full push on the touch stick.
  const STICK_REACH = 34;
  const MAX_PARTICLES = 2000, MAX_PARTICLES_GL = 50000;
  // Spawned objects alive at once on a screen (ctx.ui.spawn); more are skipped with one warning.
  const MAX_INSTANCES = 5000;
  // Controls one spawned component may have.
  const MAX_COMPONENT_CONTROLS = 256;
  // Physics: above this many bodies, only bodies sharing a grid cell (this many pixels square) are tested together.
  const BROADPHASE_MIN = 64, BROADPHASE_CELL = 64;
  // Each frame, two crowding copies each move apart by this share of their overlap, so a pair is fully apart after one
  // frame and a crowd pressing in (seekers) still holds its spacing.
  const SEPARATION_SHARE = 0.5;
  // The eight steps a path can take from a tile.
  const STEPS8 = [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [1, -1], [-1, 1], [-1, -1]];
  // '#RRGGBB' to [r, g, b]; particle colours are validated on the way in, so a bad one just goes black.
  // A particle effect's colour ramp: sorted stops, or the older pair when it has none.
  function rampOf(fx) {
    if (fx._ramp) return fx._ramp;
    let stops = (fx.colors || []).slice();
    if (stops.length === 1) stops.push({ at: 1, color: stops[0].color });
    if (stops.length < 2) stops = [{ at: 0, color: fx.colorStart }, { at: 1, color: fx.colorEnd }];
    stops.sort((a, b) => a.at - b.at);
    fx._ramp = stops.map(s => ({ at: s.at, rgb: rgbOf(s.color) }));
    return fx._ramp;
  }
  // The colour at a point through a particle's life, 0 to 1.
  function colourAt(ramp, t) {
    if (t <= ramp[0].at) return ramp[0].rgb;
    for (let i = 1; i < ramp.length; i++) {
      if (t > ramp[i].at) continue;
      const span = ramp[i].at - ramp[i - 1].at, k = span <= 0 ? 1 : (t - ramp[i - 1].at) / span;
      const a = ramp[i - 1].rgb, b = ramp[i].rgb;
      return [a[0] + (b[0] - a[0]) * k, a[1] + (b[1] - a[1]) * k, a[2] + (b[2] - a[2]) * k];
    }
    return ramp[ramp.length - 1].rgb;
  }
  function rgbOf(hex) { const n = parseInt(String(hex || '').replace('#', ''), 16); return Number.isFinite(n) ? [(n >> 16) & 255, (n >> 8) & 255, n & 255] : [0, 0, 0]; }
  const CONTACT_EVENTS = new Set(['collide', 'collide_stay', 'collide_end', 'trigger_enter', 'trigger_stay', 'trigger_exit']);
  // Do these two bodies look at each other's layer? collidesWith is a bit mask over layers 0-15, and -1 (the default)
  // means every layer, which is how everything behaved before layers existed.
  function layersMeetMasks(a, b) {
    const am = a.collidesWith === undefined ? -1 : a.collidesWith, bm = b.collidesWith === undefined ? -1 : b.collidesWith;
    // Both have to be looking at the other, so a one-way mask never produces a half-detected contact.
    return (am & (1 << (b.layer || 0))) !== 0 && (bm & (1 << (a.layer || 0))) !== 0;
  }
  // The bounding box of a collision shape (minX, minY, maxX, maxY), used to skip pairs that can't be touching.
  function boxOf(shape) {
    if (shape.circle) { shape.box = [shape.cx - shape.r, shape.cy - shape.r, shape.cx + shape.r, shape.cy + shape.r]; return shape; }
    let x0 = Infinity, y0 = Infinity, x1 = -Infinity, y1 = -Infinity;
    for (const poly of shape.polys) for (const p of poly) { if (p[0] < x0) x0 = p[0]; if (p[0] > x1) x1 = p[0]; if (p[1] < y0) y0 = p[1]; if (p[1] > y1) y1 = p[1]; }
    shape.box = [x0, y0, x1, y1]; return shape;
  }
  // Two axis-aligned boxes [x0, y0, x1, y1]: the same result polyPoly gives for them. Its first axes are the first box's
  // top edge (0, -1) then right edge (1, 0), and a later axis only wins on a strictly smaller overlap, so the y axis wins
  // ties; the normal is then turned to point from the first box's centre toward the second's.
  function rectRect(a, b) {
    const ox = Math.min(a[2], b[2]) - Math.max(a[0], b[0]), oy = Math.min(a[3], b[3]) - Math.max(a[1], b[1]);
    if (ox <= 0 || oy <= 0) return null;
    let normal, depth;
    if (ox < oy) { normal = [1, 0]; depth = ox; } else { normal = [0, -1]; depth = oy; }
    const dx = (b[0] + b[2] - a[0] - a[2]) / 2, dy = (b[1] + b[3] - a[1] - a[3]) / 2;
    if (dx * normal[0] + dy * normal[1] < 0) normal = [-normal[0], -normal[1]];
    return { normal, depth };
  }
  // Separating-axis test between two shapes made of convex polygons and/or a circle. Returns the push that
  // separates them (normal from the first shape to the second, and depth), or null.
  function collide(sa, sb) {
    if (sa.rect && sb.rect) return rectRect(sa.box, sb.box);
    let best = null;
    const pa = sa.circle ? [null] : sa.polys, pb = sb.circle ? [null] : sb.polys;
    for (const a of pa) for (const b of pb) {
      const hit = sa.circle && sb.circle ? circleCircle(sa, sb) : sa.circle ? flip(polyCircle(b, sa)) : sb.circle ? polyCircle(a, sb) : polyPoly(a, b);
      if (hit && (!best || hit.depth > best.depth)) best = hit;
    }
    return best;
  }
  function flip(hit) { return hit && { normal: [-hit.normal[0], -hit.normal[1]], depth: hit.depth }; }
  function circleCircle(a, b) { const dx = b.cx - a.cx, dy = b.cy - a.cy, d = Math.hypot(dx, dy), r = a.r + b.r; if (d >= r) return null; return d === 0 ? { normal: [0, 1], depth: r } : { normal: [dx / d, dy / d], depth: r - d }; }
  function project(poly, ax, ay) { let min = Infinity, max = -Infinity; for (const p of poly) { const v = p[0] * ax + p[1] * ay; if (v < min) min = v; if (v > max) max = v; } return [min, max]; }
  function center(poly) { let x = 0, y = 0; for (const p of poly) { x += p[0]; y += p[1]; } return [x / poly.length, y / poly.length]; }
  function polyPoly(a, b) {
    let depth = Infinity, normal = null;
    for (const poly of [a, b]) for (let i = 0; i < poly.length; i++) {
      const p = poly[i], q = poly[(i + 1) % poly.length]; let ax = q[1] - p[1], ay = p[0] - q[0]; const len = Math.hypot(ax, ay); if (!len) continue; ax /= len; ay /= len;
      const [a1, a2] = project(a, ax, ay), [b1, b2] = project(b, ax, ay); const o = Math.min(a2, b2) - Math.max(a1, b1);
      if (o <= 0) return null; if (o < depth) { depth = o; normal = [ax, ay]; }
    }
    const ca = center(a), cb = center(b); if ((cb[0] - ca[0]) * normal[0] + (cb[1] - ca[1]) * normal[1] < 0) normal = [-normal[0], -normal[1]];
    return { normal, depth };
  }
  function polyCircle(poly, c) {
    let depth = Infinity, normal = null;
    const axes = []; for (let i = 0; i < poly.length; i++) { const p = poly[i], q = poly[(i + 1) % poly.length]; axes.push([q[1] - p[1], p[0] - q[0]]); }
    let closest = poly[0], dist = Infinity; for (const p of poly) { const d = Math.hypot(p[0] - c.cx, p[1] - c.cy); if (d < dist) { dist = d; closest = p; } }
    axes.push([c.cx - closest[0], c.cy - closest[1]]);
    for (let [ax, ay] of axes) {
      const len = Math.hypot(ax, ay); if (!len) continue; ax /= len; ay /= len;
      const [a1, a2] = project(poly, ax, ay), cp = c.cx * ax + c.cy * ay, b1 = cp - c.r, b2 = cp + c.r; const o = Math.min(a2, b2) - Math.max(a1, b1);
      if (o <= 0) return null; if (o < depth) { depth = o; normal = [ax, ay]; }
    }
    const ca = center(poly); if ((c.cx - ca[0]) * normal[0] + (c.cy - ca[1]) * normal[1] < 0) normal = [-normal[0], -normal[1]];
    return { normal, depth };
  }

  // ---- Raycasts (script side) ----
  // The first thing a line from (x1, y1) to (x2, y2) meets: a visible solid control (any body or collider; triggers
  // only with triggers: true) or a solid tile of a visible tilemap. Boxes are exact, circles are exact, polygon
  // colliders count as their box. bodies and tilemaps are the script side's copy of the screen; grid(id) unpacks a
  // tilemap. Returns { id, x, y, distance, normal: { x, y } } or null. options: ignore (an ID or a list), tag (only
  // controls with it), triggers, tiles (false to leave tilemaps out).
  function raycastWorld(bodies, tilemaps, grid, x1, y1, x2, y2, options) {
    const o = options || {}, dx = x2 - x1, dy = y2 - y1, len = Math.hypot(dx, dy);
    if (!(len > 0)) return null;
    const ignore = new Set([].concat(o.ignore === undefined ? [] : o.ignore).map(String)), tag = o.tag === undefined ? '' : String(o.tag);
    let best = null;
    const found = (t, id, nx, ny) => { if (t >= 0 && t <= 1 && (!best || t < best.t)) best = { t, id, nx, ny }; };
    for (const id in bodies) {
      const b = bodies[id];
      if (!b.visible || !b.kind || ignore.has(id)) continue;
      if (b.kind.indexOf('/trigger') >= 0 && !o.triggers) continue;
      if (tag && (b.tags || []).indexOf(tag) < 0) continue;
      if (b.kind.indexOf('/circle') >= 0) {
        const rr = Math.min(b.width, b.height) / 2, cx = b.x + b.width / 2, cy = b.y + b.height / 2;
        const fx = x1 - cx, fy = y1 - cy, A = dx * dx + dy * dy, B = 2 * (fx * dx + fy * dy), C = fx * fx + fy * fy - rr * rr, disc = B * B - 4 * A * C;
        if (disc < 0) continue;
        const t = C <= 0 ? 0 : (-B - Math.sqrt(disc)) / (2 * A);
        const hx = x1 + dx * t - cx, hy = y1 + dy * t - cy, hl = Math.hypot(hx, hy) || 1;
        found(t, id, C <= 0 ? 0 : hx / hl, C <= 0 ? 0 : hy / hl);
        continue;
      }
      // A box: the slab method, entering on the side the normal names.
      let t0 = 0, t1 = 1, nx = 0, ny = 0;
      const slab = (start, dir, lo, hi, ax) => {
        if (dir === 0) return start >= lo && start <= hi;
        let ta = (lo - start) / dir, tb = (hi - start) / dir, side = -1;
        if (ta > tb) { const t = ta; ta = tb; tb = t; side = 1; }
        if (ta > t0) { t0 = ta; nx = ax === 0 ? side : 0; ny = ax === 1 ? side : 0; }
        if (tb < t1) t1 = tb;
        return t0 <= t1;
      };
      if (!slab(x1, dx, b.x, b.x + b.width, 0) || !slab(y1, dy, b.y, b.y + b.height, 1)) continue;
      found(t0, id, nx, ny);
    }
    if (o.tiles !== false) for (const id in tilemaps) {
      const m = tilemaps[id]; if (!m.solid || m.visible === false || ignore.has(id)) continue;
      const solid = solidTiles(m.solid), g = grid(id); if (!solid.size || !g) continue;
      // Walk the tiles the line crosses, in order (a DDA), and stop at the first solid one.
      const tw = m.tileWidth, th = m.tileHeight, sx = (x1 - m.x) / tw, sy = (y1 - m.y) / th, ex = (x2 - m.x) / tw, ey = (y2 - m.y) / th;
      let cx = Math.floor(sx), cy = Math.floor(sy); const stepX = ex > sx ? 1 : -1, stepY = ey > sy ? 1 : -1;
      const ddx = ex - sx, ddy = ey - sy, tDx = ddx !== 0 ? Math.abs(1 / ddx) : Infinity, tDy = ddy !== 0 ? Math.abs(1 / ddy) : Infinity;
      let tMx = ddx !== 0 ? (stepX > 0 ? cx + 1 - sx : sx - cx) * tDx : Infinity, tMy = ddy !== 0 ? (stepY > 0 ? cy + 1 - sy : sy - cy) * tDy : Infinity;
      let t = 0, nx = 0, ny = 0;
      for (let steps = 0; steps < 4096 && t <= 1; steps++) {
        if (cx >= 0 && cy >= 0 && cx < m.columns && cy < m.rows && solid.has(g.g[cy * m.columns + cx])) { found(t, id, nx, ny); break; }
        if (tMx < tMy) { t = tMx; tMx += tDx; cx += stepX; nx = -stepX; ny = 0; } else { t = tMy; tMy += tDy; cy += stepY; nx = 0; ny = -stepY; }
      }
    }
    return best ? { id: best.id, x: x1 + dx * best.t, y: y1 + dy * best.t, distance: len * best.t, normal: { x: best.nx, y: best.ny } } : null;
  }

  // A route over a tilemap from one point to another: A* over its open tiles, eight directions, never cutting a solid
  // corner. m is the script side's copy of the map, g its unpacked grid. Returns the tile centres to walk through, the
  // last being the goal's tile, or null when there's no way (either end off the map or in a solid tile, or walled off).
  function findTilePath(m, g, x1, y1, x2, y2) {
    const solid = solidTiles(m.solid), cols = m.columns, rows = m.rows, tw = m.tileWidth, th = m.tileHeight;
    const sc = Math.floor((x1 - m.x) / tw), sr = Math.floor((y1 - m.y) / th), gc = Math.floor((x2 - m.x) / tw), gr = Math.floor((y2 - m.y) / th);
    const open = (c, r) => c >= 0 && r >= 0 && c < cols && r < rows && !solid.has(g.g[r * cols + c]);
    if (!open(sc, sr) || !open(gc, gr)) return null;
    const centre = k => ({ x: m.x + (k % cols + 0.5) * tw, y: m.y + (Math.floor(k / cols) + 0.5) * th });
    const start = sr * cols + sc, goal = gr * cols + gc;
    if (start === goal) return [centre(goal)];
    const n = cols * rows, cost = new Float64Array(n).fill(Infinity), from = new Int32Array(n).fill(-1), done = new Uint8Array(n);
    const h = k => { const dx = Math.abs(k % cols - gc), dy = Math.abs(Math.floor(k / cols) - gr); return Math.max(dx, dy) + (Math.SQRT2 - 1) * Math.min(dx, dy); };
    // A binary heap of [priority, tile].
    const heap = [], push = (pr, k) => { heap.push([pr, k]); let i = heap.length - 1; while (i > 0) { const pa = (i - 1) >> 1; if (heap[pa][0] <= heap[i][0]) break; [heap[pa], heap[i]] = [heap[i], heap[pa]]; i = pa; } };
    const pop = () => { const top = heap[0], last = heap.pop(); if (heap.length) { heap[0] = last; let i = 0; for (;;) { const l = 2 * i + 1, r = l + 1; let m2 = i; if (l < heap.length && heap[l][0] < heap[m2][0]) m2 = l; if (r < heap.length && heap[r][0] < heap[m2][0]) m2 = r; if (m2 === i) break; [heap[m2], heap[i]] = [heap[i], heap[m2]]; i = m2; } } return top; };
    cost[start] = 0; push(h(start), start);
    while (heap.length) {
      const k = pop()[1]; if (done[k]) continue; done[k] = 1;
      if (k === goal) break;
      const c = k % cols, r = (k - c) / cols;
      for (const [dc, dr] of [[1, 0], [-1, 0], [0, 1], [0, -1], [1, 1], [1, -1], [-1, 1], [-1, -1]]) {
        const nc = c + dc, nr = r + dr; if (!open(nc, nr)) continue;
        if (dc && dr && (!open(c + dc, r) || !open(c, r + dr))) continue;
        const nk = nr * cols + nc, next = cost[k] + (dc && dr ? Math.SQRT2 : 1);
        if (next < cost[nk]) { cost[nk] = next; from[nk] = k; push(next + h(nk), nk); }
      }
    }
    if (from[goal] < 0) return null;
    const route = []; for (let k = goal; k !== start; k = from[k]) route.push(centre(k));
    return route.reverse();
  }

  // ---- Script worker ----
  // The script API from api/ClientJavaScript.java. It runs in a Web Worker (no page, no network); if workers aren't
  // available the same function runs in the page instead.
  // Runs one script. The mirror is the script side's copy of the screen and its compiled scripts, kept between
  // runs; each request brings only what changed (App.scriptDelta). In a worker there is one mirror for the
  // worker's life; on the page (no workers) the runner keeps one.
  function executeScript(r, mirror) {
    if (r.reset || !mirror.texts) { mirror.texts = {}; mirror.bodies = {}; mirror.tagged = {}; mirror.tilemaps = {}; mirror.vars = {}; mirror.instances = {}; }
    if (r.gone) for (const id of r.gone) { delete mirror.texts[id]; delete mirror.bodies[id]; delete mirror.tilemaps[id]; }
    if (r.instances) mirror.instances = r.instances;
    if (r.texts) Object.assign(mirror.texts, r.texts);
    if (r.bodies) Object.assign(mirror.bodies, r.bodies);
    if (r.tilemaps) Object.assign(mirror.tilemaps, r.tilemaps);
    if (r.tagged) mirror.tagged = r.tagged;
    if (r.vars) Object.assign(mirror.vars, r.vars);
    if (r.varsGone) for (const k of r.varsGone) delete mirror.vars[k];
    r = Object.assign({}, r, { texts: mirror.texts, bodies: mirror.bodies, tagged: mirror.tagged, tilemaps: mirror.tilemaps, vars: mirror.vars, instances: mirror.instances || {} });
    const ops = [], vars = {}, logs = [];
    // Tilemaps arrive run-length encoded and are unpacked only if a script asks about one, once per script run.
    const grids = {};
    const grid = id => {
      const m = r.tilemaps && r.tilemaps[String(id)]; if (!m) return null;
      if (!grids[id]) {
        const g = new Int32Array(Math.max(0, m.columns * m.rows)).fill(-1);
        let at = 0;
        for (const run of String(m.tiles || '').split(' ')) {
          if (!run) continue;
          const star = run.indexOf('*'), index = parseInt(star < 0 ? run : run.slice(0, star), 10);
          const count = star < 0 ? 1 : parseInt(run.slice(star + 1), 10);
          if (!Number.isFinite(index) || !Number.isFinite(count) || count < 1) continue;
          for (let i = 0; i < count && at < g.length; i++) g[at++] = index;
        }
        grids[id] = { m, g };
      }
      return grids[id];
    };
    const emit = (type, target, v) => { if (ops.length >= r.maxOps) throw new Error('Script output exceeds ' + r.maxOps + ' operations'); if (type === 'set_variable') vars[target] = String(v); ops.push([type, String(target === undefined ? '' : target), String(v === undefined ? '' : v)]); };
    const unsupported = () => { throw new Error('Use a built-in Server event action for navigation or server commands'); };
    const setItem = (id, resource) => { if (typeof resource !== 'string' || !/^[a-z0-9_.-]+:[a-z0-9/._-]+$/.test(resource)) throw new Error('setItem requires a namespaced item ID, e.g. minecraft:diamond'); emit('set_item', id, resource); };
    const getVariable = n => (n in vars ? vars[n] : (r.vars[n] === undefined ? '' : r.vars[n]));
    const ui = Object.freeze({
      setText: (id, v) => emit('set_text', id, v), setValue: (id, v) => emit('set_value', id, v), setItem,
      setItems: (id, items) => emit('set_value', id, JSON.stringify(items)),
      setVisible: (id, v) => emit('set_visible', id, !!v), setEnabled: (id, v) => emit('set_enabled', id, !!v),
      changeTexture: (id, v) => emit('change_texture', id, v), getVariable, setVariable: (n, v) => emit('set_variable', n, v),
      // Every control carrying a tag, in the order they are drawn. Empty when nothing has it.
      findByTag: tag => ((r.tagged && r.tagged[String(tag)]) || []).slice(),
      getTile: (id, column, row) => { const t = grid(id); return t && column >= 0 && row >= 0 && column < t.m.columns && row < t.m.rows ? t.g[row * t.m.columns + column] : -1; },
      setTile: (id, column, row, index) => {
        const t = grid(id); column = Math.floor(column); row = Math.floor(row); index = Math.floor(index);
        if (t && column >= 0 && row >= 0 && column < t.m.columns && row < t.m.rows) t.g[row * t.m.columns + column] = index;
        emit('set_tile', id, column + ',' + row + ',' + index);
      },
      fillTiles: (id, column, row, width, height, index) => {
        const t = grid(id); column = Math.floor(column); row = Math.floor(row); width = Math.floor(width); height = Math.floor(height); index = Math.floor(index);
        if (t) for (let y = row; y < row + height; y++) for (let x = column; x < column + width; x++)
          if (x >= 0 && y >= 0 && x < t.m.columns && y < t.m.rows) t.g[y * t.m.columns + x] = index;
        emit('fill_tiles', id, column + ',' + row + ',' + width + ',' + height + ',' + index);
      },
      // The tile under a point in screen coordinates, for "what am I standing on".
      tileAt: (id, x, y) => {
        const t = grid(id); if (!t) return { column: -1, row: -1, tile: -1 };
        const column = Math.floor((x - t.m.x) / t.m.tileWidth), row = Math.floor((y - t.m.y) / t.m.tileHeight);
        const inside = column >= 0 && row >= 0 && column < t.m.columns && row < t.m.rows;
        return { column, row, tile: inside ? t.g[row * t.m.columns + column] : -1 };
      },
      tileSize: id => { const t = grid(id); return t ? { columns: t.m.columns, rows: t.m.rows, tileWidth: t.m.tileWidth, tileHeight: t.m.tileHeight } : null; },
      hasTag: (id, tag) => { const b = r.bodies && r.bodies[id]; return !!(b && b.tags && b.tags.indexOf(String(tag)) >= 0); },
      getElement: id => { const b = (r.bodies && r.bodies[id]) || {}; return Object.freeze({ id, text: r.texts[id] === undefined ? '' : r.texts[id], x: b.x, y: b.y, width: b.width, height: b.height, vx: b.vx, vy: b.vy, visible: b.visible, frame: b.frame || 0, clip: b.clip || '', clipStep: b.clipStep || 0, clipDone: !!b.clipDone, state: b.state || '', tags: (b.tags || []).slice(), setText: v => emit('set_text', id, v), setItem: v => setItem(id, v) }); },
      close: () => emit('close_ui', '', ''), open: r.isServer ? (id => emit('open_ui', '', id)) : unsupported,
      play: (id, clip) => emit('set_value', id, clip),
      animate: (name) => emit('play_animation', name, ''), stopAnimation: (name) => emit('stop_animation', name, ''),
      emit: (id) => emit('emit_particles', id, ''), stopEmit: (id, clear) => emit('stop_particles', id, clear ? 'clear' : ''),
      setVelocity: (id, vx, vy) => emit('set_velocity', id, Number(vx) + ',' + Number(vy)),
      setPosition: (id, x, y) => emit('set_position', id, Number(x) + ',' + Number(y)),
      // A value for a "uniform float u_name" in the screen's shader.
      setShaderValue: (name, value) => emit('set_shader_value', name, Number(value)),
      // Forces a state graph into a state, for the cases the conditions can't express.
      setState: (graph, state) => emit('set_state', graph, state),
      setSize: (id, w, h) => emit('set_size', id, Number(w) + ',' + Number(h)),
      // A Sound control's volume, 0-1, applied at once if it is playing (web and desktop).
      setVolume: (id, volume) => emit('set_volume', id, String(Math.min(1, Math.max(0, Number(volume) || 0)))),
      // Spawned objects (web and desktop): a copy of the template control at x, y (its top-left, like setPosition),
      // returning the copy's ID. options: vx, vy (a velocity), seek (a control's ID) and speed, life (seconds),
      // clip (a sprite clip), texture. The copy exists once this script run ends: later calls in the same run can
      // move it or despawn it, but getElement sees it from the next run.
      spawn: (template, x, y, options) => {
        const o = options || {}, id = String(template) + '~' + (r.spawnSeq++);
        const n = v => (typeof v === 'number' && Number.isFinite(v) ? v : undefined);
        emit('spawn', template, JSON.stringify({ id, x: Number(x) || 0, y: Number(y) || 0, vx: n(o.vx), vy: n(o.vy), separate: n(o.separate), after: o.after === undefined ? undefined : String(o.after), seek: o.seek === undefined ? undefined : String(o.seek), path: o.path === undefined ? undefined : String(o.path), speed: n(o.speed), life: n(o.life), clip: o.clip === undefined ? undefined : String(o.clip), texture: o.texture === undefined ? undefined : String(o.texture) }));
        return id;
      },
      despawn: id => emit('despawn', id, ''),
      // Keep copies at least px apart (centre to centre): a copy's ID, or a template for all its copies, now and later.
      separate: (id, px) => emit('separate', id, String(Math.max(0, Number(px) || 0))),
      // Keep moving toward another control at speed (pixels a second); seek(id, '') stops.
      seek: (id, target, speed, options) => emit('seek', id, String(target || '') + ',' + (Number(speed) || 0) + (options && options.path ? ',' + String(options.path) : '')),
      // The IDs of a template's live copies, oldest first.
      instancesOf: template => ((r.instances && r.instances[String(template)]) || []).slice()
    });
    const p = r.player;
    const ctx = Object.freeze({
      ui, elementId: r.element, value: r.value, repeat: r.repeat, other: r.other || '',
      state: Object.freeze({ get: getVariable, set: ui.setVariable }), getVariable, setVariable: ui.setVariable,
      message: v => emit('message', '', v),
      // playSound(id, volume): volume 0-1 is optional (web and desktop; Minecraft plays at its own volume).
      client: Object.freeze({ sendMessage: v => emit('message', '', v), playSound: (v, volume) => emit('play_sound', volume === undefined ? '' : String(Math.min(1, Math.max(0, Number(volume) || 0))), v) }),
      input: Object.freeze({ isDown: name => !!(r.inputs && r.inputs[name]), axis: name => (r.axes && r.axes[name]) || 0, isTouch: () => !!r.touch }),
      // What a control is touching when the event started: solid contacts and trigger overlaps.
      physics: Object.freeze({
        touching: id => ((r.touching && r.touching[id]) || []).slice(),
        isTouching: (a, b) => !!(r.touching && r.touching[a] && r.touching[a].includes(String(b))),
        // The first solid thing a line meets (web and desktop): { id, x, y, distance, normal } or null.
        raycast: (x1, y1, x2, y2, options) => raycastWorld(r.bodies, r.tilemaps, grid, Number(x1), Number(y1), Number(x2), Number(y2), options),
        // A route over a tilemap's open tiles (web and desktop): the tile centres to walk through, or null.
        findPath: (map, x1, y1, x2, y2) => { const m = r.tilemaps && r.tilemaps[String(map)], g = grid(String(map)); return m && g ? findTilePath(m, g, Number(x1), Number(y1), Number(x2), Number(y2)) : null; },
        // Whether nothing solid stands between two controls' centres (neither of them counts).
        canSee: (a, b, options) => {
          const A = r.bodies[a], B = r.bodies[b]; if (!A || !B) return false;
          const o = Object.assign({}, options || {}); o.ignore = [a, b].concat(o.ignore === undefined ? [] : o.ignore);
          return !raycastWorld(r.bodies, r.tilemaps, grid, A.x + A.width / 2, A.y + A.height / 2, B.x + B.width / 2, B.y + B.height / 2, o);
        }
      }),
      player: r.isServer ? Object.freeze({ getName: () => p.name, getUuid: () => p.uuid, getPosition: () => JSON.parse(JSON.stringify(p.position)), getInventory: () => JSON.parse(JSON.stringify(p.inventory)), hasPermission: level => (p.permission || 0) >= level }) : undefined,
      server: Object.freeze({ runCommand: r.isServer ? (c => emit('command', '', c)) : unsupported, sendMessage: r.isServer ? (t => emit('message', '', t)) : unsupported })
    });
    const logger = level => function () { const parts = []; for (let i = 0; i < arguments.length; i++) parts.push(typeof arguments[i] === 'string' ? arguments[i] : JSON.stringify(arguments[i])); logs.push({ level, text: parts.join(' ') }); };
    const scriptConsole = Object.freeze({ log: logger('log'), info: logger('log'), warn: logger('warn'), error: logger('error') });
    let error = '';
    try {
      const fn = r.fn && /^[A-Za-z_$][\w$]*$/.test(r.fn) ? r.fn : '';
      // Compiled once per source and function, then reused: running it still starts the script from the top, as
      // before, but a 70 KB script is no longer parsed again on every tick.
      if (!mirror.compiled) { mirror.compiled = new Map(); mirror.sources = new Map(); }
      const key = r.sourceKey ? r.sourceKey + '|' + fn : '';
      let run = key ? mirror.compiled.get(key) : null;
      if (!run) {
        let source = r.source;
        if (r.sourceKey) { if (source === undefined) source = mirror.sources.get(r.sourceKey); else mirror.sources.set(r.sourceKey, source); }
        if (source === undefined) throw new Error('Script source missing; it will be sent again');
        run = new Function('ctx', 'ui', 'console', source + '\n;return ' + (fn ? 'typeof ' + fn + ' === "function" ? ' + fn + ' : undefined' : 'undefined') + ';');
        if (key) { if (mirror.compiled.size > 128) mirror.compiled.clear(); mirror.compiled.set(key, run); }
      }
      const callback = run(ctx, ui, scriptConsole);
      if (fn) { if (!callback) throw new Error('Function not found: ' + fn); callback(ctx); }
    } catch (ex) { error = ex && ex.message ? ex.message : String(ex); }
    // Variables the script set are now known to the mirror too, so the page need not send them back.
    if (!error) for (const k in vars) mirror.vars[k] = vars[k];
    return { ops: error ? [] : ops, logs, error };
  }
  function workerSetup() {
    const post = self.postMessage.bind(self);
    // Scripts get the Wysicraft API only: no network or storage, as in Minecraft.
    const blocked = ['fetch', 'XMLHttpRequest', 'WebSocket', 'WebSocketStream', 'EventSource', 'importScripts', 'indexedDB', 'caches', 'BroadcastChannel', 'Worker', 'SharedWorker', 'Request', 'Response', 'navigator'];
    for (let o = self; o; o = Object.getPrototypeOf(o)) for (const k of blocked) { try { if (Object.prototype.hasOwnProperty.call(o, k)) delete o[k]; } catch (e) { } }
    for (const k of blocked) { try { Object.defineProperty(self, k, { value: undefined, writable: false, configurable: false }); } catch (e) { } }
    const mirror = {};
    self.onmessage = e => post(Object.assign({ id: e.data.id }, executeScript(e.data, mirror)));
  }
  class ScriptRunner {
    // generation changes whenever the script side starts over (a new worker, or falling back to the page), which
    // tells the page to send a full copy of the screen again. known is which sources the worker already holds.
    constructor() { this.worker = null; this.inline = false; this.pending = new Map(); this.nextId = 1; this.generation = 0; this.known = new Set(); this.mirror = {}; }
    // Starts the worker if there isn't one, before the page works out what it needs to send.
    prepare() { if (!this.worker && !this.inline) this.start(); }
    start() {
      this.generation++; this.known = new Set();
      try {
        const url = URL.createObjectURL(new Blob([[executeScript, raycastWorld, findTilePath, solidTiles].map(f => f.toString()).join('\n') + '\n(' + workerSetup.toString() + ')();'], { type: 'text/javascript' }));
        this.worker = new Worker(url);
        this.worker.onmessage = e => { const p = this.pending.get(e.data.id); if (!p) return; this.pending.delete(e.data.id); clearTimeout(p.timer); p.resolve(e.data); };
        // A worker that can't start (some file:// setups) falls back to running scripts in the page.
        this.worker.onerror = e => { if (e && e.preventDefault) e.preventDefault(); if (this.started) return; this.fallback(); };
      } catch (ex) { this.inline = true; }
    }
    fallback() {
      this.inline = true; if (this.worker) this.worker.terminate(); this.worker = null;
      this.generation++; this.known = new Set(); this.mirror = {};
      const waiting = [...this.pending.values()]; this.pending.clear();
      for (const p of waiting) { clearTimeout(p.timer); p.resolve(executeScript(p.request, this.mirror)); }
    }
    run(request) {
      if (!this.worker && !this.inline) this.start();
      if (this.inline) return Promise.resolve(executeScript(request, this.mirror));
      const id = this.nextId++;
      return new Promise(resolve => {
        const timer = setTimeout(() => {
          // Stuck: stop the worker (and anything queued behind it) and start a fresh one next time.
          this.pending.delete(id); this.worker.terminate(); this.worker = null;
          for (const p of this.pending.values()) { clearTimeout(p.timer); p.resolve({ ops: [], logs: [], error: 'Stopped because another script ran too long' }); }
          this.pending.clear();
          resolve({ ops: [], logs: [], error: 'Script ran longer than 2 seconds and was stopped' });
        }, 2000);
        this.pending.set(id, { resolve: result => { this.started = true; resolve(result); }, timer, request });
        // A source the worker already holds is not copied across again: arena.js alone is 70 KB.
        const message = Object.assign({ id }, request);
        if (request.sourceKey) { if (this.known.has(request.sourceKey)) delete message.source; else this.known.add(request.sourceKey); }
        this.worker.postMessage(message);
      });
    }
  }

  window.Wysicraft = {
    version: '1.0.0',
    start(container, project, host, options) { const app = new App(container, project || window.WYSICRAFT_PROJECT, host || window.wysicraftHost || {}, options || {}); window.Wysicraft.app = app.api(); window.Wysicraft._app = app; return window.Wysicraft.app; },
    _internals: { bind, evaluate, parseAnimation, resolveLayout, keyName }
  };
})();
