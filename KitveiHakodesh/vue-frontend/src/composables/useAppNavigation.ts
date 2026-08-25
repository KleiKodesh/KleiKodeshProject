import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { pickLocalFile } from '@/webview-host/bridge'
import { useLocalFileStore } from '@/stores/localFileStore'
import type { TabRoute } from '@/stores/tabStore'

/**
 * Central navigation handler for all app destinations.
 * Tool pages (settings, dictionary, calendar…) route via navigateToDestination;
 * everything else uses updateActiveTab. Both navigate IN PLACE by default — a tab
 * shows one thing at a time, and nothing here is unique across tabs.
 * Side-effects (file picker, external links) are handled here too.
 *
 * Uses the PANE_NAVIGATION_KEY injection provided by AppShell so all navigation
 * operates on the correct pane's tab set. Defaults to pane 1 when outside a shell.
 */
export function useAppNavigation() {
  const pane = usePaneNavigation()

  const DESTINATION_ROUTES: Partial<Record<string, TabRoute>> = {
    'קטלוג הספרים': '/books',
    הגדרות: '/settings',
    'היברו-בוקס': '/hebrewbooks',
    'לוח שנה': '/hebrew-calendar',
    מילון: '/dictionary',
    'מידות ושיעורים': '/midot',
    'חיפוש קבצים': '/file-search',
  }

  // ── Shared side-effect actions ────────────────────────────────────────────

  /**
   * Opens the file picker. `initialDir` starts it in a specific folder — the home
   * page's frequent-folder tiles pass one; everything else opens where the shell
   * would.
   */
  async function handleFilePicker(newTab: boolean, initialDir = ''): Promise<void> {
    const result = await pickLocalFile(newTab, initialDir)
    if (!result) return
    // Navigation is driven by push events in BOTH modes (pane-aware in localFileStore, so
    // the file opens only in the pane that initiated the pick): hosted gets real C# pushes,
    // dev replays the same events after its service round-trips (see pickLocalFile). We must
    // NOT navigate again here or the file would also open in pane 1. The reply finalizes a
    // conversion whose placeholder is still up (cached hosted conversions push no
    // conversionReady; dev conversions finalize from the reply by design).
    useLocalFileStore().finalizeConvertingFromReply(result)
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
    const destination = DESTINATION_ROUTES[label]
    if (destination) {
      pane.navigateToDestination(destination)
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
    const destination = DESTINATION_ROUTES[label]
    if (destination) {
      // openInNewTab=true: opens a new tab unless the current tab is home (/), in
      // which case it replaces in-place rather than leaving an empty home behind.
      pane.navigateToDestination(destination, true)
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

  /**
   * Opens the file dialog already pointed at `folderPath` — the frequent-folder
   * tiles' action. Ctrl/middle-click opens the chosen file in a new tab, matching
   * how every other tile treats the modifier.
   */
  function openFolderPicker(folderPath: string, newTab = false): Promise<void> {
    return handleFilePicker(newTab, folderPath)
  }

  return { navigate, navigateInNewTab, openFolderPicker }
}
