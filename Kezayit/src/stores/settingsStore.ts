import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { idbGetMany, idbSet, idbClearSettings, KEYS } from '@/utils/idbPersistence'

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
  newTabPage: 'homepage' as NewTabPage,
  pdfPageFilters: false,
  resumeLastRead: true,
  defaultAutoSyncCommentary: false,
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
  const newTabPage = ref<NewTabPage>(DEFAULTS.newTabPage)
  const pdfPageFilters = ref(DEFAULTS.pdfPageFilters)
  const resumeLastRead = ref(DEFAULTS.resumeLastRead)
  const defaultAutoSyncCommentary = ref(DEFAULTS.defaultAutoSyncCommentary)
  const setupDone = ref(false)
  const midotDisclaimerAccepted = ref(false)

  // Called from main.ts before mount — loads all settings in a single IDB transaction
  async function init() {
    const data = await idbGetMany([
      KEYS.SETTINGS_CENSOR_DIVINE,
      KEYS.SETTINGS_DIACRITICS,
      KEYS.SETTINGS_HEADER_FONT,
      KEYS.SETTINGS_TEXT_FONT,
      KEYS.SETTINGS_FONT_SIZE,
      KEYS.SETTINGS_LINE_PADDING,
      KEYS.SETTINGS_COMMENTARY_HEADER_FONT,
      KEYS.SETTINGS_COMMENTARY_TEXT_FONT,
      KEYS.SETTINGS_COMMENTARY_FONT_SIZE,
      KEYS.SETTINGS_COMMENTARY_LINE_PADDING,
      KEYS.SETTINGS_SEPARATE_COMMENTARY,
      KEYS.SETTINGS_APP_ZOOM,
      KEYS.SETTINGS_NEW_TAB_PAGE,
      KEYS.SETTINGS_PDF_FILTERS,
      KEYS.SETTINGS_RESUME_LAST_READ,
      KEYS.SETTINGS_SETUP_DONE,
      KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY,
      KEYS.SETTINGS_MIDOT_DISCLAIMER,
    ])

    const g = data
    if (g[KEYS.SETTINGS_CENSOR_DIVINE] != null) censorDivineNames.value = g[KEYS.SETTINGS_CENSOR_DIVINE] as boolean
    if (g[KEYS.SETTINGS_DIACRITICS] != null) diacriticsState.value = g[KEYS.SETTINGS_DIACRITICS] as number
    if (g[KEYS.SETTINGS_HEADER_FONT] != null) headerFont.value = g[KEYS.SETTINGS_HEADER_FONT] as string
    if (g[KEYS.SETTINGS_TEXT_FONT] != null) textFont.value = g[KEYS.SETTINGS_TEXT_FONT] as string
    if (g[KEYS.SETTINGS_FONT_SIZE] != null) fontSize.value = g[KEYS.SETTINGS_FONT_SIZE] as number
    if (g[KEYS.SETTINGS_LINE_PADDING] != null) linePadding.value = g[KEYS.SETTINGS_LINE_PADDING] as number
    if (g[KEYS.SETTINGS_COMMENTARY_HEADER_FONT] != null) commentaryHeaderFont.value = g[KEYS.SETTINGS_COMMENTARY_HEADER_FONT] as string
    if (g[KEYS.SETTINGS_COMMENTARY_TEXT_FONT] != null) commentaryTextFont.value = g[KEYS.SETTINGS_COMMENTARY_TEXT_FONT] as string
    if (g[KEYS.SETTINGS_COMMENTARY_FONT_SIZE] != null) commentaryFontSize.value = g[KEYS.SETTINGS_COMMENTARY_FONT_SIZE] as number
    if (g[KEYS.SETTINGS_COMMENTARY_LINE_PADDING] != null) commentaryLinePadding.value = g[KEYS.SETTINGS_COMMENTARY_LINE_PADDING] as number
    if (g[KEYS.SETTINGS_SEPARATE_COMMENTARY] != null) useSeparateCommentarySettings.value = g[KEYS.SETTINGS_SEPARATE_COMMENTARY] as boolean
    if (g[KEYS.SETTINGS_APP_ZOOM] != null) appZoom.value = g[KEYS.SETTINGS_APP_ZOOM] as number
    if (g[KEYS.SETTINGS_NEW_TAB_PAGE] != null) newTabPage.value = g[KEYS.SETTINGS_NEW_TAB_PAGE] as NewTabPage
    if (g[KEYS.SETTINGS_PDF_FILTERS] != null) pdfPageFilters.value = g[KEYS.SETTINGS_PDF_FILTERS] as boolean
    if (g[KEYS.SETTINGS_RESUME_LAST_READ] != null) resumeLastRead.value = g[KEYS.SETTINGS_RESUME_LAST_READ] as boolean
    if (g[KEYS.SETTINGS_SETUP_DONE] != null) setupDone.value = g[KEYS.SETTINGS_SETUP_DONE] as boolean
    if (g[KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY] != null) defaultAutoSyncCommentary.value = g[KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY] as boolean
    if (g[KEYS.SETTINGS_MIDOT_DISCLAIMER] != null) midotDisclaimerAccepted.value = g[KEYS.SETTINGS_MIDOT_DISCLAIMER] as boolean

    applyCSSVariables()
  }

  function applyCSSVariables() {
    const s = document.documentElement.style
    s.setProperty('--header-font', headerFont.value)
    s.setProperty('--text-font', textFont.value)
    s.setProperty('--font-size', `${fontSize.value}%`)
    s.setProperty('--line-height', linePadding.value.toString())
    s.setProperty('--commentary-header-font', commentaryHeaderFont.value)
    s.setProperty('--commentary-text-font', commentaryTextFont.value)
    s.setProperty('--commentary-font-size', `${commentaryFontSize.value}%`)
    s.setProperty('--commentary-line-height', commentaryLinePadding.value.toString())
    document.documentElement.setAttribute(
      'data-pdf-filters',
      pdfPageFilters.value ? 'true' : 'false',
    )
    const app = document.getElementById('app')
    if (app) app.style.zoom = appZoom.value.toString()
  }

  function cycleDiacritics() {
    diacriticsState.value = (diacriticsState.value + 1) % 3
  }

  function togglePdfPageFilters() {
    pdfPageFilters.value = !pdfPageFilters.value
    document.documentElement.setAttribute(
      'data-pdf-filters',
      pdfPageFilters.value ? 'true' : 'false',
    )
    document
      .querySelectorAll<HTMLIFrameElement>('iframe[src*="/pdfjs/web/viewer.html"]')
      .forEach((iframe) => {
        try {
          iframe.contentDocument?.documentElement.setAttribute(
            'data-pdf-filters',
            pdfPageFilters.value ? 'true' : 'false',
          )
        } catch {
          /* cross-origin guard */
        }
      })
  }

  function completeSetup() {
    setupDone.value = true
    idbSet(KEYS.SETTINGS_SETUP_DONE, true)
  }

  function acceptMidotDisclaimer() {
    midotDisclaimerAccepted.value = true
    idbSet(KEYS.SETTINGS_MIDOT_DISCLAIMER, true)
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
    newTabPage.value = DEFAULTS.newTabPage
    pdfPageFilters.value = DEFAULTS.pdfPageFilters
    resumeLastRead.value = DEFAULTS.resumeLastRead
    defaultAutoSyncCommentary.value = DEFAULTS.defaultAutoSyncCommentary
    // Clear all settings from their dedicated DB
    idbClearSettings()
    applyCSSVariables()
  }

  // Watch each setting individually — only write the changed key
  watch(censorDivineNames, (v) => {
    idbSet(KEYS.SETTINGS_CENSOR_DIVINE, v)
    applyCSSVariables()
  })
  watch(diacriticsState, (v) => {
    idbSet(KEYS.SETTINGS_DIACRITICS, v)
  })
  watch(headerFont, (v) => {
    idbSet(KEYS.SETTINGS_HEADER_FONT, v)
    applyCSSVariables()
  })
  watch(textFont, (v) => {
    idbSet(KEYS.SETTINGS_TEXT_FONT, v)
    applyCSSVariables()
  })
  watch(fontSize, (v) => {
    idbSet(KEYS.SETTINGS_FONT_SIZE, v)
    applyCSSVariables()
  })
  watch(linePadding, (v) => {
    idbSet(KEYS.SETTINGS_LINE_PADDING, v)
    applyCSSVariables()
  })
  watch(commentaryHeaderFont, (v) => {
    idbSet(KEYS.SETTINGS_COMMENTARY_HEADER_FONT, v)
    applyCSSVariables()
  })
  watch(commentaryTextFont, (v) => {
    idbSet(KEYS.SETTINGS_COMMENTARY_TEXT_FONT, v)
    applyCSSVariables()
  })
  watch(commentaryFontSize, (v) => {
    idbSet(KEYS.SETTINGS_COMMENTARY_FONT_SIZE, v)
    applyCSSVariables()
  })
  watch(commentaryLinePadding, (v) => {
    idbSet(KEYS.SETTINGS_COMMENTARY_LINE_PADDING, v)
    applyCSSVariables()
  })
  watch(useSeparateCommentarySettings, (v) => {
    idbSet(KEYS.SETTINGS_SEPARATE_COMMENTARY, v)
  })
  watch(appZoom, (v) => {
    idbSet(KEYS.SETTINGS_APP_ZOOM, v)
    applyCSSVariables()
  })
  watch(newTabPage, (v) => {
    idbSet(KEYS.SETTINGS_NEW_TAB_PAGE, v)
  })
  watch(pdfPageFilters, (v) => {
    idbSet(KEYS.SETTINGS_PDF_FILTERS, v)
  })
  watch(resumeLastRead, (v) => {
    idbSet(KEYS.SETTINGS_RESUME_LAST_READ, v)
  })
  watch(defaultAutoSyncCommentary, (v) => {
    idbSet(KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY, v)
  })

  return {
    censorDivineNames,
    diacriticsState,
    headerFont,
    textFont,
    fontSize,
    linePadding,
    commentaryHeaderFont,
    commentaryTextFont,
    commentaryFontSize,
    commentaryLinePadding,
    useSeparateCommentarySettings,
    appZoom,
    newTabPage,
    pdfPageFilters,
    resumeLastRead,
    defaultAutoSyncCommentary,
    init,
    cycleDiacritics,
    togglePdfPageFilters,
    reset,
    setupDone,
    completeSetup,
    midotDisclaimerAccepted,
    acceptMidotDisclaimer,
  }
})
