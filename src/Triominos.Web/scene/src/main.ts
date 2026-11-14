import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { Animator } from './animator';
import { Director, type DirectorHost } from './director';
import { Floaters, GoldBurst, type ScreenPoint } from './effects';
import { FlightLayer } from './flight';
import { cellCenter, cellCorners, SIDE } from './geometry';
import { PlacementGlow } from './glow';
import { Interaction } from './input';
import { Rack } from './rack';
import { CarriedShadow } from './shadow';
import { Sound } from './sound';
import { createTable, FELT_DEPTH, FELT_WIDTH } from './table';
import { loadFonts } from './textures';
import { durations, easing } from './timing';
import { BOARD_CAPACITY, TileLayer } from './tiles';
import { qualitySettings, type CellView, type DealView, type DotNetHost, type MoveView, type Quality, type RackTileView, type TileView } from './types';

export type { DealView, DotNetHost, MoveEvent, MoveView, PlacementView, Quality, RackTileView, ScoreLine, SelectionView, TileView } from './types';
export { setAnimationSpeed } from './timing';

/**
 * The 3D table. Blazor creates it with init() and updates it a few times per move; everything
 * that happens per frame (camera, rendering) stays in here. Frames are only drawn while something
 * changes, so an idle table costs no GPU time.
 */
class TableScene {
  private readonly renderer: THREE.WebGLRenderer;
  private readonly scene = new THREE.Scene();
  private readonly camera = new THREE.PerspectiveCamera(36, 1, 0.5, 200);
  private readonly controls: OrbitControls;
  private readonly sun = new THREE.DirectionalLight(0xffe9c7, 3.2);
  private readonly tiles = new TileLayer(BOARD_CAPACITY);
  private readonly ghost = new TileLayer(1, false);
  private readonly glow = new PlacementGlow();
  /** The sun stands at this offset from the point it looks at. */
  private readonly sunOffset = new THREE.Vector3(-9, 22, 12);
  private readonly carriedShadow = new CarriedShadow(new THREE.Vector2(-this.sunOffset.x / this.sunOffset.y, -this.sunOffset.z / this.sunOffset.y));
  /** The outline that flashes around a tile that earned a bridge, or around the cells of a hexagon (13 at most). */
  private readonly flash = new PlacementGlow(16);
  /** The one cell a paid hint points at; it stays lit until the player moves. */
  private readonly hint = new PlacementGlow(1);
  private readonly burst = new GoldBurst();
  /** The tile that is being placed while it falls and bounces; it casts the shadow where it lands. */
  private readonly mover = new TileLayer(4);
  private readonly flights = new FlightLayer();
  private readonly rack = new Rack();
  private readonly sound = new Sound(entry => this.log(entry));
  private readonly animator = new Animator(() => this.requestFrame());
  private readonly floaters: Floaters;
  private readonly director: Director;
  private readonly interaction: Interaction;
  private readonly occupied = new Set<string>();
  private environment: THREE.Texture | null = null;
  private readonly resizeObserver: ResizeObserver;
  private frameRequested = false;
  private disposed = false;
  /** How often the player has taken hold of the camera; a camera move that follows a tile gives way when it changes. */
  private cameraGrabs = 0;
  private quality: Quality;

  /** Duration of the most recent frames in ms (CPU side), for the performance overlay. */
  readonly frameTimes: number[] = [];
  framesDrawn = 0;
  /** What the animations did, for automated tests: a name and the time in ms since the scene started. */
  readonly eventLog: { name: string; at: number }[] = [];
  private readonly startedAt = performance.now();

  /** Draw calls of the most recent frame. */
  get drawCalls(): number {
    return this.renderer.info.render.calls;
  }

