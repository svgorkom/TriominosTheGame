// Development harness: the scene with a small stand-in for the C# side, so dragging can be tried
// without Blazor. The stand-in checks corners like Core does (every corner must equal the value
// already at its vertex). Not part of the build.
import './dev.css';
import { deal, init, playMove, setAnimationSpeed, setBoard, setQuality, setRack, setTheme, stats, type DotNetHost, type PlacementView, type Quality, type RackTileView, type ScoreLine, type TileView } from '../src/main';

type Cell = [number, number];
type Values = [number, number, number];

const isUp = (r: number, c: number) => (r + c) % 2 === 0;
const corners = (r: number, c: number): Cell[] =>
  isUp(r, c) ? [[r, c + 1], [r + 1, c + 2], [r + 1, c]] : [[r + 1, c + 1], [r, c], [r, c + 2]];
const neighbors = (r: number, c: number): Cell[] =>
  isUp(r, c) ? [[r, c + 1], [r + 1, c], [r, c - 1]] : [[r, c - 1], [r - 1, c], [r, c + 1]];

let seed = 7;
const random = () => ((seed = (seed * 1664525 + 1013904223) >>> 0) / 4294967296);

const vertexValues = new Map<string, number>();
const board = new Map<string, TileView>();
let rack: RackTileView[] = [];
let nextId = 1;

const vertexValue = ([r, x]: Cell) => {
  const key = `${r},${x}`;
  if (!vertexValues.has(key)) vertexValues.set(key, Math.floor(random() * 6));
  return vertexValues.get(key)!;
};

function buildBoard(count: number): void {
  const placed = new Set<string>(['0,0']);
  const frontier: Cell[] = [...neighbors(0, 0)];
  while (placed.size < count && frontier.length > 0) {
    const [r, c] = frontier.splice(Math.floor(random() * frontier.length), 1)[0];
    if (placed.has(`${r},${c}`)) continue;
    placed.add(`${r},${c}`);
    frontier.push(...neighbors(r, c));
  }

  for (const key of placed) {
    const [row, col] = key.split(',').map(Number);
    const values = corners(row, col).map(vertexValue) as Values;
    board.set(key, { row, col, values });
  }
}

const openCells = (): Cell[] => {
  const open = new Map<string, Cell>();
  for (const t of board.values()) {
    for (const [r, c] of neighbors(t.row, t.col)) {
      if (!board.has(`${r},${c}`)) open.set(`${r},${c}`, [r, c]);
    }
  }

  return [...open.values()];
};

/** Tiles that fit somewhere (read off the vertices of an open cell, then turned), plus a few random ones. */
function dealRack(fitting: number, other: number): RackTileView[] {
  const cells = openCells();
  const tiles: RackTileView[] = [];
  for (let i = 0; i < fitting; i++) {
    const [r, c] = cells.splice(Math.floor(random() * cells.length), 1)[0];
    const values = corners(r, c).map(vertexValue) as Values;
    const turn = Math.floor(random() * 3);
    tiles.push({ id: nextId++, values: [0, 1, 2].map(k => values[(k + turn) % 3]) as Values, marked: false });
  }

  for (let i = 0; i < other; i++) {
    tiles.push({ id: nextId++, values: [0, 0, 0].map(() => Math.floor(random() * 6)) as Values, marked: false });
  }

  return tiles.sort(() => random() - 0.5);
}

function fits(values: Values, rotation: number, r: number, c: number): boolean {
  return corners(r, c).every((v, k) => {
    const existing = vertexValues.get(`${v[0]},${v[1]}`);
    return existing === undefined || existing === values[(k + rotation) % 3];
  });
}

function placementsOf(id: number): PlacementView[] {
  const tile = rack.find(t => t.id === id);
  if (!tile) return [];
  const result: PlacementView[] = [];
  for (const [row, col] of openCells()) {
    for (let rotation = 0; rotation < 3; rotation++) {
      if (fits(tile.values, rotation, row, col)) result.push({ row, col, rotation });
    }
  }

  return result;
}

const params = new URLSearchParams(location.search);
const bonus = params.get('bonus') ?? '';
const handover = params.get('handover') !== '0';
let readyCount = 0;
/** True after a move that passes the turn on, until "Toon mijn stenen" is pressed. */
let waitingForHandover = false;

/** Stand-in for the scoring of Core: the tile value, plus a forced bridge or hexagon bonus (?bonus=bridge|hexagon|both). */
function scoreOf(values: Values): { lines: ScoreLine[]; total: number } {
  const lines: ScoreLine[] = [{ label: 'Steen', points: values[0] + values[1] + values[2], gold: false }];
  if (bonus === 'bridge' || bonus === 'both') lines.push({ label: 'Brug', points: 40, gold: true });
  if (bonus === 'hexagon' || bonus === 'both') lines.push({ label: 'Hexagonbonus', points: 50, gold: true });
  return { lines, total: lines.reduce((sum, l) => sum + l.points, 0) };
}

