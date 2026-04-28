<script setup lang="ts">
import { ref, computed } from 'vue'
import { useElementSize } from '@vueuse/core'
import {
  IconBookmark20Regular, IconSearch20Regular,
  IconChevronUp20Regular, IconChevronDown20Regular,
  IconFilter16Regular, IconZoomIn20Regular, IconZoomOut20Regular,
  IconMoreHorizontal20Regular,
  IconEdit20Regular, IconEdit20Filled,
} from '@iconify-prerendered/vue-fluent'
import { usePdfViewerControls, ZOOM_OPTIONS } from './usePdfViewerControls'
import { usePdfFind } from './usePdfFind'
import PdfToolbarMoreMenu from './PdfToolbarMoreMenu.vue'
import PdfEditorToolbar from './PdfEditorToolbar.vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import FloatingSearchBar from '@/components/common/FloatingSearchBar.vue'

const props = defineProps<{ iframeEl: HTMLIFrameElement | null }>()
const getIframe = () => props.iframeEl

const viewer = usePdfViewerControls(getIframe)
const find = usePdfFind(getIframe)

const toolbarRef = ref<HTMLElement | null>(null)
const { width: toolbarWidth } = useElementSize(toolbarRef)
const showZoomSelectInline = computed(() => toolbarWidth.value >= 420)
const showPageTotalInline = computed(() => toolbarWidth.value >= 360)

const moreWrapRef = ref<HTMLElement | null>(null)
const moreOpen = ref(false)
const { justClosed } = useDropdownClose(moreWrapRef, () => { moreOpen.value = false })
function toggleMore() { if (justClosed.value) return; moreOpen.value = !moreOpen.value }
function closeMore() { moreOpen.value = false }

const editorToolbarVisible = ref(false)

defineExpose({ openFind: find.openFind })
</script>

<template>
  <div class="pdf-toolbar-wrap" dir="rtl">
    <div ref="toolbarRef" class="pdf-toolbar">
      <div ref="moreWrapRef" class="more-wrap">
        <button class="tool-btn" :class="{ active: moreOpen }" title="עוד" @click="toggleMore">
          <IconMoreHorizontal20Regular />
        </button>
        <PdfToolbarMoreMenu
          v-if="moreOpen"
          :current-zoom="viewer.currentZoom.value"
          :current-page="viewer.currentPage.value"
          :total-pages="viewer.totalPages.value"
          :show-zoom-select-inline="showZoomSelectInline"
          :show-page-total-inline="showPageTotalInline"
          :cursor-tool="viewer.cursorTool.value"
          :scroll-mode="viewer.scrollMode.value"
          :spread-mode="viewer.spreadMode.value"
          @set-zoom="(v) => { viewer.setZoom(v); closeMore() }"
          @download="viewer.download(); closeMore()"
          @print="viewer.print(); closeMore()"
          @presentation-mode="viewer.presentationMode(); closeMore()"
          @first-page="viewer.firstPage(); closeMore()"
          @last-page="viewer.lastPage(); closeMore()"
          @rotate-cw="viewer.rotateCw(); closeMore()"
          @rotate-ccw="viewer.rotateCcw(); closeMore()"
          @set-cursor-tool="(t) => { viewer.setCursorTool(t); closeMore() }"
          @set-scroll-mode="(m) => { viewer.setScrollMode(m); closeMore() }"
          @set-spread-mode="(m) => { viewer.setSpreadMode(m); closeMore() }"
          @rectangle-select="viewer.rectangleSelect(); closeMore()"
          @document-properties="viewer.documentProperties(); closeMore()"
        />
      </div>
      <div class="separator" />

      <div class="spacer" />

      <button class="tool-btn" :class="{ active: find.findOpen.value }" title="חיפוש" @click="find.toggleFind">
        <IconSearch20Regular />
      </button>
      <div class="separator" />

      <button class="tool-btn" :class="{ active: editorToolbarVisible }" title="כלי עריכה" @click="editorToolbarVisible = !editorToolbarVisible">
        <IconEdit20Filled v-if="editorToolbarVisible" />
        <IconEdit20Regular v-else />
      </button>
      <div class="separator" />

      <button class="tool-btn" title="עמוד קודם" @click="viewer.prevPage">
        <IconChevronUp20Regular />
      </button>
      <div class="page-input-wrap" @click="($refs.pageNumberInput as HTMLInputElement)?.focus()">
        <input
          ref="pageNumberInput"
          v-model="viewer.pageInputValue.value"
          class="page-number-input"
          type="number"
          min="1"
          :max="viewer.totalPages.value"
          @focus="viewer.pageInputFocused.value = true"
          @blur="viewer.pageInputFocused.value = false; viewer.commitPageInput()"
          @change="viewer.commitPageInput"
          @keydown.enter="viewer.commitPageInput"
        />
        <span v-if="showPageTotalInline && viewer.totalPages.value" class="page-total-inline">
          / {{ viewer.totalPages.value }}
        </span>
      </div>
      <button class="tool-btn" title="עמוד הבא" @click="viewer.nextPage">
        <IconChevronDown20Regular />
      </button>
      <div class="separator" />

      <button class="tool-btn" title="הקטן" @click="viewer.zoomOut"><IconZoomOut20Regular /></button>
      <select
        v-if="showZoomSelectInline"
        class="zoom-select"
        :value="viewer.currentZoom.value"
        @change="viewer.setZoom(($event.target as HTMLSelectElement).value)"
      >
        <option v-for="option in ZOOM_OPTIONS" :key="option.value" :value="option.value">{{ option.label }}</option>
        <option v-if="!ZOOM_OPTIONS.find((o) => o.value === viewer.currentZoom.value)" :value="viewer.currentZoom.value">
          {{ viewer.zoomLabel(viewer.currentZoom.value) }}
        </option>
      </select>
      <button class="tool-btn" title="הגדל" @click="viewer.zoomIn"><IconZoomIn20Regular /></button>
      <div class="separator" />

      <button class="tool-btn" title="תוכן עניינים" @click="viewer.toggleSidebar">
        <IconBookmark20Regular />
      </button>

      <div class="spacer" />
    </div>

    <!-- Find bar -->
    <FloatingSearchBar
      :visible="find.findOpen.value"
      :query="find.findQuery.value"
      :match-count="find.findMatchCount.value"
      :match-index="find.findMatchIndex.value"
      :not-found="find.findNotFound.value"
      placeholder="חיפוש..."
      :initial-position="{ x: window.innerWidth / 2 - 140, y: 84 }"
      @update:query="find.findQuery.value = $event"
      @next="find.findNext"
      @previous="find.findPrevious"
      @close="find.closeFind"
    >
      <template #after-nav>
        <span class="sep" />
        <button
          class="nav-btn"
          :class="{ active: find.findOptionsOpen.value }"
          title="אפשרויות חיפוש"
          @click="find.findOptionsOpen.value = !find.findOptionsOpen.value"
        >
          <IconFilter16Regular />
        </button>
      </template>
      <template #panel>
        <div v-if="find.findOptionsOpen.value" class="find-options-panel">
          <label class="find-option"><input v-model="find.findHighlightAll.value" type="checkbox" /><span>הדגש הכל</span></label>
          <label class="find-option"><input v-model="find.findMatchCase.value" type="checkbox" /><span>תלוי רישיות</span></label>
          <label class="find-option"><input v-model="find.findMatchDiacritics.value" type="checkbox" /><span>תלוי ניקוד</span></label>
          <label class="find-option"><input v-model="find.findWholeWord.value" type="checkbox" /><span>מילה שלמה</span></label>
        </div>
      </template>
    </FloatingSearchBar>
    <!-- Editor toolbar -->
    <PdfEditorToolbar
      v-if="editorToolbarVisible"
      :iframe-el="iframeEl"
      @close="editorToolbarVisible = false"
    />
  </div>
