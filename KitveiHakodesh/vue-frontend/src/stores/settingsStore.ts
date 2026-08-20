import { defineStore } from 'pinia'
import { computed, ref, watch } from 'vue'
import type { Ref } from 'vue'
import { lsGet, lsSet, lsDelete, lsKeys } from '@/utils/persistence'

/**
 * Disk names for the settings this store owns. Nothing else reads them.
 *
 * Every name is namespaced by area, so two features can never claim the same slot
 * in localStorage's one flat namespace. Keep that rule for anything added here:
 * `area.name`, never a bare word.
 */
const KEYS = {
  // Book text display
  SETTINGS_HEADER_FONT: 'text.headerFont',
  SETTINGS_TEXT_FONT: 'text.textFont',
  SETTINGS_FONT_SIZE: 'text.fontSize',
  SETTINGS_LINE_PADDING: 'text.linePadding',
  SETTINGS_FIXED_LINE_HEIGHT: 'text.fixedLineHeight',
  SETTINGS_LINES_CONTENT_MAX_WIDTH: 'text.maxWidth',
  SETTINGS_DIACRITICS: 'text.diacritics',

  // Commentary display
  SETTINGS_COMMENTARY_HEADER_FONT: 'commentary.headerFont',
  SETTINGS_COMMENTARY_TEXT_FONT: 'commentary.textFont',
  SETTINGS_COMMENTARY_FONT_SIZE: 'commentary.fontSize',
  SETTINGS_COMMENTARY_LINE_PADDING: 'commentary.linePadding',
  SETTINGS_COMMENTARY_MAX_WIDTH: 'commentary.maxWidth',
  SETTINGS_SEPARATE_COMMENTARY: 'commentary.useSeparateSettings',
  SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY: 'commentary.defaultAutoSync',

  // Divine-name censoring
  SETTINGS_CENSOR_DIVINE: 'censor.divineNames',
  SETTINGS_CENSOR_ELOKIM: 'censor.elokimMode',
  SETTINGS_CENSOR_OTHER_NAMES: 'censor.otherNamesMode',

  // Copy flags
  SETTINGS_COPY_CLEAN_TEXT: 'copy.cleanText',
  SETTINGS_COPY_JOIN_LINES: 'copy.joinLines',
  SETTINGS_COPY_SOURCE_POSITION: 'copy.sourcePosition',
  SETTINGS_COPY_WITH_NOTES: 'copy.withNotes',
  SETTINGS_COPY_AS_SOURCE_WITH_QUOTATION: 'copy.asSourceWithQuotation',

  // Full-text search
  SETTINGS_SEARCH_CONTEXT_MARGIN: 'search.contextMargin',
  SETTINGS_SEARCH_MAX_WORD_DISTANCE: 'search.maxWordDistance',
  SETTINGS_SEARCH_REQUIRE_ORDERED: 'search.requireOrdered',
  SETTINGS_SEARCH_EXPAND_KETIV: 'search.expandKetiv',
  SETTINGS_SEARCH_EXPAND_RELATED: 'search.expandRelated',
  SETTINGS_SEARCH_WILDCARD_WRAP: 'search.wildcardWrap',
  SETTINGS_SEARCH_GRAMMAR_WRAP: 'search.grammarWrap',

  // App shell / chrome
  SETTINGS_APP_ZOOM: 'app.zoom',
  SETTINGS_NEW_TAB_PAGE: 'app.newTabPage',
  SETTINGS_SHOW_CLOCK: 'app.showClock',
  SETTINGS_SETUP_DONE: 'app.setupDone',
  SETTINGS_COMPACT_MODE: 'app.compactMode',
  SETTINGS_CONTENT_BORDER: 'app.contentBorder',
  SETTINGS_SCROLLBARS_HIDDEN: 'app.scrollbarsHidden',
  SETTINGS_SHOW_RECENTLY_OPENED: 'app.showRecentlyOpened',
  SETTINGS_RESUME_LAST_READ: 'app.resumeLastRead',
  SETTINGS_TITLE_BAR_HIDDEN_BUTTONS: 'titleBar.hiddenButtons',

  // Per-feature preferences
  SETTINGS_BOOKS_VIEW: 'books.view',
  SETTINGS_PDF_FILTERS: 'pdf.pageFilters',
  SETTINGS_DICTIONARY_ZOOM: 'dictionary.zoom',
  SETTINGS_MIDOT_DISCLAIMER: 'midot.disclaimerAccepted',
  SETTINGS_HB_LOCAL_FOLDER: 'hebrewBooks.localFolder',
  SETTINGS_FILE_SEARCH_SORT_ORDER: 'fileSearch.sortOrder',
} as const
import { getHbLocalFolderFromRegistry, setHbLocalFolderInRegistry, setScrollbarsHiddenInHost } from '@/webview-host/bridge'
import { normalizeCopyFlags } from '@/features/book-view/copyFlagExclusivity'
import {
  DEFAULT_DIVINE_NAME_MODE,
  DEFAULT_ELOKIM_MODE,
  DEFAULT_OTHER_NAMES_SELECTED,
  normalizeDivineNameMode,
  normalizeElokimMode,
  normalizeOtherNamesSelected,
  type CensorOptions,
  type DivineNameMode,
  type ElokimMode,
  type OtherNameKey,
} from '@/utils/censorDivineNames'

