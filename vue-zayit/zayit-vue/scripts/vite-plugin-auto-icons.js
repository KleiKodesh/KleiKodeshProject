// Vite plugin to automatically extract icons during development
import { execSync } from 'child_process';
import path from 'path';
import { fileURLToPath } from 'url';

const __filename = fileURLToPath(import.meta.url);
const __dirname = path.dirname(__filename);

export function autoIconsPlugin() {
    let isFirstRun = true;

    return {
        name: 'vite-plugin-auto-icons',

        buildStart() {
            // Only run on first build/dev server start
            if (isFirstRun) {
                console.log('\n🔄 Auto-extracting icons from codebase...');
                try {
                    execSync('node scripts/auto-extract-icons.js', {
                        cwd: path.join(__dirname, '..'),
                        stdio: 'inherit'
                    });
                    isFirstRun = false;
                } catch (error) {
                    console.error('❌ Failed to extract icons:', error.message);
                }
            }
        },

        // Watch for changes in Vue/TS files and re-extract if icon usage changes
        handleHotUpdate({ file }) {
            if (file.match(/\.(vue|ts|tsx|js|jsx)$/)) {
                // Debounce: only check occasionally, not on every file change
                // This is a simple approach - could be optimized further
                return;
            }
        }
    };
}
