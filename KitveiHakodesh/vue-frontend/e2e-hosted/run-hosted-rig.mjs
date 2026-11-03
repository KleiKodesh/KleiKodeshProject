#!/usr/bin/env node
/**
 * Hosted end-to-end rig: drives the REAL desktop host (KitveiHakodeshDemoApp, WebView2) through
 * the real relay -> service -> Core path with Playwright over CDP. See README.md beside this file.
 *
 *   node e2e-hosted/run-hosted-rig.mjs [--host-dir <folder>] [--keep] [--doc <file.docx>] [--pictures <file.docx>]
 *
 * Isolation (never touches the user's app, service, data or port 5173):
 *   - everything lives in a fresh temp folder (host build, frontend build, service shadow copy,
 *     WebView2 user data, host profile files) that is deleted at the end;
 *   - the host runs in test mode (KH_TEST_CDP_PORT, KH_TEST_PROFILE_DIR - see
 *     KitveiHakodeshLib/HostTestMode.cs): no single-instance handoff, DevTools on a free port;
 *   - the service is OUR console copy on a private pipe (KHS_PIPE_NAME=KitveiHakodesh.rig-<pid>),
 *     which the launcher never elevates or replaces and the installed service leaves alone;
 *   - only the pids this script started are killed (with their child trees).
 */
import { spawn, spawnSync } from 'node:child_process'
import fs from 'node:fs'
import net from 'node:net'
import os from 'node:os'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import { chromium } from 'playwright'

const here = path.dirname(fileURLToPath(import.meta.url))
const frontendDir = path.resolve(here, '..')
const backendDir = path.resolve(frontendDir, '..', 'CSharpBackend')
const projectRoot = path.resolve(frontendDir, '..', '..')

// ── arguments ───────────────────────────────────────────────────────────────
const argv = process.argv.slice(2)
const flag = (name) => argv.includes(name)
const option = (name, fallback) => { const i = argv.indexOf(name); return i >= 0 ? argv[i + 1] : fallback }
const textDoc = path.resolve(option('--doc', path.join(projectRoot, 'FastDocView', 'samples', 'dor-hamelaktim-shabbat.docx')))
const pictureDoc = path.resolve(option('--pictures', path.join(projectRoot, 'FastDocView', 'test-data', 'apache-poi', 'VariousPictures.docx')))
const serviceBuildDir = path.resolve(option('--service-dir', path.join(backendDir, 'KitveiHakodeshService', 'bin', 'Release', 'net10.0-windows')))
const keep = flag('--keep')

const root = fs.mkdtempSync(path.join(os.tmpdir(), 'kh-hosted-rig-'))
const hostDir = path.join(root, 'host')
const log = (msg) => console.log(`[rig ${new Date().toISOString().slice(11, 19)}] ${msg}`)
const started = [] // { name, child }
const results = []

function idle(child) {
  try { os.setPriority(child.pid, os.constants.priority.PRIORITY_LOW) } catch { /* best effort */ }
}

function runStep(name, cmd, args, opts = {}) {
  log(`${name}: ${cmd} ${args.join(' ')}`)
  const r = spawnSync(cmd, args, { stdio: 'inherit', shell: false, ...opts })
  if (r.status !== 0) throw new Error(`${name} failed (exit ${r.status})`)
}

// ── 1. builds (all into the rig folder) ─────────────────────────────────────
function findMsBuild() {
  const vswhere = 'C:\\Program Files (x86)\\Microsoft Visual Studio\\Installer\\vswhere.exe'
  const r = spawnSync(vswhere, ['-latest', '-requires', 'Microsoft.Component.MSBuild', '-find', 'MSBuild\\**\\Bin\\MSBuild.exe'], { encoding: 'utf8' })
  const found = r.stdout?.split(/\r?\n/).find((l) => l.trim())
  if (!found) throw new Error('MSBuild not found (vswhere)')
  return found.trim()
}

