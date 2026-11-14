import { describe, expect, it } from 'vitest';
import { GRAIN_WINDOW, tileVariation } from '../src/variation';

const allValues: [number, number, number][] = [];
for (let a = 0; a <= 5; a++) {
  for (let b = a; b <= 5; b++) {
    for (let c = b; c <= 5; c++) {
      allValues.push([a, b, c]);
    }
  }
}

describe('tileVariation', () => {
  it('does not depend on the order of the values', () => {
    expect(tileVariation([1, 3, 5])).toEqual(tileVariation([5, 1, 3]));
    expect(tileVariation([2, 2, 4])).toEqual(tileVariation([4, 2, 2]));
  });

  it('is the same on every call', () => {
    expect(tileVariation([0, 1, 2])).toEqual(tileVariation([0, 1, 2]));
  });

  it('differs between tiles', () => {
    const grains = new Set(allValues.map(v => tileVariation(v).grain.join()));
    expect(grains.size).toBe(allValues.length);
  });

  it('keeps the shift and turn inside the gap around a tile', () => {
    for (const values of allValues) {
      const { dx, dz, yaw } = tileVariation(values);
      expect(Math.abs(dx)).toBeLessThanOrEqual(0.006);
      expect(Math.abs(dz)).toBeLessThanOrEqual(0.006);
      expect(Math.abs(yaw)).toBeLessThanOrEqual(Math.PI / 180);
    }
  });

  it('keeps the grain window, turned any way, inside the texture', () => {
    const margin = (GRAIN_WINDOW * Math.SQRT2) / 2;
    for (const values of allValues) {
      const [u, v] = tileVariation(values).grain;
      for (const centre of [u, v]) {
        expect(centre).toBeGreaterThanOrEqual(margin);
        expect(centre).toBeLessThanOrEqual(1 - margin);
      }
    }
  });
});
