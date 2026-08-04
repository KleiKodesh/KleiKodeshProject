import type { Component } from 'vue'
import {
  IconDocumentPdf20Filled,
  IconDocumentText20Filled,
  IconDocumentGlobe20Filled,
  IconDocument20Filled,
  IconPuzzlePiece20Regular,
  IconHome20Regular,
  IconLibrary20Filled,
  IconBookOpen20Filled,
  IconBookLetter20Filled,
  IconCalendarRtl20Filled,
  IconRuler20Filled,
  IconApps20Filled,
  IconFolder20Filled,
  IconDocumentPdf24Filled,
  IconDocumentText24Filled,
  IconDocumentGlobe24Filled,
  IconDocument24Filled,
  IconPuzzlePiece24Regular,
  IconHome24Regular,
  IconLibrary24Filled,
  IconBookOpen24Filled,
  IconBookLetter24Filled,
  IconCalendarRtl24Filled,
  IconRuler24Filled,
  IconApps24Filled,
  IconFolder24Filled,
} from '@iconify-prerendered/vue-fluent'
import { IconSearchSparkle24, IconSettings24 } from '@iconify-prerendered/vue-fluent-color'
import IconBookRtl20 from '@/components/IconBookRtl20.vue'
import IconBookRtl24 from '@/components/IconBookRtl24.vue'
import IconEverythingSearch from '@/components/IconEverythingSearch.vue'

/**
 * The one document-icon mapping — favicon-style glyph + brand color per kind of
 * document. Every surface that labels a document uses it: the home tiles, the
 * address-bar dropdown (tab rows AND search results), and the native chrome tab
 * strip. There used to be four near-copies of this table that had already drifted
 * apart (the tab rows showed uncolored outline glyphs while the tiles showed
 * filled colored ones); keep it to this one.
 *
 * Sizes are explicit rather than scaled: Fluent ships each icon drawn for its own
 * size, so a 20px icon is not a shrunken 24px one. Ask for the size you need.
 */

/** Stable identifier per icon — also the cache key when rasterizing for the native strip. */
export type DocumentIconKey =
  | 'book'
  | 'pdf'
  | 'html'
  | 'txt'
  | 'addin'
  | 'document'
  | 'library'
  | 'hbooks'
  | 'dict'
  | 'calendar'
  | 'ruler'
  | 'apps'
  | 'folder'
  | 'fileSearch'
  | 'settings'
  | 'home'
  | 'search'

export interface DocumentIcon {
  key: DocumentIconKey
  /** Empty string means "no explicit color" — inherit the surrounding text color. */
  color: string
  icon20: Component
  icon24: Component
}

const ICONS: Record<DocumentIconKey, DocumentIcon> = {
  book:     { key: 'book',     color: '#c1440e', icon20: IconBookRtl20,             icon24: IconBookRtl24 },
  pdf:      { key: 'pdf',      color: '#F40F02', icon20: IconDocumentPdf20Filled,   icon24: IconDocumentPdf24Filled },
  html:     { key: 'html',     color: '#0097fb', icon20: IconDocumentGlobe20Filled, icon24: IconDocumentGlobe24Filled },
  txt:      { key: 'txt',      color: '#9e9e9e', icon20: IconDocumentText20Filled,  icon24: IconDocumentText24Filled },
  addin:    { key: 'addin',    color: '#7b5ea7', icon20: IconPuzzlePiece20Regular,  icon24: IconPuzzlePiece24Regular },
  document: { key: 'document', color: '#3478f6', icon20: IconDocument20Filled,      icon24: IconDocument24Filled },
  // Destination routes. These mirror the home page's static navigation tiles
  // (useHomeTiles) glyph-for-glyph and colour-for-colour, so a tab for one of them
  // is recognisably the same thing as its tile.
  library:  { key: 'library',  color: '#B5451B', icon20: IconLibrary20Filled,       icon24: IconLibrary24Filled },
  hbooks:   { key: 'hbooks',   color: '#D94F1E', icon20: IconBookOpen20Filled,      icon24: IconBookOpen24Filled },
  dict:     { key: 'dict',     color: '#7b5ea7', icon20: IconBookLetter20Filled,    icon24: IconBookLetter24Filled },
  calendar: { key: 'calendar', color: '#2e7d32', icon20: IconCalendarRtl20Filled,   icon24: IconCalendarRtl24Filled },
  ruler:    { key: 'ruler',    color: '#8b6914', icon20: IconRuler20Filled,         icon24: IconRuler24Filled },
  apps:     { key: 'apps',     color: '#6b7fc4', icon20: IconApps20Filled,          icon24: IconApps24Filled },
  // Exactly what the tile uses. The CSS var resolves in the page; the rasterizer
  // resolves it against the live document before baking it into a PNG.
  folder:   { key: 'folder',   color: 'var(--status-warning)', icon20: IconFolder20Filled, icon24: IconFolder24Filled },
  // Self-contained multi-colour icons: they carry their own fills (gradients for
  // the sparkle, a hardcoded orange for Everything), so they are used exactly as the
  // tiles use them and take no colour of their own.
  search:   { key: 'search',   color: '',        icon20: IconSearchSparkle24,       icon24: IconSearchSparkle24 },
  fileSearch: { key: 'fileSearch', color: '',    icon20: IconEverythingSearch,      icon24: IconEverythingSearch },
  settings: { key: 'settings', color: '',        icon20: IconSettings24,            icon24: IconSettings24 },
  // Home has no tile of its own — it IS the tile grid — so it keeps a plain
  // theme-coloured glyph.
  home:     { key: 'home',     color: '',        icon20: IconHome20Regular,         icon24: IconHome24Regular },
}

export function documentIcon(key: DocumentIconKey): DocumentIcon {
  return ICONS[key]
}

/** Every icon, for callers that need to enumerate (e.g. rasterizing the whole set). */
export function allDocumentIcons(): DocumentIcon[] {
  return Object.values(ICONS)
}

/** Route (+ addin flag) → icon key. Shared by tabs, recently-opened entries, and tiles. */
export function iconKeyForRoute(route: string, isOtzariaAddin = false): DocumentIconKey {
  if (route === '/html-view' && isOtzariaAddin) return 'addin'
  switch (route) {
    case '/book-view':
      return 'book'
    case '/pdf-view':
      return 'pdf'
    case '/html-view':
      return 'html'
    case '/txt-view':
      return 'txt'
    case '/':
      return 'home'
    case '/search':
      return 'search'
    // Destination routes, matched to their home-page tile.
    case '/books':
      return 'library'
    case '/hebrewbooks':
      return 'hbooks'
    case '/dictionary':
      return 'dict'
    case '/hebrew-calendar':
      return 'calendar'
    case '/midot':
      return 'ruler'
    case '/workspaces':
      return 'apps'
    case '/file-search':
      return 'fileSearch'
    case '/settings':
      return 'settings'
    default:
      return 'document'
  }
}

/** File name (+ addin flag) → icon key, for file-search results. */
export function iconKeyForFileName(fileName: string, isAddin = false): DocumentIconKey {
  if (isAddin) return 'addin'
  switch (fileName.toLowerCase().split('.').pop()) {
    case 'pdf':
      return 'pdf'
    case 'html':
    case 'htm':
    case 'mht':
    case 'mhtml':
      return 'html'
    case 'txt':
      return 'txt'
    default:
      return 'document'
  }
}
