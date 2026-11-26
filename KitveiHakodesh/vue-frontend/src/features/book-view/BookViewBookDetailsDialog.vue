<script setup lang="ts">
/**
 * The book's details: what the library records ABOUT it (authors, category, publication place
 * and date, licence, source...). Reads them and hands them, as labelled rows, to the shared
 * DetailsDialog. Mounted only once someone opens it, so most readers never pay for the query.
 * Only rows that have a value are listed.
 */
import { ref, computed, watch } from 'vue'
import { getBookMetadata } from '@/webview-host/seforimApi'
import { getDbPathInfo } from '@/webview-host/bridge'
import type { BookMetadataRow } from '@/webview-host/queries.types'
import DetailsDialog, { type DetailsRow } from '@/components/DetailsDialog.vue'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps<{ bookId: number | undefined }>()
const emit = defineEmits<{ close: [] }>()

// -- Data --

const metadata = ref<BookMetadataRow | null>(null)
const isLoading = ref(false)
const loadFailed = ref(false)

async function loadMetadata() {
  const bookId = props.bookId
  if (bookId == null) return
  isLoading.value = true
  loadFailed.value = false
  try {
    const result = await getBookMetadata(bookId)
    // The book changed while this was in flight - its answer belongs to no one now.
    if (props.bookId !== bookId) return
    metadata.value = result ?? null
  } catch {
    if (props.bookId === bookId) loadFailed.value = true
  } finally {
    if (props.bookId === bookId) isLoading.value = false
  }
}

watch(
  () => props.bookId,
  () => {
    metadata.value = null
    void loadMetadata()
  },
  { immediate: true },
)

/** On Zayit's DB (under its `io.github.kdroidfilter.seforimapp` folder) the description row is hidden. */
const isZayitDb = ref(false)
void getDbPathInfo().then((info) => {
  isZayitDb.value = /seforimapp/i.test(info?.path ?? '')
})

// -- Rows --

/** Placeholder values the library uses for "not recorded" - showing them says nothing. */
const UNKNOWN_VALUES = new Set(['unknown'])

function meaningfulValues(values: (string | null | undefined)[]): string[] {
  return values
    .map((value) => value?.trim() ?? '')
    .filter((value) => value !== '' && !UNKNOWN_VALUES.has(value.toLowerCase()))
}

/** Percent-encoded URLs (Wikisource's Hebrew paths) are unreadable; show them decoded. */
function decodeUrlForDisplay(value: string): string {
  try {
    return decodeURI(value)
  } catch {
    return value
  }
}

/** Only web addresses become links; anything else in the column stays plain text. */
function isWebUrl(value: string): boolean {
  return /^https?:\/\//i.test(value)
}

const rows = computed<DetailsRow[]>(() => {
  const book = metadata.value
  if (!book) return []
  // The third element marks a row whose values are URLs: links, decoded, one per line.
  const candidates: [string, (string | null | undefined)[], boolean?][] = [
    [t('bookView.bookInfo.name'), [book.title]],
    // heRef is often identical to the title; repeating it adds nothing.
    [t('bookView.bookInfo.alias'), [book.heRef !== book.title ? book.heRef : null]],
    [t('bookView.bookInfo.collectiveTitle'), [book.collectiveTitle]],
    [t('bookView.bookInfo.author'), book.authors],
    [t('bookView.bookInfo.category'), [book.categoryTitle]],
    [t('bookView.bookInfo.printPlace'), book.publicationPlaces],
    [t('bookView.bookInfo.topics'), book.topics],
    [t('bookView.bookInfo.description'), isZayitDb.value ? [] : [book.description ?? book.shortDescription]],
    [t('bookView.bookInfo.copyright'), book.licenses],
    [t('bookView.bookInfo.editionSource'), book.editionSources, true],
    [t('bookView.bookInfo.digitalSource'), [book.sourceName]],
    [t('bookView.bookInfo.printYear'), book.publicationDates],
  ]
  return candidates
    .map(([label, values, isUrlList]): DetailsRow => {
      const meaningful = meaningfulValues(values)
      if (!isUrlList) return { label, value: meaningful.join(', ') }
      return {
        label,
        value: meaningful.filter((v) => !isWebUrl(v)).join('\n'),
        links: meaningful.filter(isWebUrl).map((href) => ({ text: decodeUrlForDisplay(href), href })),
      }
    })
    .filter((row) => row.value !== '' || (row.links?.length ?? 0) > 0)
})

const message = computed(() => {
  if (loadFailed.value) return t('bookView.bookInfo.loadFailed')
  if (!isLoading.value && rows.value.length === 0) return t('bookView.bookInfo.empty')
  return undefined
})
</script>

<template>
  <DetailsDialog
    :title="t('bookView.bookInfo.title')"
    :sections="[{ rows }]"
    :loading="isLoading"
    :message="message"
    @close="emit('close')"
  />
</template>
