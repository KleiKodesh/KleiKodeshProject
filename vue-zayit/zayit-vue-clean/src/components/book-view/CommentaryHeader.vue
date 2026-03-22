<script setup lang="ts">
import { computed } from 'vue'
import { IconChevronRight20Regular, IconChevronLeft20Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{ bookTitle: string; sectionLabel?: string; groups: CommentaryGroup[] }>()
const emit = defineEmits<{ 'navigate-section': [direction: 'next' | 'prev', bookId: number]; 'open-book': [bookId: number, lineIndex: number] }>()

const bookId = computed(() => props.groups.find(g => g.bookTitle === props.bookTitle)?.bookId ?? 0)

function onHeaderClick() {
  const group = props.groups.find(g => g.bookTitle === props.bookTitle)
  if (group?.lines[0] != null) emit('open-book', group.bookId, group.lines[0].lineIndex)
}
</script>

<template>
  <div class="commentary-header" title="לחץ לפתיחת הספר" @click="onHeaderClick()">
    <div class="title-block">
      <span class="book-title">{{ sectionLabel ? `${sectionLabel} > ${bookTitle}` : bookTitle }}</span>
    </div>
    <div class="header-actions" @click.stop>
      <button class="action-btn c-pointer hover-bg" title="קטע קודם" @click.stop="emit('navigate-section', 'prev', bookId)"><IconChevronRight20Regular /></button>
      <button class="action-btn c-pointer hover-bg" title="קטע הבא" @click.stop="emit('navigate-section', 'next', bookId)"><IconChevronLeft20Regular /></button>
    </div>
  </div>
</template>

<style scoped>
.commentary-header { display: flex; align-items: center; gap: 8px; padding-inline: 14px 6px; height: 36px; flex-shrink: 0; background: var(--bg-primary); cursor: default; }
.commentary-header:hover { background: var(--bg-hover); color: var(--accent-color); cursor: pointer; }
.title-block { flex: 1; display: flex; align-items: center; min-width: 0; overflow: hidden; }
.book-title { margin: 0; font-size: 13px; font-weight: 600; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; user-select: text; }
.header-actions { display: flex; align-items: center; gap: 2px; flex-shrink: 0; }
.commentary-header:hover .header-actions { opacity: 1; }
.action-btn { display: flex; align-items: center; justify-content: center; width: 24px; height: 24px; border-radius: 4px; color: var(--text-secondary); opacity: 0.4; }
.commentary-header:hover .action-btn { opacity: 1; color: var(--text-primary); }
.action-btn svg { width: 14px; height: 14px; }
</style>