</template>

<style scoped>
.pdf-toolbar-wrap {
  position: relative;
  flex-shrink: 0;
  background: var(--bg-toolbar);
  border-bottom: 1px solid var(--border-color);
}

.pdf-toolbar {
  display: flex;
  align-items: center;
  height: 32px;
  padding: 2px 4px;
  gap: 0;
}

.tool-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 32px;
  height: 32px;
  border-radius: 4px;
  flex-shrink: 0;
  color: var(--text-secondary);
}

.tool-btn svg { width: 16px; height: 16px; }

.tool-btn.active {
  color: var(--accent-color);
  background: color-mix(in srgb, var(--accent-color) 12%, transparent);
}

.separator { width: 1px; height: 18px; background: var(--border-color); margin: 0 2px; flex-shrink: 0; }

.page-input-wrap {
  display: inline-flex;
  align-items: center;
  height: 22px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-primary);
  padding: 0 5px;
  flex-shrink: 0;
  cursor: text;
  direction: ltr;
  gap: 1px;
}

.page-number-input {
  width: 20px;
  height: 16px;
  font-size: 11px;
  line-height: 16px;
  text-align: center;
  color: var(--text-primary);
  background: none;
  border: none;
  outline: none;
  padding: 0;
  margin: 0;
  display: block;
  -moz-appearance: textfield;
}

.page-number-input::-webkit-outer-spin-button,
.page-number-input::-webkit-inner-spin-button { -webkit-appearance: none; }

.page-total-inline {
  font-size: 11px;
  line-height: 16px;
  color: var(--text-secondary);
  white-space: nowrap;
  user-select: none;
  pointer-events: none;
}

.zoom-select {
  height: 22px;
  font-size: 11px;
  border: 1px solid var(--border-color);
  border-radius: 4px;
  background: var(--bg-primary);
  color: var(--text-primary);
  padding: 0 4px;
  flex-shrink: 0;
  max-width: 80px;
  cursor: pointer;
}

.more-wrap { position: relative; flex-shrink: 0; }

.spacer { flex: 1; }


.find-options-panel {
  position: absolute;
  top: calc(100% + 6px);
  left: 50%;
  transform: translateX(-50%);
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4), 0 1px 3px rgba(0, 0, 0, 0.25);
  padding: 6px 4px;
  display: flex;
  flex-direction: column;
  min-width: 140px;
  z-index: 10000;
}

.find-option {
  display: flex;
  align-items: center;
  gap: 8px;
  height: 30px;
  padding: 0 10px;
  font-size: 12px;
  color: var(--text-primary);
  cursor: pointer;
  border-radius: 4px;
  user-select: none;
  direction: rtl;
}

.find-option:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }

.find-option input[type='checkbox'] {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  accent-color: var(--accent-color);
  cursor: pointer;
}

.search-bar-enter-active, .search-bar-leave-active { transition: opacity 150ms ease, transform 150ms ease; }
.search-bar-enter-from, .search-bar-leave-to { opacity: 0; transform: translateX(-50%) translateY(-6px); }
</style>