export type NewTabPage = 'homepage' | 'openfile' | 'hebrewbooks' | 'search'
/** Layout modes for the book catalog page. */
export type BooksView = 'list' | 'tiles' | 'tree'
// Legacy values from previous app name iterations — kept only for migrating old user data.
// Do not remove these; they are matched in normalizeNewTabPage below.
type LegacyNewTabPage = NewTabPage | 'kezayit-search' | 'kitveihakodesh-search'

const DEFAULTS = {
  // How the divine names are rendered. Was a boolean in builds before the
  // multi-mode setting; normalizeDivineNameMode migrates old stored values.
  // divineNameMode covers the tetragrammaton and is the master off switch
  // ('none' disables all censoring); the other two cover the other names.
  divineNameMode: DEFAULT_DIVINE_NAME_MODE,
  elokimMode: DEFAULT_ELOKIM_MODE,
  otherNamesSelected: DEFAULT_OTHER_NAMES_SELECTED,
  diacriticsState: 0,
  headerFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
  textFont: "'Times New Roman', Times, serif",
  fontSize: 100,
  linePadding: 1.6,
  // Off by default: with an absolute line box, a word larger than the body text
  // overlaps the neighbouring row instead of pushing it apart. That trade — even
  // spacing at the cost of a possible overlap — is the user's to opt into.
  fixedLineHeight: false,
  commentaryHeaderFont: "'Segoe UI Variable', 'Segoe UI', system-ui, sans-serif",
  commentaryTextFont: "'Times New Roman', Times, serif",
  commentaryFontSize: 100,
  commentaryLinePadding: 1.6,
  useSeparateCommentarySettings: false,
  appZoom: 1.0,
  dictionaryZoom: 100,
  newTabPage: 'homepage' as NewTabPage,
  booksView: 'list' as BooksView,
  pdfPageFilters: false,
  resumeLastRead: true,
  showClock: false,
  defaultAutoSyncCommentary: false,
  // Number of characters of context shown before and after the matched terms in a search snippet.
  searchContextMarginWords: 30,
  // Advanced full-text search settings
  searchMaxWordDistance: 10,
  searchRequireOrdered: false,
  searchExpandKetiv: true,
  searchExpandRelated: false,
  searchWildcardWrap: false,
  searchGrammarWrap: false,
  copyCleanText: false,
  // Join the selected lines into one continuous run of text with NO line break
  // between them (default off = keep one line break per source line on paste).
  copyJoinLines: false,
  copySourcePosition: null as 'end' | 'start' | null,
  copyWithNotes: false,
  copyAsSourceWithQuotation: false,
  hebrewBooksLocalFolder: '',
  linesContentMaxWidth: 0,
  commentaryMaxWidth: 0,
  titleBarHiddenButtons: ['theme-toggle'] as string[],
  compactMode: true,
  contentBorder: false,
  // Auto-hiding scrollbars: a pure passthrough to the WebView2 environment's
  // ScrollBarStyle (fluent overlay bars vs classic ones), mirrored to the C#
  // registry settings on change and read by the host at environment creation —
  // takes effect on the next app launch. No CSS or DOM logic anywhere, and no
  // effect in the dev browser.
  scrollbarsHidden: false,
  showRecentlyOpened: true,
}