  constructor(private readonly canvas: HTMLCanvasElement, quality: Quality, dotnet: DotNetHost) {
    this.quality = quality;
    this.renderer = new THREE.WebGLRenderer({ canvas, antialias: true, alpha: true, powerPreference: 'high-performance' });
    // The table and the rack are drawn in two passes, so the canvas is cleared by hand.
    this.renderer.autoClear = false;
    // Two render() calls make a frame; count the draw calls of both.
    this.renderer.info.autoReset = false;
    this.renderer.outputColorSpace = THREE.SRGBColorSpace;
    this.renderer.toneMapping = THREE.ACESFilmicToneMapping;
    this.renderer.toneMappingExposure = 1.05;
    this.renderer.shadowMap.type = THREE.PCFShadowMap;
    this.renderer.shadowMap.autoUpdate = false;

    const pmrem = new THREE.PMREMGenerator(this.renderer);
    this.environment = pmrem.fromScene(new RoomEnvironment(), 0.04).texture;
    this.scene.environment = this.environment;
    this.scene.environmentIntensity = 0.35;
    this.rack.setEnvironment(this.environment);
    this.flights.setEnvironment(this.environment);
    pmrem.dispose();

    this.scene.add(new THREE.HemisphereLight(0xfff2dc, 0x1c120a, 0.18));
    this.sun.position.copy(this.sunOffset);
    this.sun.castShadow = true;
    this.sun.shadow.bias = -0.0004;
    this.sun.shadow.normalBias = 0.02;
    this.sun.shadow.radius = 4;
    this.scene.add(this.sun, this.sun.target);

    this.scene.add(createTable(), this.tiles.group, this.mover.group, this.carriedShadow.mesh, this.glow.mesh, this.flash.mesh, this.hint.mesh, this.ghost.group, this.burst.points);

    this.camera.position.set(0, 11.5, 14);
    this.controls = new OrbitControls(this.camera, canvas);
    this.configureControls();

    this.interaction = new Interaction({
      canvas,
      camera: this.camera,
      controls: this.controls,
      rack: this.rack,
      ghost: this.ghost,
      shadow: this.carriedShadow,
      glow: this.glow,
      dotnet,
      isOccupied: (row, col) => this.occupied.has(`${row},${col}`),
      requestFrame: () => this.requestFrame(),
      isBusy: () => this.director.busy || this.animator.busy,
    });

    this.floaters = new Floaters(floatersRoot(canvas));
    this.director = new Director(this.directorHost(dotnet));

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(canvas);

    this.applyQuality();
    this.resize();
    this.warmUp();
  }

  /** Compiles the shaders that the first animation would otherwise compile in the middle of a move. */
  private warmUp(): void {
    this.flights.warmUp(this.renderer, this.camera);
    this.burst.points.visible = true;
    this.renderer.compile(this.scene, this.camera);
    this.burst.points.visible = false;
  }

  /** Shows a board at once, after the animation in progress (if any) has jumped to its end. */
  showBoard(tiles: readonly TileView[], focus: boolean): void {
    this.director.whenIdle(() => {
      this.setTiles(tiles);
      if (focus) {
        this.focusTiles(tiles);
      }
    });
  }

  playMove(move: MoveView): void {
    void this.director.playMove(move);
  }

  deal(view: DealView): void {
    void this.director.deal(view);
  }

  get busy(): boolean {
    return this.director.busy || this.animator.busy;
  }

  setTiles(tiles: readonly TileView[]): void {
    this.tiles.setTiles(tiles, true);
    this.occupied.clear();
    tiles.forEach(t => this.occupied.add(`${t.row},${t.col}`));
    this.fitShadowToTiles(tiles);
    this.renderer.shadowMap.needsUpdate = true;
    this.interaction.viewChanged();
    this.requestFrame();
  }

  /** Lights the cell a hint points at; null puts the light out. */
  setHint(cell: CellView | null): void {
    this.hint.setCells(cell ? [cell] : []);
    this.requestFrame();
  }

  /** Shows the rack of the player whose turn it is; an empty rack hides it. A rack that appears slides in. */
  setRack(tiles: readonly RackTileView[]): void {
    this.director.whenIdle(() => {
      const wasShown = this.rack.visible;
      this.interaction.cancel();
      this.rack.setTiles(tiles);
      if (!wasShown && this.rack.visible) {
        this.rack.setSlide(1);
        void this.animator.animate(durations.rackSlide, t => this.rack.setSlide(1 - t), { ease: easing.inOutCubic });
      } else {
        this.rack.setSlide(0);
      }

      this.requestFrame();
    });
  }

  get interactionView(): Interaction {
    return this.interaction;
  }

  get rackView(): Rack {
    return this.rack;
  }

  get mainCamera(): THREE.PerspectiveCamera {
    return this.camera;
  }

  get canvasElement(): HTMLCanvasElement {
    return this.canvas;
  }

