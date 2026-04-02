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
      const tabId = tabStore.activeTabId
      const result = await pickFile()
      if (result) pdfStore.finishLocalFileConversion(tabId, result)
      return
    }
    if (label === 'התקן זית') {
      window.open('https://zayitapp.com/#/download', '_blank')
      return
    }
    if (label === 'בחר מסד נתונים') {
      window.__webviewPickDbPath?.()
    }
  }

  return { navigate }
}
