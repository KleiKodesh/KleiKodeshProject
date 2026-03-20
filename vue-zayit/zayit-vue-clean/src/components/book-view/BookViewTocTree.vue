<script setup lang="ts">
import { ref, computed, nextTick, watch } from 'vue'
import { onClickOutside } from '@vueuse/core'
import { useToc, type TocEntry } from './useToc'
import BookViewTocTreeSection from './BookViewTocTreeSection.vue'
import SplitPane from '@/components/common/SplitPane.vue'

const props = defineProps<{ bookId: number | undefined; bookTitle?: string; activeTocEntryId?: number; visible?: boolean }>()
const emit = defineEmits<{ close: []; select: [entry: TocEntry]; altSelect: [entry: TocEntry] }>()

const panelRef = ref<HTMLElement | null>(null)
const searchQuery = ref('')
const searchRef = ref<HTMLInputElement | null>(null)
const { tocEntries, altTocSections, loading, error } = useToc(() => props.bookId, () => props.bookTitle)

const justSelected = ref(false)

watch(loading, (val) => { if (!val) nextTick(() => searchRef.value?.focus()) })
onClickOutside(panelRef, () => { if (!justSelected.value) emit('close') })

function onSelect(entry: TocEntry) {
  justSelected.value = true
  emit('select', entry)
  nextTick(() => { justSelected.value = false })
}

function onAltSelect(entry: TocEntry) {
  emit('altSelect', entry)
}

const hasToc = computed(() => tocEntries.value.length > 0)
const hasAlt = computed(() => altTocSections.value.length > 0)
const hasBoth = computed(() => hasToc.value && hasAlt.value)
</script>

<template>
  <Transition name="toc-slide">
    <div ref="panelRef" class="toc-panel">

      <div v-if="loading" class="toc-state">טוען...</div>
      <div v-else-if="error" class="toc-state error">{{ error }}</div>

      <template v-else>
        <SplitPane :bottom-visible="hasBoth" class="toc-body">
          <template #top>
            <BookViewTocTreeSection
              v-if="hasToc"
              :title="null"
              :entries="tocEntries"
              :filter="searchQuery"
              :active-entry-id="activeTocEntryId"
              :visible="props.visible"
              :suppress-scroll="justSelected"
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
              @select="onAltSelect"
            />
          </template>
        </SplitPane>

        <div class="toc-search">
          <div class="search-inner">
            <input
              ref="searchRef"
              v-model="searchQuery"
              type="search"
              class="search-input"
              placeholder="חיפוש..."
            />
          </div>
        </div>
      </template>

    </div>
  </Transition>
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
  max-width: min(320px, 85%);
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
  overflow: hidden;
  --tree-bg: var(--bg-secondary);
}

/* toc-body fills all remaining vertical space above the search bar */
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
  background: color-mix(in srgb, var(--text-secondary) 10%, transparent);
  border-radius: 6px;
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
.search-input::placeholder { color: var(--text-secondary); }
.search-input::-webkit-search-cancel-button { filter: grayscale(1) opacity(0.4); }

.toc-state {
  padding: 32px 16px;
  text-align: center;
  font-size: 14px;
  color: var(--text-secondary);
}
.toc-state.error { color: #ff3b30; }

.toc-slide-enter-active,
.toc-slide-leave-active {
  transition: transform 180ms ease;
}
.toc-slide-enter-from,
.toc-slide-leave-to {
  transform: translateX(100%);
}
</style>
