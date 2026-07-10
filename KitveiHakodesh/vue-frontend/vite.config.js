import { defineConfig, loadEnv } from 'vite';
import vue from '@vitejs/plugin-vue';
import { fileURLToPath, URL } from 'node:url';
import { viteSingleFile } from 'vite-plugin-singlefile';
import { Worker } from 'node:worker_threads';
import path from 'node:path';
import { fileURLToPath as toPath } from 'node:url';
import net from 'node:net';
// Two workers — one handles the lines chunk (LCP path), the other handles the
// TOC query that fires simultaneously. Both open the same DB file, but two
// concurrent cold-opens are fast enough; four was too many (disk I/O contention).
const POOL_SIZE = 2;
function createWorkerPool(workerPath, workerData) {
    const workers = [];
    const pending = new Map();
    let nextRequestId = 0;
    let robin = 0;
    for (let i = 0; i < POOL_SIZE; i++) {
        const worker = new Worker(workerPath, { workerData });
        worker.on('message', (msg) => {
            const entry = pending.get(msg.requestId);
            if (!entry)
                return;
            pending.delete(msg.requestId);
            if (msg.error)
                entry.reject(new Error(msg.error));
            else if (msg.rows !== undefined)
                entry.resolve({ rows: msg.rows });
            else
                entry.resolve({ lastInsertId: msg.lastInsertId });
        });
        worker.on('error', (err) => console.error('[dev-sqlite] worker error:', err));
        workers.push(worker);
    }
    function dispatch(type, sql, params) {
        return new Promise((resolve, reject) => {
            const requestId = nextRequestId++;
            pending.set(requestId, { resolve, reject });
            workers[robin].postMessage({ requestId, type, sql, params });
            robin = (robin + 1) % POOL_SIZE;
        });
    }
    function terminate() {
        for (const w of workers)
            w.terminate();
    }
    return { dispatch, terminate };
}
// ── DocumentLocator named-pipe proxy ──────────────────────────────────────────
const DOCUMENT_LOCATOR_PIPE = '\\\\.\\pipe\\DocumentLocator';
const DOCUMENT_LOCATOR_CONNECT_TIMEOUT_MS = 1_500;
const DOCUMENT_LOCATOR_STARTUP_TIMEOUT_MS = 30_000;
const DOCUMENT_LOCATOR_STARTUP_POLL_MS = 500;
function tryCallDocumentLocatorPipe(requestJson, connectTimeoutMs) {
    return new Promise((resolve, reject) => {
        const socket = net.createConnection(DOCUMENT_LOCATOR_PIPE);
        const connectTimer = setTimeout(() => {
            socket.destroy();
            reject(Object.assign(new Error('DocumentLocator pipe connection timed out'), { code: 'ETIMEDOUT' }));
        }, connectTimeoutMs);
        let responseBuffer = Buffer.alloc(0);
        let expectedLength = -1;
        socket.on('connect', () => {
            clearTimeout(connectTimer);
            const body = Buffer.from(requestJson, 'utf8');
            const header = Buffer.alloc(4);
            header.writeInt32LE(body.length, 0);
            socket.write(Buffer.concat([header, body]));
        });
        socket.on('data', (chunk) => {
            responseBuffer = Buffer.concat([responseBuffer, chunk]);
            if (expectedLength === -1 && responseBuffer.length >= 4) {
                expectedLength = responseBuffer.readInt32LE(0);
            }
            if (expectedLength !== -1 && responseBuffer.length >= 4 + expectedLength) {
                const responseJson = responseBuffer.slice(4, 4 + expectedLength).toString('utf8');
                socket.destroy();
                resolve(responseJson);
            }
        });
        socket.on('error', (err) => {
            clearTimeout(connectTimer);
            reject(err);
        });
    });
}
function startDocumentLocatorService() {
    return new Promise((resolve) => {
        const { exec } = require('node:child_process');
        exec('sc start DocumentLocatorSvc', (err) => {
            if (err && !err.message.includes('1056') && !err.message.includes('already running')) {
                console.warn('[dev-document-locator] sc start failed:', err.message);
            }
            resolve();
        });
    });
}
function waitForDocumentLocatorPipe() {
    return new Promise((resolve, reject) => {
        const deadline = Date.now() + DOCUMENT_LOCATOR_STARTUP_TIMEOUT_MS;
        function attempt() {
            const socket = net.createConnection(DOCUMENT_LOCATOR_PIPE);
            socket.setTimeout(300);
            socket.on('connect', () => { socket.destroy(); resolve(); });
            socket.on('timeout', () => { socket.destroy(); retry(); });
            socket.on('error', () => { retry(); });
        }
        function retry() {
            if (Date.now() >= deadline) {
                reject(new Error('DocumentLocator service did not start in time'));
                return;
            }
            setTimeout(attempt, DOCUMENT_LOCATOR_STARTUP_POLL_MS);
        }
        attempt();
    });
}
async function callDocumentLocatorPipe(requestJson) {
    const pipeNotAvailable = (err) => err.code === 'ENOENT' || err.code === 'ECONNREFUSED' || err.code === 'ETIMEDOUT';
    try {
        return await tryCallDocumentLocatorPipe(requestJson, DOCUMENT_LOCATOR_CONNECT_TIMEOUT_MS);
    }
    catch (firstError) {
        if (!pipeNotAvailable(firstError))
            throw firstError;
        console.log('[dev-document-locator] service not responding — starting via sc start...');
        await startDocumentLocatorService();
        await waitForDocumentLocatorPipe();
        return await tryCallDocumentLocatorPipe(requestJson, DOCUMENT_LOCATOR_CONNECT_TIMEOUT_MS);
    }
}
function devSqlitePlugin() {
    let pool = null;
    return {
        name: 'dev-sqlite',
        apply: 'serve',
        enforce: 'pre',
        async handleHotUpdate(ctx) {
            // Ensure the plugin is alive
            return ctx.modules;
        },
        configureServer(server) {
            const env = loadEnv('development', process.cwd(), '');
            const dbPath = process.env.DB_PATH ?? env.DB_PATH ?? './data.db';
            const dictDbPath = path.resolve('./public/dictionary/KitveiHakodesh_dictionary.db');
            const userSettingsDbPath = path.join(path.dirname(path.resolve(dbPath)), 'Settings', 'user_settings.db');
            const workerPath = path.resolve(path.dirname(toPath(import.meta.url)), 'dev-sqlite-worker.cjs');
            pool = createWorkerPool(workerPath, { dbPath, dictDbPath, userSettingsDbPath });
            console.log(`[dev-sqlite] started ${POOL_SIZE}-worker pool`);
            server.httpServer?.on('close', () => pool?.terminate());
            // Wrap middleware registration to ensure it runs after all built-in Vite middleware is set up
            console.log('[dev-sqlite] registering API middleware');
            server.middlewares.use((req, res, next) => {
                if (req.url?.startsWith('/pdfjs/')) {
                    res.setHeader('Cache-Control', 'no-store');
                }
                if (req.method !== 'POST') {
                    next();
                    return;
                }
                // Document Locator endpoint
                if (req.url === '/document-locator') {
                    let body = '';
                    req.on('data', (chunk) => {
                        body += typeof chunk === 'string' ? chunk : chunk.toString('utf8');
                    });
                    req.on('error', (err) => {
                        console.error('[dev-document-locator] request error:', err.message);
                        if (!res.headersSent) {
                            res.writeHead(400, { 'Content-Type': 'application/json' });
                            res.end(JSON.stringify({ error: 'Request error' }));
                        }
                    });
                    req.on('end', async () => {
                        let requestJson;
                        try {
                            const parsed = JSON.parse(body);
                            // Convert frontend request format to DocumentLocator pipe format
                            // Frontend sends: { type: 'search', query: string, max: number }
                            // Pipe expects: { q: string, limit?: number }
                            const q = parsed.query || parsed.q || '';
                            const limit = Math.min(parsed.max || 200, 5000);
                            requestJson = JSON.stringify(limit > 0 ? { q, limit } : { q });
                        }
                        catch (err) {
                            if (!res.headersSent) {
                                res.writeHead(400, { 'Content-Type': 'application/json' });
                                res.end(JSON.stringify({ error: 'Invalid JSON' }));
                            }
                            return;
                        }
                        try {
                            const responseJson = await callDocumentLocatorPipe(requestJson);
                            const pipeResponse = JSON.parse(responseJson);
                            // Transform the pipe response to match C# FileSystemSearchHandler format
                            let reply;
                            if (pipeResponse.status === 'error') {
                                reply = { error: pipeResponse.message || 'Search error' };
                            }
                            else if (pipeResponse.status === 'ok') {
                                // Convert pipe response to C# format: { results: [{fileName, path, modifiedDate}, ...], total }
                                const paths = pipeResponse.paths || [];
                                // Extract entries with date if the new format is present
                                const entries = pipeResponse.entries;
                                const fs = require('node:fs');
                                const nodePath = require('node:path');
                                const results = (entries ?? paths.map((p) => ({ path: p, date: 0 }))).map((entry) => {
                                    const fullPath = entry.path;
                                    const lastSep = Math.max(fullPath.lastIndexOf('\\'), fullPath.lastIndexOf('/'));
                                    const fileName = lastSep >= 0 ? fullPath.slice(lastSep + 1) : fullPath;
                                    const dir = lastSep >= 0 ? fullPath.slice(0, lastSep) : '';
                                    // Use date from index if present; fall back to stat
                                    let modifiedDate = entry.date || 0;
                                    if (!modifiedDate) {
                                        try {
                                            const stat = fs.statSync(fullPath);
                                            modifiedDate = stat.mtimeMs;
                                        }
                                        catch {
                                            modifiedDate = 0;
                                        }
                                    }
                                    return { fileName, path: dir, modifiedDate };
                                });
                                reply = { results, total: pipeResponse.total || 0 };
                            }
                            else {
                                reply = { error: 'Unexpected response from DocumentLocator service' };
                            }
                            if (!res.headersSent) {
                                res.writeHead(200, { 'Content-Type': 'application/json' });
                                res.end(JSON.stringify(reply));
                            }
                        }
                        catch (err) {
                            console.error('[dev-document-locator] error:', err.message);
                            if (!res.headersSent) {
                                res.writeHead(503, { 'Content-Type': 'application/json' });
                                res.end(JSON.stringify({ error: err.message }));
                            }
                        }
                    });
                    return;
                }
                // SQLite endpoints
                const urlToType = {
                    '/query': 'query',
                    '/query-dict': 'query-dict',
                    '/query-user-settings': 'query-user-settings',
                    '/execute-user-settings': 'exec-user-settings',
                };
                const type = urlToType[req.url];
                if (!type) {
                    next();
                    return;
                }
                let body = '';
                req.on('data', (chunk) => (body += chunk));
                req.on('error', () => {
                    res.writeHead(400, { 'Content-Type': 'application/json' });
                    res.end(JSON.stringify({ error: 'Request error' }));
                });
                req.on('end', () => {
                    let sql, params;
                    try {
                        ;
                        ({ sql, params = [] } = JSON.parse(body));
                    }
                    catch {
                        res.writeHead(400, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ error: 'Invalid JSON' }));
                        return;
                    }
                    pool
                        .dispatch(type, sql, params)
                        .then((result) => {
                        res.writeHead(200, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify(result));
                    })
                        .catch((err) => {
                        console.error('[dev-sqlite] query error:', err.message);
                        res.writeHead(500, { 'Content-Type': 'application/json' });
                        res.end(JSON.stringify({ error: err.message }));
                    });
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
            // @hebcal/core (molad.js) imports temporal-polyfill/global as a side-effectful
            // install of Temporal onto globalThis. Redirect to our conditional stub which
            // only runs the polyfill when native Temporal is absent (Chromium < 138 /
            // WebView2 < 1.0.3912). On Chromium 138+ the native implementation is used
            // as-is, saving the 118KB polyfill from the bundle.
            'temporal-polyfill/global': fileURLToPath(new URL('./src/stubs/temporal-polyfill-stub.ts', import.meta.url)),
        },
    },
    server: {
        warmup: {
            clientFiles: [
                './src/main.ts',
                './src/App.vue',
                './src/layout/AppTitleBar.vue',
                './src/layout/AppPageView.vue',
                './src/layout/AppTitleBarNavDropdown.vue',
                './src/layout/AppTitleBarTabDropdown.vue',
                './src/stores/tabStore.ts',
                './src/stores/settingsStore.ts',
                './src/stores/bookViewStore.ts',
                './src/stores/booksDataStore.ts',
                './src/stores/workspaceStore.ts',
                './src/theme/themeStore.ts',
                './src/utils/persistence.ts',
                './src/webview-host/seforimDb.ts',
                './src/features/home/HomePage.vue',
            ],
        },
    },
    optimizeDeps: {
        exclude: [
            '@iconify-prerendered/vue-fluent',
            '@iconify-prerendered/vue-fluent-color',
            'tesseract.js',
        ],
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
