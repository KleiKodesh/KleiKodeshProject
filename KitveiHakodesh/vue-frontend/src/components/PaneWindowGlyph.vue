<script setup lang="ts">
/**
 * The pop-out / pop-in icons of the Word add-in's panes (KleiKodeshVsto
 * OfficePaneChrome/Fluent.cs: PopOut U+E8A7, PopIn U+E944), drawn from the same
 * Windows icon font, so the app's "own window" buttons look exactly like the add-in's.
 */
defineProps<{ kind: 'pop-out' | 'pop-in' }>()
</script>

<template>
  <span class="pane-window-glyph" aria-hidden="true">{{ kind === 'pop-out' ? '' : '' }}</span>
</template>

<style scoped>
.pane-window-glyph {
  font-family: 'Segoe Fluent Icons', 'Segoe MDL2 Assets';
  font-size: 12px;
  line-height: 1;
  font-style: normal;
  font-weight: normal;
}
</style>
