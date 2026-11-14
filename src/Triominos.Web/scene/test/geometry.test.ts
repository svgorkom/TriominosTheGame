import { describe, expect, it } from 'vitest';
import { cellAt, cellCenter, cellCorners, cellYaw, edgeNeighbours, HEIGHT, isUp, SIDE } from '../src/geometry';

const cells: { row: number; col: number }[] = [];
for (let row = -4; row <= 4; row++) {
  for (let col = -6; col <= 6; col++) {
    cells.push({ row, col });
  }
}

describe('isUp', () => {
  it('points up when row + col is even, also for negative coordinates', () => {
    expect(isUp(0, 0)).toBe(true);
    expect(isUp(0, 1)).toBe(false);
    expect(isUp(1, 0)).toBe(false);
    expect(isUp(-1, -1)).toBe(true);
    expect(isUp(-1, 0)).toBe(false);
  });
});

describe('cellAt', () => {
  it('is the inverse of cellCenter', () => {
    for (const { row, col } of cells) {
      const center = cellCenter(row, col);
      expect(cellAt(center.x, center.y)).toEqual({ row, col });
    }
  });

  it('finds the cell for points near the corners of a cell', () => {
    for (const { row, col } of cells) {
      const center = cellCenter(row, col);
      for (const corner of cellCorners(row, col)) {
        // Nudge a corner 10% toward the centre so it lies clearly inside the cell.
        const x = corner.x + (center.x - corner.x) * 0.1;
        const z = corner.y + (center.y - corner.y) * 0.1;
        expect(cellAt(x, z)).toEqual({ row, col });
      }
    }
  });
});

describe('cellCorners', () => {
  it('gives an equilateral triangle with the side length of a tile', () => {
    for (const { row, col } of cells) {
      const [a, b, c] = cellCorners(row, col);
      expect(a.distanceTo(b)).toBeCloseTo(SIDE, 9);
      expect(b.distanceTo(c)).toBeCloseTo(SIDE, 9);
      expect(c.distanceTo(a)).toBeCloseTo(SIDE, 9);
    }
  });

  it('has the apex of an up cell above (smaller z than) its base, and the reverse for a down cell', () => {
    const up = cellCorners(0, 0);
    expect(up[0].y).toBeCloseTo(up[1].y - HEIGHT, 9);
    const down = cellCorners(0, 1);
    expect(down[0].y).toBeCloseTo(down[1].y + HEIGHT, 9);
  });

  it('puts the corners of neighbouring cells on shared vertices', () => {
    // Rounded to micro-units; `+ 0` turns -0 into 0, which toFixed would otherwise print differently.
    const round = (n: number): number => Math.round(n * 1e6) / 1e6 + 0;
    const key = (v: { x: number; y: number }): string => `${round(v.x)},${round(v.y)}`;
    for (const { row, col } of cells) {
      const own = new Set(cellCorners(row, col).map(key));
      for (const n of edgeNeighbours(row, col)) {
        const shared = cellCorners(n.row, n.col).filter(v => own.has(key(v)));
        expect(shared).toHaveLength(2);
      }
    }
  });
});

describe('edgeNeighbours', () => {
  it('is symmetric: a neighbour of a cell has that cell as neighbour', () => {
    for (const { row, col } of cells) {
      for (const n of edgeNeighbours(row, col)) {
        expect(edgeNeighbours(n.row, n.col)).toContainEqual({ row, col });
      }
    }
  });

  it('returns three different cells of the opposite orientation', () => {
    for (const { row, col } of cells) {
      const neighbours = edgeNeighbours(row, col);
      expect(new Set(neighbours.map(n => `${n.row},${n.col}`)).size).toBe(3);
      for (const n of neighbours) {
        expect(isUp(n.row, n.col)).toBe(!isUp(row, col));
      }
    }
  });
});

describe('cellYaw', () => {
  it('turns down cells half a circle', () => {
    expect(cellYaw(0, 0)).toBe(0);
    expect(cellYaw(0, 1)).toBe(Math.PI);
  });
});
