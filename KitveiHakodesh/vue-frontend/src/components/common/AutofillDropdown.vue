<script setup lang="ts">
/**
 * Popup UI for useAutofill — styled like the browser's native single-field
 * autocomplete bubble: a rounded, soft-shadowed Fluent surface with a connector
 * notch pointing at the field, medium rows, a filled active row, and the
 * un-typed completion of each match shown in bold.
 *
 * Teleported to <body> and fixed-positioned so it escapes any overflow/stacking
 * context. Flips above the input when there isn't room below (e.g. the FTS bar
 * is docked at the bottom of the page); the notch flips to keep facing the field.
 *
 * <AutofillDropdown :controller="af" />
 */
import { computed, watch, nextTick, ref } from 'vue'
import { useElementBounding, useWindowSize, onClickOutside } from '@vueuse/core'
import { IconDismiss12Regular } from '@iconify-prerendered/vue-fluent'
import type { AutofillController } from '@/composables/useAutofill'

const props = defineProps<{ controller: AutofillController }>()
const af = props.controller

const inputElRef = computed(() => af.inputEl.value)
const { top, left, width, height } = useElementBounding(inputElRef)
const { width: winW, height: winH } = useWindowSize()

// The FTS input is flex:1 (fills the whole bar), so matching its width makes the
// bubble absurdly wide. Instead size to content with a cap, anchored to the
// field's leading (RTL: right) edge.
const MAX_WIDTH = 460

const bubbleRef = ref<HTMLElement | null>(null)
const listRef = ref<HTMLElement | null>(null)

const GAP = 9 // leaves room for the ~6px notch to almost touch the field
const MARGIN = 8
const MAX_HEIGHT = 200 // ~5 rows, then scroll

const spaceBelow = computed(() => winH.value - (top.value + height.value) - GAP - MARGIN)
const spaceAbove = computed(() => top.value - GAP - MARGIN)
const openUp = computed(() => spaceBelow.value < MAX_HEIGHT && spaceAbove.value > spaceBelow.value)
const listMaxHeight = computed(() =>
  Math.max(80, Math.min(MAX_HEIGHT, openUp.value ? spaceAbove.value : spaceBelow.value)),
)

const style = computed(() => {
  // Anchor the bubble's right edge to the input's right edge (RTL start); width
  // is content-driven (see CSS) but never wider than the field or MAX_WIDTH.
  const base = {
    position: 'fixed' as const,
    right: `${Math.max(0, winW.value - (left.value + width.value))}px`,
    maxWidth: `${Math.min(width.value, MAX_WIDTH)}px`,
  }
  return openUp.value
    ? { ...base, bottom: `${winH.value - top.value + GAP}px` }
    : { ...base, top: `${top.value + height.value + GAP}px` }
})

// Bold the un-typed completion: prefix (what you typed) normal, remainder bold.
const typedLen = computed(() => af.query.value.trim().length)
function head(item: string) {
  return typedLen.value ? item.slice(0, typedLen.value) : item
}
function tail(item: string) {
  return typedLen.value ? item.slice(typedLen.value) : ''
}

onClickOutside(bubbleRef, () => af.onBlur(), { ignore: [inputElRef] })

watch(
  () => af.activeIndex.value,
  async (i) => {
    if (i < 0) return
    await nextTick()
    listRef.value
      ?.querySelectorAll<HTMLElement>('.autofill-item')[i]
      ?.scrollIntoView({ block: 'nearest' })
  },
)
</script>

<template>
  <Teleport to="body">
    <div
      v-if="af.open.value && af.suggestions.value.length"
      ref="bubbleRef"
      class="autofill-bubble"
      :class="openUp ? 'up' : 'down'"
      :style="style"
    >
      <div class="autofill-header">
        <span class="autofill-title">חיפושים אחרונים</span>
        <button
          class="autofill-close"
          type="button"
          tabindex="-1"
          title="סגור"
          aria-label="סגור"
          @mousedown.prevent.stop
          @click.stop="af.onBlur()"
        >
          <IconDismiss12Regular />
        </button>
      </div>
      <ul ref="listRef" class="autofill-list" :style="{ maxHeight: `${listMaxHeight}px` }" role="listbox">
        <li
          v-for="(item, i) in af.suggestions.value"
          :key="item"
          class="autofill-item"
          :class="{ active: i === af.activeIndex.value }"
          role="option"
          :aria-selected="i === af.activeIndex.value"
          @mousedown.prevent="af.commit(item)"
          @mouseenter="af.setActive(i)"
        >
          <span class="af-head">{{ head(item) }}</span><span class="af-tail">{{ tail(item) }}</span>
        </li>
      </ul>
      <span class="autofill-notch" aria-hidden="true" />
    </div>
  </Teleport>
