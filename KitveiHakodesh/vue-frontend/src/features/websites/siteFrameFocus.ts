import { reportSiteFrameFocus } from '@/webview-host/bridge'
import { activeHostWindow } from '@/composables/detachedWindow/hostWindow'

/**
 * Whether ANY Torah website frame has keyboard focus, reported to the host once per change
 * (AppViewerSiteShortcuts.cs takes the app's shortcut keys while it does). One app-wide state, so
 * two web-view tabs cannot leave the host flag wrong when focus moves between them.
 */
let reported = false

/** Re-reads which element has focus; call after any focus change that may involve a site frame. */
export function refreshSiteFrameFocus(): void {
  // The window in use: a site frame in a detached window counts as well.
  const active = activeHostWindow().document.activeElement
  const focused = active instanceof HTMLIFrameElement && active.classList.contains('site-frame')
  if (focused === reported) return
  reported = focused
  reportSiteFrameFocus(focused)
}
