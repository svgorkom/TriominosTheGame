import * as THREE from 'three';
import type { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { cellAt } from './geometry';
import type { GlowCell, PlacementGlow } from './glow';
import type { Rack } from './rack';
import type { TileLayer } from './tiles';
import type { CellView, DotNetHost, GhostPose, PlacementView, RackTileView, SelectionView } from './types';

/** The ghost tile is never tinted: colour would tell the player whether the tile fits. */
const TINT_GHOST = 0xffffff;
const LIFT_FREE = 0.14;
const LIFT_OVER_TILE = 0.5;

/** A pointer that moves less than this many pixels, for less than CLICK_MS, is a click. */
const CLICK_DISTANCE = 8;
const CLICK_MS = 400;
const WHEEL_STEP = 50;

export interface InteractionHost {
  readonly canvas: HTMLCanvasElement;
  readonly camera: THREE.PerspectiveCamera;
  readonly controls: OrbitControls;
  readonly rack: Rack;
  readonly ghost: TileLayer;
  readonly glow: PlacementGlow;
  readonly dotnet: DotNetHost;
  isOccupied(row: number, col: number): boolean;
  requestFrame(): void;
  /** True while the scene plays an animation: tiles cannot be picked up then. */
  isBusy(): boolean;
}

interface Target {
  row: number;
  col: number;
  valid: boolean;
}

interface Held {
  tile: RackTileView;
  rotation: number;
  /** Cells that are open to a tile; they all light up, fitting or not. Null until C# has answered. */
  open: CellView[] | null;
  /** Cells where the tile fits, by "row,col"; null until C# has answered. Never shown to the player. */
  fits: Map<string, { row: number; col: number; rotations: number[] }> | null;
  loaded: Promise<void>;
  /** drag: carried while the button is down; sticky: after a click, the next click places it. */
  mode: 'drag' | 'sticky';
  startX: number;
  startY: number;
  startTime: number;
  target: Target | null;
}

/**
 * Picking up a tile from the rack and putting it on the table. All of it runs here: C# is asked
 * once when a tile is picked up (where does it fit?) and told once when it is dropped. Dragging
 * shows a ghost tile on the nearest cell. Like at a real table, nothing says where the tile fits:
 * the open cells next to the placed tiles all light up, and C# decides on the drop.
 */
export class Interaction {
  private held: Held | null = null;
  /** The ghost tile of a drop that C# is still handling, for the placement animation to start from. */
  private dropped: GhostPose | null = null;
  private pointer = { x: 0, y: 0 };
  private wheelDelta = 0;
  private readonly parent: HTMLElement;
  private readonly raycaster = new THREE.Raycaster();
  private readonly ground = new THREE.Plane(new THREE.Vector3(0, 1, 0), 0);
  private readonly ndc = new THREE.Vector2();
  private readonly point = new THREE.Vector3();

  constructor(private readonly host: InteractionHost) {
    this.parent = host.canvas.parentElement ?? host.canvas;
    // Capture on the parent so these run before the orbit controls see the same event.
    this.parent.addEventListener('pointerdown', this.onPointerDown, { capture: true });
    this.parent.addEventListener('wheel', this.onWheel, { capture: true, passive: false });
    window.addEventListener('pointermove', this.onPointerMove);
    window.addEventListener('pointerup', this.onPointerUp);
    window.addEventListener('pointercancel', this.onPointerCancel);
    window.addEventListener('keydown', this.onKeyDown);
  }

  get isHolding(): boolean {
    return this.held !== null;
  }

  dispose(): void {
    this.parent.removeEventListener('pointerdown', this.onPointerDown, { capture: true });
    this.parent.removeEventListener('wheel', this.onWheel, { capture: true });
    window.removeEventListener('pointermove', this.onPointerMove);
    window.removeEventListener('pointerup', this.onPointerUp);
    window.removeEventListener('pointercancel', this.onPointerCancel);
    window.removeEventListener('keydown', this.onKeyDown);
  }

  /** What is being held and where it fits, for the debug API; null when nothing is held or C# has not answered yet. */
  heldInfo(): { id: number; rotation: number; fits: PlacementView[] } | null {
    const held = this.held;
    if (!held?.fits) {
      return null;
    }

    const fits = [...held.fits.values()].flatMap(e => e.rotations.map(rotation => ({ row: e.row, col: e.col, rotation })));
    return { id: held.tile.id, rotation: held.rotation, fits };
  }

  /** The camera moved or the board changed: the cell under the pointer may be another one. */
  viewChanged(): void {
    if (this.held) {
      this.updateGhost();
    }
  }

  /** Drops whatever is being held without telling C#. */
  cancel(): void {
    if (!this.held) {
      return;
    }

    this.held = null;
    this.wheelDelta = 0;
    this.host.controls.enableZoom = true;
    this.host.canvas.style.cursor = '';
    this.host.rack.setHeld(null);
    this.clearOverlays();
  }

  /** Takes over the ghost tile of the drop in progress; the ghost disappears. */
  takeGhost(): GhostPose | null {
    const pose = this.dropped;
    this.dropped = null;
    if (pose) {
      this.host.ghost.setTiles([]);
      this.host.requestFrame();
    }

    return pose;
  }

  /** Removes the ghost and the glow, once the tile is on the board or back in the rack. */
  clearOverlays(): void {
    this.host.ghost.setTiles([]);
    this.host.glow.setCells([]);
    this.host.requestFrame();
  }

  private readonly onPointerDown = (e: PointerEvent): void => {
    if (e.button !== 0 || e.target !== this.host.canvas) {
      return;
    }

    this.pointer = { x: e.clientX, y: e.clientY };
    if (this.host.isBusy()) {
      // Nothing can be picked up while tiles move; the camera still works outside the rack.
      if (this.inRackStrip(e.clientX, e.clientY)) {
        swallow(e);
      }

      return;
    }

    if (this.held) {
      swallow(e);
      if (this.held.mode === 'sticky') {
        void this.tryDrop();
      }

      return;
    }

    const tile = this.rackTileAt(e.clientX, e.clientY);
    if (tile) {
      swallow(e);
      this.pick(tile, e);
    }
  };

  private readonly onPointerMove = (e: PointerEvent): void => {
    this.pointer = { x: e.clientX, y: e.clientY };
    if (this.held) {
      this.updateGhost();
      return;
    }

    if (e.target !== this.host.canvas) {
      return;
    }

    const tile = this.host.isBusy() ? null : this.rackTileAt(e.clientX, e.clientY);
    this.host.canvas.style.cursor = tile ? 'grab' : '';
    if (this.host.rack.setHover(tile?.id ?? null)) {
      this.host.requestFrame();
    }
  };

  private readonly onPointerUp = (e: PointerEvent): void => {
    const held = this.held;
    if (!held || e.button !== 0 || held.mode !== 'drag') {
      return;
    }

    this.pointer = { x: e.clientX, y: e.clientY };
    const moved = Math.hypot(e.clientX - held.startX, e.clientY - held.startY);
    if (moved < CLICK_DISTANCE && e.timeStamp - held.startTime < CLICK_MS) {
      held.mode = 'sticky';
      return;
    }

    void this.tryDrop();
  };

  private readonly onPointerCancel = (): void => this.cancel();

  private readonly onWheel = (e: WheelEvent): void => {
    if (!this.held) {
      return;
    }

    e.preventDefault();
    e.stopImmediatePropagation();
    this.wheelDelta += e.deltaMode === WheelEvent.DOM_DELTA_LINE ? e.deltaY * 33 : e.deltaY;
    if (Math.abs(this.wheelDelta) >= WHEEL_STEP) {
      this.rotate(this.wheelDelta > 0 ? 'clockwise' : 'counterclockwise');
      this.wheelDelta = 0;
    }
  };

  private readonly onKeyDown = (e: KeyboardEvent): void => {
    if (e.ctrlKey || e.metaKey || e.altKey || e.repeat) {
      return;
    }

    if (e.key === 'Escape') {
      this.cancel();
    } else if (this.held && (e.key === 'r' || e.key === 'R')) {
      e.preventDefault();
      this.rotate(e.shiftKey ? 'counterclockwise' : 'clockwise');
    } else if (e.code === 'Space' && !this.held && !this.host.isBusy() && !isControl(e.target)) {
      e.preventDefault();
      void this.host.dotnet.invokeMethodAsync('DrawRequested').catch(() => undefined);
    }
  };

  private pick(tile: RackTileView, e: PointerEvent): void {
    const held: Held = {
      tile,
      rotation: 0,
      open: null,
      fits: null,
      loaded: Promise.resolve(),
      mode: 'drag',
      startX: e.clientX,
      startY: e.clientY,
      // The event's own time, not the time its handler runs: a slow frame (a tablet, software rendering)
      // can hold the release back so long that a tap would otherwise look like a slow drag.
      startTime: e.timeStamp,
      target: null,
    };
    this.held = held;
    this.host.controls.enableZoom = false;
    this.host.canvas.style.cursor = 'grabbing';
    this.host.rack.setHover(null);
    this.host.rack.setHeld(tile.id);
    try {
      this.host.canvas.setPointerCapture(e.pointerId);
    } catch {
      // Synthetic or already released pointers cannot be captured; dragging still works.
    }

    held.loaded = this.host.dotnet
      .invokeMethodAsync<SelectionView>('TileSelected', tile.id)
      .then(({ open, fits: list }) => {
        if (this.held !== held) {
          return;
        }

        if (list.length === 0) {
          // Nowhere to put it (C# says why): the tile goes back.
          this.cancel();
          return;
        }

        held.open = open;
        held.fits = new Map();
        for (const p of list) {
          const key = `${p.row},${p.col}`;
          const entry = held.fits.get(key) ?? { row: p.row, col: p.col, rotations: [] };
          entry.rotations.push(p.rotation);
          held.fits.set(key, entry);
        }

        this.refreshGlow();
      })
      .catch(() => {
        if (this.held === held) {
          this.cancel();
        }
      });
    this.host.requestFrame();
  }

  private rotate(direction: 'clockwise' | 'counterclockwise'): void {
    const held = this.held;
    if (!held) {
      return;
    }

    // Rotation r shows corner value (k + r) % 3 at cell corner k, which turns the tile
    // counterclockwise; two steps are one step clockwise.
    held.rotation = (held.rotation + (direction === 'clockwise' ? 2 : 1)) % 3;
    this.refreshGlow();
  }

  /** Lights every open cell the same way, and moves the ghost tile to the new rotation. */
  private refreshGlow(): void {
    const held = this.held;
    if (!held?.open) {
      return;
    }

    const cells: GlowCell[] = held.open.map(({ row, col }) => ({ row, col }));
    this.host.glow.setCells(cells);
    this.updateGhost();
  }

  private updateGhost(): void {
    const held = this.held;
    if (!held) {
      return;
    }

    held.target = null;
    const point = this.groundPoint(this.pointer.x, this.pointer.y);
    if (!held.fits || !point || this.inRackStrip(this.pointer.x, this.pointer.y)) {
      this.host.ghost.setTiles([]);
      this.host.requestFrame();
      return;
    }

    const { row, col } = cellAt(point.x, point.z);
    const occupied = this.host.isOccupied(row, col);
    const entry = held.fits.get(`${row},${col}`);
    const valid = !occupied && !!entry && entry.rotations.includes(held.rotation);
    held.target = { row, col, valid };

    const v = held.tile.values;
    const values: [number, number, number] = [0, 1, 2].map(k => v[(k + held.rotation) % 3]) as [number, number, number];
    this.host.ghost.setTiles([{ row, col, values, lift: occupied ? LIFT_OVER_TILE : LIFT_FREE, tint: TINT_GHOST }]);
    this.host.requestFrame();
  }

  private async tryDrop(): Promise<void> {
    const held = this.held;
    if (!held) {
      return;
    }

    await held.loaded;
    if (this.held !== held) {
      return;
    }

    this.updateGhost();
    if (this.inRackStrip(this.pointer.x, this.pointer.y)) {
      this.cancel();
      return;
    }

    const target = held.target;
    if (!target?.valid) {
      if (held.mode === 'drag') {
        this.cancel();
      }

      return;
    }

    // The ghost and the hidden rack tile stay until C# has sent the new board and rack.
    const { row, col } = target;
    const { rotation } = held;
    this.dropped = { row, col, lift: LIFT_FREE, tint: TINT_GHOST };
    this.held = null;
    this.host.controls.enableZoom = true;
    this.host.canvas.style.cursor = '';
    this.host.glow.setCells([]);
    try {
      await this.host.dotnet.invokeMethodAsync('TileDropped', held.tile.id, row, col, rotation);
    } finally {
      this.dropped = null;
      this.host.rack.setHeld(null);
      this.clearOverlays();
    }
  }

  private rackTileAt(clientX: number, clientY: number): RackTileView | null {
    const rect = this.host.canvas.getBoundingClientRect();
    if (!this.inRackStrip(clientX, clientY)) {
      return null;
    }

    return this.host.rack.tileAt(clientX - rect.left, clientY - (rect.bottom - this.host.rack.heightPx));
  }

  private inRackStrip(clientX: number, clientY: number): boolean {
    const rect = this.host.canvas.getBoundingClientRect();
    return (
      this.host.rack.visible &&
      clientY >= rect.bottom - this.host.rack.heightPx &&
      clientY <= rect.bottom &&
      clientX >= rect.left &&
      clientX <= rect.right
    );
  }

  /** Where the pointer's ray meets the table plane, or null when it points at the sky. */
  private groundPoint(clientX: number, clientY: number): THREE.Vector3 | null {
    const rect = this.host.canvas.getBoundingClientRect();
    this.ndc.set(((clientX - rect.left) / rect.width) * 2 - 1, -(((clientY - rect.top) / rect.height) * 2 - 1));
    this.raycaster.setFromCamera(this.ndc, this.host.camera);
    return this.raycaster.ray.intersectPlane(this.ground, this.point);
  }
}

function swallow(e: Event): void {
  e.stopImmediatePropagation();
  e.preventDefault();
}

/** Space on a button or a select belongs to that control. */
function isControl(target: EventTarget | null): boolean {
  return target instanceof HTMLElement && ['BUTTON', 'SELECT', 'INPUT', 'TEXTAREA', 'A'].includes(target.tagName);
}
