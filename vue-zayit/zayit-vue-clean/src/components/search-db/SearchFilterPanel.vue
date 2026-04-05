<script setup lang="ts">
import { computed } from 'vue'
import { IconMinimize20Regular } from '@iconify-prerendered/vue-fluent'
import { useBooksDataStore } from '@/stores/booksDataStore'
import LoadingAnimation from '@/components/common/LoadingAnimation.vue'
import SearchFilterNode from './SearchFilterNode.vue'
import type { CategoryNode } from '@/components/books-fs/booksCategoryTree'

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
</script>

<template>
  <div class="panel">
    <div class="panel-header">
      <div
        class="header-check"
        :class="{ checked: isAllChecked, indet: isIndet }"
        @click="isAllChecked ? emit('uncheckAll') : emit('checkAll')"
      >
        <span class="check-col">
          <span class="check-mark">✓</span>
          <span class="dash-mark">–</span>
        </span>
        <span class="panel-title">הצג הכל</span>
      </div>
      <button class="close-btn c-pointer hover-bg" title="סגור" @click.stop="emit('close')">
        <IconMinimize20Regular />
      </button>
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
  min-width: 180px;
  max-width: 300px;
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
}
.panel-header {
  display: flex;
  align-items: center;
  height: 26px;
  border-bottom: 1px solid var(--border-color);
  flex-shrink: 0;
}
.header-check {
  display: flex;
  align-items: center;
  flex: 1;
  height: 26px;
  cursor: pointer;
  font-size: 12px;
  font-weight: 600;
  color: var(--text-primary);
}
.header-check:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}
.check-col {
  width: 28px;
  height: 26px;
  flex-shrink: 0;
  display: flex;
  align-items: center;
  justify-content: center;
  font-size: 11px;
  color: var(--accent-color);
}
.check-mark {
  display: none;
}
.dash-mark {
  display: none;
}
.header-check.checked .check-mark {
  display: block;
}
.header-check.indet .dash-mark {
  display: block;
}
.panel-title {
  flex: 1;
}
.close-btn {
  display: flex;
  align-items: center;
  justify-content: center;
  width: 26px;
  height: 26px;
  flex-shrink: 0;
  border-radius: 0;
  color: var(--text-secondary);
}
.panel-body {
  flex: 1;
  overflow-y: auto;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}
</style>
