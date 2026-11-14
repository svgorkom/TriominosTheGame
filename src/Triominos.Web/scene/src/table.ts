import * as THREE from 'three';
import { planarUv } from './geometry';
import { feltTextures, walnutTexture } from './textures';

/** Size of the playing area (felt), in world units. */
export const FELT_WIDTH = 44;
export const FELT_DEPTH = 30;

/** How often the felt texture repeats across the width of the table. */
const FELT_REPEAT = 16;

const RIM = 2.2;
const RIM_HEIGHT = 0.55;
const CORNER = 1.4;

export function roundedRect(path: THREE.Path, width: number, depth: number, radius: number): void {
  const x = -width / 2;
  const y = -depth / 2;
  path.moveTo(x + radius, y);
  path.lineTo(x + width - radius, y);
  path.quadraticCurveTo(x + width, y, x + width, y + radius);
  path.lineTo(x + width, y + depth - radius);
  path.quadraticCurveTo(x + width, y + depth, x + width - radius, y + depth);
  path.lineTo(x + radius, y + depth);
  path.quadraticCurveTo(x, y + depth, x, y + depth - radius);
  path.lineTo(x, y + radius);
  path.quadraticCurveTo(x, y, x + radius, y);
}

/** The table: a felt playing surface inside a raised, lacquered walnut rim. */
export function createTable(): THREE.Group {
  const group = new THREE.Group();

  const felt = feltTextures();
  // The same number of fibres per unit in both directions.
  [felt.map, felt.bump].forEach(texture => texture.repeat.set(FELT_REPEAT, (FELT_REPEAT * FELT_DEPTH) / FELT_WIDTH));
  const feltMesh = new THREE.Mesh(
    new THREE.PlaneGeometry(FELT_WIDTH + 0.4, FELT_DEPTH + 0.4),
    new THREE.MeshPhysicalMaterial({
      color: 0xffffff,
      map: felt.map,
      bumpMap: felt.bump,
      bumpScale: 0.6,
      roughness: 0.97,
      metalness: 0,
      // The nap of the cloth catches the light at a low angle: the far side of the table lights up.
      sheen: 1,
      sheenRoughness: 0.45,
      sheenColor: new THREE.Color(0x5fae8c),
    }),
  );
  feltMesh.rotation.x = -Math.PI / 2;
  feltMesh.receiveShadow = true;
  group.add(feltMesh);

  const outer = new THREE.Shape();
  roundedRect(outer, FELT_WIDTH + RIM * 2, FELT_DEPTH + RIM * 2, CORNER + RIM);
  const hole = new THREE.Path();
  roundedRect(hole, FELT_WIDTH, FELT_DEPTH, CORNER * 0.5);
  outer.holes.push(hole);

  const rimGeometry = new THREE.ExtrudeGeometry(outer, {
    depth: RIM_HEIGHT,
    bevelEnabled: true,
    bevelThickness: 0.12,
    bevelSize: 0.14,
    bevelSegments: 4,
    curveSegments: 12,
  });
  rimGeometry.rotateX(-Math.PI / 2);
  planarUv(rimGeometry);
  rimGeometry.computeVertexNormals();

  const rim = new THREE.Mesh(
    rimGeometry,
    new THREE.MeshPhysicalMaterial({
      map: walnutTexture(),
      roughness: 0.5,
      clearcoat: 1,
      clearcoatRoughness: 0.12,
    }),
  );
  rim.position.y = -0.12;
  rim.castShadow = true;
  rim.receiveShadow = true;
  group.add(rim);

  return group;
}
