/**
 * Runtime-toggleable tracer for the commentary scroll paths
 * (scrollToGroup + restoreCommentaryScrollPos), in BOTH commentary panels.
 *
 * WHY THIS EXISTS
 * These flows work in dev but misbehave inside the C# WebView2 host, and some of
 * the failures are rare enough to take many attempts to reproduce. The difference
 * is almost always TIMING: dev reads data from the local service over a fast
 * in-process-ish pipe, while the host round-trips every query through the
 * postMessage bridge (window.__webviewQuery / __webviewAction). That changes when
 * measurements settle, when content backfills land, and whether the rAF /
 * MutationObserver correction windows and bounded retries in useCommentaryScroll
 * still hold. This tracer makes that divergence VISIBLE so we stop guessing.
 *
 * HOW TO USE (works in the WebView2 DevTools console - no rebuild needed):
 *   __commentaryScrollTrace.on()        // start logging; SURVIVES RELOADS
 *   ...reproduce the bug (however many attempts it takes)...
 *   __commentaryScrollTrace.summary()   // quick check that you caught it
 *   __commentaryScrollTrace.save()      // download a .json file to send on
 *   __commentaryScrollTrace.off()       // stop
 *
 * Capture survives a reload and a crash: `on()` persists the enabled flag, and
 * events are mirrored into localStorage (throttled, and flushed on page hide), so
 * a bug that only shows up on the fifth attempt is not lost by the reload in
 * between. `save()` and `copy()` include whatever was recovered.
 *
 * SAFETY: corpus text (Hebrew section/book labels) is MASKED by default in
 * everything that leaves the app - save(), copy(), summary() - because those
 * outputs get pasted into bug reports and chat. Each Hebrew run becomes a stable
 * [H:xxxx] placeholder, so identity is still comparable across events. Pass
 * { raw: true } for an unmasked local copy.
 *
 * Each event carries a monotonically increasing seq, a ms timestamp relative to
 * the first event of ITS OWN flow, the flow name, and a free-form payload. Flow
 * names are slot-tagged ("scrollToGroup:bottom" / "restore:side") because the two
 * commentary panels scroll concurrently - filter a dump by flow to read one panel,
 * and use `seq` (not `t`) to order events across panels.
 *
 * WHAT TO LOOK FOR: exactly ONE scrollToGroup BEGIN per panel per anchor change is
 * correct. Zero means nothing scrolled (the panel keeps a stale offset); two means
 * a superseded callback is still firing. Any ABORT_* names the reason.
 */

interface TraceEvent {
  seq: number
  /** ms since the current flow started. */
  t: number
  /** Logical flow this event belongs to, e.g. "scrollToGroup:bottom". */
  flow: string
  /** Short event name - the decision point. */
  event: string
  /** Arbitrary structured detail for this decision point. */
  detail: Record<string, unknown>
}

import { copyTextToClipboard } from './clipboard'

const LS_ENABLED = 'kitvei-hakodesh.debug.commentaryScrollTrace'
const LS_EVENTS = 'kitvei-hakodesh.debug.commentaryScrollTrace.events'

let _enabled = false
let _seq = 0
// One origin PER FLOW. A single shared origin made every begin() reset the clock
// for every other flow, so with the two commentary panels scrolling concurrently
// the `t` column was meaningless (both panels reported t=0 for unrelated moments).
const _flowOrigins = new Map<string, number>()
let _events: TraceEvent[] = []
// Keep memory bounded during long capture sessions.
const MAX_EVENTS = 5000
// Fewer are mirrored to localStorage: the quota is ~5MB and a write happens while
// the user is mid-interaction, so this stays small enough to be cheap.
const MAX_PERSISTED = 1500
const PERSIST_THROTTLE_MS = 1000

function now(): number {
  return performance.now()
}

// ── Corpus masking ───────────────────────────────────────────────────────────

const HEB = /[\u0590-\u05FF\uFB1D-\uFB4F]+/g

/** FNV-1a, 4 hex chars. Not cryptographic - just a stable per-run label. */
function shortHash(input: string): string {
  let h = 0x811c9dc5
  for (let i = 0; i < input.length; i++) {
    h ^= input.charCodeAt(i)
    h = Math.imul(h, 0x01000193)
  }
  return (h >>> 0).toString(16).padStart(8, '0').slice(0, 4)
}

