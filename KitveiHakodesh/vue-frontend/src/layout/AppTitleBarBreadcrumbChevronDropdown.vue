<script setup lang="ts">
import { ref, nextTick } from 'vue'
import { IconChevronDown16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'

export interface BreadcrumbItem {
  id: number
  text: string
}

const props = defineProps<{
  /** All siblings to list in the dropdown. */
  siblings: BreadcrumbItem[]
  /** The currently active sibling id (highlighted). Null if none matches. */
  activeSiblingId: number | null
}>()

const emit = defineEmits<{ select: [item: BreadcrumbItem] }>()

const isOpen = ref(false)
const wrapperRef = ref<HTMLElement | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

const dropdownTop = ref(0)
const dropdownLeft = ref(0)

const { justClosed } = useDropdownClose(wrapperRef, () => (isOpen.value = false), {
  toggleButton: toggleButtonRef,
  ignore: [dropdownRef],
})

function onToggle(event: MouseEvent) {
  event.stopPropagation()
  if (justClosed.value) return
  if (!isOpen.value) {
    const rect = toggleButtonRef.value?.getBoundingClientRect()
    if (rect) {
      dropdownTop.value = rect.bottom + 2
      dropdownLeft.value = rect.right
    }
  }
  isOpen.value = !isOpen.value
  if (isOpen.value) {
    nextTick(() => {
      const active = dropdownRef.value?.querySelector('.toc-chevron-dropdown-item.active')
      active?.scrollIntoView({ block: 'nearest', behavior: 'instant' })
    })
  }
}

function onSelectItem(item: BreadcrumbItem, event: MouseEvent) {
  event.stopPropagation()
  isOpen.value = false
  emit('select', item)
}
</script>

<template>
  <div v-if="siblings.length > 0" ref="wrapperRef" class="toc-chevron-wrapper">
    <button
      ref="toggleButtonRef"
      class="toc-chevron-button"
      :class="{ open: isOpen }"
      title="פתח תפריט"
      @click="onToggle"
    >
      <IconChevronDown16Regular />
    </button>

    <Teleport to="body">
      <div
        v-if="isOpen"
        ref="dropdownRef"
        class="toc-chevron-dropdown"
        :style="{ top: dropdownTop + 'px', left: dropdownLeft + 'px' }"
        @click.stop
      >
        <button
          v-for="item in siblings"
          :key="item.id"
          class="toc-chevron-dropdown-item"
          :class="{ active: item.id === activeSiblingId }"
          @click="onSelectItem(item, $event)"
        >
          {{ item.text }}
        </button>
      </div>
    </Teleport>
  </div>

  <span v-else class="toc-chevron-static">
    <IconChevronDown16Regular />
  </span>
</template>

<style scoped>
.toc-chevron-wrapper {
  display: contents;
}

.toc-chevron-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  line-height: 1;
  border-radius: 3px;
  color: var(--text-secondary);
  opacity: 0.6;
  padding: 0;
  transition: opacity 100ms, color 100ms;
  flex-shrink: 0;
}

.toc-chevron-button:hover,
.toc-chevron-button.open {
  opacity: 1;
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.toc-chevron-button svg {
  width: 12px;
  height: 12px;
  transition: transform 150ms;
  transform: rotate(90deg);
}

.toc-chevron-button.open svg {
  transform: rotate(0deg);
}

.toc-chevron-static {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 18px;
  align-self: center;
  flex-shrink: 0;
  color: var(--text-secondary);
  opacity: 0.4;
  margin-inline: 1px;
}

.toc-chevron-static svg {
  width: 12px;
  height: 12px;
  transform: rotate(90deg);
}
</style>

<style>
/* Unscoped — teleported to <body>, lives outside this component's scope */
.toc-chevron-dropdown {
  position: fixed;
  transform: translateX(-100%);
  min-width: 160px;
  max-width: 280px;
  max-height: 320px;
  overflow-y: auto;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 6px;
  box-shadow: 0 4px 16px rgba(0, 0, 0, 0.3);
  z-index: 9999;
  padding: 4px 0;
  scrollbar-width: thin;
  scrollbar-color: var(--border-color) transparent;
  direction: rtl;
}

.toc-chevron-dropdown-item {
  display: block;
  width: 100%;
  height: 26px;
  padding: 0 10px;
  text-align: right;
  font-size: 12px;
  color: var(--text-primary);
  white-space: nowrap;
  overflow: hidden;
  text-overflow: ellipsis;
  line-height: 26px;
  border-radius: 0;
  background: none;
  border: none;
  cursor: pointer;
}

.toc-chevron-dropdown-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.toc-chevron-dropdown-item.active {
  color: var(--accent-color);
  font-weight: 600;
}
</style>
