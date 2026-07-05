import { inject } from 'vue'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { pickLocalFile } from '@/webview-host/bridge'
import type { TabRoute } from '@/stores/tabStore'

/**
 * Central navigation handler for all app destinations.
 * Singletons are routed via navigateToSingleton (enforces one-tab rule + closes current).
 * Multi-instance pages use updateActiveTab (in-place navigation).
 * Side-effects (file picker, external links) are handled here too.
 *
 * Uses the PANE_NAVIGATION_KEY injection provided by AppShell so all navigation
 * operates on the correct pane's tab set. Defaults to pane 1 when outside a shell.
 */
export function useAppNavigation() {
  const pane = usePaneNavigation()

  const SINGLETON_ROUTES: Partial<Record<string, TabRoute>> = {
    ספרים: '/books',
    הגדרות: '/settings',
    'היברו-בוקס': '/hebrewbooks',
    'סביבות עבודה': '/workspaces',
    'לוח שנה': '/hebrew-calendar',
    מילון: '/dictionary',
    'מידות ושיעורים': '/midot',
    'חיפוש קבצים': '/file-search',
  }

  // ── Shared side-effect actions ────────────────────────────────────────────

  async function handleFilePicker(newTab: boolean): Promise<void> {
    const result = await pickLocalFile()
    // In hosted mode, push events handle navigation — pickLocalFile() returns null.
    // In dev mode, navigate directly with the blob URL.
    if (!result) return
    const fn = result.fileName ?? ''
    const ext = fn.substring(fn.lastIndexOf('.')).toLowerCase()
    const isTxt = ext === '.txt'
    const isHtmlLike = ext === '.htm' || ext === '.html'
    const route = isTxt ? '/txt-view' : isHtmlLike ? '/html-view' : '/pdf-view'
    const tabData = {
      route: route as TabRoute,
      title: result.fileName,
      localFileName: result.fileName,
      localFilePath: result.filePath,
      localFileVirtualUrl: result.url,
      localFileConverting: false,
    }
    if (newTab) pane.openTab(tabData)
    else pane.updateActiveTab(tabData)
  }

  // NOTE: "זית" (Zayit) here refers to the external Zayit app (zayitapp.com) — a separate
  // Torah study program whose database this app can use. This is NOT this app's old name.
  // Do not rename or remove this URL.
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
      pane.navigateToSingleton(singleton)
      return
    }
    if (label === 'חיפוש') {
      pane.updateActiveTab({ route: '/search', title: 'חיפוש' })
      return
    }
    if (label === 'פתח קובץ') { await handleFilePicker(false); return }
    if (label === 'התקן כתבי הקודש') { handleExternalLink(); return }
    if (label === 'הורד מסד ספרים') { handleExternalLink(); return }
    if (label === 'בחר מסד ספרים' || label === 'בחר מסד נתונים') { handleDbPicker(); return }
  }

  async function navigateInNewTab(label: string): Promise<void> {
    const singleton = SINGLETON_ROUTES[label]
    if (singleton) {
      // Singleton in new-tab context: switch to existing pane-scoped tab or open a new one
      const existing = pane.tabs.find((t) => t.route === singleton)
      if (existing) pane.switchTab(existing.id)
      else pane.openTab({ route: singleton, title: label })
      return
    }
    if (label === 'חיפוש') {
      pane.openTab({ route: '/search', title: 'חיפוש' })
      return
    }
    if (label === 'פתח קובץ') { await handleFilePicker(true); return }
    if (label === 'התקן כתבי הקודש') { handleExternalLink(); return }
    if (label === 'הורד מסד ספרים') { handleExternalLink(); return }
    if (label === 'בחר מסד ספרים' || label === 'בחר מסד נתונים') { handleDbPicker(); return }
  }

  return { navigate, navigateInNewTab }
}
