import { defineStore } from 'pinia'
import { ref, watch } from 'vue'

const STORAGE_KEY = 'zayit-settings'

export type NewTabPage = 'homepage' | 'openfile' | 'hebrewbooks' | 'kezayit-search'

interface Settings {
  censorDivineNames: boolean
  diacriticsState: number
  headerFont: string
  textFont: string
  fontSize: number
  linePadding: number
  commentaryHeaderFont: string
  commentaryTextFont: string
  commentaryFontSize: number
  commentaryLinePadding: number
  useSeparateCommentarySettings: boolean
  appZoom: number
  newTabPage: NewTabPage
  pdfPageFilters: boolean
  resumeLastRead: boolean
}

const DEFAULTS: Settings = {
  censorDivineNames: false,
  diacriticsState: 0,
  headerFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
  textFont: "'Times New Roman', Times, serif",
  fontSize: 105,
  linePadding: 1.6,
  commentaryHeaderFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
  commentaryTextFont: "'Times New Roman', Times, serif",
  commentaryFontSize: 105,
  commentaryLinePadding: 1.6,
  useSeparateCommentarySettings: false,
  appZoom: 1.0,
  newTabPage: 'homepage',
  pdfPageFilters: false,
  resumeLastRead: true,
}

function load(): Settings {
  try {
    const raw = localStorage.getItem(STORAGE_KEY)
    if (raw) return { ...DEFAULTS, ...JSON.parse(raw) }
  } catch {}
  return { ...DEFAULTS }
}

export const useSettingsStore = defineStore('settings', () => {
  const saved = load()

  const censorDivineNames = ref(saved.censorDivineNames)
  const diacriticsState = ref(saved.diacriticsState)
  const headerFont = ref(saved.headerFont)
  const textFont = ref(saved.textFont)
  const fontSize = ref(saved.fontSize)
  const linePadding = ref(saved.linePadding)
  const commentaryHeaderFont = ref(saved.commentaryHeaderFont)
  const commentaryTextFont = ref(saved.commentaryTextFont)
  const commentaryFontSize = ref(saved.commentaryFontSize)
  const commentaryLinePadding = ref(saved.commentaryLinePadding)
  const useSeparateCommentarySettings = ref(saved.useSeparateCommentarySettings)
  const appZoom = ref(saved.appZoom)
  const newTabPage = ref<NewTabPage>(saved.newTabPage)
  const pdfPageFilters = ref(saved.pdfPageFilters)
  const resumeLastRead = ref(saved.resumeLastRead)

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

  function persist() {
    localStorage.setItem(STORAGE_KEY, JSON.stringify({
      censorDivineNames: censorDivineNames.value,
      diacriticsState: diacriticsState.value,
      headerFont: headerFont.value,
      textFont: textFont.value,
      fontSize: fontSize.value,
      linePadding: linePadding.value,
      commentaryHeaderFont: commentaryHeaderFont.value,
      commentaryTextFont: commentaryTextFont.value,
      commentaryFontSize: commentaryFontSize.value,
      commentaryLinePadding: commentaryLinePadding.value,
      useSeparateCommentarySettings: useSeparateCommentarySettings.value,
      appZoom: appZoom.value,
      newTabPage: newTabPage.value,
      pdfPageFilters: pdfPageFilters.value,
      resumeLastRead: resumeLastRead.value,
    }))
  }

  function cycleDiacritics() { diacriticsState.value = (diacriticsState.value + 1) % 3 }

  function togglePdfPageFilters() {
    pdfPageFilters.value = !pdfPageFilters.value
    // Sync to all open PDF iframes
    document.documentElement.setAttribute('data-pdf-filters', pdfPageFilters.value ? 'true' : 'false')
    document.querySelectorAll<HTMLIFrameElement>('iframe[src*="/pdfjs/web/viewer.html"]').forEach(iframe => {
      try {
        iframe.contentDocument?.documentElement.setAttribute('data-pdf-filters', pdfPageFilters.value ? 'true' : 'false')
      } catch { /* cross-origin guard */ }
    })
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
    localStorage.removeItem(STORAGE_KEY)
    applyCSSVariables()
  }

  applyCSSVariables()

  watch([
    censorDivineNames, diacriticsState, headerFont, textFont, fontSize, linePadding,
    commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
    useSeparateCommentarySettings, appZoom, newTabPage, pdfPageFilters, resumeLastRead,
  ], () => { persist(); applyCSSVariables() })

  return {
    censorDivineNames, diacriticsState, headerFont, textFont, fontSize, linePadding,
    commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
    useSeparateCommentarySettings, appZoom, newTabPage, pdfPageFilters, resumeLastRead,
    cycleDiacritics, togglePdfPageFilters, reset,
  }
})
