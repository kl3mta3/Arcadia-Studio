// Blockfall: a falling-block puzzle that runs entirely in a Wysicraft client script.
//
// Board: 10 x 20 cells kept in screen variable "board" as a 200-character string
//   ('.' empty, I O T S Z J L = locked blocks, row by row from the top).
// Drawing: 100 image controls named b<row>_<pair>; each shows two neighbouring cells
//   using one of 81 textures p_<left><right>.png (e empty, i o t s z j l colors, g ghost).
// Time: the game screen's Tick event runs tick() every 250 ms (Tick interval in Screen settings).
// Keys: the screen's Key event runs key() with the key's name (held keys repeat every 150 ms, never queued):
//   A/D or Left/Right move, W or Up rotate,
//   S or Down soft drop, Space hard drop, P pause, R or Enter play again after game over.
// Budget: a script call may emit at most 128 UI operations. Only changed tiles are redrawn,
//   and anything that doesn't fit is finished on the next call.

var W = 10, H = 20, PAIRS = 5, MAX_OPS = 124;
var TEX = 'blockfall:textures/gui/image/';
var ORDER = 'IOTSZJL';
var LETTER = { '.': 'e', I: 'i', O: 'o', T: 't', S: 's', Z: 'z', J: 'j', L: 'l', g: 'g' };
// Each piece: four rotations of four [column, row] cells inside a 4x4 box.
var SHAPES = {
  I: [[[0,1],[1,1],[2,1],[3,1]], [[2,0],[2,1],[2,2],[2,3]], [[0,2],[1,2],[2,2],[3,2]], [[1,0],[1,1],[1,2],[1,3]]],
  O: [[[1,0],[2,0],[1,1],[2,1]], [[1,0],[2,0],[1,1],[2,1]], [[1,0],[2,0],[1,1],[2,1]], [[1,0],[2,0],[1,1],[2,1]]],
  T: [[[1,0],[0,1],[1,1],[2,1]], [[1,0],[1,1],[2,1],[1,2]], [[0,1],[1,1],[2,1],[1,2]], [[1,0],[0,1],[1,1],[1,2]]],
  S: [[[1,0],[2,0],[0,1],[1,1]], [[1,0],[1,1],[2,1],[2,2]], [[1,1],[2,1],[0,2],[1,2]], [[0,0],[0,1],[1,1],[1,2]]],
  Z: [[[0,0],[1,0],[1,1],[2,1]], [[2,0],[1,1],[2,1],[1,2]], [[0,1],[1,1],[1,2],[2,2]], [[1,0],[0,1],[1,1],[0,2]]],
  J: [[[0,0],[0,1],[1,1],[2,1]], [[1,0],[2,0],[1,1],[1,2]], [[0,1],[1,1],[2,1],[2,2]], [[1,0],[1,1],[0,2],[1,2]]],
  L: [[[2,0],[0,1],[1,1],[2,1]], [[1,0],[1,1],[1,2],[2,2]], [[0,1],[1,1],[2,1],[0,2]], [[0,0],[1,0],[1,1],[1,2]]]
};
// Gravity: game ticks (250 ms each) per one-row fall, by level.
var FALL_TICKS = [4, 4, 3, 3, 2, 2, 2, 1, 1, 1];
var LINE_POINTS = [0, 100, 300, 500, 800];
var VARS = ['mode', 'board', 'piece', 'next', 'bag', 'score', 'lines', 'level', 'best', 'timer', 'landed', 'shown'];

function emptyBoard() { var b = ''; for (var i = 0; i < W * H; i++) b += '.'; return b; }
function num(v) { var n = Number(v); return isNaN(n) ? 0 : n; }

// ---- State -------------------------------------------------------------------------------
function load(ctx) {
  var s = { ctx: ctx, before: {}, ops: [] };
  for (var i = 0; i < VARS.length; i++) s.before[VARS[i]] = ctx.state.get(VARS[i]) || '';
  s.mode = s.before.mode || 'idle';
  s.board = s.before.board.length === W * H ? s.before.board : emptyBoard();
  s.shown = s.before.shown.length === W * H ? s.before.shown : emptyBoard(); // design shows empty tiles
  s.piece = parsePiece(s.before.piece);
  s.next = s.before.next;
  s.bag = s.before.bag;
  s.score = num(s.before.score); s.lines = num(s.before.lines); s.level = Math.max(1, num(s.before.level));
  s.best = num(s.before.best); s.timer = num(s.before.timer); s.landed = s.before.landed === '1';
  return s;
}
function parsePiece(text) {
  if (!text) return null;
  var p = text.split(',');
  return { type: p[0], rot: num(p[1]), x: num(p[2]), y: num(p[3]) };
}
function pieceText(p) { return p ? p.type + ',' + p.rot + ',' + p.x + ',' + p.y : ''; }

