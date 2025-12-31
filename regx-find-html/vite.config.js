import { defineConfig } from 'vite';
import { viteSingleFile } from 'vite-plugin-singlefile';

export default defineConfig({
  plugins: [viteSingleFile()],
  build: {
    target: 'esnext',
    assetsInlineLimit: 100000000,
    chunkSizeWarningLimit: 100000000,
    cssCodeSplit: false,
    outDir: 'dist',
    rollupOptions: {
      output: {
        inlineDynamicImports: true,
        manualChunks: undefined,
      }
    }
  },
  base: './',
  resolve: {
    alias: {
      '@': new URL('./js', import.meta.url).pathname,
      '@css': new URL('./css', import.meta.url).pathname,
      '@resources': new URL('./Resources', import.meta.url).pathname
    }
  }
});