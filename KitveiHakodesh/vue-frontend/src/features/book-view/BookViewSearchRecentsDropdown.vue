<script setup lang="ts">
/**
 * The book-view search bar's recent-searches listbox.
 *
 * A classic combobox popup, not the floating bubble the search page uses: a plain
 * rectangular list sitting flush under the field, no notch, no drop shadow, no rounded
 * card. The WAI-ARIA combobox reference is exactly this — an absolutely positioned
 * listbox anchored to the field, sized to it, with a flat border and a filled row for
 * the active option. The bubble reads as a floating panel that happens to contain
 * options; this reads as the field's own list, which is what it is.
 *
 * It is also the manageable variant: an × on each row drops one query, and a footer
 * clears the history. The search page's popup is read-only.
 *
 * Rendered inline (no Teleport) — it is part of the field, so it lives in the bar's own
 * stacking context and moves with it.
 */
import { computed, watch, nextTick, ref } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'
import { IconDismiss12Regular } from '@iconify-prerendered/vue-fluent'
import type { RecentSearchesController } from '@/composables/useRecentSearches'

const props = defineProps<{
  controller: RecentSearchesController
  /** True when the bar is docked at the bottom, so the list opens upward. */
  openUp?: boolean
  /**
   * DOM id for the listbox, so the field's aria-controls can point at it. Supplied by
   * the bar rather than fixed here: split view mounts two bars at once, and a constant
   * id would duplicate across them and aim one field at the other pane's list.
   */
  listboxId: string
}>()
const recents = props.controller

const RECENTS_TITLE = 'חיפושים אחרונים'
const REMOVE_LABEL = 'מחדש'

const listboxRef = ref<HTMLElement | null>(null)
const inputElRef = computed(() => recents.inputEl.value)

useDropdownClose(listboxRef, () => recents.onBlur(), { ignore: [inputElRef] })

// Removing the last row leaves nothing to show, so close rather than sit there empty.
function removeItem(item: string) {
  recents.remove(item)
  if (!recents.suggestions.value.length) recents.onBlur()
}

watch(
  () => recents.activeIndex.value,
  async (i) => {
    if (i < 0) return
    await nextTick()
    listboxRef.value?.querySelectorAll<HTMLElement>('.option')[i]?.scrollIntoView({ block: 'nearest' })
  },
)
</script>

<template>
  <div
    v-if="recents.open.value && recents.suggestions.value.length"
    ref="listboxRef"
    class="combo-popup"
    :class="{ 'open-up': openUp }"
  >
    <div class="combo-label">{{ RECENTS_TITLE }}</div>
    <ul :id="listboxId" class="combo-listbox" role="listbox" :aria-label="RECENTS_TITLE">
      <li
        v-for="(item, i) in recents.suggestions.value"
        :key="item"
        class="option"
        :class="{ active: i === recents.activeIndex.value }"
        role="option"
        :aria-selected="i === recents.activeIndex.value"
        @mousedown.prevent="recents.commit(item)"
        @mouseenter="recents.setActive(i)"
      >
        <span class="option-text">{{ item }}</span>
        <button
          class="option-remove"
          type="button"
          tabindex="-1"
          :title="REMOVE_LABEL"
          :aria-label="REMOVE_LABEL"
          @mousedown.prevent.stop
          @click.stop="removeItem(item)"
        >
          <IconDismiss12Regular />
        </button>
      </li>
    </ul>
  </div>
</template>

<style scoped>
/* Styled as the mode dropdown beside it: same corner, padding, shadow and offset, so
   the bar's two menus read as one family. Sized to the input alone — it is that field's
   list, so it neither spans the buttons nor grows to fit its content. */
.combo-popup {
  position: absolute;
  z-index: 100;
  top: calc(100% + 6px);
  inset-inline: 0;
  padding: 2px;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 4px;
  box-shadow: 0 4px 12px rgba(0, 0, 0, 0.25);
  box-sizing: border-box;
}
.combo-popup.open-up {
  top: auto;
  bottom: calc(100% + 6px);
}

.combo-label {
  padding: 4px 8px 3px;
  font-size: 10px;
  color: var(--text-secondary);
  border-bottom: 1px solid var(--border-color);
}

.combo-listbox {
  margin: 0;
  padding: 0;
  list-style: none;
  max-height: 200px;
  overflow-y: auto;
  overscroll-behavior: contain;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
}

/* Same row shape as .mode-option: 28px, 8px inset, 4px corner, 12px text. */
.option {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 28px;
  padding: 0 8px;
  border-radius: 4px;
  font-size: 12px;
  color: var(--text-primary);
  cursor: default;
  white-space: nowrap;
}
.option-text {
  flex: 1;
  min-width: 0;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.option:hover { background: color-mix(in srgb, var(--text-primary) 6%, transparent); }
.option.active { background: color-mix(in srgb, var(--text-primary) 10%, transparent); }

.option-remove {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 16px;
  height: 16px;
  border: none;
  background: none;
  border-radius: 3px;
  color: var(--text-secondary);
  cursor: pointer;
  /* Only on the row under the pointer: an × on every row at once is visual noise. */
  opacity: 0;
}
.option:hover .option-remove,
.option.active .option-remove { opacity: 0.7; }
.option-remove:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--text-primary) 12%, transparent);
  color: var(--text-primary);
}
.option-remove svg { width: 12px; height: 12px; }

</style>
