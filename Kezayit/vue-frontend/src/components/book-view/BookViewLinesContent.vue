<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { storeToRefs } from 'pinia'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { LineItem } from './useLinesTable'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'
import ContextMenu from '@/components/common/ContextMenu.vue'
import type { ContextMenuItem } from '@/components/common/ContextMenu.vue'
import { useEventListener } from '@vueuse/core'
import { useScopedKeys } from '@/composables/useTextSelectionKeys'
import { useScopedCopy } from '@/composables/useLineCopy'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'

const emit = defineEmits<{ scrolled: [number, number]; lineSelected: [number]; 'ctrl-f': [] }>()
const props = defineProps<{
  // lines + prioritise passed from BookViewPage (single useLines call, no duplicate fetch)
  lines: LineItem[]
  prioritise: (lineIndex: number) => void
  altTocLabelMap?: Map<number, string>
  selectedLineId?: number | null
  bottomVisible?: boolean
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
  hiddenCommentaryBookIds?: Set<number>
  searchQuery?: string
  currentMatchLineIndex?: number
  currentMatchOccurrence?: number
  // TOC / search-result navigation: scroll to this line index on first load
  initialLineIndex?: number
  // Session restore: scroll to this line index on first load (from persisted state)
  initialScrollIndex?: number
  initialScrollOffset?: number
  searchHighlightLineIndex?: number
  searchHighlightQuery?: string
  searchHighlightSnippet?: string
  searchHighlightTerms?: string[]
  searchBarVisible?: boolean
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { zoom, autoSelectTopLine } = storeToRefs(bookViewStore)
const tabId = tabStore.activeTabId
const bookId = tabStore.activeTab.bookId!

const diacriticsState = computed(() => settingsStore.diacriticsState)
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

// ── Render helpers ────────────────────────────────────────────────────────────

function highlightMatches(
  raw: string,
  content: string,
  query: string,
  isCurrentLine: boolean,
  currentOccurrence: number,
): string {
  const q = removeDiacriticsForSearch(query.trim())
  if (!q) return content
  const stripped = removeDiacriticsForSearch(content.replace(/<[^>]*>/g, ''))
  const matchStarts = new Set<number>()
  let idx = 0
  while ((idx = stripped.indexOf(q, idx)) !== -1) {
    matchStarts.add(idx)
    idx++
  }
  if (!matchStarts.size) return content

  const out: string[] = []
  let strippedPos = 0,
    inTag = false,
    inMatch = false,
    matchStrippedCount = 0,
    matchOccurrence = 0
  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!
    if (ch === '<') {
      inTag = true
      out.push(ch)
      continue
    }
    if (ch === '>') {
      inTag = false
      out.push(ch)
      continue
    }
    if (inTag) {
      out.push(ch)
      continue
    }
    const isDiacritic = /[\u0591-\u05C7]/.test(ch)
    if (!isDiacritic && matchStarts.has(strippedPos) && !inMatch) {
      out.push(
        `<mark class="search-match${isCurrentLine && matchOccurrence === currentOccurrence ? ' current' : ''}">`,
      )
      inMatch = true
      matchStrippedCount = 0
    }
    out.push(ch)
    if (!isDiacritic) {
      if (inMatch && ++matchStrippedCount === q.length) {
        out.push('</mark>')
        inMatch = false
        matchOccurrence++
      }
      strippedPos++
    }
  }
  return out.join('')
}

/**
 * Highlight search-result terms within the snippet region of a line.
 *
 * The C# backend strips HTML and all diacritics (Unicode Non-Spacing Marks) before
 * matching, so both the snippet and the terms are plain stripped text. We need to:
 *   1. Find where the snippet sits inside the stripped line text (with retry).
 *   2. Within that region, highlight each term using the same HTML-aware / diacritic-aware
 *      walk that highlightMatches uses.
 *
 * Retry strategy: if the full snippet isn't found, progressively drop words from both
 * ends (alternating) until at least 2 words remain. This handles edge cases where the
 * C# snippet was trimmed mid-word or the line content differs slightly.
 */
