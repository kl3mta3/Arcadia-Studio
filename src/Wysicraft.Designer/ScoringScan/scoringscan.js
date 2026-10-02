// The leaderboard scan: reads a game's code and finds what holds the score, what else is worth
// showing on the board (level, wave, time…), and what means a run has ended. The server runs it on
// every game (lib/autoscore.js fetches the game's files and calls analyzeSources); Arcadia Studio
// can run this same file on a project before publishing, so both come to the same answer.
//
// No imports and no I/O: plain JavaScript that runs in Node or a browser.
//
// Input, one of:
//   analyzeSources({ project })            an Arcadia Studio project (what the web export writes as
//                                          window.WYSICRAFT_PROJECT = {...}: screens, scripts, ...)
//   analyzeSources({ texts })              a plain JS game: [{ file, text }] for its HTML and scripts
//   analyzeSources({ project, texts })     both (texts are only used to spot Arcadia.submitScore)
// findWysicraftProject(text) pulls the project object out of an exported page or project.js.
//
// Output:
//   { engine: 'wysicraft' | 'script', usesSdk, project: { id, name, screens, scripts } | null,
//     suggestions: { score: [...], trigger: [...], stats: [...] },   best first, each with a
//        source ({ kind: 'wysicraft', variable, path } or { kind: 'global', name, path }), a score
//        (how sure), evidence ([{ file, line, code }]) and label / key / equals as they apply
//     recommended: { score: source | null, trigger: { any: [{ ...source, equals }] } | null, board } }
// confident(analysis) says whether the server would set scoring up by itself from this result
// (a clear score and a clear end-of-run signal). describe(source) / describeTrigger(trigger) give
// the short text the mod panel shows, e.g. "g.score" and "g.dead OR g.phase = over".

const IDENT = /^[A-Za-z_$][\w$]*$/;
// Names that look like a score, a stat worth showing, and "the run is over".
const SCORE_WORDS = /^(score|points?|pts|xp|exp|experience|coins?|gold|gems?|stars?|distance|dist|meters|kills?|total)$/i;
const STAT_WORDS = /^(level|lvl|wave|stage|floor|depth|round|lives|kills?|combo|smash\w*|coins?|gems?|gold|time|seconds|distance|dist|height|lines|rows|cleared|best|highscore|max\w*)$/i;
const END_WORDS = /^(over|game_?over|gameover|dead|died|death|lose|lost|end|ended|finished|done|results?)$/i;
// true/false flags that mean a run is over (win or lose): g.dead = true, gameOver = true, g.win = true
const FLAG_WORDS = /^(is_?|has_?)?(game_?over|over|dead|died|lost|lose|win|won|wins|victory|complete|completed|cleared|finished|escaped|ended)$/i;
const WIN_WORDS = /^(is_?|has_?)?(win|won|wins|victory|complete|completed|cleared|finished|escaped)$/i;

// "Run ended" trigger: one or more conditions, any of which fires it (OR). Each condition is a
// source plus either equals: 'over' (value check) or equals: '' (just "is true / set").
// Older settings stored a single condition ({ ...source, equals }), so both shapes are accepted.
export const conditionsOf = (t) => (Array.isArray(t?.any) ? t.any : t ? [t] : []);

export const describe = (s) => (s.kind === 'wysicraft'
  ? `${s.variable}${s.path ? '.' + s.path : ''}`
  : `${s.name}${s.path ? '.' + s.path : ''}`);
export const describeCondition = (c) => (c.equals === '' || c.equals == null || c.test === 'truthy'
  ? describe(c)
  : `${describe(c)} = ${c.equals}`);
export const describeTrigger = (t) => conditionsOf(t).map(describeCondition).join(' OR ');

// ---------------------------------------------------------------------------
// Scanner

const lineOf = (text, index) => text.slice(0, index).split('\n').length;
const snippetAt = (text, index) => {
  const start = text.lastIndexOf('\n', index) + 1;
  const end = text.indexOf('\n', index);
  return text.slice(start, end === -1 ? undefined : end).trim().slice(0, 160);
};

