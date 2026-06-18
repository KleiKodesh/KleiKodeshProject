<script setup lang="ts">
import { computed, ref } from 'vue'
import { storeToRefs } from 'pinia'
import {
  IconSearch20Regular,
  IconLayoutRowTwo20Regular,
  IconLayoutRowTwoFocusBottom20Filled,
  IconLayoutColumnTwoFocusLeft20Filled,
  IconZoomIn20Regular,
  IconZoomOut20Regular,
  IconTimeline20Regular,
  IconTimeline20Filled,
} from '@iconify-prerendered/vue-fluent'
import IconTreeRtl from '@/components/IconTreeRtl.vue'
import BookViewRelatedBooksDropdown from './BookViewRelatedBooksDropdown.vue'
import { useSettingsStore } from '@/stores/settingsStore'
import { useBookViewStore } from '@/stores/bookViewStore'
import { ZOOM_CONFIG } from '@/composables/useZoom'
import type { CommentaryGroup } from './commentary/useCommentary'
import type { LineItem } from './lines/useBookViewLinesTable'

const props = defineProps<{
  commentaryVisible: boolean
  searchVisible: boolean
  tocVisible: boolean
  hasToc: boolean
  hasCommentaries: boolean
  hasRelatedBooks: boolean
  bookId: number | undefined
  bookHasTeamim: boolean
  filterGroups: CommentaryGroup[]
  relatedBooksLoaded: boolean
  currentScrollLineIndex: number
  lines: LineItem[]
  onRelatedBooksOpen?: () => void
  commentaryMode?: 'off' | 'bottom' | 'side'
}>()
defineEmits<{ cycleCommentaryMode: []; toggleSearch: []; toggleToc: []; exportToWord: [] }>()

const settingsStore = useSettingsStore()
const bookViewStore = useBookViewStore()
const { zoom, commentaryZoom, toolbarPosition, autoSelectTopLine } = storeToRefs(bookViewStore)

const diacriticsState = computed(() => settingsStore.diacriticsState)

// When the book has no teamim the cycle is 0→2→0, so the title reflects only two stages.
const diacriticsTitle = computed(() => {
  if (!props.bookHasTeamim) {
    return diacriticsState.value === 0 ? 'הסר ניקוד' : 'שחזר ניקוד'
  }
  return ['הסר טעמים', 'הסר גם ניקוד', 'שחזר טעמים וניקוד'][diacriticsState.value]!
})

function onDiacriticsClick() {
  if (props.bookHasTeamim) {
    settingsStore.cycleDiacritics()
  } else {
    settingsStore.cycleDiacriticsNoTeamim()
  }
}

const tocBtnRef = ref<HTMLElement | null>(null)
defineExpose({ tocBtnRef })
</script>

