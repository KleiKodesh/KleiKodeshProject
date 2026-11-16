import type { ContextMenuCheckboxItem } from '@/components/ContextMenu.vue'
import { t } from '@/i18n'
import { useSettingsStore } from '@/stores/settingsStore'

/**
 * The three copy-cleaning checkboxes, shared by every copy menu (book view, commentary,
 * FastDocView, the global fallback) so they read and write the same settings.
 * Copy cleaning is independent of the toolbar's clean-text view: settingsStore.copyCleanFlags.
 */
export function copyCleanMenuItems(): ContextMenuCheckboxItem[] {
  const settingsStore = useSettingsStore()
  return [
    {
      type: 'checkbox',
      label: t('bookView.copy.stripNikkud'),
      get checked() { return settingsStore.copyStripNikkud },
      onChange: (value: boolean) => { settingsStore.copyStripNikkud = value },
    },
    {
      type: 'checkbox',
      label: t('bookView.copy.stripTeamim'),
      get checked() { return settingsStore.copyStripTeamim },
      onChange: (value: boolean) => { settingsStore.copyStripTeamim = value },
    },
    {
      type: 'checkbox',
      label: t('bookView.copy.stripModernPunctuation'),
      get checked() { return settingsStore.copyStripModernPunctuation },
      onChange: (value: boolean) => { settingsStore.copyStripModernPunctuation = value },
    },
  ]
}
