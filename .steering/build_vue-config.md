---
inclusion: fileMatch
fileMatchPattern: '**/vite.config.*|**/vue.config.*|**/package.json'
---

# Vue Build Configuration

## Single File Output (Required for VSTO)
Configure Vite for single bundled file to avoid CORS issues in WebView2:

```typescript
// vite.config.ts
export default defineConfig({
  build: {
    rollupOptions: {
      output: {
        manualChunks: undefined,
        inlineDynamicImports: true,
        entryFileNames: 'index.js',
        chunkFileNames: 'index.js',
        assetFileNames: '[name].[ext]'
      }
    },
    assetsInlineLimit: 100000000, // Inline all assets
    cssCodeSplit: false
  }
})
```

## Development Setup
- Always run `npm install` immediately after project creation
- Use TypeScript, Router, Pinia, Vitest, ESLint, Prettier as standard
- Remove template content - create clean projects
- Test with `npm run dev` immediately after setup