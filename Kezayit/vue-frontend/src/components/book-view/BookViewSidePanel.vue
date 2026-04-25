<script setup lang="ts">
defineProps<{
  visible?: boolean
  toggleButtonEl?: HTMLElement | null
}>()

const emit = defineEmits<{ close: [] }>()
</script>

<template>
  <div class="side-panel-shell" :class="{ 'is-hidden': !visible }" @click="emit('close')">
    <div class="side-panel" @click.stop>
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
  opacity: 1;
  pointer-events: auto;
  transition: opacity 180ms ease;
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
  transition: transform 180ms ease;
  transform: translateX(0);
}

.side-panel-shell.is-hidden {
  opacity: 0;
  pointer-events: none;
}

.side-panel-shell.is-hidden .side-panel {
  transform: translateX(100%);
}
</style>
