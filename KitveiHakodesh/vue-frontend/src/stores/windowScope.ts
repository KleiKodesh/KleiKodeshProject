declare global {
  interface Window {
    /** This desktop-app window's id, injected by the C# host (MainForm / AppViewer.WindowId). */
    __webviewWindowId?: string
  }
}

/**
 * Which desktop-app window this page is. The desktop app can show several windows, and
 * each keeps its own tabs, recents and per-tab state, so every workspace-scoped storage
 * key carries the window's id (see `windowScopedId`).
 *
 * The first window, and every other host (Word task pane, dev browser), has '' and uses
 * the plain workspace id - exactly what single-window builds wrote, so nothing saved
 * before needs moving.
 */
export const windowId: string =
  typeof window !== 'undefined' && typeof window.__webviewWindowId === 'string' ? window.__webviewWindowId : ''

/** The storage id of a workspace inside one window: `wsId` for window '', else `wsId@win`. */
export function windowScopedId(wsId: string, win: string = windowId): string {
  return win ? `${wsId}@${win}` : wsId
}
