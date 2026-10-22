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
  if (reason.startsWith('ids in table')) return 'ה־id-ים בקובץ חורגים מהטווח המותר לספרייה נוספת'
  if (reason.startsWith('cannot be opened')) return 'הקובץ אינו מסד SQLite תקין'
  if (reason.startsWith('no line text') || reason.startsWith('line text unreadable'))
    return 'לא ניתן לקרוא את טקסט השורות של הספרייה'
  if (reason.startsWith('full-text search skipped'))
    return 'הספרייה פגומה (קובץ משובש או קטוע) — החיפוש בתוכן מדלג עליה, וחיפוש שאר הספריות לא נפגע'
  if (reason.startsWith('not loaded yet')) return 'ממתינה להפעלה מחדש של השירות'
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
