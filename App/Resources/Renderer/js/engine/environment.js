import * as THREE from 'three';

/**
 * A soft "photo studio" reflection environment (gradient dome + light panels), so chrome,
 * pearl and glossy materials have something to reflect. Neutral grey — never tints the text.
 * @returns {THREE.Texture}
 */
export function studioEnvironment(renderer) {
  const scene = new THREE.Scene();
  const dome = new THREE.Mesh(
    new THREE.SphereGeometry(10, 32, 16),
    new THREE.MeshBasicMaterial({ side: THREE.BackSide, vertexColors: true }),
  );
  const colors = [];
  const position = dome.geometry.attributes.position;
  for (let i = 0; i < position.count; i++) {
    const t = (position.getY(i) / 10 + 1) / 2;
    const shade = 0.04 + 0.5 * t * t;
    colors.push(shade, shade, shade);
  }
  dome.geometry.setAttribute('color', new THREE.Float32BufferAttribute(colors, 3));
  scene.add(dome);

  const panel = new THREE.PlaneGeometry(1, 1);
  const white = new THREE.MeshBasicMaterial({ color: '#ffffff', side: THREE.DoubleSide });
  [[0, 7, 3, 8, 3], [-7, 2, 4, 2, 6], [7, 1, 2, 2, 6], [0, -2, -8, 10, 1]].forEach(([x, y, z, w, h]) => {
    const light = new THREE.Mesh(panel, white);
    light.position.set(x, y, z);
    light.scale.set(w, h, 1);
    light.lookAt(0, 0, 0);
    scene.add(light);
  });

  const pmrem = new THREE.PMREMGenerator(renderer);
  const texture = pmrem.fromScene(scene, 0.03).texture;
  pmrem.dispose();
  dome.geometry.dispose();
  dome.material.dispose();
  panel.dispose();
  white.dispose();
  return texture;
}
