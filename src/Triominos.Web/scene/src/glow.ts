import * as THREE from 'three';
import { cellCenter, cellYaw, planarUv, TILE_SCALE, UP_CORNERS } from './geometry';

const DEFAULT_CAPACITY = 160;
const GOLD = 0xf0cf7a;

/** A cell that lights up, and whether the picked-up tile fits it in its current rotation. */
export interface GlowCell {
  row: number;
  col: number;
  strong: boolean;
  /** Size factor of the outline; 1 is the size of the cell. */
  scale?: number;
}

/** Soft golden outlines on the cells where the picked-up tile fits: one instanced mesh. */
export class PlacementGlow {
  readonly mesh: THREE.InstancedMesh;
  private readonly matrix = new THREE.Matrix4();
  private readonly position = new THREE.Vector3();
  private readonly rotation = new THREE.Quaternion();
  private readonly scale = new THREE.Vector3(TILE_SCALE, 1, TILE_SCALE);
  private readonly up = new THREE.Vector3(0, 1, 0);
  private readonly color = new THREE.Color();
  private readonly material: THREE.MeshBasicMaterial;

  constructor(private readonly capacity = DEFAULT_CAPACITY) {
    const shape = new THREE.Shape();
    shape.moveTo(UP_CORNERS[0].x, -UP_CORNERS[0].y);
    shape.lineTo(UP_CORNERS[1].x, -UP_CORNERS[1].y);
    shape.lineTo(UP_CORNERS[2].x, -UP_CORNERS[2].y);
    shape.closePath();
    const geometry = new THREE.ShapeGeometry(shape);
    geometry.rotateX(-Math.PI / 2);
    planarUv(geometry);

    this.material = new THREE.MeshBasicMaterial({
      map: glowTexture(),
      transparent: true,
      depthWrite: false,
      blending: THREE.AdditiveBlending,
      toneMapped: false,
      polygonOffset: true,
      polygonOffsetFactor: -1,
    });
    this.mesh = new THREE.InstancedMesh(geometry, this.material, capacity);
    this.mesh.count = 0;
    this.mesh.visible = false;
    this.mesh.renderOrder = 2;
  }

  /** Multiplies the brightness of every outline; fades a flash out. */
  setBrightness(value: number): void {
    this.material.color.setScalar(value);
  }

  setCells(cells: readonly GlowCell[]): void {
    const count = Math.min(cells.length, this.capacity);
    for (let i = 0; i < count; i++) {
      const cell = cells[i];
      const center = cellCenter(cell.row, cell.col);
      this.position.set(center.x, 0.03, center.y);
      this.rotation.setFromAxisAngle(this.up, cellYaw(cell.row, cell.col));
      const size = TILE_SCALE * (cell.scale ?? 1);
      this.scale.set(size, 1, size);
      this.matrix.compose(this.position, this.rotation, this.scale);
      this.mesh.setMatrixAt(i, this.matrix);
      this.mesh.setColorAt(i, this.color.setScalar(cell.strong ? 1 : 0.32));
    }

    this.mesh.count = count;
    this.mesh.visible = count > 0;
    this.mesh.instanceMatrix.needsUpdate = true;
    if (this.mesh.instanceColor) {
      this.mesh.instanceColor.needsUpdate = true;
    }

    this.mesh.computeBoundingSphere();
  }
}

/** A triangle with a soft golden edge and a faint fill; the apex is at the top of the texture. */
function glowTexture(): THREE.Texture {
  const width = 256;
  const height = 222;
  const canvas = document.createElement('canvas');
  canvas.width = width;
  canvas.height = height;
  const ctx = canvas.getContext('2d')!;
  const inset = 26;
  const path = () => {
    ctx.beginPath();
    ctx.moveTo(width / 2, inset * 1.4);
    ctx.lineTo(width - inset * 1.15, height - inset);
    ctx.lineTo(inset * 1.15, height - inset);
    ctx.closePath();
  };

  const color = `#${GOLD.toString(16)}`;
  path();
  ctx.fillStyle = 'rgba(240, 207, 122, 0.16)';
  ctx.fill();
  ctx.shadowColor = color;
  ctx.shadowBlur = 26;
  ctx.lineWidth = 9;
  ctx.lineJoin = 'round';
  ctx.strokeStyle = 'rgba(255, 232, 160, 0.85)';
  path();
  ctx.stroke();
  ctx.shadowBlur = 10;
  ctx.lineWidth = 4;
  path();
  ctx.stroke();

  const texture = new THREE.CanvasTexture(canvas);
  texture.colorSpace = THREE.SRGBColorSpace;
  texture.anisotropy = 4;
  return texture;
}
