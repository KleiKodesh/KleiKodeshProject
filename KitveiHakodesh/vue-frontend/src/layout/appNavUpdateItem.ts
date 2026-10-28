import { computed, type Component } from 'vue'
import { IconArrowDownload24Regular, IconArrowSync24Regular } from '@iconify-prerendered/vue-fluent'
import { t } from '@/i18n'
import { useUpdateNavState, type UpdateNavTone } from '@/composables/useUpdateNavState'

/** The rail / overflow key of the update row. Not a destination, so not a label. */
export const APP_NAV_UPDATE_KEY = 'update'

export interface AppNavUpdateItem {
  tone: UpdateNavTone
  /** The row text. */
  text: string
  /** The tooltip: the text plus what a click does. */
  title: string
  icon: Component
  /** The colour of the icon, the hamburger dot and the row's edge. */
  color: string
  /** Nothing to click while downloading or installing. */
  disabled: boolean
}

const TONE_COLORS: Record<UpdateNavTone, string> = {
  ready: 'var(--accent-color)',
  aging: '#e8871e',
  overdue: '#d13438',
  downloading: 'var(--text-secondary)',
  installing: 'var(--text-secondary)',
  failed: '#d13438',
}

/**
 * The update row's face, one for every nav surface (AppTitleBarNavDropdown, AppNavSidebar,
 * AppNavSidebarOverflowMenu, the hamburger's dot). Null while there is nothing to show.
 * Kept beside appNavItems for the same reason that list exists: the surfaces must agree.
 */
export function useAppNavUpdateItem() {
  const state = useUpdateNavState()
  const item = computed<AppNavUpdateItem | null>(() => {
    const tone = state.tone.value
    if (!tone) return null
    const version = state.readyVersion.value ?? state.downloadingVersion.value
    const text =
      tone === 'downloading'
        ? t('layout.nav.update.downloading', { version, percent: state.downloadPercent.value })
        : tone === 'installing'
          ? t('layout.nav.update.installing')
          : tone === 'failed'
            ? t('layout.nav.update.failed')
            : t('layout.nav.update.ready', { version })
    const hint =
      tone === 'failed'
        ? t('layout.nav.update.retryHint')
        : tone === 'ready' || tone === 'aging' || tone === 'overdue'
          ? t('layout.nav.update.installHint')
          : ''
    return {
      tone,
      text,
      title: hint ? `${text}\n${hint}` : text,
      icon: tone === 'downloading' ? IconArrowDownload24Regular : IconArrowSync24Regular,
      color: TONE_COLORS[tone],
      disabled: tone === 'downloading' || tone === 'installing',
    }
  })
  return { item, install: state.installUpdate }
}
