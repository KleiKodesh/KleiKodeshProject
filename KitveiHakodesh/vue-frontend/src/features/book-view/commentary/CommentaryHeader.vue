<script setup lang="ts">
import { computed, ref } from 'vue'
import { onLongPress } from '@vueuse/core'
import {
  IconChevronRight20Regular,
  IconChevronLeft20Regular,
} from '@iconify-prerendered/vue-fluent'
import { hasNativeChromeTabs } from '@/webview-host/bridge'

const props = defineProps<{
  bookId: number
  bookTitle: string
  firstLineIndex?: number
  sectionLabel?: string
  subSectionLabel?: string
  /** Async-resolved TOC path for this commentary book's current line in its own TOC */
  ownTocPath?: string
}>()
const emit = defineEmits<{
  'navigate-section': [direction: 'next' | 'prev', bookId: number]
  'open-book': [bookId: number, lineIndex: number]
}>()

const headerEl = ref<HTMLElement | null>(null)

// bookTitle already arrives cleaned by commentaryGroupBuilder — do not strip again here.
const displayPath = computed(() => {
  return props.ownTocPath ? `${props.bookTitle} ${props.ownTocPath}` : props.bookTitle
})

// The gesture opens the commented book either way; only where tabs exist does it get
// its own tab, so the hint must not promise one in the single-tab hosts.
const tooltipText = computed(() => {
  const hint = hasNativeChromeTabs
    ? 'לחיצה כפולה או Ctrl+לחיצה (או לחיצה ממושכת במסך מגע) לפתיחת הספר בלשונית חדשה'
    : 'לחיצה כפולה או Ctrl+לחיצה (או לחיצה ממושכת במסך מגע) לפתיחת הספר'
  return `${displayPath.value}\n${hint}`
})

function openBook() {
  if (props.firstLineIndex != null) emit('open-book', props.bookId, props.firstLineIndex)
}

onLongPress(headerEl, openBook, { delay: 500 })

function onHeaderClick(e: MouseEvent) {
  if (e.ctrlKey) openBook()
}

function onHeaderDblClick() {
  openBook()
}
</script>

<template>
  <div
    ref="headerEl"
    class="commentary-header"
    :data-book-id="props.bookId"
    :title="tooltipText"
    @click="onHeaderClick"
    @dblclick="onHeaderDblClick"
  >
    <div class="title-block">
      <span class="book-title">{{ displayPath }}</span>
    </div>
    <!-- .stop on click does not cover dblclick — without @dblclick.stop a fast
         next/next click would leak to the header and navigate the tab away. -->
    <div class="header-actions" @click.stop @dblclick.stop>
      <button
        class="action-btn c-pointer hover-bg"
        title="קטע קודם"
        @click.stop="emit('navigate-section', 'prev', props.bookId)"
      >
        <IconChevronRight20Regular />
      </button>
      <button
        class="action-btn c-pointer hover-bg"
        title="קטע הבא"
        @click.stop="emit('navigate-section', 'next', props.bookId)"
      >
        <IconChevronLeft20Regular />
      </button>
    </div>
  </div>
</template>

<style scoped>
.commentary-header {
  display: flex;
  align-items: center;
  gap: 8px;
  /* Same value the lines use (set by CommentaryView's .body) so the header text
     and the commentary text share a start edge; the 6px end padding stays fixed
     for the action buttons. */
  padding-inline: var(--commentary-pad-inline, 12px) 6px;
  height: 36px;
  flex-shrink: 0;
  max-width: var(--commentary-max-width, none);
  margin-inline: auto;
  background: var(--bg-primary);
  cursor: default;
}
.commentary-header:hover {
  background: var(--bg-hover);
  color: var(--accent-color);
  cursor: pointer;
}
.title-block {
  flex: 1;
  display: flex;
  align-items: baseline;
  gap: 6px;
  min-width: 0;
  overflow: hidden;
}
.book-title {
  margin: 0;
  font-family: var(--commentary-header-font);
  font-size: 1em;
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  min-width: 0;
  flex-shrink: 1;
  user-select: text;
}
.header-actions {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}
.commentary-header:hover .header-actions {
  opacity: 1;
}
.action-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  color: var(--text-secondary);
  opacity: 0.2;
}
.commentary-header:hover .action-btn {
  opacity: 1;
  color: var(--text-primary);
}
.action-btn svg {
  width: 14px;
  height: 14px;
}
</style>
