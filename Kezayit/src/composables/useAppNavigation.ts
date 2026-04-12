import { useTabStore } from '@/stores/tabStore'
import { usePdfStore } from '@/stores/pdfStore'
import { pickFile } from '@/host/bridge'
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
  }

  async function navigate(label: string) {
    const singleton = SINGLETON_ROUTES[label]
    if (singleton) {
      tabStore.navigateToSingleton(singleton)
      return
    }
    if (label === 'חיפוש') {
      tabStore.updateActiveTab({ route: '/search', title: 'חיפוש' })
      return
    }
    if (label === 'פתח קובץ') {
      const result = await pickFile()
      // In hosted mode, both PDFs (localPdfReady) and Word files (conversionReady) are
      // handled via push events — pickFile() returns null. In dev mode it returns the
      // blob URL directly, so we navigate here.
      if (result) {
        tabStore.updateActiveTab({
          route: '/pdf-view',
          title: result.fileName,
          pdfFileName: result.fileName,
          pdfFilePath: result.filePath,
          pdfVirtualUrl: result.url,
          pdfConverting: false,
        })
      }
      return
    }
    if (label === 'התקן כזית') {
      window.open('https://zayitapp.com/#/download', '_blank')
      return
    }
    if (label === 'בחר מסד נתונים') {
      window.__webviewPickDbPath?.()
    }
  }

  async function navigateInNewTab(label: string) {
    const singleton = SINGLETON_ROUTES[label]
    if (singleton) {
      // If a singleton tab already exists, just switch to it (don't close current).
      // Otherwise open a brand new tab for it.
      const existing = tabStore.tabs.find((t) => t.route === singleton)
      if (existing) {
        tabStore.switchTab(existing.id)
      } else {
        tabStore.openTab({ route: singleton, title: label })
      }
      return
    }
    if (label === 'חיפוש') {
      tabStore.openTab({ route: '/search', title: 'חיפוש' })
      return
    }
    if (label === 'פתח קובץ') {
      const result = await pickFile()
      // In hosted mode push events handle navigation — pickFile() returns null.
      // In dev mode open the result in a new tab.
      if (result) {
        tabStore.openTab({
          route: '/pdf-view',
          title: result.fileName,
          pdfFileName: result.fileName,
          pdfFilePath: result.filePath,
          pdfVirtualUrl: result.url,
          pdfConverting: false,
        })
      }
      return
    }
    if (label === 'התקן כזית') {
      window.open('https://zayitapp.com/#/download', '_blank')
      return
    }
    if (label === 'בחר מסד נתונים') {
      window.__webviewPickDbPath?.()
    }
  }

  return { navigate, navigateInNewTab }
}
