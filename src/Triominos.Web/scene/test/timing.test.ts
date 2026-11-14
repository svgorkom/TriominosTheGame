import { afterEach, describe, expect, it } from 'vitest';
import { easing, scaled, setAnimationSpeed } from '../src/timing';

afterEach(() => setAnimationSpeed(1));

describe('scaled', () => {
  it('keeps a duration at normal speed', () => {
    expect(scaled(0.6)).toBeCloseTo(0.6, 12);
  });

  it('halves a duration at double speed', () => {
    setAnimationSpeed(2);
    expect(scaled(0.6)).toBeCloseTo(0.3, 12);
  });

  it('keeps the speed between 0.05 and 8', () => {
    setAnimationSpeed(0);
    expect(scaled(1)).toBeCloseTo(20, 9);
    setAnimationSpeed(1000);
    expect(scaled(1)).toBeCloseTo(0.125, 9);
    setAnimationSpeed(-3);
    expect(scaled(1)).toBeCloseTo(20, 9);
  });
});

describe('easing', () => {
  it.each(['linear', 'inQuad', 'outCubic', 'inOutCubic'] as const)('%s runs from 0 to 1', name => {
    expect(easing[name](0)).toBeCloseTo(0, 12);
    expect(easing[name](1)).toBeCloseTo(1, 12);
  });

  it.each(['linear', 'inQuad', 'outCubic', 'inOutCubic'] as const)('%s never goes backwards', name => {
    let previous = easing[name](0);
    for (let i = 1; i <= 100; i++) {
      const value = easing[name](i / 100);
      expect(value).toBeGreaterThanOrEqual(previous);
      previous = value;
    }
  });

  it('parabola is 0 at both ends and 1 in the middle', () => {
    expect(easing.parabola(0)).toBe(0);
    expect(easing.parabola(1)).toBe(0);
    expect(easing.parabola(0.5)).toBe(1);
  });
});
