import * as THREE from 'three';
import { cellAt, cellCenter, cellYaw, TILE_TOP } from './geometry';
import type { GlowCell, PlacementGlow } from './glow';
import type { Rack } from './rack';
import type { CarriedShadow } from './shadow';
import type { TileLayer } from './tiles';
import type { CellView, DotNetHost, GhostPose, PlacementView, RackTileView, SelectionView } from './types';
import { tileVariation } from './variation';

/** The carried tile is never tinted: colour would tell the player whether the tile fits. */
const TINT_GHOST = 0xffffff;
/** Height of the carried tile: over open felt, and (LIFT_OVER_TILE) over tiles that lie there. */
const LIFT_CARRY = 0.45;
const LIFT_FREE = 0.14;
const LIFT_OVER_TILE = 0.5;

/** How quickly the carried tile catches up with the pointer, settles on a cell and turns (time constants in ms). */
const FOLLOW_MS = 30;
const SNAP_MS = 55;
const TURN_MS = 70;
const THIRD_TURN = (Math.PI * 2) / 3;

/** A pointer that moves less than this many pixels, for less than CLICK_MS, is a click. */
const CLICK_DISTANCE = 8;
const CLICK_MS = 400;
const WHEEL_STEP = 50;
/** A tile dragged with a finger is carried this many pixels above it, so the finger does not hide it. */
export const TOUCH_LIFT_PX = 70;
/** A tile carried after a tap waits this many pixels above the rack until the table is tapped. */
const PREVIEW_PX = 130;

export interface InteractionHost {
  readonly canvas: HTMLCanvasElement;
  readonly camera: THREE.PerspectiveCamera;
  readonly rack: Rack;
  readonly ghost: TileLayer;
  /** The shadow under the carried tile. */
  readonly shadow: CarriedShadow;
  readonly glow: PlacementGlow;
  readonly dotnet: DotNetHost;
  isOccupied(row: number, col: number): boolean;
  /** Shows the points of a placement over a cell, or removes them (null cell). */
  previewPoints(cell: CellView | null, points: number, bonus: boolean): void;
  requestFrame(): void;
  /** True while the scene plays an animation: tiles cannot be picked up then. */
  isBusy(): boolean;
}

interface Target {
  row: number;
  col: number;
  /** True when the tile settles on the cell: an open cell, or any empty cell when open cells are not shown. */
  open: boolean;
  valid: boolean;
}

/** Where the tile fits on one cell: the rotations that work, with their points. */
interface Fit {
  row: number;
  col: number;
  rotations: number[];
  points: Map<number, number>;
  /** True when some rotation earns a bridge or hexagon bonus. */
  bonus: boolean;
}

/** Brightness of the open cells where an assisted player's tile does not fit, and of the bonus cells. */
const GLOW_NO_FIT = 0.35;
const GLOW_BONUS = 1.8;
const SCALE_BONUS = 1.1;

/** Where the carried tile is, or where it is going. */
interface Carried {
  x: number;
  z: number;
  /** Height of the underside above the table. */
  lift: number;
  yaw: number;
}

interface Goal extends Carried {
  /** Height of what lies under the tile: the felt or the top of a tile. */
  surface: number;
  /** True when the tile settles on a cell, false when it follows the pointer. */
  snapped: boolean;
  values: [number, number, number];
  tint: number;
}

interface Held {
  tile: RackTileView;
  rotation: number;
  /** The way the tile is turned while it is carried; it turns to a cell when it comes over an open one. */
  yaw: number;
  /** Cells that are open to a tile, by "row,col"; they all light up, fitting or not. Null until C# has answered. */
  open: Map<string, CellView> | null;
  /** Cells where the tile fits, by "row,col"; null until C# has answered. Only shown to an assisted player. */
  fits: Map<string, Fit> | null;
  /** Whether the open cells light up; see SelectionView. */
  showOpen: boolean;
  /** Whether the game helps the player; see SelectionView. */
  assisted: boolean;
  /** The cell the tile last turned to by itself, so it does it once per cell. */
  autoTurned: string | null;
  loaded: Promise<void>;
  /** drag: carried while the button is down; sticky: after a click, the next click places it. */
  mode: 'drag' | 'sticky';
  /** The pointer that picked the tile up; while dragging, other pointers are ignored. */
  pointerId: number;
  /** Picked up with a finger: a drag carries it above the finger, a tap on the table aims instead of following moves. */
  touch: boolean;
  /** Where a finger last tapped the table (world x, z); the carried tile waits there. Null before the first tap. */
  anchor: { x: number; z: number } | null;
  startX: number;
  startY: number;
  startTime: number;
  target: Target | null;
}

