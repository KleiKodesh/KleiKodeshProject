/**
 * Whether a message came from inside a Torah website's iframe (SiteFrameLayer's .site-frame),
 * at any depth: `parent` stays readable across origins, so the sender's chain of windows is
 * walked up to the app.
 */
import { allHostWindows } from '@/composables/detachedWindow/hostWindow'

export function isFromWebsiteFrame(source: MessageEventSource | null): boolean {
  if (!source || !('parent' in source)) return false
  // Site frames may sit in the app's window or in any detached window.
  const hostWindows = allHostWindows()
  const siteWindows = hostWindows.flatMap((host) =>
    [...host.document.querySelectorAll<HTMLIFrameElement>('iframe.site-frame')].map((f) => f.contentWindow))
  let w: Window | null = source as Window
  for (let depth = 0; w && !hostWindows.includes(w as never) && depth < 10; depth++) {
    if (siteWindows.includes(w)) return true
    w = w.parent === w ? null : w.parent
  }
  return false
}