// ---- Rules -------------------------------------------------------------------------------
function cellsOf(p, rot, x, y) {
  var shape = SHAPES[p.type][rot], out = [];
  for (var i = 0; i < 4; i++) out.push([x + shape[i][0], y + shape[i][1]]);
  return out;
}
function fits(s, p, rot, x, y) {
  var cells = cellsOf(p, rot, x, y);
  for (var i = 0; i < 4; i++) {
    var cx = cells[i][0], cy = cells[i][1];
    if (cx < 0 || cx >= W || cy >= H) return false;
    if (cy >= 0 && s.board.charAt(cy * W + cx) !== '.') return false;
  }
  return true;
}
// 7-piece bag: every piece once per seven, in random order.
function drawPiece(s) {
  if (!s.bag) {
    var letters = ORDER.split('');
    for (var i = letters.length - 1; i > 0; i--) { var j = Math.floor(Math.random() * (i + 1)); var t = letters[i]; letters[i] = letters[j]; letters[j] = t; }
    s.bag = letters.join('');
  }
  var next = s.bag.charAt(0); s.bag = s.bag.substring(1);
  return next;
}
function spawn(s) {
  var type = s.next || drawPiece(s);
  s.next = drawPiece(s);
  s.piece = { type: type, rot: 0, x: 3, y: type === 'I' ? -1 : 0 };
  s.landed = false; s.timer = 0;
  if (!fits(s, s.piece, 0, s.piece.x, s.piece.y)) gameOver(s);
}
function lock(s) {
  var cells = cellsOf(s.piece, s.piece.rot, s.piece.x, s.piece.y), board = s.board.split('');
  for (var i = 0; i < 4; i++) {
    if (cells[i][1] < 0) { gameOver(s); return; }
    board[cells[i][1] * W + cells[i][0]] = s.piece.type;
  }
  // Clear full rows and drop everything above them.
  var rows = [], cleared = 0;
  for (var r = 0; r < H; r++) {
    var row = board.slice(r * W, r * W + W).join('');
    if (row.indexOf('.') < 0) cleared++; else rows.push(row);
  }
  var top = ''; for (var k = 0; k < cleared * W; k++) top += '.';
  s.board = top + rows.join('');
  if (cleared > 0) {
    s.lines += cleared;
    s.score += LINE_POINTS[cleared] * s.level;
    s.level = Math.min(10, 1 + Math.floor(s.lines / 10));
  }
  s.piece = null;
  spawn(s);
}
function gameOver(s) {
  s.mode = 'over';
  s.piece = null;
  if (s.score > s.best) s.best = s.score;
}
function move(s, dx) {
  if (s.mode !== 'playing' || !s.piece) return;
  if (fits(s, s.piece, s.piece.rot, s.piece.x + dx, s.piece.y)) { s.piece.x += dx; s.landed = false; }
}
function rotate(s) {
  if (s.mode !== 'playing' || !s.piece) return;
  var rot = (s.piece.rot + 1) % 4, kicks = [[0,0],[-1,0],[1,0],[-2,0],[2,0],[0,-1]];
  for (var i = 0; i < kicks.length; i++) {
    var x = s.piece.x + kicks[i][0], y = s.piece.y + kicks[i][1];
    if (fits(s, s.piece, rot, x, y)) { s.piece.rot = rot; s.piece.x = x; s.piece.y = y; s.landed = false; return; }
  }
}
function softDrop(s) {
  if (s.mode !== 'playing' || !s.piece) return;
  if (fits(s, s.piece, s.piece.rot, s.piece.x, s.piece.y + 1)) { s.piece.y++; s.score += 1; s.timer = 0; }
  else lock(s);
}
function hardDrop(s) {
  if (s.mode !== 'playing' || !s.piece) return;
  while (fits(s, s.piece, s.piece.rot, s.piece.x, s.piece.y + 1)) { s.piece.y++; s.score += 2; }
  lock(s);
}
function gravity(s) {
  if (s.mode !== 'playing' || !s.piece) return;
  s.timer++;
  if (s.timer < FALL_TICKS[s.level - 1]) return;
  s.timer = 0;
  if (fits(s, s.piece, s.piece.rot, s.piece.x, s.piece.y + 1)) { s.piece.y++; s.landed = false; }
  else if (s.landed) lock(s);   // one fall step of grace after landing, to slide or rotate
  else s.landed = true;
}
function reset(s) {
  s.board = emptyBoard(); s.score = 0; s.lines = 0; s.level = 1; s.bag = ''; s.next = '';
  s.mode = 'playing'; spawn(s);
}