/**
 * Picking up a tile from the rack and putting it on the table. All of it runs here: C# is asked
 * once when a tile is picked up (where does it fit?) and told once when it is dropped. The tile
 * follows the pointer above the table with its shadow under it, and settles on a cell where it
 * is open to a tile. Like at a real table, nothing says where the tile fits: every open cell next
 * to the placed tiles lights up the same way, the tile is never tinted, and C# decides on the drop.
 * A drop on a cell where the tile does not fit is reported too, so C# can say why (and charge the
 * mistake penalty of the hardest level). The difficulty changes two things: at the hardest level no
 * cell lights up and the tile settles on every empty cell; at the easiest level the game helps
 * (cells where the tile fits light up brighter, points show, the tile turns by itself).
 */
export class Interaction {
  private held: Held | null = null;
  /** The ghost tile of a drop that C# is still handling, for the placement animation to start from. */
  private dropped: GhostPose | null = null;
  /** Where the carried tile is going and where it is drawn now; both null while it is not shown. */
  private goal: Goal | null = null;
  private shown: Carried | null = null;
  private lastStep = 0;
  private pointer = { x: 0, y: 0 };
  /** A finger on the table while a tile is carried after a tap: a tap when it lifts, a camera move if it moved or a second finger came. */
  private tap: { id: number; x: number; y: number; spoiled: boolean } | null = null;
  /** Pointers that are down on the canvas, to tell a tap from a pinch. */
  private readonly down = new Set<number>();
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

  /** Turns the carried tile a step clockwise, for the rotate button; nothing happens when nothing is held. */
  rotateHeld(): void {
    this.rotate('clockwise');
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

    const fits = [...held.fits.values()].flatMap(e => e.rotations.map(rotation => ({ row: e.row, col: e.col, rotation, points: e.points.get(rotation) ?? 0, bonus: e.bonus })));
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

    this.setHeld(null);
    this.wheelDelta = 0;
    this.host.canvas.style.cursor = '';
    this.host.rack.setHeld(null);
    this.clearOverlays();
  }

  /** Takes over the ghost tile of the drop in progress, at the height it has reached; the ghost disappears. */
  takeGhost(): GhostPose | null {
    const dropped = this.dropped;
    this.dropped = null;
    if (!dropped) {
      return null;
    }

    const pose = { ...dropped, lift: this.shown?.lift ?? dropped.lift };
    this.hideGhost();
    return pose;
  }

  /** Removes the ghost and the glow, once the tile is on the board or back in the rack. */
  clearOverlays(): void {
    this.host.previewPoints(null, 0, false);
    this.host.glow.setCells([]);
    this.hideGhost();
  }