  /** Points the camera at the middle of the tiles. */
  focusTiles(tiles: readonly TileView[]): void {
    if (tiles.length === 0) {
      this.controls.target.set(0, 0, 0);
    } else {
      const sum = new THREE.Vector2();
      tiles.forEach(t => sum.add(cellCenter(t.row, t.col)));
      sum.divideScalar(tiles.length);
      const offset = this.camera.position.clone().sub(this.controls.target);
      this.controls.target.set(sum.x, 0, sum.y);
      this.camera.position.copy(this.controls.target).add(offset);
    }

    this.controls.update();
    this.requestFrame();
  }

  setQuality(quality: Quality): void {
    this.quality = quality;
    this.applyQuality();
    this.resize();
  }

  setSound(enabled: boolean, volume: number, preview: boolean): void {
    this.sound.configure(enabled, volume, preview);
  }

  /** State of the audio context, for automated tests. */
  get audioState(): string {
    return this.sound.state;
  }

  dispose(): void {
    this.disposed = true;
    this.director.dispose();
    this.sound.dispose();
    this.floaters.dispose();
    this.resizeObserver.disconnect();
    this.interaction.dispose();
    this.controls.dispose();
    this.rack.dispose();
    [this.scene, this.flights.scene].forEach(root =>
      root.traverse(obj => {
        if (obj instanceof THREE.Mesh || obj instanceof THREE.Points) {
          obj.geometry.dispose();
          const materials = Array.isArray(obj.material) ? obj.material : [obj.material];
          materials.forEach(m => {
            Object.values(m).forEach(v => v instanceof THREE.Texture && v.dispose());
            m.dispose();
          });
        }
      }),
    );
    this.environment?.dispose();
    this.renderer.dispose();
  }

  private configureControls(): void {
    const c = this.controls;
    c.enableDamping = true;
    c.dampingFactor = 0.09;
    c.screenSpacePanning = false;
    c.minDistance = 6;
    c.maxDistance = 34;
    c.minPolarAngle = 0.12;
    c.maxPolarAngle = 1.15;
    c.zoomSpeed = 0.9;
    // Left button pans (and will drag tiles in phase 2), right button orbits, wheel zooms.
    c.mouseButtons = { LEFT: THREE.MOUSE.PAN, MIDDLE: THREE.MOUSE.DOLLY, RIGHT: THREE.MOUSE.ROTATE };
    c.touches = { ONE: THREE.TOUCH.PAN, TWO: THREE.TOUCH.DOLLY_ROTATE };
    c.addEventListener('start', () => this.cameraGrabs++);
    c.addEventListener('change', () => {
      this.clampTarget();
      this.interaction.viewChanged();
      this.requestFrame();
    });
  }

  /** Keeps the camera above the felt when panning. */
  private clampTarget(): void {
    const target = this.controls.target;
    const limitX = FELT_WIDTH / 2 - SIDE;
    const limitZ = FELT_DEPTH / 2 - SIDE;
    const clampedX = THREE.MathUtils.clamp(target.x, -limitX, limitX);
    const clampedZ = THREE.MathUtils.clamp(target.z, -limitZ, limitZ);
    if (clampedX !== target.x || clampedZ !== target.z) {
      this.camera.position.x += clampedX - target.x;
      this.camera.position.z += clampedZ - target.z;
      target.set(clampedX, 0, clampedZ);
    }
  }

  /** A tight shadow camera around the tiles gives sharper shadows for the same map size. */
  private fitShadowToTiles(tiles: readonly TileView[]): void {
    const box = new THREE.Box2(new THREE.Vector2(-4, -4), new THREE.Vector2(4, 4));
    tiles.forEach(t => box.expandByPoint(cellCenter(t.row, t.col)));
    box.expandByScalar(SIDE * 1.5);
    const center = box.getCenter(new THREE.Vector2());
    const half = Math.max(box.max.x - box.min.x, box.max.y - box.min.y) / 2;

    this.sun.target.position.set(center.x, 0, center.y);
    this.sun.position.set(center.x + this.sunOffset.x, this.sunOffset.y, center.y + this.sunOffset.z);
    const shadowCamera = this.sun.shadow.camera;
    shadowCamera.left = -half;
    shadowCamera.right = half;
    shadowCamera.top = half;
    shadowCamera.bottom = -half;
    shadowCamera.near = 5;
    shadowCamera.far = 60;
    shadowCamera.updateProjectionMatrix();
    this.sun.target.updateMatrixWorld();
  }

