/**
 * Sending error reports, and the queue that holds the ones that could not go out yet.
 *
 * THE RULE THE WHOLE FILE IS BUILT ON: a report the user wrote is never lost. Every
 * failure that might not be the user's fault — offline, a timeout, a 5xx, a missing
 * endpoint — leaves the report on the pending queue and retries later. Only a server
 * saying "this is wrong and will stay wrong" (a 4xx that isn't the id conflict) drops it.
 */

import { ref, computed, watch } from 'vue'
import { useOnline } from '@vueuse/core'
import { lsGet, lsSet, registerLocalStorageReclaimer } from '@/utils/persistence'
import {
  ERROR_REPORT_SCHEMA_VERSION,
  encodeErrorReportBody,
  reissueReportId,
  withContentDigest,
  type ErrorReportPayload,
} from './errorReportPayload'
import {
  ERROR_REPORT_ENDPOINT,
  ERROR_REPORT_TIMEOUT_MS,
  hasErrorReportEndpoint,
} from './errorReportEndpoint'

/** Namespaced `area.name` like every other key in this app — see utils/persistence. */
const PENDING_REPORTS_KEY = 'errorReport.pending'

/**
 * What a send attempt concluded.
 *   sent        — the server took it; it is off the queue.
 *   duplicate   — taken, and the server already had this content_digest.
 *   queued      — not sent, but kept: offline, timeout, 5xx, or no endpoint configured.
 *   rejected    — the server refused it permanently (4xx); it is off the queue.
 */
export type ErrorReportOutcome = 'sent' | 'duplicate' | 'queued' | 'rejected'

export interface ErrorReportResult {
  outcome: ErrorReportOutcome
  /**
   * The server refused THIS CONTENT under THIS id (409). The report stays on the queue
   * under a fresh id so the user can resubmit it deliberately, but no automatic flush
   * will ever send it — resending refused content on a timer is how an app turns into
   * a spammer.
   */
  needsManualResend?: boolean
  /** The server's answer to "can I apply a textCorrection automatically?", when it said. */
  correctionSupported?: boolean
}

/** The 200 body both apps' collectors answer with. Everything in it is optional. */
interface ErrorReportResponse {
  duplicate?: boolean
  correction_supported?: boolean
}

// ── The pending queue ─────────────────────────────────────────────────────────

/**
 * Anything but an array means the key was never written, was written by an older
 * build, or was half-written — and a non-array here would throw on the first enqueue,
 * losing the report the user just typed. Start empty instead.
 */
/** What the key held, before any filtering — so a load that dropped something can tell. */
const storedReportCount = (() => {
  const stored = lsGet<unknown[]>(PENDING_REPORTS_KEY)
  return Array.isArray(stored) ? stored.length : 0
})()

function readPendingReports(): ErrorReportPayload[] {
  const stored = lsGet<ErrorReportPayload[]>(PENDING_REPORTS_KEY)
  return Array.isArray(stored) ? stored.filter(isCurrentShape) : []
}

/** A queued report from an older schema (or a half-written one) cannot be digested or
 *  sent — it would throw inside every flush. Dropped on load, never migrated. */
function isCurrentShape(p: unknown): p is ErrorReportPayload {
  const report = p as Partial<ErrorReportPayload> | null
  return typeof report === 'object' && report !== null
    && typeof report.report_id === 'string'
    && report.schema_version === ERROR_REPORT_SCHEMA_VERSION
    && typeof report.location === 'object' && report.location !== null
}

/** The queue shares localStorage's ~5 MB with every other key: past this many reports the
 *  oldest are dropped, so a long offline spell cannot fill the quota and block every write. */
const MAX_PENDING_REPORTS = 20
/** A refused (409) report waits this long for a manual resend, then is dropped. */
const MANUAL_RESEND_TTL_MS = 7 * 24 * 60 * 60 * 1000

/** Drop expired manual-resend reports, then the oldest past MAX_PENDING_REPORTS. */
function trimPendingReports(reports: ErrorReportPayload[]): ErrorReportPayload[] {
  const now = Date.now()
  const live = reports.filter(
    (p) => !p.manualResendOnly || !(now - Date.parse(p.created_at) > MANUAL_RESEND_TTL_MS),
  )
  return live.slice(-MAX_PENDING_REPORTS)
}

