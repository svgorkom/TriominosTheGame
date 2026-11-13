import * as THREE from 'three';
import { cellCenter, cellYaw, createTileGeometry, SIDE, TILE_SCALE, TILE_TOP, UP_CORNERS } from './geometry';
import type { TileView } from './types';
import { digitAtlas, ivoryTexture } from './textures';

const MAX_TILES = 56;
const DIGIT_SIZE = SIDE * 0.3;

/** How far from the centroid towards its corner a digit sits (0 = centre, 1 = corner). */
const DIGIT_INSET = 0.58;

/**
 * Draws all tiles with one instanced mesh for the bodies and one instanced mesh per digit value,
 * so the cost does not grow with the number of tiles (7 draw calls in total).
 */
export class TileLayer {
  readonly group = new THREE.Group();
  private readonly bodies: THREE.InstancedMesh;
  private readonly digits: THREE.InstancedMesh[] = [];
  private readonly matrix = new THREE.Matrix4();
  private readonly position = new THREE.Vector3();
  private readonly rotation = new THREE.Quaternion();
  private readonly scale = new THREE.Vector3(TILE_SCALE, 1, TILE_SCALE);
  private readonly up = new THREE.Vector3(0, 1, 0);

  constructor() {
    const ivory = ivoryTexture();
    this.bodies = new THREE.InstancedMesh(
      createTileGeometry(),
      new THREE.MeshPhysicalMaterial({
        map: ivory,
        color: 0xffffff,
        roughness: 0.32,
        clearcoat: 0.9,
        clearcoatRoughness: 0.15,
        sheen: 0.2,
        sheenColor: new THREE.Color(0xfff6e0),
      }),
      MAX_TILES,
    );
    this.bodies.castShadow = true;
    this.bodies.receiveShadow = true;
    this.bodies.count = 0;
    this.group.add(this.bodies);

    const atlas = digitAtlas();
    const gold = new THREE.MeshPhysicalMaterial({
      color: 0xc2973c,
      map: atlas,
      metalness: 1,
      roughness: 0.38,
      envMapIntensity: 0.7,
      // Flat decals at one height: blending gives smooth edges without sorting problems.
      transparent: true,
      depthWrite: false,
      polygonOffset: true,
      polygonOffsetFactor: -2,
    });

    for (let digit = 0; digit <= 5; digit++) {
      const geometry = new THREE.PlaneGeometry(DIGIT_SIZE, DIGIT_SIZE);
      geometry.rotateX(-Math.PI / 2);
      const uv = geometry.getAttribute('uv');
      for (let i = 0; i < uv.count; i++) {
        uv.setX(i, (digit + uv.getX(i)) / 6);
      }

      const mesh = new THREE.InstancedMesh(geometry, gold, MAX_TILES * 3);
      mesh.count = 0;
      this.digits.push(mesh);
      this.group.add(mesh);
    }
  }

  /** Replaces all tiles. Cheap: only instance matrices are rewritten. */
  setTiles(tiles: readonly TileView[]): void {
    const count = Math.min(tiles.length, MAX_TILES);
    const digitCounts = [0, 0, 0, 0, 0, 0];

    for (let i = 0; i < count; i++) {
      const tile = tiles[i];
      const center = cellCenter(tile.row, tile.col);
      const yaw = cellYaw(tile.row, tile.col);

      this.position.set(center.x, 0, center.y);
      this.rotation.setFromAxisAngle(this.up, yaw);
      this.matrix.compose(this.position, this.rotation, this.scale);
      this.bodies.setMatrixAt(i, this.matrix);

      for (let corner = 0; corner < 3; corner++) {
        const value = tile.values[corner];
        const offset = UP_CORNERS[corner].clone().rotateAround(new THREE.Vector2(), -yaw).multiplyScalar(DIGIT_INSET * TILE_SCALE);
        // The digit's top points towards its corner.
        const digitYaw = Math.atan2(-offset.x, -offset.y);
        this.position.set(center.x + offset.x, TILE_TOP + 0.002, center.y + offset.y);
        this.rotation.setFromAxisAngle(this.up, digitYaw);
        this.matrix.compose(this.position, this.rotation, new THREE.Vector3(1, 1, 1));
        const mesh = this.digits[value];
        mesh.setMatrixAt(digitCounts[value]++, this.matrix);
      }
    }

    this.bodies.count = count;
    this.bodies.instanceMatrix.needsUpdate = true;
    this.bodies.computeBoundingSphere();
    this.digits.forEach((mesh, value) => {
      mesh.count = digitCounts[value];
      mesh.instanceMatrix.needsUpdate = true;
      mesh.computeBoundingSphere();
    });
  }
}
