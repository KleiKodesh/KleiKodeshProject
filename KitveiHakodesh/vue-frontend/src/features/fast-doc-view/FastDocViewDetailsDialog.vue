<script setup lang="ts">
/**
 * The open document's details, grouped as Word's File > Info / Properties shows them: General (title, subject, tags,
 * categories, comments, status, author, last modified by, company, manager), Dates, Statistics (the counts the file was
 * saved with) and File (name, location, size). The properties are read from the file's small property part when the
 * dialog opens (fastDocViewProperties); rows without a value are left out. Listed by the shared DetailsDialog.
 */
import { computed, ref, watch } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailsDialog, { type DetailsRow, type DetailsSection } from '@/components/DetailsDialog.vue'
import { fastDocViewApi, type FastDocViewProperties, type FastDocViewSummary } from '@/webview-host/fastDocViewApi'

const props = defineProps<{ filePath: string | null; summary: FastDocViewSummary | null }>()
const emit = defineEmits<{ close: [] }>()
const { t, locale } = useI18n()

const fileName = computed(() => props.filePath?.split(/[\\/]/).pop() ?? '')
const properties = ref<FastDocViewProperties | null>(null)
const loading = ref(false)

watch(
  () => props.summary?.documentId,
  async (documentId, _old, onCleanup) => {
    // A reply for a document that is no longer shown is dropped (the newer read owns the dialog).
    let stale = false
    onCleanup(() => { stale = true })
    properties.value = null
    if (!documentId || !fastDocViewApi.properties) return
    loading.value = true
    try {
      const read = await fastDocViewApi.properties(documentId)
      if (!stale) properties.value = read
    } catch (error) {
      if (!stale) console.warn('[FastDocView] properties', error)
    } finally {
      if (!stale) loading.value = false
    }
  },
  { immediate: true },
)

function formatDate(text: string | null | undefined): string {
  if (!text) return ''
  const date = new Date(text) // "Z" = a moment; no zone = the wall-clock time the file stores (read as local)
  if (Number.isNaN(date.getTime())) return text
  return new Intl.DateTimeFormat(locale.value, { dateStyle: 'medium', timeStyle: 'short' }).format(date)
}

function formatNumber(value: number | null | undefined): string {
  return value == null ? '' : new Intl.NumberFormat(locale.value).format(value)
}

function formatSize(bytes: number | null | undefined): string {
  if (bytes == null) return ''
  const units = ['B', 'KB', 'MB', 'GB']
  let value = bytes
  let unit = 0
  while (value >= 1024 && unit < units.length - 1) {
    value /= 1024
    unit++
  }
  return `${new Intl.NumberFormat(locale.value, { maximumFractionDigits: unit === 0 ? 0 : 1 }).format(value)} ${units[unit]}`
}

function row(key: string, value: string | null | undefined): DetailsRow {
  return { label: t(`fastDocView.detail.${key}`), value: value ?? '' }
}

const sections = computed<DetailsSection[]>(() => {
  const p = properties.value
  const summary = props.summary
  const minutes = p?.totalEditingMinutes
  const groups: DetailsSection[] = [
    {
      title: t('fastDocView.detail.general'),
      rows: [
        row('title', p?.title ?? summary?.title),
        row('subject', p?.subject),
        row('keywords', p?.keywords),
        row('category', p?.category),
        row('comments', p?.comments),
        row('status', p?.contentStatus),
        row('author', p?.author),
        row('lastModifiedBy', p?.lastModifiedBy),
        row('company', p?.company),
        row('manager', p?.manager),
      ],
    },
    {
      title: t('fastDocView.detail.dates'),
      rows: [row('created', formatDate(p?.created)), row('modified', formatDate(p?.modified)), row('lastPrinted', formatDate(p?.lastPrinted))],
    },
    {
      title: t('fastDocView.detail.statistics'),
      rows: [
        row('pages', formatNumber(p?.pages)),
        row('words', formatNumber(p?.words)),
        row('characters', formatNumber(p?.characters)),
        row('charactersWithSpaces', formatNumber(p?.charactersWithSpaces)),
        row('lines', formatNumber(p?.lines)),
        row('paragraphs', formatNumber(p?.paragraphs)),
        row('editingTime', minutes == null ? '' : t('fastDocView.detail.editingMinutes', { n: formatNumber(minutes) })),
        row('revision', p?.revision),
        row('application', p?.application),
        row('template', p?.template),
        row('headings', summary && summary.headingCount > 0 ? formatNumber(summary.headingCount) : ''),
        row('footnotes', summary && summary.footnoteCount > 0 ? formatNumber(summary.footnoteCount) : ''),
        row('endnotes', summary && summary.endnoteCount > 0 ? formatNumber(summary.endnoteCount) : ''),
        row('direction', summary ? t(summary.isRightToLeft ? 'fastDocView.detail.rtl' : 'fastDocView.detail.ltr') : ''),
      ],
    },
    {
      title: t('fastDocView.detail.file'),
      rows: [
        row('fileName', fileName.value),
        { ...row('location', props.filePath), revealPath: props.filePath ?? undefined },
        row('size', formatSize(p?.fileSize)),
        row('fileCreated', formatDate(p?.fileCreated)),
        row('fileModified', formatDate(p?.fileModified)),
      ],
    },
  ]
  return groups.map((group) => ({ ...group, rows: group.rows.filter((r) => r.value !== '') })).filter((group) => group.rows.length > 0)
})
</script>

<template>
  <DetailsDialog :title="t('fastDocView.details')" :sections="sections" :loading="loading && !properties" @close="emit('close')" />
</template>
