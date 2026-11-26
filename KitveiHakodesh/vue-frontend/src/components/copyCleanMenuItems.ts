import { IconTextClearFormatting20Regular } from '@iconify-prerendered/vue-fluent'
import { PunctuationMarksIcon, CantillationMarksIcon } from '@/components/textGlyphIcon'
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
      label: t('bookView.copy.stripNikkud'), icon: IconTextClearFormatting20Regular,
      get checked() { return settingsStore.copyStripNikkud },
      onChange: (value: boolean) => { settingsStore.copyStripNikkud = value },
    },
    {
      type: 'checkbox',
      label: t('bookView.copy.stripTeamim'), icon: CantillationMarksIcon,
      get checked() { return settingsStore.copyStripTeamim },
      onChange: (value: boolean) => { settingsStore.copyStripTeamim = value },
    },
    {
      type: 'checkbox',
      label: t('bookView.copy.stripModernPunctuation'), icon: PunctuationMarksIcon,
      get checked() { return settingsStore.copyStripModernPunctuation },
      onChange: (value: boolean) => { settingsStore.copyStripModernPunctuation = value },
    },
  ]
}
