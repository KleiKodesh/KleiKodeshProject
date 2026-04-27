<script setup lang="ts">
import { computed, nextTick, onMounted, ref, watch } from 'vue'
import type { TocEntry, AltTocSection } from './useToc'
import { SearchableTree } from '@/utils/tocSearchUtils'
import BookViewTocTreeSection from './BookViewTocTreeSection.vue'
import SplitPane from '@/components/common/SplitPane.vue'

const props = defineProps<{
  activeTocEntryId?: number
  tocEntries: TocEntry[]
  altTocSections: AltTocSection[]
  loading: boolean
  error: string | null
  tocSearchTree?: SearchableTree
}>()

const emit = defineEmits<{ select: [TocEntry]; altSelect: [TocEntry] }>()

const searchRef = ref<HTMLInputElement | null>(null)
const tocSectionRef = ref<InstanceType<typeof BookViewTocTreeSection> | null>(null)
const searchQuery = ref('')

// Component mounts fresh each time the panel opens (v-if), so onMounted handles the
// initial focus. The loading watcher covers the case where TOC data arrives after mount.
onMounted(() => {
  if (!props.loading) nextTick(() => searchRef.value?.focus({ preventScroll: true }))
})

watch(
  () => props.loading,
  (val) => {
    if (!val) nextTick(() => searchRef.value?.focus({ preventScroll: true }))
  },
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
  <div class="toc-tree">
    <div v-if="loading" class="toc-state">&#x5D8;&#x5D5;&#x5E2;&#x5DF;...</div>
    <div v-else-if="error" class="toc-state error">{{ error }}</div>
    <template v-else>
      <SplitPane :bottom-visible="hasToc && hasAlt" class="toc-body">
        <template #top>
          <BookViewTocTreeSection
            ref="tocSectionRef"
            v-if="hasToc"
            :title="null"
            :entries="tocEntries"
            :filter="searchQuery"
            :active-entry-id="activeTocEntryId"
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
            placeholder="&#x5D7;&#x5D9;&#x5E4;&#x5D5;&#x5E9;..."
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
.toc-tree {
  display: flex;
  flex-direction: column;
  height: 100%;
  overflow: hidden;
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
