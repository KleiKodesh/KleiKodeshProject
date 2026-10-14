<script setup lang="ts" generic="T extends string">
import { ref, computed, nextTick } from 'vue'
import { IconChevronDown20Regular } from '@iconify-prerendered/vue-fluent'
import { useEventListener } from '@vueuse/core'
import { useDropdownClose } from '@/composables/useDropdownClose'

/**
 * Drop-in replacement for a native <select>. The list is teleported to <body> and placed by
 * open() right under its trigger (or above it when there is more room there), capped to the
 * room left, so it is never clipped by an `overflow: hidden` ancestor or by the WebView host
 * window like the native popup is.
 */
export interface AppSelectOption<V extends string> {
  value: V
  label: string
}

const props = defineProps<{ options: AppSelectOption<T>[] }>()
const model = defineModel<T>({ required: true })

const isOpen = ref(false)
const triggerRef = ref<HTMLButtonElement | null>(null)
const listRef = ref<HTMLElement | null>(null)
const listStyle = ref<Record<string, string>>({})
const highlighted = ref(-1)

const selectedLabel = computed(
  () => props.options.find((o) => o.value === model.value)?.label ?? '',
)

// Placement (flip + max-height) is done in open(); the generic fit would shift it off the toggle.
const closer = useDropdownClose(listRef, () => (isOpen.value = false), {
  toggleButton: triggerRef,
  fitToViewport: false,
})

// Placement is computed once on open; close instead of drifting off the trigger.
function closeUnlessInsideList(e: Event) {
  if (isOpen.value && !listRef.value?.contains(e.target as Node)) isOpen.value = false
}
useEventListener(window, 'scroll', closeUnlessInsideList, { capture: true, passive: true })
useEventListener(window, 'resize', closeUnlessInsideList)

const GAP = 2
const MARGIN = 8

async function open() {
  const trigger = triggerRef.value!.getBoundingClientRect()
  listStyle.value = {
    top: '0px',
    left: '0px',
    minWidth: `${trigger.width}px`,
    visibility: 'hidden',
  }
  highlighted.value = props.options.findIndex((o) => o.value === model.value)
  isOpen.value = true
  await nextTick()
  const list = listRef.value
  if (!list) return

  // The app zooms #app but the list lives in <body>; measure the scale between the
  // CSS px we write and the rect px we read, so placement is right at any zoom.
  const probe = list.getBoundingClientRect()
  const scale = probe.width / list.offsetWidth || 1
  const height = probe.height
  const roomBelow = window.innerHeight - trigger.bottom - GAP - MARGIN
  const roomAbove = trigger.top - GAP - MARGIN
  const openUp = height > roomBelow && roomAbove > roomBelow
  const top = openUp ? Math.max(MARGIN, trigger.top - GAP - height) : trigger.bottom + GAP
  // RTL: align the list's right edge with the trigger's right edge.
  const left = Math.max(MARGIN, trigger.right - Math.max(probe.width, trigger.width))

  listStyle.value = {
    top: `${top / scale}px`,
    left: `${left / scale}px`,
    minWidth: `${trigger.width / scale}px`,
    maxHeight: `${(openUp ? roomAbove : roomBelow) / scale}px`,
    overflowY: 'auto',
  }
  await nextTick()
  list.querySelector<HTMLElement>('.selected')?.scrollIntoView({ block: 'nearest' })
}

function toggle() {
  if (closer.justClosed.value) return
  if (isOpen.value) isOpen.value = false
  else open()
}

function choose(value: T) {
  model.value = value
  isOpen.value = false
  triggerRef.value?.focus()
}

function onKeydown(e: KeyboardEvent) {
  if (!isOpen.value) {
    if (['ArrowDown', 'ArrowUp', 'Enter', ' '].includes(e.key)) {
      e.preventDefault()
      open()
    }
    return
  }
  const count = props.options.length
  if (e.key === 'ArrowDown') highlighted.value = (highlighted.value + 1) % count
  else if (e.key === 'ArrowUp') highlighted.value = (highlighted.value - 1 + count) % count
  else if (e.key === 'Enter' || e.key === ' ') {
    if (highlighted.value >= 0) choose(props.options[highlighted.value]!.value)
  } else if (e.key === 'Escape' || e.key === 'Tab') {
    isOpen.value = false
    return
  } else return
  e.preventDefault()
  nextTick(() => listRef.value?.children[highlighted.value]?.scrollIntoView({ block: 'nearest' }))
}
</script>

<template>
  <button
    ref="triggerRef"
    type="button"
    class="app-select"
    :class="{ open: isOpen }"
    @click="toggle"
    @keydown="onKeydown"
  >
    <span class="app-select-label">{{ selectedLabel }}</span>
    <IconChevronDown20Regular class="app-select-chevron" aria-hidden="true" />
    <!-- Inside the button so the component has ONE root (parent classes + scoped styles
         apply); teleported, so in the DOM the list is in <body> and its clicks never reach the button. -->
    <Teleport to="body">
      <div v-if="isOpen" ref="listRef" class="app-select-list" :style="listStyle" role="listbox">
        <div
          v-for="(o, i) in options"
          :key="o.value"
          class="app-select-option"
          :class="{ selected: o.value === model, highlighted: i === highlighted }"
          role="option"
          @mouseenter="highlighted = i"
          @click="choose(o.value)"
        >
          {{ o.label }}
        </div>
      </div>
    </Teleport>
  </button>
</template>

<style scoped>
.app-select {
  display: flex;
  align-items: center;
  gap: 6px;
  height: 30px;
  padding: 0 10px;
  box-sizing: border-box;
  background: var(--bg-primary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  font: inherit;
  font-size: 12px;
  color: var(--text-primary);
  text-align: start;
  cursor: pointer;
  outline: none;
  min-width: 0;
}
.app-select:hover {
  border-color: var(--text-secondary);
}
.app-select:focus-visible,
.app-select.open {
  border-color: var(--accent-color);
}
.app-select-label {
  flex: 1;
  overflow: hidden;
  text-overflow: ellipsis;
  white-space: nowrap;
}
.app-select-chevron {
  flex-shrink: 0;
  width: 14px;
  height: 14px;
  color: var(--text-secondary);
  transition: transform 0.15s;
}
.app-select.open .app-select-chevron {
  transform: rotate(180deg);
}
.app-select-list {
  position: fixed;
  z-index: 10000;
  max-width: calc(100vw - 16px);
  padding: 4px 0;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.18);
  direction: rtl;
  font-size: 12px;
  color: var(--text-primary);
}
.app-select-option {
  padding: 5px 12px;
  white-space: nowrap;
  cursor: pointer;
}
.app-select-option.highlighted {
  background: var(--hover-bg, color-mix(in srgb, var(--accent-color) 10%, transparent));
}
.app-select-option.selected {
  color: var(--accent-color);
  font-weight: 600;
}
</style>
