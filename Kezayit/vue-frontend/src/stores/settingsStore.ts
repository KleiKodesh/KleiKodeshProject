import { defineStore } from 'pinia'
import { ref, watch } from 'vue'
import { lsGet, lsSet, lsClearAll, KEYS } from '@/utils/persistence'

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
  const dictionaryZoom = ref(DEFAULTS.dictionaryZoom)
  const newTabPage = ref<NewTabPage>(DEFAULTS.newTabPage)
  const pdfPageFilters = ref(DEFAULTS.pdfPageFilters)
  const resumeLastRead = ref(DEFAULTS.resumeLastRead)
  const defaultAutoSyncCommentary = ref(DEFAULTS.defaultAutoSyncCommentary)
  const setupDone = ref(false)
  const midotDisclaimerAccepted = ref(false)

  // Synchronous — all settings are in localStorage
  function init() {
    const v = <T>(key: string) => lsGet<T>(key)
    const b = v<boolean>(KEYS.SETTINGS_CENSOR_DIVINE); if (b != null) censorDivineNames.value = b
    const d = v<number>(KEYS.SETTINGS_DIACRITICS); if (d != null) diacriticsState.value = d
    const hf = v<string>(KEYS.SETTINGS_HEADER_FONT); if (hf != null) headerFont.value = hf
    const tf = v<string>(KEYS.SETTINGS_TEXT_FONT); if (tf != null) textFont.value = tf
    const fs = v<number>(KEYS.SETTINGS_FONT_SIZE); if (fs != null) fontSize.value = fs
    const lp = v<number>(KEYS.SETTINGS_LINE_PADDING); if (lp != null) linePadding.value = lp
    const chf = v<string>(KEYS.SETTINGS_COMMENTARY_HEADER_FONT); if (chf != null) commentaryHeaderFont.value = chf
    const ctf = v<string>(KEYS.SETTINGS_COMMENTARY_TEXT_FONT); if (ctf != null) commentaryTextFont.value = ctf
    const cfs = v<number>(KEYS.SETTINGS_COMMENTARY_FONT_SIZE); if (cfs != null) commentaryFontSize.value = cfs
    const clp = v<number>(KEYS.SETTINGS_COMMENTARY_LINE_PADDING); if (clp != null) commentaryLinePadding.value = clp
    const sc = v<boolean>(KEYS.SETTINGS_SEPARATE_COMMENTARY); if (sc != null) useSeparateCommentarySettings.value = sc
    const az = v<number>(KEYS.SETTINGS_APP_ZOOM); if (az != null) appZoom.value = az
    const dz = v<number>(KEYS.SETTINGS_DICTIONARY_ZOOM); if (dz != null) dictionaryZoom.value = dz
    const nt = v<NewTabPage>(KEYS.SETTINGS_NEW_TAB_PAGE); if (nt != null) newTabPage.value = nt
    const pf = v<boolean>(KEYS.SETTINGS_PDF_FILTERS); if (pf != null) pdfPageFilters.value = pf
    const rl = v<boolean>(KEYS.SETTINGS_RESUME_LAST_READ); if (rl != null) resumeLastRead.value = rl
    const sd = v<boolean>(KEYS.SETTINGS_SETUP_DONE); if (sd != null) setupDone.value = sd
    const da = v<boolean>(KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY); if (da != null) defaultAutoSyncCommentary.value = da
    const md = v<boolean>(KEYS.SETTINGS_MIDOT_DISCLAIMER); if (md != null) midotDisclaimerAccepted.value = md
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
    document.documentElement.setAttribute('data-pdf-filters', pdfPageFilters.value ? 'true' : 'false')
    const app = document.getElementById('app')
    if (app) app.style.zoom = appZoom.value.toString()
  }

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
    lsClearAll()
    applyCSSVariables()
  }

  watch(censorDivineNames, (v) => { lsSet(KEYS.SETTINGS_CENSOR_DIVINE, v); applyCSSVariables() })
  watch(diacriticsState, (v) => lsSet(KEYS.SETTINGS_DIACRITICS, v))
  watch(headerFont, (v) => { lsSet(KEYS.SETTINGS_HEADER_FONT, v); applyCSSVariables() })
  watch(textFont, (v) => { lsSet(KEYS.SETTINGS_TEXT_FONT, v); applyCSSVariables() })
  watch(fontSize, (v) => { lsSet(KEYS.SETTINGS_FONT_SIZE, v); applyCSSVariables() })
  watch(linePadding, (v) => { lsSet(KEYS.SETTINGS_LINE_PADDING, v); applyCSSVariables() })
  watch(commentaryHeaderFont, (v) => { lsSet(KEYS.SETTINGS_COMMENTARY_HEADER_FONT, v); applyCSSVariables() })
  watch(commentaryTextFont, (v) => { lsSet(KEYS.SETTINGS_COMMENTARY_TEXT_FONT, v); applyCSSVariables() })
  watch(commentaryFontSize, (v) => { lsSet(KEYS.SETTINGS_COMMENTARY_FONT_SIZE, v); applyCSSVariables() })
  watch(commentaryLinePadding, (v) => { lsSet(KEYS.SETTINGS_COMMENTARY_LINE_PADDING, v); applyCSSVariables() })
  watch(useSeparateCommentarySettings, (v) => lsSet(KEYS.SETTINGS_SEPARATE_COMMENTARY, v))
  watch(appZoom, (v) => { lsSet(KEYS.SETTINGS_APP_ZOOM, v); applyCSSVariables() })
  watch(dictionaryZoom, (v) => lsSet(KEYS.SETTINGS_DICTIONARY_ZOOM, v))
  watch(newTabPage, (v) => lsSet(KEYS.SETTINGS_NEW_TAB_PAGE, v))
  watch(pdfPageFilters, (v) => lsSet(KEYS.SETTINGS_PDF_FILTERS, v))
  watch(resumeLastRead, (v) => lsSet(KEYS.SETTINGS_RESUME_LAST_READ, v))
  watch(defaultAutoSyncCommentary, (v) => lsSet(KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY, v))

  return {
    censorDivineNames, diacriticsState, headerFont, textFont, fontSize, linePadding,
    commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
    useSeparateCommentarySettings, appZoom, dictionaryZoom, newTabPage, pdfPageFilters, resumeLastRead,
    defaultAutoSyncCommentary, setupDone, midotDisclaimerAccepted,
    init, cycleDiacritics, togglePdfPageFilters, reset, completeSetup, acceptMidotDisclaimer,
  }
})
