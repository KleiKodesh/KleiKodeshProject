<script setup lang="ts">
/**
 * The book-info content: what the library records ABOUT a book (authors, category, publication
 * place and date, licence, source...). Rendered both inside the toolbar's info dropdown and
 * inline in the overflow flyout, so the two faces show the same thing.
 *
 * Fetches when mounted and again when the book changes. Mounted only once someone opens it,
 * so most readers never pay for the query. Only rows that have a value are rendered.
 */
import { ref, computed, watch } from 'vue'
import { getBookMetadata } from '@/webview-host/seforimApi'
import { openUrlInSystemBrowser, getDbPathInfo } from '@/webview-host/bridge'
import type { BookMetadataRow } from '@/webview-host/queries.types'
import { useI18n } from 'vue-i18n'

const { t } = useI18n()

const props = defineProps<{
  bookId: number | undefined
}>()

// ── Data ──────────────────────────────────────────────────────────────────────

const metadata = ref<BookMetadataRow | null>(null)
const loadedBookId = ref<number | null>(null)
const isLoading = ref(false)
const loadFailed = ref(false)

async function loadMetadata() {
  const bookId = props.bookId
  if (bookId == null || loadedBookId.value === bookId) return
  isLoading.value = true
  loadFailed.value = false
  try {
    const result = await getBookMetadata(bookId)
    // The book changed while this was in flight - its answer belongs to no one now.
    if (props.bookId !== bookId) return
    metadata.value = result ?? null
    loadedBookId.value = bookId
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
    loadedBookId.value = null
    isLoading.value = false
    loadFailed.value = false
    void loadMetadata()
  },
  { immediate: true },
)

/** On Zayit's DB (under its `io.github.kdroidfilter.seforimapp` folder) the description row is hidden. */
const isZayitDb = ref(false)
void getDbPathInfo().then((info) => {
  isZayitDb.value = /seforimapp/i.test(info?.path ?? '')
})

// ── Rows ──────────────────────────────────────────────────────────────────────

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

/** One URL as a link: the decoded text to show, and the raw address to open. */
interface BookInfoLink {
  text: string
  href: string
}

interface BookInfoRow {
  label: string
  /** Plain-text value; empty on a link row. */
  value: string
  /** Set on the edition-source row: one link per line. */
  links: BookInfoLink[]
}

/** Only web addresses become links; anything else in the column stays plain text. */
function isWebUrl(value: string): boolean {
  return /^https?:\/\//i.test(value)
}

const infoRows = computed<BookInfoRow[]>(() => {
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
    .map(([label, values, isUrlList]): BookInfoRow => {
      const meaningful = meaningfulValues(values)
      if (!isUrlList) return { label, value: meaningful.join(', '), links: [] }
      // URLs go one per line; a value that is not a web address stays as text.
      return {
        label,
        value: meaningful.filter((v) => !isWebUrl(v)).join('\n'),
        links: meaningful.filter(isWebUrl).map((href) => ({ text: decodeUrlForDisplay(href), href })),
      }
    })
    .filter((row) => row.value !== '' || row.links.length > 0)
})
</script>

<template>
  <div class="book-info-panel">
    <div v-if="isLoading" class="book-info-message">{{ $t('bookView.loading') }}</div>
    <div v-else-if="loadFailed" class="book-info-message">{{ $t('bookView.bookInfo.loadFailed') }}</div>
    <div v-else-if="infoRows.length === 0" class="book-info-message">{{ $t('bookView.bookInfo.empty') }}</div>
    <dl v-else class="book-info-list">
      <div v-for="row in infoRows" :key="row.label" class="book-info-row">
        <dt>{{ row.label }}</dt>
        <dd>
          <template v-if="row.value">{{ row.value }}</template>
          <!-- A real href so it reads and behaves as a link; the click goes to the system
               browser through the host, since a WebView2 popup is not what anyone wants. -->
          <a
            v-for="link in row.links"
            :key="link.href"
            class="book-info-link"
            :href="link.href"
            :title="link.text"
            @click.prevent="openUrlInSystemBrowser(link.href)"
          >{{ link.text }}</a>
        </dd>
      </div>
    </dl>
  </div>
</template>

<style scoped>
/* main.css sets `* { user-select: none }` on every element, so opting the panel's root
   out is not enough - each descendant has to be opted back in too. */
.book-info-panel,
.book-info-panel * {
  user-select: text;
  cursor: text;
}
.book-info-panel {
  text-align: start;
  font-size: 12px;
  color: var(--text-primary);
}
.book-info-list {
  margin: 0;
}
.book-info-row {
  padding: 4px 0;
}
.book-info-row + .book-info-row {
  border-top: 1px solid var(--border-color);
}
.book-info-row dt {
  color: var(--text-secondary);
  font-size: 11px;
}
.book-info-row dd {
  margin: 0;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
/* One line per link, cut with an ellipsis - the full address is in the tooltip. */
.book-info-link {
  display: block;
  min-width: 0;
  max-width: 100%;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  color: var(--accent-color);
  cursor: pointer;
}
.book-info-link:hover {
  text-decoration: underline;
}
.book-info-message {
  padding: 4px 0;
  color: var(--text-secondary);
}
</style>
