<script setup lang="ts">
/**
 * The open document's details: its file name and place, the title it declares, text direction
 * and how many headings and notes it has - whatever the viewer knows without reading the whole
 * file. Rows without a value are left out. Listed by the shared DetailsDialog.
 */
import { computed } from 'vue'
import { useI18n } from 'vue-i18n'
import DetailsDialog, { type DetailsRow } from '@/components/DetailsDialog.vue'
import type { FastDocViewSummary } from '@/webview-host/fastDocViewApi'

const props = defineProps<{ filePath: string | null; summary: FastDocViewSummary | null }>()
const emit = defineEmits<{ close: [] }>()
const { t } = useI18n()

const fileName = computed(() => props.filePath?.split(/[\\/]/).pop() ?? '')

const rows = computed<DetailsRow[]>(() => {
  const summary = props.summary
  const candidates: DetailsRow[] = [
    { label: t('fastDocView.detail.fileName'), value: fileName.value },
    { label: t('fastDocView.detail.location'), value: props.filePath ?? '', revealPath: props.filePath ?? undefined },
    { label: t('fastDocView.detail.title'), value: summary?.title ?? '' },
    summary ? { label: t('fastDocView.detail.direction'), value: t(summary.isRightToLeft ? 'fastDocView.detail.rtl' : 'fastDocView.detail.ltr') } : { label: '', value: '' },
    { label: t('fastDocView.detail.headings'), value: summary && summary.headingCount > 0 ? String(summary.headingCount) : '' },
    { label: t('fastDocView.detail.footnotes'), value: summary && summary.footnoteCount > 0 ? String(summary.footnoteCount) : '' },
    { label: t('fastDocView.detail.endnotes'), value: summary && summary.endnoteCount > 0 ? String(summary.endnoteCount) : '' },
  ]
  return candidates.filter((row) => row.value !== '')
})
</script>

<template>
  <DetailsDialog :title="t('fastDocView.details')" :sections="[{ rows }]" @close="emit('close')" />
</template>
