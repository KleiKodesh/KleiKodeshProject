import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { viteSingleFile } from 'vite-plugin-singlefile'
import { autoIconsPlugin } from './scripts/vite-plugin-auto-icons'

// Production build configuration for C# WebView2 integration
export default defineConfig({
    plugins: [
        autoIconsPlugin(),
        vue(),
        viteSingleFile(), // Bundle everything into single HTML file
    ],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url))
        },
    },
    build: {
        outDir: 'dist', // Output to standard dist folder
        target: 'esnext',
        assetsInlineLimit: 100000000, // Inline all assets
        chunkSizeWarningLimit: 100000000,
        cssCodeSplit: false,
        rollupOptions: {
            output: {
                inlineDynamicImports: true,
            },
        },
    },
    define: {
        // Disable Iconify API - force offline mode
        'import.meta.env.VITE_ICONIFY_API': JSON.stringify(''),
    },
})
