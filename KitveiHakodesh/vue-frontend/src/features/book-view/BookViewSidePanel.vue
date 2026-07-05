<script setup lang="ts">
import { ref, computed, useAttrs } from 'vue'
import { useDropdownClose } from '@/composables/useDropdownClose'

const props = defineProps<{
  toggleButtonEl?: HTMLElement | null
  isOverlay: boolean
}>()

const emit = defineEmits<{ close: [] }>()

// Fragment root — attrs cannot be inherited automatically.
defineOptions({ inheritAttrs: false })
const attrs = useAttrs()

const panelRef = ref<HTMLElement | null>(null)

// Click-outside close only applies in overlay mode.
useDropdownClose(
  panelRef,
  () => { if (props.isOverlay) emit('close') },
  { toggleButton: computed(() => props.toggleButtonEl ?? null) },
)
</script>

<template>
  <!-- Overlay mode: full-screen backdrop + floating panel -->
  <template v-if="isOverlay">
    <div class="side-panel-shell" @click.self="emit('close')">
      <div ref="panelRef" class="side-panel side-panel-overlay">
        <slot />
      </div>
    </div>
  </template>

  <!-- Inline mode: plain column, no backdrop. Accepts attrs (e.g. :style width from parent). -->
  <template v-else>
    <div ref="panelRef" v-bind="attrs" class="side-panel side-panel-inline">
      <slot />
    </div>
  </template>
</template>

<style scoped>
/* ── Overlay mode ──────────────────────────────────────────────────────────── */
.side-panel-shell {
  position: absolute;
  top: 0;
  bottom: 0;
  left: 0;
  right: 0;
  z-index: 100;
  background: rgba(0, 0, 0, 0.28);
}

.side-panel-overlay {
  position: absolute;
  top: 0;
  right: 0;
  bottom: 0;
}

/* ── Shared panel styles ───────────────────────────────────────────────────── */
.side-panel {
  display: flex;
  flex-direction: column;
  width: fit-content;
  background: var(--bg-secondary);
  border-left: 1px solid var(--border-color);
  overflow: hidden;
  --tree-bg: var(--bg-secondary);
}

/* ── Inline mode ───────────────────────────────────────────────────────────── */
.side-panel-inline {
  height: 100%;
  flex-shrink: 0;
}
</style>
