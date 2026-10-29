/**
 * The "service restarts to apply a change" dance of the additional-libraries dialog, as plain
 * functions so it can be tested without a DOM or a service.
 *
 * The old dialog reloaded the page after a FIXED 800ms. A service restart takes as long as it
 * takes (the data host is rebuilt: database resolved and opened, index builders started), so the
 * reload usually landed before the service was back and the page came up against a dead pipe.
 */

export interface WaitForRestartOptions {
  /** Give up (resolve false) after this long in total. */
  timeoutMs?: number
  /** Pause between probes. */
  intervalMs?: number
  /** How long to wait for the OLD instance to go away before assuming it already restarted. */
  downWaitMs?: number
  sleep?: (ms: number) => Promise<void>
  now?: () => number
}

const realSleep = (ms: number) => new Promise<void>((resolve) => setTimeout(resolve, ms))

/**
 * Resolves true once the service is answering again AFTER having restarted; false on timeout.
 *
 * Phase 1: probe until it FAILS (the old instance is gone) — otherwise the very first probe
 * would hit the instance that is about to stop and we would report "back" too early. If it never
 * fails within `downWaitMs`, the restart was fast enough that we missed the gap; carry on.
 * Phase 2: probe until it succeeds.
 */
export async function waitForServiceRestart(
  probe: () => Promise<unknown>,
  options: WaitForRestartOptions = {},
): Promise<boolean> {
  const {
    timeoutMs = 90_000,
    intervalMs = 400,
    downWaitMs = 8_000,
    sleep = realSleep,
    now = Date.now,
  } = options
  const start = now()
  const elapsed = () => now() - start

  // Phase 1 — wait for the old instance to stop answering.
  let wentDown = false
  while (elapsed() < downWaitMs && elapsed() < timeoutMs) {
    try {
      await probe()
    } catch {
      wentDown = true
      break
    }
    await sleep(intervalMs)
  }
  void wentDown

  // Phase 2 — wait for it to answer again.
  while (elapsed() < timeoutMs) {
    try {
      await probe()
      return true
    } catch {
      await sleep(intervalMs)
    }
  }
  return false
}

export type RestoreStatus = 'waiting' | 'starting'

export interface RestoreServiceDeps {
  /** Resolves with the answering data host's instance id (undefined = old service build). */
  ping: () => Promise<string | undefined>
  /** Asks the host app to start the service (no prompt). Resolves true when it is listening. */
  startService: () => Promise<boolean>
  /** The instance that was answering BEFORE the change, when known. */
  previousInstance?: string
  /** Reports what is really happening, for the dialog's text. */
  onStatus?: (status: RestoreStatus, startAttempt: number) => void
  /** Give up (resolve ok:false) after this long in total. */
  timeoutMs?: number
  intervalMs?: number
  /** How long the old instance may keep answering before it is taken as "already reloaded". */
  sameInstanceGraceMs?: number
  /** How long the service may stay silent before we start it ourselves. */
  startAfterMs?: number
  /** Minimum gap between two start attempts. */
  startRetryMs?: number
  sleep?: (ms: number) => Promise<void>
  now?: () => number
}

export interface RestoreResult {
  ok: boolean
  /** How many times the service was started by us (0 = it came back / never went down). */
  startAttempts: number
}

/**
 * After a library change the service rebuilds its data host (normally without ever going
 * down). Resolves ok once a REBUILT instance answers. Never just waits for a service that is
 * gone: when nothing answers for `startAfterMs` it asks the host app to start the service
 * (again every `startRetryMs`) while it keeps probing, and only then - after `timeoutMs` -
 * reports failure.
 */
