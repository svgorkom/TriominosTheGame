import * as THREE from 'three';
import { HEIGHT, SIDE, TILE_SCALE, TILE_TOP } from './geometry';
import { roundedRect } from './table';
import type { Skin } from './textures';
import type { SceneTheme } from './theme';
import { TileLayer } from './tiles';
import type { RackTileView, TilePose } from './types';

const CAPACITY = 40;
/** How far the tiles lean back from upright. */
const LEAN = THREE.MathUtils.degToRad(20);
/** The camera looks down at the rack from this angle off vertical, about square to the tiles. */
const TILT = THREE.MathUtils.degToRad(66);
/** Distance between the centres of two neighbouring tiles. */
const PITCH = SIDE * TILE_SCALE + 0.14;
/** Slide a tile up the rack (along its face) when hovered or singled out. */
const LIFT_HOVER = 0.38;
const LIFT_MARKED = 0.2;
/** Tiles stand a little away from the back board when hovered, as if picked at. */
const PULL_HOVER = 0.08;
/** The stand: back board, lip in front of the tiles and the foot it all rests on. */
const BOARD = 0.2;
const LIP = 0.24;
const LIP_RISE = 0.16;
const FOOT = 0.16;
/** Where the bottom edge of a tile rests, in the tilted frame (the centroid is at z = 0). */
const TILE_BOTTOM = HEIGHT / 3;

export class Rack {
  readonly scene = new THREE.Scene();
  readonly camera = new THREE.PerspectiveCamera(26, 1, 0.5, 120);
  /** Height of the rack strip in CSS pixels. */
  heightPx = 0;

  private width = 0;
  private tiles: RackTileView[] = [];
  private heldId: number | null = null;
  private hoverId: number | null = null;
  /** Tiles that have a slot but are not shown yet (still on their way) or are gone. */
  private readonly hidden = new Set<number>();
  /** Everything that slides: the stand and its tiles. The lights stay put. */
  private readonly holder = new THREE.Group();
  /**
   * The frame of the back board: x along the rack, y out of the board towards the player, -z up
   * along the board. Tiles lie in it as on the table, so the TileLayer needs no changes.
   */
  private readonly tilted = new THREE.Group();
  /** 0 = in view, 1 = slid out of the strip. */
  private slideAmount = 0;
  private viewHeight = 0;
  private readonly layer = new TileLayer(CAPACITY, false);
  private readonly tray = new THREE.Group();
  private readonly trayMaterial = new THREE.MeshPhysicalMaterial({ roughness: 0.55, clearcoat: 0.6, clearcoatRoughness: 0.35 });
  private readonly key = new THREE.DirectionalLight(0xffffff, 2.6);
  private readonly fill = new THREE.HemisphereLight(0xffffff, 0x000000, 0.35);
  private markedTint = 0xffffff;
  private readonly raycaster = new THREE.Raycaster();
  private readonly plane = new THREE.Plane();
  private readonly hit3 = new THREE.Vector3();
  private readonly ndc = new THREE.Vector2();

  constructor() {
    this.key.position.set(-6, 14, 9);
    // Turning the table frame by 90 degrees minus the lean stands its up axis towards the player.
    this.tilted.rotation.x = Math.PI / 2 - LEAN;
    this.tilted.add(this.layer.group);
    this.holder.add(this.tray, this.tilted);
    this.scene.add(this.key, this.fill, this.holder);
  }

  /** Dresses the tray and its tiles in a theme. */
  applyTheme(theme: SceneTheme, skin: Skin): void {
    this.layer.applyTheme(theme, skin);
    this.trayMaterial.map = skin.wood;
    this.trayMaterial.needsUpdate = true;
    this.key.color.set(theme.sun);
    this.fill.color.set(theme.sky);
    this.fill.groundColor.set(theme.ground);
    this.markedTint = theme.marked;
    this.refresh();
  }

  /** True while the rack has tiles to show. */
  get visible(): boolean {
    return this.tiles.length > 0;
  }

  /** True while any of the rack is in view, so it needs drawing. */
  get drawn(): boolean {
    return this.visible && this.slideAmount < 1;
  }

