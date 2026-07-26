import { defineConfig, loadEnv } from 'vite'
import vue from '@vitejs/plugin-vue'
import { fileURLToPath, URL } from 'node:url'
import { viteSingleFile } from 'vite-plugin-singlefile'
import type { Plugin } from 'vite'
import path from 'node:path'
import fs from 'node:fs'
import { fileURLToPath as toPath } from 'node:url'
import net from 'node:net'
import http from 'node:http'
import { spawn, exec, type ChildProcess } from 'node:child_process'
import { encode as mpEncode, decode as mpDecode } from '@msgpack/msgpack'

// ── KitveiHakodesh service (loopback HTTP host + private pipe handshake) ────────
// The clean data path: this dev server spawns ITS OWN .NET service instance, which
// hosts a MessagePack RPC over http://127.0.0.1:<port> on an OS-assigned free port.
// That port is PRIVATE — the service never writes it to a file or stdout; this dev
// server learns it over the service's named pipe (the getHttpPort op), which is
// ACL'd to our user. The BROWSER then talks to the HTTP host DIRECTLY (serviceClient
// discovers the base via /khs-endpoint) — node is not in the data path.
//
// Isolation: each dev server uses a UNIQUE pipe name (its pid), so it reaches ITS
// instance and never reuses/kills another dev server's or app's service. The service
// is tied to our lifetime via KHS_OWNER_PID (Http/OwnerWatcher) so it can't orphan.
const KHS_HOST = '127.0.0.1'
// Unique per dev-server PROCESS (stable across in-process config reloads). Any spawner
// picks its own name; the service reads it from KHS_PIPE_NAME.
const KHS_PIPE_NAME = `KitveiHakodesh.${process.pid}`
const KHS_PIPE = `\\\\.\\pipe\\${KHS_PIPE_NAME}`
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

/** One request/response round-trip over OUR private pipe using 4-byte LE length framing.
 *  Used only for the control plane (getHttpPort, shutdown) — data goes browser↔HTTP. */
function callPipe(requestBody: Buffer, connectTimeoutMs = KHS_CONNECT_TIMEOUT_MS): Promise<Buffer> {
  return new Promise((resolve, reject) => {
    const socket = net.createConnection(KHS_PIPE)
    let connected = false
    const timer = setTimeout(() => {
      socket.destroy()
      reject(Object.assign(new Error('pipe connection timed out'), { code: 'ETIMEDOUT', connected }))
    }, connectTimeoutMs)
    let resp = Buffer.alloc(0)
    let expected = -1
    socket.on('connect', () => {
      connected = true
      clearTimeout(timer)
      const header = Buffer.alloc(4)
      header.writeInt32LE(requestBody.length, 0)
      socket.write(Buffer.concat([header, requestBody]))
    })
    socket.on('data', (chunk) => {
      resp = Buffer.concat([resp, chunk])
      if (expected === -1 && resp.length >= 4) expected = resp.readInt32LE(0)
      if (expected !== -1 && resp.length >= 4 + expected) {
        const body = resp.subarray(4, 4 + expected)
        socket.destroy()
        resolve(body)
      }
    })
    socket.on('error', (err) => { clearTimeout(timer); reject(Object.assign(err as Error, { connected })) })
  })
}

/** Encode a control-op request frame: { Op, Args } (Args = nested msgpack, PascalCase keys). */
function encodeReq(op: string, args: unknown = {}): Buffer {
  return Buffer.from(mpEncode({ Op: op, Args: mpEncode(args ?? {}) }))
}

/** Decode the { Ok, Result?, Error? } envelope and return the nested-msgpack Result decoded. */
function decodeResult<T = any>(body: Buffer): T {
  const env = mpDecode(body) as { Ok: boolean; Result?: Uint8Array; Error?: string }
  if (!env.Ok) throw new Error(env.Error || 'service error')
  return (env.Result && env.Result.length ? mpDecode(env.Result) : undefined) as T
}

/** True if OUR service instance is answering on its private pipe. */
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

/** Ask the service (over the private pipe) for its loopback HTTP port + bearer token. Retries
 *  briefly to ride out the pipe accept-loop gap / boot window. This is the ONLY way the
 *  port/token leave the service — never a file, never stdout. */
