import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'
import { viteSingleFile } from 'vite-plugin-singlefile'
import type { Plugin } from 'vite'
import path from 'node:path'
import fs from 'node:fs'
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
const KHS_DIR = path.resolve(
  path.dirname(toPath(import.meta.url)),
  '../CSharpBackend/KitveiHakodeshService',
)
const KHS_PROJECT = path.join(KHS_DIR, 'KitveiHakodeshService.csproj')
// The FtsLib project reference — its source affects the built exe, so it counts
// toward the "needs rebuild?" staleness check below.
const KHS_FTSLIB_DIR = path.resolve(
  path.dirname(toPath(import.meta.url)),
  '../CSharpBackend/Ftslib-Csharp/FtsLib',
)
// The already-built Release exe. We spawn THIS directly (not `dotnet run`): a warm
// `dotnet run` costs ~4s to pipe-ready (SDK host + MSBuild up-to-date check on every
// launch) versus ~385ms for the prebuilt exe — a ~3.6s tax paid on every dev start.
const KHS_EXE = path.join(KHS_DIR, 'bin', 'Release', 'net10.0', 'KitveiHakodeshService.exe')
const KHS_CONNECT_TIMEOUT_MS = 2_000
const KHS_STARTUP_TIMEOUT_MS = 120_000 // a cold rebuild can take a while
const KHS_STARTUP_POLL_MS = 250

/** One request/response round-trip over a named pipe using 4-byte LE length framing.
 *  A rejected error carries `.connected` = whether the socket ever connected, so callers
 *  can tell a (retryable) pre-connect failure from a mid-stream one. */
function callPipe(pipePath: string, requestBody: Buffer, connectTimeoutMs: number): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(pipePath)
    let connected = false
    const connectTimer = setTimeout(() => {
      socket.destroy()
      reject(Object.assign(new Error('pipe connection timed out'), { code: 'ETIMEDOUT', connected }))
    }, connectTimeoutMs)

    let responseBuffer = Buffer.alloc(0)
    let expectedLength = -1

    socket.on('connect', () => {
      connected = true
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
      reject(Object.assign(err as Error, { connected }))
    })
  })
}

/**
 * callPipe with a few fast retries for TRANSIENT pre-connect failures. A serial pipe
 * accept loop briefly has no listening instance between accepting one client and
 * creating the next, and during service boot the pipe doesn't exist yet — both surface
 * as ENOENT/ETIMEDOUT on connect. Retrying the connection a handful of times (short
 * backoff) rides those out WITHOUT tearing down and rebuilding a healthy service, which
 * is what the caller's `ensureKhsService()` fallback would otherwise do. A failure that
 * happens AFTER connecting is a real error and is thrown immediately (no retry).
 */
