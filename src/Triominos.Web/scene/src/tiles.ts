import * as THREE from 'three';
import { cellCenter, cellYaw, createTileGeometry, SIDE, TILE_SCALE, TILE_TOP, UP_CORNERS } from './geometry';
import type { TilePose, TileView } from './types';
import type { Skin } from './textures';
import type { SceneTheme } from './theme';
import { GRAIN_WINDOW, tileVariation } from './variation';

const DIGIT_SIZE = SIDE * 0.3;

/** How far from the centroid towards its corner a digit sits (0 = centre, 1 = corner). */
const DIGIT_INSET = 0.58;

/** Capacity of the board layer: the 56 tiles of the standard set. */
export const BOARD_CAPACITY = 56;

/**
 * Draws a set of tiles with one instanced mesh for the bodies and one instanced mesh per digit
 * value, so the cost does not grow with the number of tiles (7 draw calls per layer).
 */
export class TileLayer {
  readonly group = new THREE.Group();
  private readonly bodies: THREE.InstancedMesh;
  private readonly face: THREE.MeshPhysicalMaterial;
  /** One material per digit value, so a theme can give every value its own colour. */
  private readonly inks: THREE.MeshPhysicalMaterial[] = [];
  private readonly digits: THREE.InstancedMesh[] = [];
  private readonly matrix = new THREE.Matrix4();
  private readonly pivot = new THREE.Matrix4();
  private readonly position = new THREE.Vector3();
  private readonly rotation = new THREE.Quaternion();
  private readonly flipRotation = new THREE.Quaternion();
  private readonly uniform = new THREE.Vector3();
  private readonly up = new THREE.Vector3(0, 1, 0);
  private readonly right = new THREE.Vector3(1, 0, 0);
  private readonly color = new THREE.Color();
  /** Turns a tile around its middle: the pivot sits halfway through its thickness. */
  private readonly lowerPivot = new THREE.Matrix4().makeTranslation(0, -TILE_TOP / 2, 0);
  private readonly bodyScale = new THREE.Matrix4().makeScale(TILE_SCALE, 1, TILE_SCALE);
  /** Where each digit lies on a tile lying face up, relative to the tile's own origin. */
  private readonly digitMatrices: THREE.Matrix4[] = UP_CORNERS.map(corner => {
    const x = corner.x * DIGIT_INSET * TILE_SCALE;
    const z = corner.y * DIGIT_INSET * TILE_SCALE;
    // The digit's top points towards its corner.
    return new THREE.Matrix4().compose(
      new THREE.Vector3(x, TILE_TOP + 0.002, z),
      new THREE.Quaternion().setFromAxisAngle(new THREE.Vector3(0, 1, 0), Math.atan2(-x, -z)),
      new THREE.Vector3(1, 1, 1),
    );
  });
  private readonly digitCounts = [0, 0, 0, 0, 0, 0];
  /** Per tile, the window on the face texture it shows: centre (u, v) and angle. */
  private readonly grain: THREE.InstancedBufferAttribute;

  constructor(private readonly capacity: number, castShadow = true) {
    const geometry = createTileGeometry();
    this.grain = new THREE.InstancedBufferAttribute(new Float32Array(capacity * 3), 3);
    this.grain.setUsage(THREE.DynamicDrawUsage);
    geometry.setAttribute('grain', this.grain);
    this.face = new THREE.MeshPhysicalMaterial({ color: 0xffffff, roughness: 0.32, clearcoat: 0.9, clearcoatRoughness: 0.15, sheen: 0.2 });
    this.face.onBeforeCompile = showOwnGrain;
    this.bodies = new THREE.InstancedMesh(geometry, this.face, capacity);
    this.bodies.castShadow = castShadow;
    this.bodies.receiveShadow = true;
    this.bodies.count = 0;
    this.bodies.visible = false;
    this.group.add(this.bodies);

    for (let digit = 0; digit <= 5; digit++) {
      const ink = new THREE.MeshPhysicalMaterial({
        envMapIntensity: 0.7,
        // Flat decals at one height: blending gives smooth edges without sorting problems.
        transparent: true,
        depthWrite: false,
        polygonOffset: true,
        polygonOffsetFactor: -2,
      });
      this.inks.push(ink);
      const geometry = new THREE.PlaneGeometry(DIGIT_SIZE, DIGIT_SIZE);
      geometry.rotateX(-Math.PI / 2);
      const uv = geometry.getAttribute('uv');
      for (let i = 0; i < uv.count; i++) {
        uv.setX(i, (digit + uv.getX(i)) / 6);
      }

      const mesh = new THREE.InstancedMesh(geometry, ink, capacity * 3);
      mesh.count = 0;
      mesh.visible = false;
      this.digits.push(mesh);
      this.group.add(mesh);
    }
  }