async function fetchHttpEndpointOverPipe(attempts = 8): Promise<{ port: number; token: string }> {
  let lastErr: any
  for (let i = 0; i < attempts; i++) {
    try {
      const { Port, Token } = decodeResult<{ Port: number; Token: string }>(
        await callPipe(encodeReq('getHttpPort')))
      if (Port > 0 && Token) return { port: Port, token: Token }
    } catch (err) { lastErr = err }
    await new Promise((r) => setTimeout(r, 40 + i * 40))
  }
  throw lastErr ?? new Error('service did not report an HTTP endpoint')
}

/** Graceful `shutdown` over the private pipe: the service cancels its FTS build cleanly
 *  (aborts any merge, releases the index lock) and exits, freeing its HTTP port. */
async function pipeShutdown(): Promise<void> {
  try { await callPipe(encodeReq('shutdown')) } catch { /* it drops the pipe as it exits — expected */ }
}

let khsProc: ChildProcess | null = null
// This dev server's own service; `khsReady` gates startup. Module-level so the child's `exit`
// handler can reassign it when respawning.
let khsReady: Promise<void> | null = null
// True while node is intentionally stopping the service (rebuild/dev-close) — suppresses the
// exit-handler's proactive respawn so we don't race our own (re)start.
let khsManagedStop = false
// True once the dev server is closing for good — no respawn from here on.
let khsShuttingDown = false
// Crash-loop guard for proactive respawn.
let khsRespawnStrikes = 0
let khsLastRespawn = 0
// OUR instance's loopback HTTP port + bearer token, learned from the service over the private
// pipe after spawn (0/'' until known). Handed to the browser via the /khs-endpoint dev route.
// The token is the endpoint's real security boundary: the HTTP host 401s any request without
// it, so a local port-scanner or a malicious web page can't use the service even if it finds
// the port. (/khs-endpoint itself is guarded by Vite's default dev CORS — localhost origins
// only — and its allowedHosts DNS-rebinding protection.)
let khsHttpPort = 0
let khsHttpToken = ''
// Absolute seforim.db path forwarded to the service so it queries the same DB the app uses.
// Set in configureServer before the service is spawned.
let khsDbPath: string | undefined
// Absolute FTS index dir forwarded to the service for full-text search (optional).
let khsFtsIndexPath: string | undefined

/**
 * Ask OUR service instance to shut down GRACEFULLY (via the private-pipe `shutdown` op), so it
 * cancels its background FTS build cleanly (aborts any in-flight merge, releases the index write
 * lock) and leaves the index resumable — never hard-killed mid-merge, which risks index
 * corruption. Returns once the service has exited (pipe down) or times out.
 */
async function gracefulStopKhs(): Promise<void> {
  if (!(await khsPipeIsUp())) return
  console.log('[khs] asking service to shut down gracefully (safe for the FTS index)...')
  await pipeShutdown()
  const deadline = Date.now() + 30_000
  while (Date.now() < deadline) {
    if (!(await khsPipeIsUp())) { console.log('[khs] service stopped cleanly'); return }
    await new Promise((r) => setTimeout(r, 300))
  }
  console.warn('[khs] graceful shutdown timed out — will force-kill')
}

/**
 * Force-kill ONLY the service instance THIS dev server spawned (its pid tree) — never a
 * blanket kill-by-name, which would tear down a concurrent dev server's or app's own instance.
 * Fallback for when graceful shutdown was ignored. Orphans from a crashed dev server are handled
 * on the service side by Http/OwnerWatcher (KHS_OWNER_PID), not here.
 */
