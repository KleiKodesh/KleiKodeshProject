import { computed, reactive, type ComputedRef } from 'vue'
import { useOnline } from '@vueuse/core'

/**
 * Whether a website can be reached, for the red broken-link badge on its icon (same badge
 * and same method the Hameshiv nav button used).
 *
 * A no-cors fetch resolves (opaque) whenever the server answered at all and rejects only on
 * a network failure - DNS, refused, offline, or our timeout. One check per site address,
 * shared by every icon of that site. Offline is known at once from the OS online state.
 * Re-checked every minute while unreachable (the badge clears without a restart) and every
 * 10 minutes while reachable.
 */
const TIMEOUT_MS = 8000
const RECHECK_UNREACHABLE_MS = 60_000
const RECHECK_REACHABLE_MS = 10 * 60_000

const answered = reactive<Record<string, boolean>>({})
const checkedAt: Record<string, number> = {}
const inFlight = new Set<string>()
const online = useOnline()

async function check(origin: string): Promise<void> {
  inFlight.add(origin)
  let ok: boolean
  try {
    await fetch(`${origin}/`, { mode: 'no-cors', cache: 'no-store', signal: AbortSignal.timeout(TIMEOUT_MS) })
    ok = true
  } catch {
    ok = false
  }
  inFlight.delete(origin)
  answered[origin] = ok
  checkedAt[origin] = Date.now()
}

function originOf(url: string): string {
  try {
    return new URL(url).origin
  } catch {
    return ''
  }
}

export function useWebsiteUnreachable(url: () => string): ComputedRef<boolean> {
  return computed(() => {
    const origin = originOf(url())
    if (!origin) return false
    if (!online.value) return true
    const last = checkedAt[origin]
    const stale = last === undefined || Date.now() - last > (answered[origin] ? RECHECK_REACHABLE_MS : RECHECK_UNREACHABLE_MS)
    if (stale && !inFlight.has(origin)) void check(origin)
    return answered[origin] === false
  })
}
