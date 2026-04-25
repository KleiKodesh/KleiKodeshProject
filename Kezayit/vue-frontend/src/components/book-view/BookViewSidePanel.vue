<script setup lang="ts">
import { computed, ref } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'

const props = defineProps<{
  visible?: boolean
  toggleButtonEl?: HTMLElement | null
}>()

const emit = defineEmits<{ close: [] }>()

const panelRef = ref<HTMLElement | null>(null)

useDropdownClose(
  panelRef,
  () => emit('close'),
  { toggleButton: computed(() => props.toggleButtonEl ?? null), closeOnBlur: false },
)
</script>

<template>
  <div ref="panelRef" class="side-panel" :class="{ 'is-hidden': !visible }">
    <slot />
  </div>
</template>

<style scoped>
.side-panel {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  z-index: 100;
  display: flex;
  flex-direction: column;
  width: fit-content;
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
  overflow: hidden;
  --tree-bg: var(--bg-secondary);
  transition: transform 180ms ease;
  transform: translateX(0);
  pointer-events: auto;
}

.side-panel.is-hidden {
  transform: translateX(100%);
  pointer-events: none;
}
</style>