  /**
   * Moves the carried tile a step towards its goal; the scene calls this every frame it draws.
   * Returns whether the tile is still on its way, so the scene keeps drawing.
   */
  step(now: number): boolean {
    const goal = this.goal;
    const elapsed = Math.min(now - this.lastStep, 50);
    this.lastStep = now;
    if (!goal) {
      return false;
    }

    let moving = false;
    if (!this.shown) {
      this.shown = { x: goal.x, z: goal.z, lift: goal.lift, yaw: goal.yaw };
    } else {
      const shown = this.shown;
      const near = 1 - Math.exp(-elapsed / (goal.snapped ? SNAP_MS : FOLLOW_MS));
      const turn = 1 - Math.exp(-elapsed / TURN_MS);
      const dx = goal.x - shown.x;
      const dz = goal.z - shown.z;
      const dLift = goal.lift - shown.lift;
      // The short way round.
      const dYaw = Math.atan2(Math.sin(goal.yaw - shown.yaw), Math.cos(goal.yaw - shown.yaw));
      moving = Math.abs(dx) + Math.abs(dz) + Math.abs(dLift) > 0.002 || Math.abs(dYaw) > 0.004;
      if (moving) {
        shown.x += dx * near;
        shown.z += dz * near;
        shown.lift += dLift * near;
        shown.yaw += dYaw * turn;
      } else {
        this.shown = { x: goal.x, z: goal.z, lift: goal.lift, yaw: goal.yaw };
      }
    }

    const { x, z, lift, yaw } = this.shown;
    this.host.ghost.setPoses([{ x, y: lift, z, yaw, flip: 0, scale: 1, values: goal.values, tint: goal.tint }]);
    this.host.shadow.show(x, z, yaw, goal.surface, lift - goal.surface);
    return moving;
  }

  private hideGhost(): void {
    this.goal = null;
    this.shown = null;
    this.host.ghost.setPoses([]);
    this.host.shadow.hide();
    this.host.requestFrame();
  }

