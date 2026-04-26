<script setup lang="ts">
import { computed, ref } from 'vue'
import type { ComponentPublicInstance } from 'vue'
import BooksFsTreeView from './BooksFsTreeView.vue'
import BooksFsTileView from './BooksFsTileView.vue'
import BooksFsListView from './BooksFsListView.vue'
import type { FsItem } from './useBooksFs'
import type { CategoryNode, BookRow } from './booksCategoryTree'

const props = defineProps<{ items: FsItem[]; view: 'list' | 'tiles' | 'tree' }>()
const emit = defineEmits<{ selectBook: [BookRow]; enterFolder: [CategoryNode] }>()

type BooksFsViewInstance = ComponentPublicInstance & {
  focusContainer?: () => void
  reset?: () => void
}

const activeViewRef = ref<BooksFsViewInstance | null>(null)

const activeViewComponent = computed(() => {
  if (props.view === 'tree') return BooksFsTreeView
  if (props.view === 'tiles') return BooksFsTileView
  return BooksFsListView
})

const activeViewProps = computed(() => (props.view === 'tree' ? {} : { items: props.items }))

function focusContainer() {
  activeViewRef.value?.focusContainer?.()
}

function reset() {
  activeViewRef.value?.reset?.()
}

defineExpose({ focusContainer, reset })
</script>

<template>
  <div class="books-fs-view">
      <component
        :is="activeViewComponent"
        ref="activeViewRef"
        v-bind="activeViewProps"
        @select-book="$emit('selectBook', $event)"
        @enter-folder="$emit('enterFolder', $event)"
      />
  </div>
</template>

<style scoped>
.books-fs-view {
  height: 100%;
  overflow: hidden;
}
</style>
