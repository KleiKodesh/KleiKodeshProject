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
import { lsGet, lsSet } from '@/utils/persistence'
import {
  encodeErrorReportBody,
  reissueReportId,
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
function readPendingReports(): ErrorReportPayload[] {
  const stored = lsGet<ErrorReportPayload[]>(PENDING_REPORTS_KEY)
  return Array.isArray(stored) ? stored : []
}

const pendingReports = ref<ErrorReportPayload[]>(readPendingReports())

function savePendingReports(): void {
  lsSet(PENDING_REPORTS_KEY, pendingReports.value)
}

function enqueue(payload: ErrorReportPayload): void {
  // Keyed by report_id, so re-queueing a report that already failed once replaces it
  // rather than piling up a copy per retry.
  const existing = pendingReports.value.findIndex((p) => p.report_id === payload.report_id)
  if (existing >= 0) pendingReports.value[existing] = payload
  else pendingReports.value.push(payload)
  savePendingReports()
}

function dequeue(reportId: string): void {
  pendingReports.value = pendingReports.value.filter((p) => p.report_id !== reportId)
  savePendingReports()
}

// ── The POST ──────────────────────────────────────────────────────────────────

/**
 * One attempt. Decides ONLY what the server said; the queue is managed by the callers
 * below, which is what keeps "did it go out?" and "what do we keep?" separable.
 *
 * `allowReissue` guards the one recursive case: a 409 means the server already holds a
 * DIFFERENT report under this id, so the same report goes out again under a new id.
 * Once. A server answering 409 to every id would otherwise loop forever.
 */
async function postReport(
  payload: ErrorReportPayload,
  allowReissue: boolean,
): Promise<ErrorReportResult & { sent: ErrorReportPayload }> {
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

  if (response.status === 409 && allowReissue) {
    return postReport(reissueReportId(payload), false)
  }

  // 5xx is the server having a bad day, not the report being wrong: keep it.
  if (response.status >= 500) return { outcome: 'queued', sent: payload }
  return { outcome: 'rejected', sent: payload }
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
  const { sent, ...result } = await postReport(payload, true)
  // Queue what was actually SENT, not what was handed in: a 409 reissues the report
  // under a fresh id, and re-queueing the original would replay the id the server has
  // already refused — a collision re-paid on every retry, forever.
  if (result.outcome === 'queued') enqueue(sent)
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
 * Try every pending report once, oldest first.
 *
 * Serial rather than parallel on purpose: the queue is tiny and a collector receiving
 * one report at a time can deduplicate by content_digest without racing itself. The
 * `flushing` latch stops the online watcher and a manual flush from running together
 * and sending every report twice.
 */
export async function flushPendingErrorReports(): Promise<void> {
  if (flushing || !hasErrorReportEndpoint()) return
  flushing = true
  try {
    for (const payload of [...pendingReports.value]) {
      const { sent, outcome } = await postReport(payload, true)
      // A reissued id replaces the queued entry rather than joining it, so a report
      // that keeps failing transiently does not accumulate one copy per 409.
      if (outcome === 'queued') {
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
 * Retry the queue whenever the machine comes back online.
 *
 * Called once, from the app root. Module-level rather than a composable so the watcher
 * exists exactly once however many book panes are open — each pane mounting its own
 * would mean one flush per pane the moment connectivity returned.
 */
export function startErrorReportRetries(): void {
  // VueUse's useOnline is a reactive wrapper over the online/offline events, so this
  // watch IS the whole retry trigger — no listeners and no polling of our own.
  const online = useOnline()
  if (online.value) void flushPendingErrorReports()
  // Deliberately never stopped: it has no owning component, and it must live as long
  // as the app does.
  watch(online, (isOnline) => {
    if (isOnline) void flushPendingErrorReports()
  })
}