  private readonly onPointerDown = (e: PointerEvent): void => {
    if (e.target !== this.host.canvas) {
      return;
    }

    this.down.add(e.pointerId);
    if (this.tap && this.tap.id !== e.pointerId) {
      // A second finger: a pinch or a twist of the camera, not a tap.
      this.tap.spoiled = true;
    }

    if (e.button !== 0) {
      return;
    }

    if (this.held?.touch && e.pointerType === 'touch') {
      this.touchWhileHolding(e);
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
    const tap = this.tap;
    if (tap?.id === e.pointerId && Math.hypot(e.clientX - tap.x, e.clientY - tap.y) >= CLICK_DISTANCE) {
      // The finger moves the camera; the carried tile stays where it waits.
      tap.spoiled = true;
    }

    if (this.held?.touch && (this.held.mode === 'sticky' || e.pointerId !== this.held.pointerId)) {
      return;
    }

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
    this.down.delete(e.pointerId);
    const tap = this.tap;
    if (tap?.id === e.pointerId) {
      this.tap = null;
      if (!tap.spoiled && this.held?.mode === 'sticky') {
        this.pointer = { x: tap.x, y: tap.y };
        void this.tryDrop();
      }

      return;
    }

    const held = this.held;
    if (!held || e.button !== 0 || held.mode !== 'drag' || e.pointerId !== held.pointerId) {
      return;
    }

    this.pointer = { x: e.clientX, y: e.clientY };
    const moved = Math.hypot(e.clientX - held.startX, e.clientY - held.startY);
    if (moved < CLICK_DISTANCE && e.timeStamp - held.startTime < CLICK_MS) {
      held.mode = 'sticky';
      // A tile carried after a tap with a finger now waits in the hand, above the rack.
      this.updateGhost();
      return;
    }

    void this.tryDrop();
  };

  private readonly onPointerCancel = (e: PointerEvent): void => {
    this.down.delete(e.pointerId);
    if (this.tap?.id === e.pointerId) {
      this.tap = null;
    } else if (this.held?.mode === 'drag' && e.pointerId === this.held.pointerId) {
      this.cancel();
    }
  };

  /**
   * A finger touches down while a tile picked up by a finger is carried. During a drag, other
   * fingers do nothing. After a tap, a finger on the rack puts the tile back when it lifts; on the
   * table it is left to the camera (pan, pinch, twist) and only counts as a tap, which aims or
   * places the tile, if it lifts without moving while no other finger came.
   */
  private touchWhileHolding(e: PointerEvent): void {
    const held = this.held!;
    if (held.mode === 'drag' || this.host.isBusy()) {
      swallow(e);
      return;
    }

    if (this.inRackStrip(e.clientX, e.clientY)) {
      swallow(e);
    }

    this.tap = { id: e.pointerId, x: e.clientX, y: e.clientY, spoiled: this.down.size > 1 };
  }

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
      // Tiles stand in the rack pointing up.
      yaw: 0,
      open: null,
      fits: null,
      showOpen: true,
      assisted: false,
      autoTurned: null,
      loaded: Promise.resolve(),
      mode: 'drag',
      pointerId: e.pointerId,
      touch: e.pointerType === 'touch',
      anchor: null,
      startX: e.clientX,
      startY: e.clientY,
      // The event's own time, not the time its handler runs: a slow frame (a tablet, software rendering)
      // can hold the release back so long that a tap would otherwise look like a slow drag.
      startTime: e.timeStamp,
      target: null,
    };
    this.setHeld(held);
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
      .then(({ open, fits: list, showOpen, assisted }) => {
        if (this.held !== held) {
          return;
        }

        held.open = new Map(open.map(c => [`${c.row},${c.col}`, c]));
        held.showOpen = showOpen;
        held.assisted = assisted;
        held.fits = new Map();
        for (const p of list) {
          const key = `${p.row},${p.col}`;
          const entry = held.fits.get(key) ?? { row: p.row, col: p.col, rotations: [], points: new Map<number, number>(), bonus: false } satisfies Fit;
          entry.rotations.push(p.rotation);
          entry.points.set(p.rotation, p.points);
          entry.bonus ||= p.bonus;
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

    // An assisted player turns only between the rotations that fit the cell under the tile.
    const target = held.target;
    const fit = held.assisted && target ? held.fits?.get(`${target.row},${target.col}`) : undefined;
    const steps = fit && fit.rotations.length > 0 ? 3 : 1;
    for (let i = 0; i < steps; i++) {
      this.turnOnce(held, direction);
      if (!fit || fit.rotations.includes(held.rotation)) {
        break;
      }
    }

    this.refreshGlow();
  }

  private turnOnce(held: Held, direction: 'clockwise' | 'counterclockwise'): void {
    // Rotation r shows corner value (k + r) % 3 at cell corner k, which turns the tile
    // counterclockwise; two steps are one step clockwise.
    held.rotation = (held.rotation + (direction === 'clockwise' ? 2 : 1)) % 3;
    if (this.shown) {
      // The values have jumped a corner on; drawn a third of a turn back, the tile looks as it
      // did, and from there it turns to where it is going.
      this.shown.yaw += direction === 'clockwise' ? THIRD_TURN : -THIRD_TURN;
    }
  }

  /**
   * Lights every open cell the same way (an assisted player sees the cells where the tile fits
   * brighter, bonus cells brightest), and moves the carried tile to the new rotation.
   */
  private refreshGlow(): void {
    const held = this.held;
    if (!held?.open) {
      return;
    }

    const cells: GlowCell[] = !held.showOpen
      ? []
      : [...held.open.values()].map(({ row, col }) => {
          if (!held.assisted) {
            return { row, col };
          }

          const fit = held.fits?.get(`${row},${col}`);
          if (!fit) {
            return { row, col, strength: GLOW_NO_FIT };
          }

          return fit.bonus ? { row, col, strength: GLOW_BONUS, scale: SCALE_BONUS } : { row, col };
        });
    this.host.glow.setCells(cells);
    this.updateGhost();
  }

  private updateGhost(): void {
    const held = this.held;
    if (!held) {
      return;
    }

    held.target = null;
    const aim = this.aim(held);
    if (!held.open || !held.fits || !aim) {
      this.host.previewPoints(null, 0, false);
      this.hideGhost();
      return;
    }

    const { point, waiting } = aim;
    const v = held.tile.values;
    const values: [number, number, number] = [0, 1, 2].map(k => v[(k + held.rotation) % 3]) as [number, number, number];
    if (waiting) {
      // Not aimed yet: the tile floats in the hand above the rack, where turning it can be seen.
      this.host.previewPoints(null, 0, false);
      this.goal = { x: point.x, z: point.z, lift: LIFT_CARRY, yaw: held.yaw, surface: 0, snapped: false, values, tint: TINT_GHOST };
      this.host.requestFrame();
      return;
    }

    const { row, col } = cellAt(point.x, point.z);
    const key = `${row},${col}`;
    const occupied = this.host.isOccupied(row, col);
    const entry = held.fits.get(key);
    const open = (held.showOpen ? held.open.has(key) : true) && !occupied;
    if (held.assisted && open && entry && held.autoTurned !== key && !entry.rotations.includes(held.rotation)) {
      // Turned once on arrival; after that the player turns it as they like.
      held.autoTurned = key;
      held.rotation = entry.rotations[0];
    }

    const valid = open && !!entry && entry.rotations.includes(held.rotation);
    held.target = { row, col, open, valid };
    if (held.assisted && valid && entry) {
      this.host.previewPoints({ row, col }, entry.points.get(held.rotation) ?? 0, entry.bonus);
    } else {
      this.host.previewPoints(null, 0, false);
    }

    if (open) {
      held.yaw = cellYaw(row, col);
    }

    if (open) {
      // It settles on every open cell the way it would lie on the table, fitting or not, so the
      // settling gives nothing away and nothing jumps when it is let go.
      const center = cellCenter(row, col);
      const lie = tileVariation(values);
      this.goal = { x: center.x + lie.dx, z: center.y + lie.dz, lift: LIFT_FREE, yaw: held.yaw + lie.yaw, surface: 0, snapped: true, values, tint: TINT_GHOST };
    } else {
      const lift = occupied ? LIFT_OVER_TILE : LIFT_CARRY;
      this.goal = { x: point.x, z: point.z, lift, yaw: held.yaw, surface: occupied ? TILE_TOP : 0, snapped: false, values, tint: TINT_GHOST };
    }

    this.host.requestFrame();
  }

  /**
   * The point on the table the carried tile is aimed at; null when there is none (over the rack,
   * or at the sky). waiting: a tile carried after a tap that has not been aimed yet.
   */
  private aim(held: Held): { point: { x: number; z: number }; waiting: boolean } | null {
    if (held.touch && held.mode === 'sticky') {
      if (held.anchor) {
        return { point: held.anchor, waiting: false };
      }

      const rect = this.host.canvas.getBoundingClientRect();
      const center = this.host.rack.tileCenter(held.tile.id);
      const x = rect.left + (center?.x ?? rect.width / 2);
      const point = this.groundPoint(x, rect.bottom - this.host.rack.heightPx - PREVIEW_PX);
      return point ? { point: { x: point.x, z: point.z }, waiting: true } : null;
    }

    if (this.inRackStrip(this.pointer.x, this.pointer.y)) {
      return null;
    }

    const lift = held.touch ? TOUCH_LIFT_PX : 0;
    const point = this.groundPoint(this.pointer.x, this.pointer.y - lift);
    return point ? { point: { x: point.x, z: point.z }, waiting: false } : null;
  }

  /** Sets what is held and tells C# when the hand empties or fills, for the rotate button. */
  private setHeld(held: Held | null): void {
    const was = this.held !== null;
    this.held = held;
    if (was !== (held !== null)) {
      void this.host.dotnet.invokeMethodAsync('HoldChanged', held !== null).catch(() => undefined);
    }
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

    if (this.inRackStrip(this.pointer.x, this.pointer.y)) {
      this.cancel();
      return;
    }

    if (held.touch && held.mode === 'sticky') {
      // A tap aims the tile: it goes to the tapped cell and, if it fits there, is laid.
      const point = this.groundPoint(this.pointer.x, this.pointer.y);
      held.anchor = point ? { x: point.x, z: point.z } : held.anchor;
    }

    this.updateGhost();

    const target = held.target;
    if (!target?.open) {
      if (held.mode === 'drag') {
        this.cancel();
      }

      return;
    }

    // The ghost and the hidden rack tile stay until C# has sent the new board and rack. A tile
    // that does not fit goes to C# too, which refuses it, says why and sends the rack back.
    const { row, col } = target;
    const { rotation } = held;
    this.dropped = target.valid ? { row, col, lift: LIFT_FREE, tint: TINT_GHOST } : null;
    this.setHeld(null);
    this.host.canvas.style.cursor = '';
    this.host.glow.setCells([]);
    this.host.previewPoints(null, 0, false);
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
