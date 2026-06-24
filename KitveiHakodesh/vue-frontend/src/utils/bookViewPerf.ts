/**
 * Lightweight performance logging for the book view load path.
 *
 * Usage:
 *   import { bookViewPerf } from '@/utils/bookViewPerf'
 *
 *   bookViewPerf.mark('lines:chunk0Start')
 *   // ... work ...
 *   bookViewPerf.measure('lines:chunk0', 'lines:chunk0Start')
 *
 * All timing is relative to the first mark recorded in a given session.
 * Call bookViewPerf.reset() when a new book is opened so numbers are relative
 * to the current navigation, not the previous one.
 *
 * The full report is printed by bookViewPerf.report() — also auto-printed
 * after a configurable idle delay following the last measure call.
 *
 * To disable, set VITE_BOOK_VIEW_PERF=false in .env.development.
 */

const ENABLED = import.meta.env.VITE_BOOK_VIEW_PERF !== 'false'

// Auto-report fires this many ms after the last measure call with no new activity.
const AUTO_REPORT_IDLE_MS = 1500

interface PerfEntry {
  label: string
  /** Wall-clock ms since the session origin (first mark of the session). */
  sinceOrigin: number
  /** Duration from the named start mark, or null for point marks. */
  duration: number | null
}

let _origin: number | null = null
let _entries: PerfEntry[] = []
let _autoReportTimer: ReturnType<typeof setTimeout> | null = null

function now(): number {
  return performance.now()
}

function relativeNow(): number {
  if (_origin == null) _origin = now()
  return now() - _origin
}

function scheduleAutoReport() {
  if (!ENABLED) return
  if (_autoReportTimer) clearTimeout(_autoReportTimer)
  _autoReportTimer = setTimeout(() => {
    _autoReportTimer = null
    report()
  }, AUTO_REPORT_IDLE_MS)
}

/**
 * Record a point-in-time mark (no duration).
 * Use for moments you want to see on the timeline: "IDB resolved", "first lines rendered", etc.
 */
function mark(label: string): number {
  if (!ENABLED) return 0
  const time = relativeNow()
  _entries.push({ label, sinceOrigin: time, duration: null })
  scheduleAutoReport()
  return time
}

/**
 * Record a measure with an explicit duration in ms.
 * Use when you already have a start time from a previous `mark()` return value.
 */
function measure(label: string, startTime: number): void {
  if (!ENABLED) return
  const end = relativeNow()
  const duration = end - startTime
  _entries.push({ label, sinceOrigin: end, duration })
  scheduleAutoReport()
}

/**
 * Reset all entries and the session origin.
 * Call this when a new book is opened so timings are relative to the new navigation.
 */
function reset(): void {
  if (_autoReportTimer) { clearTimeout(_autoReportTimer); _autoReportTimer = null }
  _origin = null
  _entries = []
}

/**
 * Print a formatted table of all recorded entries to the browser console.
 * Each row shows the label, time since session start, and duration (if a measure).
 */
function report(): void {
  if (!ENABLED || _entries.length === 0) return

  const rows = _entries.map((e) => ({
    label: e.label,
    't (ms)': e.sinceOrigin.toFixed(1),
    'duration (ms)': e.duration != null ? e.duration.toFixed(1) : '—',
  }))

  // Group sequential entries with the same prefix for easier scanning
  console.groupCollapsed(
    `%c📖 BookView Perf Report  (${_entries.length} entries, span ${_entries[_entries.length - 1]?.sinceOrigin.toFixed(0) ?? '?'} ms)`,
    'font-weight: bold; color: #4fc3f7',
  )
  console.table(rows)
  console.groupEnd()
}

export const bookViewPerf = { mark, measure, reset, report, get enabled() { return ENABLED } }