export async function restoreServiceAfterChange(deps: RestoreServiceDeps): Promise<RestoreResult> {
  const {
    ping, startService, previousInstance, onStatus,
    timeoutMs = 60_000, intervalMs = 400, sameInstanceGraceMs = 8_000,
    startAfterMs = 4_000, startRetryMs = 5_000,
    sleep = realSleep, now = Date.now,
  } = deps
  const start = now()
  let silentSince: number | null = null
  let lastStartAt = -Infinity
  let startAttempts = 0
  onStatus?.('waiting', 0)

  while (now() - start < timeoutMs) {
    try {
      const instance = await ping()
      const elapsed = now() - start
      const rebuilt = previousInstance === undefined || instance === undefined || instance !== previousInstance
      // The same instance may keep answering for a moment before it goes down; a service that
      // never reports an instance id, or a reload too fast to observe, is accepted after the grace.
      if (rebuilt || elapsed >= sameInstanceGraceMs) return { ok: true, startAttempts }
      silentSince = null
      onStatus?.('waiting', startAttempts)
    } catch {
      const t = now()
      silentSince ??= t
      if (t - silentSince >= startAfterMs && t - lastStartAt >= startRetryMs) {
        lastStartAt = t
        startAttempts++
        onStatus?.('starting', startAttempts)
        try { await startService() } catch { /* the probes below tell whether it worked */ }
      }
    }
    await sleep(intervalMs)
  }
  return { ok: false, startAttempts }
}

/** Short Hebrew wording for a service-side `reason` string of a library that is not attached. */
export function additionalLibraryReasonText(reason: string | undefined): string | null {
  if (!reason) return null
  if (reason.startsWith('file not found'))
    return 'הקובץ לא נמצא (או שאינו נגיש לשירות — ייתכן נתיב של כונן רשת/משתמש אחר)'
  if (reason.startsWith('file not accessible')) return 'הקובץ אינו נגיש לשירות'
  if (reason.startsWith('missing required table'))
    return 'חסרות בקובץ טבלאות חובה של סכמת seforim.db'
  if (reason.startsWith('required tables have unexpected columns'))
    return 'מבנה העמודות בקובץ אינו תואם לסכמת seforim.db'
  if (reason.startsWith('ids in table')) {
    // The service names the table: "ids in table 'line' exceed the per-library id range".
    const table = /ids in table '([^']+)'/.exec(reason)?.[1]
    const what = table === 'line' ? 'שורות' : table === 'book' ? 'ספרים' : table === 'category' ? 'קטגוריות' : 'פריטים'
    // Hebrew only: a Latin word in the middle of an RTL sentence is reordered by the bidi algorithm.
    return `מספרי ה${what} בקובץ גדולים מהטווח שמותר לספרייה נוספת (מספרים גבוהים מדי). אם קיימת גרסה דחוסה או קטנה יותר של אותה ספרייה, השתמשו בה`
  }
  if (reason.startsWith('cannot be opened')) return 'הקובץ אינו מסד SQLite תקין'
  if (reason.startsWith('no line text') || reason.startsWith('line text unreadable'))
    return 'לא ניתן לקרוא את טקסט השורות של הספרייה'
  if (reason.startsWith('full-text search skipped'))
    return 'הספרייה פגומה (קובץ משובש או קטוע) — החיפוש בתוכן מדלג עליה, וחיפוש שאר הספריות לא נפגע'
  if (reason.startsWith('not loaded yet')) return 'ממתינה לטעינה מחדש (מתבצעת עכשיו)'
  return 'לא נטענה: ' + reason
}

/** Hebrew text for an `error` code of an add/pick/remove result. */
export function additionalLibraryErrorText(code: string): string {
  switch (code) {
    case 'empty path':
      return 'לא נבחר נתיב'
    case "path contains ';' or '|'":
      return "הנתיב מכיל תו שאינו נתמך (';' או '|')"
    case 'already configured':
      return 'הספרייה הזו כבר מוגדרת'
    case 'no more library slots available':
      return 'הגעת למספר המרבי של ספריות נוספות שניתן להוסיף בהתקנה זו'
    case 'not configured':
      return 'הספרייה אינה מוגדרת עוד'
    default:
      if (code.startsWith('could not save the setting'))
        return 'לא ניתן היה לשמור את ההגדרה (כתיבה לרישום נכשלה)'
      return 'הפעולה נכשלה: ' + code
  }
}

/** Human text for a thrown failure (timeout, service unavailable, anything else). */
export function describeFailure(err: unknown): string {
  const name = err instanceof Error ? err.name : ''
  if (name === 'ServiceCallTimeoutError') return 'השירות לא ענה בזמן'
  if (name === 'ServiceUnavailableError') return 'שירות הנתונים אינו זמין כרגע'
  const message = err instanceof Error ? err.message : String(err)
  return message ? 'שגיאה: ' + message : 'שגיאה לא ידועה'
}
