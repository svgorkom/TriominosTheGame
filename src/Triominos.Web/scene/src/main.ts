import * as THREE from 'three';
import { OrbitControls } from 'three/examples/jsm/controls/OrbitControls.js';
import { RoomEnvironment } from 'three/examples/jsm/environments/RoomEnvironment.js';
import { cellCenter, SIDE } from './geometry';
import { PlacementGlow } from './glow';
import { Interaction } from './input';
import { Rack } from './rack';
import { createTable, FELT_DEPTH, FELT_WIDTH } from './table';
import { loadFonts } from './textures';
import { BOARD_CAPACITY, TileLayer } from './tiles';
import { qualitySettings, type DotNetHost, type Quality, type RackTileView, type TileView } from './types';

export type { DotNetHost, PlacementView, Quality, RackTileView, TileView } from './types';

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
  private readonly rack = new Rack();
  private readonly interaction: Interaction;
  private readonly occupied = new Set<string>();
  private environment: THREE.Texture | null = null;
  private readonly resizeObserver: ResizeObserver;
  private frameRequested = false;
  private disposed = false;
  private quality: Quality;

  /** Duration of the most recent frames in ms (CPU side), for the performance overlay. */
  readonly frameTimes: number[] = [];
  framesDrawn = 0;

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
    pmrem.dispose();

    this.scene.add(new THREE.HemisphereLight(0xfff2dc, 0x1c120a, 0.18));
    this.sun.position.set(-9, 22, 12);
    this.sun.castShadow = true;
    this.sun.shadow.bias = -0.0004;
    this.sun.shadow.normalBias = 0.02;
    this.sun.shadow.radius = 4;
    this.scene.add(this.sun, this.sun.target);

    this.scene.add(createTable(), this.tiles.group, this.glow.mesh, this.ghost.group);

    this.camera.position.set(0, 11.5, 14);
    this.controls = new OrbitControls(this.camera, canvas);
    this.configureControls();

    this.interaction = new Interaction({
      canvas,
      camera: this.camera,
      controls: this.controls,
      rack: this.rack,
      ghost: this.ghost,
      glow: this.glow,
      dotnet,
      isOccupied: (row, col) => this.occupied.has(`${row},${col}`),
      requestFrame: () => this.requestFrame(),
    });

    this.resizeObserver = new ResizeObserver(() => this.resize());
    this.resizeObserver.observe(canvas);

    this.applyQuality();
    this.resize();
  }

  setTiles(tiles: readonly TileView[]): void {
    this.tiles.setTiles(tiles);
    this.occupied.clear();
    tiles.forEach(t => this.occupied.add(`${t.row},${t.col}`));
    this.fitShadowToTiles(tiles);
    this.renderer.shadowMap.needsUpdate = true;
    this.interaction.viewChanged();
    this.requestFrame();
  }

  /** Shows the rack of the player whose turn it is; an empty rack hides it. */
  setRack(tiles: readonly RackTileView[]): void {
    this.interaction.cancel();
    this.rack.setTiles(tiles);
    this.requestFrame();
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

  dispose(): void {
    this.disposed = true;
    this.resizeObserver.disconnect();
    this.interaction.dispose();
    this.controls.dispose();
    this.rack.dispose();
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
    this.sun.position.set(center.x - 9, 22, center.y + 12);
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
    // While damping, the controls keep moving after the input stops.
    const moving = this.controls.update();
    this.drawFrame();
    this.framesDrawn++;
    this.frameTimes.push(performance.now() - start);
    if (this.frameTimes.length > 120) {
      this.frameTimes.shift();
    }

    if (moving) {
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

    if (this.rack.visible) {
      const width = this.canvas.clientWidth;
      const height = this.rack.heightPx;
      renderer.clearDepth();
      renderer.setScissorTest(true);
      renderer.setViewport(0, 0, width, height);
      renderer.setScissor(0, 0, width, height);
      renderer.render(this.rack.scene, this.rack.camera);
      renderer.setScissorTest(false);
    }
  }
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
  current?.setTiles(tiles);
  if (focus) {
    current?.focusTiles(tiles);
  }
}

/** Shows the rack of the player whose turn it is; an empty list hides it. */
export function setRack(tiles: RackTileView[]): void {
  current?.setRack(tiles);
}

export function setQuality(quality: Quality): void {
  current?.setQuality(quality);
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
    cellScreen: project,
  };
}
