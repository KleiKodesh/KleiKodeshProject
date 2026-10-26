import { computed, inject, type Component } from 'vue'
import {
  IconDatabase24Filled,
  IconArrowDownload24Filled,
} from '@iconify-prerendered/vue-fluent'
import { documentIcon, type DocumentIconKey } from '@/utils/documentIcons'
import { dbReady } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import type { HomeSuggestionCard } from './homeSuggestionCard'
import { APP_NAV_ITEMS, APP_NAV_SETTINGS_ITEM, HAMESHIV_LABEL } from '@/layout/appNavItems'
import { useHameshivReachable } from '@/composables/useHameshivReachable'
import { t } from '@/i18n'
import { useSeforimLibraryDownload } from '@/composables/useSeforimLibraryDownload'

/** A destination's icon + colour, taken from the shared table (utils/documentIcons). */
function destinationIcon(key: DocumentIconKey): { icon: Component; color: string } {
  const icon = documentIcon(key)
  return { icon: icon.icon24, color: icon.color }
}

/** A shared destination: label (route key) + translated text + description from appNavItems, icon from the shared table. */
function navDestination(key: DocumentIconKey, label: string) {
  const item = [...APP_NAV_ITEMS.value, APP_NAV_SETTINGS_ITEM].find((navItem) => navItem.label === label)
  return { label, text: item?.text ?? label, description: item?.description ?? '', ...destinationIcon(key) }
}

/**
 * The navigation destinations as home cards.
 *
 * Hidden while this pane's nav rail is showing — the rail offers every one of them
 * on every page (see README). The first two swap when no database is available.
 * Keep the list in sync with `AppTitleBarNavDropdown.vue`; neither is derived from
 * the other.
 */
export function useHomeNavCards(navigate: (label: string) => unknown) {
  const settingsStore = useSettingsStore()
  const paneId = inject<1 | 2>('paneId', 1)
  const hameshivReachable = useHameshivReachable()
  // The download card's subtitle becomes the live status once a download has anything to say.
  const libraryDownload = useSeforimLibraryDownload()

  return computed<HomeSuggestionCard[]>(() => {
    if (settingsStore.getNavSidebarVisible(paneId)) return []
    const dbMissing = !dbReady.value
    const destinations: { label: string; text: string; description: string; icon: Component; color: string }[] = [
      dbMissing
        ? { label: 'הורד מסד ספרים', text: t('layout.dbDownload'), description: libraryDownload.statusText.value || t('layout.dbDownloadDescription'), icon: IconArrowDownload24Filled, color: '#B5451B' }
        : navDestination('library', 'קטלוג הספרים'),
      dbMissing
        ? { label: 'בחר מסד ספרים', text: t('layout.dbChoose'), description: t('layout.dbChooseDescription'), icon: IconDatabase24Filled, color: '#3478f6' }
        : navDestination('search', 'חיפוש'),
      navDestination('hbooks', 'היברו-בוקס'),
      navDestination('folder', 'פתח קובץ'),
      navDestination('fileSearch', 'חיפוש קבצים'),
      navDestination('dict', 'מילון'),
      navDestination('calendar', 'לוח שנה'),
      navDestination('ruler', 'מידות ושיעורים'),
      ...(hameshivReachable.value ? [navDestination('hameshiv', HAMESHIV_LABEL)] : []),
      navDestination('settings', 'הגדרות'),
    ]
    return destinations.map((destination) => ({
      key: `nav:${destination.label}`,
      title: destination.text,
      // Shown in the card's muted subtitle line (.card-path).
      path: destination.description,
      icon: destination.icon,
      color: destination.color,
      open: () => void navigate(destination.label),
    }))
  })
}
