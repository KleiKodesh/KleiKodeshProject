import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';
import { viteSingleFile } from 'vite-plugin-singlefile';
import Database from 'better-sqlite3';
import path from 'node:path';
function devSqlitePlugin() {
    let db;
    return {
        name: 'dev-sqlite',
        apply: 'serve',
        configureServer(server) {
            // loadEnv with prefix '' loads all vars including non-VITE_ ones
            const env = loadEnv('development', process.cwd(), '');
            const dbPath = process.env.DB_PATH ?? env.DB_PATH ?? './data.db';
            try {
                db = new Database(path.resolve(dbPath));
                console.log(`[dev-sqlite] opened ${dbPath}`);
            }
            catch (err) {
                console.error(`[dev-sqlite] failed to open DB at ${dbPath}:`, err);
                return;
            }
            server.middlewares.use((req, res, next) => {
                if (req.url !== '/query' || req.method !== 'POST') {
                    next();
                    return;
                }
                let body = '';
                req.on('data', (chunk) => (body += chunk));
                req.on('end', () => {
                    try {
                        const { sql, params = [] } = JSON.parse(body);
                        const rows = db.prepare(sql).all(...params);
                        res.writeHead(200, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ rows }));
                    }
                    catch (err) {
                        res.writeHead(500, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ error: err.message }));
                    }
                });
            });
        },
    };
}
export default defineConfig({
    plugins: [devSqlitePlugin(), vue(), viteSingleFile()],
    resolve: {
        alias: {
            '@': fileURLToPath(new URL('./src', import.meta.url)),
        },
    },
    build: {
        assetsInlineLimit: Number.MAX_SAFE_INTEGER,
        cssCodeSplit: false,
        rollupOptions: {
            output: {
                inlineDynamicImports: true,
            },
        },
    },
});
