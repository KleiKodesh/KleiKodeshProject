<script setup lang="ts">
import { ref } from 'vue'
import BooksFsTreeView from './BooksFsTreeView.vue'
import BooksFsTileView from './BooksFsTileView.vue'
import BooksFsListView from './BooksFsListView.vue'
import type { FsItem } from './useBooksFs'
import type { CategoryNode, BookRow } from './booksCategoryTree'

const props = defineProps<{ items: FsItem[]; view: 'list' | 'tiles' | 'tree' }>()
const emit = defineEmits<{ selectBook: [BookRow]; enterFolder: [CategoryNode] }>()

const treeViewRef = ref<InstanceType<typeof BooksFsTreeView> | null>(null)
const tileViewRef = ref<InstanceType<typeof BooksFsTileView> | null>(null)
const listViewRef = ref<InstanceType<typeof BooksFsListView> | null>(null)

function focusContainer() {
  if (props.view === 'tree') {
    treeViewRef.value?.focusContainer()
    return
  }
  if (props.view === 'tiles') {
    tileViewRef.value?.focusContainer()
    return
  }
  listViewRef.value?.focusContainer()
}

function reset() {
  if (props.view === 'tree') treeViewRef.value?.reset()
}

defineExpose({ focusContainer, reset })
</script>

<template>
  <div class="books-fs-view">
    <BooksFsTreeView
      ref="treeViewRef"
      v-show="view === 'tree'"
      @select-book="$emit('selectBook', $event)"
    />
    <BooksFsTileView
      ref="tileViewRef"
      v-show="view === 'tiles'"
      :items="items"
      @select-book="$emit('selectBook', $event)"
      @enter-folder="$emit('enterFolder', $event)"
    />
    <BooksFsListView
      ref="listViewRef"
      v-show="view === 'list'"
      :items="items"
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
