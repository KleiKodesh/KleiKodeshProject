import { reportSiteFrameFocus } from '@/webview-host/bridge'

/**
 * Whether ANY Torah website frame has keyboard focus, reported to the host once per change
 * (AppViewerSiteShortcuts.cs takes the app's shortcut keys while it does). One app-wide state, so
 * two web-view tabs in split view cannot leave the host flag wrong when focus moves between them.
 */
let reported = false

/** Re-reads which element has focus; call after any focus change that may involve a site frame. */
export function refreshSiteFrameFocus(): void {
  const focused = document.activeElement instanceof HTMLIFrameElement && document.activeElement.classList.contains('web-view-frame')
  if (focused === reported) return
  reported = focused
  reportSiteFrameFocus(focused)
}
