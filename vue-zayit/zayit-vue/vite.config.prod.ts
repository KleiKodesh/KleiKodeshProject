import { fileURLToPath, URL } from 'node:url'
import { defineConfig } from 'vite'
import vue from '@vitejs/plugin-vue'
import { viteSingleFile } from 'vite-plugin-singlefile'

// Production build configuration for C# WebView2 integration
export default defineConfig({
    plugins: [
        vue(),
        viteSingleFile(), // Bundle everything into single HTML file
    ],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url))
        },
    },
    build: {
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
})
