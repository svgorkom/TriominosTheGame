import { seededRandom } from './textures';

/** How far a tile on the table lies from the middle of its cell, in world units. */
const MAX_SHIFT = 0.006;

/** How far a tile on the table is turned out of line, in radians (one degree). */
const MAX_TURN = Math.PI / 180;

/** The part of the ivory texture one tile shows; the rest is room to move and turn the window. */
export const GRAIN_WINDOW = 0.45;

/** What makes one tile differ from the next: how it lies on the table and which part of the ivory it shows. */
export interface TileVariation {
  /** Offset from the middle of the cell, in world units. */
  dx: number;
  dz: number;
  /** Turn around the vertical axis, in radians. */
  yaw: number;
  /** Centre (u, v) and angle of the tile's window on the ivory texture. */
  grain: readonly [number, number, number];
}

const cache = new Map<number, TileVariation>();

/**
 * The variation of a tile. It depends only on the three values, whatever corner they are at, so a
 * tile looks and lies the same in the rack, in flight and on the table, in every game.
 *
 * The shift and the turn together stay inside the gap that TILE_SCALE leaves around a tile
 * (0.0208 on each side): a turn of one degree swings the end of an edge out by 0.0133, the shift
 * adds 0.006 at most, so two neighbours never overlap.
 */
export function tileVariation(values: readonly number[]): TileVariation {
  const [a, b, c] = [...values].sort((x, y) => x - y);
  const key = a * 36 + b * 6 + c;
  let variation = cache.get(key);
  if (!variation) {
    const random = seededRandom(key * 7919 + 13);
    const spread = (): number => random() * 2 - 1;
    // A window of GRAIN_WINDOW, turned any way, stays inside the texture when its centre keeps
    // half its diagonal away from the edge.
    const margin = (GRAIN_WINDOW * Math.SQRT2) / 2;
    const place = (): number => margin + random() * (1 - 2 * margin);
    variation = {
      dx: spread() * MAX_SHIFT,
      dz: spread() * MAX_SHIFT,
      yaw: spread() * MAX_TURN,
      grain: [place(), place(), random() * Math.PI * 2],
    };
    cache.set(key, variation);
  }

  return variation;
}