function killOwnKhs(): Promise<void> {
  const pid = khsProc?.pid
  if (!pid || process.platform !== 'win32') return Promise.resolve()
  return new Promise((resolve) => { exec(`taskkill /pid ${pid} /T /F`, () => resolve()) })
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

/** Spawn the prebuilt Release exe DIRECTLY (no `dotnet run` host), wait for its private pipe,
 *  then learn its OS-assigned HTTP port over that pipe.
 *   • KHS_PIPE_NAME — our unique per-dev-server pipe, so we reach OUR instance.
 *   • KHS_OWNER_PID — lets the service self-clean if this dev server dies without a graceful
 *     stop (Http/OwnerWatcher), so no orphaned port is ever left behind. */
async function spawnKhsExe(): Promise<void> {
  console.log('[khs] starting service (prebuilt exe)...')
  khsProc = spawn(KHS_EXE, [], {
    stdio: ['ignore', 'pipe', 'pipe'],
    windowsHide: true,
    env: {
      ...process.env,
      ...(khsDbPath ? { DB_PATH: khsDbPath } : {}),
      ...(khsFtsIndexPath ? { FTS_INDEX_PATH: khsFtsIndexPath } : {}),
      KHS_PIPE_NAME: KHS_PIPE_NAME,
      KHS_OWNER_PID: String(process.pid),
    },
  })
  khsProc.stdout?.on('data', (d: Buffer) => process.stdout.write(`[khs] ${d}`))
  khsProc.stderr?.on('data', (d: Buffer) => process.stderr.write(`[khs] ${d}`))
  // Detect elevation failure: if the exe has requireAdministrator in its manifest and
  // this terminal is not elevated, spawn succeeds but the process exits immediately with
  // code null or a Windows error code. Surface a clear message instead of a cryptic hang.
  khsProc.on('error', (err: NodeJS.ErrnoException) => {
    if (err.code === 'UNKNOWN' || (err as any).errno === -4094 /* ERROR_ELEVATION_REQUIRED */) {
      console.error(
        '[khs] ERROR: KitveiHakodeshService requires Administrator privileges (for NTFS USN journal access).\n' +
        '      Restart your terminal as Administrator and run npm run dev again.',
      )
    }
  })
  khsProc.on('exit', (code) => {
    console.log(`[khs] service exited (${code})`)
    khsProc = null
    khsHttpPort = 0; khsHttpToken = '' // stale — the browser re-discovers via /khs-endpoint
    // Node isn't in the data path, so it can't lazily restart the service on a failed request.
    // Supervise it instead: respawn on an UNMANAGED exit — the service's own setSeforimDbPath
    // self-restart, or a crash — so the host (on a fresh port) comes back.
    if (khsShuttingDown || khsManagedStop) return
    const now = Date.now()
    khsRespawnStrikes = now - khsLastRespawn < 3000 ? khsRespawnStrikes + 1 : 0
    khsLastRespawn = now
    if (khsRespawnStrikes >= 5) {
      console.error('[khs] service is crash-looping — not respawning (fix it, then restart dev)')
      return
    }
    setTimeout(() => {
      if (khsShuttingDown || khsManagedStop || khsProc) return
      console.log('[khs] service went down — respawning')
      khsReady = ensureKhsService().catch((e: any) => console.error('[khs] respawn failed:', e?.message))
    }, 400)
  })
  await waitForKhsPipe()
  ;({ port: khsHttpPort, token: khsHttpToken } = await fetchHttpEndpointOverPipe())
  console.log(`[khs] service ready — HTTP host on http://${KHS_HOST}:${khsHttpPort} (endpoint learned over the private pipe)`)
}

/**
 * Ensure exactly one, up-to-date service is running — with the minimum work:
 *   • REUSE   — if a healthy service is already answering and no source changed,
 *               keep it (near-instant; C# edits force a rebuild+restart instead).
 *   • REBUILD — only when a service/FtsLib source file is newer than the built exe.
 *   • RESPAWN — spawn the PREBUILT exe directly (~385ms), never `dotnet run` (~4s).
 * "Existing" means OUR OWN instance (its private pid-based pipe): another dev server's or
 * app's service has a different pipe name, so we never see, reuse, or kill it. Any restart
 * stops our old instance GRACEFULLY first (so its FTS build isn't hard-killed mid-merge).
 */
async function ensureKhsService(): Promise<void> {
  if (khsProc) {
    // A spawn from this session is already in flight — don't start a second.
    console.log('[khs] service is already starting — waiting for it')
    await waitForKhsPipe()
    if (!khsHttpPort || !khsHttpToken) ({ port: khsHttpPort, token: khsHttpToken } = await fetchHttpEndpointOverPipe())
    return
  }

  const needsBuild = serviceNeedsBuild()
  const alreadyUp = await khsPipeIsUp()

  // Fast path: OUR instance is already answering and nothing changed → reuse it. The common
  // case for an in-process config reload; costs a single pipe probe. (Re-learn the port if a
  // config reload dropped our khsHttpPort but the instance is still up.)
  if (alreadyUp && !needsBuild) {
    console.log('[khs] our service instance is up and current — reusing it')
    if (!khsHttpPort || !khsHttpToken) ({ port: khsHttpPort, token: khsHttpToken } = await fetchHttpEndpointOverPipe())
    return
  }

  // We must (re)start. Mark the stop as MANAGED so the exit handler doesn't also try to
  // respawn underneath us. Stop OUR existing instance GRACEFULLY first, then force-kill it,
  // so the exe is unlocked (a rebuild would fail to overwrite it otherwise).
  try {
    khsManagedStop = true
    if (alreadyUp) {
      console.log('[khs] stopping our existing service instance...')
      await gracefulStopKhs()
    }
    await killOwnKhs()

    if (needsBuild) {
      await buildKhsService()
    } else {
      console.log('[khs] service is up-to-date — skipping build')
    }

    await spawnKhsExe()
  } finally {
    khsManagedStop = false
  }
}

/** Stop OUR service instance on dev-server shutdown. */
function stopKhsService(): void {
  khsShuttingDown = true // stop the exit handler from respawning
  const pid = khsProc?.pid
  khsProc = null
  // Graceful stop first (so the FTS build isn't hard-killed mid-merge); the service's
  // OwnerWatcher also self-stops once this process dies, but we ask nicely first. Force-kill
  // our own pid as a fallback. Best-effort on dev-server close.
  void gracefulStopKhs().finally(() => { if (pid) exec(`taskkill /pid ${pid} /T /F`, () => {}) })
}

function devSqlitePlugin(): Plugin {
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

      // Spawn + supervise THIS dev server's own service instance. The browser talks to its
      // HTTP host directly, so node is NOT in the data path — it only owns the service's
      // lifecycle + the one-time port handoff. Kicked off (not awaited) so dev comes up now.
      khsReady = ensureKhsService().catch((err) => {
        console.error('[khs] failed to start service:', err.message)
      })

      server.httpServer?.on('close', () => stopKhsService())

      server.middlewares.use((req: any, res: any, next: any) => {
        // Endpoint discovery: the browser asks the dev server (same-origin) where our
        // service's HTTP host is and for the bearer token every data request must carry.
        // Both were learned PRIVATELY over the pipe — never a file. This route is shielded
        // by Vite's default dev CORS (localhost origins only) + allowedHosts (DNS-rebinding
        // guard), so an external web page can't read it. 503 until the service has reported
        // its endpoint (the browser client retries).
        if (req.url === '/khs-endpoint') {
          res.setHeader('Cache-Control', 'no-store')
          // Only advertise when OUR service child is actually alive — never serve a stale
          // port/token from a dead instance (e.g. mid-respawn, or after the crash-loop guard
          // gave up). 503 → the browser client retries until a healthy instance is back.
          if (khsProc && khsHttpPort > 0 && khsHttpToken) {
            res.setHeader('Content-Type', 'application/json')
            res.end(JSON.stringify({ base: `http://${KHS_HOST}:${khsHttpPort}`, token: khsHttpToken }))
          } else {
            res.statusCode = 503
            res.end('{"error":"service not ready"}')
          }
          return
        }

        // SAME-ORIGIN streaming proxy for local files → pdf.js loads them by URL and
        // range-fetches (progressive; never the whole file in memory). Serving via this vite
        // route (not the service's cross-origin port directly) keeps the viewer's file URL
        // same-origin — it passes pdf.js's file-origin check with no viewer patch and no CORS.
        // Node PIPES the service's response (forwarding Range/206), so it holds no file bytes.
        // The service's GET /file is capability-gated by the ?h= handle (minted only via the
        // token-gated openLocalFile op), so no token is needed on this hop.
        if (req.method === 'GET' && typeof req.url === 'string' && req.url.startsWith('/khs-file/')) {
          // Handle rides in the PATH (hex, URL-safe) — survives pdf.js's file= param round-trip.
          // URL form 1: /khs-file/<fileHandle>           — single-file grant (PDF etc.)
          // URL form 2: /khs-file/<folderHandle>/rel/path — folder grant (HTML + siblings)
          // In both cases the service path is /file/<rest-of-url-after-/khs-file/>.
          const rest = req.url.slice('/khs-file/'.length).split('?')[0]
          if (!khsHttpPort || !rest) { res.writeHead(502); res.end(); return }
          const headers: Record<string, string> = {}
          if (req.headers.range) headers.Range = req.headers.range as string
          const proxy = http.request(
            { host: KHS_HOST, port: khsHttpPort, path: `/file/${rest}`, method: 'GET', headers },
            (pres) => { res.writeHead(pres.statusCode || 502, pres.headers); pres.pipe(res) },
          )
          proxy.on('error', () => { if (!res.headersSent) { res.writeHead(502); res.end() } })
          proxy.end()
          return
        }

        // Don't cache pdf.js assets. All DATA flows browser → service HTTP host directly.
        if (req.url?.startsWith('/pdfjs/')) res.setHeader('Cache-Control', 'no-store')
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