</template>

<style scoped>
.autofill-bubble {
  z-index: 10000;
  width: max-content; /* size to the longest suggestion, capped by inline max-width */
  min-width: 200px;
  /* Padding here (not on the list) insets the scroll list — and its scrollbar —
     from the rounded corners, so the scrollbar track isn't clipped by the radius.
     The notch side gets extra padding so no row sits under the notch. */
  padding: 6px;
  overflow: visible; /* let the notch stick out */
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 14px;
  box-shadow:
    0 10px 30px rgba(0, 0, 0, 0.34),
    0 2px 8px rgba(0, 0, 0, 0.2);
  direction: rtl;
}
.autofill-list {
  margin: 0;
  padding: 0;
  list-style: none;
  overflow-y: auto;
  overflow-x: hidden;
  overscroll-behavior: contain;
  scrollbar-width: thin;
  scrollbar-color: color-mix(in srgb, var(--text-secondary) 30%, transparent) transparent;
}
.autofill-list::-webkit-scrollbar {
  width: 4px;
}
.autofill-list::-webkit-scrollbar-track {
  background: transparent;
}
.autofill-list::-webkit-scrollbar-thumb {
  background: color-mix(in srgb, var(--text-secondary) 30%, transparent);
  border-radius: 4px;
}
.autofill-list::-webkit-scrollbar-thumb:hover {
  background: color-mix(in srgb, var(--text-secondary) 50%, transparent);
}
.autofill-item {
  display: block;
  height: 26px;
  line-height: 26px;
  padding: 0 12px;
  border-radius: 8px;
  font-size: 12px;
  color: var(--text-primary);
  cursor: default;
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.autofill-item + .autofill-item {
  margin-top: 1px;
}
.autofill-item.active {
  background: color-mix(in srgb, var(--text-primary) 12%, transparent);
}
.af-head {
  opacity: 0.85;
}
.af-tail {
  font-weight: 700;
}

/* Header — a title on the leading (RTL: right) side and a close button on the
   trailing side, with a separator line beneath it before the list. */
.autofill-header {
  display: flex;
  align-items: center;
  justify-content: space-between;
  gap: 8px;
  height: 24px;
  padding: 0 8px 0 4px;
  margin-bottom: 5px;
  border-bottom: 1px solid var(--border-color);
}
.autofill-title {
  font-size: 11px;
  font-weight: 600;
  color: var(--text-secondary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
}
.autofill-close {
  display: flex;
  align-items: center;
  justify-content: center;
  flex-shrink: 0;
  width: 18px;
  height: 18px;
  border: none;
  background: none;
  border-radius: 4px;
  color: var(--text-secondary);
  cursor: pointer;
  opacity: 0.7;
}
.autofill-close:hover {
  opacity: 1;
  background: color-mix(in srgb, var(--text-primary) 12%, transparent);
  color: var(--text-primary);
}
.autofill-close svg {
  width: 12px;
  height: 12px;
}

/* Reserve room on the notch side so no row ever sits under the notch's
   inner half (which paints bubble-bg and would otherwise cover the row). */
.autofill-bubble.up {
  padding-bottom: 13px;
}
.autofill-bubble.down {
  padding-top: 13px;
}

/* Connector notch — a rotated square that reads as a triangle, with the two
   outward-facing edges bordered to blend into the bubble outline. */
.autofill-notch {
  position: absolute;
  z-index: 1; /* paint above the list */
  width: 12px;
  height: 12px;
  right: 20px;
  background: var(--bg-secondary);
  transform: rotate(45deg);
}
.autofill-bubble.up .autofill-notch {
  bottom: -6px;
  border-right: 1px solid var(--border-color);
  border-bottom: 1px solid var(--border-color);
}
.autofill-bubble.down .autofill-notch {
  top: -6px;
  border-left: 1px solid var(--border-color);
  border-top: 1px solid var(--border-color);
}
</style>
