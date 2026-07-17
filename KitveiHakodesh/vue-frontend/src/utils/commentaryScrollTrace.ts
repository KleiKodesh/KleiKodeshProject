/**
 * Runtime-toggleable tracer for the commentary scroll paths
 * (scrollToGroup + restoreCommentaryScrollPos).
 *
 * WHY THIS EXISTS
 * These two flows work in dev but misbehave inside the C# WebView2 host. The
 * difference is almost always TIMING: dev reads data from the local service over
 * a fast in-process-ish pipe, while the host round-trips every query through the
 * postMessage bridge (window.__webviewQuery / __webviewAction). That changes when
 * measurements settle, when content backfills land, and whether the rAF /
 * MutationObserver correction windows and bounded retries in useCommentaryScroll
 * still hold. This tracer makes that divergence VISIBLE so we stop guessing.
 *
 * HOW TO USE (works in the WebView2 DevTools console — no rebuild needed):
 *   __commentaryScrollTrace.on()     // start logging
 *   ...reproduce the bug (open a line / reopen the panel / switch layout)...
 *   __commentaryScrollTrace.dump()   // print the full ordered timeline as a table
 *   __commentaryScrollTrace.off()    // stop
 *
 * Each event carries a monotonically increasing seq, a ms timestamp relative to
 * the first event of the current "flow", the flow name, and a free-form payload.
 * Compare a dev capture against a host capture side by side: the FIRST row where
 * the branch/attempt/measured-height diverges is the bug.
 */

interface TraceEvent {
  seq: number
  /** ms since the current flow started. */
  t: number
  /** Logical flow this event belongs to, e.g. "scrollToGroup" / "restore". */
  flow: string
  /** Short event name — the decision point. */
  event: string
  /** Arbitrary structured detail for this decision point. */
  detail: Record<string, unknown>
}

let _enabled = false
let _seq = 0
let _flowOrigin: number | null = null
let _events: TraceEvent[] = []
// Keep memory bounded during long capture sessions.
const MAX_EVENTS = 5000

function now(): number {
  return performance.now()
}

/** Begin a new flow — resets the relative clock so timings read from 0. */
function begin(flow: string, detail: Record<string, unknown> = {}): void {
  if (!_enabled) return
  _flowOrigin = now()
  push(flow, 'BEGIN', detail)
}

/** Record one decision point inside the active flow. */
function push(flow: string, event: string, detail: Record<string, unknown> = {}): void {
  if (!_enabled) return
  if (_flowOrigin == null) _flowOrigin = now()
  _events.push({
    seq: _seq++,
    t: +(now() - _flowOrigin).toFixed(1),
    flow,
    event,
    detail,
  })
  if (_events.length > MAX_EVENTS) _events.splice(0, _events.length - MAX_EVENTS)
  // Live echo so you also see it stream in the console as it happens.
  // eslint-disable-next-line no-console
  console.debug(`[scrollTrace ${flow}] ${event}`, detail)
}

function on(): string {
  _enabled = true
  return 'commentary scroll trace ON — reproduce, then __commentaryScrollTrace.dump()'
}

function off(): string {
  _enabled = false
  return 'commentary scroll trace OFF'
}

function clear(): string {
  _events = []
  _seq = 0
  _flowOrigin = null
  return 'cleared'
}

/** Print the captured timeline as a console table (ordered by seq). */
function dump(): TraceEvent[] {
  const rows = _events.map((e) => ({
    seq: e.seq,
    't (ms)': e.t,
    flow: e.flow,
    event: e.event,
    detail: JSON.stringify(e.detail),
  }))
  // eslint-disable-next-line no-console
  console.table(rows)
  return _events
}

export const commentaryScrollTrace = { begin, push, on, off, clear, dump, get enabled() { return _enabled } }

// Expose on window so it can be driven from the WebView2 DevTools console with no
// import and no rebuild. Guarded so SSR / non-browser contexts don't throw.
if (typeof window !== 'undefined') {
  ;(window as unknown as Record<string, unknown>).__commentaryScrollTrace = commentaryScrollTrace
}
