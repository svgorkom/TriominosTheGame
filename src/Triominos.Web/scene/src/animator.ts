import { easing, scaled, type Easing } from './timing';

type Update = (t: number) => void;

interface Active {
  start: number;
  delay: number;
  duration: number;
  update: Update;
  ease: Easing;
  resolve: () => void;
}

export interface AnimateOptions {
  /** Delay in seconds at normal speed. */
  delay?: number;
  ease?: Easing;
}

/**
 * Runs time-based tweens. The scene steps it once per frame and keeps drawing while it reports
 * that something is still moving, so an idle scene draws nothing. Every tween is a promise, so a
 * sequence is plain async code.
 */
export class Animator {
  private active: Active[] = [];

  /** @param wake asks the scene for a frame, so a new tween starts moving. */
  constructor(private readonly wake: () => void) {}

  get busy(): boolean {
    return this.active.length > 0;
  }

  /** Calls update(0..1) every frame for the given duration (seconds at normal speed). */
  animate(seconds: number, update: Update, options: AnimateOptions = {}): Promise<void> {
    return new Promise(resolve => {
      this.active.push({
        start: performance.now(),
        delay: scaled(options.delay ?? 0) * 1000,
        duration: Math.max(scaled(seconds) * 1000, 1),
        update,
        ease: options.ease ?? easing.linear,
        resolve,
      });
      this.wake();
    });
  }

  /** Advances every tween to the given time; returns whether any is left. */
  step(now: number): boolean {
    const remaining: Active[] = [];
    for (const tween of this.active) {
      const elapsed = now - tween.start - tween.delay;
      if (elapsed < 0) {
        remaining.push(tween);
        continue;
      }

      const t = Math.min(elapsed / tween.duration, 1);
      tween.update(tween.ease(t));
      if (t < 1) {
        remaining.push(tween);
      } else {
        tween.resolve();
      }
    }

    this.active = remaining;
    return remaining.length > 0;
  }

  /** Jumps every tween to its end state. */
  finishAll(): void {
    const all = this.active;
    this.active = [];
    for (const tween of all) {
      tween.update(1);
      tween.resolve();
    }
  }
}

/**
 * One sequence of animations (a move, a deal). Once finish() is called every remaining step of
 * the sequence jumps to its end state at once, so the scene is never left half-way.
 */
export class Run {
  private skipped = false;

  constructor(private readonly animator: Animator) {}

  get isSkipped(): boolean {
    return this.skipped;
  }

  animate(seconds: number, update: Update, options: AnimateOptions = {}): Promise<void> {
    if (this.skipped) {
      update(1);
      return Promise.resolve();
    }

    return this.animator.animate(seconds, update, options);
  }

  /** Waits without changing anything. */
  pause(seconds: number): Promise<void> {
    return this.animate(seconds, () => undefined);
  }

  finish(): void {
    this.skipped = true;
    this.animator.finishAll();
  }
}
