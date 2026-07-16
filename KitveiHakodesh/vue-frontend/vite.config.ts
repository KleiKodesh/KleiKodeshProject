import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'
import { viteSingleFile } from 'vite-plugin-singlefile'
import type { Plugin } from 'vite'
import path from 'node:path'
import { fileURLToPath as toPath } from 'node:url'
import net from 'node:net'
import { spawn, exec, execFile, type ChildProcess } from 'node:child_process'
import { encode as mpEncode } from '@msgpack/msgpack'

// ── KitveiHakodesh service (named-pipe RPC courier) ────────────────────────────
// The clean data path: the dev server spawns the .NET service and forwards the
// app's {op,args} envelopes to it over the KitveiHakodesh pipe. The service owns
// all backend knowledge (DocumentLocator delegation now; SQLite/FTS later) — this
// middleware is a dumb proxy with no per-op logic.
const KHS_PIPE = '\\\\.\\pipe\\KitveiHakodesh'
const KHS_PROJECT = path.resolve(
  path.dirname(toPath(import.meta.url)),
  '../CSharpBackend/KitveiHakodeshService/KitveiHakodeshService.csproj',
)
const KHS_CONNECT_TIMEOUT_MS = 2_000
const KHS_STARTUP_TIMEOUT_MS = 120_000 // first `dotnet run` may build from cold
const KHS_STARTUP_POLL_MS = 500

/** One request/response round-trip over a named pipe using 4-byte LE length framing. */
function callPipe(pipePath: string, requestBody: Buffer, connectTimeoutMs: number): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(pipePath)
    const connectTimer = setTimeout(() => {
      socket.destroy()
      reject(Object.assign(new Error('pipe connection timed out'), { code: 'ETIMEDOUT' }))
    }, connectTimeoutMs)

    let responseBuffer = Buffer.alloc(0)
    let expectedLength = -1

    socket.on('connect', () => {
      clearTimeout(connectTimer)
      // The channel is MessagePack now — frame the raw binary body verbatim (4-byte LE length).
      const header = Buffer.alloc(4)
      header.writeInt32LE(requestBody.length, 0)
      socket.write(Buffer.concat([header, requestBody]))
    })

    socket.on('data', (chunk) => {
      responseBuffer = Buffer.concat([responseBuffer, chunk])
      if (expectedLength === -1 && responseBuffer.length >= 4) {
        expectedLength = responseBuffer.readInt32LE(0)
      }
      if (expectedLength !== -1 && responseBuffer.length >= 4 + expectedLength) {
        const responseBody = responseBuffer.subarray(4, 4 + expectedLength)
        socket.destroy()
        resolve(responseBody)
      }
    })

    socket.on('error', (err) => {
      clearTimeout(connectTimer)
      reject(err)
    })
  })
}

/** Build a MessagePack request frame body: { Op, Args } with Args = nested msgpack of the args
 *  object (PascalCase keys to match the service's keyAsPropertyName DTOs). */
function encodeKhsRequest(op: string, args: unknown): Buffer {
  return Buffer.from(mpEncode({ Op: op, Args: mpEncode(args ?? {}) }))
}

/** True if something is already answering on the KitveiHakodesh pipe. */
function khsPipeIsUp(): Promise<boolean> {
  return new Promise((resolve) => {
    const socket = net.createConnection(KHS_PIPE)
    socket.setTimeout(300)
    socket.on('connect', () => { socket.destroy(); resolve(true) })
    socket.on('timeout', () => { socket.destroy(); resolve(false) })
    socket.on('error', () => resolve(false))
  })
}

function waitForKhsPipe(): Promise<void> {
  return new Promise((resolve, reject) => {
    const deadline = Date.now() + KHS_STARTUP_TIMEOUT_MS
    const attempt = async () => {
      if (await khsPipeIsUp()) { resolve(); return }
      if (Date.now() >= deadline) { reject(new Error('KitveiHakodesh service did not start in time')); return }
      setTimeout(attempt, KHS_STARTUP_POLL_MS)
    }
    void attempt()
  })
}

let khsProc: ChildProcess | null = null
// Absolute seforim.db path forwarded to the service so it queries the same DB the
// dev-sqlite worker uses. Set in configureServer before the service is spawned.
let khsDbPath: string | undefined
// Absolute FTS index dir forwarded to the service for full-text search (optional).
let khsFtsIndexPath: string | undefined

