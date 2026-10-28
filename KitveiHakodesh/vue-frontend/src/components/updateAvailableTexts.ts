import { t } from '@/i18n'
/** UI texts of the settings page's updates section (SettingsPageUpdatesSection.vue). */
export const UPDATE_PROMPT_TITLE = t('components.update.title')

export function updatePromptBody(version: string): string {
  return t('components.update.body', { version })
}

export const UPDATE_INSTALL_LABEL = t('components.update.installNow')
export const UPDATE_INSTALL_FAILED = t('components.update.installFailed')
