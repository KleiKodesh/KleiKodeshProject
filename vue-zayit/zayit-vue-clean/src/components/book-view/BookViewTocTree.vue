<script setup lang="ts">
import { ref, computed, onMounted, nextTick, watch } from 'vue'
import { useEventListener } from '@vueuse/core'
import { useToc, type TocEntry } from './useToc'
import BookViewTocTreeSection from './BookViewTocTreeSection.vue'
import SplitPane from '@/components/common/SplitPane.vue'

const props = defineProps<{ bookId: number | undefined; bookTitle?: string }>()
const emit = defineEmits<{ close: []; select: [entry: TocEntry] }>()

const searchQuery = ref('')
const searchRef = ref<HTMLInputElement | null>(null)
const { tocEntries, altTocSections, loading, error } = useToc(() => props.bookId)

// Focus search once content is ready (after loading resolves and DOM updates)
watch(loading, (val) => { if (!val) nextTick(() => searchRef.value?.focus()) })
useEventListener('keydown', (e: KeyboardEvent) => { if (e.key === 'Escape') emit('close') })

const hasToc = computed(() => displayedTocEntries.value.length > 0)
const hasAlt = computed(() => altTocSections.value.length > 0)
const hasBoth = computed(() => hasToc.value && hasAlt.value)

const displayedTocEntries = computed(() => {
  const entries = tocEntries.value
  if (!props.bookTitle || entries.length === 0) return entries
  const roots = entries.filter(e => e.parentId === null)
  if (roots.length !== 1 || roots[0]!.text !== props.bookTitle) return entries
  const rootId = roots[0]!.id
  return entries
    .filter(e => e.id !== rootId)
    .map(e => e.parentId === rootId ? { ...e, parentId: null, level: e.level - 1 } : { ...e, level: e.level - 1 })
})
</script>

<template>
  <Transition name="toc-overlay">
    <div class="toc-backdrop">
      <div class="toc-backdrop-dismiss" @click="$emit('close')" />
      <div class="toc-panel" @click.stop>

        <div v-if="loading" class="toc-state">טוען...</div>
        <div v-else-if="error" class="toc-state error">{{ error }}</div>

        <template v-else>
          <!-- Split pane fills all space above the search bar -->
          <SplitPane :bottom-visible="hasBoth" class="toc-body">
            <template #top>
              <BookViewTocTreeSection
                v-if="hasToc"
                :title="null"
                :entries="displayedTocEntries"
                :filter="searchQuery"
                @select="$emit('select', $event)"
              />
            </template>
            <template #bottom>
              <BookViewTocTreeSection
                v-for="section in altTocSections"
                :key="section.structure.id"
                :title="null"
                :entries="section.entries"
                :filter="searchQuery"
                @select="$emit('select', $event)"
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
    </div>
  </Transition>
</template>

<style scoped>
.toc-backdrop {
  position: absolute;
  inset: 0;
  z-index: 100;
}

.toc-panel {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  display: flex;
  flex-direction: column;
  width: fit-content;
  max-width: min(320px, 85%);
  background: rgba(var(--bg-secondary-rgb), 0.82);
  border-left: 1px solid var(--border-color);
  overflow: hidden;
}

.toc-backdrop-dismiss {
  position: absolute;
  inset: 0;
  cursor: pointer;
  background: color-mix(in srgb, #000 30%, transparent);
}

/* toc-body fills all remaining vertical space above the search bar */
.toc-body {
  flex: 1;
  min-height: 0;
}

.toc-search {
  padding: 6px 8px 8px;
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
  /* must not drive panel width — align to whatever width the content set */
  width: 100%;
  box-sizing: border-box;
}

.search-inner {
  display: flex;
  align-items: center;
  background: color-mix(in srgb, var(--text-secondary) 12%, transparent);
  border-radius: 10px;
  padding: 5px 8px;
}

.search-input {
  flex: 1;
  width: 0; /* let flex drive width, not intrinsic size */
  min-width: 0;
  background: none;
  border: none;
  outline: none;
  font-size: 13px;
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

.toc-overlay-enter-active,
.toc-overlay-leave-active {
  transition: opacity 180ms ease;
}
.toc-overlay-enter-active .toc-panel,
.toc-overlay-leave-active .toc-panel {
  transition: transform 180ms ease;
}
.toc-overlay-enter-from,
.toc-overlay-leave-to { opacity: 0; }
.toc-overlay-enter-from .toc-panel,
.toc-overlay-leave-to .toc-panel { transform: translateX(100%); }
</style>