/**
 * Remove only display/reading settings from localStorage, preserving app structure:
 * the tab lists, the workspace list, and the one-time onboarding flag (so the setup
 * wizard never re-appears).
 *
 * The settings-versus-structural split is a product decision, which is why it lives
 * here and not in the storage driver: the driver has no way to know that `tabs:*` is
 * structure while `text.fontSize` is a preference.
 *
 * Now that every key is namespaced, structure is identified by prefix rather than by
 * a list of bare names — so a new structural key is preserved automatically as long
 * as it lands under one of these namespaces.
 */
const PRESERVE_PREFIXES = ['tabs:', 'workspaces.']
const PRESERVE_KEYS = new Set<string>([KEYS.SETTINGS_SETUP_DONE])

function clearPersistedSettings(): void {
  for (const key of lsKeys()) {
    if (PRESERVE_PREFIXES.some((p) => key.startsWith(p))) continue
    if (PRESERVE_KEYS.has(key)) continue
    lsDelete(key)
  }
}

export const useSettingsStore = defineStore('settings', () => {
  const divineNameMode = ref<DivineNameMode>(DEFAULTS.divineNameMode)
  const elokimMode = ref<ElokimMode>(DEFAULTS.elokimMode)
  const otherNamesSelected = ref<OtherNameKey[]>([...DEFAULTS.otherNamesSelected])
  const diacriticsState = ref(DEFAULTS.diacriticsState)
  const headerFont = ref(DEFAULTS.headerFont)
  const textFont = ref(DEFAULTS.textFont)
  const fontSize = ref(DEFAULTS.fontSize)
  const linePadding = ref(DEFAULTS.linePadding)
  const fixedLineHeight = ref(DEFAULTS.fixedLineHeight)
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
  const showClock = ref(DEFAULTS.showClock)
  const defaultAutoSyncCommentary = ref(DEFAULTS.defaultAutoSyncCommentary)
  const setupDone = ref(false)
  const midotDisclaimerAccepted = ref(false)
  const searchContextMarginWords = ref(DEFAULTS.searchContextMarginWords)
  const searchMaxWordDistance = ref(DEFAULTS.searchMaxWordDistance)
  const searchRequireOrdered = ref(DEFAULTS.searchRequireOrdered)
  const searchExpandKetiv = ref(DEFAULTS.searchExpandKetiv)
  const searchExpandRelated = ref(DEFAULTS.searchExpandRelated)
  const searchWildcardWrap = ref(DEFAULTS.searchWildcardWrap)
  const searchGrammarWrap = ref(DEFAULTS.searchGrammarWrap)
  const copyCleanText = ref(DEFAULTS.copyCleanText)
  const copyJoinLines = ref(DEFAULTS.copyJoinLines)
  const copySourcePosition = ref<'end' | 'start' | null>(DEFAULTS.copySourcePosition)
  const copyWithNotes = ref(DEFAULTS.copyWithNotes)
  const copyAsSourceWithQuotation = ref(DEFAULTS.copyAsSourceWithQuotation)
  const hebrewBooksLocalFolder = ref(DEFAULTS.hebrewBooksLocalFolder)
  const linesContentMaxWidth = ref(DEFAULTS.linesContentMaxWidth)
  const commentaryMaxWidth = ref(DEFAULTS.commentaryMaxWidth)
  const titleBarHiddenButtons = ref<string[]>(DEFAULTS.titleBarHiddenButtons)
  const compactMode = ref(DEFAULTS.compactMode)
  const contentBorder = ref(DEFAULTS.contentBorder)
  const scrollbarsHidden = ref(DEFAULTS.scrollbarsHidden)
  const showRecentlyOpened = ref(DEFAULTS.showRecentlyOpened)
  const fileSearchSortOrder = ref<import('@/features/local-file-search/useLocalFileSearch').LocalFileSearchSortOrder>('relevance')
  /** Which layout the book catalog page renders in. */
  const booksView = ref<BooksView>(DEFAULTS.booksView)

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
    // Exact line spacing: the reading views resolve --line-height against their own
    // font-size once (an absolute length) instead of letting every inline element
    // recompute it, so an oversized word no longer stretches its row.
    document.documentElement.setAttribute('data-fixed-line-height', fixedLineHeight.value ? 'true' : 'false')
    // When not using separate commentary settings, mirror the book settings.
    const effectiveCommentaryHeaderFont = useSeparateCommentarySettings.value ? commentaryHeaderFont.value : headerFont.value
    const effectiveCommentaryTextFont = useSeparateCommentarySettings.value ? commentaryTextFont.value : textFont.value
    const effectiveCommentaryFontSize = useSeparateCommentarySettings.value ? commentaryFontSize.value : fontSize.value
    const effectiveCommentaryLinePadding = useSeparateCommentarySettings.value ? commentaryLinePadding.value : linePadding.value
    style.setProperty('--commentary-header-font', effectiveCommentaryHeaderFont)
    style.setProperty('--commentary-text-font', effectiveCommentaryTextFont)
    style.setProperty('--commentary-font-size', `${effectiveCommentaryFontSize}%`)
    style.setProperty('--commentary-line-height', effectiveCommentaryLinePadding.toString())
    style.setProperty('--lines-content-max-width', linesContentMaxWidth.value > 0 ? `${linesContentMaxWidth.value}px` : 'none')
    const effectiveCommentaryMaxWidth = useSeparateCommentarySettings.value ? commentaryMaxWidth.value : linesContentMaxWidth.value
    style.setProperty('--commentary-max-width', effectiveCommentaryMaxWidth > 0 ? `${effectiveCommentaryMaxWidth}px` : 'none')
    document.documentElement.setAttribute('data-pdf-filters', pdfPageFilters.value ? 'true' : 'false')
    document.documentElement.setAttribute('data-density', compactMode.value ? 'compact' : 'normal')
    // Content-border toggle — zero the inset/border/radius (and re-show the title
    // bar divider) when off; the app-shell / title-bar CSS reads these vars.
    style.setProperty('--content-inset', contentBorder.value ? '3px' : '0px')
    style.setProperty('--content-border-width', contentBorder.value ? '1px' : '0px')
    style.setProperty('--content-border-radius', contentBorder.value ? '8px' : '0px')
    style.setProperty('--title-bar-divider-width', contentBorder.value ? '0px' : '1px')
    const app = document.getElementById('app')
    if (app) app.style.zoom = appZoom.value.toString()
  }

  // ── Init ──────────────────────────────────────────────────────────────────

  // Synchronous — all settings are in localStorage
  function init() {
    // Accepts both the current mode string and the legacy boolean (true → 'yudDaled').
    const storedDivineNameMode = normalizeDivineNameMode(lsGet(KEYS.SETTINGS_CENSOR_DIVINE))
    if (storedDivineNameMode != null) divineNameMode.value = storedDivineNameMode
    const storedElokimMode = normalizeElokimMode(lsGet(KEYS.SETTINGS_CENSOR_ELOKIM))
    if (storedElokimMode != null) elokimMode.value = storedElokimMode
    const storedOtherNamesSelected = normalizeOtherNamesSelected(lsGet(KEYS.SETTINGS_CENSOR_OTHER_NAMES))
    if (storedOtherNamesSelected != null) otherNamesSelected.value = storedOtherNamesSelected
    loadSetting(KEYS.SETTINGS_DIACRITICS, diacriticsState)
    loadSetting(KEYS.SETTINGS_HEADER_FONT, headerFont)
    loadSetting(KEYS.SETTINGS_TEXT_FONT, textFont)
    loadSetting(KEYS.SETTINGS_FONT_SIZE, fontSize)
    loadSetting(KEYS.SETTINGS_LINE_PADDING, linePadding)
    loadSetting(KEYS.SETTINGS_FIXED_LINE_HEIGHT, fixedLineHeight)
    loadSetting(KEYS.SETTINGS_COMMENTARY_HEADER_FONT, commentaryHeaderFont)
    loadSetting(KEYS.SETTINGS_COMMENTARY_TEXT_FONT, commentaryTextFont)
    loadSetting(KEYS.SETTINGS_COMMENTARY_FONT_SIZE, commentaryFontSize)
    loadSetting(KEYS.SETTINGS_COMMENTARY_LINE_PADDING, commentaryLinePadding)
    loadSetting(KEYS.SETTINGS_SEPARATE_COMMENTARY, useSeparateCommentarySettings)
    loadSetting(KEYS.SETTINGS_APP_ZOOM, appZoom)
    loadSetting(KEYS.SETTINGS_DICTIONARY_ZOOM, dictionaryZoom)
    const storedNewTabPage = normalizeNewTabPage(lsGet<LegacyNewTabPage>(KEYS.SETTINGS_NEW_TAB_PAGE))
    if (storedNewTabPage != null) newTabPage.value = storedNewTabPage
    loadSetting(KEYS.SETTINGS_PDF_FILTERS, pdfPageFilters)
    loadSetting(KEYS.SETTINGS_RESUME_LAST_READ, resumeLastRead)
    loadSetting(KEYS.SETTINGS_SHOW_CLOCK, showClock)
    loadSetting(KEYS.SETTINGS_SETUP_DONE, setupDone)
    loadSetting(KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY, defaultAutoSyncCommentary)
    loadSetting(KEYS.SETTINGS_MIDOT_DISCLAIMER, midotDisclaimerAccepted)
    loadSetting(KEYS.SETTINGS_SEARCH_CONTEXT_MARGIN, searchContextMarginWords)
    loadSetting(KEYS.SETTINGS_SEARCH_MAX_WORD_DISTANCE, searchMaxWordDistance)
    loadSetting(KEYS.SETTINGS_SEARCH_REQUIRE_ORDERED, searchRequireOrdered)
    loadSetting(KEYS.SETTINGS_SEARCH_EXPAND_KETIV, searchExpandKetiv)
    loadSetting(KEYS.SETTINGS_SEARCH_EXPAND_RELATED, searchExpandRelated)
    loadSetting(KEYS.SETTINGS_SEARCH_WILDCARD_WRAP, searchWildcardWrap)
    loadSetting(KEYS.SETTINGS_SEARCH_GRAMMAR_WRAP, searchGrammarWrap)
    loadSetting(KEYS.SETTINGS_COPY_CLEAN_TEXT, copyCleanText)
    loadSetting(KEYS.SETTINGS_COPY_JOIN_LINES, copyJoinLines)
    loadSetting(KEYS.SETTINGS_COPY_SOURCE_POSITION, copySourcePosition)
    loadSetting(KEYS.SETTINGS_COPY_WITH_NOTES, copyWithNotes)
    loadSetting(KEYS.SETTINGS_COPY_AS_SOURCE_WITH_QUOTATION, copyAsSourceWithQuotation)
    // Repair any contradictory copy-flag combination persisted by an OLDER build
    // (before the copy menu's exclusivity guards existed). Loaded values are read
    // back verbatim with no validation, so normalize once here — otherwise a saved
    // e.g. {withNotes, sourcePosition:'end'} would drive the copy builder into a
    // self-contradicting state. See copyFlagExclusivity.ts for the rules.
    {
      const repaired = normalizeCopyFlags({
        copySourcePosition: copySourcePosition.value,
        copyWithNotes: copyWithNotes.value,
        copyAsSourceWithQuotation: copyAsSourceWithQuotation.value,
      })
      copySourcePosition.value = repaired.copySourcePosition
      copyWithNotes.value = repaired.copyWithNotes
      copyAsSourceWithQuotation.value = repaired.copyAsSourceWithQuotation
    }
    loadSetting(KEYS.SETTINGS_HB_LOCAL_FOLDER, hebrewBooksLocalFolder)
    // If the WPF installer configured a local folder but localStorage has nothing yet,
    // seed it from the injected value so the settings page reflects it immediately.
    if (!hebrewBooksLocalFolder.value && window.__webviewHbLocalFolder) {
      hebrewBooksLocalFolder.value = window.__webviewHbLocalFolder
    }
    // In dev, the HB folder lives in the SHARED registry (HKCU\...\KitveiHakodesh\HebrewBooks\
    // LocalFolder) so dev and the hosted app agree. Seed from there when localStorage is empty;
    // the persist watcher below writes user changes back to the same registry value.
    if (!hebrewBooksLocalFolder.value) {
      getHbLocalFolderFromRegistry()
        .then((folder) => {
          if (folder && !hebrewBooksLocalFolder.value) hebrewBooksLocalFolder.value = folder
        })
        .catch(() => {})
    }
    loadSetting(KEYS.SETTINGS_LINES_CONTENT_MAX_WIDTH, linesContentMaxWidth)
    loadSetting(KEYS.SETTINGS_COMMENTARY_MAX_WIDTH, commentaryMaxWidth)
    loadSetting(KEYS.SETTINGS_TITLE_BAR_HIDDEN_BUTTONS, titleBarHiddenButtons)
    loadSetting(KEYS.SETTINGS_COMPACT_MODE, compactMode)
    loadSetting(KEYS.SETTINGS_CONTENT_BORDER, contentBorder)
    loadSetting(KEYS.SETTINGS_SCROLLBARS_HIDDEN, scrollbarsHidden)
    loadSetting(KEYS.SETTINGS_SHOW_RECENTLY_OPENED, showRecentlyOpened)
    loadSetting(KEYS.SETTINGS_FILE_SEARCH_SORT_ORDER, fileSearchSortOrder)
    loadSetting(KEYS.SETTINGS_BOOKS_VIEW, booksView)
    applyCSSVariables()
  }

  // ── Persistence watchers ──────────────────────────────────────────────────

  persistSetting(divineNameMode, KEYS.SETTINGS_CENSOR_DIVINE, applyCSSVariables)
  persistSetting(elokimMode, KEYS.SETTINGS_CENSOR_ELOKIM)
  persistSetting(otherNamesSelected, KEYS.SETTINGS_CENSOR_OTHER_NAMES)
  persistSetting(diacriticsState, KEYS.SETTINGS_DIACRITICS)
  persistSetting(headerFont, KEYS.SETTINGS_HEADER_FONT, applyCSSVariables)
  persistSetting(textFont, KEYS.SETTINGS_TEXT_FONT, applyCSSVariables)
  persistSetting(fontSize, KEYS.SETTINGS_FONT_SIZE, applyCSSVariables)
  persistSetting(linePadding, KEYS.SETTINGS_LINE_PADDING, applyCSSVariables)
  persistSetting(fixedLineHeight, KEYS.SETTINGS_FIXED_LINE_HEIGHT, applyCSSVariables)
  persistSetting(commentaryHeaderFont, KEYS.SETTINGS_COMMENTARY_HEADER_FONT, applyCSSVariables)
  persistSetting(commentaryTextFont, KEYS.SETTINGS_COMMENTARY_TEXT_FONT, applyCSSVariables)
  persistSetting(commentaryFontSize, KEYS.SETTINGS_COMMENTARY_FONT_SIZE, applyCSSVariables)
  persistSetting(commentaryLinePadding, KEYS.SETTINGS_COMMENTARY_LINE_PADDING, applyCSSVariables)
  persistSetting(useSeparateCommentarySettings, KEYS.SETTINGS_SEPARATE_COMMENTARY, applyCSSVariables)
  persistSetting(appZoom, KEYS.SETTINGS_APP_ZOOM, applyCSSVariables)
  persistSetting(dictionaryZoom, KEYS.SETTINGS_DICTIONARY_ZOOM)
  persistSetting(newTabPage, KEYS.SETTINGS_NEW_TAB_PAGE)
  persistSetting(pdfPageFilters, KEYS.SETTINGS_PDF_FILTERS)
  persistSetting(resumeLastRead, KEYS.SETTINGS_RESUME_LAST_READ)
  persistSetting(showClock, KEYS.SETTINGS_SHOW_CLOCK)
  persistSetting(defaultAutoSyncCommentary, KEYS.SETTINGS_DEFAULT_AUTO_SYNC_COMMENTARY)
  persistSetting(searchContextMarginWords, KEYS.SETTINGS_SEARCH_CONTEXT_MARGIN)
  persistSetting(searchMaxWordDistance, KEYS.SETTINGS_SEARCH_MAX_WORD_DISTANCE)
  persistSetting(searchRequireOrdered, KEYS.SETTINGS_SEARCH_REQUIRE_ORDERED)
  persistSetting(searchExpandKetiv, KEYS.SETTINGS_SEARCH_EXPAND_KETIV)
  persistSetting(searchExpandRelated, KEYS.SETTINGS_SEARCH_EXPAND_RELATED)
  persistSetting(searchWildcardWrap, KEYS.SETTINGS_SEARCH_WILDCARD_WRAP)
  persistSetting(searchGrammarWrap, KEYS.SETTINGS_SEARCH_GRAMMAR_WRAP)
  persistSetting(copyCleanText, KEYS.SETTINGS_COPY_CLEAN_TEXT)
  persistSetting(copyJoinLines, KEYS.SETTINGS_COPY_JOIN_LINES)
  persistSetting(copySourcePosition, KEYS.SETTINGS_COPY_SOURCE_POSITION)
  persistSetting(copyWithNotes, KEYS.SETTINGS_COPY_WITH_NOTES)
  persistSetting(copyAsSourceWithQuotation, KEYS.SETTINGS_COPY_AS_SOURCE_WITH_QUOTATION)
  persistSetting(hebrewBooksLocalFolder, KEYS.SETTINGS_HB_LOCAL_FOLDER, () => {
    // Mirror the folder into the shared registry (dev only; hosted persists via the C# host),
    // so the hosted app and dev read the same HKCU value.
    setHbLocalFolderInRegistry(hebrewBooksLocalFolder.value || '')
  })
  persistSetting(linesContentMaxWidth, KEYS.SETTINGS_LINES_CONTENT_MAX_WIDTH, applyCSSVariables)
  persistSetting(commentaryMaxWidth, KEYS.SETTINGS_COMMENTARY_MAX_WIDTH, applyCSSVariables)
  persistSetting(titleBarHiddenButtons, KEYS.SETTINGS_TITLE_BAR_HIDDEN_BUTTONS)
  persistSetting(compactMode, KEYS.SETTINGS_COMPACT_MODE, applyCSSVariables)
  persistSetting(contentBorder, KEYS.SETTINGS_CONTENT_BORDER, applyCSSVariables)
  persistSetting(scrollbarsHidden, KEYS.SETTINGS_SCROLLBARS_HIDDEN, () => {
    // Mirror into the host so the next launch's WebView2 environment picks it up.
    setScrollbarsHiddenInHost(scrollbarsHidden.value)
  })
  persistSetting(showRecentlyOpened, KEYS.SETTINGS_SHOW_RECENTLY_OPENED)
  persistSetting(fileSearchSortOrder, KEYS.SETTINGS_FILE_SEARCH_SORT_ORDER)
  persistSetting(booksView, KEYS.SETTINGS_BOOKS_VIEW)

  // ── Derived ───────────────────────────────────────────────────────────────

  /**
   * The three censoring settings bundled for censorDivineNames(). Every render
   * path reads this so they cannot drift apart, and so a render cache can key on
   * it — see the cache keys in useBookViewLineRenderer / useCommentaryRender.
   */
  const censorOptions = computed<CensorOptions>(() => ({
    mode: divineNameMode.value,
    elokim: elokimMode.value,
    otherNames: otherNamesSelected.value,
  }))

  /** Stable cache key for the censoring settings. */
  const censorCacheKey = computed(
    () => `${divineNameMode.value}:${elokimMode.value}:${[...otherNamesSelected.value].sort().join(',')}`,
  )

  // ── Actions ───────────────────────────────────────────────────────────────

  function cycleDiacritics() {
    diacriticsState.value = (diacriticsState.value + 1) % 3
  }

  /**
   * Cycle diacritics for a book that has no cantillation marks (hasTeamim is falsy).
   * State 1 (strip teamim only) is meaningless for such books, so the cycle skips it:
   * 0 → 2 → 0  and  1 → 2 (state 1 is treated as "nothing stripped yet")
   */
  function cycleDiacriticsNoTeamim() {
    diacriticsState.value = diacriticsState.value === 2 ? 0 : 2
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
    divineNameMode.value = DEFAULTS.divineNameMode
    elokimMode.value = DEFAULTS.elokimMode
    otherNamesSelected.value = [...DEFAULTS.otherNamesSelected]
    diacriticsState.value = DEFAULTS.diacriticsState
    headerFont.value = DEFAULTS.headerFont
    textFont.value = DEFAULTS.textFont
    fontSize.value = DEFAULTS.fontSize
    linePadding.value = DEFAULTS.linePadding
    fixedLineHeight.value = DEFAULTS.fixedLineHeight
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
    showClock.value = DEFAULTS.showClock
    defaultAutoSyncCommentary.value = DEFAULTS.defaultAutoSyncCommentary
    searchContextMarginWords.value = DEFAULTS.searchContextMarginWords
    searchMaxWordDistance.value = DEFAULTS.searchMaxWordDistance
    searchRequireOrdered.value = DEFAULTS.searchRequireOrdered
    searchExpandKetiv.value = DEFAULTS.searchExpandKetiv
    searchExpandRelated.value = DEFAULTS.searchExpandRelated
    searchWildcardWrap.value = DEFAULTS.searchWildcardWrap
    searchGrammarWrap.value = DEFAULTS.searchGrammarWrap
    copyCleanText.value = DEFAULTS.copyCleanText
    copyJoinLines.value = DEFAULTS.copyJoinLines
    copySourcePosition.value = DEFAULTS.copySourcePosition
    copyWithNotes.value = DEFAULTS.copyWithNotes
    copyAsSourceWithQuotation.value = DEFAULTS.copyAsSourceWithQuotation
    hebrewBooksLocalFolder.value = DEFAULTS.hebrewBooksLocalFolder
    linesContentMaxWidth.value = DEFAULTS.linesContentMaxWidth
    commentaryMaxWidth.value = DEFAULTS.commentaryMaxWidth
    titleBarHiddenButtons.value = DEFAULTS.titleBarHiddenButtons
    compactMode.value = DEFAULTS.compactMode
    contentBorder.value = DEFAULTS.contentBorder
    scrollbarsHidden.value = DEFAULTS.scrollbarsHidden
    showRecentlyOpened.value = DEFAULTS.showRecentlyOpened
    clearPersistedSettings()
    applyCSSVariables()
  }

  return {
    divineNameMode, elokimMode, otherNamesSelected, censorOptions, censorCacheKey,
    diacriticsState, headerFont, textFont, fontSize, linePadding, fixedLineHeight,
    commentaryHeaderFont, commentaryTextFont, commentaryFontSize, commentaryLinePadding,
    useSeparateCommentarySettings, appZoom, dictionaryZoom, newTabPage, pdfPageFilters, resumeLastRead,
    showClock,
    defaultAutoSyncCommentary, setupDone, midotDisclaimerAccepted, searchContextMarginWords,
    searchMaxWordDistance, searchRequireOrdered, searchExpandKetiv, searchExpandRelated, searchWildcardWrap, searchGrammarWrap,
    copyCleanText,
    copyJoinLines,
    copySourcePosition,
    copyWithNotes,
    copyAsSourceWithQuotation,
    hebrewBooksLocalFolder,
    linesContentMaxWidth,
    commentaryMaxWidth,
    titleBarHiddenButtons,
    compactMode,
    contentBorder,
    scrollbarsHidden,
    showRecentlyOpened,
    fileSearchSortOrder,
    booksView,
    init, cycleDiacritics, cycleDiacriticsNoTeamim, togglePdfPageFilters, reset, completeSetup, acceptMidotDisclaimer,
  }
})
  function normalizeNewTabPage(value: LegacyNewTabPage | null): NewTabPage | null {
    if (value === 'kezayit-search' || value === 'kitveihakodesh-search') return 'search'
    return value
  }
