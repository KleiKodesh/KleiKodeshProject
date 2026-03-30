<script setup lang="ts">
import { computed } from 'vue'
import { IconDismiss20Regular } from '@iconify-prerendered/vue-fluent'
import { useBooksDataStore } from '@/stores/booksDataStore'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import SearchFilterNode from './SearchFilterNode.vue'
import type { CategoryNode } from '@/components/books-fs/booksFsTree'

const props = defineProps<{
  checkedBookIds: Set<number>
  resultCounts: Map<number, number>
  hasSearched?: boolean
}>()
const emit = defineEmits<{
  toggleBook: [number]
  toggleCategory: [CategoryNode, boolean]
  checkAll: []
  uncheckAll: []
  close: []
}>()

const booksStore = useBooksDataStore()
const total = computed(() => booksStore.allBooks.length)
const isAllChecked = computed(() => total.value > 0 && props.checkedBookIds.size === total.value)
const isIndet = computed(
  () => props.checkedBookIds.size > 0 && props.checkedBookIds.size < total.value,
)

function setIndet(el: HTMLInputElement | null) {
  if (el) el.indeterminate = isIndet.value
}
</script>

<template>
  <div class="panel">
    <div class="panel-header">
      <input
        type="checkbox"
        class="cb"
        :checked="isAllChecked"
        :ref="(el) => setIndet(el as HTMLInputElement | null)"
        @change="isAllChecked ? emit('uncheckAll') : emit('checkAll')"
        title="בחר/בטל הכל"
      />
      <span class="panel-title">סינון תוצאות</span>
      <button class="close-btn" @click="emit('close')"><IconDismiss20Regular /></button>
    </div>
    <div class="panel-body">
      <LoadingAnimation v-if="booksStore.loading" />
      <template v-else>
        <SearchFilterNode
          v-for="cat in booksStore.ROOT.children"
          :key="cat.id"
          :category="cat"
          :checked-book-ids="checkedBookIds"
          :result-counts="resultCounts"
          :has-searched="hasSearched"
          @toggle-book="emit('toggleBook', $event)"
          @toggle-category="(c, v) => emit('toggleCategory', c, v)"
        />
      </template>
    </div>
  </div>
</template>

<style scoped>
.panel {
  position: absolute;
  right: 0;
  top: 0;
  bottom: 0;
  z-index: 10;
  display: flex;
  flex-direction: column;
  min-width: 200px;
  max-width: 320px;
  background: color-mix(in srgb, var(--bg-primary) 97%, transparent);
  border-left: 1px solid var(--border-color);
}
.panel-header {
  display: flex;
  align-items: center;
  gap: 6px;
  padding: 4px 6px 4px 4px;
  border-bottom: 1px solid var(--border-color);
  min-height: 36px;
}
.cb {
  width: 14px;
  height: 14px;
  flex-shrink: 0;
  cursor: pointer;
}
.panel-title {
  flex: 1;
  font-size: 13px;
  font-weight: 600;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 24px;
  height: 24px;
  padding: 4px;
  border-radius: 4px;
}
.panel-body {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
.panel-body::-webkit-scrollbar {
  width: 4px;
}
.panel-body::-webkit-scrollbar-thumb {
  background: var(--border-color);
  border-radius: 2px;
}
</style>
