import { ref } from 'vue'
import { idbClearAll, idbScheduleReset } from '@/utils/persistence'
import { resetHostApp } from '@/webview-host/bridge'

/** Set to true just before a reset/reload to block all interaction until the page reloads. */
export const resetting = ref(false)

/**
 * Full app reset: wipe every local database and localStorage key, then reset the
 * host (FTS index, C# settings) and reload into a clean state.
 *
 * Lives here rather than in a store because it belongs to no single domain — it
 * wipes all seven databases plus localStorage, so no one store owns it. It used to
 * hang off `tabStore`, which had nothing to do with any of it.
 *
 * `idbScheduleReset()` first is the crash-safety net: it writes the
 * `__pendingReset` localStorage flag, and `idbCheckAndExecReset()` at boot redoes
 * the wipe if the flag is still set — i.e. if we died partway through. The wipe
 * clears the flag itself, and only after every database is actually gone
 * (`idbClearAll` calls `lsClearAll` last, by design — do not reorder it).
 */
export async function resetEverything(): Promise<void> {
  resetting.value = true
  idbScheduleReset()
  await idbClearAll()
  await resetHostApp()
}
