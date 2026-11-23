// Build pieces shared by vite.config.ts (the app) and vite.calendar.config.ts (the
// standalone calendar), so both compile the same source the same way.
import fs from 'node:fs'
import { fileURLToPath, URL } from 'node:url'
import type { PluginOption } from 'vite'
import vue from '@vitejs/plugin-vue'
import VueI18nPlugin from '@intlify/unplugin-vue-i18n/vite'
import { viteSingleFile } from 'vite-plugin-singlefile'

/** The zmanim library's data files (places, names, zones). */
export const ZMANIM_ASSETS_DIR = fileURLToPath(new URL('../kleikodesh-zmanim.js/assets/', import.meta.url))
// Every file in the library's assets/ folder ships, so new data files need no change here.
export const zmanimAssetFiles = (): string[] => fs.readdirSync(ZMANIM_ASSETS_DIR, { withFileTypes: true }).filter((e) => e.isFile()).map((e) => e.name)

/** Vue, precompiled i18n and the one-file output: shared with vite.calendar.config.ts. */
export function vueSingleFilePlugins(): PluginOption[] {
  return [
    vue(),
    // Precompiles src/i18n/locales/**/*.json so the runtime-only vue-i18n build ships (no message compiler).
    VueI18nPlugin({ include: [fileURLToPath(new URL('./src/i18n/locales/**', import.meta.url))], runtimeOnly: true, compositionOnly: true, fullInstall: false }),
    viteSingleFile(),
  ]
}

/** Import aliases: shared with vite.calendar.config.ts. */
export const appResolve = {
  alias: {
    '@': fileURLToPath(new URL('./src', import.meta.url)),
    // The zmanim library, compiled from its TypeScript source in the submodule.
    'kleikodesh-zmanim': fileURLToPath(new URL('../kleikodesh-zmanim.js/src/index.ts', import.meta.url)),
    // @hebcal/core (molad.js) imports temporal-polyfill/global as a side-effectful
    // install of Temporal onto globalThis. Redirect to our conditional stub which
    // only runs the polyfill when native Temporal is absent (Chromium < 138 /
    // WebView2 < 1.0.3912). On Chromium 138+ the native implementation is used
    // as-is, saving the 118KB polyfill from the bundle.
    'temporal-polyfill/global': fileURLToPath(new URL('./src/stubs/temporal-polyfill-stub.ts', import.meta.url)),
  },
}
