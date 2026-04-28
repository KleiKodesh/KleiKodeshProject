import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';
import { viteSingleFile } from 'vite-plugin-singlefile';
import Database from 'better-sqlite3';
import path from 'node:path';
function devSqlitePlugin() {
    let db;
    let dictDb;
    return {
        name: 'dev-sqlite',
        apply: 'serve',
        configureServer(server) {
            const env = loadEnv('development', process.cwd(), '');
            const dbPath = process.env.DB_PATH ?? env.DB_PATH ?? './data.db';
            const dictDbPath = path.resolve('./public/dictionary/kezayit_dictionary.db');
            try {
                db = new Database(path.resolve(dbPath));
                console.log(`[dev-sqlite] opened ${dbPath}`);
            }
            catch (err) {
                console.error(`[dev-sqlite] failed to open DB at ${dbPath}:`, err);
            }
            try {
                dictDb = new Database(dictDbPath, { readonly: true });
                console.log(`[dev-sqlite] opened kezayit_dictionary.db`);
            }
            catch (err) {
                console.error(`[dev-sqlite] failed to open kezayit_dictionary.db:`, err);
            }
            server.middlewares.use((req, res, next) => {
                // Prevent browser caching of PDF.js files during dev
                if (req.url?.startsWith('/pdfjs/')) {
                    res.setHeader('Cache-Control', 'no-store');
                }
                const isQuery = req.url === '/query' && req.method === 'POST';
                const isDictQuery = req.url === '/query-dict' && req.method === 'POST';
                if (!isQuery && !isDictQuery) {
                    next();
                    return;
                }
                const target = isDictQuery ? dictDb : db;
                if (!target) {
                    res.writeHead(503, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ error: 'Database not available' }));
                    return;
                }
                let body = '';
                req.on('data', (chunk) => (body += chunk));
                req.on('end', () => {
                    try {
                        const { sql, params = [] } = JSON.parse(body);
                        const rows = target.prepare(sql).all(...params);
                        res.writeHead(200, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ rows }));
                    }
                    catch (err) {
                        console.error('[dev-sqlite] query error:', err.message);
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