function maskString(s: string): string {
  return s.replace(HEB, (w) => `[H:${shortHash(w)}]`)
}

function maskDeep(value: unknown): unknown {
  if (typeof value === 'string') return maskString(value)
  if (Array.isArray(value)) return value.map(maskDeep)
  if (value && typeof value === 'object') {
    const out: Record<string, unknown> = {}
    for (const [k, v] of Object.entries(value as Record<string, unknown>)) out[k] = maskDeep(v)
    return out
  }
  return value
}

function maskEvents(events: TraceEvent[]): TraceEvent[] {
  return events.map((e) => ({ ...e, detail: maskDeep(e.detail) as Record<string, unknown> }))
}

// ── Persistence across reloads ───────────────────────────────────────────────

let _persistTimer: ReturnType<typeof setTimeout> | null = null

function writePersisted(): void {
  _persistTimer = null
  try {
    const tail = _events.slice(-MAX_PERSISTED)
    localStorage.setItem(LS_EVENTS, JSON.stringify({ seq: _seq, events: tail }))
  } catch {
    // Quota exceeded or storage unavailable - keep tracing in memory rather than
    // letting a debugging aid break the app.
  }
}

function schedulePersist(): void {
  if (_persistTimer != null) return
  _persistTimer = setTimeout(writePersisted, PERSIST_THROTTLE_MS)
}

function flushPersisted(): void {
  if (_persistTimer != null) clearTimeout(_persistTimer)
  writePersisted()
}

/** Reload the events a previous page load left behind, and prepend them. */
function recover(): string {
  try {
    const stored = localStorage.getItem(LS_EVENTS)
    if (!stored) return 'nothing persisted'
    const parsed = JSON.parse(stored) as { seq?: number; events?: TraceEvent[] }
    const recovered = parsed.events ?? []
    if (!recovered.length) return 'nothing persisted'
    // Keep the older run's events ahead of this run's, and make sure new seq
    // numbers cannot collide with recovered ones.
    _events = [...recovered, ..._events]
    _seq = Math.max(_seq, (parsed.seq ?? 0) + 1, ...recovered.map((e) => e.seq + 1))
    return `recovered ${recovered.length} events from a previous page load`
  } catch {
    return 'could not read persisted events'
  }
}

// ── Recording ────────────────────────────────────────────────────────────────

/** Begin a new flow - resets that flow's relative clock so timings read from 0. */
function begin(flow: string, detail: Record<string, unknown> = {}): void {
  if (!_enabled) return
  _flowOrigins.set(flow, now())
  push(flow, 'BEGIN', detail)
}

/** Record one decision point inside the active flow. */
function push(flow: string, event: string, detail: Record<string, unknown> = {}): void {
  if (!_enabled) return
  let origin = _flowOrigins.get(flow)
  if (origin == null) {
    origin = now()
    _flowOrigins.set(flow, origin)
  }
  _events.push({
    seq: _seq++,
    t: +(now() - origin).toFixed(1),
    flow,
    event,
    detail,
  })
  if (_events.length > MAX_EVENTS) _events.splice(0, _events.length - MAX_EVENTS)
  schedulePersist()
  // Live echo so you also see it stream in the console as it happens.
  // eslint-disable-next-line no-console
  console.debug(`[scrollTrace ${flow}] ${event}`, detail)
}

// ── Control ──────────────────────────────────────────────────────────────────

function on(): string {
  _enabled = true
  try { localStorage.setItem(LS_ENABLED, '1') } catch { /* ignore */ }
  const recovered = recover()
  return `commentary scroll trace ON (survives reloads) - ${recovered}. Reproduce, then __commentaryScrollTrace.save()`
}

function off(): string {
  _enabled = false
  try { localStorage.removeItem(LS_ENABLED) } catch { /* ignore */ }
  flushPersisted()
  return 'commentary scroll trace OFF (captured events kept - save() still works)'
}

function clear(): string {
  _events = []
  _seq = 0
  _flowOrigins.clear()
  try { localStorage.removeItem(LS_EVENTS) } catch { /* ignore */ }
  return 'cleared'
}

// ── Output ───────────────────────────────────────────────────────────────────