function highlightSearchResult(
  raw: string,
  content: string,
  snippet: string,
  terms: string[],
): string {
  if (!snippet || !terms.length) return content

  // Use raw (pre-censoring) for region detection — the C# snippet was built from uncensored text.
  // Use content (post-censoring) for the output walk so the displayed text is correct.
  const strippedContent = removeDiacriticsForSearch(raw.replace(/<[^>]*>/g, ''))
  const strippedSnippet = removeDiacriticsForSearch(snippet)

  // Find the snippet region in the stripped content, with word-dropping retry.
  const region = findSnippetRegion(strippedContent, strippedSnippet)
  if (!region) return content

  const { start: regionStart, end: regionEnd } = region

  // Build a set of (strippedPos, length) pairs for each term occurrence within the region.
  const matchRanges: Array<{ start: number; len: number }> = []
  for (const term of terms) {
    const t = removeDiacriticsForSearch(term)
    if (!t) continue
    let idx = regionStart
    while (idx < regionEnd && (idx = strippedContent.indexOf(t, idx)) !== -1 && idx < regionEnd) {
      matchRanges.push({ start: idx, len: t.length })
      idx++
    }
  }
  if (!matchRanges.length) return content

  // Sort by start position so we can walk left-to-right.
  matchRanges.sort((a, b) => a.start - b.start)

  // Walk the content HTML-aware + diacritic-aware, inserting <mark> tags at match boundaries.
  const out: string[] = []
  let strippedPos = 0
  let inTag = false
  let inMatch = false
  let matchEndPos = 0
  let rangeIdx = 0

  for (let i = 0; i < content.length; i++) {
    const ch = content[i]!

    if (ch === '<') {
      inTag = true
      out.push(ch)
      continue
    }
    if (ch === '>') {
      inTag = false
      out.push(ch)
      continue
    }
    if (inTag) {
      out.push(ch)
      continue
    }

    const isDiacritic = /[\u0591-\u05C7]/.test(ch)

    if (!isDiacritic) {
      // Close any open match that has ended.
      if (inMatch && strippedPos >= matchEndPos) {
        out.push('</mark>')
        inMatch = false
      }

      // Skip overlapping/consumed ranges.
      while (rangeIdx < matchRanges.length && matchRanges[rangeIdx]!.start < strippedPos) {
        rangeIdx++
      }

      // Open a new match if we're at a range start and not already inside one.
      if (
        !inMatch &&
        rangeIdx < matchRanges.length &&
        matchRanges[rangeIdx]!.start === strippedPos
      ) {
        out.push('<mark class="search-match">')
        matchEndPos = strippedPos + matchRanges[rangeIdx]!.len
        inMatch = true
        rangeIdx++
      }
    }

    out.push(ch)
    if (!isDiacritic) strippedPos++
  }

  if (inMatch) out.push('</mark>')
  return out.join('')
}

/**
 * Find the start/end positions (in stripped-text coordinates) of the snippet within the line.
 * Retries by progressively dropping words from the edges if the full snippet isn't found.
 * Returns null if no match found with at least 2 words.
 */
function findSnippetRegion(
  strippedLine: string,
  strippedSnippet: string,
): { start: number; end: number } | null {
  // Strip leading/trailing ellipsis that C# adds when snippet is a substring.
  const clean = strippedSnippet
    .replace(/^\.{2,}/, '')
    .replace(/\.{2,}$/, '')
    .trim()
  if (!clean) return null

  // Try the full snippet first, then progressively drop words from edges.
  const words = clean.split(/\s+/).filter(Boolean)
  if (!words.length) return null

  // Try substrings: full → drop last → drop first → drop both → ...
  // We alternate dropping from end and start, keeping at least 2 words.
  const candidates: string[] = []
  let lo = 0,
    hi = words.length
  candidates.push(words.slice(lo, hi).join(' '))
  while (hi - lo > 2) {
    // drop from end
    hi--
    candidates.push(words.slice(lo, hi).join(' '))
    if (hi - lo <= 2) break
    // drop from start
    lo++
    candidates.push(words.slice(lo, hi).join(' '))
  }

  for (const candidate of candidates) {
    const idx = strippedLine.indexOf(candidate)
    if (idx !== -1) {
      return { start: idx, end: idx + candidate.length }
    }
  }
  return null
}

// Cache rendered HTML per line — avoids re-running applyDiacriticsFilter (DOM TreeWalker)
// and censorDivineNames (6 regexes) on every render cycle for unchanged lines.
// The cache is invalidated as a whole whenever any rendering input changes.
const renderCache = new Map<number, string>()
let renderCacheKey = ''

