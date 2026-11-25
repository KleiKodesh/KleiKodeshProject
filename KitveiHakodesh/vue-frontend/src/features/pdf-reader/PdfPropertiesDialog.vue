<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'

/** Document properties: the PDF's own metadata, in a modal dialog in the window it was opened from. */
const props = defineProps<{ document: PDFDocumentProxy; fileName: string | null }>()
const emit = defineEmits<{ close: [] }>()
const hostWindow = useHostWindow()

const rows = ref<{ key: string; value: string }[]>([])

function pdfDate(raw: unknown): string {
  const m = typeof raw === 'string' ? /D:(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?/.exec(raw) : null
  if (!m) return ''
  const d = new Date(+m[1]!, +(m[2] ?? 1) - 1, +(m[3] ?? 1), +(m[4] ?? 0), +(m[5] ?? 0))
  return d.toLocaleString()
}
function size(bytes: number): string {
  return bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
}

onMounted(async () => {
  const [{ info }, { length }, page1] = await Promise.all([
    props.document.getMetadata(),
    props.document.getDownloadInfo(),
    props.document.getPage(1),
  ])
  const i = info as Record<string, unknown>
  const [w, h] = [page1.view[2]! - page1.view[0]!, page1.view[3]! - page1.view[1]!]
  rows.value = [
    { key: 'fileName', value: props.fileName ?? '' },
    { key: 'fileSize', value: size(length) },
    { key: 'title', value: String(i.Title ?? '') },
    { key: 'author', value: String(i.Author ?? '') },
    { key: 'subject', value: String(i.Subject ?? '') },
    { key: 'keywords', value: String(i.Keywords ?? '') },
    { key: 'created', value: pdfDate(i.CreationDate) },
    { key: 'modified', value: pdfDate(i.ModDate) },
    { key: 'creator', value: String(i.Creator ?? '') },
    { key: 'producer', value: String(i.Producer ?? '') },
    { key: 'version', value: String(i.PDFFormatVersion ?? '') },
    { key: 'pageCount', value: String(props.document.numPages) },
    { key: 'pageSize', value: `${Math.round((w / 72) * 25.4)} x ${Math.round((h / 72) * 25.4)} mm` },
  ]
})

useEventListener(hostWindow, 'keydown', (e: KeyboardEvent) => {
  if (e.code === 'Escape' || e.code === 'Enter') {
    e.preventDefault()
    emit('close')
  }
})
</script>

<template>
  <Teleport :to="hostWindow.document.body">
    <div class="props-backdrop" @click.self="emit('close')">
      <div class="props-dialog" role="dialog" aria-modal="true" :aria-label="$t('pdfReader.properties')">
        <h3 class="props-title">{{ $t('pdfReader.properties') }}</h3>
        <dl class="props-list">
          <template v-for="row in rows" :key="row.key">
            <dt>{{ $t(`pdfReader.prop.${row.key}`) }}</dt>
            <dd>{{ row.value || '-' }}</dd>
          </template>
        </dl>
        <div class="props-actions">
          <button class="props-ok-btn" autofocus @click="emit('close')">{{ $t('components.ok') }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* The app's dialog look (components/AlertDialog.vue). */
.props-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 10000;
}
.props-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 420px;
  max-height: 80vh;
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.props-title {
  margin: 0;
  font-size: 14px;
  color: var(--text-primary);
}
.props-list {
  margin: 0;
  display: grid;
  grid-template-columns: auto 1fr;
  gap: 6px 16px;
  font-size: 12px;
  color: var(--text-primary);
  overflow: auto;
  scrollbar-width: thin;
}
.props-list dt {
  opacity: 0.7;
  white-space: nowrap;
}
.props-list dd {
  margin: 0;
  overflow-wrap: anywhere;
}
.props-actions {
  display: flex;
  justify-content: flex-end;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}
.props-ok-btn {
  height: 30px;
  padding: 0 20px;
  font-size: 12px;
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
  border-radius: 4px;
}
.props-ok-btn:hover {
  background: var(--control-bg-hover);
}
</style>
