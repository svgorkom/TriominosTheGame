import * as THREE from 'three';
import { rgba, type DigitTheme, type FeltTheme, type SceneTheme, type TileTheme, type WoodTheme } from './theme';

/**
 * Procedural textures, drawn once on canvases at startup. They keep the download small and
 * avoid third-party assets. Every generator is seeded, so the table looks the same each time.
 */

/** Small, fast seeded random generator (mulberry32). */
export function seededRandom(seed: number): () => number {
  let state = seed >>> 0;
  return () => {
    state = (state + 0x6d2b79f5) >>> 0;
    let t = state;
    t = Math.imul(t ^ (t >>> 15), t | 1);
    t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
    return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
  };
}

function canvas(width: number, height: number): [HTMLCanvasElement, CanvasRenderingContext2D] {
  const c = document.createElement('canvas');
  c.width = width;
  c.height = height;
  return [c, c.getContext('2d')!];
}

function toTexture(c: HTMLCanvasElement, color: boolean, repeat = 1): THREE.CanvasTexture {
  const texture = new THREE.CanvasTexture(c);
  texture.colorSpace = color ? THREE.SRGBColorSpace : THREE.NoColorSpace;
  texture.wrapS = texture.wrapT = THREE.RepeatWrapping;
  texture.repeat.set(repeat, repeat);
  texture.anisotropy = 8;
  return texture;
}

/**
 * Felt: fine noise with short fibres lying every way over it, as a colour map and a matching
 * bump map. The fibres wrap around the edges, so the texture repeats without a seam.
 */
export function feltTextures(theme: FeltTheme): { map: THREE.Texture; bump: THREE.Texture } {
  const size = 512;
  const random = seededRandom(11);
  const [colorCanvas, color] = canvas(size, size);
  const [bumpCanvas, bump] = canvas(size, size);
  const colorData = color.createImageData(size, size);
  const bumpData = bump.createImageData(size, size);
  for (let i = 0; i < size * size; i++) {
    const n = random();
    const shade = 0.9 + n * 0.2;
    colorData.data[i * 4] = theme.base[0] * shade;
    colorData.data[i * 4 + 1] = theme.base[1] * shade;
    colorData.data[i * 4 + 2] = theme.base[2] * shade;
    colorData.data[i * 4 + 3] = 255;
    const b = 110 + n * 90;
    bumpData.data[i * 4] = bumpData.data[i * 4 + 1] = bumpData.data[i * 4 + 2] = b;
    bumpData.data[i * 4 + 3] = 255;
  }

  color.putImageData(colorData, 0, 0);
  bump.putImageData(bumpData, 0, 0);

  color.lineCap = bump.lineCap = 'round';
  for (let fibre = 0; fibre < 2600; fibre++) {
    const x = random() * size;
    const y = random() * size;
    const angle = random() * Math.PI * 2;
    const length = 4 + random() * 9;
    const dx = Math.cos(angle) * length;
    const dy = Math.sin(angle) * length;
    const bend = (random() - 0.5) * length * 0.8;
    const light = random() < 0.55;
    color.strokeStyle = light ? rgba(theme.lightFibre, 0.1 + random() * 0.16) : rgba(theme.darkFibre, 0.1 + random() * 0.18);
    bump.strokeStyle = light ? `rgba(255, 255, 255, ${0.2 + random() * 0.3})` : `rgba(0, 0, 0, ${0.15 + random() * 0.25})`;
    color.lineWidth = bump.lineWidth = 0.7 + random() * 0.9;
    // A fibre that crosses an edge is drawn again on the other side.
    for (const ox of [-size, 0, size]) {
      for (const oy of [-size, 0, size]) {
        if (x + ox < -16 || x + ox > size + 16 || y + oy < -16 || y + oy > size + 16) {
          continue;
        }

        for (const ctx of [color, bump]) {
          ctx.beginPath();
          ctx.moveTo(x + ox, y + oy);
          ctx.quadraticCurveTo(x + ox + dx / 2 - (dy / length) * bend, y + oy + dy / 2 + (dx / length) * bend, x + ox + dx, y + oy + dy);
          ctx.stroke();
        }
      }
    }
  }

  return { map: toTexture(colorCanvas, true), bump: toTexture(bumpCanvas, false) };
}