/** Print the captured timeline as a console table (ordered by seq). */
function dump(opts: { raw?: boolean } = {}): TraceEvent[] {
  const events = opts.raw ? _events : maskEvents(_events)
  const rows = events.map((e) => ({
    seq: e.seq,
    't (ms)': e.t,
    flow: e.flow,
    event: e.event,
    detail: JSON.stringify(e.detail),
  }))
  // eslint-disable-next-line no-console
  console.table(rows)
  return events
}

/**
 * Counts per flow plus every abort, so you can confirm the bug was captured
 * BEFORE navigating away and losing the chance.
 */
function summary(): string {
  const byFlow = new Map<string, { begins: number; aborts: string[]; events: number }>()
  for (const e of _events) {
    let row = byFlow.get(e.flow)
    if (!row) {
      row = { begins: 0, aborts: [], events: 0 }
      byFlow.set(e.flow, row)
    }
    row.events++
    if (e.event === 'BEGIN') row.begins++
    if (e.event.startsWith('ABORT')) row.aborts.push(e.event)
  }
  const lines = [`${_events.length} events across ${byFlow.size} flow(s)`]
  for (const [flow, row] of byFlow) {
    lines.push(
      `  ${flow}: ${row.begins} BEGIN, ${row.events} events` +
        (row.aborts.length ? `, ABORTS: ${row.aborts.join(', ')}` : ''),
    )
  }
  const text = lines.join('\n')
  // eslint-disable-next-line no-console
  console.log(text)
  return text
}

function serialize(opts: { raw?: boolean } = {}): string {
  return JSON.stringify(opts.raw ? _events : maskEvents(_events), null, 1)
}

/**
 * Download the capture as a .json file. Masked unless { raw: true } - the file is
 * meant to be sent on, and corpus text must not travel with it.
 */
function save(opts: { raw?: boolean } = {}): string {
  flushPersisted()
  const text = serialize(opts)
  const stamp = new Date().toISOString().replace(/[:.]/g, '-')
  const name = `commentary-scroll-trace-${stamp}${opts.raw ? '-RAW' : ''}.json`
  try {
    const blob = new Blob([text], { type: 'application/json' })
    const url = URL.createObjectURL(blob)
    const a = document.createElement('a')
    a.href = url
    a.download = name
    document.body.appendChild(a)
    a.click()
    a.remove()
    setTimeout(() => URL.revokeObjectURL(url), 10_000)
    return `saved ${_events.length} events to ${name}${opts.raw ? ' (UNMASKED - do not share)' : ''}`
  } catch {
    // Downloads can be blocked in an embedded WebView - fall back to the clipboard.
    void copy(opts)
    return `download blocked; ${_events.length} events copied to the clipboard instead`
  }
}

/** Copy the capture to the clipboard (masked unless { raw: true }). */
async function copy(opts: { raw?: boolean } = {}): Promise<string> {
  flushPersisted()
  const text = serialize(opts)
  try {
    if (!(await copyTextToClipboard(text))) throw new Error('clipboard unavailable')
    return `copied ${_events.length} events to the clipboard`
  } catch {
    // eslint-disable-next-line no-console
    console.log(text)
    return `clipboard unavailable - ${_events.length} events logged above instead`
  }
}

export const commentaryScrollTrace = {
  begin,
  push,
  on,
  off,
  clear,
  dump,
  summary,
  save,
  copy,
  recover,
  get enabled() {
    return _enabled
  },
}

// Expose on window so it can be driven from the WebView2 DevTools console with no
// import and no rebuild. Guarded so SSR / non-browser contexts don't throw.
if (typeof window !== 'undefined') {
  ;(window as unknown as Record<string, unknown>).__commentaryScrollTrace = commentaryScrollTrace

  // Re-arm after a reload: a capture that takes many attempts must not be lost
  // just because one of those attempts reloaded the page.
  try {
    if (localStorage.getItem(LS_ENABLED) === '1') {
      _enabled = true
      recover()
      // eslint-disable-next-line no-console
      console.log(
        '[scrollTrace] re-armed after reload; %d events recovered. save() to download.',
        _events.length,
      )
    }
  } catch {
    /* storage unavailable - tracing simply starts off */
  }

  // Last chance to persist before the page goes away.
  window.addEventListener('pagehide', flushPersisted)
  document.addEventListener('visibilitychange', () => {
    if (document.visibilityState === 'hidden') flushPersisted()
  })
}
