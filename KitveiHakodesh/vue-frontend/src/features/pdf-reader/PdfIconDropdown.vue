<script setup lang="ts" generic="T extends string | number">
import { computed, ref, type Component } from 'vue'
import ContextMenu, { type ContextMenuItem } from '@/components/ContextMenu.vue'

/**
 * An icon-only toolbar button that opens a menu of choices (icon + text), as the app's view mode dropdown.
 * The button shows the chosen item's icon; the tooltip names the menu and the chosen item.
 */
export interface IconDropdownOption<V> {
  value: V
  label: string
  icon: Component
}
const props = defineProps<{
  options: IconDropdownOption<T>[]
  /** The chosen value, or null when none applies (the first option's icon shows, not lit). */
  value: T | null
  /** The menu's name; the tooltip reads "<menu>: <chosen item>". */
  menuTitle: string
}>()
const emit = defineEmits<{ select: [value: T] }>()

const shown = computed(() => props.options.find((o) => o.value === props.value) ?? props.options[0]!)
const menuRef = ref<InstanceType<typeof ContextMenu> | null>(null)
const items = computed<ContextMenuItem[]>(() =>
  props.options.map((o) => ({
    type: 'checkbox',
    label: o.label,
    icon: o.icon,
    checked: o.value === props.value,
    onChange: () => {
      emit('select', o.value)
      menuRef.value?.hide()
    },
  })),
)
function open(event: MouseEvent) {
  const rect = (event.currentTarget as HTMLElement).getBoundingClientRect()
  void menuRef.value?.showAtPosition(rect.left, rect.bottom)
}
</script>

<template>
  <button :title="`${menuTitle}: ${shown.label}`" @mousedown.prevent @click="open">
    <component :is="shown.icon" :key="String(shown.value)" />
  </button>
  <ContextMenu ref="menuRef" :items="items" />
</template>

<style scoped>
/* The toolbar's button size and shape (two roots, so the toolbar's scoped styles don't reach this button). */
button {
  display: flex;
  align-items: center;
  justify-content: center;
  width: var(--toolbar-button-size);
  height: var(--toolbar-button-size);
  padding: 6px;
  border-radius: 4px;
  flex-shrink: 0;
}
button svg {
  width: 16px;
  height: 16px;
}
</style>