/**
 * Ask a running service to shut down GRACEFULLY over the pipe, so it cancels its
 * background FTS build cleanly (aborts any in-flight merge, releases the index write
 * lock) and leaves the index resumable — never hard-killed mid-merge, which risks
 * index corruption. Returns once the service has exited (pipe down) or times out.
 */
async function gracefulStopKhs(): Promise<void> {
  if (!(await khsPipeIsUp())) return
  console.log('[khs] asking service to shut down gracefully (safe for the FTS index)...')
  try {
    await callPipe(KHS_PIPE, encodeKhsRequest('shutdown', {}), KHS_CONNECT_TIMEOUT_MS)
  } catch { /* it may drop the connection as it exits — expected */ }
  const deadline = Date.now() + 30_000
  while (Date.now() < deadline) {
    if (!(await khsPipeIsUp())) { console.log('[khs] service stopped cleanly'); return }
    await new Promise((r) => setTimeout(r, 300))
  }
  console.warn('[khs] graceful shutdown timed out — will force-kill')
}

/**
 * Kill EVERY KitveiHakodesh service process — the `dotnet run` host and the apphost
 * exe — from this or any prior dev session, then wait for them to actually exit (so
 * the exe is unlocked and `dotnet run` can rebuild it). Only a fallback for anything
 * that ignored the graceful shutdown; call gracefulStopKhs() first.
 */