  /** Slides the rack out of the strip (1) or back in (0). */
  setSlide(amount: number): void {
    this.slideAmount = amount;
    const a = Math.min(Math.max(amount, 0), 1);
    // Down the screen is toward the viewer and downward: the camera is tilted by TILT.
    const distance = a * this.viewHeight * 1.1;
    this.holder.position.set(0, -distance * Math.sin(TILT), distance * Math.cos(TILT));
  }

  setEnvironment(texture: THREE.Texture | null): void {
    this.scene.environment = texture;
    this.scene.environmentIntensity = 0.35;
  }

  /**
   * Replaces the tiles. Also puts back a tile that was being held. The slots of the hidden tiles
   * stay empty until reveal() shows them.
   */
  setTiles(tiles: readonly RackTileView[], hidden: readonly number[] = []): void {
    this.tiles = [...tiles];
    this.heldId = null;
    this.hoverId = null;
    this.hidden.clear();
    hidden.forEach(id => this.hidden.add(id));
    this.rebuildTray();
    this.refresh();
    this.fit();
  }

  /** Stops showing a tile for good (it was laid); the slot stays until the rack is replaced. */
  hide(id: number): void {
    this.hidden.add(id);
    this.refresh();
  }

  /** Shows a tile that was hidden, because it has arrived. */
  reveal(id: number): void {
    this.hidden.delete(id);
    this.refresh();
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
    this.holder.updateMatrixWorld();
    this.plane.set(new THREE.Vector3(0, 1, 0), -TILE_TOP).applyMatrix4(this.tilted.matrixWorld);
    if (!this.raycaster.ray.intersectPlane(this.plane, this.hit3)) {
      return null;
    }

    const local = this.tilted.worldToLocal(this.hit3);
    const slot = Math.round(local.x / PITCH + (this.tiles.length - 1) / 2);
    const tile = this.tiles[slot];
    if (!tile || Math.abs(local.x - this.slotX(slot)) > PITCH / 2) {
      return null;
    }

    // From the bottom edge up to the apex, plus the room a lifted tile takes.
    return local.z <= TILE_BOTTOM && local.z >= TILE_BOTTOM - HEIGHT - LIFT_HOVER ? tile : null;
  }

  /** Centre of a tile in strip pixels, for tests and the debug overlay. */
  tileCenter(id: number): { x: number; y: number } | null {
    const index = this.tiles.findIndex(t => t.id === id);
    if (index < 0) {
      return null;
    }

    this.holder.updateMatrixWorld();
    const point = this.tilted.localToWorld(new THREE.Vector3(this.slotX(index), TILE_TOP, 0)).project(this.camera);
    return { x: ((point.x + 1) / 2) * this.width, y: ((1 - point.y) / 2) * this.heightPx };
  }

