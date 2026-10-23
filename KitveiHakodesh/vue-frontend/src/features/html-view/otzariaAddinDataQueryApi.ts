/**
 * Data-query API surface for Otzaria addins.
 *
 * Implements the official Otzaria plugin API (docs/plugin-sdk/API_REFERENCE.md in
 * the otzaria/otzaria repository) restricted to data-query methods only. Everything
 * with side effects on the app — reader.*, navigation.*, ui.*, network.*, database.*,
 * notes.*, history.*, fs.* and the rest — is rejected with PERMISSION_DENIED so
 * addins degrade gracefully instead of hanging.
 *
 * Allowed surface and the permissions reported for it:
 *   app.info.read           app.getInfo / getTheme / getLocale / getGrantedPermissions / getConnectivity
 *   library.books.read      library.findBooks / getBookMetadata / getTree
 *   library.content.read    library.getBookToc / getBookContent
 *   settings.read           settings.get / getMany (safe read-only key allowlist)
 *   plugin.storage.read/write  storage.* — per-addin sandboxed IndexedDB, no app data access
 *
 * Library access goes through seforimApi so it works in BOTH runtimes: dev routes
 * through the KitveiHakodesh service, hosted through the __webviewQuery bridge.
 *
 * Return shapes follow the official reference: `bookId` is the human-readable book
 * title, `id` is our numeric database id, book content is a plain-text string.
 */

import { getBookById, getAllTocEntries, getLinesPaged } from '@/webview-host/seforimApi'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBooksDataStore } from '@/stores/booksDataStore'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'
import type { BookRow } from '@/webview-host/queries.types'
import {
  addinStorageGet,
  addinStorageSet,
  addinStorageRemove,
  addinStorageListKeys,
} from './otzariaAddinStorage'

export const GRANTED_PERMISSIONS: readonly string[] = [
  'app.info.read',
  'library.books.read',
  'library.content.read',
  'settings.read',
  'plugin.storage.read',
  'plugin.storage.write',
]

export class OtzariaBridgeError extends Error {
  readonly code: string
  constructor(code: string, message: string) {
    super(message)
    this.code = code
  }
}

const DENIED_METHOD_PREFIXES = [
  'reader.', 'navigation.', 'ui.', 'network.', 'search.', 'database.',
  'notes.', 'history.', 'calendar.', 'notifications.', 'fs.',
  'publishedData.', 'feedback.', 'shortcut.', 'plugin.',
]
const DENIED_APP_METHODS = ['app.openUrl', 'app.getUserEmail']

const MAX_CONTENT_CHARS = 5000
const MAX_CONTENT_LINES = 500

// ── Theme ─────────────────────────────────────────────────────────────────────

/** Builds the official theme shape ({ mode, colorScheme, typography }) from the app's CSS variables. */
export function buildAddinTheme() {
  const rootStyle = document.documentElement.style
  const isDark = document.documentElement.getAttribute('data-theme-preset')?.includes('dark') ?? true
  const readVariable = (name: string, fallback: string) =>
    rootStyle.getPropertyValue(name).trim() || fallback

  const background = readVariable('--bg-primary-custom', isDark ? '#1e1e1e' : '#ffffff')
  const surface = readVariable('--bg-secondary-custom', isDark ? '#252526' : '#f3f3f3')
  const textPrimary = readVariable('--text-primary-custom', isDark ? '#d4d4d4' : '#616161')
  const textSecondary = readVariable('--text-secondary-custom', isDark ? '#858585' : '#999999')
  const settingsStore = useSettingsStore()

  return {
    mode: isDark ? 'dark' : 'light',
    colorScheme: {
      primary: '#0078d4',
      onPrimary: '#ffffff',
      secondary: textSecondary,
      onSecondary: '#ffffff',
      secondaryContainer: surface,
      onSecondaryContainer: textPrimary,
      surface,
      onSurface: textPrimary,
      surfaceContainerHigh: surface,
      surfaceContainerHighest: surface,
      background,
      onBackground: textPrimary,
      error: '#d13438',
      onError: '#ffffff',
      outline: textSecondary,
    },
    typography: {
      fontFamily: settingsStore.textFont,
      fontSize: settingsStore.fontSize,
      lineHeight: settingsStore.linePadding,
      commentatorsFontFamily: settingsStore.commentaryTextFont,
      commentatorsFontSize: settingsStore.commentaryFontSize,
    },
  }
}

function buildConnectivity() {
  return { isOfflineMode: false, hasNetwork: navigator.onLine, isOnline: navigator.onLine }
}

// ── Settings allowlist ────────────────────────────────────────────────────────

