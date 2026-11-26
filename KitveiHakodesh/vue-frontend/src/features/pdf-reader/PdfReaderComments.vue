<script setup lang="ts">
import { computed } from 'vue'
import { WindowedScroller } from 'vue-windowed-scroller'
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

/** One WindowedScroller row each: a page's heading before its first comment, then the comments. */
type Row = { kind: 'page'; pageIndex: number } | { kind: 'comment'; comment: PdfComment }
const rows = computed(() => {
  const out: Row[] = []
  let page = -1
  for (const c of props.comments) {
    if (c.pageIndex !== page) out.push({ kind: 'page', pageIndex: (page = c.pageIndex) })
    out.push({ kind: 'comment', comment: c })
  }
  return out
})
// As PDF.js's comment list and popup: a long date, in the app's language.
const dateFormat = computed(() => new Intl.DateTimeFormat(locale.value, { dateStyle: 'long' }))
</script>

<template>
  <div class="pdf-comments">
    <div v-if="rows.length === 0" class="empty">{{ loading ? $t('pdfReader.panel.loading') : $t('pdfReader.panel.noComments') }}</div>
    <WindowedScroller v-else class="comment-list" thin :count="rows.length" :row-source-version="rows">
      <template #row="{ index }">
        <template v-for="row in rows.slice(index, index + 1)" :key="index">
          <div v-if="row.kind === 'page'" class="page-head">{{ $t('pdfReader.panel.page', { n: labelOf?.(row.pageIndex + 1) ?? row.pageIndex + 1 }) }}</div>
          <button v-else type="button" class="comment" @click="emit('select', row.comment)">
            <span v-if="row.comment.author || row.comment.date" class="meta">
              <span v-if="row.comment.author" class="author">{{ row.comment.author }}</span>
              <span v-if="row.comment.date" class="date">{{ dateFormat.format(row.comment.date) }}</span>
            </span>
            <span class="text" dir="auto">{{ row.comment.text }}</span>
          </button>
        </template>
      </template>
    </WindowedScroller>
    <div v-if="loading && rows.length" class="empty">{{ $t('pdfReader.panel.loading') }}</div>
  </div>
</template>

<style scoped>
.pdf-comments {
  display: flex;
  flex-direction: column;
  flex: 1;
  min-height: 0;
}
.pdf-comments > .windowed-scroller {
  flex: 1;
  min-height: 0;
}
.empty {
  padding: 10px;
  font-size: 12px;
  color: var(--text-secondary);
}
.page-head {
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