  private applyQuality(): void {
    const settings = qualitySettings[this.quality];
    this.renderer.setPixelRatio(Math.min(window.devicePixelRatio, settings.pixelRatioCap));
    this.renderer.shadowMap.enabled = settings.shadows;
    this.sun.castShadow = settings.shadows;
    if (this.sun.shadow.mapSize.x !== settings.shadowMapSize) {
      this.sun.shadow.mapSize.set(settings.shadowMapSize, settings.shadowMapSize);
      this.sun.shadow.map?.dispose();
      this.sun.shadow.map = null;
    }

    this.renderer.shadowMap.needsUpdate = true;
    this.scene.traverse(obj => {
      if (obj instanceof THREE.Mesh) {
        const materials = Array.isArray(obj.material) ? obj.material : [obj.material];
        materials.forEach(m => (m.needsUpdate = true));
      }
    });
    this.requestFrame();
  }

  private resize(): void {
    const width = this.canvas.clientWidth;
    const height = this.canvas.clientHeight;
    if (width === 0 || height === 0) {
      return;
    }

    this.renderer.setSize(width, height, false);
    const rackHeight = this.rack.resize(width, height);
    // Other parts of the page (the buttons) stay above the rack.
    (this.canvas.parentElement ?? this.canvas).style.setProperty('--rack-h', `${rackHeight}px`);
    this.camera.aspect = width / height;
    // Shift the view up so the middle of the table is the middle of the space above the rack.
    this.camera.setViewOffset(width, height, 0, rackHeight / 2, width, height);
    this.camera.updateProjectionMatrix();
    this.requestFrame();
  }

  /** Draws one frame at the next animation frame; repeated requests collapse into one. */
  requestFrame(): void {
    if (this.frameRequested || this.disposed) {
      return;
    }

    this.frameRequested = true;
    requestAnimationFrame(() => this.frame());
  }

  private frame(): void {
    this.frameRequested = false;
    if (this.disposed) {
      return;
    }

    const start = performance.now();
    const animating = this.animator.step(start);
    const carrying = this.interaction.step(start);
    // While damping, the controls keep moving after the input stops.
    const moving = this.controls.update();
    this.drawFrame();
    this.framesDrawn++;
    this.frameTimes.push(performance.now() - start);
    if (this.frameTimes.length > 120) {
      this.frameTimes.shift();
    }

    if (moving || animating || carrying) {
      this.requestFrame();
    }
  }

  private drawFrame(): void {
    const renderer = this.renderer;
    renderer.info.reset();
    renderer.setScissorTest(false);
    renderer.setViewport(0, 0, this.canvas.clientWidth, this.canvas.clientHeight);
    renderer.clear();
    renderer.render(this.scene, this.camera);

    if (this.rack.drawn) {
      const width = this.canvas.clientWidth;
      const height = this.rack.heightPx;
      renderer.clearDepth();
      renderer.setScissorTest(true);
      renderer.setViewport(0, 0, width, height);
      renderer.setScissor(0, 0, width, height);
      renderer.render(this.rack.scene, this.rack.camera);
      renderer.setScissorTest(false);
    }

    if (this.flights.visible) {
      // Tiles in flight are drawn over everything, also over the rack.
      renderer.clearDepth();
      renderer.setViewport(0, 0, this.canvas.clientWidth, this.canvas.clientHeight);
      renderer.render(this.flights.scene, this.camera);
    }
  }

  /** What the director needs from the scene. */
  private directorHost(dotnet: DotNetHost): DirectorHost {
    const scene = this;
    return {
      animator: this.animator,
      rack: this.rack,
      mover: this.mover,
      floaters: this.floaters,
      burst: this.burst,
      flash: this.flash,
      sound: this.sound,
      get sparkles() {
        return qualitySettings[scene.quality].sparkles;
      },
      setBoard: tiles => this.setTiles(tiles),
      shadowChanged: () => {
        this.renderer.shadowMap.needsUpdate = true;
        this.requestFrame();
      },
      takeGhost: () => this.interaction.takeGhost(),
      anchor: name => this.anchor(name),
      aboveCell: (row, col) => this.toCanvas(new THREE.Vector3(cellCenter(row, col).x, 0.9, cellCenter(row, col).y)),
      rackTop: () => ({ x: this.canvas.clientWidth / 2, y: this.canvas.clientHeight - this.rack.heightPx - 8 }),
      slotPoint: tileId => {
        const center = this.rack.tileCenter(tileId) ?? { x: this.canvas.clientWidth / 2, y: this.rack.heightPx / 2 };
        return { x: center.x, y: this.canvas.clientHeight - this.rack.heightPx + center.y };
      },
      planFollow: (row, col) => this.planFollow(row, col),
      rackTileSize: () => this.rack.unitPx() * SIDE,
      canvasSize: () => ({ width: this.canvas.clientWidth, height: this.canvas.clientHeight }),
      flyTiles: states => {
        this.flights.set(states, this.camera, this.canvas.clientWidth, this.canvas.clientHeight);
        this.requestFrame();
      },
      clearFlights: () => {
        this.flights.clear();
        this.requestFrame();
      },
      ready: () => {
        this.log('ready');
        dotnet.invokeMethodAsync('AnimationsFinished').catch(() => undefined);
      },
      log: entry => this.log(entry),
    };
  }

