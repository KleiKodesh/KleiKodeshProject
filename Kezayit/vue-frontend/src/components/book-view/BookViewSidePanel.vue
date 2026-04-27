<script setup lang="ts">
import { ref } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'

const props = defineProps<{
  toggleButtonEl?: HTMLElement | null
}>()

const emit = defineEmits<{ close: [] }>()

const panelRef = ref<HTMLElement | null>(null)

useDropdownClose(
  panelRef,
  () => emit('close'),
  { toggleButton: () => props.toggleButtonEl ?? null },
)
</script>

<template>
  <div class="side-panel-shell" @click.self="emit('close')">
    <div ref="panelRef" class="side-panel">
      <slot />
    </div>
  </div>
</template>

<style scoped>
.side-panel-shell {
  position: absolute;
  top: 0;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 100;
  background: rgba(0, 0, 0, 0.28);
}

.side-panel {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
  display: flex;
  flex-direction: column;
  width: fit-content;
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
  overflow: hidden;
  --tree-bg: var(--bg-secondary);
}
</style>
