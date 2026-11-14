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

/** A tile placed freely in the world, for tiles in flight. */
export interface TilePose {
  x: number;
  /** Height of the underside when the tile lies face up. */
  y: number;
  z: number;
  yaw: number;
  /** 0 = face up, 1 = face down; in between the tile is turning over. */
  flip: number;
  /** Uniform size factor; 1 is the size on the board. */
  scale: number;
  values: readonly [number, number, number];
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

/** A cell a tile may be laid on at all, whether or not the picked-up tile fits it. */
export interface CellView {
  row: number;
  col: number;
}

/** The answer to picking up a tile: the cells to light up, and where the tile really fits. */
export interface SelectionView {
  /** Every open cell. This is all the player gets to see; it says nothing about the tile. */
  open: CellView[];
  fits: PlacementView[];
}

/** One line of a score: what it is for, the points, and whether it earns the gold effect. */
export interface ScoreLine {
  label: string;
  points: number;
  gold: boolean;
}

/**
 * A tile was laid. The scene starts it from the ghost tile and lets it bounce; for a computer
 * (`hidden`) there is no ghost tile, so it flies in face down from the player's panel instead.
 */
export interface PlacedEvent {
  kind: 'placed';
  player: number;
  /** True for a computer player: the tile was not in a rack on screen. */
  hidden: boolean;
  tileId: number;
  row: number;
  col: number;
  /** Corner values as laid, clockwise from the cell's corner 0. */
  values: [number, number, number];
  lines: ScoreLine[];
  total: number;
  /** The cells of every hexagon the tile completed, each once; empty without a hexagon bonus. */
  hexagon: { row: number; col: number }[];
}

/**
 * A tile was drawn from the pool into the rack of the current player. For a computer (`hidden`)
 * the tile flies face down to the player's panel and its values are not known to the scene.
 */
export interface DrawnEvent {
  kind: 'drawn';
  player: number;
  /** True for a computer player: the tile is not shown. */
  hidden: boolean;
  tileId: number;
  penalty: number;
}

export interface PassedEvent {
  kind: 'passed';
  player: number;
  /** True for a computer player: the penalty shows at its panel, not at the rack. */
  hidden: boolean;
  penalty: number;
}

/** What one player's remaining tiles are worth at the end of the round. */
export interface SettlementStep {
  name: string;
  tiles: RackTileView[];
  line: ScoreLine;
}

/** The round ended. A draw has no winner and nothing to settle. */
export interface FinishedEvent {
  kind: 'finished';
  winner: number | null;
  goingOut: ScoreLine | null;
  rivals: SettlementStep[];
}

export type MoveEvent = PlacedEvent | DrawnEvent | PassedEvent | FinishedEvent;

/** Everything the scene needs to show one move: what happened, and how the table looks afterwards. */
export interface MoveView {
  events: MoveEvent[];
  /** The board after the move. */
  board: TileView[];
  /** The rack to show afterwards; empty when the turn passes on or the round ended. */
  rack: RackTileView[];
}

/** The deal at the start of a round: how many tiles each player gets, in turn order. */
export interface DealView {
  counts: number[];
}

/** The ghost tile the player let go of: where the placement animation starts. */
export interface GhostPose {
  row: number;
  col: number;
  /** Height above the table in world units. */
  lift: number;
  tint: number;
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
  /** Sparkles per gold burst; the low setting uses fewer. */
  sparkles: number;
}

export const qualitySettings: Record<Quality, QualitySettings> = {
  low: { pixelRatioCap: 1, shadows: false, shadowMapSize: 1024, sparkles: 12 },
  medium: { pixelRatioCap: 1.5, shadows: true, shadowMapSize: 2048, sparkles: 32 },
  high: { pixelRatioCap: 2, shadows: true, shadowMapSize: 4096, sparkles: 56 },
};
