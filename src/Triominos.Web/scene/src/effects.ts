import * as THREE from 'three';
import { scaled, durations } from './timing';
import type { ScoreLine } from './types';

/** A point in canvas pixels (from the top left of the canvas). */
export interface ScreenPoint {
  x: number;
  y: number;
}

export type NoteTone = 'penalty' | 'gold' | 'plain';

/**
 * Floating score lines. They are plain HTML on top of the canvas, animated with the Web
 * Animations API, so they run on the compositor and never ask the 3D scene for a frame.
 */
export class Floaters {
  private readonly active = new Set<Animation>();

  /** @param root an empty element on top of the canvas, owned by this class from now on. */
  constructor(private readonly root: HTMLElement) {}

  /** Breaks a score down line by line above a point and ends with the total. Resolves when it has faded. */
  score(at: ScreenPoint, lines: readonly ScoreLine[], total: number): Promise<void> {
    const group = this.group(at);
    const animations: Animation[] = [];
    const stagger = durations.pointsStagger;
    const rise = durations.pointsRise;

    lines.forEach((line, index) => {
      const element = document.createElement('div');
      element.className = `float-line${line.gold ? ' gold' : ''}`;
      element.append(this.span('b', `+${line.points}`), this.span('i', line.label));
      group.append(element);
      animations.push(this.pop(element, index * stagger, rise));
    });

    const totalElement = document.createElement('div');
    totalElement.className = `float-total${lines.some(l => l.gold) ? ' gold' : ''}`;
    totalElement.append(this.span('b', `+${total}`));
    group.append(totalElement);
    animations.push(this.pop(totalElement, lines.length * stagger + 0.1, rise));

    this.keepInView(group, at);
    animations.push(this.lift(group, lines.length * stagger + rise + 0.1, 70));
    return this.settle(animations, group);
  }

  /** A short note, such as a penalty or a settlement line. */
  note(at: ScreenPoint, text: string, tone: NoteTone, rise: number = durations.pointsRise): Promise<void> {
    const group = this.group(at);
    const element = document.createElement('div');
    element.className = `float-line ${tone}`;
    element.append(this.span('b', text));
    group.append(element);
    this.keepInView(group, at);
    return this.settle([this.pop(element, 0, rise), this.lift(group, rise, 46)], group);
  }

  /** Jumps every running animation to its end, which removes the text. */
  finishAll(): void {
    [...this.active].forEach(animation => animation.finish());
  }

  dispose(): void {
    [...this.active].forEach(animation => animation.cancel());
    this.root.replaceChildren();
  }

  private group(at: ScreenPoint): HTMLElement {
    const group = document.createElement('div');
    group.className = 'float-group';
    group.style.left = `${at.x}px`;
    group.style.top = `${at.y}px`;
    this.root.append(group);
    return group;
  }

  /** Keeps a tall stack from running off the top of the canvas, where the page's own panels sit. */
  private keepInView(group: HTMLElement, at: ScreenPoint): void {
    const top = at.y - group.offsetHeight - 80;
    if (top < 70) {
      group.style.top = `${at.y + (70 - top)}px`;
    }
  }

  private span(tag: 'b' | 'i', text: string): HTMLElement {
    const element = document.createElement(tag);
    element.textContent = text;
    return element;
  }

  /** Fades in with a small pop, stays, fades out. */
  private pop(element: HTMLElement, delay: number, total: number): Animation {
    return this.run(
      element,
      [
        { opacity: 0, transform: 'translateY(12px) scale(0.8)', offset: 0 },
        { opacity: 1, transform: 'translateY(0) scale(1.06)', offset: 0.12 },
        { opacity: 1, transform: 'translateY(0) scale(1)', offset: 0.2 },
        { opacity: 1, transform: 'translateY(0) scale(1)', offset: 0.78 },
        { opacity: 0, transform: 'translateY(-6px) scale(1)', offset: 1 },
      ],
      delay,
      total,
    );
  }

  /** Moves the whole group up while its lines come and go. */
  private lift(group: HTMLElement, total: number, distance: number): Animation {
    return this.run(group, [{ transform: 'translate(-50%, -100%)' }, { transform: `translate(-50%, calc(-100% - ${distance}px))` }], 0, total, 'ease-out');
  }

  private run(element: HTMLElement, keyframes: Keyframe[], delay: number, total: number, easing = 'ease'): Animation {
    const animation = element.animate(keyframes, {
      duration: scaled(total) * 1000,
      delay: scaled(delay) * 1000,
      easing,
      fill: 'both',
    });
    this.active.add(animation);
    return animation;
  }

  private settle(animations: Animation[], group: HTMLElement): Promise<void> {
    return Promise.all(animations.map(a => a.finished.catch(() => undefined))).then(() => {
      animations.forEach(a => this.active.delete(a));
      group.remove();
    });
  }
}