function killKhsProcesses(): Promise<void> {
  if (process.platform !== 'win32') return Promise.resolve()
  return new Promise((resolve) => {
    // execFile (no shell) avoids cmd.exe quote-mangling of the pipe/braces below.
    const ps = [
      "$t = Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'KitveiHakodeshService.exe' -or ($_.Name -eq 'dotnet.exe' -and $_.CommandLine -like '*KitveiHakodeshService*') };",
      "$t | ForEach-Object { Stop-Process -Id $_.ProcessId -Force -ErrorAction SilentlyContinue };",
      "$ids = @($t.ProcessId);",
      "if ($ids.Count -gt 0) { $end = (Get-Date).AddSeconds(5); while ((Get-Date) -lt $end) { if (-not (Get-Process -Id $ids -ErrorAction SilentlyContinue)) { break }; Start-Sleep -Milliseconds 150 } }",
    ].join(' ')
    execFile('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ps], () => resolve())
  })
}

/**
 * Ensure exactly one, freshly-built service is running. Always kills any existing
 * instance first (so C# edits take effect on every dev launch), then spawns a new
 * one via `dotnet run` — which rebuilds. Never leaves duplicates behind.
 */
async function ensureKhsService(): Promise<void> {
  if (khsProc) {
    // A spawn from this session is already in flight — don't start a second.
    console.log('[khs] service is already starting — waiting for it')
    await waitForKhsPipe()
    return
  }
  // Replace any running/orphaned service with a fresh build every launch — but stop
  // the old one GRACEFULLY first so its FTS build isn't hard-killed mid-merge (which
  // corrupts the index). Force-kill only stragglers that ignored the graceful stop.
  console.log('[khs] stopping any existing service instance...')
  await gracefulStopKhs()
  await killKhsProcesses()
  console.log('[khs] starting fresh service via `dotnet run`...')
  khsProc = spawn('dotnet', ['run', '--project', KHS_PROJECT, '-c', 'Debug'], {
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
    env: {
      ...process.env,
      ...(khsDbPath ? { DB_PATH: khsDbPath } : {}),
      ...(khsFtsIndexPath ? { FTS_INDEX_PATH: khsFtsIndexPath } : {}),
    },
  })
  khsProc.stdout?.on('data', (d: Buffer) => process.stdout.write(`[khs] ${d}`))
  khsProc.stderr?.on('data', (d: Buffer) => process.stderr.write(`[khs] ${d}`))
  khsProc.on('exit', (code) => { console.log(`[khs] service exited (${code})`); khsProc = null })
  await waitForKhsPipe()
  console.log('[khs] service ready')
}

/** Kill the spawned service (and its child `dotnet` host) on dev-server shutdown. */
function stopKhsService(): void {
  const pid = khsProc?.pid
  khsProc = null
  if (!pid) return
  // Graceful stop first (so the FTS build isn't hard-killed mid-merge), then force-kill
  // the tree as a fallback. Best-effort on dev-server close.
  void gracefulStopKhs().finally(() => exec(`taskkill /pid ${pid} /T /F`, () => {}))
}

function devSqlitePlugin(): Plugin {
  let khsReady: Promise<void> | null = null

  return {
    name: 'dev-sqlite',
    apply: 'serve',
    enforce: 'pre',

    async handleHotUpdate(ctx) {
      // Ensure the plugin is alive
      return ctx.modules
    },

    configureServer(server) {
      const env = loadEnv('development', process.cwd(), '')
      const dbPath = process.env.DB_PATH ?? env.DB_PATH ?? './data.db'

      // Forward the resolved (absolute) seforim.db path to the service. The service
      // derives the user_settings.db path from it ({dbDir}/Settings/user_settings.db).
      khsDbPath = path.resolve(dbPath)

      // Optional: forward the FTS index dir (built by the app) for full-text search.
      const ftsIndexPath = process.env.FTS_INDEX_PATH ?? env.FTS_INDEX_PATH ?? ''
      khsFtsIndexPath = ftsIndexPath ? path.resolve(ftsIndexPath) : undefined

      // Start the KitveiHakodesh service alongside the dev server. Kicked off here
      // (not awaited) so the dev server comes up immediately; /khs awaits khsReady.
      khsReady = ensureKhsService().catch((err) => {
        console.error('[khs] failed to start service:', err.message)
      })

      server.httpServer?.on('close', () => stopKhsService())

      // Wrap middleware registration to ensure it runs after all built-in Vite middleware is set up
      console.log('[dev-sqlite] registering API middleware')
      server.middlewares.use((req: any, res: any, next: any) => {
        if (req.url?.startsWith('/pdfjs/')) {
          res.setHeader('Cache-Control', 'no-store')
        }

        if (req.method !== 'POST') {
          next()
          return
        }

        // KitveiHakodesh service RPC — dumb proxy: forward the {op,args} envelope
        // to the service pipe and return its {ok,...} reply verbatim.
        if (req.url === '/khs') {
          // Dumb binary courier: the request body is a MessagePack frame, forwarded verbatim
          // over the pipe; the reply bytes are returned verbatim. No per-op logic, no parsing —
          // encoding/decoding lives entirely in the frontend serviceClient and the service.
          const chunks: Buffer[] = []

          req.on('data', (chunk: Buffer | string) => {
            chunks.push(typeof chunk === 'string' ? Buffer.from(chunk) : chunk)
          })

          req.on('error', () => {
            if (!res.headersSent) { res.writeHead(400); res.end('request error') }
          })

          req.on('end', async () => {
            const body = Buffer.concat(chunks)
            try {
              if (khsReady) await khsReady
              let response: Buffer
              try {
                response = await callPipe(KHS_PIPE, body, KHS_CONNECT_TIMEOUT_MS)
              } catch {
                // Service may have died mid-session — try to (re)start once, then retry.
                khsReady = ensureKhsService()
                await khsReady
                response = await callPipe(KHS_PIPE, body, KHS_CONNECT_TIMEOUT_MS)
              }
              if (!res.headersSent) {
                res.writeHead(200, { 'Content-Type': 'application/octet-stream' })
                res.end(response)
              }
            } catch (err: any) {
              console.error('[khs] request error:', err?.message)
              // HTTP error status → the frontend serviceClient throws on !res.ok (it never
              // tries to msgpack-decode a non-200 body).
              if (!res.headersSent) { res.writeHead(503); res.end(err?.message || 'service unavailable') }
            }
          })
          return
        }

        // All other DB access (seforim, dictionary, user-settings) also goes through
        // /khs — there is no dev SQLite worker anymore.
        next()
      })
    },
  }
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
    // The @iconify-prerendered/* packages ship as a SINGLE ~12.5MB index.js barrel
    // (not one-file-per-icon), so EXCLUDING them from pre-bundling does NOT serve
    // "only the imported symbols" — it makes Vite serve the whole barrel raw, which
    // its dev transform inflates to ~81MB with `Cache-Control: no-cache`, re-served
    // and re-parsed on every cold reload (~5s blank screen).
    // Pre-bundling (include) does NOT tree-shake the barrel down to used icons
    // (esbuild keeps the full re-export set → ~13.5MB), but it converts it ONCE at
    // startup into an immutable, long-cached .vite/deps chunk served in ~0.25s and
    // reused across restarts. Prod (vite build via rollup) DOES tree-shake to the
    // ~90 icons actually imported, so this only affects dev.
    include: [
      '@iconify-prerendered/vue-fluent',
      '@iconify-prerendered/vue-fluent-color',
    ],
    // tesseract.js is lazy-loaded (wasm + workers) and must stay unbundled.
    exclude: ['tesseract.js'],
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
