import { describe, expect, it } from 'vitest';
import { themeNames, themeOf, themes } from '../src/theme';

/** Relative luminance of an sRGB colour (WCAG). */
function luminance(color: number): number {
  const channel = (value: number) => {
    const c = value / 255;
    return c <= 0.04045 ? c / 12.92 : ((c + 0.055) / 1.055) ** 2.4;
  };

  return 0.2126 * channel((color >> 16) & 255) + 0.7152 * channel((color >> 8) & 255) + 0.0722 * channel(color & 255);
}

const contrast = (a: number, b: number): number => {
  const [light, dark] = [luminance(a), luminance(b)].sort((x, y) => y - x);
  return (light + 0.05) / (dark + 0.05);
};

describe('themes', () => {
  it('has the five themes the settings offer, the classic one first', () => {
    expect(themeNames).toEqual(['classic', 'modern', 'playful', 'midnight', 'porcelain']);
  });

  it('gives the classic theme for a name that is not a theme', () => {
    expect(themeOf('neon')).toBe(themes.classic);
    expect(themeOf('toString')).toBe(themes.classic);
    expect(themeOf('midnight')).toBe(themes.midnight);
  });

  it.each(themeNames)('%s: every digit can be read on the tile', name => {
    const theme = themes[name];
    const face = parseInt(theme.tile.edge.slice(1), 16);
    // The gold of the classic theme (2.1) is the measure: no theme may be harder to read.
    theme.digits.colors.forEach(color => expect(contrast(color, face)).toBeGreaterThan(2.1));
  });

  it.each(themeNames)('%s: the tiles stand out from the felt', name => {
    const theme = themes[name];
    const felt = (theme.felt.base[0] << 16) | (theme.felt.base[1] << 8) | theme.felt.base[2];
    expect(contrast(parseInt(theme.tile.edge.slice(1), 16), felt)).toBeGreaterThan(3);
  });
});