  /** Dresses the tiles in a theme; the textures come from the shared skin. */
  applyTheme(theme: SceneTheme, skin: Skin): void {
    this.face.map = skin.tile;
    this.face.sheenColor.set(theme.tile.sheen);
    this.face.needsUpdate = true;
    this.inks.forEach((ink, digit) => {
      ink.map = skin.digits;
      ink.color.set(theme.digits.colors[digit]);
      ink.metalness = theme.digits.metalness;
      ink.roughness = theme.digits.roughness;
      ink.needsUpdate = true;
    });
  }

  /**
   * Replaces all tiles with tiles lying on cells. Cheap: only instance matrices and colours are
   * rewritten. Loose tiles lie as a hand put them down, each a little out of line in its own way
   * (see tileVariation); the others lie exactly on their cells.
   */
  setTiles(tiles: readonly TileView[], loose = false): void {
    const count = Math.min(tiles.length, this.capacity);
    this.digitCounts.fill(0);
    for (let i = 0; i < count; i++) {
      const tile = tiles[i];
      const center = cellCenter(tile.row, tile.col);
      const yaw = cellYaw(tile.row, tile.col);
      if (loose) {
        const lie = tileVariation(tile.values);
        this.put(i, center.x + lie.dx, tile.lift ?? 0, center.y + lie.dz, yaw + lie.yaw, 0, 1, tile.values, tile.tint);
      } else {
        this.put(i, center.x, tile.lift ?? 0, center.y, yaw, 0, 1, tile.values, tile.tint);
      }
    }

    this.commit(count);
  }

  /** Replaces all tiles with freely posed tiles, for tiles in flight. */
  setPoses(poses: readonly TilePose[]): void {
    const count = Math.min(poses.length, this.capacity);
    this.digitCounts.fill(0);
    for (let i = 0; i < count; i++) {
      const pose = poses[i];
      this.put(i, pose.x, pose.y, pose.z, pose.yaw, pose.flip, pose.scale, pose.values, pose.tint);
    }

    this.commit(count);
  }

  /** Writes one tile and its three digits. y is the height of the tile's underside when lying face up. */
  private put(
    index: number,
    x: number,
    y: number,
    z: number,
    yaw: number,
    flip: number,
    scale: number,
    values: readonly number[],
    tint: number | undefined,
  ): void {
    this.position.set(x, y + (TILE_TOP / 2) * scale, z);
    this.rotation.setFromAxisAngle(this.up, yaw);
    if (flip !== 0) {
      this.rotation.multiply(this.flipRotation.setFromAxisAngle(this.right, flip * Math.PI));
    }

    this.pivot.compose(this.position, this.rotation, this.uniform.setScalar(scale)).multiply(this.lowerPivot);
    this.matrix.copy(this.pivot).multiply(this.bodyScale);
    this.bodies.setMatrixAt(index, this.matrix);
    this.bodies.setColorAt(index, this.color.set(tint ?? 0xffffff));
    this.grain.set(tileVariation(values).grain, index * 3);

    for (let corner = 0; corner < 3; corner++) {
      const value = values[corner];
      this.matrix.copy(this.pivot).multiply(this.digitMatrices[corner]);
      this.digits[value].setMatrixAt(this.digitCounts[value]++, this.matrix);
    }
  }

  private commit(count: number): void {
    this.bodies.count = count;
    this.bodies.visible = count > 0;
    this.bodies.instanceMatrix.needsUpdate = true;
    this.grain.needsUpdate = true;
    if (this.bodies.instanceColor) {
      this.bodies.instanceColor.needsUpdate = true;
    }

    this.bodies.computeBoundingSphere();
    this.digits.forEach((mesh, value) => {
      mesh.count = this.digitCounts[value];
      mesh.visible = mesh.count > 0;
      mesh.instanceMatrix.needsUpdate = true;
      mesh.computeBoundingSphere();
    });
  }
}

/**
 * Makes every tile show its own part of the face texture: the tile's UVs are shrunk to a window,
 * turned and moved by the per-instance `grain` attribute, so no two tiles have the same veins.
 */
function showOwnGrain(shader: { vertexShader: string }): void {
  shader.vertexShader = shader.vertexShader
    .replace('#include <common>', '#include <common>\nattribute vec3 grain;')
    .replace(
      '#include <uv_vertex>',
      `#include <uv_vertex>
#ifdef USE_MAP
  {
    float grainSin = sin(grain.z);
    float grainCos = cos(grain.z);
    vMapUv = grain.xy + mat2(grainCos, grainSin, -grainSin, grainCos) * (vMapUv - 0.5) * ${GRAIN_WINDOW.toFixed(3)};
  }
#endif`,
    );
}