async function callPipeWithRetry(requestBody: Buffer, attempts = 6): Promise<Buffer> {
  let lastErr: any
  for (let i = 0; i < attempts; i++) {
    try {
      return await callPipe(KHS_PIPE, requestBody, KHS_CONNECT_TIMEOUT_MS)
    } catch (err: any) {
      lastErr = err
      // Only retry a pre-connect transient; a mid-stream failure means the service
      // took the request and something went wrong downstream — don't replay it.
      if (err?.connected) throw err
      await new Promise((r) => setTimeout(r, 30 + i * 40))
    }
  }
  throw lastErr
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
 * Newest mtime (ms) among the service's own source files AND its FtsLib project
 * reference — everything that, if edited, means the built exe is stale. Walks the
 * two source trees skipping bin/obj. Returns 0 if nothing is found.
 */
function newestSourceMtimeMs(): number {
  let newest = 0
  const walk = (dir: string) => {
    let entries: fs.Dirent[]
    try { entries = fs.readdirSync(dir, { withFileTypes: true }) } catch { return }
    for (const e of entries) {
      if (e.isDirectory()) {
        if (e.name === 'bin' || e.name === 'obj' || e.name === '.vs') continue
        walk(path.join(dir, e.name))
      } else if (/\.(cs|csproj|json|props|targets)$/i.test(e.name)) {
        try {
          const m = fs.statSync(path.join(dir, e.name)).mtimeMs
          if (m > newest) newest = m
        } catch { /* ignore unreadable */ }
      }
    }
  }
  walk(KHS_DIR)
  walk(KHS_FTSLIB_DIR)
  return newest
}

/**
 * True when the built Release exe is missing or OLDER than the newest service/FtsLib
 * source file — i.e. an edit was made and we must rebuild. A pure-mtime check in Node
 * (a few ms) instead of `dotnet build`'s own up-to-date check (~2.3s even for a no-op),
 * so an unchanged tree skips the build entirely.
 */
function serviceNeedsBuild(): boolean {
  let exeMtime: number
  try { exeMtime = fs.statSync(KHS_EXE).mtimeMs } catch { return true } // no exe yet → build
  return newestSourceMtimeMs() > exeMtime
}

/** Build the service (Release, incremental). Resolves on success, rejects on failure. */
function buildKhsService(): Promise<void> {
  return new Promise((resolve, reject) => {
    console.log('[khs] source changed — building service (dotnet build -c Release)...')
    const t0 = Date.now()
    const proc = spawn('dotnet', ['build', KHS_PROJECT, '-c', 'Release', '-v', 'q', '--nologo'], {
      stdio: ['ignore', 'pipe', 'pipe'],
      windowsHide: true,
    })
    let tail = ''
    proc.stdout?.on('data', (d: Buffer) => { tail += d.toString(); if (tail.length > 4000) tail = tail.slice(-4000) })
    proc.stderr?.on('data', (d: Buffer) => { tail += d.toString(); if (tail.length > 4000) tail = tail.slice(-4000) })
    proc.on('error', reject)
    proc.on('exit', (code) => {
      if (code === 0) {
        console.log(`[khs] build complete (${((Date.now() - t0) / 1000).toFixed(1)}s)`)
        resolve()
      } else {
        console.error(`[khs] build FAILED (exit ${code}):\n${tail}`)
        reject(new Error(`service build failed (exit ${code})`))
      }
    })
  })
}

/** Spawn the prebuilt Release exe DIRECTLY (no `dotnet run` host) and wait for the pipe. */
async function spawnKhsExe(): Promise<void> {
  console.log('[khs] starting service (prebuilt exe)...')
  khsProc = spawn(KHS_EXE, [], {
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

/**
 * Ensure exactly one, up-to-date service is running — with the minimum work:
 *   • REUSE   — if a healthy service is already answering and no source changed,
 *               keep it (near-instant; C# edits force a rebuild+restart instead).
 *   • REBUILD — only when a service/FtsLib source file is newer than the built exe.
 *   • RESPAWN — spawn the PREBUILT exe directly (~385ms), never `dotnet run` (~4s).
 * Any restart stops the old instance GRACEFULLY first (so its FTS build isn't
 * hard-killed mid-merge, which corrupts the index), then force-kills stragglers.
 */
async function ensureKhsService(): Promise<void> {
  if (khsProc) {
    // A spawn from this session is already in flight — don't start a second.
    console.log('[khs] service is already starting — waiting for it')
    await waitForKhsPipe()
    return
  }

  const needsBuild = serviceNeedsBuild()
  const alreadyUp = await khsPipeIsUp()

  // Fast path: a service is already answering and nothing changed → reuse it as-is.
  // This is the common case for a re-run/HMR restart and costs a single pipe probe.
  if (alreadyUp && !needsBuild) {
    console.log('[khs] existing service is up and current — reusing it')
    return
  }

  // We must (re)start. Stop any existing instance GRACEFULLY first, then force-kill
  // stragglers, so the exe is unlocked (a rebuild would fail to overwrite it otherwise).
  if (alreadyUp) {
    console.log('[khs] stopping existing service instance...')
    await gracefulStopKhs()
  }
  await killKhsProcesses()

  if (needsBuild) {
    await buildKhsService()
  } else {
    console.log('[khs] service is up-to-date — skipping build')
  }

  await spawnKhsExe()
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
        if (req.url === '/khs-stream') {
          // Streaming courier: the service PUSHES many length-prefixed frames over one
          // pipe connection (search results, indexing progress); every incoming pipe byte
          // is forwarded verbatim into a chunked HTTP response — frames keep their 4-byte
          // LE prefixes so the browser can split them. Closing the HTTP request destroys
          // the pipe socket, which is the service's cancel signal. No polling anywhere.
          const streamChunks: Buffer[] = []
          req.on('data', (chunk: Buffer | string) => {
            streamChunks.push(typeof chunk === 'string' ? Buffer.from(chunk) : chunk)
          })
          req.on('error', () => {
            if (!res.headersSent) { res.writeHead(400); res.end('request error') }
          })
          req.on('end', async () => {
            const body = Buffer.concat(streamChunks)

            // Connect directly (NO pre-probe: a probe connection consumes the pipe's
            // listening instance and races the real connect straight into ENOENT).
            // Resolves once connected + headers/request are on the wire; from then on
            // the socket streams into the response until the service closes it.
            const openStream = () =>
              new Promise<void>((resolve, reject) => {
                const socket = net.createConnection(KHS_PIPE)
                let connected = false
                const connectTimer = setTimeout(
                  () => socket.destroy(Object.assign(new Error('pipe connect timeout'), { code: 'ETIMEDOUT' })),
                  KHS_CONNECT_TIMEOUT_MS,
                )
                socket.on('connect', () => {
                  connected = true
                  clearTimeout(connectTimer)
                  res.writeHead(200, { 'Content-Type': 'application/octet-stream', 'Cache-Control': 'no-cache' })
                  res.flushHeaders?.()
                  const header = Buffer.alloc(4)
                  header.writeInt32LE(body.length, 0)
                  socket.write(Buffer.concat([header, body]))
                  // Client gone (tab closed, search superseded via AbortController) → cancel.
                  res.on('close', () => socket.destroy())
                  resolve()
                })
                socket.on('data', (chunk) => { res.write(chunk) })
                socket.on('close', () => { if (connected) res.end() })
                socket.on('error', (err) => {
                  clearTimeout(connectTimer)
                  if (!connected) reject(err)   // pre-connect failure → caller may retry
                  else res.end()                // mid-stream failure → just end the response
                })
              })

            try {
              if (khsReady) await khsReady
              try {
                await openStream()
              } catch {
                // Service may have died / be restarting — (re)start once, then retry.
                khsReady = ensureKhsService()
                await khsReady
                await openStream()
              }
            } catch (err: any) {
              console.error('[khs] stream error:', err?.message)
              if (!res.headersSent) { res.writeHead(503); res.end(err?.message || 'service unavailable') }
            }
          })
          return
        }

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
                // Ride out transient pre-connect ENOENT (accept-loop gap / boot window)
                // WITHOUT restarting a healthy service.
                response = await callPipeWithRetry(body)
              } catch {
                // Retries exhausted — the service is genuinely down. (Re)start it, then retry.
                khsReady = ensureKhsService()
                await khsReady
                response = await callPipeWithRetry(body)
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
        './src/layout/AddressBar.vue',
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
