<script setup lang="ts">
import { ref, computed, watch } from 'vue'
import { IconDismiss20Regular, IconArrowStepOver20Regular } from '@iconify-prerendered/vue-fluent'
import CommentaryHeaderNav from './CommentaryHeaderNav.vue'
import type { CommentaryGroup } from './useCommentary'

const props = defineProps<{ bookTitle: string; connectionTypes: string[]; groups: CommentaryGroup[]; scrollToGroup: (bookId: number) => void; isSticky?: boolean; showNav?: boolean }>()
const emit = defineEmits<{ close: []; 'navigate-section': [direction: 'next' | 'prev', bookId: number]; 'update:showNav': [val: boolean]; 'open-book': [bookId: number, lineIndex: number] }>()

const CT_LABELS: Record<string, string> = { SOURCE: 'מקור', OTHER: 'אחר', COMMENTARY: 'מפרשים', TARGUM: 'תרגום', REFERENCE: 'הפניה' }

const localShowNav = ref(false)
const showNav = computed({
  get: () => props.isSticky ? (props.showNav ?? false) : localShowNav.value,
  set: (val) => props.isSticky ? emit('update:showNav', val) : (localShowNav.value = val),
})

const localActiveBookId = ref(props.groups.find(g => g.bookTitle === props.bookTitle)?.bookId ?? 0)
const activeBookId = computed(() => localActiveBookId.value || (props.groups.find(g => g.bookTitle === props.bookTitle)?.bookId ?? 0))

// keep localActiveBookId in sync when the sticky header switches group due to scrolling
watch(() => props.bookTitle, (title) => {
  const id = props.groups.find(g => g.bookTitle === title)?.bookId
  if (id) localActiveBookId.value = id
})

function onHeaderClick(e: MouseEvent) {
  if (e.ctrlKey || e.metaKey) {
    const group = props.groups.find(g => g.bookTitle === props.bookTitle)
    if (group && group.lines[0] != null) emit('open-book', group.bookId, group.lines[0].lineIndex)
    return
  }
  if (props.isSticky) showNav.value = true
  else localShowNav.value = !localShowNav.value
}
</script>

<template>
  <div class="commentary-header" :class="{ 'is-sticky': isSticky }" title="לחץ לניווט מפרשים • Ctrl+לחץ לפתיחת הספר" @click="onHeaderClick($event)">
    <template v-if="!showNav">
      <h5 class="book-title">{{ connectionTypes[0] ? `${CT_LABELS[connectionTypes[0]] ?? connectionTypes[0]} > ${bookTitle}` : bookTitle }}</h5>
      <div class="header-actions" @click.stop>
        <button class="action-btn nav-btn c-pointer hover-bg" title="ניווט מפרשים" @click.stop="showNav = true"><IconArrowStepOver20Regular /></button>
        <!-- TODO: decide whether to remove close button -->
        <!-- <button v-if="isSticky" class="action-btn c-pointer hover-bg" title="סגור חלונית מפרשים" @click.stop="emit('close')"><IconDismiss20Regular /></button> -->
      </div>
    </template>
    <CommentaryHeaderNav v-if="showNav" :groups="groups" :scroll-to-group="scrollToGroup" :book-title="bookTitle" :active-book-id="activeBookId" @input-blur="showNav = false" @navigate-section="(d, id) => emit('navigate-section', d, id)" @close="emit('close')" @open-book="(bookId, lineIndex) => emit('open-book', bookId, lineIndex)" @update:active-book-id="localActiveBookId = $event" />
  </div>
</template>

<style scoped>
.commentary-header { display: flex; align-items: center; gap: 8px; padding-inline: 14px 6px; height: 32px; flex-shrink: 0; position: sticky; top: 0; z-index: 1; cursor: pointer; container-type: inline-size; background: var(--bg-primary); }
.book-title { flex: 1; margin: 0; font-size: 13px; white-space: nowrap; overflow: hidden; text-overflow: ellipsis; min-width: 0; user-select: text; }
.book-title:hover { color: var(--accent-color); }
.badge-wrapper { position: relative; flex-shrink: 0; }
.header-actions { display: flex; align-items: center; gap: 2px; flex-shrink: 0; }
.action-btn { display: flex; align-items: center; justify-content: center; width: 24px; height: 24px; border-radius: 4px; color: var(--text-secondary); }
.action-btn svg { width: 14px; height: 14px; }
/* nav button hidden by default on inline headers, visible on hover; always visible on sticky */
.nav-btn { opacity: 0; }
.commentary-header:hover .nav-btn { opacity: 1; }
.commentary-header.is-sticky .nav-btn { opacity: 1; }
</style>
