/**
 * Every animation duration and easing curve of the scene lives here, so a speed setting (phase 4)
 * has one place to hook into. Durations are in seconds at normal speed; use scaled() to apply the
 * speed.
 */
export const durations = {
  /** A tile flies from the pot to a rack or to a player. */
  flight: 0.6,
  /** Delay between two tiles of the deal. */
  dealStagger: 0.05,
  /** A placed tile: the fall onto the table, then two shrinking bounces. */
  drop: { fall: 0.13, bounce: 0.15, settle: 0.09 },
  /** Heights of the two bounces, in world units. */
  bounceHeights: [0.075, 0.028],
  /** A floating score line rises and fades. */
  pointsRise: 1.5,
  /** Delay between two score lines. */
  pointsStagger: 0.22,
  /** The gold sparkles of a bridge or hexagon. */
  burst: 1.0,
  /** The rack slides in or out of view. */
  rackSlide: 0.42,
  /** A pause before the rack slides away, so the placed tile can be seen. */
  beforeHandover: 0.5,
  /** How long the settlement shows the tiles left in one rack. */
  settlementHold: 1.5,
} as const;

let speed = 1;

/** Sets the animation speed: 1 is normal, 2 twice as fast. */
export function setAnimationSpeed(multiplier: number): void {
  speed = Math.min(Math.max(multiplier, 0.05), 8);
}

/** Converts a duration at normal speed to the current speed. */
export function scaled(seconds: number): number {
  return seconds / speed;
}

export type Easing = (t: number) => number;

export const easing = {
  linear: (t: number) => t,
  inQuad: (t: number) => t * t,
  outCubic: (t: number) => 1 - (1 - t) ** 3,
  inOutCubic: (t: number) => (t < 0.5 ? 4 * t ** 3 : 1 - (-2 * t + 2) ** 3 / 2),
  /** Rises and falls back to zero: 0 at both ends, 1 in the middle. */
  parabola: (t: number) => 4 * t * (1 - t),
} satisfies Record<string, Easing>;