function buildHost() {
  // VueBuildStamp points at a fresh file newer than every source, so the csproj's BuildVue
  // target skips npm run build (the rig builds its own frontend below); SyncVue still copies.
  const stamp = path.join(root, 'vue.stamp')
  fs.writeFileSync(stamp, '')
  const future = new Date(Date.now() + 10 * 365 * 86_400_000) // newer than any in-progress edit
  fs.utimesSync(stamp, future, future)
  // Same trick for the library's AOT service publish: the rig runs its own service copy, and the
  // existing payload (bin/LibPayload) is only copied, never republished into the shared folder.
  const khsStamp = path.join(root, 'khs.stamp')
  fs.writeFileSync(khsStamp, '')
  fs.utimesSync(khsStamp, future, future)
  runStep('host build', findMsBuild(), [
    path.join(backendDir, 'KitveiHakodeshDemoApp', 'KitveiHakodeshDemoApp.csproj'),
    '/p:Configuration=Debug', `/p:OutDir=${hostDir}\\`, `/p:VueBuildStamp=${stamp}`, `/p:KhsPublishedExe=${khsStamp}`,
    '/v:m', '/nr:false', '/m:1', '/restore',
  ])
}

function buildFrontend() {
  // The production build plus the test hooks (VITE_KH_TEST_HOOKS=1), into the rig's host copy.
  runStep('frontend build', process.execPath, [
    path.join(frontendDir, 'node_modules', 'vite', 'bin', 'vite.js'), 'build',
    '--outDir', path.join(hostDir, 'KitveiHakodesh'), '--emptyOutDir',
  ], { cwd: frontendDir, env: { ...process.env, VITE_KH_TEST_HOOKS: '1' } })
}

function shadowService() {
  // Same shadow as the Vite dev plugin: program files only; data stays in the build (KHS_DATA_DIR).
  const dir = path.join(root, 'service')
  fs.mkdirSync(dir, { recursive: true })
  for (const e of fs.readdirSync(serviceBuildDir, { withFileTypes: true })) {
    const s = path.join(serviceBuildDir, e.name)
    if (e.isFile()) fs.copyFileSync(s, path.join(dir, e.name))
    else if (e.isDirectory() && e.name === 'runtimes') fs.cpSync(s, path.join(dir, e.name), { recursive: true })
  }
  return path.join(dir, 'KitveiHakodeshService.exe')
}

// ── 2. processes ────────────────────────────────────────────────────────────
function freePort() {
  return new Promise((resolve, reject) => {
    const s = net.createServer()
    s.unref()
    s.on('error', reject)
    s.listen(0, '127.0.0.1', () => { const { port } = s.address(); s.close(() => resolve(port)) })
  })
}

const pipeName = `KitveiHakodesh.rig-${process.pid}`
const pipeListed = () => { try { return fs.readdirSync('\\\\.\\pipe\\').some((n) => n.toLowerCase() === pipeName.toLowerCase()) } catch { return false } }

async function waitFor(what, fn, timeoutMs = 60_000, stepMs = 200) {
  const end = Date.now() + timeoutMs
  let last
  while (Date.now() < end) {
    try { last = await fn(); if (last) return last } catch (e) { last = e }
    await new Promise((r) => setTimeout(r, stepMs))
  }
  throw new Error(`timed out waiting for ${what}${last instanceof Error ? ': ' + last.message : ''}`)
}

function startProcess(name, exe, args, env) {
  log(`start ${name}: ${exe}`)
  const child = spawn(exe, args, { cwd: path.dirname(exe), env: { ...process.env, ...env }, stdio: ['ignore', 'pipe', 'pipe'], windowsHide: false })
  idle(child)
  child.stdout.on('data', (d) => process.stdout.write(`[${name}] ${d}`))
  child.stderr.on('data', (d) => process.stderr.write(`[${name}] ${d}`))
  started.push({ name, child })
  return child
}

function teardown() {
  for (const { name, child } of started.reverse()) {
    if (child.exitCode != null) continue
    log(`kill ${name} (pid ${child.pid}, tree)`)
    spawnSync('taskkill', ['/PID', String(child.pid), '/T', '/F'], { stdio: 'ignore' })
  }
  if (keep) { log(`kept ${root}`); return }
  for (let i = 0; i < 20; i++) {
    try { fs.rmSync(root, { recursive: true, force: true }); return } catch { spawnSync('cmd', ['/c', 'timeout', '/t', '1', '/nobreak'], { stdio: 'ignore' }) }
  }
  log(`could not delete ${root}`)
}

// ── 3. checks ───────────────────────────────────────────────────────────────
async function check(name, fn) {
  const t0 = Date.now()
  try {
    const detail = await fn()
    results.push({ name, ok: true, detail })
    log(`PASS ${name}${detail ? ' - ' + detail : ''} (${Date.now() - t0} ms)`)
  } catch (e) {
    results.push({ name, ok: false, detail: e.message })
    log(`FAIL ${name} - ${e.message}`)
  }
}

