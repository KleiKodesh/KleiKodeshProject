<script setup lang="ts">
import { onMounted, ref } from 'vue'
import { useEventListener } from '@vueuse/core'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { useHostWindow } from '@/composables/detachedWindow/hostWindow'
import { IconFolderOpen20Regular } from '@iconify-prerendered/vue-fluent'
import { revealFileInFolder } from '@/webview-host/bridge'

/** Document properties: the PDF's own metadata, in a modal dialog in the window it was opened from. */
const props = defineProps<{ document: PDFDocumentProxy; fileName: string | null; filePath?: string | null }>()
const emit = defineEmits<{ close: [] }>()
const hostWindow = useHostWindow()

type Row = { key: string; value: string }
// Two sections, as PDF.js's own properties dialog: what the document is, then technical details.
const sections = ref<{ key: 'sectionDescription' | 'sectionAdvanced'; rows: Row[] }[]>([])

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
  sections.value = [{ key: 'sectionDescription', rows: [
    { key: 'fileName', value: props.fileName ?? '' },
    { key: 'fileSize', value: size(length) },
    { key: 'title', value: String(i.Title ?? '') },
    { key: 'author', value: String(i.Author ?? '') },
    { key: 'subject', value: String(i.Subject ?? '') },
    { key: 'keywords', value: String(i.Keywords ?? '') },
    { key: 'created', value: pdfDate(i.CreationDate) },
    { key: 'modified', value: pdfDate(i.ModDate) },
    { key: 'creator', value: String(i.Creator ?? '') },
  ] }, { key: 'sectionAdvanced', rows: [
    { key: 'producer', value: String(i.Producer ?? '') },
    { key: 'version', value: String(i.PDFFormatVersion ?? '') },
    { key: 'location', value: props.filePath ?? '' },
    { key: 'pageCount', value: String(props.document.numPages) },
    { key: 'pageSize', value: `${Math.round((w / 72) * 25.4)} x ${Math.round((h / 72) * 25.4)} mm` },
  ] }]
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
        <section v-for="(section, n) in sections" :key="section.key" class="props-section" :class="{ 'props-section-next': n > 0 }">
          <h4 class="props-section-title">{{ $t(`pdfReader.prop.${section.key}`) }}</h4>
          <dl class="props-list">
            <template v-for="row in section.rows" :key="row.key">
              <dt>{{ $t(`pdfReader.prop.${row.key}`) }}</dt>
              <!-- one line each; the full value in the tooltip, and a selection copies all of it -->
              <dd :class="{ 'props-location': row.key === 'location', 'props-missing': !row.value }" :title="row.value || undefined">
                <span class="props-value">{{ row.value || $t('pdfReader.prop.notAvailable') }}</span>
                <!-- the file's exact place on disk, plus "open file location" -->
                <button
                  v-if="row.key === 'location' && row.value"
                  class="props-reveal-btn"
                  :title="$t('pdfReader.prop.openLocation')"
                  @click="revealFileInFolder(row.value)"
                >
                  <IconFolderOpen20Regular />
                </button>
              </dd>
            </template>
          </dl>
        </section>
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
  padding: 12px 14px 10px;
  width: 420px;
  max-height: 80vh;
  overflow: auto;
  scrollbar-width: thin;
  display: flex;
  flex-direction: column;
  gap: 8px;
}
/* Everything in it can be selected and copied: the app sets user-select: none on every
   element (main.css), so each descendant needs it back, not only the value cells. */
.props-dialog,
.props-dialog :is(h3, h4, section, dl, dt, dd, span) {
  user-select: text;
}
.props-title {
  margin: 0;
  font-size: 14px;
  color: var(--text-primary);
}
.props-section-next {
  margin-top: 4px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}
.props-section-title {
  margin: 0 0 6px;
  font-size: 13px;
  font-weight: 600;
  color: var(--text-primary);
}
.props-list {
  margin: 0;
  display: grid;
  /* a fixed label column, so every value starts on one line */
  grid-template-columns: 120px minmax(0, 1fr);
  gap: 5px 12px;
  font-size: 12px;
  color: var(--text-primary);
}
.props-list dt {
  opacity: 0.7;
  white-space: nowrap;
}
.props-list dd {
  margin: 0;
  min-width: 0;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  /* every value can be selected and copied (the dialog sits inside the app, which may disable selection) */
  cursor: text;
}
.props-missing {
  color: var(--text-secondary);
}
.props-location {
  display: flex;
  align-items: center;
}
.props-location .props-value {
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  /* inside itself a path reads left to right */
  direction: ltr;
  unicode-bidi: isolate;
}
.props-reveal-btn {
  display: inline-flex;
  flex-shrink: 0;
  margin-inline-start: 6px;
  align-items: center;
  justify-content: center;
  width: 22px;
  height: 22px;
  padding: 0;
  border-radius: 4px;
  cursor: pointer;
}
.props-reveal-btn svg {
  width: 16px;
  height: 16px;
}
.props-actions {
  display: flex;
  justify-content: flex-end;
  padding-top: 4px;
}
.props-ok-btn {
  height: 26px;
  padding: 0 16px;
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
