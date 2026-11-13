/**
 * A tile as drawn: its cell and the value at each cell corner (0 to 2, clockwise). The optional
 * fields are only used for tiles that are not lying flat on the board.
 */
export interface TileView {
  row: number;
  col: number;
  values: [number, number, number];
  /** Height above the table in world units. */
  lift?: number;
  /** Colour that multiplies the ivory of the tile, as 0xRRGGBB. */
  tint?: number;
}

/** A tile in the rack of the player whose turn it is. */
export interface RackTileView {
  id: number;
  /** Corner values A, B, C, clockwise. */
  values: [number, number, number];
  /** True for a tile the rules single out (the allowed starting tiles). */
  marked: boolean;
}

/** A cell where the picked-up tile fits, with one of the rotations (0 to 2) that works. */
export interface PlacementView {
  row: number;
  col: number;
  rotation: number;
}

/** The C# side: a Blazor DotNetObjectReference, or a stand-in in the dev harness. */
export interface DotNetHost {
  invokeMethodAsync<T = void>(method: string, ...args: unknown[]): Promise<T>;
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
