import { defineConfig } from 'vite'
import { viteStaticCopy } from 'vite-plugin-static-copy'
import vue from '@vitejs/plugin-vue'

// Builds into the app's web root; the main window shows it in an iframe on the Çizim page.
export default defineConfig({
  base: './',
  plugins: [
    vue(),
    viteStaticCopy({
      targets: [
        { src: './node_modules/@mlightcad/cad-simple-viewer/dist/mtext-renderer-worker.js', dest: 'assets', rename: { stripBase: true } },
        { src: './node_modules/@mlightcad/libredwg-converter/dist/libredwg-parser-worker.js', dest: 'assets', rename: { stripBase: true } },
        { src: './node_modules/@mlightcad/libredwg-converter/dist/libredwg-web.wasm', dest: 'assets', rename: { stripBase: true } }
      ]
    })
  ],
  build: {
    outDir: '../../src/MuhendisKit/wwwroot/cad2d',
    emptyOutDir: true,
    modulePreload: false,
    chunkSizeWarningLimit: 8000,
    rollupOptions: {
      output: {
        manualChunks(id) {
          const p = id.split('\\').join('/')
          if (p.includes('/node_modules/three/')) return 'three'
          if (/@mlightcad\/(three-renderer|mtext-|shx-parser)/.test(p)) return 'three-renderer'
          if (/@mlightcad\/(data-model|geometry-engine|graphic-interface|common)\//.test(p)) return 'data-model'
        }
      }
    }
  }
})
