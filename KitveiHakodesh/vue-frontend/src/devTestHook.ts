/**
 * DEV-only window hook for the Playwright live rigs.
 *
 * Loaded lazily from main.ts behind `import.meta.env.DEV` — never part of a
 * production build. Exposes just enough to drive real user flows from a test
 * (open a local file into a tab, switch/close tabs, read the PDF unsaved-edit
 * state) without scraping UI that varies between dev and hosted chrome.
 */
import { useTabStore } from '@/stores/tabStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { restoreLocalFile } from '@/webview-host/bridge'

export function installDevTestHook() {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()

  async function openLocalFile(fullPath: string): Promise<string | null> {
    const fileName = fullPath.split(/[\\/]/).pop() ?? fullPath
    const restored = await restoreLocalFile(fullPath)
    if (!restored?.url) return null
    const route = restored.kind === 'html' ? '/html-view' : '/pdf-view'
    const tab = tabStore.openTab({ route, title: fileName })
    tabStore.updateTab(tab.id, {
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: restored.url,
      isOtzariaAddin: false,
    })
    return tab.id
  }

  ;(window as unknown as Record<string, unknown>).__khTest = {
    openLocalFile,
    tabStore,
    bookViewStore,
  }
}
