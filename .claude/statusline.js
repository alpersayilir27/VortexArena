#!/usr/bin/env node
'use strict';

// Claude Code statusline.
// Reads the status JSON from stdin and prints a single line:
//   <model display_name> | <pct>% ctx | <used>/<total> | <project folder>
// e.g.  Opus 5 (1M context) | 5% ctx | 47.9k/1M | vortexarena
// Never throws: on any error it still prints a best-effort line.

const fs = require('fs');

function readStdin() {
  try {
    return fs.readFileSync(0, 'utf8'); // fd 0 = stdin (Claude Code pipes JSON in)
  } catch (e) {
    return '';
  }
}

// 1000000 -> "1M", 47900 -> "47.9k", 512 -> "512"
function fmtTokens(n) {
  n = Math.max(0, Math.round(n));
  if (n >= 1e6) return trimDecimal(n / 1e6) + 'M';
  if (n >= 1e3) return trimDecimal(n / 1e3) + 'k';
  return String(n);
}
function trimDecimal(v) {
  let s = v.toFixed(1);
  if (s.endsWith('.0')) s = s.slice(0, -2); // 1.0 -> 1, 47.9 stays
  return s;
}

// Derive the total context window from the model name, e.g.
// "Opus 4.8 (1M context)" -> 1000000, "... (200k context)" -> 200000.
// Falls back to 200000 when the model name carries no hint.
function parseContextTotal(displayName) {
  if (displayName) {
    const m = displayName.match(/(\d+(?:\.\d+)?)\s*([kKmM])\s*context/);
    if (m) {
      const num = parseFloat(m[1]);
      const unit = m[2].toLowerCase();
      return Math.round(num * (unit === 'm' ? 1e6 : 1e3));
    }
  }
  return 200000;
}

// Sum the last transcript message's usage: this reflects how full the
// context window currently is (cached + fresh input + output).
function computeUsedTokens(transcriptPath) {
  if (!transcriptPath) return 0;
  let content;
  try {
    content = fs.readFileSync(transcriptPath, 'utf8');
  } catch (e) {
    return 0;
  }
  const lines = content.split(/\r?\n/);
  for (let i = lines.length - 1; i >= 0; i--) {
    const line = lines[i].trim();
    if (!line) continue;
    let obj;
    try {
      obj = JSON.parse(line);
    } catch (e) {
      continue;
    }
    const usage = obj && obj.message && obj.message.usage;
    if (usage) {
      const used =
        (usage.input_tokens || 0) +
        (usage.cache_creation_input_tokens || 0) +
        (usage.cache_read_input_tokens || 0) +
        (usage.output_tokens || 0);
      if (used > 0) return used;
    }
  }
  return 0;
}

// Last path segment, handling both / and \ separators.
function basename(p) {
  if (!p) return '';
  return p.replace(/[\\/]+$/, '').split(/[\\/]/).pop() || '';
}

function main() {
  let data = {};
  try {
    data = JSON.parse(readStdin() || '{}');
  } catch (e) {
    data = {};
  }

  const displayName = (data.model && data.model.display_name) || 'Claude';
  const totalCtx = parseContextTotal(displayName);
  const used = computeUsedTokens(data.transcript_path);
  const pct = totalCtx > 0 ? Math.round((used / totalCtx) * 100) : 0;

  const ws = data.workspace || {};
  const dir = ws.project_dir || data.cwd || ws.current_dir || process.cwd();
  const folder = basename(dir);

  process.stdout.write(
    `${displayName} | ${pct}% ctx | ${fmtTokens(used)}/${fmtTokens(totalCtx)} | ${folder}`
  );
}

main();
