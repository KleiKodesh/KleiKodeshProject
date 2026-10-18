import { watch } from 'vue'
import { useTabStore } from './tabStore'
import { useLocalFileStore } from './localFileStore'

/**
 * Session restore for the local-file tabs (/pdf-view, /html-view) that survived from
 * the previous session, done lazily.
 *
 * Restoring a file tab is real work — a HebrewBooks download or a virtual-host grant
 * on the C# side — so at boot only the tab the reader actually sees in each pane is
 * restored. Every other persisted file tab keeps its title and icon in the strip and
 * is restored through the same `restoreTab` path the first time it becomes active.
 * Closing a tab that was never restored is safe: its view never mounted and nothing
 * was registered for it. Tabs opened later in the session are not this module's
 * business — their own open path fills the URL in.
 */
export function restoreLocalFileTabsLazily(): void {
  const tabStore = useTabStore()
  const localFileStore = useLocalFileStore()
  const awaitingRestore = new Set(
    tabStore.tabs
      .filter((t) => t.route === '/pdf-view' || t.route === '/html-view')
      .map((t) => t.id),
  )
  if (awaitingRestore.size === 0) return

  function restoreIfAwaiting(tabId: string) {
    if (!awaitingRestore.delete(tabId)) return
    void localFileStore.restoreTab(tabId)
  }

  const stop = watch(
    [() => tabStore.activeTabId, () => tabStore.pane2ActiveTabId],
    ([activeId, pane2ActiveId]) => {
      restoreIfAwaiting(activeId)
      restoreIfAwaiting(pane2ActiveId)
      if (awaitingRestore.size === 0) stop()
    },
    { immediate: true },
  )
}
