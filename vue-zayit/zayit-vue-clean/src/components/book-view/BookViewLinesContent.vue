<script setup lang="ts">
import { computed, ref, watch, nextTick, onBeforeUnmount } from 'vue'
import { storeToRefs } from 'pinia'
import { useVirtualizer } from '@tanstack/vue-virtual'
import { useTabStore } from '@/stores/tabStore'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import type { LineItem } from './useLines'
import { applyDiacriticsFilter, removeDiacriticsForSearch } from '@/utils/hebrewTextProcessing'
import { censorDivineNames } from '@/utils/censorDivineNames'
import ContextMenu from '@/components/common/ContextMenu.vue'
import type { ContextMenuItem } from '@/components/common/ContextMenu.vue'
import { useEventListener } from '@vueuse/core'
import { useScopedKeys } from '@/composables/useScopedKeys'
import { useScopedCopy } from '@/composables/useScopedCopy'
import { scrollToIndexWithRetry } from '@/utils/scrollToIndexWithRetry'
import { useVirtualScrollerKeys } from '@/composables/useVirtualScrollerKeys'

const emit = defineEmits<{ scrolled: [number]; lineSelected: [number]; 'ctrl-f': [] }>()
const props = defineProps<{
  altTocLabelMap?: Map<number, string>
  selectedLineId?: number | null
  bottomVisible?: boolean
  commentaryScrollIndex?: number | null
  commentaryScrollOffset?: number | null
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
}>()

const tabStore = useTabStore()
const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { zoom } = storeToRefs(bookViewStore)
const tabId = tabStore.activeTabId
const bookId = tabStore.activeTab.bookId!
const { lines, prioritise } = useLines(() => bookId)

const diacriticsState = computed(() => settingsStore.diacriticsState)
const fontPx = computed(() => (zoom.value / 100) * (settingsStore.fontSize / 100) * 15)

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

function lineContent(raw: string, lineIndex: number): string {
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
  if (props.searchHighlightQuery?.trim() && lineIndex === props.searchHighlightLineIndex)
    content = highlightMatches(raw, content, props.searchHighlightQuery, false, -1)
  return content
}

const scrollerEl = ref<HTMLElement | null>(null)

const { isSelectAll, selectAllInContainer } = useScopedKeys(scrollerEl, {
  onCtrlF: () => emit('ctrl-f'),
})
useScopedCopy(scrollerEl, () => lines.value.map((l) => l.content).filter(Boolean), isSelectAll)
useVirtualScrollerKeys(
  scrollerEl,
  () =>
    virtualizer.value as unknown as import('@tanstack/vue-virtual').Virtualizer<Element, Element>,
  () => lines.value.length,
)

const contextMenuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const contextMenuItems: ContextMenuItem[] = [
  { label: 'העתק', action: () => document.execCommand('copy') },
  {
    label: 'העתק כבלוק',
    action: () => {
      let joined: string
      if (isSelectAll.value) {
        joined = lines.value
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
    count: lines.value.length,
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
  // scrollOffset = scrollTop - first.start: how far past the first rendered item we are.
  // first may be an overscan item above the viewport — that's fine, the offset compensates.
  return {
    scrollIndex: first.index,
    scrollOffset: Math.max(0, scrollerEl.value.scrollTop - first.start),
  }
}

// ── Scroll restore ────────────────────────────────────────────────────────────

let programmaticScrollTimer: ReturnType<typeof setTimeout> | null = null
let programmaticScrolling = false

function restoreScrollPos(lineIndex: number, scrollOffset = 0) {
  // scrollToIndex() uses estimated sizes and triggers TanStack's internal scroll correction.
  // Wait one rAF for that correction to settle, then set scrollTop directly using the
  // real measured item.start from measurementsCache — TanStack is idle by then.
  // programmaticScrolling suppresses savePos during restore.
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

watch(
  lines,
  (val) => {
    if (!val.length) return
    const targetIndex = props.initialLineIndex ?? props.initialScrollIndex
    if (targetIndex == null) return
    // Prioritise the target chunk so it loads before others.
    prioritise(targetIndex)
    // Wait until the target line's content is loaded (streaming: starts as null).
    const stopWatch = watch(
      () => lines.value[targetIndex]?.content,
      (content) => {
        if (content == null) return
        stopWatch()
        restoreScrollPos(
          targetIndex,
          props.initialScrollIndex != null ? (props.initialScrollOffset ?? 0) : 0,
        )
        if (props.searchHighlightLineIndex != null && scrollerEl.value) {
          nextTick(() => {
            const mark = scrollerEl.value!.querySelector('mark.search-match') as HTMLElement | null
            mark?.scrollIntoView({ block: 'center' })
          })
        }
      },
      { immediate: true, flush: 'post' },
    )
  },
  { flush: 'post', once: true },
)

// ── Persist scroll position ───────────────────────────────────────────────────

function savePos() {
  if (programmaticScrolling) return
  const pos = captureScrollPos()
  if (pos) {
    tabStore.setBookViewState(tabId, bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
      zoom: zoom.value,
    })
    tabStore.setLastReadPos(bookId, {
      ...pos,
      selectedLineId: props.selectedLineId,
      commentaryScrollIndex: props.commentaryScrollIndex,
      commentaryScrollOffset: props.commentaryScrollOffset,
    })
  }
}

// Save when the app goes to background (tab switch, WebView losing focus) or on page unload.
// Not saved on every scroll event — only on these lifecycle boundaries.
useEventListener(document, 'visibilitychange', () => {
  if (document.visibilityState === 'hidden') savePos()
})
useEventListener(window, 'beforeunload', savePos)

// Save on unmount — covers in-app tab switching where visibility never changes.
onBeforeUnmount(savePos)

// ── Scroll event ──────────────────────────────────────────────────────────────

function onScroll() {
  if (scrollerEl.value && !programmaticScrolling) {
    // Use the same method as captureScrollPos: find the first item whose bottom edge
    // is at or below scrollTop — i.e. the first line actually visible in the viewport,
    // not the first overscan-rendered item which may be above the fold.
    const scrollTop = scrollerEl.value.scrollTop
    const items = virtualizer.value.getVirtualItems()
    const firstVisible = items.find((v) => v.start + v.size > scrollTop) ?? items[0]
    const lineIndex = firstVisible?.index ?? 0
    prioritise(lineIndex)
    emit('scrolled', lineIndex)
  }
}

// ── Programmatic navigation ───────────────────────────────────────────────────

function setProgrammaticScroll() {
  programmaticScrolling = true
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
  programmaticScrollTimer = setTimeout(() => {
    programmaticScrolling = false
  }, 300)
}

// Scrolls to a line by id — used for TOC and commentary navigation.
// Skips scrolling if the line is already fully visible to avoid jarring jumps
// when the in-app search bar navigates between results on the same screen.
function scrollToLineId(lineId: number) {
  const lineIndex = lines.value.find((l) => l.id === lineId)?.lineIndex
  if (lineIndex == null) return
  prioritise(lineIndex)
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
    -52,
  )
}

onBeforeUnmount(() => {
  if (programmaticScrollTimer) clearTimeout(programmaticScrollTimer)
})

defineExpose({ scrollToLineId, scrollToLineIndex })

function onLineClick(index: number) {
  const line = lines.value[index]
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
.line.selected::after {
  content: '';
  position: absolute;
  top: 0;
  bottom: 0;
  right: 4px;
  width: 3px;
  background: var(--accent-color);
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
