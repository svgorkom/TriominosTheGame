import * as THREE from 'three';

/**
 * Entry point of the 3D scene, imported by Blazor. Phase 0 only proves the pipeline: a table,
 * one tile and render-on-demand. The game scene is built in phase 1.
 */

interface SceneHandle {
  renderer: THREE.WebGLRenderer;
  scene: THREE.Scene;
  camera: THREE.PerspectiveCamera;
  resizeObserver: ResizeObserver;
  frameRequested: boolean;
}

let handle: SceneHandle | null = null;

/** Creates the scene on a canvas. Calling it again replaces the previous scene. */
export function init(canvas: HTMLCanvasElement): void {
  dispose();

  const renderer = new THREE.WebGLRenderer({ canvas, antialias: true });
  renderer.setPixelRatio(Math.min(window.devicePixelRatio, 2));
  renderer.shadowMap.enabled = true;
  renderer.shadowMap.type = THREE.PCFSoftShadowMap;
  renderer.outputColorSpace = THREE.SRGBColorSpace;
  renderer.toneMapping = THREE.ACESFilmicToneMapping;

  const scene = new THREE.Scene();
  scene.background = new THREE.Color(0x1a110b);

  const camera = new THREE.PerspectiveCamera(40, 1, 0.1, 100);
  camera.position.set(0, 9, 7);
  camera.lookAt(0, 0, 0);

  scene.add(new THREE.HemisphereLight(0xfff4e0, 0x20140c, 0.6));
  const sun = new THREE.DirectionalLight(0xfff1d6, 2.2);
  sun.position.set(-4, 10, 5);
  sun.castShadow = true;
  sun.shadow.mapSize.set(2048, 2048);
  scene.add(sun);

  const felt = new THREE.Mesh(
    new THREE.PlaneGeometry(14, 10),
    new THREE.MeshStandardMaterial({ color: 0x0f3d2e, roughness: 0.95 }),
  );
  felt.rotation.x = -Math.PI / 2;
  felt.receiveShadow = true;
  scene.add(felt);

  const tile = new THREE.Mesh(createTileGeometry(), new THREE.MeshPhysicalMaterial({
    color: 0xf4eedf,
    roughness: 0.35,
    clearcoat: 0.8,
    clearcoatRoughness: 0.2,
  }));
  tile.castShadow = true;
  scene.add(tile);

  const resizeObserver = new ResizeObserver(() => {
    resize(canvas, renderer, camera);
    requestFrame();
  });
  resizeObserver.observe(canvas);

  handle = { renderer, scene, camera, resizeObserver, frameRequested: false };
  resize(canvas, renderer, camera);
  requestFrame();
}

/** Releases the GPU resources of the scene. */
export function dispose(): void {
  if (!handle) {
    return;
  }

  handle.resizeObserver.disconnect();
  handle.scene.traverse(obj => {
    if (obj instanceof THREE.Mesh) {
      obj.geometry.dispose();
      (Array.isArray(obj.material) ? obj.material : [obj.material]).forEach(m => m.dispose());
    }
  });
  handle.renderer.dispose();
  handle = null;
}

/** Draws one frame at the next animation frame; repeated requests collapse into one. */
function requestFrame(): void {
  if (!handle || handle.frameRequested) {
    return;
  }

  handle.frameRequested = true;
  requestAnimationFrame(() => {
    if (!handle) {
      return;
    }

    handle.frameRequested = false;
    handle.renderer.render(handle.scene, handle.camera);
  });
}

function resize(canvas: HTMLCanvasElement, renderer: THREE.WebGLRenderer, camera: THREE.PerspectiveCamera): void {
  const width = canvas.clientWidth;
  const height = canvas.clientHeight;
  if (width === 0 || height === 0) {
    return;
  }

  renderer.setSize(width, height, false);
  camera.aspect = width / height;
  camera.updateProjectionMatrix();
}

/** An equilateral triangle with rounded, beveled edges, lying flat on the table. */
function createTileGeometry(): THREE.BufferGeometry {
  const side = 2;
  const height = (side * Math.sqrt(3)) / 2;
  const shape = new THREE.Shape();
  shape.moveTo(0, (2 * height) / 3);
  shape.lineTo(side / 2, -height / 3);
  shape.lineTo(-side / 2, -height / 3);
  shape.closePath();

  const geometry = new THREE.ExtrudeGeometry(shape, {
    depth: 0.18,
    bevelEnabled: true,
    bevelThickness: 0.04,
    bevelSize: 0.06,
    bevelSegments: 4,
  });
  geometry.rotateX(-Math.PI / 2);
  geometry.translate(0, 0.04, 0);
  return geometry;
}
