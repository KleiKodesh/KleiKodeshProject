<script setup lang="ts">
import { computed, shallowRef } from 'vue'
import { storeToRefs } from 'pinia'
import { useEventListener } from '@vueuse/core'
import { IconArrowRight20Regular, IconFolder20Filled } from '@iconify-prerendered/vue-fluent'
import { useBooksDataStore } from '@/stores/booksDataStore'
import { withoutHiddenCategories, type CategoryNode } from '@/features/book-catalog/bookCatalogTree'

// Walks the catalog's folders - the same shelves the catalog page browses - and hands back
// the one the user picks. Folders only: a user-space root is a folder, never a book.
const emit = defineEmits<{ pick: [CategoryNode]; pickAll: []; cancel: [] }>()

const { ROOT } = storeToRefs(useBooksDataStore())
const browseRoot = computed<CategoryNode>(() => ({
  ...ROOT.value,
  children: withoutHiddenCategories(ROOT.value.children),
}))

/** The folders entered so far, below the root. */
const trail = shallowRef<CategoryNode[]>([])
const current = computed(() => trail.value[trail.value.length - 1] ?? browseRoot.value)

function enter(node: CategoryNode) {
  trail.value = [...trail.value, node]
}
function back() {
  trail.value = trail.value.slice(0, -1)
}

useEventListener('keydown', (e: KeyboardEvent) => {
  if (e.key === 'Escape') {
    e.preventDefault()
    emit('cancel')
  }
})
</script>

<template>
  <Teleport to="body">
    <div class="picker-backdrop" dir="rtl" @click.self="emit('cancel')">
      <div class="picker-dialog" role="dialog" :aria-label="$t('userSpace.picker.title')">
        <p class="picker-title">{{ $t('userSpace.picker.title') }}</p>
        <div class="picker-path">
          <button v-if="trail.length" class="bar-btn" :title="$t('userSpace.picker.back')" @click="back">
            <IconArrowRight20Regular />
          </button>
          <span class="picker-path-text">{{ trail.map((n) => n.title).join(' / ') }}</span>
        </div>
        <div class="picker-list">
          <p v-if="!current.children.length" class="picker-empty">{{ $t('userSpace.picker.noSubfolders') }}</p>
          <button v-for="node in current.children" :key="node.id" class="picker-row" @click="enter(node)">
            <IconFolder20Filled class="picker-folder-icon" />
            <span class="picker-row-title">{{ node.title }}</span>
          </button>
        </div>
        <div class="picker-actions">
          <!-- At the top: the whole catalog is one root of its own. -->
          <button v-if="!trail.length" class="picker-ok-btn" @click="emit('pickAll')">{{ $t('userSpace.picker.addWholeCatalog') }}</button>
          <button
            v-if="trail.length"
            class="picker-ok-btn"
            @click="emit('pick', current)"
          >{{ $t('userSpace.picker.addThis', { title: current.title }) }}</button>
          <button class="picker-cancel-btn" @click="emit('cancel')">{{ $t('components.cancel') }}</button>
        </div>
      </div>
    </div>
  </Teleport>
</template>

<style scoped>
/* The shared dialog look (ConfirmDialog), taller for a folder list. */
.picker-backdrop {
  position: fixed;
  inset: 0;
  background: rgba(0, 0, 0, 0.5);
  display: flex;
  align-items: center;
  justify-content: center;
  z-index: 9999;
}
.picker-dialog {
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
  padding: 20px 20px 14px;
  width: 420px;
  max-width: calc(100vw - 32px);
  height: min(520px, calc(100vh - 64px));
  display: flex;
  flex-direction: column;
  gap: 10px;
}
.picker-title {
  margin: 0;
  font-size: 14px;
  font-weight: 600;
  color: var(--text-primary);
}
.picker-path {
  display: flex;
  align-items: center;
  gap: 6px;
  min-height: 24px;
  font-size: 12px;
  color: var(--text-secondary);
}
.picker-path-text {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.picker-list {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  scrollbar-width: thin;
  border: 1px solid var(--border-color);
  border-radius: 4px;
}
.picker-empty {
  padding: 16px;
  font-size: 13px;
  color: var(--text-secondary);
  text-align: center;
}
.picker-row {
  display: flex;
  align-items: center;
  gap: var(--item-gap);
  width: 100%;
  height: 34px;
  padding: 0 10px;
  text-align: start;
  font-size: 13px;
  color: var(--text-primary);
}
.picker-row:hover {
  background: var(--hover-bg);
}
.picker-folder-icon {
  flex-shrink: 0;
  color: var(--status-warning);
}
.picker-row-title {
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.picker-actions {
  display: flex;
  flex-direction: row-reverse;
  justify-content: flex-start;
  gap: 8px;
  padding-top: 10px;
  border-top: 1px solid var(--border-color);
}
.picker-cancel-btn,
.picker-ok-btn {
  height: 30px;
  padding: 0 14px;
  font-size: 12px;
  border-radius: 4px;
}
.picker-cancel-btn {
  border: 1px solid var(--border-color);
  background: var(--control-bg);
  color: var(--text-primary);
}
.picker-cancel-btn:hover {
  background: var(--control-bg-hover);
}
.picker-ok-btn {
  max-width: 260px;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
  color: var(--accent-color, #3478f6);
  border: 1px solid color-mix(in srgb, var(--accent-color, #3478f6) 40%, transparent);
  background: color-mix(in srgb, var(--accent-color, #3478f6) 8%, transparent);
}
.picker-ok-btn:hover {
  background: color-mix(in srgb, var(--accent-color, #3478f6) 16%, transparent);
}
</style>
