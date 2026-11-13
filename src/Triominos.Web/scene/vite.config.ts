import { defineConfig } from 'vite';

// Builds the 3D scene as one ES module that Blazor imports with JS interop.
export default defineConfig({
  build: {
    outDir: '../wwwroot/js/scene',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: true,
    lib: {
      entry: 'src/main.ts',
      formats: ['es'],
      fileName: () => 'scene.js',
    },
  },
});
