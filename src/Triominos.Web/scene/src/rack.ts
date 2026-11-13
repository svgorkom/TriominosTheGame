import * as THREE from 'three';
import { cellAt, cellCenter, HEIGHT, SIDE, TILE_TOP } from './geometry';
import { roundedRect } from './table';
import { walnutTexture } from './textures';
import { TileLayer } from './tiles';
import type { RackTileView, TileView } from './types';

const CAPACITY = 40;
const TILT = THREE.MathUtils.degToRad(48);
const TRAY_DEPTH = 0.3;
const TRAY_BEVEL = 0.06;
const LIFT_HOVER = 0.32;
const LIFT_MARKED = 0.16;
const TINT_MARKED = 0xffe8a6;

/**
 * The rack of the player whose turn it is: the tiles lie in a strip (one row of the board grid,
 * so up and down triangles alternate) on a walnut tray. It is its own scene, drawn after the
 * table into the bottom strip of the canvas, so it stays the same size and in the same place
 * whatever the table camera does.
 */
export class Rack {
  readonly scene = new THREE.Scene();
  readonly camera = new THREE.PerspectiveCamera(26, 1, 0.5, 120);
  /** Height of the rack strip in CSS pixels. */
  heightPx = 0;

  private width = 0;
  private tiles: RackTileView[] = [];
  private heldId: number | null = null;
  private hoverId: number | null = null;
  private readonly layer = new TileLayer(CAPACITY, false);
  private readonly tray = new THREE.Group();
  private readonly trayMaterial = new THREE.MeshPhysicalMaterial({ map: walnutTexture(), roughness: 0.5, clearcoat: 1, clearcoatRoughness: 0.12 });
  private readonly insetMaterial = new THREE.MeshStandardMaterial({ color: 0x0f3d2e, roughness: 0.97 });
  private readonly raycaster = new THREE.Raycaster();
  private readonly plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -TILE_TOP);
  private readonly hit3 = new THREE.Vector3();
  private readonly ndc = new THREE.Vector2();

  constructor() {
    const key = new THREE.DirectionalLight(0xffe9c7, 2.6);
    key.position.set(-6, 14, 9);
    this.scene.add(key, new THREE.HemisphereLight(0xfff2dc, 0x1c120a, 0.35), this.tray, this.layer.group);
  }

  /** True while the rack has tiles to show. */
  get visible(): boolean {
    return this.tiles.length > 0;
  }

  setEnvironment(texture: THREE.Texture | null): void {
    this.scene.environment = texture;
    this.scene.environmentIntensity = 0.35;
  }

  /** Replaces the tiles. Also puts back a tile that was being held. */
  setTiles(tiles: readonly RackTileView[]): void {
    this.tiles = [...tiles];
    this.heldId = null;
    this.hoverId = null;
    this.rebuildTray();
    this.refresh();
    this.fit();
  }

  /** Hides one tile while it is being carried over the table. */
  setHeld(id: number | null): void {
    this.heldId = id;
    this.refresh();
  }

  /** Lifts the tile under the pointer; returns whether anything changed. */
  setHover(id: number | null): boolean {
    if (id === this.hoverId) {
      return false;
    }

    this.hoverId = id;
    this.refresh();
    return true;
  }

  /** Sizes the strip for a canvas of the given CSS size and returns the strip height. */
  resize(canvasWidth: number, canvasHeight: number): number {
    this.width = canvasWidth;
    this.heightPx = Math.round(THREE.MathUtils.clamp(canvasHeight * 0.25, 120, 200));
    this.fit();
    return this.heightPx;
  }

  /** The tile under a point in the strip (CSS pixels from its top left), if any. */
  tileAt(x: number, y: number): RackTileView | null {
    if (!this.visible || this.width === 0) {
      return null;
    }

    this.ndc.set((x / this.width) * 2 - 1, -((y / this.heightPx) * 2 - 1));
    this.raycaster.setFromCamera(this.ndc, this.camera);
    if (!this.raycaster.ray.intersectPlane(this.plane, this.hit3)) {
      return null;
    }

    const local = this.hit3.sub(this.layer.group.position);
    const cell = cellAt(local.x, local.z);
    return cell.row === 0 && cell.col >= 0 && cell.col < this.tiles.length ? this.tiles[cell.col] : null;
  }

  /** Centre of a tile in strip pixels, for tests and the debug overlay. */
  tileCenter(id: number): { x: number; y: number } | null {
    const index = this.tiles.findIndex(t => t.id === id);
    if (index < 0) {
      return null;
    }

    const center = cellCenter(0, index);
    const point = new THREE.Vector3(center.x, TILE_TOP, center.y).add(this.layer.group.position).project(this.camera);
    return { x: ((point.x + 1) / 2) * this.width, y: ((1 - point.y) / 2) * this.heightPx };
  }

  get tilesInOrder(): readonly RackTileView[] {
    return this.tiles;
  }

  dispose(): void {
    this.scene.traverse(obj => {
      if (obj instanceof THREE.Mesh) {
        obj.geometry.dispose();
        const materials = Array.isArray(obj.material) ? obj.material : [obj.material];
        materials.forEach(m => {
          Object.values(m).forEach(v => v instanceof THREE.Texture && v.dispose());
          m.dispose();
        });
      }
    });
  }

  private refresh(): void {
    const views: TileView[] = [];
    this.tiles.forEach((tile, index) => {
      if (tile.id === this.heldId) {
        return;
      }

      const hover = tile.id === this.hoverId;
      views.push({
        row: 0,
        col: index,
        values: tile.values,
        lift: hover ? LIFT_HOVER : tile.marked ? LIFT_MARKED : 0,
        tint: tile.marked ? TINT_MARKED : undefined,
      });
    });

    this.layer.setTiles(views);
    // Centre the strip: it spans (n + 1) half cells wide and one cell row high.
    this.layer.group.position.set(-((this.tiles.length + 1) * SIDE) / 4, 0, -HEIGHT / 2);
  }

  /** A walnut tray with a felt inset, as wide as the strip of tiles. */
  private rebuildTray(): void {
    this.tray.children.forEach(child => (child as THREE.Mesh).geometry.dispose());
    this.tray.clear();
    this.tray.visible = this.tiles.length > 0;
    if (!this.tray.visible) {
      return;
    }

    const width = ((this.tiles.length + 1) * SIDE) / 2 + 1.5;
    const depth = HEIGHT + 1.15;

    const outer = new THREE.Shape();
    roundedRect(outer, width, depth, 0.7);
    const trayGeometry = new THREE.ExtrudeGeometry(outer, {
      depth: TRAY_DEPTH,
      bevelEnabled: true,
      bevelThickness: TRAY_BEVEL,
      bevelSize: 0.08,
      bevelSegments: 3,
      curveSegments: 10,
    });
    trayGeometry.rotateX(-Math.PI / 2);
    const tray = new THREE.Mesh(trayGeometry, this.trayMaterial);
    tray.position.y = -(TRAY_DEPTH + TRAY_BEVEL) - 0.09;
    this.tray.add(tray);

    const inner = new THREE.Shape();
    roundedRect(inner, width - 0.6, depth - 0.6, 0.45);
    const insetGeometry = new THREE.ShapeGeometry(inner, 10);
    insetGeometry.rotateX(-Math.PI / 2);
    const inset = new THREE.Mesh(insetGeometry, this.insetMaterial);
    inset.position.y = -0.085;
    this.tray.add(inset);
  }

  /** Places the camera so the whole tray fits the strip, as large as the strip height allows. */
  private fit(): void {
    if (this.width === 0 || this.heightPx === 0) {
      return;
    }

    const aspect = this.width / this.heightPx;
    const tan = Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2));
    const stripWidth = ((this.tiles.length + 1) * SIDE) / 2 + 2.6;
    const needWidth = Math.max(stripWidth, 9);
    const needHeight = (HEIGHT + 1.7 + LIFT_HOVER) * Math.cos(TILT) + 0.5;
    const distance = Math.max(needWidth / 2 / (tan * aspect), needHeight / 2 / tan);

    this.camera.aspect = aspect;
    this.camera.position.set(0, distance * Math.cos(TILT), distance * Math.sin(TILT));
    this.camera.lookAt(0, 0, 0);
    this.camera.updateProjectionMatrix();
    this.camera.updateMatrixWorld();
  }
}