function getRenderCacheKey(): string {
  return `${diacriticsState.value}|${settingsStore.censorDivineNames}|${props.searchQuery ?? ''}|${props.currentMatchLineIndex ?? -1}|${props.currentMatchOccurrence ?? 0}|${props.searchHighlightLineIndex ?? -1}|${props.searchHighlightQuery ?? ''}|${props.searchHighlightSnippet ?? ''}|${props.searchHighlightTerms?.join(',') ?? ''}`
}

function lineContent(raw: string, lineIndex: number): string {
  const key = getRenderCacheKey()
  if (key !== renderCacheKey) {
    renderCache.clear()
    renderCacheKey = key
  }
  const cached = renderCache.get(lineIndex)
  if (cached !== undefined) return cached

  let content =
    diacriticsState.value === 0 ? raw : applyDiacriticsFilter(raw, diacriticsState.value)
  if (settingsStore.censorDivineNames) content = censorDivineNames(content)
  if (props.searchQuery?.trim())
    content = highlightMatches(
      raw,
      content,
      props.searchQuery,
      lineIndex === props.currentMatchLineIndex,
      props.currentMatchOccurrence ?? 0,
    )
  if (lineIndex === props.searchHighlightLineIndex) {
    if (props.searchHighlightSnippet && props.searchHighlightTerms?.length) {
      content = highlightSearchResult(
        raw,
        content,
        props.searchHighlightSnippet,
        props.searchHighlightTerms,
      )
    } else if (props.searchHighlightQuery?.trim()) {
      content = highlightMatches(raw, content, props.searchHighlightQuery, false, -1)
    }
  }

  renderCache.set(lineIndex, content)
  return content
}

// ── Scroller setup ────────────────────────────────────────────────────────────

const scrollerEl = ref<HTMLElement | null>(null)

const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('ctrl-f'),
})
useScopedCopy(
  scrollerEl,
  () => props.lines.map((l) => l.content).filter(Boolean) as string[],
  isSelectAll,
)
useVirtualScrollerKeys(
  scrollerEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => props.lines.length,
)

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const contextMenuItems: ContextMenuItem[] = [
  { label: 'העתק', action: () => document.execCommand('copy') },
  {
    label: 'העתק כבלוק',
    action: () => {
      let joined: string
      if (isSelectAll.value) {
        joined = props.lines
          .map((l) => l.content)
          .filter(Boolean)
          .join(' ')
      } else {
        const sel = window.getSelection()
        if (!sel || sel.rangeCount === 0) return
        const range = sel.getRangeAt(0)
        const fragment = range.cloneContents()
        const tmp = document.createElement('div')
        tmp.appendChild(fragment)
        joined = Array.from(tmp.querySelectorAll('.line'))
          .map((el) => el.innerHTML)
          .join(' ')
        if (!joined) joined = tmp.innerHTML
      }
      if (!joined.trim()) return
      const htmlContent = `<!DOCTYPE html><html><head><meta charset="utf-8"><style>body{direction:rtl;}</style></head><body><div>${joined}</div></body></html>`
      const tempDiv = document.createElement('div')
      tempDiv.innerHTML = joined
      navigator.clipboard.write([
        new ClipboardItem({
          'text/html': new Blob([htmlContent], { type: 'text/html' }),
          'text/plain': new Blob([tempDiv.textContent ?? ''], { type: 'text/plain' }),
        }),
      ])
    },
  },
  { label: 'בחר הכל', action: selectAllInContainer },
]

const virtualizer = useVirtualizer(
  computed(() => ({
    count: props.lines.length,
    getScrollElement: () => scrollerEl.value,
    estimateSize: () => 32,
    overscan: 10,
  })),
)

const virtualItems = computed(() => virtualizer.value.getVirtualItems())
const totalSize = computed(() => virtualizer.value.getTotalSize())

// ── Scroll capture ────────────────────────────────────────────────────────────

function captureScrollPos() {
  const first = virtualItems.value[0]
  if (!first || !scrollerEl.value) return null
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollerEl.value.scrollTop - first.start),
  }
}

// ── Scroll restore ────────────────────────────────────────────────────────────

let programmaticScrollTimer: ReturnType<typeof setTimeout> | null = null
let programmaticScrolling = false

