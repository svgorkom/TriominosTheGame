/** A tile on the board, as sent by C#: its cell and the value at each cell corner (0 to 2, clockwise). */
export interface TileView {
  row: number;
  col: number;
  values: [number, number, number];
}

/** Rendering quality; lower settings trade shadows and resolution for speed. */
export type Quality = 'low' | 'medium' | 'high';

export interface QualitySettings {
  pixelRatioCap: number;
  shadows: boolean;
  shadowMapSize: number;
}

export const qualitySettings: Record<Quality, QualitySettings> = {
  low: { pixelRatioCap: 1, shadows: false, shadowMapSize: 1024 },
  medium: { pixelRatioCap: 1.5, shadows: true, shadowMapSize: 2048 },
  high: { pixelRatioCap: 2, shadows: true, shadowMapSize: 4096 },
};