const fdvState = (page) => page.evaluate(() => window.__fastDocViewTest?.state() ?? null)

/** A copy of the picture docx whose PNG part is gone: the reference stays, the bytes do not. */
function makeMissingPictureDoc() {
  const target = path.join(root, 'missing-picture.docx')
  const ps = `Add-Type -AssemblyName System.IO.Compression.FileSystem
Copy-Item -LiteralPath '${pictureDoc}' -Destination '${target}'
$z = [System.IO.Compression.ZipFile]::Open('${target}', 'Update')
$z.Entries | Where-Object { $_.FullName -like 'word/media/*.png' } | ForEach-Object { $_.Delete() }
$z.Dispose()`
  const r = spawnSync('powershell', ['-NoProfile', '-NonInteractive', '-Command', ps], { encoding: 'utf8' })
  if (r.status !== 0) throw new Error('could not make the missing-picture docx: ' + r.stderr)
  return target
}

async function openFastDocTab(page, filePath) {
  const id = await page.evaluate((p) => {
    const name = p.split(/[\\/]/).pop()
    const tab = window.__khTest.tabStore.openTab({ route: '/fast-doc-view', title: name, localFileName: name, localFilePath: p })
    return tab.id
  }, filePath)
  await waitFor(`text of ${path.basename(filePath)}`, async () => {
    const s = await fdvState(page)
    if (!s || s.blockCount === 0) return false
    return page.evaluate(() => [...document.querySelectorAll('.fast-doc-view-block-text')].some((b) => b.textContent.trim().length > 0))
  }, 90_000)
  return id
}

/** Every picture image's state in the shown document (the src kind, decode result, box size). */
const pictureReport = (page) => page.evaluate(async () => {
  const out = []
  for (const img of document.querySelectorAll('.fast-doc-view-block-text img[data-word-picture]')) {
    const r = img.getBoundingClientRect()
    let type = null
    if (img.src) {
      if (!img.complete) await new Promise((res) => { img.onload = img.onerror = res })
      try { type = (await (await fetch(img.src)).blob()).type } catch { type = 'fetch-failed' }
    }
    out.push({ id: img.getAttribute('data-word-picture'), src: img.getAttribute('src') ?? '', type, naturalWidth: img.naturalWidth, w: r.width, h: r.height })
  }
  return out
})

