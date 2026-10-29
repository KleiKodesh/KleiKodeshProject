<script setup lang="ts">
/**
 * "ספרים קרובים" dropdown in the BookView toolbar.
 *
 * Shows all books linked to the current book (SOURCE / TARGUM / COMMENTARY
 * connection types) grouped by meaningful category (ראשונים, אחרונים, etc.
 * for commentary; מקורות / תרגומים for flat types). Clicking a book opens it
 * in a new tab, scrolled to the line that corresponds to the current top
 * visible line.
 *
 * Resolution order for the target line:
 *   1. Direct hit  — the current top line already has a link to that book.
 *   2. Forward scan — nearest line ahead that has a link.
 *   3. Backward scan — nearest line behind that has a link.
 *   4. Fallback — open the book at its beginning (lineIndex 0).
 */
import { ref, computed, watch } from 'vue'
import { storeToRefs } from 'pinia'
import { IconLibrary16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { usePaneNavigation } from '@/composables/usePaneNavigation'
import { useBookViewStore } from '@/stores/bookViewStore'
import { useOverflowSubmenu } from './useOverflowSubmenu'
import {
  getLinkTargetForSourceLineAndBook,
  getNextSectionWithCommentary,
  getPrevSectionWithCommentary,
} from '@/webview-host/seforimApi'
import { loadStaticFilterGroupsForBook, type CommentaryGroup } from './commentary/useCommentary'
import {
  getPrimaryConnectionType,
  getBaseTextReverseConnectionTypeIds,
} from './commentary/commentaryConnectionTypes'
import {
  getReverseLineData,
  getReverseBooks,
  getAltTocStructures,
  getAllAltTocEntries,
  getLinesPaged,
} from '@/webview-host/seforimApi'
import type { LinesTable } from './lines/linesTable'
import type { TocEntry } from '@/webview-host/queries.types'
import { loadSectionTocEntries } from './lines/wordLinkSection'
import { displayCommentaryLabel } from './commentary/commentaryLabelDisplay'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps<{
  bookId: number | undefined
  filterGroups: CommentaryGroup[]
  relatedBooksLoaded: boolean
  currentScrollLineIndex: number
  lines: LinesTable
  onOpen?: () => void
  /** Renders as a row of the toolbar's "more" flyout, its panel a side submenu. */
  inOverflow?: boolean
}>()

const emit = defineEmits<{ 'open-change': [isOpen: boolean] }>()

const paneNavigation = usePaneNavigation()
const bookViewStore = useBookViewStore()
const { toolbarPosition } = storeToRefs(bookViewStore)

// ── Open / close ──────────────────────────────────────────────────────────────

const isOpen = ref(false)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

function setOpen(value: boolean) {
  isOpen.value = value
  emit('open-change', value)
}

const { justClosed } = useDropdownClose(dropdownRef, () => setOpen(false), {
  toggleButton: toggleButtonRef,
})

function toggleOpen() {
  if (justClosed.value) return
  const opening = !isOpen.value
  setOpen(opening)
  if (opening) props.onOpen?.()
}

// ── Book list — grouped by category (ראשונים / אחרונים / etc.) ───────────────
//
// For connection types that carry a subSectionLabel (COMMENTARY, OTHER), we use
// the subSectionLabel (the period/category) as the section header instead of the
// generic sectionLabel ("מפרשים"). For flat types (SOURCE, TARGUM, REFERENCE)
// there is no subSectionLabel, so we fall back to sectionLabel as before.

interface RelatedBook {
  bookId: number
  bookTitle: string
  /** Set for a commentary of a linked source book: the line is resolved through that source. */
  viaSourceBookId?: number
  /** A base text of the current book: its links point AT this book, not from it. */
  isBaseText?: boolean
  /** The book's own primary base when that is a commentary on the via-source (Rabbeinu Yonah -> Rif Berachot). */
  chainBaseBookId?: number
}

interface RelatedBooksSection {
  label: string
  books: RelatedBook[]
}

const dropdownPositionClass = computed(() => {
  return `dropdown-${toolbarPosition.value}`
})
const submenuStyle = useOverflowSubmenu(() => !!props.inOverflow, isOpen, toggleButtonRef, dropdownRef)

const sections = computed<RelatedBooksSection[]>(() => {
  const sectionMap = new Map<string, RelatedBook[]>()
  const sectionOrder: string[] = []
  const seen = new Set<number>()

  for (const group of props.filterGroups) {
    // Use the specific category label (ראשונים, אחרונים…) when available,
    // otherwise fall back to the connection-type section label (מקורות, תרגומים…).
    const label = group.subSectionLabel || group.sectionLabel || ''
    if (!sectionMap.has(label)) {
      sectionMap.set(label, [])
      sectionOrder.push(label)
    }
    if (seen.has(group.bookId)) continue
    seen.add(group.bookId)
    sectionMap.get(label)!.push({
      bookId: group.bookId,
      bookTitle: group.bookTitle,
      isBaseText: getPrimaryConnectionType(group.connectionTypes) === 'SOURCE',
    })
  }

  // Commentaries of each linked source book (ריב"א על ברכות → all of ברכות's commentaries),
  // placed right after the source books' own section(s), before every other section.
  const lastSourceSection = sectionOrder.reduce((last, label, index) =>
    sectionMap.get(label)!.some((book) => book.isBaseText) ? index : last, -1)
  let insertAt = lastSourceSection + 1
  for (const source of sourceCommentaries.value) {
    const label = t('bookView.related.commentariesOf', { title: source.sourceTitle })
    const books: RelatedBook[] = []
    for (const { group, baseBookId } of source.groups) {
      if (group.bookId === props.bookId || seen.has(group.bookId)) continue
      if (getPrimaryConnectionType(group.connectionTypes) === 'SOURCE') continue
      seen.add(group.bookId)
      books.push({
        bookId: group.bookId,
        bookTitle: group.bookTitle,
        viaSourceBookId: source.sourceBookId,
        chainBaseBookId: baseBookId != null && baseBookId !== source.sourceBookId ? baseBookId : undefined,
      })
    }
    sectionMap.set(label, books)
    sectionOrder.splice(insertAt++, 0, label)
  }

  return sectionOrder
    .map((label) => ({ label, books: sectionMap.get(label)! }))
    .filter((section) => section.books.length > 0)
})

// ── Commentaries of linked source books ──────────────────────────────────────

interface SourceCommentaries {
  sourceBookId: number
  sourceTitle: string
  groups: { group: CommentaryGroup; baseBookId: number | undefined }[]
}

const sourceCommentaries = ref<SourceCommentaries[]>([])

watch(
  currentSourceSignature,
  async (signature) => {
    const sources = props.filterGroups.filter((group) =>
      getPrimaryConnectionType(group.connectionTypes) === 'SOURCE')
    const unique = sources.filter((group, index) =>
      sources.findIndex((other) => other.bookId === group.bookId) === index)
    const loaded = await Promise.all(unique.map(async (source) => ({
      sourceBookId: source.bookId,
      sourceTitle: source.bookTitle,
      groups: await loadOwnCommentaryGroups(source.bookId),
    })))
    if (signature !== currentSourceSignature()) return
    sourceCommentaries.value = loaded
  },
  { immediate: true },
)

/**
 * The source's commentary groups, minus books whose own base text is a different book
 * (Rif Shabbat merely cites Berachot). A book on a commentary of the source (Rabbeinu
 * Yonah on Rif Berachot) stays — its base is itself a commentary on the source.
 */
async function loadOwnCommentaryGroups(
  sourceBookId: number,
): Promise<{ group: CommentaryGroup; baseBookId: number | undefined }[]> {
  const groups = await loadStaticFilterGroupsForBook(sourceBookId).catch(() => [])
  const typeIds = getBaseTextReverseConnectionTypeIds()
  const bases = await Promise.all(groups.map(async (group) =>
    (await getReverseBooks(group.bookId, typeIds).catch(() => [])).map((row) => row.sourceBookId)))
  // getReverseBooks lists a book's bases best first (declared before inferred), and a
  // stray COMMENTARY link from the source is common — so only the primary base counts.
  const primary = bases.map((own) => own[0])
  const onSource = new Set(groups.filter((_, index) => primary[index] === sourceBookId).map((g) => g.bookId))
  return groups
    .map((group, index) => ({ group, baseBookId: primary[index] }))
    .filter(({ baseBookId }) => baseBookId == null || baseBookId === sourceBookId || onSource.has(baseBookId))
}

function currentSourceSignature() {
  return props.filterGroups
    .filter((group) => getPrimaryConnectionType(group.connectionTypes) === 'SOURCE')
    .map((group) => group.bookId)
    .filter((id, index, ids) => ids.indexOf(id) === index)
    .join(',')
}

// ── Opening a book ────────────────────────────────────────────────────────────

const openingBookId = ref<number | null>(null)

interface LinkedLine {
  targetLineId: number
  lineIndex: number
}

/** The line in `targetBookId` linked from `fromLineId` in `fromBookId`, or the nearest linked one ahead / behind. */
async function resolveLinkedLine(
  fromBookId: number, fromLineId: number, fromLineIndex: number, targetBookId: number,
): Promise<LinkedLine | undefined> {
  // 1. Direct hit — does the line already link to this book?
  const directRows = await getLinkTargetForSourceLineAndBook(fromLineId, targetBookId)
  if (directRows.length) return directRows[0]!

  // 2. Forward scan — nearest line ahead with a link to this book
  const forwardRows = await getNextSectionWithCommentary(fromBookId, targetBookId, fromLineIndex)
  if (forwardRows.length) {
    const targetRows = await getLinkTargetForSourceLineAndBook(forwardRows[0]!.id, targetBookId)
    if (targetRows.length) return targetRows[0]!
  }

  // 3. Backward scan — nearest line behind with a link to this book
  const backwardRows = await getPrevSectionWithCommentary(fromBookId, targetBookId, fromLineIndex)
  if (backwardRows.length) {
    const targetRows = await getLinkTargetForSourceLineAndBook(backwardRows[0]!.id, targetBookId)
    if (targetRows.length) return targetRows[0]!
  }

  return undefined
}

// Base-text links are stored base line → this book's line, so the base line is found by a
// REVERSE lookup on this book's line; probed outward from the top line, nearest first.
const BASE_LINE_PROBE_RADIUS = 30

async function resolveBaseLine(topLineIndex: number, baseBookId: number): Promise<LinkedLine | undefined> {
  const typeIds = getBaseTextReverseConnectionTypeIds()
  for (let distance = 0; distance <= BASE_LINE_PROBE_RADIUS; distance++) {
    for (const lineIndex of distance === 0 ? [topLineIndex] : [topLineIndex + distance, topLineIndex - distance]) {
      const line = props.lines.at(lineIndex)
      if (line == null) continue
      const rows = await getReverseLineData([line.id], typeIds)
      const hit = rows.find((row) => row.sourceBookId === baseBookId)
      if (hit) return { targetLineId: hit.sourceLineId, lineIndex: hit.lineIndex }
    }
  }
  return undefined
}

// ── TOC match cascade ─────────────────────────────────────────────────────────
//
// Books cut by the same headings (both by daf, both by perek) line up by label. The top
// line's deepest heading is looked up in the target; with no match there, its parent, and
// so on up. The main TOC goes first, the alt TOC (daf / parasha structures) only when the
// main one matched at no level.

interface TocSectionMatch {
  start: number
  /** Exclusive; null = to the end of the book. */
  end: number | null
  source: 'main' | 'alt'
  /** 0 = the deepest heading matched, 1 = its parent, … */
  depth: number
}

/** The entry's label with its parent's — "daf 45a" alone can repeat across a book's chapters. */
function tocLabelPair(entry: TocEntry, byId: Map<number, TocEntry>): string {
  const parent = entry.parentId != null ? byId.get(entry.parentId) : undefined
  return `${parent?.text.trim() ?? ''} / ${entry.text.trim()}`
}

// Order-independent on purpose: alt TOC entries arrive in id order, not lineIndex order.

/** The heading the line sits under: the last-starting positioned entry at or before it, deepest on ties. */
function findHeadingAt(entries: TocEntry[], lineIndex: number): TocEntry | undefined {
  let found: TocEntry | undefined
  for (const e of entries) {
    if (e.lineIndex == null || e.lineIndex > lineIndex) continue
    if (found == null || e.lineIndex > found.lineIndex! || (e.lineIndex === found.lineIndex && e.level > found.level)) found = e
  }
  return found
}

/** An entry's line range; an unpositioned heading starts at its earliest positioned descendant. */
function tocEntryRange(entry: TocEntry, entries: TocEntry[]): { start: number; end: number | null } | null {
  let start = entry.lineIndex
  if (start == null) {
    const childrenOf = new Map<number, TocEntry[]>()
    for (const e of entries) if (e.parentId != null) childrenOf.set(e.parentId, [...(childrenOf.get(e.parentId) ?? []), e])
    const stack = [...(childrenOf.get(entry.id) ?? [])]
    while (stack.length) {
      const e = stack.pop()!
      if (e.lineIndex != null && (start == null || e.lineIndex < start)) start = e.lineIndex
      stack.push(...(childrenOf.get(e.id) ?? []))
    }
  }
  if (start == null) return null
  let end: number | null = null
  for (const e of entries) {
    if (e.lineIndex == null || e.lineIndex <= start || e.level > entry.level) continue
    if (end == null || e.lineIndex < end) end = e.lineIndex
  }
  return { start, end }
}

function matchTocCascade(
  currentEntries: TocEntry[], targetEntries: TocEntry[], topLineIndex: number,
): Omit<TocSectionMatch, 'source'> | null {
  const currentById = new Map(currentEntries.map((e) => [e.id, e]))
  const targetById = new Map(targetEntries.map((e) => [e.id, e]))
  let heading = findHeadingAt(currentEntries, topLineIndex)
  for (let depth = 0; heading != null; depth++) {
    const label = heading.text.trim()
    const sameLabel = targetEntries.filter((e) => e.text.trim() === label)
    // A label repeated in the target is disambiguated by its parent's label.
    const pair = tocLabelPair(heading, currentById)
    const matches = sameLabel.length > 1
      ? sameLabel.filter((e) => tocLabelPair(e, targetById) === pair)
      : sameLabel
    if (matches.length === 1) {
      const range = tocEntryRange(matches[0]!, targetEntries)
      if (range) return { ...range, depth }
    }
    heading = heading.parentId != null ? currentById.get(heading.parentId) : undefined
  }
  return null
}

async function loadAltTocEntryLists(bookId: number): Promise<{ key: string; entries: TocEntry[] }[]> {
  const structures = await getAltTocStructures(bookId)
  return Promise.all(structures.map(async (s) => ({ key: s.key, entries: await getAllAltTocEntries(s.id) })))
}

async function findMatchingTocSection(
  fromBookId: number, fromLineIndex: number, targetBookId: number,
): Promise<TocSectionMatch | null> {
  const [fromEntries, targetEntries] = await Promise.all([
    loadSectionTocEntries(fromBookId), loadSectionTocEntries(targetBookId),
  ])
  const main = matchTocCascade(fromEntries, targetEntries, fromLineIndex)
  if (main) return { ...main, source: 'main' }

  const [fromAlt, targetAlt] = await Promise.all([
    loadAltTocEntryLists(fromBookId), loadAltTocEntryLists(targetBookId),
  ])
  for (const from of fromAlt) {
    const target = targetAlt.find((t) => t.key === from.key)
    const alt = target && matchTocCascade(from.entries, target.entries, fromLineIndex)
    if (alt) return { ...alt, source: 'alt' }
  }
  return null
}

// ── Resolving the target line ─────────────────────────────────────────────────

/**
 * Where the reader is, in the book whose division the target follows — what the target's
 * TOC is compared against. The current book's top line, except for a commentary on a
 * commentary of the source (Rabbeinu Yonah, Shiltei HaGiborim: their daf is the Rif's
 * folio), where it is the line reached in that intermediate base.
 */
interface ReaderPosition {
  bookId: number
  lineIndex: number
}

interface ResolvedTarget {
  /** The target line reached by links, if any. */
  linked: number | undefined
  /**
   * What the target's TOC is compared against, in order of preference — the first whose
   * matched section passes the division check wins. Empty when the chain broke.
   *
   * The trap: the Rif (and books on Rif pages) label their sections "daf X" too, but X is
   * the Rif's own folio, not the Talmud daf. Reading Rif Berachot, its "daf 20a" must not
   * be matched against Tosafot's "daf 20a"; the reader's place in the Talmud (Rif's link
   * into Berachot) is the next position tried. Rif -> Shiltei stays on Rif folios.
   */
  positions: ReaderPosition[]
}

/** How many of the matched section's first lines are mapped back to the reader's position. */
const DIVISION_PROBE_LINES = 3

/**
 * Same heading, same division? Rif Berachot is cut by "daf" too, but by its own folios:
 * its "daf 20a" is not Bavli 20a. The matched section is trusted only when one of its
 * first lines links (either direction) into the section the reader sits in.
 */
async function sectionMatchesReaderDivision(
  targetBookId: number, section: TocSectionMatch, position: ReaderPosition,
): Promise<boolean> {
  const entries = await loadSectionTocEntries(position.bookId)
  const heading = findHeadingAt(entries, position.lineIndex)
  const range = heading && tocEntryRange(heading, entries)
  if (!range) return false
  const inReaderSection = (lineIndex: number) =>
    lineIndex >= range.start && (range.end == null || lineIndex < range.end)

  const probeCount = section.end == null ? DIVISION_PROBE_LINES
    : Math.min(DIVISION_PROBE_LINES, section.end - section.start)
  const probes = await getLinesPaged(targetBookId, probeCount, section.start)
  const [reverseRows, forwardRows] = await Promise.all([
    getReverseLineData(probes.map((line) => line.id), getBaseTextReverseConnectionTypeIds()),
    Promise.all(probes.map((line) => getLinkTargetForSourceLineAndBook(line.id, position.bookId))),
  ])
  return reverseRows.some((row) => row.sourceBookId === position.bookId && inReaderSection(row.lineIndex))
    || forwardRows.flat().some((row) => inReaderSection(row.lineIndex))
}

/**
 * The link-resolved line, kept inside the TOC section matching the reader's position: a
 * link outside it is a lateral one, so the section start wins; no link, the section start;
 * neither, the book start. A section whose division does not line up with the reader's
 * (the Rif's folios against the Bavli's dafs) is ignored — link only.
 */
async function resolveTargetLineIndex(book: RelatedBook): Promise<number | undefined> {
  const { linked, positions } = await resolveLinkedTarget(book)
  for (const position of positions) {
    const section = await findMatchingTocSection(position.bookId, position.lineIndex, book.bookId)
    if (section == null) continue
    const inside = linked != null && linked >= section.start && (section.end == null || linked < section.end)
    if (inside) return linked
    if (await sectionMatchesReaderDivision(book.bookId, section, position)) return section.start
  }
  return linked
}

async function resolveLinkedTarget(book: RelatedBook): Promise<ResolvedTarget> {
  if (props.bookId == null) return { linked: undefined, positions: [] }
  const top: ReaderPosition = { bookId: props.bookId, lineIndex: props.currentScrollLineIndex }

  // Find the line id for the current top visible line
  const topLine = props.lines.at(props.currentScrollLineIndex)
  if (topLine == null) return { linked: undefined, positions: [top] }

  // A commentary of a source book has no direct link: hop through the source's line —
  // and, for a commentary on one of the source's commentaries, through that one's line too.
  // A commentary on the current book itself (Rif -> Shiltei) shares its division: direct links.
  if (book.viaSourceBookId != null && book.chainBaseBookId !== props.bookId) {
    const sourceLine = await resolveBaseLine(topLine.lineIndex, book.viaSourceBookId)
    if (sourceLine == null) return { linked: undefined, positions: [top] }
    if (book.chainBaseBookId != null) {
      const chainLine = await resolveLinkedLine(
        book.viaSourceBookId, sourceLine.targetLineId, sourceLine.lineIndex, book.chainBaseBookId)
      if (chainLine == null) return { linked: undefined, positions: [] }
      const target = await resolveLinkedLine(
        book.chainBaseBookId, chainLine.targetLineId, chainLine.lineIndex, book.bookId)
      return {
        linked: target?.lineIndex,
        positions: [{ bookId: book.chainBaseBookId, lineIndex: chainLine.lineIndex }],
      }
    }
    const target = await resolveLinkedLine(
      book.viaSourceBookId, sourceLine.targetLineId, sourceLine.lineIndex, book.bookId)
    return { linked: target?.lineIndex, positions: [top, { bookId: book.viaSourceBookId, lineIndex: sourceLine.lineIndex }] }
  }

  if (book.isBaseText) {
    const baseLine = await resolveBaseLine(topLine.lineIndex, book.bookId)
    return {
      linked: baseLine?.lineIndex,
      positions: baseLine ? [top, { bookId: book.bookId, lineIndex: baseLine.lineIndex }] : [top],
    }
  }

  // Fallback (undefined) — open at the beginning
  const target = await resolveLinkedLine(props.bookId, topLine.id, topLine.lineIndex, book.bookId)
  return { linked: target?.lineIndex, positions: [top] }
}

async function onBookClick(book: RelatedBook) {
  if (openingBookId.value != null) return
  openingBookId.value = book.bookId
  setOpen(false)
  try {
    // A failed lookup still opens the book (at its start) rather than nothing.
    const targetLineIndex = await resolveTargetLineIndex(book).catch(() => undefined)
    paneNavigation.openTab({
      route: '/book-view',
      title: book.bookTitle,
      bookId: book.bookId,
      openTocLineIndex: targetLineIndex,
    })
  } finally {
    openingBookId.value = null
  }
}
</script>

<template>
  <div class="related-books-wrapper" :class="{ 'overflow-host': inOverflow }">
    <button
      ref="toggleButtonRef"
      :class="{ active: isOpen, 'overflow-row': inOverflow }"
      :role="inOverflow ? 'menuitem' : undefined"
      :title="$t('bookView.related.title')"
      @click="toggleOpen"
    >
      <IconLibrary16Regular />
      <span v-if="inOverflow">{{ $t('bookView.related.title') }}</span>
    </button>

    <div v-if="isOpen" ref="dropdownRef" class="related-books-dropdown" :class="inOverflow ? 'overflow-submenu' : dropdownPositionClass" :style="submenuStyle">
      <div v-if="!relatedBooksLoaded" class="loading-message">{{ $t('bookView.related.scanning') }}</div>
      <div v-else-if="sections.length === 0" class="empty-message">{{ $t('bookView.related.empty') }}</div>
      <template v-else v-for="section in sections" :key="section.label">
        <div class="section-caption">{{ displayCommentaryLabel(section.label) }}</div>
        <button
          v-for="book in section.books"
          :key="book.bookId"
          class="book-row"
          :disabled="openingBookId === book.bookId"
          @click="onBookClick(book)"
        >
          {{ book.bookTitle }}
        </button>
      </template>
    </div>
  </div>
</template>

<style scoped>
.related-books-wrapper {
  position: relative;
}

/* ── Toggle button — inherits toolbar button styles from main.css ── */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  /* The toolbar's button size, not a copy of what it happened to be: the overflow
     arithmetic charges every pinned control one of these, so a button that is its own size
     makes the toolbar believe it has room it does not have. */
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button.active {
  color: var(--accent-color);
}

/* ── Dropdown panel ── */
.related-books-dropdown {
  position: absolute;
  min-width: 180px;
  max-width: 300px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  z-index: 100;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* ── Positioning based on toolbar position ── */
.dropdown-top {
  top: calc(100% + 4px);
  inset-inline-start: 0;
}

.dropdown-bottom {
  bottom: calc(100% + 4px);
  inset-inline-start: 0;
}

.dropdown-left {
  top: 0;
  inset-inline-end: calc(100% + 4px);
}

.dropdown-right {
  top: 0;
  inset-inline-start: calc(100% + 4px);
}

.empty-message {
  padding: 6px 10px;
  font-size: 12px;
  color: var(--text-secondary);
}

.loading-message {
  padding: 6px 10px;
  font-size: 12px;
  color: var(--text-secondary);
}

/* ── Section captions ── */
.section-caption {
  padding: 4px 10px 2px;
  font-size: 10px;
  color: var(--text-secondary);
  user-select: none;
  pointer-events: none;
}
.section-caption:not(:first-child) {
  border-top: 1px solid var(--border-color);
  margin-top: 2px;
  padding-top: 6px;
}

/* ── Book rows ── */
.book-row {
  display: block;
  width: 100%;
  height: 28px;
  padding: 0 10px;
  text-align: start;
  font-size: 12px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  border-radius: 0;
}
.book-row:hover {
  background: color-mix(in srgb, var(--text-primary) 6%, transparent);
}
.book-row:active {
  background: color-mix(in srgb, var(--text-primary) 10%, transparent);
}
.book-row:disabled {
  opacity: 0.5;
  cursor: not-allowed;
}
</style>
