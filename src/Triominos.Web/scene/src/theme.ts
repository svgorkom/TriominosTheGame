/**
 * The looks of the table. A theme only changes colours, the wood and the typeface of the digits;
 * every theme keeps the lacquered tiles and rim, so the table stays glossy whatever its colours.
 * The matching colours of the menus and the HUD are in wwwroot/css/app.css (data-theme).
 */

/** Names match the values of the C# <c>TableTheme</c> enum in lower case and the data-theme values in the CSS. */
export type ThemeName = 'classic' | 'modern' | 'playful' | 'midnight' | 'porcelain';

/** A colour as red, green and blue, 0 to 255. */
export type Rgb = readonly [number, number, number];

export interface FeltTheme {
  base: Rgb;
  /** Fibres that catch the light and fibres that lie in shadow. */
  lightFibre: Rgb;
  darkFibre: Rgb;
  /** Colour of the nap where the light strikes it at a low angle. */
  sheen: number;
}

export interface WoodTheme {
  /** The base colour across the grain: one edge, the middle, the other edge. */
  base: readonly [string, string, string];
  darkGrain: Rgb;
  lightGrain: Rgb;
}

export interface TileTheme {
  /** The face is a little lighter in the middle than at the edge. */
  center: string;
  edge: string;
  vein: Rgb;
  sheen: number;
}

export interface DigitTheme {
  /** Colour of the digits 0 to 5. */
  colors: readonly [number, number, number, number, number, number];
  /** 1 for metal inlay, 0 for enamel. */
  metalness: number;
  roughness: number;
  /** CSS font of the digits; the family must be one of the fonts the page loads. */
  font: string;
}

export interface SceneTheme {
  felt: FeltTheme;
  wood: WoodTheme;
  tile: TileTheme;
  digits: DigitTheme;
  /** The lamp above the table, and the light from above and below that fills the shadows. */
  sun: number;
  sky: number;
  ground: number;
  /** The outlines of the open cells and the flash of a bonus. */
  glow: Rgb;
  sparkle: number;
  /** Tint of the tiles the rules single out in the rack; it multiplies the colour of the tile. */
  marked: number;
}

const GOLD = 0xc2973c;
const SERIF_DIGITS = '700 104px "Playfair Display", Georgia, serif';
const SANS_DIGITS = '600 96px Inter, system-ui, sans-serif';

const six = (color: number): DigitTheme['colors'] => [color, color, color, color, color, color];

export const themes: Record<ThemeName, SceneTheme> = {
  // Green felt, lacquered walnut, ivory and gold: the casino table the game started with.
  classic: {
    felt: { base: [15, 61, 46], lightFibre: [64, 128, 100], darkFibre: [4, 22, 15], sheen: 0x5fae8c },
    wood: { base: ['#46291a', '#3b2416', '#4a2c1b'], darkGrain: [28, 14, 7], lightGrain: [122, 78, 46] },
    tile: { center: '#fbf7ec', edge: '#ece3cf', vein: [150, 128, 96], sheen: 0xfff6e0 },
    digits: { colors: six(GOLD), metalness: 1, roughness: 0.38, font: SERIF_DIGITS },
    sun: 0xffe9c7,
    sky: 0xfff2dc,
    ground: 0x1c120a,
    glow: [240, 207, 122],
    sparkle: 0xffd36b,
    marked: 0xffe8a6,
  },
  // Graphite cloth, black piano lacquer, white porcelain and black chrome.
  modern: {
    felt: { base: [40, 43, 49], lightFibre: [104, 110, 122], darkFibre: [14, 16, 20], sheen: 0x6f7888 },
    wood: { base: ['#131417', '#0d0e10', '#16171a'], darkGrain: [4, 4, 5], lightGrain: [52, 55, 62] },
    tile: { center: '#fdfdfe', edge: '#eceef2', vein: [150, 156, 168], sheen: 0xffffff },
    digits: { colors: six(0x353b44), metalness: 1, roughness: 0.3, font: SANS_DIGITS },
    sun: 0xf3f6ff,
    sky: 0xf2f5ff,
    ground: 0x0e0f12,
    glow: [205, 220, 240],
    sparkle: 0xdfe9ff,
    marked: 0xcfe0ff,
  },
  // Azure felt, light maple and cream tiles; every value has its own enamel colour.
  playful: {
    felt: { base: [24, 86, 156], lightFibre: [98, 160, 226], darkFibre: [8, 34, 70], sheen: 0x7fb9f2 },
    wood: { base: ['#c99b5e', '#bd8d4f', '#d0a368'], darkGrain: [120, 78, 38], lightGrain: [238, 204, 152] },
    tile: { center: '#fffaf0', edge: '#f4ead6', vein: [176, 150, 110], sheen: 0xfff6e0 },
    digits: { colors: [0x5d6673, 0x1f6fd1, 0x2a9650, 0xd63c3c, 0x8a45c2, 0xe07a14], metalness: 0, roughness: 0.3, font: SERIF_DIGITS },
    sun: 0xfff1d2,
    sky: 0xfff6e6,
    ground: 0x10243f,
    glow: [255, 214, 120],
    sparkle: 0xffc94d,
    marked: 0xffe08a,
  },
  // Midnight-blue felt, ebony, pearl-white tiles and rose gold.
  midnight: {
    felt: { base: [12, 26, 60], lightFibre: [58, 84, 148], darkFibre: [3, 8, 24], sheen: 0x4a67b0 },
    wood: { base: ['#1e1613', '#16100e', '#211815'], darkGrain: [6, 4, 3], lightGrain: [78, 56, 46] },
    tile: { center: '#f7f8fc', edge: '#e2e6f0', vein: [128, 140, 170], sheen: 0xeef2ff },
    digits: { colors: six(0xb87657), metalness: 1, roughness: 0.34, font: SERIF_DIGITS },
    sun: 0xe6ecff,
    sky: 0xe8eeff,
    ground: 0x070b16,
    glow: [240, 190, 165],
    sparkle: 0xf5c2a6,
    marked: 0xffd9c4,
  },
  // The light one: grey-blue felt, pale ash, white porcelain and gold.
  porcelain: {
    felt: { base: [72, 98, 122], lightFibre: [138, 164, 186], darkFibre: [38, 56, 74], sheen: 0x7f9db5 },
    wood: { base: ['#dccdb0', '#d1c09f', '#e2d4b9'], darkGrain: [150, 126, 92], lightGrain: [246, 236, 214] },
    tile: { center: '#ffffff', edge: '#f1f3f6', vein: [170, 178, 190], sheen: 0xffffff },
    digits: { colors: six(0xb08327), metalness: 1, roughness: 0.38, font: SERIF_DIGITS },
    sun: 0xfffaf0,
    sky: 0xffffff,
    ground: 0x56646f,
    // A deeper gold than elsewhere: on the pale felt a light glow would wash out to white.
    glow: [255, 196, 70],
    sparkle: 0xffd060,
    marked: 0xffe8a6,
  },
};

export const themeNames = Object.keys(themes) as ThemeName[];

/** The theme with a name; the classic one for a name that is not a theme. */
export function themeOf(name: string): SceneTheme {
  return Object.hasOwn(themes, name) ? themes[name as ThemeName] : themes.classic;
}

export const rgba = (color: Rgb, alpha: number): string => `rgba(${color[0]}, ${color[1]}, ${color[2]}, ${alpha})`;