function restoreScrollPos(lineIndex: number, scrollOffset = 0) {
  programmaticScrolling = true
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  virtualizer.value.scrollToIndex(lineIndex, { align: 'start' })
  requestAnimationFrame(() => {
    const item = virtualizer.value.measurementsCache.find((m) => m.index === lineIndex)
    if (item && scrollerEl.value) scrollerEl.value.scrollTop = item.start + scrollOffset
    requestAnimationFrame(() => {
      programmaticScrolling = false
    })
  })
}

// ── Initial scroll on load ────────────────────────────────────────────────────

// ── Initial scroll on load ────────────────────────────────────────────────────
// Waits for lines to be non-empty, then waits for the target line's content to load,
// then restores scroll. The outer watch stops itself after the first successful restore
// so it doesn't re-trigger on subsequent lines updates.
{
  let restored = false
  let stop: (() => void) | null = null
  stop = watch(
    () => props.lines,
    (val) => {
      if (!val.length) return
      const targetIndex = props.initialLineIndex ?? props.initialScrollIndex
      if (targetIndex == null) {
        stop?.()
        return
      }
      props.prioritise(targetIndex)
      let stopContentWatch: (() => void) | null = null
      stopContentWatch = watch(
        () => props.lines[targetIndex]?.content,
        (content) => {
          if (content == null) return
          if (restored) {
            stopContentWatch?.()
            return
          }
          restored = true
          stopContentWatch?.()
          stop?.()
          nextTick(() => {
            const offset = props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0
            restoreScrollPos(targetIndex, offset)
            // Emit scrolled after the programmatic restore so TOC tracking picks up
            // the correct position even if tocEntries weren't loaded yet during onScroll.
            requestAnimationFrame(() =>
              requestAnimationFrame(() => emit('scrolled', targetIndex, targetIndex)),
            )
            if (props.searchHighlightLineIndex != null && scrollerEl.value) {
              nextTick(() => {
                const mark = scrollerEl.value!.querySelector(
                  'mark.search-match',
                ) as HTMLElement | null
                mark?.scrollIntoView({ block: 'center' })
              })
            }
          })
        },
        { immediate: true, flush: 'post' },
      )
    },
    { flush: 'post', immediate: true },
  )
}

// ── Persist scroll position ───────────────────────────────────────────────────

// Last known good position — updated on every scroll so unmount always has fresh data
// even if the DOM is already detached when onBeforeUnmount fires (WebView2 behaviour).
let lastKnownPos: { scrollIndex: number; scrollOffset: number } | null = null

function savePos() {
  if (programmaticScrolling) return
  const pos = lastKnownPos ?? captureScrollPos()
  if (pos) {
    tabStore.setBookViewState(tabId, bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
      hiddenCommentaryBookIds: props.hiddenCommentaryBookIds
        ? Array.from(props.hiddenCommentaryBookIds)
        : undefined,
      zoom: zoom.value,
      bottomVisible: props.bottomVisible,
      autoSelectTopLine: autoSelectTopLine.value,
    })
    tabStore.setLastReadPos(bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
      hiddenCommentaryBookIds: props.hiddenCommentaryBookIds
        ? Array.from(props.hiddenCommentaryBookIds)
        : undefined,
    })
  }
}

useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') savePos()
})
useEventListener(window, 'beforeunload', savePos)
onBeforeUnmount(() => {
  // Force-clear the programmatic flag so savePos is never silently skipped at unmount.
  programmaticScrolling = false
  if (programmaticScrollTimer) {
    clearTimeout(programmaticScrollTimer)
    programmaticScrollTimer = null
  }
  savePos()
})

function onScroll() {
  if (!scrollerEl.value || programmaticScrolling) return
  const scrollTop = scrollerEl.value.scrollTop
  const items = virtualizer.value.getVirtualItems()
  // For scroll position tracking (TOC, persistence): first item with any part visible
  const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
  const lineIndex = firstVisible?.index ?? 0
  // For auto-select: first fully visible line (top edge at or below scrollTop)
  const firstFull = items.find((v) => v.start >= scrollTop) ?? firstVisible
  const fullLineIndex = firstFull?.index ?? lineIndex
  lastKnownPos = captureScrollPos()
  props.prioritise(lineIndex)
  emit('scrolled', lineIndex, fullLineIndex)
}

