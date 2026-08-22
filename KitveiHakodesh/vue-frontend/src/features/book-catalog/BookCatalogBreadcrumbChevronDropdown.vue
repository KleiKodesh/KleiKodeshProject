<script setup lang="ts">
import { ref, computed } from 'vue'
import { IconChevronDown16Regular } from '@iconify-prerendered/vue-fluent'
import { useDropdownClose } from '@/composables/useDropdownClose'
import type { CategoryNode } from '@/features/book-catalog/bookCatalogTree'

const props = defineProps<{
  /** The node whose children are listed in this dropdown. */
  parentNode: CategoryNode
  /** The currently active child node (highlighted in the list). */
  activeChildId: number
}>()

const emit = defineEmits<{ select: [node: CategoryNode] }>()

const isOpen = ref(false)
const wrapperRef = ref<HTMLElement | null>(null)
const dropdownRef = ref<HTMLElement | null>(null)
const toggleButtonRef = ref<HTMLElement | null>(null)

// Position of the teleported dropdown in viewport coordinates
const dropdownTop = ref(0)
const dropdownLeft = ref(0)

// Target is the always-mounted wrapper div so useDropdownClose has a stable element
// to attach to even when the teleported dropdown is destroyed (v-if false).
// The teleported dropdown is passed as ignore so clicks inside it don't count as outside.
const { justClosed } = useDropdownClose(wrapperRef, () => (isOpen.value = false), {
  toggleButton: toggleButtonRef,
  ignore: [dropdownRef],
})

function onToggle() {
  if (justClosed.value) return
  if (!isOpen.value) {
    // Measure button position before opening so the teleported dropdown lands correctly
    const rect = toggleButtonRef.value?.getBoundingClientRect()
    if (rect) {
      dropdownTop.value = rect.bottom + 2
      dropdownLeft.value = rect.right
    }
  }
  isOpen.value = !isOpen.value
}

function onSelectItem(node: CategoryNode) {
  isOpen.value = false
  emit('select', node)
}

const hasChildren = computed(() => props.parentNode.children.length > 0)
</script>

<template>
  <div v-if="hasChildren" class="chevron-wrapper" ref="wrapperRef">
    <button
      ref="toggleButtonRef"
      class="chevron-button"
      :class="{ open: isOpen }"
      :title="isOpen ? 'סגור' : 'הצג תיקיות'"
      @click.stop="onToggle"
    >
      <IconChevronDown16Regular />
    </button>

    <Teleport to="body">
      <div
        v-if="isOpen"
        ref="dropdownRef"
        class="chevron-dropdown"
        :style="{ top: dropdownTop + 'px', left: dropdownLeft + 'px' }"
      >
        <button
          v-for="child in parentNode.children"
          :key="child.id"
          class="chevron-dropdown-item"
          :class="{ active: child.id === activeChildId }"
          @click="onSelectItem(child)"
        >
          {{ child.title }}
        </button>
      </div>
    </Teleport>
  </div>

  <span v-else class="chevron-static">
    <IconChevronDown16Regular />
  </span>
</template>

<style scoped>
.chevron-wrapper {
  display: inline-flex;
  align-items: center;
  flex-shrink: 0;
}

.chevron-button {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 22px;
  border-radius: 4px;
  color: var(--text-secondary);
  opacity: 0.6;
  padding: 0;
  transition: opacity 100ms, color 100ms;
}

.chevron-button:hover,
.chevron-button.open {
  opacity: 1;
  color: var(--text-primary);
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.chevron-button svg {
  width: 12px;
  height: 12px;
  transition: transform 150ms;
  transform: rotate(90deg);
}

.chevron-button.open svg {
  transform: rotate(0deg);
}

.chevron-static {
  display: inline-flex;
  align-items: center;
  justify-content: center;
  width: 18px;
  height: 22px;
  flex-shrink: 0;
  color: var(--text-secondary);
  opacity: 0.4;
}

.chevron-static svg {
  width: 12px;
  height: 12px;
  transform: rotate(90deg);
}
</style>

<style>
/* Unscoped — the dropdown is teleported to <body> and lives outside this component's scope */
.chevron-dropdown {
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

.chevron-dropdown-item {
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

.chevron-dropdown-item:hover {
  background: color-mix(in srgb, var(--text-primary) 8%, transparent);
}

.chevron-dropdown-item.active {
  color: var(--accent-color);
  font-weight: 600;
}
</style>
