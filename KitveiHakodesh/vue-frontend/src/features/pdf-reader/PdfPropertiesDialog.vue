<script setup lang="ts">
import { onMounted, ref } from 'vue'
import type { PDFDocumentProxy } from 'pdfjs-dist'
import { useI18n } from 'vue-i18n'
import DetailsDialog, { type DetailsRow, type DetailsSection } from '@/components/DetailsDialog.vue'

/** Document properties: the PDF's own metadata, listed by the shared DetailsDialog. */
const props = defineProps<{ document: PDFDocumentProxy; fileName: string | null; filePath?: string | null }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()

const sections = ref<DetailsSection[]>([])

function pdfDate(raw: unknown): string {
  const m = typeof raw === 'string' ? /D:(\d{4})(\d{2})?(\d{2})?(\d{2})?(\d{2})?/.exec(raw) : null
  if (!m) return ''
  const d = new Date(+m[1]!, +(m[2] ?? 1) - 1, +(m[3] ?? 1), +(m[4] ?? 0), +(m[5] ?? 0))
  return d.toLocaleString()
}
function size(bytes: number): string {
  return bytes >= 1048576 ? `${(bytes / 1048576).toFixed(1)} MB` : `${Math.max(1, Math.round(bytes / 1024))} KB`
}
/** A row for a property key; a missing value reads "Not available" in the subdued colour. */
function row(key: string, value: string, revealPath?: string): DetailsRow {
  return { label: t(`pdfReader.prop.${key}`), value: value || t('pdfReader.prop.notAvailable'), muted: !value, revealPath: value ? revealPath : undefined }
}

onMounted(async () => {
  const [{ info }, { length }, page1] = await Promise.all([
    props.document.getMetadata(),
    props.document.getDownloadInfo(),
    props.document.getPage(1),
  ])
  const i = info as Record<string, unknown>
  const [w, h] = [page1.view[2]! - page1.view[0]!, page1.view[3]! - page1.view[1]!]
  // Two sections, as PDF.js's own properties dialog: what the document is, then technical details.
  sections.value = [{
    title: t('pdfReader.prop.sectionDescription'),
    rows: [
      row('fileName', props.fileName ?? ''),
      row('fileSize', size(length)),
      row('title', String(i.Title ?? '')),
      row('author', String(i.Author ?? '')),
      row('subject', String(i.Subject ?? '')),
      row('keywords', String(i.Keywords ?? '')),
      row('created', pdfDate(i.CreationDate)),
      row('modified', pdfDate(i.ModDate)),
      row('creator', String(i.Creator ?? '')),
    ],
  }, {
    title: t('pdfReader.prop.sectionAdvanced'),
    rows: [
      row('producer', String(i.Producer ?? '')),
      row('version', String(i.PDFFormatVersion ?? '')),
      row('location', props.filePath ?? '', props.filePath ?? undefined),
      row('pageCount', String(props.document.numPages)),
      row('pageSize', `${Math.round((w / 72) * 25.4)} x ${Math.round((h / 72) * 25.4)} mm`),
    ],
  }]
})
</script>

<template>
  <DetailsDialog :title="t('pdfReader.properties')" :sections="sections" :loading="sections.length === 0" @close="emit('close')" />
</template>