/** Lacquered wood: long grain lines with soft growth rings. */
export function woodTexture(theme: WoodTheme): THREE.Texture {
  const width = 1024;
  const height = 512;
  const random = seededRandom(23);
  const [c, ctx] = canvas(width, height);

  const base = ctx.createLinearGradient(0, 0, 0, height);
  base.addColorStop(0, theme.base[0]);
  base.addColorStop(0.5, theme.base[1]);
  base.addColorStop(1, theme.base[2]);
  ctx.fillStyle = base;
  ctx.fillRect(0, 0, width, height);

  for (let line = 0; line < 140; line++) {
    const y0 = random() * height;
    const amplitude = 2 + random() * 10;
    const frequency = 0.002 + random() * 0.006;
    const phase = random() * Math.PI * 2;
    const dark = random() < 0.6;
    ctx.strokeStyle = dark ? rgba(theme.darkGrain, 0.15 + random() * 0.35) : rgba(theme.lightGrain, 0.08 + random() * 0.2);
    ctx.lineWidth = 0.6 + random() * 2.4;
    ctx.beginPath();
    for (let x = 0; x <= width; x += 8) {
      const y = y0 + Math.sin(x * frequency + phase) * amplitude + Math.sin(x * 0.03 + phase) * 0.8;
      if (x === 0) {
        ctx.moveTo(x, y);
      } else {
        ctx.lineTo(x, y);
      }
    }

    ctx.stroke();
  }

  return toTexture(c, true);
}

/**
 * The face of the tiles: ivory or porcelain with faint marble veins, shared by all tiles. Every tile shows its own part of it (see
 * tileVariation), so it is drawn at twice the size a single tile would need.
 */
export function tileTexture(theme: TileTheme): THREE.Texture {
  const size = 512;
  const detail = 2;
  const random = seededRandom(37);
  const [c, ctx] = canvas(size * detail, size * detail);
  ctx.scale(detail, detail);

  const base = ctx.createRadialGradient(size * 0.45, size * 0.4, size * 0.05, size / 2, size / 2, size * 0.75);
  base.addColorStop(0, theme.center);
  base.addColorStop(1, theme.edge);
  ctx.fillStyle = base;
  ctx.fillRect(0, 0, size, size);

  ctx.filter = `blur(${1.5 * detail}px)`;
  for (let vein = 0; vein < 20; vein++) {
    ctx.strokeStyle = rgba(theme.vein, 0.08 + random() * 0.12);
    ctx.lineWidth = 0.8 + random() * 2.5;
    ctx.beginPath();
    let x = random() * size;
    let y = random() * size;
    ctx.moveTo(x, y);
    for (let step = 0; step < 6; step++) {
      const nx = x + (random() - 0.3) * size * 0.4;
      const ny = y + (random() - 0.5) * size * 0.35;
      ctx.quadraticCurveTo(x + (random() - 0.5) * 80, y + (random() - 0.5) * 80, nx, ny);
      x = nx;
      y = ny;
    }

    ctx.stroke();
  }

  ctx.filter = 'none';
  return toTexture(c, true);
}

/**
 * The digits 0 to 5 side by side, white on transparent, for the engraved numbers. The material
 * tints them. Requires the fonts of the digits to be loaded (see loadFonts).
 */
export function digitAtlas(theme: DigitTheme): THREE.Texture {
  const cell = 128;
  const [c, ctx] = canvas(cell * 6, cell);
  ctx.textAlign = 'center';
  ctx.textBaseline = 'middle';
  // Playfair Display and Inter have lining figures: a clear 1 and equal heights, easy to read at a glance.
  ctx.font = theme.font;
  for (let digit = 0; digit <= 5; digit++) {
    const x = cell * digit + cell / 2;
    const y = cell / 2 + 4;
    // A soft dark halo suggests the engraving's edge.
    ctx.shadowColor = 'rgba(70, 45, 15, 0.7)';
    ctx.shadowBlur = 6;
    ctx.fillStyle = '#ffffff';
    ctx.fillText(String(digit), x, y);
    ctx.shadowBlur = 0;
  }

  const texture = new THREE.CanvasTexture(c);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = 8;
  texture.premultiplyAlpha = false;
  return texture;
}

/** Loads the fonts the canvas textures use, so the first draw does not fall back. */
export async function loadFonts(): Promise<void> {
  if (!('fonts' in document)) {
    return;
  }

  try {
    await Promise.all([document.fonts.load('700 104px "Playfair Display"'), document.fonts.load('600 96px Inter')]);
  } catch {
    // Fall back to the next font in the list; the digits still render.
  }
}

/** The textures that several parts of the scene share: one set per theme. */
export interface Skin {
  tile: THREE.Texture;
  digits: THREE.Texture;
  wood: THREE.Texture;
}

export function createSkin(theme: SceneTheme): Skin {
  return { tile: tileTexture(theme.tile), digits: digitAtlas(theme.digits), wood: woodTexture(theme.wood) };
}

export function disposeSkin(skin: Skin): void {
  skin.tile.dispose();
  skin.digits.dispose();
  skin.wood.dispose();
}
