import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'
import { viteSingleFile } from 'vite-plugin-singlefile'
import type { Plugin } from 'vite'
import { Worker } from 'node:worker_threads'
import path from 'node:path'
import { fileURLToPath as toPath } from 'node:url'

// Two workers — one handles the lines chunk (LCP path), the other handles the
// TOC query that fires simultaneously. Both open the same DB file, but two
// concurrent cold-opens are fast enough; four was too many (disk I/O contention).
const POOL_SIZE = 2

interface PendingRequest {
  resolve: (value: unknown) => void
  reject: (reason: unknown) => void
}

function createWorkerPool(workerPath: string, workerData: object) {
  const workers: Worker[] = []
  // pending[requestId] → { resolve, reject }
  const pending = new Map<number, PendingRequest>()
  let nextRequestId = 0
  // Round-robin index for dispatching to workers
  let robin = 0

  for (let i = 0; i < POOL_SIZE; i++) {
    const worker = new Worker(workerPath, { workerData })
    worker.on('message', (msg: { requestId: number; rows?: unknown[]; lastInsertId?: number; error?: string }) => {
      const entry = pending.get(msg.requestId)
      if (!entry) return
      pending.delete(msg.requestId)
      if (msg.error) entry.reject(new Error(msg.error))
      else if (msg.rows !== undefined) entry.resolve({ rows: msg.rows })
      else entry.resolve({ lastInsertId: msg.lastInsertId })
    })
    worker.on('error', (err) => console.error('[dev-sqlite] worker error:', err))
    workers.push(worker)
  }

  function dispatch(type: string, sql: string, params: unknown[]): Promise<unknown> {
    return new Promise((resolve, reject) => {
      const requestId = nextRequestId++
      pending.set(requestId, { resolve, reject })
      workers[robin]!.postMessage({ requestId, type, sql, params })
      robin = (robin + 1) % POOL_SIZE
    })
  }

  function terminate() {
    for (const w of workers) w.terminate()
  }

  return { dispatch, terminate }
}

function devSqlitePlugin(): Plugin {
  let pool: ReturnType<typeof createWorkerPool> | null = null

  return {
    name: 'dev-sqlite',
    apply: 'serve',
    enforce: 'pre',

    configureServer(server) {
      const env = loadEnv('development', process.cwd(), '')
      const dbPath = process.env.DB_PATH ?? env.DB_PATH ?? './data.db'
      const dictDbPath = path.resolve('./public/dictionary/KitveiHakodesh_dictionary.db')
      const userSettingsDbPath = path.join(path.dirname(path.resolve(dbPath)), 'Settings', 'user_settings.db')
      const workerPath = path.resolve(path.dirname(toPath(import.meta.url)), 'dev-sqlite-worker.cjs')

      pool = createWorkerPool(workerPath, { dbPath, dictDbPath, userSettingsDbPath })
      console.log(`[dev-sqlite] started ${POOL_SIZE}-worker pool`)

      server.httpServer?.on('close', () => pool?.terminate())

      const middleware = (req: any, res: any, next: any) => {
        if (req.url?.startsWith('/pdfjs/')) {
          res.setHeader('Cache-Control', 'no-store')
        }

        const urlToType: Record<string, string> = {
          '/query':                  'query',
          '/query-dict':             'query-dict',
          '/query-user-settings':    'query-user-settings',
          '/execute-user-settings':  'exec-user-settings',
        }
        const type = req.method === 'POST' ? urlToType[req.url] : undefined
        if (!type) { next(); return }

        let body = ''
        req.on('data', (chunk: string) => (body += chunk))
        req.on('error', () => {
          res.writeHead(400, { 'Content-Type': 'application/json' })
          res.end(JSON.stringify({ error: 'Request error' }))
        })
        req.on('end', () => {
          let sql: string, params: unknown[]
          try {
            ;({ sql, params = [] } = JSON.parse(body))
          } catch {
            res.writeHead(400, { 'Content-Type': 'application/json' })
            res.end(JSON.stringify({ error: 'Invalid JSON' }))
            return
          }

          pool!.dispatch(type, sql, params).then((result) => {
            res.writeHead(200, { 'Content-Type': 'application/json' })
            res.end(JSON.stringify(result))
          }).catch((err: Error) => {
            console.error('[dev-sqlite] query error:', err.message)
            res.writeHead(500, { 'Content-Type': 'application/json' })
            res.end(JSON.stringify({ error: err.message }))
          })
        })
      }

      server.middlewares.use(middleware)
    },
  }
}

export default defineConfig({
  plugins: [devSqlitePlugin(), vue(), viteSingleFile()],
  resolve: {
    alias: {
      '@': fileURLToPath(new URL('./src', import.meta.url)),
      // Chromium 138+ (WebView2 1.0.3912 = Chromium 147) ships Temporal natively.
      // @hebcal/core imports temporal-polyfill/global which adds ~118KB to the bundle.
      // Stub it out — the native Temporal object is already available.
      'temporal-polyfill/global': fileURLToPath(new URL('./src/stubs/temporal-polyfill-stub.ts', import.meta.url)),
    },
  },
  optimizeDeps: {
    // Exclude large packages that tree-shake well from dep pre-bundling.
    // Including them in the pre-bundle means the browser downloads and parses the
    // entire package on cold start. Excluding lets Vite serve only the symbols
    // actually imported, as individual transformed modules.
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
})
