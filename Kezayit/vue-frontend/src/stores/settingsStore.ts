import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import type { Ref } from 'vue'
import { lsGet, lsSet, lsClearSettingsOnly, KEYS } from '@/utils/persistence'

export type NewTabPage = 'homepage' | 'openfile' | 'hebrewbooks' | 'kezayit-search'

const DEFAULTS = {
  censorDivineNames: false,
  diacriticsState: 0,
  headerFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
  textFont: "'Times New Roman', Times, serif",
  fontSize: 100,
  linePadding: 1.6,
  commentaryHeaderFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
  commentaryTextFont: "'Times New Roman', Times, serif",
  commentaryFontSize: 100,
  commentaryLinePadding: 1.6,
  useSeparateCommentarySettings: false,
  appZoom: 1.0,
  dictionaryZoom: 100,
  newTabPage: 'homepage' as NewTabPage,
  pdfPageFilters: false,
  resumeLastRead: false,
  defaultAutoSyncCommentary: false,
  // Number of words of context shown before and after the matched terms in a search snippet.
  // Converted to visible chars (× CHARS_PER_WORD) before being sent to the C# snippet builder.
  searchContextMarginWords: 30,
}

export const useSettingsStore = defineStore('settings', () => {
  const censorDivineNames = ref(DEFAULTS.censorDivineNames)
  const diacriticsState = ref(DEFAULTS.diacriticsState)
  const headerFont = ref(DEFAULTS.headerFont)
  const textFont = ref(DEFAULTS.textFont)
  const fontSize = ref(DEFAULTS.fontSize)
  const linePadding = ref(DEFAULTS.linePadding)
  const commentaryHeaderFont = ref(DEFAULTS.commentaryHeaderFont)
  const commentaryTextFont = ref(DEFAULTS.commentaryTextFont)
  const commentaryFontSize = ref(DEFAULTS.commentaryFontSize)
  const commentaryLinePadding = ref(DEFAULTS.commentaryLinePadding)
  const useSeparateCommentarySettings = ref(DEFAULTS.useSeparateCommentarySettings)
  const appZoom = ref(DEFAULTS.appZoom)
  const dictionaryZoom = ref(DEFAULTS.dictionaryZoom)
  const newTabPage = ref<NewTabPage>(DEFAULTS.newTabPage)
  const pdfPageFilters = ref(DEFAULTS.pdfPageFilters)
  const resumeLastRead = ref(DEFAULTS.resumeLastRead)
  const defaultAutoSyncCommentary = ref(DEFAULTS.defaultAutoSyncCommentary)
  const setupDone = ref(false)
  const midotDisclaimerAccepted = ref(false)
  const searchContextMarginWords = ref(DEFAULTS.searchContextMarginWords)

  // ── Helpers ───────────────────────────────────────────────────────────────

  /** Read a value from localStorage and assign it to the ref if present. */
  function loadSetting<T>(key: string, target: Ref<T>): void {
    const value = lsGet<T>(key)
    if (value != null) target.value = value
  }

  /** Watch a ref and persist it to localStorage on every change. */
  function persistSetting<T>(target: Ref<T>, key: string, afterSave?: () => void): void {
    watch(target, (value) => {
      lsSet(key, value)
      afterSave?.()
    })
  }

  // ── CSS sync ──────────────────────────────────────────────────────────────

  function applyCSSVariables() {
    const style = document.documentElement.style
    style.setProperty('--header-font', headerFont.value)
    style.setProperty('--text-font', textFont.value)
    style.setProperty('--font-size', `${fontSize.value}%`)
    style.setProperty('--line-height', linePadding.value.toString())
    style.setProperty('--commentary-header-font', commentaryHeaderFont.value)
    style.setProperty('--commentary-text-font', commentaryTextFont.value)
    style.setProperty('--commentary-font-size', `${commentaryFontSize.value}%`)
    style.setProperty('--commentary-line-height', commentaryLinePadding.value.toString())
    document.documentElement.setAttribute('data-pdf-filters', pdfPageFilters.value ? 'true' : 'false')
    const app = document.getElementById('app')
    if (app) app.style.zoom = appZoom.value.toString()
  }

  // ── Init ──────────────────────────────────────────────────────────────────

  // Synchronous — all settings are in localStorage
  function init() {
    loadSetting(KEYS.SETTINGS_CENSOR_DIVINE, censorDivineNames)
    loadSetting(KEYS.SETTINGS_DIACRITICS, diacriticsState)
    loadSetting(KEYS.SETTINGS_HEADER_FONT, headerFont)
    loadSetting(KEYS.SETTINGS_TEXT_FONT, textFont)
    loadSetting(KEYS.SETTINGS_FONT_SIZE, fontSize)
    loadSetting(KEYS.SETTINGS_LINE_PADDING, linePadding)
    loadSetting(KEYS.SETTINGS_COMMENTARY_HEADER_FONT, commentaryHeaderFont)
    loadSetting(KEYS.SETTINGS_COMMENTARY_TEXT_FONT, commentaryTextFont)
    loadSetting(KEYS.SETTINGS_COMMENTARY_FONT_SIZE, commentaryFontSize)
    loadSetting(KEYS.SETTINGS_COMMENTARY_LINE_PADDING, commentaryLinePadding)
    loadSetting(KEYS.SETTINGS_SEPARATE_COMMENTARY, useSeparateCommentarySettings)
    loadSetting(KEYS.SETTINGS_APP_ZOOM, appZoom)
    loadSetting(KEYS.SETTINGS_DICTIONARY_ZOOM, dictionaryZoom)
    loadSetting(KEYS.SETTINGS_NEW_TAB_PAGE, newTabPage)
    loadSetting(KEYS.SETTINGS_PDF_FILTERS, pdfPageFilters)
    loadSetting(KEYS.SETTINGS_RESUME_LAST_READ, resumeLastRead)
    loadSetting(KEYS.SETTINGS_SETUP_DONE, setupDone)
    loadSetting(KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY, defaultAutoSyncCommentary)
    loadSetting(KEYS.SETTINGS_MIDOT_DISCLAIMER, midotDisclaimerAccepted)
    loadSetting(KEYS.SETTINGS_SEARCH_CONTEXT_MARGIN, searchContextMarginWords)
    applyCSSVariables()
  }

  // ── Persistence watchers ──────────────────────────────────────────────────

  persistSetting(censorDivineNames, KEYS.SETTINGS_CENSOR_DIVINE, applyCSSVariables)
  persistSetting(diacriticsState, KEYS.SETTINGS_DIACRITICS)
  persistSetting(headerFont, KEYS.SETTINGS_HEADER_FONT, applyCSSVariables)
  persistSetting(textFont, KEYS.SETTINGS_TEXT_FONT, applyCSSVariables)
  persistSetting(fontSize, KEYS.SETTINGS_FONT_SIZE, applyCSSVariables)
  persistSetting(linePadding, KEYS.SETTINGS_LINE_PADDING, applyCSSVariables)
  persistSetting(commentaryHeaderFont, KEYS.SETTINGS_COMMENTARY_HEADER_FONT, applyCSSVariables)
  persistSetting(commentaryTextFont, KEYS.SETTINGS_COMMENTARY_TEXT_FONT, applyCSSVariables)
  persistSetting(commentaryFontSize, KEYS.SETTINGS_COMMENTARY_FONT_SIZE, applyCSSVariables)
  persistSetting(commentaryLinePadding, KEYS.SETTINGS_COMMENTARY_LINE_PADDING, applyCSSVariables)
  persistSetting(useSeparateCommentarySettings, KEYS.SETTINGS_SEPARATE_COMMENTARY)
  persistSetting(appZoom, KEYS.SETTINGS_APP_ZOOM, applyCSSVariables)
  persistSetting(dictionaryZoom, KEYS.SETTINGS_DICTIONARY_ZOOM)
  persistSetting(newTabPage, KEYS.SETTINGS_NEW_TAB_PAGE)
  persistSetting(pdfPageFilters, KEYS.SETTINGS_PDF_FILTERS)
  persistSetting(resumeLastRead, KEYS.SETTINGS_RESUME_LAST_READ)
  persistSetting(defaultAutoSyncCommentary, KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY)
  persistSetting(searchContextMarginWords, KEYS.SETTINGS_SEARCH_CONTEXT_MARGIN)

  // ── Actions ───────────────────────────────────────────────────────────────

  function cycleDiacritics() {
    diacriticsState.value = (diacriticsState.value + 1) % 3
  }

  function togglePdfPageFilters() {
    pdfPageFilters.value = !pdfPageFilters.value
    document.documentElement.setAttribute('data-pdf-filters', pdfPageFilters.value ? 'true' : 'false')
    document.querySelectorAll<HTMLIFrameElement>('iframe[src*="/pdfjs/web/viewer.html"]').forEach((iframe) => {
      try {
        iframe.contentDocument?.documentElement.setAttribute('data-pdf-filters', pdfPageFilters.value ? 'true' : 'false')
      } catch { /* cross-origin guard */ }
    })
  }

  function completeSetup() {
    setupDone.value = true
    lsSet(KEYS.SETTINGS_SETUP_DONE, true)
  }

  function acceptMidotDisclaimer() {
    midotDisclaimerAccepted.value = true
    lsSet(KEYS.SETTINGS_MIDOT_DISCLAIMER, true)
  }

  function reset() {
    censorDivineNames.value = DEFAULTS.censorDivineNames
    diacriticsState.value = DEFAULTS.diacriticsState
    headerFont.value = DEFAULTS.headerFont
    textFont.value = DEFAULTS.textFont
    fontSize.value = DEFAULTS.fontSize
    linePadding.value = DEFAULTS.linePadding
    commentaryHeaderFont.value = DEFAULTS.commentaryHeaderFont
    commentaryTextFont.value = DEFAULTS.commentaryTextFont
    commentaryFontSize.value = DEFAULTS.commentaryFontSize
    commentaryLinePadding.value = DEFAULTS.commentaryLinePadding
    useSeparateCommentarySettings.value = DEFAULTS.useSeparateCommentarySettings
    appZoom.value = DEFAULTS.appZoom
    dictionaryZoom.value = DEFAULTS.dictionaryZoom
    newTabPage.value = DEFAULTS.newTabPage
    pdfPageFilters.value = DEFAULTS.pdfPageFilters
    resumeLastRead.value = DEFAULTS.resumeLastRead
    defaultAutoSyncCommentary.value = DEFAULTS.defaultAutoSyncCommentary
    searchContextMarginWords.value = DEFAULTS.searchContextMarginWords
    lsClearSettingsOnly()
    applyCSSVariables()
  }

  return {
    censorDivineNames, diacriticsState, headerFont, textFont, fontSize, linePadding,
    commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
    useSeparateCommentarySettings, appZoom, dictionaryZoom, newTabPage, pdfPageFilters, resumeLastRead,
    defaultAutoSyncCommentary, setupDone, midotDisclaimerAccepted, searchContextMarginWords,
    init, cycleDiacritics, togglePdfPageFilters, reset, completeSetup, acceptMidotDisclaimer,
  }
})
