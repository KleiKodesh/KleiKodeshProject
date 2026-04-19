<script setup lang="ts">
import { ref, computed, nextTick, watch } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import type { TocEntry, AltTocSection } from './useToc'
import { SearchableTree } from '@/utils/tocSearchUtils'
import BookViewTocTreeSection from './BookViewTocTreeSection.vue'
import SplitPane from '@/components/common/SplitPane.vue'

const props = defineProps<{
  bookId: number | undefined
  bookTitle?: string
  activeTocEntryId?: number
  visible?: boolean
  tocEntries: TocEntry[]
  altTocSections: AltTocSection[]
  loading: boolean
  error: string | null
  tocSearchTree?: SearchableTree
  toggleButtonEl?: HTMLElement | null
}>()
const emit = defineEmits<{ close: []; select: [TocEntry]; altSelect: [TocEntry] }>()

const panelRef = ref<HTMLElement | null>(null)
const searchRef = ref<HTMLInputElement | null>(null)
const tocSectionRef = ref<InstanceType<typeof BookViewTocTreeSection> | null>(null)
const searchQuery = ref('')

watch(
  () => props.loading,
  (val) => {
    if (!val) nextTick(() => searchRef.value?.focus({ preventScroll: true }))
  },
)
watch(
  () => props.visible,
  (val) => {
    if (val) nextTick(() => searchRef.value?.focus({ preventScroll: true }))
  },
)
useDropdownClose(
  panelRef,
  () => emit('close'),
  { toggleButton: computed(() => props.toggleButtonEl ?? null), closeOnBlur: false },
)

function focusTocList() {
  const el = tocSectionRef.value?.containerRef?.()
  el?.focus()
}

function onSelect(entry: TocEntry) {
  emit('select', entry)
}

const hasToc = computed(() => props.tocEntries.length > 0)
const hasAlt = computed(() => props.altTocSections.length > 0)

// Build alt TOC search trees lazily — only when the user actually types a search query
watch(searchQuery, (q) => {
  if (!q) return
  for (const section of props.altTocSections) {
    if (section.searchTree == null) {
      section.searchTree = new SearchableTree(section.entries)
    }
  }
})
</script>

<template>
  <div ref="panelRef" class="toc-panel" :class="{ 'is-hidden': !visible }">
      <div v-if="loading" class="toc-state">טוען...</div>
      <div v-else-if="error" class="toc-state error">{{ error }}</div>
      <template v-else>
        <SplitPane :bottom-visible="hasToc && hasAlt" class="toc-body">
          <template #top>
            <BookViewTocTreeSection
              ref="tocSectionRef"
              v-show="hasToc"
              :title="null"
              :entries="tocEntries"
              :filter="searchQuery"
              :active-entry-id="activeTocEntryId"
              :visible="props.visible"
              :search-tree="tocSearchTree"
              @select="onSelect"
            />
          </template>
          <template #bottom>
            <BookViewTocTreeSection
              v-for="section in altTocSections"
              :key="section.structure.id"
              :title="null"
              :entries="section.entries"
              :filter="searchQuery"
              :search-tree="section.searchTree ?? undefined"
              @select="emit('altSelect', $event)"
            />
          </template>
        </SplitPane>
        <div class="toc-search">
          <div class="search-inner">
            <input
              ref="searchRef"
              v-model="searchQuery"
              type="search"
              name="toc-search"
              class="search-input"
              placeholder="חיפוש..."
              @keydown.up.prevent="focusTocList"
              @keydown.down.prevent="focusTocList"
              @keydown.tab.prevent="focusTocList"
            />
          </div>
        </div>
      </template>
    </div>
</template>

<style scoped>
.toc-panel {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  z-index: 100;
  display: flex;
  flex-direction: column;
  width: fit-content;
  max-width: 30%;
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
  overflow: hidden;
  --tree-bg: var(--bg-secondary);
  transition: transform 180ms ease;
  transform: translateX(0);
  pointer-events: auto;
}
.toc-panel.is-hidden {
  transform: translateX(100%);
  pointer-events: none;
}
.toc-body {
  flex: 1;
  min-height: 0;
}
.toc-search {
  padding: 5px 6px 6px;
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
  box-sizing: border-box;
  background: var(--tree-bg, var(--bg-primary));
}
.search-inner {
  display: flex;
  align-items: center;
  padding: 4px 8px;
}
.search-input {
  flex: 1;
  width: 0;
  min-width: 0;
  background: none;
  border: none;
  outline: none;
  font-size: 12px;
  color: var(--text-primary);
}
.search-input::placeholder {
  color: var(--text-secondary);
}
.search-input::-webkit-search-cancel-button {
  filter: grayscale(1) opacity(0.4);
}
.toc-state {
  padding: 32px 16px;
  text-align: center;
  font-size: 14px;
  color: var(--text-secondary);
}
.toc-state.error {
  color: #ff3b30;
}

</style>