const pendingReports = ref<ErrorReportPayload[]>(trimPendingReports(readPendingReports()))

function savePendingReports(): void {
  lsSet(PENDING_REPORTS_KEY, pendingReports.value)
}
// The load dropped or trimmed something: write the queue back now, not on the next enqueue.
if (pendingReports.value.length !== storedReportCount) savePendingReports()

// localStorage is full: the older half of the queue goes — the newest reports stay.
registerLocalStorageReclaimer(() => {
  if (pendingReports.value.length < 2) return
  pendingReports.value = pendingReports.value.slice(Math.floor(pendingReports.value.length / 2))
  savePendingReports()
})

function enqueue(payload: ErrorReportPayload): void {
  // Keyed by report_id, so re-queueing a report that already failed once replaces it
  // rather than piling up a copy per retry.
  const existing = pendingReports.value.findIndex((p) => p.report_id === payload.report_id)
  if (existing >= 0) pendingReports.value[existing] = payload
  else pendingReports.value.push(payload)
  pendingReports.value = trimPendingReports(pendingReports.value)
  savePendingReports()
}

function dequeue(reportId: string): void {
  pendingReports.value = pendingReports.value.filter((p) => p.report_id !== reportId)
  savePendingReports()
}

/** Statuses that mean "do not send this again" — the collector's contract 2.4. */
const PERMANENT_STATUSES = new Set([400, 409, 413, 422])

/**
 * At most this many queued reports go out per flush.
 *
 * The collector rate-limits to 8 per minute per IP and answers 429 beyond that. Six
 * leaves headroom for a report the user sends by hand while a flush is running, so a
 * backlog drains steadily instead of tripping the limit it just caused.
 */
const MAX_FLUSH_PER_RUN = 6

/** How often a pending queue is retried while the app is running. */
const FLUSH_INTERVAL_MS = 5 * 60 * 1000

// ── The POST ──────────────────────────────────────────────────────────────────

/**
 * One attempt. Decides ONLY what the server said; the queue is managed by the callers
 * below, which is what keeps "did it go out?" and "what do we keep?" separable.
 *
 * The content digest is stamped here rather than when the payload was built: it is a
 * SHA-256 over the canonical encoding, so computing it is async, and the dialog
 * rebuilds its payload on every keystroke.
 */
async function postReport(
  draft: ErrorReportPayload,
): Promise<ErrorReportResult & { sent: ErrorReportPayload }> {
  const payload = await withContentDigest(draft)
  const body = encodeErrorReportBody(payload)
  // Over the body cap even after the per-field caps: only correction text can be this
  // large, and correction text is never truncated. Nothing retrying will fix it.
  if (body === null) return { outcome: 'rejected', sent: payload }

  let response: Response
  try {
    response = await fetch(ERROR_REPORT_ENDPOINT, {
      method: 'POST',
      headers: {
        'Content-Type': 'application/json; charset=utf-8',
        Accept: 'application/json',
      },
      body,
      signal: AbortSignal.timeout(ERROR_REPORT_TIMEOUT_MS),
    })
  } catch {
    // Timeout, DNS, connection refused — all transient as far as this app can tell.
    return { outcome: 'queued', sent: payload }
  }

  if (response.ok) {
    // A collector that answers 200 with no body, or with something that isn't JSON,
    // still accepted the report — the flags are extra information, not the verdict.
    let parsed: ErrorReportResponse = {}
    try {
      parsed = (await response.json()) as ErrorReportResponse
    } catch { /* no body; the 200 is the answer */ }
    return {
      outcome: parsed.duplicate ? 'duplicate' : 'sent',
      correctionSupported: parsed.correction_supported,
      sent: payload,
    }
  }

  // The collector's contract: 400/409/413/422 are permanent, everything else is
  // transient. A 409 means THIS CONTENT was refused, so re-sending it under a new id
  // would just manufacture a second copy of a report that was already turned down.
  // The report keeps a fresh id so the user can resubmit deliberately, but nothing
  // sends it again automatically.
  if (response.status === 409) {
    return { outcome: 'rejected', needsManualResend: true, sent: reissueReportId(payload) }
  }
  if (PERMANENT_STATUSES.has(response.status)) {
    return { outcome: 'rejected', sent: payload }
  }

  // 408 and 429 land here with the 5xx family: the server is busy or rate-limiting,
  // which is the most likely answer during a queue flush. Dropping those would throw
  // away exactly the reports a flush exists to deliver.
  return { outcome: 'queued', sent: payload }
}

