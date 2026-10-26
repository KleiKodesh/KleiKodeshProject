/**
 * True while the host window fills the whole screen (F11). The tab strip is hidden
 * then, so destinations take over the current tab like in the VSTO task pane.
 * Same check ClockWidget uses.
 */
export function isWindowFullscreen(): boolean {
  return window.innerHeight >= screen.height
}
