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
import { useUpdateNavState } from '@/composables/useUpdateNavState'

export function installDevTestHook() {
  const tabStore = useTabStore()
  const bookViewStore = useBookViewStore()

  async function openLocalFile(fullPath: string, asOtzariaAddin = false): Promise<string | null> {
    const fileName = fullPath.split(/[\\/]/).pop() ?? fullPath
    const restored = await restoreLocalFile(fullPath)
    if (!restored?.url) return null
    const route = restored.kind === 'html' ? '/html-view' : '/pdf-view'
    const tab = tabStore.openTab({ route, title: fileName })
    tabStore.updateTab(tab.id, {
      localFileName: fileName,
      localFilePath: fullPath,
      localFileVirtualUrl: restored.url,
      isOtzariaAddin: asOtzariaAddin && route === '/html-view',
    })
    return tab.id
  }

  // Stage the nav update row without a real download: `fakeUpdate('99.9.9', 3)` = ready
  // for 3 days (orange); `fakeUpdate(null)` clears it.
  const updateNav = useUpdateNavState()
  function fakeUpdate(version: string | null, ageDays = 0, state: 'ready' | 'installing' | 'failed' = 'ready') {
    updateNav.devRefs.readyVersion.value = version
    updateNav.devRefs.readySince.value = version ? Date.now() - ageDays * 86_400_000 : null
    updateNav.devRefs.isInstalling.value = state === 'installing'
    updateNav.devRefs.hasInstallFailed.value = state === 'failed'
  }

  ;(window as unknown as Record<string, unknown>).__khTest = {
    openLocalFile,
    fakeUpdate,
    tabStore,
    bookViewStore,
  }
}
