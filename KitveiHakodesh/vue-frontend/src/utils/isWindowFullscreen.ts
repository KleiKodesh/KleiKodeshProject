import { activeHostWindow } from '@/composables/detachedWindow/hostWindow'
/**
 * True while the host window fills the whole screen (F11). The tab strip is hidden
 * then, so destinations take over the current tab like in the VSTO task pane.
 * Same check ClockWidget uses.
 */
export function isWindowFullscreen(): boolean {
  const host = activeHostWindow()
  return host.innerHeight >= host.screen.height
}
