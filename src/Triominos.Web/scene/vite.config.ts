import { defineConfig } from 'vite';

// Builds the 3D scene as one ES module that Blazor imports with JS interop.
export default defineConfig({
  // The dev harness (dev/index.html) imports the app's CSS and fonts from ../wwwroot.
  server: { fs: { allow: ['..'] } },
  build: {
    outDir: '../wwwroot/js/scene',
    emptyOutDir: true,
    target: 'es2022',
    sourcemap: false,
    lib: {
      entry: 'src/main.ts',
      formats: ['es'],
      fileName: () => 'scene.js',
    },
  },
});
