import * as THREE from 'three';

/**
 * Board geometry, mirroring Triominos.Core.Board.Cell and Vertex. A cell points up when
 * row + col is even. Vertex (row, x) lies at world x = x * SIDE / 2, z = row * HEIGHT; rows grow
 * toward the viewer (+z), so "up" on screen is -z.
 */

/** Length of a tile side in world units. */
export const SIDE = 1.6;

/** Height of a tile triangle. */
export const HEIGHT = (SIDE * Math.sqrt(3)) / 2;

/** Thickness of a tile, without bevel. */
export const TILE_DEPTH = 0.11;

/** Tiles are drawn slightly smaller than their cell, leaving a thin gap. */
export const TILE_SCALE = 0.955;

export function isUp(row: number, col: number): boolean {
  return (row + col) % 2 === 0;
}

/** World position (x, z) of the centroid of a cell. */
export function cellCenter(row: number, col: number): THREE.Vector2 {
  const x = ((col + 1) * SIDE) / 2;
  const z = (row + (isUp(row, col) ? 2 / 3 : 1 / 3)) * HEIGHT;
  return new THREE.Vector2(x, z);
}

/**
 * The cell under a world position (x, z); the inverse of cellCenter. In row r, cell c spans
 * half-cell units c to c + 2 (u = 2x / SIDE) and points up when r + c is even; at depth v into
 * the row (0 = top edge, 1 = bottom edge) an up cell is v wide on each side of its apex column
 * u = c + 1 and a down cell is 1 - v wide.
 */
export function cellAt(x: number, z: number): { row: number; col: number } {
  const row = Math.floor(z / HEIGHT);
  const v = z / HEIGHT - row;
  const u = (2 * x) / SIDE;
  let best = { row, col: Math.floor(u) };
  let bestExcess = Infinity;
  for (let col = Math.floor(u) - 2; col <= Math.floor(u) + 1; col++) {
    const half = isUp(row, col) ? v : 1 - v;
    const excess = Math.abs(u - (col + 1)) - half;
    if (excess < bestExcess) {
      bestExcess = excess;
      best = { row, col };
    }
  }

  return best;
}

/**
 * Corner positions (x, z) of an up-pointing tile relative to its centroid, clockwise from the
 * apex: 0 = apex, 1 = bottom right, 2 = bottom left. A down-pointing tile is the same shape
 * turned 180 degrees, which maps these onto its corners 0 = bottom, 1 = top left, 2 = top right,
 * exactly matching the corner numbering in Core.
 */
export const UP_CORNERS: readonly THREE.Vector2[] = [
  new THREE.Vector2(0, (-2 * HEIGHT) / 3),
  new THREE.Vector2(SIDE / 2, HEIGHT / 3),
  new THREE.Vector2(-SIDE / 2, HEIGHT / 3),
];

/** World positions (x, z) of the three corners of a cell. */
export function cellCorners(row: number, col: number): THREE.Vector2[] {
  const center = cellCenter(row, col);
  // A down-pointing cell is the up-pointing shape turned 180 degrees.
  const sign = isUp(row, col) ? 1 : -1;
  return UP_CORNERS.map(corner => new THREE.Vector2(center.x + sign * corner.x, center.y + sign * corner.y));
}

/** Rotation of a tile around the vertical axis. */
export function cellYaw(row: number, col: number): number {
  return isUp(row, col) ? 0 : Math.PI;
}

/** A tile: a beveled triangular prism lying on the table, top face at y = TILE_DEPTH + bevel. */
export function createTileGeometry(): THREE.BufferGeometry {
  const shape = new THREE.Shape();
  // Shape space: (x, y) becomes world (x, -z) after rotateX(-PI/2).
  shape.moveTo(UP_CORNERS[0].x, -UP_CORNERS[0].y);
  shape.lineTo(UP_CORNERS[1].x, -UP_CORNERS[1].y);
  shape.lineTo(UP_CORNERS[2].x, -UP_CORNERS[2].y);
  shape.closePath();

  const bevel = 0.05;
  const geometry = new THREE.ExtrudeGeometry(shape, {
    depth: TILE_DEPTH,
    bevelEnabled: true,
    bevelThickness: bevel,
    bevelSize: bevel * 1.2,
    bevelOffset: -bevel * 1.2,
    bevelSegments: 5,
    curveSegments: 1,
  });
  geometry.rotateX(-Math.PI / 2);
  geometry.translate(0, bevel, 0);
  planarUv(geometry);
  geometry.computeVertexNormals();
  return geometry;
}

/** Height of the top face of a tile. */
export const TILE_TOP = TILE_DEPTH + 0.1;

/** Replaces the UVs with a top-down projection, so one texture covers every face consistently. */
export function planarUv(geometry: THREE.BufferGeometry, scale = 1): void {
  geometry.computeBoundingBox();
  const box = geometry.boundingBox!;
  const position = geometry.getAttribute('position');
  const uv = new Float32Array(position.count * 2);
  const width = (box.max.x - box.min.x) / scale;
  const depth = (box.max.z - box.min.z) / scale;
  for (let i = 0; i < position.count; i++) {
    uv[i * 2] = (position.getX(i) - box.min.x) / width;
    uv[i * 2 + 1] = 1 - (position.getZ(i) - box.min.z) / depth;
  }

  geometry.setAttribute('uv', new THREE.BufferAttribute(uv, 2));
}
