import { computed, inject, type Component } from 'vue'
import {
  IconDatabase24Filled,
  IconArrowDownload24Filled,
} from '@iconify-prerendered/vue-fluent'
import { documentIcon, type DocumentIconKey } from '@/utils/documentIcons'
import { dbReady } from '@/webview-host/seforimDb'
import { useSettingsStore } from '@/stores/settingsStore'
import type { HomeSuggestionCard } from './homeSuggestionCard'

/** A destination's icon + colour, taken from the shared table (utils/documentIcons). */
function destinationIcon(key: DocumentIconKey): { icon: Component; color: string } {
  const icon = documentIcon(key)
  return { icon: icon.icon24, color: icon.color }
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

  return computed<HomeSuggestionCard[]>(() => {
    if (settingsStore.getNavSidebarVisible(paneId)) return []
    const dbMissing = !dbReady.value
    const destinations: { label: string; icon: Component; color: string }[] = [
      dbMissing
        ? { label: 'הורד מסד ספרים', icon: IconArrowDownload24Filled, color: '#B5451B' }
        : { label: 'קטלוג הספרים', ...destinationIcon('library') },
      dbMissing
        ? { label: 'בחר מסד ספרים', icon: IconDatabase24Filled, color: '#3478f6' }
        : { label: 'חיפוש', ...destinationIcon('search') },
      { label: 'היברו-בוקס', ...destinationIcon('hbooks') },
      { label: 'פתח קובץ', ...destinationIcon('folder') },
      { label: 'חיפוש קבצים', ...destinationIcon('fileSearch') },
      { label: 'מילון', ...destinationIcon('dict') },
      { label: 'לוח שנה', ...destinationIcon('calendar') },
      { label: 'מידות ושיעורים', ...destinationIcon('ruler') },
      { label: 'הגדרות', ...destinationIcon('settings') },
    ]
    return destinations.map((destination) => ({
      key: `nav:${destination.label}`,
      title: destination.label,
      path: '',
      icon: destination.icon,
      color: destination.color,
      open: () => void navigate(destination.label),
    }))
  })
}
