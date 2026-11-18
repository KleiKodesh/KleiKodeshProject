import { reactive, watch } from 'vue'
import { lsGet, lsSet } from '@/utils/persistence'

/**
 * The reader's choices about background websites (Settings > Websites, and the "remember for
 * this site" box on the leave prompt). Stored with the app's other settings, so an app reset
 * clears them.
 */
export type SiteLeaveAction = 'ask' | 'keep' | 'unload'

export interface WebsitesSettings {
  /** What happens when you leave a heavy site's tab. */
  leaveAction: SiteLeaveAction
  /** From this many MB a hidden site counts as heavy. */
  heavyMb: number
  /** Per-site remembered answer to the leave prompt, by origin (https://site). */
  remembered: Record<string, 'keep' | 'unload'>
}

const KEY = 'websites.settings'
export const WEBSITES_SETTINGS_DEFAULTS: WebsitesSettings = { leaveAction: 'ask', heavyMb: 250, remembered: {} }

const stored = lsGet<Partial<WebsitesSettings>>(KEY) ?? {}
export const websitesSettings = reactive<WebsitesSettings>({
  ...WEBSITES_SETTINGS_DEFAULTS,
  ...stored,
  remembered: { ...(stored.remembered ?? {}) },
})

watch(websitesSettings, (value) => lsSet(KEY, value), { deep: true })
