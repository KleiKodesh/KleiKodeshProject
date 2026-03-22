<script setup lang="ts">
import { computed } from 'vue'
import { IconChevronRight20Regular, IconChevronLeft20Regular } from '@iconify-prerendered/vue-fluent'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{ bookTitle: string; sectionLabel?: string; groups: CommentaryGroup[] }>()
const emit = defineEmits<{ 'navigate-section': [direction: 'next' | 'prev', bookId: number]; 'open-book': [bookId: number, lineIndex: number] }>()

const bookId = computed(() => props.groups.find(g => g.bookTitle === props.bookTitle)?.bookId ?? 0)

function onHeaderClick(e: MouseEvent) {
  if (!(e.ctrlKey || e.metaKey)) return
  const group = props.groups.find(g => g.bookTitle === props.bookTitle)
  if (group?.lines[0] != null) emit('open-book', group.bookId, group.lines[0].lineIndex)
}
</script>

<template>
  <div class="commentary-header" title="Ctrl+לחץ לפתיחת הספר" @click="onHeaderClick($event)">
    <div class="title-block">
      <span v-if="sectionLabel" class="section-label">{{ sectionLabel }}</span>
      <h5 class="book-title">{{ bookTitle }}</h5>
    </div>
    <div class="header-actions" @click.stop>
      <button class="action-btn c-pointer hover-bg" title="קטע קודם" @click.stop="emit('navigate-section', 'prev', bookId)"><IconChevronRight20Regular /></button>
      <button class="action-btn c-pointer hover-bg" title="קטע הבא" @click.stop="emit('navigate-section', 'next', bookId)"><IconChevronLeft20Regular /></button>
    </div>
  </div>
</template>

<style scoped>
.commentary-header { display: flex; align-items: center; gap: 8px; padding-inline: 14px 6px; height: 36px; flex-shrink: 0; background: var(--bg-primary); cursor: default; }
.title-block { flex: 1; display: flex; align-items: baseline; gap: 5px; min-width: 0; overflow: hidden; }
.section-label { flex-shrink: 0; font-size: 11px; color: var(--text-secondary); white-space: nowrap; }
.book-title { flex-shrink: 1; margin: 0; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; user-select: text; }
.header-actions { display: flex; align-items: center; gap: 2px; flex-shrink: 0; opacity: 0; }
.commentary-header:hover .header-actions { opacity: 1; }
.action-btn { display: flex; align-items: center; justify-content: center; width: 24px; height: 24px; border-radius: 4px; color: var(--text-secondary); }
.action-btn svg { width: 14px; height: 14px; }
</style>