const SAFE_SETTING_READERS: Record<string, () => unknown> = {
  // Official Otzaria setting keys
  'key-dark-mode': () => document.documentElement.getAttribute('data-theme-preset')?.includes('dark') ?? true,
  'key-font-size': () => useSettingsStore().fontSize,
  'key-font-family': () => useSettingsStore().textFont,
  'key-line-height': () => useSettingsStore().linePadding,
  'key-commentators-font-size': () => useSettingsStore().commentaryFontSize,
  'key-commentators-font-family': () => useSettingsStore().commentaryTextFont,
  // Legacy keys from the first bridge version
  'reading.fontSize': () => useSettingsStore().fontSize,
  'reading.lineSpacing': () => useSettingsStore().linePadding,
  'app.isDark': () => document.documentElement.getAttribute('data-theme-preset')?.includes('dark') ?? true,
}

function readSafeSetting(key: string): unknown {
  return SAFE_SETTING_READERS[key]?.() ?? null
}

// ── Book resolution ───────────────────────────────────────────────────────────

/**
 * The loaded catalog, read from the books data store (one load per session, shared with
 * the whole app) instead of a fresh getAllBooks/getAllCategories round-trip per addin call.
 * The title map and the category map are built once per catalog instance and reused;
 * the store reassigns `allBooks` when the catalog changes, which invalidates them.
 */
interface AddinCatalog {
  books: BookRow[]
  booksById: Map<number, BookRow>
  booksByTitle: Map<string, BookRow>
  categoriesById: Map<number, CategoryNode>
  root: CategoryNode
}

let addinCatalog: AddinCatalog | null = null

async function loadAddinCatalog(): Promise<AddinCatalog> {
  const store = useBooksDataStore()
  await store.ensureLoaded()
  if (!store.loaded) throw new OtzariaBridgeError('INTERNAL', store.error ?? 'library not loaded')
  if (addinCatalog && addinCatalog.books === store.allBooks) return addinCatalog
  const booksByTitle = new Map<string, BookRow>()
  // First title wins, matching the old `find` over the list.
  for (const book of store.allBooks) if (!booksByTitle.has(book.title)) booksByTitle.set(book.title, book)
  const categoriesById = new Map<number, CategoryNode>()
  const collect = (nodes: CategoryNode[]) => {
    for (const node of nodes) {
      categoriesById.set(node.id, node)
      collect(node.children)
    }
  }
  collect(store.ROOT.children)
  addinCatalog = {
    books: store.allBooks,
    booksById: store.allBooksMap,
    booksByTitle,
    categoriesById,
    root: store.ROOT,
  }
  return addinCatalog
}

function toBookMeta(row: { id: number; title: string }) {
  return { id: row.id, bookId: row.title, title: row.title, type: 'text', source: 'library' }
}

/** Resolves { bookId } (title string, or a numeric id for legacy callers) / { id } to a book row. */
async function resolveBook(payload: Record<string, unknown>): Promise<BookRow> {
  const numericId =
    typeof payload.id === 'number' ? payload.id
    : typeof payload.bookId === 'number' ? payload.bookId
    : null
  const bookTitle = typeof payload.bookId === 'string' ? payload.bookId.trim() : ''
  if (numericId === null && !bookTitle)
    throw new OtzariaBridgeError('INVALID_PARAMS', 'bookId (title) or id is required')

  const catalog = await loadAddinCatalog()
  const book = numericId !== null
    ? catalog.booksById.get(numericId)
    : catalog.booksByTitle.get(bookTitle)
  if (!book) throw new OtzariaBridgeError('NOT_FOUND', 'book not found')
  return book
}

function buildCategoryPath(catalog: AddinCatalog, categoryId: number | null): string {
  if (categoryId == null) return ''
  const titles: string[] = []
  let current = catalog.categoriesById.get(categoryId)
  while (current) {
    titles.unshift(current.title)
    current = current.parentId != null ? catalog.categoriesById.get(current.parentId) : undefined
  }
  return titles.join('/')
}

// ── Library methods ───────────────────────────────────────────────────────────

async function findBooks(payload: Record<string, unknown>) {
  const searchText = typeof payload.query === 'string' ? payload.query.trim() : ''
  const limit = Math.min(Number(payload.limit) || 20, 200)
  const { books } = await loadAddinCatalog()
  const matches: BookRow[] = []
  for (const row of books) {
    if (matches.length >= limit) break
    if (!searchText || row.title.includes(searchText)) matches.push(row)
  }
  return matches.map(toBookMeta)
}

async function getBookMetadata(payload: Record<string, unknown>) {
  const book = await resolveBook(payload)
  const [catalog, bookInfo] = await Promise.all([loadAddinCatalog(), getBookById(book.id)])
  return {
    ...toBookMeta(book),
    categoryPath: buildCategoryPath(catalog, book.categoryId),
    totalLines: bookInfo?.totalLines ?? null,
    ...(book.authors ? { author: book.authors } : {}),
  }
}

async function getBookToc(payload: Record<string, unknown>) {
  const book = await resolveBook(payload)
  const entries = await getAllTocEntries(book.id)
  return entries.map((entry) => ({ text: entry.text, index: entry.lineIndex, level: entry.level }))
}

