import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';
import { viteSingleFile } from 'vite-plugin-singlefile';
import Database from 'better-sqlite3';
import path from 'node:path';
function devSqlitePlugin() {
    let db;
    let dictDb;
    let userSettingsDb = null;
    return {
        name: 'dev-sqlite',
        apply: 'serve',
        enforce: 'pre',
        configureServer(server) {
            const env = loadEnv('development', process.cwd(), '');
            const dbPath = process.env.DB_PATH ?? env.DB_PATH ?? './data.db';
            const dictDbPath = path.resolve('./public/dictionary/KitveiHakodesh_dictionary.db');
            try {
                db = new Database(path.resolve(dbPath));
                console.log(`[dev-sqlite] opened ${dbPath}`);
            }
            catch (err) {
                console.error(`[dev-sqlite] failed to open DB at ${dbPath}:`, err);
            }
            try {
                dictDb = new Database(dictDbPath, { readonly: true });
                console.log(`[dev-sqlite] opened KitveiHakodesh_dictionary.db`);
            }
            catch (err) {
                console.error(`[dev-sqlite] failed to open KitveiHakodesh_dictionary.db:`, err);
            }
            try {
                const userSettingsDbPath = path.join(path.dirname(path.resolve(dbPath)), 'Settings', 'user_settings.db');
                userSettingsDb = new Database(userSettingsDbPath);
                userSettingsDb.exec(`
          CREATE TABLE IF NOT EXISTS user_highlights (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            bookId INTEGER NOT NULL, lineId INTEGER NOT NULL,
            startOffset INTEGER NOT NULL, endOffset INTEGER NOT NULL,
            colorArgb INTEGER NOT NULL, createdAt INTEGER NOT NULL
          );
          CREATE INDEX IF NOT EXISTS idx_user_highlights_book_line ON user_highlights (bookId, lineId);
          CREATE TABLE IF NOT EXISTS user_notes (
            id INTEGER PRIMARY KEY AUTOINCREMENT,
            bookId INTEGER NOT NULL, lineId INTEGER NOT NULL,
            startOffset INTEGER NOT NULL, endOffset INTEGER NOT NULL,
            note TEXT NOT NULL, quote TEXT NOT NULL,
            createdAt INTEGER NOT NULL, updatedAt INTEGER NOT NULL
          );
          CREATE INDEX IF NOT EXISTS idx_user_notes_book_line ON user_notes (bookId, lineId);
        `);
                console.log(`[dev-sqlite] opened user_settings.db at ${userSettingsDbPath}`);
            }
            catch (err) {
                console.warn(`[dev-sqlite] could not open user_settings.db:`, err.message);
            }
            // Middleware handler function
            const middleware = (req, res, next) => {
                if (req.url?.startsWith('/pdfjs/')) {
                    res.setHeader('Cache-Control', 'no-store');
                }
                const isQuery = req.url === '/query' && req.method === 'POST';
                const isDictQuery = req.url === '/query-dict' && req.method === 'POST';
                const isUserSettingsQuery = req.url === '/query-user-settings' && req.method === 'POST';
                const isUserSettingsExec = req.url === '/execute-user-settings' && req.method === 'POST';
                if (!isQuery && !isDictQuery && !isUserSettingsQuery && !isUserSettingsExec) {
                    next();
                    return;
                }
                console.log(`[dev-sqlite] handling ${req.url}`);
                let body = '';
                req.on('data', (chunk) => (body += chunk));
                req.on('error', () => {
                    console.error('[dev-sqlite] request error');
                    res.writeHead(400, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ error: 'Request error' }));
                });
                req.on('end', () => {
                    try {
                        const { sql, params = [] } = JSON.parse(body);
                        console.log(`[dev-sqlite] executing: ${sql.substring(0, 50)}...`);
                        if (isUserSettingsExec) {
                            if (!userSettingsDb) {
                                console.error('[dev-sqlite] user settings DB not available');
                                res.writeHead(503, { 'Content-Type': 'application/json' });
                                res.end(JSON.stringify({ error: 'User settings DB not available' }));
                                return;
                            }
                            const result = userSettingsDb.prepare(sql).run(...params);
                            console.log(`[dev-sqlite] insert result: ${result.lastInsertRowid}`);
                            res.writeHead(200, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({ lastInsertId: result.lastInsertRowid }));
                            return;
                        }
                        const target = isDictQuery ? dictDb : isUserSettingsQuery ? userSettingsDb : db;
                        if (!target) {
                            console.error('[dev-sqlite] target DB not available');
                            res.writeHead(503, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({ error: 'Database not available' }));
                            return;
                        }
                        const rows = target.prepare(sql).all(...params);
                        console.log(`[dev-sqlite] query returned ${rows.length} rows`);
                        res.writeHead(200, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ rows }));
                    }
                    catch (err) {
                        console.error('[dev-sqlite] query error:', err.message);
                        res.writeHead(500, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ error: err.message }));
                    }
                });
            };
            server.middlewares.use(middleware);
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
