import * as THREE from 'three';
import { planarUv, TILE_SCALE, UP_CORNERS } from './geometry';

/** How much bigger than a tile the quad is, to leave room for the soft edge. */
const SPREAD = 1.5;

/** Darkness of the shadow of a tile that almost touches the surface. */
const DARKEST = 0.55;

/**
 * The soft shadow under the tile that is being carried: one blurred triangle on the surface below
 * it. It tells how high the tile is held, costs one draw call and no shadow map, and is there at
 * every quality level.
 */
export class CarriedShadow {
  readonly mesh: THREE.Mesh;
  private readonly material: THREE.MeshBasicMaterial;

  /** @param slope how far the shadow moves (x, z) for every unit the tile is lifted: the direction of the light. */
  constructor(private readonly slope: THREE.Vector2) {
    const shape = new THREE.Shape();
    shape.moveTo(UP_CORNERS[0].x, -UP_CORNERS[0].y);
    shape.lineTo(UP_CORNERS[1].x, -UP_CORNERS[1].y);
    shape.lineTo(UP_CORNERS[2].x, -UP_CORNERS[2].y);
    shape.closePath();
    const geometry = new THREE.ShapeGeometry(shape);
    geometry.rotateX(-Math.PI / 2);
    geometry.scale(SPREAD, 1, SPREAD);
    planarUv(geometry);

    this.material = new THREE.MeshBasicMaterial({
      color: 0x000000,
      alphaMap: shadowTexture(),
      transparent: true,
      depthWrite: false,
      polygonOffset: true,
      polygonOffsetFactor: -1,
    });
    this.mesh = new THREE.Mesh(geometry, this.material);
    this.mesh.renderOrder = 1;
    this.mesh.visible = false;
  }

  /**
   * Puts the shadow under a tile at (x, z) with the given yaw.
   * @param surface height of what the shadow falls on (the felt, or the top of a tile).
   * @param height how far the underside of the tile is above that surface.
   */
  show(x: number, z: number, yaw: number, surface: number, height: number): void {
    const h = Math.max(height, 0);
    this.mesh.position.set(x + this.slope.x * h, surface + 0.012, z + this.slope.y * h);
    this.mesh.rotation.y = yaw;
    // Higher up the shadow is wider and fainter.
    this.mesh.scale.setScalar(TILE_SCALE * (1 + 0.35 * h));
    this.material.opacity = DARKEST / (1 + 2.2 * h);
    this.mesh.visible = true;
  }

  hide(): void {
    this.mesh.visible = false;
  }
}

/** A white triangle with a blurred edge, apex at the top, used as the opacity of the shadow. */
function shadowTexture(): THREE.Texture {
  const width = 128;
  const height = 111;
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d')!;
  ctx.fillStyle = '#000000';
  ctx.fillRect(0, 0, width, height);

  // The tile takes 1 / SPREAD of the quad, around the centroid of the triangle (a third from the base).
  const cx = width / 2;
  const cy = (height * 2) / 3;
  const k = 1 / SPREAD;
  // Drawn far off the canvas: only its blurred shadow lands on it, which also works without ctx.filter.
  const away = width * 4;
  ctx.shadowColor = '#ffffff';
  ctx.shadowBlur = 12;
  ctx.shadowOffsetX = away;
  ctx.fillStyle = '#ffffff';
  ctx.beginPath();
  ctx.moveTo(cx - away, cy - cy * k);
  ctx.lineTo(cx - away + (width / 2) * k, cy + (height - cy) * k);
  ctx.lineTo(cx - away - (width / 2) * k, cy + (height - cy) * k);
  ctx.closePath();
  ctx.fill();

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.NoColorSpace;
  return texture;
}
