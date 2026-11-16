/**
 * The current key combination of a shortcut, as text for tooltips and menus.
 *
 * Tooltips used to spell the key into the translated string ("Table of contents (Ctrl+K)").
 * Once the user can change shortcuts that would lie, so the strings carry only the words
 * and the key is added here from the shortcuts store. Called during render, so a change in
 * settings updates every tooltip at once (the store read is reactive).
 */
import { t } from '@/i18n'
import { comboText } from './shortcutCombo'
import { useShortcutsStore } from './shortcutsStore'

const arrows = () => ({
  left: t('settings.shortcutsList.leftArrow'),
  right: t('settings.shortcutsList.rightArrow'),
  up: t('settings.shortcutsList.upArrow'),
  down: t('settings.shortcutsList.downArrow'),
})

/** "Ctrl+K", or '' when the user removed the shortcut. */
export function shortcutLabel(id: string): string {
  const combo = useShortcutsStore().combosFor(id)[0]
  return combo ? comboText(combo, arrows()) : ''
}

/** `text (Ctrl+K)`, or just `text` when the shortcut has no combination. */
export function withShortcut(text: string, id: string): string {
  const key = shortcutLabel(id)
  return key ? `${text} (${key})` : text
}
