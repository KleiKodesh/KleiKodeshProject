<script setup lang="ts">
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import type { PdfComment } from './usePdfComments'

/** The document's comments grouped by page, with author and date when the file has them; click jumps there. */
const props = defineProps<{
  comments: PdfComment[]
  loading: boolean
  /** A page's label for display (page labels when the document has them). */
  labelOf?: (n: number) => string
}>()
const emit = defineEmits<{ select: [comment: PdfComment] }>()
const { locale } = useI18n()

const groups = computed(() => {
  const out: { pageIndex: number; items: PdfComment[] }[] = []
  for (const c of props.comments) {
    const last = out[out.length - 1]
    if (last && last.pageIndex === c.pageIndex) last.items.push(c)
    else out.push({ pageIndex: c.pageIndex, items: [c] })
  }
  return out
})
const dateFormat = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'short', timeStyle: 'short' }))
</script>

<template>
  <div class="pdf-comments">
    <div v-if="groups.length === 0" class="empty">{{ loading ? $t('pdfReader.panel.loading') : $t('pdfReader.panel.noComments') }}</div>
    <section v-for="group in groups" :key="group.pageIndex" class="group">
      <div class="page-head">{{ $t('pdfReader.panel.page', { n: labelOf?.(group.pageIndex + 1) ?? group.pageIndex + 1 }) }}</div>
      <button v-for="c in group.items" :key="c.key" type="button" class="comment" @click="emit('select', c)">
        <span v-if="c.author || c.date" class="meta">
          <span v-if="c.author" class="author">{{ c.author }}</span>
          <span v-if="c.date" class="date">{{ dateFormat.format(c.date) }}</span>
        </span>
        <span class="text" dir="auto">{{ c.text }}</span>
      </button>
    </section>
    <div v-if="loading && groups.length" class="empty">{{ $t('pdfReader.panel.loading') }}</div>
  </div>
</template>

<style scoped>
.pdf-comments {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.empty {
  padding: 10px;
  font-size: 12px;
  color: var(--text-secondary);
}
.page-head {
  position: sticky;
  top: 0;
  padding: 4px 10px;
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  background: var(--bg-toolbar);
}
.comment {
  display: flex;
  flex-direction: column;
  align-items: stretch;
  gap: 2px;
  width: 100%;
  height: auto;
  padding: 5px 10px;
  text-align: start;
  border-radius: 0;
  background: none;
}
.comment:hover {
  background: var(--hover-bg);
}
.meta {
  display: flex;
  gap: 6px;
  font-size: 10.5px;
  color: var(--text-secondary);
  overflow: hidden;
}
.author {
  font-weight: 600;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.date {
  white-space: nowrap;
}
.text {
  font-size: 12px;
  line-height: 1.35;
  color: var(--text-primary);
  white-space: pre-wrap;
  overflow-wrap: anywhere;
  display: -webkit-box;
  -webkit-line-clamp: 4;
  -webkit-box-orient: vertical;
  overflow: hidden;
}
</style>
