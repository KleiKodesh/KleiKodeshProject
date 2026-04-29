import { useTabStore } from '@/stores/tabStore'
import { usePdfStore } from '@/stores/pdfStore'
import { pickFile } from '@/webview-host/bridge'
import type { TabRoute } from '@/stores/tabStore'

/**
 * Central navigation handler for all app destinations.
 * Singletons are routed via navigateToSingleton (enforces one-tab rule + closes current).
 * Multi-instance pages use updateActiveTab (in-place navigation).
 * Side-effects (file picker, external links) are handled here too.
 */
export function useAppNavigation() {
  const tabStore = useTabStore()
  const pdfStore = usePdfStore()

  const SINGLETON_ROUTES: Partial<Record<string, TabRoute>> = {
    ספרים: '/books',
    הגדרות: '/settings',
    'היברו-בוקס': '/hebrewbooks',
    'סביבות עבודה': '/workspaces',
    'לוח שנה': '/hebrew-calendar',
    מילון: '/dictionary',
    'מידות ושיעורים': '/midot',
  }

  // ── Shared side-effect actions ────────────────────────────────────────────

  async function handleFilePicker(newTab: boolean): Promise<void> {
    const result = await pickFile()
    // In hosted mode, push events handle navigation — pickFile() returns null.
    // In dev mode, navigate directly with the blob URL.
    if (!result) return
    const tabData = {
      route: '/pdf-view' as TabRoute,
      title: result.fileName,
      pdfFileName: result.fileName,
      pdfFilePath: result.filePath,
      pdfVirtualUrl: result.url,
      pdfConverting: false,
    }
    if (newTab) tabStore.openTab(tabData)
    else tabStore.updateActiveTab(tabData)
  }

  function handleExternalLink(): void {
    window.open('https://zayitapp.com/#/download', '_blank')
  }

  function handleDbPicker(): void {
    window.__webviewPickDbPath?.()
  }

  // ── Public navigation functions ───────────────────────────────────────────

  async function navigate(label: string): Promise<void> {
    const singleton = SINGLETON_ROUTES[label]
    if (singleton) {
      tabStore.navigateToSingleton(singleton)
      return
    }
    if (label === 'חיפוש') {
      tabStore.updateActiveTab({ route: '/search', title: 'חיפוש' })
      return
    }
    if (label === 'פתח קובץ') { await handleFilePicker(false); return }
    if (label === 'התקן כזית') { handleExternalLink(); return }
    if (label === 'בחר מסד נתונים') { handleDbPicker(); return }
  }

  async function navigateInNewTab(label: string): Promise<void> {
    const singleton = SINGLETON_ROUTES[label]
    if (singleton) {
      // If a singleton tab already exists, just switch to it (don't close current).
      // Otherwise open a brand new tab for it.
      const existing = tabStore.tabs.find((t) => t.route === singleton)
      if (existing) tabStore.switchTab(existing.id)
      else tabStore.openTab({ route: singleton, title: label })
      return
    }
    if (label === 'חיפוש') {
      tabStore.openTab({ route: '/search', title: 'חיפוש' })
      return
    }
    if (label === 'פתח קובץ') { await handleFilePicker(true); return }
    if (label === 'התקן כזית') { handleExternalLink(); return }
    if (label === 'בחר מסד נתונים') { handleDbPicker(); return }
  }

  return { navigate, navigateInNewTab }
}
