// Reports the size of a published site per file type: raw, gzip (level 9) and Brotli (quality 11), next to
// the precompressed .br and .gz files that `dotnet publish` wrote itself. A second directory (a publish
// without trimming, say) is compared with the first by total.
// Usage: node measure.mjs <wwwroot> [other-wwwroot]
import { readdirSync, readFileSync, statSync, appendFileSync } from 'node:fs';
import { join, extname, relative } from 'node:path';
import { brotliCompressSync, gzipSync, constants } from 'node:zlib';

const root = process.argv[2];
const other = process.argv[3];

function walk(dir) {
  return readdirSync(dir, { withFileTypes: true }).flatMap(e => (e.isDirectory() ? walk(join(dir, e.name)) : [join(dir, e.name)]));
}

const kind = file => {
  const name = file.toLowerCase();
  if (name.endsWith('.wasm')) return name.includes('dotnet.native') ? 'wasm: .NET runtime' : 'wasm: assemblies';
  if (name.endsWith('.js') && name.includes('/js/scene/')) return 'scene.js';
  if (name.endsWith('.js')) return 'js: other';
  if (name.endsWith('.woff2')) return 'fonts';
  if (name.endsWith('.dat')) return 'icu data';
  return extname(name).slice(1) || 'other';
};

const kb = n => (n / 1024).toFixed(1).padStart(9);

function measure(dir) {
  const files = walk(dir).filter(f => !/\.(br|gz)$/.test(f));
  const rows = new Map();
  const big = [];
  for (const file of files) {
    const data = readFileSync(file);
    const sizes = {
      raw: data.length,
      gzip: gzipSync(data, { level: 9 }).length,
      brotli: brotliCompressSync(data, { params: { [constants.BROTLI_PARAM_QUALITY]: 11 } }).length,
      pubBr: safeSize(file + '.br'),
      pubGz: safeSize(file + '.gz'),
    };
    const k = kind(file);
    const row = rows.get(k) ?? { files: 0, raw: 0, gzip: 0, brotli: 0, pubBr: 0, pubGz: 0 };
    row.files++;
    for (const key of Object.keys(sizes)) row[key] += sizes[key];
    rows.set(k, row);
    big.push({ file: relative(dir, file), ...sizes });
  }

  return { rows, big };
}

function safeSize(file) {
  try {
    return statSync(file).size;
  } catch {
    return 0;
  }
}

function table({ rows, big }) {
  const total = { files: 0, raw: 0, gzip: 0, brotli: 0, pubBr: 0, pubGz: 0 };
  const lines = ['| type | files | raw KiB | gzip KiB | brotli KiB | published .gz KiB | published .br KiB |', '|---|---:|---:|---:|---:|---:|---:|'];
  for (const [k, r] of [...rows].sort((a, b) => b[1].raw - a[1].raw)) {
    lines.push(`| ${k} | ${r.files} | ${kb(r.raw)} | ${kb(r.gzip)} | ${kb(r.brotli)} | ${kb(r.pubGz)} | ${kb(r.pubBr)} |`);
    for (const key of Object.keys(total)) total[key] += r[key];
  }

  lines.push(`| **total** | ${total.files} | ${kb(total.raw)} | ${kb(total.gzip)} | ${kb(total.brotli)} | ${kb(total.pubGz)} | ${kb(total.pubBr)} |`);
  lines.push('', 'Largest files:', '', '| file | raw KiB | gzip KiB | brotli KiB | published .br KiB |', '|---|---:|---:|---:|---:|');
  for (const f of big.sort((a, b) => b.raw - a.raw).slice(0, 12)) {
    lines.push(`| ${f.file} | ${kb(f.raw)} | ${kb(f.gzip)} | ${kb(f.brotli)} | ${kb(f.pubBr)} |`);
  }

  return { text: lines.join('\n'), total };
}

const main = table(measure(root));
let output = `### Published size: ${root}\n\n${main.text}\n`;
if (other) {
  const second = table(measure(other));
  const pct = (a, b) => `${(((b - a) / a) * 100).toFixed(0)}%`;
  output += `\n### Without trimming: ${other}\n\n${second.text}\n\nTrimming saves raw ${pct(second.total.raw, main.total.raw)}, gzip ${pct(second.total.gzip, main.total.gzip)}, brotli ${pct(second.total.brotli, main.total.brotli)} of the total.\n`;
}

console.log(output);
if (process.env.GITHUB_STEP_SUMMARY) appendFileSync(process.env.GITHUB_STEP_SUMMARY, output);
