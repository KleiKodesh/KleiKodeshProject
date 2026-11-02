import { computed, ref, watch } from 'vue'
import { useOnline } from '@vueuse/core'

/**
 * Whether hameshiv.com can be reached, for the broken-connection badge on its nav buttons.
 *
 * A real request to the site, not a favicon <img> probe: a no-cors fetch resolves (opaque)
 * whenever the server answered at all and rejects only on a network failure - DNS, refused,
 * offline, or our timeout. The OS online/offline events short-circuit it: offline is known
 * at once, and coming back online re-checks immediately. It re-checks every minute while
 * unreachable (the badge clears without a restart) and every 10 minutes while reachable
 * (the site going down while we stay online still shows).
 *
 * Module-level so every surface (title-bar menu, rail, rail overflow, home tile) shares one
 * check.
 */
const URL = 'https://hameshiv.com/'
const TIMEOUT_MS = 8000
const RECHECK_UNREACHABLE_MS = 60_000
const RECHECK_REACHABLE_MS = 10 * 60_000

const siteAnswered = ref(true)
let started = false
let recheckTimer: ReturnType<typeof setTimeout> | undefined

// Bumped per check: an older fetch still in flight must not overwrite a newer answer.
let generation = 0

async function check(): Promise<void> {
  clearTimeout(recheckTimer)
  const myGeneration = ++generation
  let answered: boolean
  try {
    await fetch(URL, { mode: 'no-cors', cache: 'no-store', signal: AbortSignal.timeout(TIMEOUT_MS) })
    answered = true
  } catch {
    answered = false
  }
  if (myGeneration !== generation) return
  siteAnswered.value = answered
  recheckTimer = setTimeout(() => void check(), answered ? RECHECK_REACHABLE_MS : RECHECK_UNREACHABLE_MS)
}

export function useHameshivUnreachable() {
  const online = useOnline()
  if (!started) {
    started = true
    void check()
    // Never stopped: the check lives as long as the app does.
    watch(online, (isOnline) => { if (isOnline) void check() })
  }
  return computed(() => !online.value || !siteAnswered.value)
}
