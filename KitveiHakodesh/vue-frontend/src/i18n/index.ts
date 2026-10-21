import { createI18n } from 'vue-i18n'

// UI language only. The layout stays RTL in every language; book text is never translated.
//
// Messages live in locales/<lang>/<area>.json and are merged under the file name as a
// namespace: locales/en/bookView.json → t('bookView.someKey'). One file per area keeps
// parallel edits apart. Hebrew is the source language and the fallback for any key an
// English file is missing.
//
// Switching language reloads the page (setUiLanguage), so strings built once at module
// load (menus, constant tables) are always in the active language.
//
// The CATALOG page is never translated and always RTL (user decision): locales/en/bookCatalog.json
// is deliberately an exact copy of the Hebrew file, and shared strings rendered inside the
// catalog (new-tab hint, index status) use `tHe`.

export type UiLanguage = 'he' | 'en'

const STORAGE_KEY = 'uiLanguage'

function readUiLanguage(): UiLanguage {
  try {
    return localStorage.getItem(STORAGE_KEY) === 'en' ? 'en' : 'he'
  } catch {
    return 'he'
  }
}

const files = import.meta.glob<{ default: Record<string, any> }>('./locales/*/*.json', { eager: true })
const messages: Record<UiLanguage, Record<string, any>> = { he: {}, en: {} }
for (const [path, mod] of Object.entries(files)) {
  const [, lang, area] = path.match(/\.\/locales\/(\w+)\/(\w+)\.json$/) ?? []
  if (area && (lang === 'he' || lang === 'en')) messages[lang][area] = mod.default
}

export const uiLanguage = readUiLanguage()

export const i18n = createI18n({
  legacy: false,
  locale: uiLanguage,
  fallbackLocale: 'he',
  messages,
  missingWarn: false,
  fallbackWarn: false,
})

/** For plain .ts modules; components use `useI18n()` or `$t` in templates. */
export const t: (key: string, named?: Record<string, unknown>) => string = (key, named) =>
  (i18n.global as unknown as { t: (k: string, n?: Record<string, unknown>) => string }).t(key, named ?? {})

/** Hebrew regardless of UI language - for strings shown on the never-translated catalog page. */
export const tHe: (key: string, named?: Record<string, unknown>) => string = (key, named) =>
  (i18n.global as unknown as { t: (k: string, n: Record<string, unknown>, o: { locale: string }) => string }).t(
    key,
    named ?? {},
    { locale: 'he' },
  )

export function setUiLanguage(lang: UiLanguage): void {
  if (lang === uiLanguage) return
  try {
    localStorage.setItem(STORAGE_KEY, lang)
  } catch {
    return
  }
  location.reload()
}