// ---- Drawing -----------------------------------------------------------------------------
// What the board should look like: locked blocks, the landing ghost, then the falling piece.
function targetBoard(s) {
  var view = s.board.split('');
  if (s.piece && s.mode !== 'over') {
    var gy = s.piece.y;
    while (fits(s, s.piece, s.piece.rot, s.piece.x, gy + 1)) gy++;
    var ghost = cellsOf(s.piece, s.piece.rot, s.piece.x, gy), cells = cellsOf(s.piece, s.piece.rot, s.piece.x, s.piece.y), i;
    for (i = 0; i < 4; i++) if (ghost[i][1] >= 0) view[ghost[i][1] * W + ghost[i][0]] = 'g';
    for (i = 0; i < 4; i++) if (cells[i][1] >= 0) view[cells[i][1] * W + cells[i][0]] = s.piece.type;
  }
  return view.join('');
}
function finish(s) {
  var ui = s.ctx.ui, ops = [];
  var b = s.before, fresh = b.mode !== s.mode;
  // Labels and overlays: only when something changed.
  if (fresh || num(b.score) !== s.score) ops.push(function(){ ui.setText('score_value', String(s.score)); });
  if (fresh || num(b.lines) !== s.lines) ops.push(function(){ ui.setText('lines_value', String(s.lines)); });
  if (fresh || Math.max(1, num(b.level)) !== s.level) ops.push(function(){ ui.setText('level_value', String(s.level)); });
  if (fresh || num(b.best) !== s.best) ops.push(function(){ ui.setText('best_value', String(s.best)); });
  if (fresh || b.next !== s.next) ops.push(function(){ ui.changeTexture('next', TEX + 'next_' + (s.next && s.mode !== 'over' ? s.next.toLowerCase() : 'none') + '.png'); });
  if (fresh) {
    ops.push(function(){ ui.setVisible('pause_panel', s.mode === 'paused'); });
    ops.push(function(){ ui.setVisible('over_panel', s.mode === 'over'); });
    if (s.mode === 'over') ops.push(function(){ ui.setText('over_score', 'Score ' + s.score + (s.score > 0 && s.score >= s.best ? '  (new best!)' : '')); });
  }
  // Variables to save (only those that changed), plus "shown" if any tile is redrawn.
  var after = { mode: s.mode, board: s.board, piece: pieceText(s.piece), next: s.next, bag: s.bag, score: String(s.score),
                lines: String(s.lines), level: String(s.level), best: String(s.best), timer: String(s.timer), landed: s.landed ? '1' : '0' };
  var saves = [];
  for (var key in after) if (after[key] !== b[key]) saves.push(key);
  // Tiles: redraw changed pairs within the remaining budget.
  var target = targetBoard(s), shown = s.shown.split(''), room = MAX_OPS - ops.length - saves.length - 1;
  for (var r = 0; r < H && room > 0; r++) {
    for (var p = 0; p < PAIRS && room > 0; p++) {
      var i = r * W + p * 2;
      if (target.charAt(i) === shown[i] && target.charAt(i + 1) === shown[i + 1]) continue;
      shown[i] = target.charAt(i); shown[i + 1] = target.charAt(i + 1);
      (function(id, tex){ ops.push(function(){ ui.changeTexture(id, tex); }); })('b' + r + '_' + p, TEX + 'p_' + LETTER[shown[i]] + LETTER[shown[i + 1]] + '.png');
      room--;
    }
  }
  after.shown = shown.join('');
  if (after.shown !== b.shown) saves.push('shown');
  for (var o = 0; o < ops.length; o++) ops[o]();
  for (var v = 0; v < saves.length; v++) s.ctx.state.set(saves[v], after[saves[v]]);
}

// ---- Event handlers (assigned in the editor) ---------------------------------------------
function run(ctx, action) { var s = load(ctx); action(s); finish(s); }
function start(ctx)      { run(ctx, reset); }                       // Game screen opens
function restart(ctx)    { run(ctx, reset); }                       // Play again
function tick(ctx)       { run(ctx, gravity); }                     // screen Tick, every 250 ms
function moveLeft(ctx)   { run(ctx, function(s){ move(s, -1); }); }
function moveRight(ctx)  { run(ctx, function(s){ move(s, 1); }); }
function turn(ctx)       { run(ctx, rotate); }
function down(ctx)       { run(ctx, softDrop); }
function drop(ctx)       { run(ctx, hardDrop); }
function togglePause(ctx) {
  run(ctx, function(s){ if (s.mode === 'playing') s.mode = 'paused'; else if (s.mode === 'paused') s.mode = 'playing'; });
}
// Screen Key event: ctx.value is the key's name ("a", "left", "space", "enter", ...).
// Holding a key repeats it at the screen's Key repeat rate (ctx.repeat is true). Moving and soft drop
// repeat; drop, rotate, pause and restart need a fresh press, so holding Space can't drop piece after piece.
function key(ctx) {
  var k = String(ctx.value || '');
  if (ctx.repeat && !(k === 'a' || k === 'left' || k === 'd' || k === 'right' || k === 's' || k === 'down')) return;
  run(ctx, function(s){
    if (k === 'a' || k === 'left') move(s, -1);
    else if (k === 'd' || k === 'right') move(s, 1);
    else if (k === 'w' || k === 'up') rotate(s);
    else if (k === 's' || k === 'down') softDrop(s);
    else if (k === 'space') { if (s.mode === 'over') reset(s); else hardDrop(s); }
    else if (k === 'p') { if (s.mode === 'playing') s.mode = 'paused'; else if (s.mode === 'paused') s.mode = 'playing'; }
    else if ((k === 'r' || k === 'enter') && s.mode === 'over') reset(s);
  });
}
