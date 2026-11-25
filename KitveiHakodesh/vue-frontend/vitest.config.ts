import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'
import vue from '@vitejs/plugin-vue'

// Standalone Vitest config — deliberately does NOT extend vite.config.ts, whose dev plugin
// spawns the .NET KHS service (not needed for unit tests, and it blocks the runner from
// exiting). Keep the '@' alias so imports resolve like the app.
export default defineConfig({
  // .vue files: the windowed scroller's entry (vue-windowed-scroller) exports its component.
  plugins: [vue()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      'kleikodesh-zmanim': fileURLToPath(new URL('../kleikodesh-zmanim.js/src/index.ts', import.meta.url)),
      'vue-windowed-scroller': fileURLToPath(new URL('../vue-windowed-scroller/src/index.ts', import.meta.url)),
    },
    dedupe: ['vue', '@vueuse/core'],
  },
  test: {
    include: ['src/**/*.{test,spec}.ts'],
    environment: 'node',
  },
})