  private log(name: string): void {
    this.eventLog.push({ name, at: Math.round(performance.now() - this.startedAt) });
  }

  /** The centre of a named element of the page, in canvas pixels; elements opt in with data-anchor. */
  private anchor(name: string): ScreenPoint | null {
    const element = (this.canvas.parentElement ?? document).querySelector(`[data-anchor="${name}"]`);
    if (!element) {
      return null;
    }

    const box = element.getBoundingClientRect();
    const canvas = this.canvas.getBoundingClientRect();
    return { x: box.left - canvas.left + box.width / 2, y: box.top - canvas.top + box.height / 2 };
  }

  /**
   * Plans a camera move that brings a cell inside the part of the canvas that the panels and the
   * rack leave free; null when the cell is there already. The camera only slides (left drag pans),
   * so the player's view angle and zoom stay as they set them.
   */
  private planFollow(row: number, col: number): ((progress: number) => void) | null {
    const width = this.canvas.clientWidth;
    const height = this.canvas.clientHeight;
    const corners = cellCorners(row, col).map(p => this.toCanvas(new THREE.Vector3(p.x, 0, p.y)));
    const xs = corners.map(p => p.x);
    const ys = corners.map(p => p.y);

    const sideMargin = THREE.MathUtils.clamp(width * 0.15, 80, 240);
    const top = THREE.MathUtils.clamp(height * 0.2, 90, 180);
    const bottom = Math.max(height - this.rack.heightPx - 100, top + 60);
    const shiftX = shiftIntoRange(Math.min(...xs), Math.max(...xs), sideMargin, width - sideMargin);
    const shiftY = shiftIntoRange(Math.min(...ys), Math.max(...ys), top, bottom);
    if (shiftX === 0 && shiftY === 0) {
      return null;
    }

    // Moving the camera by d makes a point on the table appear where the point d away appeared before.
    const centerX = (Math.min(...xs) + Math.max(...xs)) / 2;
    const centerY = (Math.min(...ys) + Math.max(...ys)) / 2;
    const from = this.tablePoint(centerX, centerY);
    const to = this.tablePoint(centerX + shiftX, centerY + shiftY);
    if (!from || !to) {
      return null;
    }

    const dx = from.x - to.x;
    const dz = from.z - to.z;
    const grabs = this.cameraGrabs;
    let applied = 0;
    return progress => {
      if (grabs !== this.cameraGrabs) {
        return;
      }

      const step = progress - applied;
      applied = progress;
      this.camera.position.x += dx * step;
      this.camera.position.z += dz * step;
      this.controls.target.x += dx * step;
      this.controls.target.z += dz * step;
      this.controls.update();
    };
  }

  /** Where the table lies under a point of the canvas (pixels), or null when that points at the sky. */
  private tablePoint(x: number, y: number): THREE.Vector3 | null {
    const ndc = new THREE.Vector2((x / this.canvas.clientWidth) * 2 - 1, -((y / this.canvas.clientHeight) * 2 - 1));
    const raycaster = new THREE.Raycaster();
    raycaster.setFromCamera(ndc, this.camera);
    return raycaster.ray.intersectPlane(new THREE.Plane(new THREE.Vector3(0, 1, 0), 0), new THREE.Vector3());
  }

  /** A point in the world as canvas pixels. */
  private toCanvas(point: THREE.Vector3): ScreenPoint {
    point.project(this.camera);
    return { x: ((point.x + 1) / 2) * this.canvas.clientWidth, y: ((1 - point.y) / 2) * this.canvas.clientHeight };
  }
}

