/**
 * Whether a message came from inside a Torah website's iframe (SiteFrameLayer's .site-frame),
 * at any depth: `parent` stays readable across origins, so the sender's chain of windows is
 * walked up to the app.
 */
export function isFromWebsiteFrame(source: MessageEventSource | null): boolean {
  if (!source || !('parent' in source)) return false
  const siteWindows = [...document.querySelectorAll<HTMLIFrameElement>('iframe.site-frame')].map((f) => f.contentWindow)
  let w: Window | null = source as Window
  for (let depth = 0; w && w !== window && depth < 10; depth++) {
    if (siteWindows.includes(w)) return true
    w = w.parent === w ? null : w.parent
  }
  return false
}
