import { fileURLToPath } from 'node:url'
import { defineConfig } from 'vitest/config'

// Standalone Vitest config — deliberately does NOT extend vite.config.ts, whose dev plugin
// spawns the .NET KHS service (not needed for unit tests, and it blocks the runner from
// exiting). Keep the '@' alias so imports resolve like the app.
export default defineConfig({
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
    },
  },
  test: {
    include: ['src/**/*.{test,spec}.ts'],
    environment: 'node',
  },
})