<template>
  <div class="book-view-toolbar" :class="`toolbar-${toolbarPosition}`">
    <button
      ref="tocBtnRef"
      :class="{ active: tocVisible }"
      :disabled="!hasToc"
      title="תוכן עניינים (Ctrl+K)"
      @click="$emit('toggleToc')"
    >
      <IconTreeRtl />
    </button>
    <BookViewRelatedBooksDropdown
      :book-id="bookId"
      :filter-groups="filterGroups"
      :related-books-loaded="relatedBooksLoaded"
      :current-scroll-line-index="currentScrollLineIndex"
      :lines="lines"
      :disabled="!hasRelatedBooks"
      :on-open="onRelatedBooksOpen"
    />
    <button
      :class="{ active: commentaryMode !== 'off' }"
      :disabled="!hasCommentaries"
      :title="commentaryMode === 'off' ? 'פאנל מפרשים (Ctrl+J)' : commentaryMode === 'bottom' ? 'עבור לתצוגה צדדית' : 'סגור פאנל מפרשים'"
      @click="$emit('cycleCommentaryMode')"
    >
      <IconLayoutColumnTwoFocusLeft20Filled v-if="commentaryMode === 'side'" />
      <IconLayoutRowTwoFocusBottom20Filled v-else-if="commentaryMode === 'bottom'" />
      <IconLayoutRowTwo20Regular v-else />
    </button>
    <button
      :class="{ active: autoSelectTopLine }"
      :disabled="!hasCommentaries"
      :title="
        autoSelectTopLine
          ? 'סנכרן מפרשים\nלחץ לכיבוי הסנכרון האוטומטי'
          : 'סנכרן מפרשים\nמפרשים יתעדכנו אוטומטית לפי השורה העליונה'
      "
      @click="bookViewStore.toggleAutoSelectTopLine()"
    >
      <IconTimeline20Filled v-if="autoSelectTopLine" />
      <IconTimeline20Regular v-else />
    </button>
    <button
      :class="{ active: searchVisible }"
      title="חיפוש (Ctrl+F)"
      @click="$emit('toggleSearch')"
    >
      <IconSearch20Regular />
    </button>

    <div class="separator" />

    <button
      :title="`הקטן (Ctrl-)\nטקסט: ${zoom}% | פירוש: ${commentaryZoom}%\nאיפוס: Ctrl+0`"
      :disabled="zoom <= ZOOM_CONFIG.MIN && commentaryZoom <= ZOOM_CONFIG.MIN"
      @click="bookViewStore.zoomOut()"
    >
      <IconZoomOut20Regular />
    </button>
    <button
      :title="`הגדל (Ctrl+)\nטקסט: ${zoom}% | פירוש: ${commentaryZoom}%\nאיפוס: Ctrl+0`"
      :disabled="zoom >= ZOOM_CONFIG.MAX && commentaryZoom >= ZOOM_CONFIG.MAX"
      @click="bookViewStore.zoomIn()"
    >
      <IconZoomIn20Regular />
    </button>

    <div class="separator" />

    <button
      :class="[
        'diacritics-btn',
        { 'state-1': diacriticsState === 1, 'state-2': diacriticsState === 2 },
      ]"
      :title="diacriticsTitle"
      @click="onDiacriticsClick()"
    >
      <svg
        v-if="diacriticsState === 0"
        width="16"
        height="18"
        viewBox="0 0 126 139"
        fill="currentColor"
      >
        <g transform="translate(0,139) scale(0.1,-0.1)">
          <path
            d="M398 1153c-37-40-48-66-48-112 0-56 15-90 62-138 39-40 40-41 19-52-28-15-68-87-76-137-3-22-1-70 5-106 13-71 4-108-25-108-8 0-15-7-15-15 0-12 19-15 113-15 134 0 157 10 157 68 0 42-12 62-82 141-51 59-61 99-34 136 13 18 24 9 180-139 134-128 167-164 172-192 8-45 27-43 63 6 59 81 49 150-34 242-49 54-57 90-33 154 9 27 17 33 50 37 22 3 42 7 44 10 3 3 5 32 5 66 1 94-27 126-118 137-26 3-61 15-76 26-39 27-50 14-55-69-5-80 13-122 64-149 24-13 32-23 28-34-4-8-9-25-11-37-3-13-9-23-13-23-8 0-232 208-267 249-12 14-25 38-28 53-8 35-15 35-47 1z"
          />
          <path
            d="M450 410c0 27 3 30 30 30s30-3 30-33c0-26 6-36 24-45 37-16 57-54 63-116l6-56H566c-36 0-36 0-36 43 0 113-91 116-106 4-6-44-9-47-35-47-28 0-29 2-29 50 0 63 13 89 55 118 27 17 35 29 35 52Z"
          />
          <path
            d="M650 395V360h50c47 0 50-2 50-25 0-14-4-25-9-25-19 0-23-41-7-65 22-33 60-33 82 0 16 24 12 65-7 65-5 0-9 11-9 25 0 23 3 25 50 25h50v35 35H775 650V395Z"
          />
        </g>
      </svg>
      <svg
        v-else-if="diacriticsState === 1"
        width="16"
        height="18"
        viewBox="0 0 112 135"
        fill="currentColor"
      >
        <g transform="translate(0,135) scale(0.1,-0.1)">
          <path
            d="M328 1103c-37-40-48-66-48-112 0-56 15-90 62-138 39-40 40-41 19-52-28-15-68-87-76-137-3-22-1-70 5-106 13-71 4-108-25-108-8 0-15-7-15-15 0-12 19-15 113-15 134 0 157 10 157 68 0 42-12 62-82 141-51 59-61 99-34 136 13 18 24 9 180-139 134-128 167-164 172-192 8-45 27-43 63 6 59 81 49 150-34 242-49 54-57 90-33 154 9 27 17 33 50 37 22 3 42 7 44 10 3 3 5 32 5 66 1 94-27 126-118 137-26 3-61 15-76 26-39 27-50 14-55-69-5-80 13-122 64-149 24-13 32-23 28-34-4-8-9-25-11-37-3-13-9-23-13-23-8 0-232 208-267 249-12 14-25 38-28 53-8 35-15 35-47 1z"
          />
          <path
            d="M440 345l0-35 50 0c47 0 50-2 50-25 0-14-4-25-9-25-17 0-20-45-5-67 30-46 104-12 90 42-4 14-11 25-16 25-6 0-10 11-10 25 0 23 3 25 50 25l50 0 0 35 0 35-125 0-125 0 0-35z"
          />
        </g>
      </svg>
      <svg
        v-else
        width="16"
        height="16"
        viewBox="0 0 88 111"
        fill="currentColor"
        style="transform: scale(0.85)"
      >
        <g transform="translate(0,111) scale(0.1,-0.1)">
          <path
            d="M198 903c-37-40-48-66-48-112 0-56 15-90 62-138 39-40 40-41 19-52-28-15-68-87-76-137-3-22-1-70 5-106 13-71 4-108-25-108-8 0-15-7-15-15 0-12 19-15 113-15 134 0 157 10 157 68 0 42-12 62-82 141-51 59-61 99-34 136 13 18 24 9 180-139 134-128 167-164 172-192 8-45 27-43 63 6 59 81 49 150-34 242-49 54-57 90-33 154 9 27 17 33 50 37 22 3 42 7 44 10 3 3 5 32 5 66 1 94-27 126-118 137-26 3-61 15-76 26-39 27-50 14-55-69-5-80 13-122 64-149 24-13 32-23 28-34-4-8-9-25-11-37-3-13-9-23-13-23-8 0-232 208-267 249-12 14-25 38-28 53-8 35-15 35-47 1z"
          />
        </g>
      </svg>
    </button>
    <button title="ייצא ל-Word" @click="$emit('exportToWord')">
      <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 28 28">
        <path fill="none" d="M0 0h28v28H0z" />
        <path fill="currentColor" d="M11.75 13A2.25 2.25 0 0 1 14 15.25v8.5A2.25 2.25 0 0 1 11.75 26h-8.5A2.25 2.25 0 0 1 1 23.75v-8.5A2.25 2.25 0 0 1 3.25 13zm2.816-11a2.4 2.4 0 0 1 1.698.703l6.93 6.93A2.75 2.75 0 0 1 24 11.579V23.6a2.4 2.4 0 0 1-2.4 2.4h-7.508a3.24 3.24 0 0 0 .82-1.5H21.6a.9.9 0 0 0 .9-.9V12H16a2 2 0 0 1-2-2V3.5H6.4a.9.9 0 0 0-.9.9V12H4V4.4A2.4 2.4 0 0 1 6.4 2zm-4.181 13.751l-.935 4.95l-.996-4.95H6.502l-.952 4.95l-.958-4.95H3l1.553 7.499h1.949l.998-4.5l.954 4.5l1.93-.001L12 15.75zM15.5 10a.5.5 0 0 0 .5.5h5.94L15.5 4.06z" />
      </svg>
    </button>
  </div>
