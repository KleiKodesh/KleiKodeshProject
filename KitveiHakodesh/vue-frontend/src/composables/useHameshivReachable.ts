import { readonly, ref, type Ref } from 'vue'

/**
 * Whether hameshiv.com is reachable — its nav item is shown only then.
 *
 * Probed once per app session with an image load, NOT fetch: filters such as NetFree
 * answer blocked sites with an HTML page (HTTP 200/418) that a no-cors fetch would
 * count as success, while an image decode fails on it.
 */
const PROBE_URL = 'https://hameshiv.com/static/favicon-48.png'
const PROBE_TIMEOUT_MS = 8000

const reachable = ref(false)
let probed = false

function probe(): void {
  if (probed || typeof Image === 'undefined') return
  probed = true
  const img = new Image()
  const timer = setTimeout(() => finish(false), PROBE_TIMEOUT_MS)
  function finish(ok: boolean) {
    clearTimeout(timer)
    img.onload = img.onerror = null
    if (ok) reachable.value = true
  }
  img.onload = () => finish(img.naturalWidth > 0)
  img.onerror = () => finish(false)
  img.src = `${PROBE_URL}?_=${Date.now()}`
}

export function useHameshivReachable(): Readonly<Ref<boolean>> {
  probe()
  return readonly(reachable)
}
