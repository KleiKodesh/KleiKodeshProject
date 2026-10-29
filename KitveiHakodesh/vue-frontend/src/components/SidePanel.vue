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
  { toggleButton: computed(() => props.toggleButtonEl ?? null), fitToViewport: false },
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
  inset-inline-end: 0;
  inset-inline-start: 0;
  z-index: 100;
  background: rgba(0, 0, 0, 0.28);
}

.side-panel-overlay {
  position: absolute;
  top: 0;
  inset-inline-start: 0;
  bottom: 0;
}

/* ── Shared panel styles ───────────────────────────────────────────────────── */
.side-panel {
  display: flex;
  flex-direction: column;
  width: fit-content;
  /* Same surface as the page toolbar beside it. */
  background: var(--bg-toolbar);
  overflow: hidden;
  --tree-bg: var(--bg-toolbar);
}

/* ── Inline mode ───────────────────────────────────────────────────────────── */
/* No edge border: inline mode is followed by a resize sash, which draws the line.
   Painting one here too stacked a 1px border against the 1px sash - a 2px line that
   read as the old thick separator, and hovering lit only half of it. */
.side-panel-inline {
  height: 100%;
  flex-shrink: 0;
  /* Default cap before the reader drags the sash: long titles wrap instead of widening the
     panel without limit. A dragged width (useSidePanelLayout, 0.1..0.5) lifts it. */
  max-width: 30%;
}

/* Overlay mode floats over the content with no sash beside it, so it needs its
   own edge. */
.side-panel-overlay {
  border-inline-end: 1px solid var(--border-color);
}
</style>