async function getBookContent(payload: Record<string, unknown>) {
  const book = await resolveBook(payload)
  const offset = Math.max(Number(payload.offset) || 0, 0)
  const characterLimit = Math.min(Number(payload.limit) || 1000, MAX_CONTENT_CHARS)
  const lines = await getLinesPaged(book.id, MAX_CONTENT_LINES, offset)
  const text = lines.map((line) => line.content).join('\n')
  return text.length > characterLimit ? text.slice(0, characterLimit) : text
}

interface TreeNode {
  title: string
  path: string
  categories: TreeNode[]
  books: { id: number; bookId: string; title: string; type: string; author?: string }[]
}

function toTreeBook(book: BookRow) {
  return {
    id: book.id,
    bookId: book.title,
    title: book.title,
    type: 'text',
    ...(book.authors ? { author: book.authors } : {}),
  }
}

/** Walks the catalog tree once, building the addin shape; stops at the requested path when given. */
function buildTreeNode(node: CategoryNode, path: string, includeBooks: boolean): TreeNode {
  return {
    title: node.title,
    path,
    categories: node.children.map((child) =>
      buildTreeNode(child, path ? `${path}/${child.title}` : child.title, includeBooks),
    ),
    books: includeBooks ? node.books.map(toTreeBook) : [],
  }
}

function findCategoryByPath(nodes: CategoryNode[], parentPath: string, wanted: string): [CategoryNode, string] | null {
  for (const node of nodes) {
    const path = parentPath ? `${parentPath}/${node.title}` : node.title
    if (path === wanted) return [node, path]
    const found = findCategoryByPath(node.children, path, wanted)
    if (found) return found
  }
  return null
}

async function getTree(payload: Record<string, unknown>) {
  const includeBooks = payload.includeBooks !== false
  const { root } = await loadAddinCatalog()

  const requestedPath = typeof payload.path === 'string' ? payload.path : ''
  if (!requestedPath) return buildTreeNode(root, '', includeBooks)
  const found = findCategoryByPath(root.children, '', requestedPath)
  return found ? buildTreeNode(found[0], found[1], includeBooks) : null
}

// ── Routing ───────────────────────────────────────────────────────────────────

/**
 * Routes one addin API call. Returns the `data` half of the official envelope;
 * throws OtzariaBridgeError for denied, unknown or failed calls.
 */
export async function routeDataQueryCall(
  method: string,
  payload: unknown,
  addinId: string,
): Promise<unknown> {
  if (DENIED_APP_METHODS.includes(method) || DENIED_METHOD_PREFIXES.some((prefix) => method.startsWith(prefix)))
    throw new OtzariaBridgeError(
      'PERMISSION_DENIED',
      `${method} is not available here: only data-query APIs are enabled`,
    )

  const parameters = (payload ?? {}) as Record<string, unknown>

  switch (method) {
    case 'app.getInfo':
      return { version: '1.0.0', buildNumber: '1', platform: 'windows' }
    case 'app.getTheme':
      return buildAddinTheme()
    case 'app.getLocale':
      return { locale: 'he', textDirection: 'rtl' }
    case 'app.getGrantedPermissions':
      return { permissions: [...GRANTED_PERMISSIONS] }
    case 'app.getConnectivity':
      return buildConnectivity()

    case 'library.findBooks':
      return findBooks(parameters)
    case 'library.getBookMetadata':
      return getBookMetadata(parameters)
    case 'library.getBookToc':
      return getBookToc(parameters)
    case 'library.getBookContent':
      return getBookContent(parameters)
    case 'library.getTree':
      return getTree(parameters)

    case 'settings.get':
      return readSafeSetting(String(parameters.key ?? ''))
    case 'settings.getMany': {
      const values: Record<string, unknown> = {}
      for (const key of (parameters.keys as string[]) ?? []) values[key] = readSafeSetting(key)
      return values
    }

    case 'storage.get':
      return addinStorageGet(addinId, String(parameters.key ?? ''))
    case 'storage.set':
      await addinStorageSet(addinId, String(parameters.key ?? ''), parameters.value)
      return true
    case 'storage.remove':
      await addinStorageRemove(addinId, String(parameters.key ?? ''))
      return true
    case 'storage.list':
      return addinStorageListKeys(addinId)
  }

  throw new OtzariaBridgeError('NOT_FOUND', `unknown method: ${method}`)
}

/** Official plugin.boot event payload. */
export function buildBootPayload(addinId: string) {
  return {
    plugin: { id: addinId, name: addinId, version: '' },
    app: { version: '1.0.0', buildNumber: '1', platform: 'windows', runMode: 'foreground' },
    theme: buildAddinTheme(),
    locale: { locale: 'he', textDirection: 'rtl' },
    permissions: [...GRANTED_PERMISSIONS],
    connectivity: buildConnectivity(),
  }
}