function addCandidate(map, key, cand) {
  const existing = map.get(key);
  if (existing) {
    existing.score += cand.score;
    for (const ev of cand.evidence || []) {
      if (existing.evidence.length < 3 && !existing.evidence.some((e) => e.code === ev.code)) existing.evidence.push(ev);
    }
  } else {
    map.set(key, { ...cand, evidence: [...(cand.evidence || [])] });
  }
}

const labelFor = (name) => {
  const last = name.split('.').pop();
  const nice = { xp: 'XP', exp: 'XP', pts: 'Points', dist: 'Distance', lvl: 'Level' }[last.toLowerCase()];
  return nice || last.replace(/_/g, ' ').replace(/^\w/, (c) => c.toUpperCase());
};

function scanWysicraft(project) {
  const scripts = Object.entries(project.scripts || {}).map(([file, text]) => ({ file, text: String(text) }));
  const scoreC = new Map();
  const statC = new Map();
  const trigC = new Map();

  // Screen variables and their defaults.
  const screenVars = new Map();
  for (const [screenId, screen] of Object.entries(project.screens || {})) {
    for (const [name, value] of Object.entries(screen.variables || {})) screenVars.set(name, { screen: screenId, value });
  }

  // visibleIf / enabledIf rules like  mode == 'over'  are strong hints for the end-of-run trigger.
  JSON.stringify(project.screens || {}, (k, v) => {
    if (typeof v === 'string' && /If$/.test(k)) {
      for (const m of v.matchAll(/([A-Za-z_$][\w$]*)\s*==\s*['"]([\w -]+)['"]/g)) {
        const [, variable, value] = m;
        const s = END_WORDS.test(value) ? 6 : 1;
        addCandidate(trigC, `${variable}=${value}`, {
          source: { kind: 'wysicraft', variable, path: '' }, equals: value, score: s,
          evidence: [{ file: 'screens', line: 0, code: `${k}: ${v}` }],
        });
      }
    }
    return v;
  });

  for (const { file, text } of scripts) {
    // ctx.state.set('name', value): state variables. JSON.stringify(obj) means obj's fields are readable.
    const jsonVars = new Map();
    // The value runs to the end of the statement, so nested calls like JSON.stringify(g) are kept whole.
    for (const m of text.matchAll(/ctx\.state\.set\(\s*['"]([A-Za-z_$][\w$]*)['"]\s*,\s*([^;\n]{0,120})/g)) {
      const [, variable, expr] = m;
      const js = /JSON\.stringify\(\s*([A-Za-z_$][\w$]*)\s*\)/.exec(expr);
      if (js) jsonVars.set(js[1], variable);
      else if (SCORE_WORDS.test(variable)) {
        addCandidate(scoreC, `${variable}`, { source: { kind: 'wysicraft', variable, path: '' }, label: labelFor(variable), score: 3,
          evidence: [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }] });
      }
    }
    // String values assigned to a state variable, directly or through a helper like setMode(ctx, g, 'over').
    const setters = new Map(); // function name -> state variable it sets from its last param
    for (const m of text.matchAll(/function\s+([A-Za-z_$][\w$]*)\s*\(([^)]*)\)\s*\{([^}]{0,200})\}/g)) {
      const [, fn, params, body] = m;
      const last = params.split(',').map((p) => p.trim()).pop();
      const set = new RegExp(`ctx\\.state\\.set\\(\\s*['"]([A-Za-z_$][\\w$]*)['"]\\s*,\\s*${last}\\s*\\)`).exec(body);
      if (last && set) setters.set(fn, set[1]);
    }
    for (const [fn, variable] of setters) {
      for (const m of text.matchAll(new RegExp(`\\b${fn}\\([^)]*?['"]([\\w -]+)['"]\\s*\\)`, 'g'))) {
        const value = m[1];
        if (!END_WORDS.test(value)) continue;
        addCandidate(trigC, `${variable}=${value}`, { source: { kind: 'wysicraft', variable, path: '' }, equals: value, score: 4,
          evidence: [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }] });
      }
    }
    for (const m of text.matchAll(/ctx\.state\.set\(\s*['"]([A-Za-z_$][\w$]*)['"]\s*,\s*['"]([\w -]+)['"]\s*\)/g)) {
      const [, variable, value] = m;
      if (!END_WORDS.test(value)) continue;
      addCandidate(trigC, `${variable}=${value}`, { source: { kind: 'wysicraft', variable, path: '' }, equals: value, score: 4,
        evidence: [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }] });
    }
    // Text fields on JSON variables set to an end word: s.phase = 'over'  ->  game.phase = over
    for (const [obj, variable] of jsonVars) {
      for (const m of text.matchAll(new RegExp(`\\b${obj}\\.([A-Za-z_$][\\w$]*)\\s*=\\s*['"]([\\w -]+)['"]`, 'g'))) {
        const [, field, value] = m;
        if (!END_WORDS.test(value)) continue;
        addCandidate(trigC, `${variable}.${field}=${value}`, { source: { kind: 'wysicraft', variable, path: field }, equals: value, score: 4,
          evidence: [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }] });
      }
    }
    // true/false flags on JSON variables: g.dead = true, g.win = true
    for (const [obj, variable] of jsonVars) {
      for (const m of text.matchAll(new RegExp(`\\b${obj}\\.([A-Za-z_$][\\w$]*)\\s*=\\s*true\\b`, 'g'))) {
        const field = m[1];
        if (!FLAG_WORDS.test(field)) continue;
        addCandidate(trigC, `${variable}.${field}=`, { source: { kind: 'wysicraft', variable, path: field }, equals: '', score: WIN_WORDS.test(field) ? 3 : 4,
          evidence: [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }] });
      }
    }
    // Numeric fields of JSON variables: obj.field += ...  (increments are the best sign of a score).
    for (const [obj, variable] of jsonVars) {
      for (const m of text.matchAll(new RegExp(`\\b${obj}\\.([A-Za-z_$][\\w$]*)\\s*(\\+=|-=|\\+\\+|--|=)`, 'g'))) {
        const [, field, op] = m;
        const path = field;
        const ev = [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }];
        if (SCORE_WORDS.test(field)) {
          addCandidate(scoreC, `${variable}.${path}`, { source: { kind: 'wysicraft', variable, path }, label: labelFor(field), score: op === '+=' ? 5 : 2, evidence: ev });
        } else if (STAT_WORDS.test(field)) {
          addCandidate(statC, `${variable}.${path}`, { source: { kind: 'wysicraft', variable, path }, key: field.toLowerCase().replace(/[^a-z0-9_]/g, '_').slice(0, 32), label: labelFor(field), score: 2, evidence: ev });
        }
      }
    }
  }

  // Plain screen variables with score-like names.
  for (const [name, v] of screenVars) {
    if (SCORE_WORDS.test(name)) {
      addCandidate(scoreC, name, { source: { kind: 'wysicraft', variable: name, path: '' }, label: labelFor(name), score: 2,
        evidence: [{ file: `screen ${v.screen}`, line: 0, code: `variable ${name} = ${JSON.stringify(v.value)}` }] });
    }
  }

  // Plain state variables, however the script reaches them: literal ctx.state.get/set('score'),
  // screen variables, or a list of names read and saved in a loop, e.g.
  //   var VARS = ['mode', 'score', 'lines'];  ...  ctx.state.set(saves[v], after[saves[v]]);
  // Then look at how each name is used: `x.score += 10` means score, `x.mode = 'over'` means the end.
  const stateNames = new Map(); // name -> evidence
  for (const name of screenVars.keys()) stateNames.set(name, null);
  for (const { file, text } of scripts) {
    for (const m of text.matchAll(/ctx\.state\.(?:get|set)\(\s*['"]([A-Za-z_$][\w$]*)['"]/g)) {
      if (!stateNames.has(m[1])) stateNames.set(m[1], { file, line: lineOf(text, m.index), code: snippetAt(text, m.index) });
    }
    if (/\.state\.(?:get|set)\(\s*[^'"\s)]/.test(text)) { // state read or saved by a computed name
      for (const m of text.matchAll(/\[\s*((?:['"][A-Za-z_$][\w$]*['"]\s*,\s*)+['"][A-Za-z_$][\w$]*['"])\s*\]/g)) {
        for (const n of m[1].matchAll(/['"]([A-Za-z_$][\w$]*)['"]/g)) {
          if (!stateNames.has(n[1])) stateNames.set(n[1], { file, line: lineOf(text, m.index), code: snippetAt(text, m.index) });
        }
      }
    }
  }
  const esc = (s) => s.replace(/\$/g, '\\$');
  for (const [name, declared] of stateNames) {
    const src = { kind: 'wysicraft', variable: name, path: '' };
    for (const { file, text } of scripts) {
      const ev = (m) => [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }];
      if (SCORE_WORDS.test(name)) {
        let hits = 0;
        for (const m of text.matchAll(new RegExp(`(?:\\.|\\b)${esc(name)}\\s*(\\+=|\\+\\+)`, 'g'))) {
          if (hits++ >= 3) break;
          addCandidate(scoreC, name, { source: src, label: labelFor(name), score: 2, evidence: ev(m) });
        }
        if (hits) addCandidate(scoreC, name, { source: src, label: labelFor(name), score: 1, evidence: declared ? [declared] : [] });
      } else if (STAT_WORDS.test(name)) {
        const m = new RegExp(`(?:\\.|\\b)${esc(name)}\\s*(\\+=|\\+\\+|=)`).exec(text);
        if (m) addCandidate(statC, name, { source: src, key: name.toLowerCase().slice(0, 32), label: labelFor(name), score: 2, evidence: ev(m) });
      }
      for (const m of text.matchAll(new RegExp(`(?:\\.|\\b)${esc(name)}\\s*=\\s*['"]([\\w -]+)['"]`, 'g'))) {
        if (!END_WORDS.test(m[1])) continue;
        addCandidate(trigC, `${name}=${m[1]}`, { source: src, equals: m[1], score: 4, evidence: ev(m) });
      }
      if (FLAG_WORDS.test(name)) {
        const m = new RegExp(`(?:\\.|\\b)${esc(name)}\\s*=\\s*(true|['"]1['"])`).exec(text);
        if (m) addCandidate(trigC, `${name}=`, { source: src, equals: '', score: WIN_WORDS.test(name) ? 3 : 4, evidence: ev(m) });
      }
    }
  }

  return { scoreC, statC, trigC };
}

function scanScripts(texts) {
  const scoreC = new Map();
  const statC = new Map();
  const trigC = new Map();
  for (const { file, text } of texts) {
    // Top-level-looking declarations: var/let/const name = ...
    for (const m of text.matchAll(/(?:^|\n)\s{0,6}(?:var|let|const)\s+([^;=\n]+?)\s*(?:=|;|\n)/g)) {
      for (const raw of m[1].split(',')) {
        const name = raw.trim().split(/\s|=/)[0];
        if (!IDENT.test(name)) continue;
        const ev = [{ file, line: lineOf(text, m.index + 1), code: snippetAt(text, m.index + 1) }];
        if (SCORE_WORDS.test(name)) addCandidate(scoreC, name, { source: { kind: 'global', name, path: '' }, label: labelFor(name), score: 3, evidence: ev });
        else if (STAT_WORDS.test(name)) addCandidate(statC, name, { source: { kind: 'global', name, path: '' }, key: name.toLowerCase().slice(0, 32), label: labelFor(name), score: 2, evidence: ev });
      }
    }
    // Increments raise confidence: score += 10, score++, and object fields like state.score += 10.
    for (const m of text.matchAll(/(?<![\w$.])([A-Za-z_$][\w$]*)(?:\.([A-Za-z_$][\w$]*))?\s*(\+=|\+\+)/g)) {
      const [, name, field] = m;
      const leaf = field || name;
      const ev = [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }];
      const key = field ? `${name}.${field}` : name;
      if (SCORE_WORDS.test(leaf)) {
        addCandidate(scoreC, key, { source: { kind: 'global', name, path: field || '' }, label: labelFor(leaf), score: field ? 4 : 2, evidence: ev });
      } else if (field && STAT_WORDS.test(field)) {
        addCandidate(statC, key, { source: { kind: 'global', name, path: field }, key: field.toLowerCase().slice(0, 32), label: labelFor(field), score: 2, evidence: ev });
      }
    }
    // State strings: mode = 'over'
    for (const m of text.matchAll(/\b([A-Za-z_$][\w$]*)\s*=\s*['"]([\w -]+)['"]/g)) {
      const [, name, value] = m;
      if (!END_WORDS.test(value)) continue;
      addCandidate(trigC, `${name}=${value}`, { source: { kind: 'global', name, path: '' }, equals: value, score: 4,
        evidence: [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }] });
    }
    // Boolean flags: gameOver = true, dead = true, won = true, and fields like state.win = true
    for (const m of text.matchAll(/\b([A-Za-z_$][\w$]*)(?:\.([A-Za-z_$][\w$]*))?\s*=\s*true\b/g)) {
      const [, name, field] = m;
      const flag = field || name;
      if (!FLAG_WORDS.test(flag)) continue;
      const path = field || '';
      addCandidate(trigC, `${name}${path ? '.' + path : ''}=`, { source: { kind: 'global', name, path }, equals: '', score: WIN_WORDS.test(flag) ? 3 : 4,
        evidence: [{ file, line: lineOf(text, m.index), code: snippetAt(text, m.index) }] });
    }
  }
  return { scoreC, statC, trigC };
}

const ranked = (map, n) => [...map.values()].sort((a, b) => b.score - a.score).slice(0, n);

// Finds `window.WYSICRAFT_PROJECT = { ... }` anywhere in a script or page and parses just that
// object (brace matching that skips over strings), whatever code comes before or after it.
export function findWysicraftProject(text) {
  const m = /window\.WYSICRAFT_PROJECT\s*=\s*/.exec(text);
  if (!m) return null;
  const start = m.index + m[0].length;
  if (text[start] !== '{') return null;
  let depth = 0;
  let quote = null;
  let escaped = false;
  for (let i = start; i < text.length; i++) {
    const c = text[i];
    if (quote) {
      if (escaped) escaped = false;
      else if (c === '\\') escaped = true;
      else if (c === quote) quote = null;
      continue;
    }
    if (c === '"' || c === "'") quote = c;
    else if (c === '{') depth++;
    else if (c === '}' && --depth === 0) {
      try {
        return JSON.parse(text.slice(start, i + 1));
      } catch {
        return null;
      }
    }
  }
  return null;
}

// A scan is "confident" when there's a clear score source and a clear end-of-run trigger.
export function confident(analysis) {
  const [s1, s2] = analysis.suggestions.score;
  const [t1] = analysis.suggestions.trigger;
  return !!(s1 && t1 && s1.score >= 5 && t1.score >= 4 && (!s2 || s1.score > s2.score));
}

// The scan itself. project: the Studio project object, if it's a Studio game; texts: the game's
// HTML and scripts as [{ file, text }] (for a plain JS game, and to spot Arcadia.submitScore).
export function analyzeSources({ project = null, texts = [] } = {}) {
  const usesSdk = texts.some((t) => /Arcadia\??\.submitScore|\/sdk\/arcadia\.js/.test(t.text));
  const engine = project ? 'wysicraft' : 'script';
  const { scoreC, statC, trigC } = project ? scanWysicraft(project) : scanScripts(texts);

  const score = ranked(scoreC, 6);
  const trigger = ranked(trigC, 6);
  const stats = ranked(statC, 8).filter((s) => !score[0] || describe(s.source) !== describe(score[0].source));

  return {
    engine,
    usesSdk,
    project: project ? { id: project.id, name: project.name, screens: Object.keys(project.screens || {}), scripts: Object.keys(project.scripts || {}) } : null,
    suggestions: { score, trigger, stats },
    recommended: {
      score: score[0]?.source || null,
      // The strongest end-of-run signal, plus any "won" flags, so winning also submits a score.
      trigger: trigger[0] ? {
        any: [trigger[0], ...trigger.slice(1).filter((t) => t.equals === '' && WIN_WORDS.test((t.source.path || t.source.variable || t.source.name).split('.').pop()))]
          .map((t) => ({ ...t.source, equals: t.equals })),
      } : null,
      board: { label: score[0]?.label || 'Score', format: 'points', aggregate: 'best', order: 'desc', max: null, minSeconds: 5 },
    },
  };
}