// ── Public API ────────────────────────────────────────────────────────────────

/**
 * Send now. Whatever the result, the queue ends up correct: a report that did not get
 * through is on it, a report that did (or was refused outright) is not.
 */
export async function sendErrorReport(payload: ErrorReportPayload): Promise<ErrorReportResult> {
  if (!hasErrorReportEndpoint()) {
    enqueue(payload)
    return { outcome: 'queued' }
  }
  const { sent, ...result } = await postReport(payload)
  // Queue what was actually SENT, not what was handed in: a 409 reissues the report
  // under a fresh id, and re-queueing the original would replay the id the server has
  // already refused — a collision re-paid on every retry, forever.
  if (result.outcome === 'queued') enqueue(sent)
  else if (result.needsManualResend) enqueue({ ...sent, manualResendOnly: true })
  else dequeue(sent.report_id)
  if (sent.report_id !== payload.report_id) dequeue(payload.report_id)
  return result
}

/** "שמור לשליחה מאוחרת" — straight onto the queue, no attempt made. */
export function queueErrorReport(payload: ErrorReportPayload): void {
  enqueue(payload)
}

/** How many reports are still waiting. For a UI that wants to say so. */
export const pendingErrorReportCount = computed(() => pendingReports.value.length)

let flushing = false

/**
 * Try the pending reports, oldest first, up to MAX_FLUSH_PER_RUN.
 *
 * Serial rather than parallel on purpose: the queue is tiny and a collector receiving
 * one report at a time can deduplicate by content_digest without racing itself. The
 * `flushing` latch stops the timer and the online watcher from running together and
 * sending every report twice. The per-run cap matters for the same reason the 429
 * handling does — a long queue that bursts is what earns a rate-limit.
 */
export async function flushPendingErrorReports(): Promise<void> {
  if (flushing || !hasErrorReportEndpoint()) return
  flushing = true
  try {
    const sweepable = pendingReports.value
      .filter((p) => !p.manualResendOnly)
      .slice(0, MAX_FLUSH_PER_RUN)
    for (const payload of sweepable) {
      let posted: Awaited<ReturnType<typeof postReport>>
      try {
        posted = await postReport(payload)
      } catch (err) {
        // A report that cannot even be built into a request never will be: drop it, or it
        // throws again on every flush and holds up the reports behind it.
        console.warn('[errorReport] dropping a queued report that cannot be sent:', err)
        dequeue(payload.report_id)
        continue
      }
      const { sent, outcome, needsManualResend } = posted
      // A reissued id replaces the queued entry rather than joining it, so a report
      // that keeps failing transiently does not accumulate one copy per 409.
      if (needsManualResend) {
        dequeue(payload.report_id)
        enqueue({ ...sent, manualResendOnly: true })
      } else if (outcome === 'queued') {
        if (sent.report_id !== payload.report_id) {
          dequeue(payload.report_id)
          enqueue(sent)
        }
      } else {
        dequeue(payload.report_id)
        dequeue(sent.report_id)
      }
    }
  } finally {
    flushing = false
  }
}

/**
 * Retry the queue on a timer, and again whenever the machine comes back online.
 *
 * BOTH triggers are needed. Coming back online catches the laptop-reopened case, but a
 * report queued by a 5xx or a rate-limit while the machine never lost connectivity
 * would otherwise sit untouched until the next app start — useOnline never fires.
 *
 * Called once, from the app root. Module-level rather than a composable so this exists
 * exactly once however many book panes are open — each pane mounting its own would
 * mean one flush per pane the moment connectivity returned.
 */
export function startErrorReportRetries(): void {
  // VueUse's useOnline is a reactive wrapper over the online/offline events, so this
  // watch needs no listeners of our own.
  const online = useOnline()
  if (online.value) void flushPendingErrorReports()
  // Deliberately never stopped: neither has an owning component, and both must live as
  // long as the app does.
  watch(online, (isOnline) => {
    if (isOnline) void flushPendingErrorReports()
  })
  setInterval(() => {
    if (online.value) void flushPendingErrorReports()
  }, FLUSH_INTERVAL_MS)
}
