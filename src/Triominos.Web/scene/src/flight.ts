import * as THREE from 'three';
import { SIDE } from './geometry';
import type { Skin } from './textures';
import type { SceneTheme } from './theme';
import { TileLayer } from './tiles';
import type { TilePose } from './types';

/** The plane the flying tiles are placed on; only its height matters for how the tiles look. */
const FLIGHT_HEIGHT = 0.5;

/** A tile in flight, described in screen terms: where it is on the canvas and how big it looks. */
export interface FlightState {
  /** Centre in canvas pixels. */
  x: number;
  y: number;
  /** Length of a tile side in pixels. */
  size: number;
  yaw: number;
  /** 0 = face up, 1 = face down. */
  flip: number;
  values: readonly [number, number, number];
  tint?: number;
}

/**
 * Tiles flying between the pot, the racks and the players. They are drawn in a pass of their
 * own, over the whole canvas and over the rack, so a tile can leave the rack strip and cross
 * the table. Positions are given in canvas pixels and turned into the table plane with the table
 * camera, so a tile lands exactly under the point it was aimed at.
 */
export class FlightLayer {
  readonly scene = new THREE.Scene();
  private readonly layer = new TileLayer(24, false);
  private readonly raycaster = new THREE.Raycaster();
  private readonly plane = new THREE.Plane(new THREE.Vector3(0, 1, 0), -FLIGHT_HEIGHT);
  private readonly ndc = new THREE.Vector2();
  private readonly hit = new THREE.Vector3();
  private readonly here = new THREE.Vector3();
  private readonly there = new THREE.Vector3();
  private readonly key = new THREE.DirectionalLight(0xffffff, 2.6);
  private readonly fill = new THREE.HemisphereLight(0xffffff, 0x000000, 0.35);
  private shown = false;

  constructor() {
    this.key.position.set(-6, 14, 9);
    this.scene.add(this.key, this.fill, this.layer.group);
  }

  applyTheme(theme: SceneTheme, skin: Skin): void {
    this.layer.applyTheme(theme, skin);
    this.key.color.set(theme.sun);
    this.fill.color.set(theme.sky);
    this.fill.groundColor.set(theme.ground);
  }

  get visible(): boolean {
    return this.shown;
  }

  setEnvironment(texture: THREE.Texture | null): void {
    this.scene.environment = texture;
    this.scene.environmentIntensity = 0.35;
  }

  /** Shows the given tiles, seen through the table camera of a canvas of the given size. */
  set(states: readonly FlightState[], camera: THREE.PerspectiveCamera, width: number, height: number): void {
    const poses: TilePose[] = [];
    for (const state of states) {
      const pose = this.pose(state, camera, width, height);
      if (pose) {
        poses.push(pose);
      }
    }

    this.layer.setPoses(poses);
    this.shown = poses.length > 0;
  }

  clear(): void {
    this.layer.setPoses([]);
    this.shown = false;
  }

  /** Puts one dummy tile in view so the shaders are compiled before the first real flight. */
  warmUp(renderer: THREE.WebGLRenderer, camera: THREE.Camera): void {
    this.layer.setPoses([{ x: 0, y: 0, z: 0, yaw: 0, flip: 0, scale: 1, values: [0, 1, 2] }]);
    renderer.compile(this.scene, camera);
    this.clear();
  }

  private pose(state: FlightState, camera: THREE.PerspectiveCamera, width: number, height: number): TilePose | null {
    this.ndc.set((state.x / width) * 2 - 1, -((state.y / height) * 2 - 1));
    this.raycaster.setFromCamera(this.ndc, camera);
    if (!this.raycaster.ray.intersectPlane(this.plane, this.hit)) {
      return null;
    }

    // How many pixels one world unit measures here, to scale the tile to the wanted size.
    this.here.copy(this.hit).project(camera);
    this.there.set(this.hit.x + 1, this.hit.y, this.hit.z).project(camera);
    const pixelsPerUnit = (Math.abs(this.there.x - this.here.x) / 2) * width;
    if (pixelsPerUnit === 0) {
      return null;
    }

    return {
      x: this.hit.x,
      y: this.hit.y,
      z: this.hit.z,
      yaw: state.yaw,
      flip: state.flip,
      scale: state.size / (SIDE * pixelsPerUnit),
      values: state.values,
      tint: state.tint,
    };
  }
}