  /** Screen pixels per world unit along a row of the strip, for sizing tiles that fly in. */
  unitPx(): number {
    this.holder.updateMatrixWorld();
    const a = this.tilted.localToWorld(new THREE.Vector3(0, TILE_TOP, 0)).project(this.camera);
    const b = this.tilted.localToWorld(new THREE.Vector3(1, TILE_TOP, 0)).project(this.camera);
    return (Math.abs(b.x - a.x) / 2) * this.width;
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

  /** x of the centre of a slot in the tilted frame; the row of slots is centred on 0. */
  private slotX(slot: number): number {
    return (slot - (this.tiles.length - 1) / 2) * PITCH;
  }

  private refresh(): void {
    const poses: TilePose[] = [];
    this.tiles.forEach((tile, index) => {
      if (tile.id === this.heldId || this.hidden.has(tile.id)) {
        return;
      }

      const hover = tile.id === this.hoverId;
      const lift = hover ? LIFT_HOVER : tile.marked ? LIFT_MARKED : 0;
      // Every tile points up (yaw 0); lifting slides it up along the board (-z).
      poses.push({
        x: this.slotX(index),
        y: hover ? PULL_HOVER : 0,
        z: -lift,
        yaw: 0,
        flip: 0,
        scale: 1,
        values: tile.values,
        tint: tile.marked ? this.markedTint : undefined,
      });
    });

    this.layer.setPoses(poses);
  }

  /**
   * The wooden stand, as wide as the row of tiles: a back board with a lip the tiles rest on (one
   * profile, extruded along the rack) on a flat foot.
   */
  private rebuildTray(): void {
    this.tray.children.forEach(child => (child as THREE.Mesh).geometry.dispose());
    this.tray.clear();
    this.tray.visible = this.tiles.length > 0;
    if (!this.tray.visible) {
      return;
    }

    const width = this.tiles.length * PITCH + 0.7;
    const boardTop = TILE_BOTTOM - HEIGHT * 0.78;
    const lipFront = TILE_BOTTOM + 0.04 + LIP;

    // Profile in the tilted frame, shape x = z (down the board), shape y = y (out of the board).
    const profile = new THREE.Shape();
    profile.moveTo(boardTop, -0.01);
    profile.lineTo(TILE_BOTTOM + 0.04, -0.01);
    profile.lineTo(TILE_BOTTOM + 0.04, TILE_TOP + LIP_RISE);
    profile.lineTo(lipFront, TILE_TOP + LIP_RISE);
    profile.lineTo(lipFront, -BOARD);
    profile.lineTo(boardTop, -BOARD);
    profile.closePath();
    const bevel = 0.035;
    const standGeometry = new THREE.ExtrudeGeometry(profile, {
      depth: width,
      bevelEnabled: true,
      bevelThickness: bevel,
      bevelSize: bevel,
      bevelSegments: 3,
      curveSegments: 1,
    });
    // Extrusion (shape z) becomes the length of the rack (x), shape x becomes z.
    standGeometry.rotateY(-Math.PI / 2);
    standGeometry.translate(width / 2, 0, 0);
    const stand = new THREE.Mesh(standGeometry, this.trayMaterial);
    stand.rotation.copy(this.tilted.rotation);
    this.tray.add(stand);

    // The foot runs under the stand from the front of the lip to behind the board.
    stand.updateMatrix();
    const corners = [
      new THREE.Vector3(0, -BOARD, lipFront),
      new THREE.Vector3(0, -BOARD, boardTop),
      new THREE.Vector3(0, TILE_TOP + LIP_RISE, lipFront),
    ].map(c => c.applyMatrix4(stand.matrix));
    const bottom = Math.min(...corners.map(c => c.y));
    const front = Math.max(...corners.map(c => c.z)) + 0.12;
    const back = Math.min(...corners.map(c => c.z)) - 0.35;
    const outer = new THREE.Shape();
    roundedRect(outer, width + 0.3, front - back, 0.18);
    const footGeometry = new THREE.ExtrudeGeometry(outer, {
      depth: FOOT,
      bevelEnabled: true,
      bevelThickness: bevel,
      bevelSize: bevel,
      bevelSegments: 3,
      curveSegments: 8,
    });
    footGeometry.rotateX(-Math.PI / 2);
    const foot = new THREE.Mesh(footGeometry, this.trayMaterial);
    foot.position.set(0, bottom - FOOT, (front + back) / 2);
    this.tray.add(foot);
  }

  /** Places the camera so the whole stand fits the strip, as large as the strip height allows. */
  private fit(): void {
    if (this.width === 0 || this.heightPx === 0) {
      return;
    }

    const aspect = this.width / this.heightPx;
    const tan = Math.tan(THREE.MathUtils.degToRad(this.camera.fov / 2));
    const needWidth = Math.max(this.tiles.length * PITCH + 1.6, 9);
    // The tiles and the board, seen almost square on, plus the foot and room for a lifted tile.
    const needHeight = HEIGHT + LIFT_HOVER + 1.6;
    const distance = Math.max(needWidth / 2 / (tan * aspect), needHeight / 2 / tan);

    // Aim at the middle of the tiles, a little above the centroid because of the lift.
    this.tilted.updateMatrix();
    // In the holder's frame, so a slide in progress does not move the camera.
    const target = new THREE.Vector3(0, 0, -LIFT_HOVER / 2).applyMatrix4(this.tilted.matrix);
    this.viewHeight = 2 * distance * tan;
    this.camera.aspect = aspect;
    this.camera.position.set(target.x, target.y + distance * Math.cos(TILT), target.z + distance * Math.sin(TILT));
    this.camera.lookAt(target);
    this.camera.updateProjectionMatrix();
    this.camera.updateMatrixWorld();
  }
}