// ── Programmatic navigation ───────────────────────────────────────────────────

function setProgrammaticScroll() {
  programmaticScrolling = true
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  programmaticScrollTimer = setTimeout(() => {
    programmaticScrolling = false
  }, 300)
}

function scrollToLineId(lineId: number, fallbackLineIndex?: number) {
  const lineIndex = props.lines.find((l) => l.id === lineId)?.lineIndex ?? fallbackLineIndex
  if (lineIndex == null) return
  props.prioritise(lineIndex)
  const scroller = scrollerEl.value
  const vItem = virtualItems.value.find((v) => v.index === lineIndex)
  if (vItem && scroller) {
    const viewTop = scroller.scrollTop
    const viewBottom = viewTop + scroller.clientHeight
    if (vItem.start >= viewTop && vItem.start + vItem.size <= viewBottom) return
  }
  setProgrammaticScroll()
  virtualizer.value.scrollToIndex(lineIndex, { align: 'start' })
}

function scrollToLineIndex(lineIndex: number) {
  if (!scrollerEl.value) return
  setProgrammaticScroll()
  scrollToIndexWithRetry(
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
    scrollerEl.value,
    lineIndex,
    props.searchBarVisible ? 44 : 0,
  )
}

defineExpose({ scrollToLineId, scrollToLineIndex })

function onLineClick(index: number) {
  const line = props.lines[index]
  if (props.bottomVisible && line && line.id > 0) emit('lineSelected', line.id)
}
</script>

<template>
  <div class="lines-content">
    <ContextMenu ref="contextMenuRef" :items="contextMenuItems" />
    <div
      ref="scrollerEl"
      class="scroller"
      tabindex="0"
      data-ctrlf-enabled
      :style="{ fontSize: `${fontPx}px` }"
      @scroll="onScroll"
      @contextmenu="contextMenuRef?.show($event)"
    >
      <div :style="{ height: `${totalSize}px`, position: 'relative' }">
        <div
          v-for="vItem in virtualItems"
          :key="String(vItem.key)"
          :ref="(el) => el && virtualizer.measureElement(el as Element)"
          :data-index="vItem.index"
          :style="{
            position: 'absolute',
            top: 0,
            right: 0,
            left: 0,
            transform: `translateY(${vItem.start}px)`,
          }"
        >
          <div
            v-if="lines[vItem.index]?.content != null"
            class="line"
            :class="{ selected: props.bottomVisible && selectedLineId === lines[vItem.index]?.id }"
            :data-alt-toc="props.altTocLabelMap?.get(vItem.index)"
            v-html="lineContent(lines[vItem.index]!.content!, vItem.index)"
            @click="onLineClick(vItem.index)"
          />
          <div v-else class="line placeholder" />
        </div>
      </div>
    </div>
  </div>
</template>

<style scoped>
.lines-content {
  height: 100%;
  position: relative;
}
.scroller {
  height: 100%;
  overflow-y: auto;
}
.line {
  padding-inline: 12px;
  font-family: var(--text-font);
  font-size: var(--font-size, 100%);
  line-height: var(--line-height, 1.7);
  color: var(--text-primary);
  text-align: justify;
  position: relative;
}
.line.placeholder {
  height: 28px;
  margin-inline: 12px;
  margin-block: 4px;
  border-radius: 4px;
  background: color-mix(in srgb, var(--text-primary) 5%, transparent);
}
.line::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  right: 4px;
  width: 3px;
  background: var(--accent-color);
  opacity: 0;
  transition: opacity 150ms ease;
}
.line.selected::after {
  opacity: 1;
}
.line[data-alt-toc]::before {
  content: attr(data-alt-toc);
  display: block;
  font-size: 0.85rem;
  font-weight: 600;
  opacity: 0.35;
  padding-block-end: 2px;
}
.line :deep(h1),
.line :deep(h2),
.line :deep(h3),
.line :deep(h4),
.line :deep(h5),
.line :deep(h6) {
  font-family: var(--header-font);
}
.line :deep(mark.search-match) {
  background: rgba(255, 165, 0, 0.4);
  color: inherit;
  border-radius: 2px;
}
.line :deep(mark.search-match.current) {
  background: rgba(255, 165, 0, 0.9);
  color: #000;
}
</style>