</template>

<style scoped>
.book-view-toolbar {
  display: flex;
  align-items: center;
  justify-content: center;
  gap: 0;
  padding: 2px 4px;
  background: var(--bg-toolbar);
  flex-shrink: 0;
  transition: background 120ms;
}

/* ── Orientation ── */
.toolbar-top,
.toolbar-bottom {
  flex-direction: row;
  height: 32px;
  justify-content: center;
}
.toolbar-left,
.toolbar-right {
  flex-direction: column;
  justify-content: flex-start;
  width: 40px;
  height: auto;
  padding: 4px 2px;
}

/* ── Borders ── */
.toolbar-top {
  border-bottom: 1px solid var(--border-color);
}
.toolbar-bottom {
  border-top: 1px solid var(--border-color);
}
.toolbar-left {
  border-right: 1px solid var(--border-color);
}
.toolbar-right {
  border-left: 1px solid var(--border-color);
}

/* ── Buttons ── */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button svg {
  width: 16px;
  height: 16px;
}
button.active {
  color: var(--accent-color);
}
button:disabled {
  opacity: 0.4;
  cursor: not-allowed;
}

/* ── Separators ── */
.separator {
  background: var(--border-color);
  flex-shrink: 0;
}
.toolbar-top .separator,
.toolbar-bottom .separator {
  width: 1px;
  height: 18px;
  margin: 0 2px;
}
.toolbar-left .separator,
.toolbar-right .separator {
  width: 18px;
  height: 1px;
  margin: 2px 0;
}

/* ── Diacritics ── */
.diacritics-btn.state-1 {
  color: #ff8c00;
}
.diacritics-btn.state-2 {
  color: #ff4500;
}
</style>