async function runChecks(page, consoleErrors) {
  const locale = JSON.parse(fs.readFileSync(path.join(frontendDir, 'src', 'i18n', 'locales', 'he', 'fastDocView.json'), 'utf8'))
  const localeEn = JSON.parse(fs.readFileSync(path.join(frontendDir, 'src', 'i18n', 'locales', 'en', 'fastDocView.json'), 'utf8'))

  // The host was launched with the text document as argv[1]: the real open-file path.
  await check('open .docx from the command line (host open path)', async () => {
    await waitFor('the launch file text', async () => {
      const s = await fdvState(page)
      return s && s.blockCount > 0 && page.evaluate(() => [...document.querySelectorAll('.fast-doc-view-block-text')].some((b) => b.textContent.trim().length > 20))
    }, 120_000)
    const s = await fdvState(page)
    return `${s.blockCount} blocks, ${s.footnoteCount} footnotes, index complete=${s.isIndexComplete}`
  })

  await check('scroll moves the first visible block', async () => {
    const before = (await fdvState(page)).firstVisible?.index ?? 0
    await page.locator('.fast-doc-view-scroller').first().hover()
    for (let i = 0; i < 10; i++) await page.mouse.wheel(0, 600)
    const after = await waitFor('scroll', async () => { const s = await fdvState(page); return s.firstVisible && s.firstVisible.index > before ? s.firstVisible.index : false }, 10_000)
    return `first visible ${before} -> ${after}`
  })

  await check('jump to a block lands on it, with text', async () => {
    await waitFor('index complete', async () => (await fdvState(page)).isIndexComplete, 120_000)
    const target = Math.floor((await fdvState(page)).blockCount / 2)
    await page.evaluate((i) => window.__fastDocViewTest.scrollToBlock(i), target)
    const landed = await waitFor('jump', async () => { const s = await fdvState(page); return s.firstVisible && Math.abs(s.firstVisible.index - target) <= 1 ? s.firstVisible.index : false }, 10_000)
    await waitFor('text at the jump', () => page.evaluate(() => [...document.querySelectorAll('.fast-doc-view-block-text')].some((b) => b.textContent.trim().length > 0)), 10_000)
    return `target ${target}, first visible ${landed}`
  })

  await check('notes pane opens with notes', async () => {
    const s = await fdvState(page)
    if (s.footnoteCount + s.endnoteCount === 0) throw new Error('the sample has no notes')
    await page.locator('.fast-doc-view-notes-place button').first().click()
    await waitFor('notes pane', async () => (await fdvState(page)).notesPaneOpen, 10_000)
    const text = await waitFor('note text', () => page.evaluate(() => document.querySelector('.fast-doc-view-notes-pane')?.textContent.trim().length || 0), 15_000)
    return `pane open, ${text} chars of notes`
  })

  await check('search in the document finds matches', async () => {
    const word = await page.evaluate(() => {
      const text = [...document.querySelectorAll('.fast-doc-view-block-text')].map((b) => b.textContent).join(' ')
      return text.split(/[\s.,:;()\[\]"'!?-]+/).find((w) => w.length >= 4) ?? null
    })
    if (!word) throw new Error('no word to search for')
    const titles = [locale.search, localeEn.search]
    await page.locator(titles.map((t) => `.fast-doc-view-frame-toolbar button[title="${t}"]`).join(', ')).first().click()
    await waitFor('search bar', async () => (await fdvState(page)).search.visible, 5_000)
    const input = page.locator('.fast-doc-view-main input').first()
    await input.fill(word)
    const s = await waitFor('matches', async () => { const st = (await fdvState(page)).search; return !st.isSearching && st.matchCount > 0 ? st : false }, 30_000)
    await page.keyboard.press('Enter')
    return `${s.matchCount} matches for a ${word.length}-letter word`
  })

  // Object URLs: count what the page creates and revokes from here on.
  await page.evaluate(() => {
    const created = new Set(), revoked = new Set()
    const create = URL.createObjectURL.bind(URL), revoke = URL.revokeObjectURL.bind(URL)
    URL.createObjectURL = (o) => { const u = create(o); created.add(u); return u }
    URL.revokeObjectURL = (u) => { revoked.add(u); revoke(u) }
    window.__rigBlobs = { created, revoked }
  })

  let pictureTabId = null
  await check('pictures load through the hosted picture path', async () => {
    pictureTabId = await openFastDocTab(page, pictureDoc)
    const errorsBefore = consoleErrors.length
    const pics = await waitFor('picture sources', async () => {
      const r = await pictureReport(page)
      return r.length > 0 && r.some((p) => p.src) && r.every((p) => p.src || p.w > 0) ? r : false
    }, 30_000)
    const shown = pics.filter((p) => p.src)
    const notBlob = shown.filter((p) => !p.src.startsWith('blob:'))
    if (notBlob.length) throw new Error(`not an object URL: ${notBlob.map((p) => p.src).join(', ')}`)
    const browserTypes = /^image\/(png|jpeg|gif|bmp|webp|svg\+xml)$/
    const broken = shown.filter((p) => browserTypes.test(p.type) && !(p.naturalWidth > 0))
    if (broken.length) throw new Error(`did not decode: ${JSON.stringify(broken)}`)
    if (!shown.some((p) => p.naturalWidth > 0)) throw new Error(`no picture decoded: ${JSON.stringify(pics)}`)
    const unsized = pics.filter((p) => !(p.w > 0 && p.h > 0))
    if (unsized.length) throw new Error(`unsized: ${JSON.stringify(unsized)}`)
    if (consoleErrors.length > errorsBefore) throw new Error(`console errors: ${consoleErrors.slice(errorsBefore).join(' | ')}`)
    return pics.map((p) => `${p.type ?? 'no source'} ${p.naturalWidth}px box ${Math.round(p.w)}x${Math.round(p.h)}`).join('; ')
  })

  let missingTabId = null
  await check('a missing picture leaves a sized empty box', async () => {
    const doc = makeMissingPictureDoc()
    missingTabId = await openFastDocTab(page, doc)
    // the full document's pictures must be settled: each one either has a source or failed
    const pics = await waitFor('picture states', async () => {
      const r = await pictureReport(page)
      const loaded = r.filter((p) => p.src).length
      return r.length > 0 && loaded > 0 ? r : false
    }, 30_000)
    await new Promise((r) => setTimeout(r, 1500)) // let the failed request settle
    const after = await pictureReport(page)
    const empty = after.filter((p) => !p.src)
    if (empty.length === 0) throw new Error(`no empty box: ${JSON.stringify(pics)}`)
    const unsized = empty.filter((p) => !(p.w > 0 && p.h > 0))
    if (unsized.length) throw new Error(`empty box without size: ${JSON.stringify(unsized)}`)
    return `${empty.length} empty sized box(es): ${empty.map((p) => `${Math.round(p.w)}x${Math.round(p.h)}`).join(', ')}`
  })

  await check('closing the tabs revokes every object URL', async () => {
    for (const id of [missingTabId, pictureTabId].filter(Boolean)) await page.evaluate((i) => window.__khTest.tabStore.closeTab(i), id)
    const r = await waitFor('revokes', () => page.evaluate(() => {
      const { created, revoked } = window.__rigBlobs
      const left = [...created].filter((u) => !revoked.has(u))
      return created.size > 0 && left.length === 0 ? { created: created.size } : false
    }), 10_000).catch(async () => page.evaluate(() => {
      const { created, revoked } = window.__rigBlobs
      throw new Error(`created ${created.size}, still live ${[...created].filter((u) => !revoked.has(u)).length}`)
    }))
    return `${r.created} created, all revoked`
  })

  await check('no console errors during the run', async () => {
    if (consoleErrors.length) throw new Error(consoleErrors.join(' | '))
    return 'none'
  })
}

// ── main ────────────────────────────────────────────────────────────────────
async function main() {
  for (const f of [textDoc, pictureDoc, path.join(serviceBuildDir, 'KitveiHakodeshService.exe')])
    if (!fs.existsSync(f)) throw new Error(`missing ${f}`)
  log(`rig folder ${root}`)
  // --host-dir <folder>: reuse a host build this rig made (the "host" folder of a --keep run)
  const reuseHost = option('--host-dir')
  if (reuseHost) fs.cpSync(reuseHost, hostDir, { recursive: true })
  else buildHost()
  buildFrontend()
  const hostExe = fs.readdirSync(hostDir).filter((n) => n.endsWith('.exe') && !n.includes('vshost')).map((n) => path.join(hostDir, n))[0]
  if (!hostExe) throw new Error('host exe not found')

  const serviceExe = shadowService()
  startProcess('service', serviceExe, [], { KHS_PIPE_NAME: pipeName, KHS_DATA_DIR: serviceBuildDir, KHS_OWNER_PID: String(process.pid) })
  await waitFor(`service pipe ${pipeName}`, async () => pipeListed(), 120_000)
  log(`service up on ${pipeName}`)

  const port = await freePort()
  const profile = path.join(root, 'profile')
  startProcess('host', hostExe, [textDoc], {
    KH_TEST_CDP_PORT: String(port), KH_TEST_PROFILE_DIR: profile, KHS_PIPE_NAME: pipeName,
  })
  await waitFor('DevTools endpoint', async () => (await fetch(`http://127.0.0.1:${port}/json/version`)).ok, 90_000, 500)
  const browser = await chromium.connectOverCDP(`http://127.0.0.1:${port}`)
  try {
    const page = await waitFor('app page', async () => browser.contexts().flatMap((c) => c.pages()).find((p) => p.url().includes('kitveihakodesh-app')), 60_000)
    const consoleErrors = []
    page.on('console', (m) => { if (m.type() === 'error') consoleErrors.push(m.text()) })
    page.on('pageerror', (e) => consoleErrors.push(String(e)))
    await waitFor('test hooks', () => page.evaluate(() => !!window.__khTest), 60_000)
    log(`attached to ${page.url()}`)
    await runChecks(page, consoleErrors)
  } finally {
    await browser.close().catch(() => {})
  }
}

let exitCode = 1
try {
  await main()
  exitCode = results.length && results.every((r) => r.ok) ? 0 : 1
} catch (e) {
  log(`ERROR ${e.stack ?? e}`)
} finally {
  teardown()
  console.log('\n=== hosted rig results ===')
  for (const r of results) console.log(`${r.ok ? 'PASS' : 'FAIL'}  ${r.name}${r.detail ? '  (' + r.detail + ')' : ''}`)
  process.exit(exitCode)
}
