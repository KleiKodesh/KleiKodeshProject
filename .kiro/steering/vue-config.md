# Vue Configuration Guidelines

## Single File Output Configuration
- Configure Vue projects for single file output using Vite build configuration
- Use `vite.config.ts` to configure build output as a single bundled file
- Set `build.rollupOptions.output.manualChunks` to undefined to prevent code splitting
- Configure `build.rollupOptions.output.inlineDynamicImports` to true for single file output
- Use `build.assetsInlineLimit` to inline all assets into the bundle

## Development Setup
- Always run `npm install` immediately after project creation
- Configure for immediate testing with `npm run dev`
- Use TypeScript, Router, Pinia, Vitest, ESLint, and Prettier as standard setup
- Ensure the project can be tested immediately after creation
- Create clean projects without template content - remove default components and views
- Replace template content with actual application components

## Build Configuration Example
```typescript
// vite.config.ts
export default defineConfig({
  build: {
    rollupOptions: {
      output: {
        manualChunks: undefined,
        inlineDynamicImports: true,
        entryFileNames: 'assets/[name].js',
        chunkFileNames: 'assets/[name].js',
        assetFileNames: 'assets/[name].[ext]'
      }
    },
    assetsInlineLimit: 100000000 // Inline all assets
  }
})
```