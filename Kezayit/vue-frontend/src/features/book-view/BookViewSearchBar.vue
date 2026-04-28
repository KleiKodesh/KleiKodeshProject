<script setup lang="ts">
import { ref, watch, computed, nextTick } from 'vue'
import { useBookViewStore } from '@/stores/bookViewStore'
import {
  IconLayoutRowTwoFocusTop20Filled,
  IconLayoutRowTwoFocusBottom20Filled,
} from '@iconify-prerendered/vue-fluent'
import FloatingSearchBar from '@/components/common/FloatingSearchBar.vue'
import type { SearchMode } from './bookViewTypes'

const props = defineProps<{
  visible: boolean
  toolbarVisible: boolean
  matchCount: number
  currentMatch: number
  commentaryVisible: boolean
  mode: SearchMode
}>()
const emit = defineEmits<{
  close: []
  queryChange: [string]
  next: []
  prev: []
  modeChange: [SearchMode]
}>()

const bookViewStore = useBookViewStore()
const searchBarRef = ref<InstanceType<typeof FloatingSearchBar> | null>(null)
const inputValue = ref('')
const searchMode = ref<SearchMode>(props.mode)

watch(() => props.mode, (m) => { if (searchMode.value !== m) searchMode.value = m })
watch(inputValue, (v) => emit('queryChange', v))
watch(searchMode, (m) => { emit('modeChange', m); nextTick(() => searchBarRef.value?.focus()) })
watch(() => props.visible, (v) => { if (v) nextTick(() => searchBarRef.value?.focus()) })
watch(() => props.commentaryVisible, (v) => {
  if (!v && searchMode.value === 'commentary') searchMode.value = 'content'
})

const APP_TITLE_BAR = 40
const BOOK_TOOLBAR = 32
const BAR_WIDTH = 260

const defaultPosition = computed(() => ({
  x: window.innerWidth / 2 - BAR_WIDTH / 2,
  y: APP_TITLE_BAR + (props.toolbarVisible ? BOOK_TOOLBAR : 0) + 4,
}))

const placeholder = computed(() =>
  searchMode.value === 'content' ? 'חיפוש בטקסט...' : 'חיפוש במפרשים...',
)

function onClose() {
  inputValue.value = ''
  emit('close')
}

defineExpose({ focus: () => searchBarRef.value?.focus() })
</script>

<template>
  <FloatingSearchBar
    ref="searchBarRef"
    :visible="visible"
    :query="inputValue"
    :match-count="matchCount"
    :match-index="currentMatch + 1"
    :not-found="false"
    :placeholder="placeholder"
    :initial-position="defaultPosition"
    :saved-position="bookViewStore.searchBarPos"
    @update:query="inputValue = $event"
    @next="emit('next')"
    @previous="emit('prev')"
    @close="onClose"
    @position-change="bookViewStore.setSearchBarPos($event)"
  >
    <template v-if="commentaryVisible" #before-nav>
      <button
        class="mode-btn"
        :class="{ active: searchMode === 'commentary' }"
        :title="searchMode === 'content' ? 'עבור לחיפוש במפרשים' : 'עבור לחיפוש בטקסט'"
        @click="searchMode = searchMode === 'content' ? 'commentary' : 'content'"
      >
        <IconLayoutRowTwoFocusBottom20Filled v-if="searchMode === 'commentary'" />
        <IconLayoutRowTwoFocusTop20Filled v-else />
      </button>
    </template>
  </FloatingSearchBar>
</template>

<style scoped>
.mode-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  border-radius: 4px;
  flex-shrink: 0;
  color: var(--text-secondary);
}
.mode-btn svg { width: 16px; height: 16px; }
.mode-btn.active { color: var(--accent-color); }
</style>
