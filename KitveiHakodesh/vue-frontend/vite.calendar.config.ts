// The standalone calendar (src/calendar-app): calendar.html as ONE offline file in
// dist-calendar/, which the installer ships as the "calendar" option (an Edge app window,
// no service). Same source and the same compile as the app (vite.shared.ts).
//   npx vite build --config vite.calendar.config.ts
import fs from 'node:fs'
import path from 'node:path'
import { defineConfig, type Plugin } from 'vite'
import { appResolve, vueSingleFilePlugins, ZMANIM_ASSETS_DIR, zmanimAssetFiles } from './vite.shared.js'

/** virtual:zmanim-inline-assets - every zmanim data file as a data: URL (a page from disk cannot fetch beside it). */
function zmanimInlineAssets(): Plugin {
  const id = 'virtual:zmanim-inline-assets'
  return {
    name: 'zmanim-inline-assets',
    resolveId: (source) => (source === id ? `\0${id}` : null),
    load(resolved) {
      if (resolved !== `\0${id}`) return null
      const urls = Object.fromEntries(zmanimAssetFiles().map((file) => [
        file, `data:application/octet-stream;base64,${fs.readFileSync(path.join(ZMANIM_ASSETS_DIR, file)).toString('base64')}`,
      ]))
      return `export const zmanimAssetUrls = ${JSON.stringify(urls)}`
    },
  }
}

export default defineConfig({
  plugins: [...vueSingleFilePlugins(), zmanimInlineAssets()],
  resolve: appResolve,
  // No public/ copy: the page is the whole app.
  publicDir: false,
  build: {
    outDir: 'dist-calendar',
    emptyOutDir: true,
    assetsInlineLimit: Number.MAX_SAFE_INTEGER,
    cssCodeSplit: false,
    rollupOptions: {
      input: 'calendar.html',
      output: { inlineDynamicImports: true },
    },
  },
})
