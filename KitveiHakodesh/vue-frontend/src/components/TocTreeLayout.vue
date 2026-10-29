<script setup lang="ts">
import { nextTick, onMounted, ref, watch } from 'vue'
import MessageBanner from './MessageBanner.vue'

/**
 * The table-of-contents panel's frame: the tree(s) in the default slot, the search box under
 * them (type="search": its own clear X), and the loading / error states. The book view's TOC and
 * the FastDocView outline both use it, so they look the same by construction. `query` is the
 * search box's model; arrow keys / Tab in the box ask the page to focus the list (`focusList`).
 */
const props = withDefaults(defineProps<{ loading?: boolean; error?: string | null; dir?: 'rtl' | 'ltr' }>(), { dir: 'rtl' })
const query = defineModel<string>('query', { default: '' })
const emit = defineEmits<{ focusList: [] }>()

const searchRef = ref<HTMLInputElement | null>(null)
// The panel mounts fresh each time it opens (v-if), so onMounted focuses the box; the watcher
// covers a TOC whose data arrives after mount.
onMounted(() => {
  if (!props.loading) nextTick(() => searchRef.value?.focus({ preventScroll: true }))
})
watch(
  () => props.loading,
  (busy) => {
    if (!busy) nextTick(() => searchRef.value?.focus({ preventScroll: true }))
  },
)
</script>

<template>
  <div :dir="dir" class="toc-tree">
    <div v-if="loading" class="toc-state ui-chrome">{{ $t('bookView.loading') }}</div>
    <div v-else-if="error" class="toc-state ui-chrome">
      <MessageBanner :message="error" :dismissible="false" />
    </div>
    <template v-else>
      <slot />
      <div class="toc-search">
        <div class="search-inner">
          <input
            ref="searchRef"
            v-model="query"
            type="search"
            class="search-input"
            :placeholder="$t('components.searchPlaceholder')"
            @keydown.up.prevent="emit('focusList')"
            @keydown.down.prevent="emit('focusList')"
            @keydown.tab.prevent="emit('focusList')"
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
</style>