/** How far a span from min to max has to move to lie between low and high; 0 when it lies there already. */
function shiftIntoRange(min: number, max: number, low: number, high: number): number {
  if (min < low) {
    return low - min;
  }

  return max > high ? high - max : 0;
}

/** The empty element on the page that holds the floating text, or a new one next to the canvas. */
function floatersRoot(canvas: HTMLCanvasElement): HTMLElement {
  const parent = canvas.parentElement ?? document.body;
  const existing = parent.querySelector<HTMLElement>('.floaters');
  if (existing) {
    return existing;
  }

  const created = document.createElement('div');
  created.className = 'floaters';
  parent.append(created);
  return created;
}

let current: TableScene | null = null;

/** Creates the scene on a canvas; replaces any previous scene. */
export async function init(canvas: HTMLCanvasElement, quality: Quality, dotnet: DotNetHost): Promise<void> {
  dispose();
  await loadFonts();
  current = new TableScene(canvas, quality, dotnet);
  exposeDebugApi();
}

/** Shows the given tiles; with focus, the camera moves to their centre. */
export function setBoard(tiles: TileView[], focus = false): void {
  current?.showBoard(tiles, focus);
}

/**
 * Plays a move: the tile falls on the table, the points float up, drawn tiles fly in, the rack
 * slides away. When everything has settled the scene calls AnimationsFinished on the C# side.
 * Returns at once; Blazor would otherwise wait for the animation to end.
 */
export function playMove(move: MoveView): void {
  current?.playMove(move);
}

/** Deals the tiles of a new round from the pot to the players. Returns at once, like playMove. */
export function deal(view: DealView): void {
  current?.deal(view);
}

/** Lights the cell a hint points at; null puts the light out. */
export function setHint(cell: CellView | null): void {
  current?.setHint(cell);
}

/** Shows the rack of the player whose turn it is; an empty list hides it. */
export function setRack(tiles: RackTileView[]): void {
  current?.setRack(tiles);
}

export function setQuality(quality: Quality): void {
  current?.setQuality(quality);
}

/** Turns the sounds on or off and sets the volume (0 to 1); with preview, one sound plays at the new level. */
export function setSound(enabled: boolean, volume: number, preview = false): void {
  current?.setSound(enabled, volume, preview);
}

/** Frame statistics for the performance overlay: frames drawn and average CPU time per frame. */
export function stats(): { frames: number; averageMs: number; drawCalls: number } {
  if (!current) {
    return { frames: 0, averageMs: 0, drawCalls: 0 };
  }

  const times = current.frameTimes;
  const average = times.length === 0 ? 0 : times.reduce((a, b) => a + b, 0) / times.length;
  return { frames: current.framesDrawn, averageMs: average, drawCalls: current.drawCalls };
}

export function dispose(): void {
  current?.dispose();
  current = null;
}

/**
 * With ?debug in the address, screen positions of the rack tiles and board cells are available
 * as window.__triominos, so automated tests can drag tiles without guessing coordinates.
 */
function exposeDebugApi(): void {
  if (!location.search.includes('debug')) {
    return;
  }

  const project = (row: number, col: number) => {
    const scene = current!;
    const center = cellCenter(row, col);
    const point = new THREE.Vector3(center.x, 0, center.y).project(scene.mainCamera);
    const rect = scene.canvasElement.getBoundingClientRect();
    return { x: rect.left + ((point.x + 1) / 2) * rect.width, y: rect.top + ((1 - point.y) / 2) * rect.height };
  };

  (window as unknown as Record<string, unknown>).__triominos = {
    rackTiles: () => {
      const scene = current!;
      const rect = scene.canvasElement.getBoundingClientRect();
      return scene.rackView.tilesInOrder.map(t => {
        const c = scene.rackView.tileCenter(t.id)!;
        return { id: t.id, marked: t.marked, values: t.values, x: rect.left + c.x, y: rect.bottom - scene.rackView.heightPx + c.y };
      });
    },
    heldInfo: () => current!.interactionView.heldInfo(),
    busy: () => current!.busy,
    audio: () => current!.audioState,
    log: () => current!.eventLog,
    frames: () => current!.framesDrawn,
    drawCalls: () => current!.drawCalls,
    cellScreen: project,
  };
}