/** Stand-in for Core's hexagon cells: the six cells around the first corner of the tile (?bonus=hexagon|both). */
function hexagonOf(row: number, col: number): { row: number; col: number }[] {
  if (bonus !== 'hexagon' && bonus !== 'both') return [];
  const [vr, vx] = corners(row, col)[0];
  return [[vr - 1, vx - 2], [vr - 1, vx - 1], [vr - 1, vx], [vr, vx - 2], [vr, vx - 1], [vr, vx]].map(([r, c]) => ({ row: r, col: c }));
}

const boardTiles = (): TileView[] => [...board.values()];
const sumOf = (tiles: RackTileView[]): number => tiles.reduce((s, t) => s + t.values[0] + t.values[1] + t.values[2], 0);
const showButton = document.querySelector<HTMLButtonElement>('[data-dev="show"]')!;

(window as unknown as Record<string, unknown>).__dev = { placementsOf, board: () => board.size, rack: () => rack.length, ready: () => readyCount };

const message = document.querySelector('.message')!;
const host: DotNetHost = {
  async invokeMethodAsync(method: string, ...args: unknown[]) {
    if (method === 'TileSelected') {
      const fits = placementsOf(args[0] as number);
      const open = openCells().map(([row, col]) => ({ row, col }));
      return { open, fits } as never;
    }

    if (method === 'TileDropped') {
      const [id, row, col, rotation] = args as number[];
      const tile = rack.find(t => t.id === id)!;
      const values = [0, 1, 2].map(k => tile.values[(k + rotation) % 3]) as Values;
      corners(row, col).forEach((v, k) => vertexValues.set(`${v[0]},${v[1]}`, values[k]));
      board.set(`${row},${col}`, { row, col, values });
      rack = rack.filter(t => t.id !== id);
      message.textContent = `Gelegd: ${tile.values.join('-')} met draaiing ${rotation}.`;
      const score = scoreOf(values);
      // Like Home.razor.cs: the move goes to the scene in one call and the rack is hidden when the turn passes on.
      waitingForHandover = handover;
      playMove({
        events: [{ kind: 'placed', player: 0, hidden: false, tileId: id, row, col, values, ...score, hexagon: hexagonOf(row, col) }],
        board: boardTiles(),
        rack: handover ? [] : rack,
      });
    } else if (method === 'DrawRequested') {
      const drawn = dealRack(0, 1);
      rack = [...rack, ...drawn];
      message.textContent = 'Getrokken (spatie).';
      playMove({ events: [{ kind: 'drawn', player: 0, hidden: false, tileId: drawn[0].id, penalty: 5 }], board: boardTiles(), rack });
    } else if (method === 'AnimationsFinished') {
      readyCount++;
      showButton.hidden = !waitingForHandover;
    }

    return undefined as never;
  },
};

const quality = (params.get('quality') ?? 'medium') as Quality;
buildBoard(Number(params.get('tiles') ?? 24));
rack = dealRack(Number(params.get('fit') ?? 5), Number(params.get('other') ?? 3));
if (params.has('marked')) rack.slice(0, 2).forEach(t => (t.marked = true));

// ?theme=classic|modern|playful|midnight|porcelain; the page takes the matching colours from the CSS.
const theme = params.get('theme') ?? 'classic';
document.documentElement.dataset.theme = theme;
await init(document.getElementById('scene') as HTMLCanvasElement, quality, host, theme);
(window as unknown as Record<string, unknown>).__setTheme = (name: string) => {
  document.documentElement.dataset.theme = name;
  setTheme(name);
};
setBoard([...board.values()], true);
setRack(rack);
setQuality(quality);

const tools: Record<string, () => void> = {
  deal: () => deal({ counts: [9, 9] }),
  pass: () => {
    waitingForHandover = handover;
    playMove({ events: [{ kind: 'passed', player: 0, hidden: false, penalty: 10 }], board: boardTiles(), rack: handover ? [] : rack });
  },
  finish: () => {
    const rivals = dealRack(0, 4);
    playMove({
      events: [
        {
          kind: 'finished',
          winner: 0,
          goingOut: { label: 'Uitgaan', points: 25 + sumOf(rivals), gold: true },
          rivals: [{ name: 'Speler 2', tiles: rivals, line: { label: 'Stenen van Speler 2', points: sumOf(rivals), gold: true } }],
        },
      ],
      board: boardTiles(),
      rack: [],
    });
  },
  show: () => {
    showButton.hidden = true;
    waitingForHandover = false;
    setRack(rack);
  },
};
document.querySelectorAll<HTMLElement>('[data-dev]').forEach(button => button.addEventListener('click', () => tools[button.dataset.dev!]()));
if (params.has('speed')) setAnimationSpeed(Number(params.get('speed')));

const out = document.getElementById('stats')!;
setInterval(() => {
  const s = stats();
  out.textContent = `frames ${s.frames}  cpu ${s.averageMs.toFixed(2)} ms  draw calls ${s.drawCalls}`;
}, 500);