const GOLD = new THREE.Color(0xffd36b);
const CAPACITY = 64;
const GRAVITY = 5.5;

/** A burst of gold sparkles above a tile, for a bridge or a hexagon: one draw call. */
export class GoldBurst {
  readonly points: THREE.Points;
  private readonly origins = new Float32Array(CAPACITY * 3);
  private readonly velocities = new Float32Array(CAPACITY * 3);
  private readonly phases = new Float32Array(CAPACITY);
  private readonly positions = new Float32Array(CAPACITY * 3);
  private readonly colors = new Float32Array(CAPACITY * 3);
  private readonly geometry = new THREE.BufferGeometry();
  private count = 0;

  constructor() {
    this.geometry.setAttribute('position', new THREE.BufferAttribute(this.positions, 3).setUsage(THREE.DynamicDrawUsage));
    this.geometry.setAttribute('color', new THREE.BufferAttribute(this.colors, 3).setUsage(THREE.DynamicDrawUsage));
    this.points = new THREE.Points(
      this.geometry,
      new THREE.PointsMaterial({
        size: 0.5,
        map: sparkleTexture(),
        vertexColors: true,
        transparent: true,
        blending: THREE.AdditiveBlending,
        depthWrite: false,
        toneMapped: false,
      }),
    );
    // The bounds change every frame and the burst is small, so culling would only get in the way.
    this.points.frustumCulled = false;
    this.points.renderOrder = 3;
    this.points.visible = false;
  }

  /** Starts a burst around a point on the table. */
  emit(x: number, z: number, count: number, reach: number): void {
    this.count = Math.min(count, CAPACITY);
    for (let i = 0; i < this.count; i++) {
      const angle = Math.random() * Math.PI * 2;
      const speed = (0.6 + Math.random() * 1.6) * reach;
      this.origins.set([x + Math.cos(angle) * 0.25, 0.25, z + Math.sin(angle) * 0.25], i * 3);
      this.velocities.set([Math.cos(angle) * speed, 1.3 + Math.random() * 1.9, Math.sin(angle) * speed], i * 3);
      this.phases[i] = Math.random() * Math.PI * 2;
    }

    this.geometry.setDrawRange(0, this.count);
    this.points.visible = true;
    this.setProgress(0);
  }

  /** Moves the sparkles to their place at time t (0 to 1) of their life. */
  setProgress(t: number): void {
    const seconds = t * durations.burst;
    for (let i = 0; i < this.count; i++) {
      const o = i * 3;
      this.positions[o] = this.origins[o] + this.velocities[o] * seconds;
      this.positions[o + 1] = Math.max(this.origins[o + 1] + this.velocities[o + 1] * seconds - 0.5 * GRAVITY * seconds * seconds, 0.05);
      this.positions[o + 2] = this.origins[o + 2] + this.velocities[o + 2] * seconds;
      const twinkle = 0.65 + 0.35 * Math.sin(seconds * 22 + this.phases[i]);
      const fade = (1 - t) ** 1.6 * twinkle;
      this.colors[o] = GOLD.r * fade;
      this.colors[o + 1] = GOLD.g * fade;
      this.colors[o + 2] = GOLD.b * fade;
    }

    this.geometry.getAttribute('position').needsUpdate = true;
    this.geometry.getAttribute('color').needsUpdate = true;
    if (t >= 1) {
      this.clear();
    }
  }

  clear(): void {
    this.count = 0;
    this.points.visible = false;
  }
}

/** A soft round glow with a four-pointed star. */
function sparkleTexture(): THREE.Texture {
  const size = 64;
  const canvas = document.createElement('canvas');
  canvas.width = size;
  canvas.height = size;
  const ctx = canvas.getContext('2d')!;
  const gradient = ctx.createRadialGradient(size / 2, size / 2, 0, size / 2, size / 2, size / 2);
  gradient.addColorStop(0, 'rgba(255,255,255,1)');
  gradient.addColorStop(0.18, 'rgba(255,240,200,0.85)');
  gradient.addColorStop(0.5, 'rgba(255,205,110,0.22)');
  gradient.addColorStop(1, 'rgba(255,190,80,0)');
  ctx.fillStyle = gradient;
  ctx.fillRect(0, 0, size, size);
  ctx.strokeStyle = 'rgba(255,248,225,0.9)';
  ctx.lineWidth = 2;
  ctx.beginPath();
  ctx.moveTo(size / 2, 3);
  ctx.lineTo(size / 2, size - 3);
  ctx.moveTo(3, size / 2);
  ctx.lineTo(size - 3, size / 2);
  ctx.stroke();

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  return texture;
}
