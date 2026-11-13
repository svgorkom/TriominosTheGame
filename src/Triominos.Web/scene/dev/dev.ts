// Development harness: renders a random but valid board without Blazor. Not part of the build.
import './dev.css';
import { init, setBoard, setQuality, stats, type Quality, type TileView } from '../src/main';

type Cell = [number, number];

const isUp = (r: number, c: number) => (r + c) % 2 === 0;
const corners = (r: number, c: number): Cell[] =>
  isUp(r, c) ? [[r, c + 1], [r + 1, c + 2], [r + 1, c]] : [[r + 1, c + 1], [r, c], [r, c + 2]];
const neighbors = (r: number, c: number): Cell[] =>
  isUp(r, c) ? [[r, c + 1], [r + 1, c], [r, c - 1]] : [[r, c - 1], [r - 1, c], [r, c + 1]];

function randomBoard(count: number, seed: number): TileView[] {
  let s = seed;
  const random = () => ((s = (s * 1664525 + 1013904223) >>> 0) / 4294967296);
  const placed = new Set<string>(['0,0']);
  const frontier: Cell[] = [...neighbors(0, 0)];
  while (placed.size < count && frontier.length > 0) {
    const [r, c] = frontier.splice(Math.floor(random() * frontier.length), 1)[0];
    if (placed.has(`${r},${c}`)) continue;
    placed.add(`${r},${c}`);
    frontier.push(...neighbors(r, c));
  }

  const vertexValues = new Map<string, number>();
  const value = ([r, x]: Cell) => {
    const key = `${r},${x}`;
    if (!vertexValues.has(key)) vertexValues.set(key, Math.floor(random() * 6));
    return vertexValues.get(key)!;
  };

  return [...placed].map(key => {
    const [row, col] = key.split(',').map(Number);
    const [a, b, c] = corners(row, col).map(value);
    return { row, col, values: [a, b, c] };
  });
}

const params = new URLSearchParams(location.search);
const quality = (params.get('quality') ?? 'medium') as Quality;
const tiles = Number(params.get('tiles') ?? 24);

await init(document.getElementById('scene') as HTMLCanvasElement, quality);
setBoard(randomBoard(tiles, 7), true);
setQuality(quality);

const out = document.getElementById('stats')!;
setInterval(() => {
  const s = stats();
  out.textContent = `frames ${s.frames}  cpu ${s.averageMs.toFixed(2)} ms  draw calls ${s.drawCalls}`;
}, 500);
