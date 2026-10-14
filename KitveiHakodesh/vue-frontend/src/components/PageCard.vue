<script setup lang="ts">
/**
 * The setup-wizard card frame: a centred, bordered card filling the page height.
 * `header` slot sits above a divider; the default slot is the scrollable body;
 * optional `footer` slot sits below a divider.
 */
</script>

<template>
  <div class="page-card-outer">
    <div class="page-card">
      <div v-if="$slots.header" class="page-card-header"><slot name="header" /></div>
      <div class="page-card-body"><slot /></div>
      <div v-if="$slots.footer" class="page-card-footer"><slot name="footer" /></div>
    </div>
  </div>
</template>

<style scoped>
.page-card-outer {
  flex: 1;
  min-height: 0;
  width: 100%;
  max-width: 560px;
  margin: 0 auto;
  padding: clamp(4px, 3vh, 20px) 16px;
  box-sizing: border-box;
  display: flex;
  flex-direction: column;
}
.page-card {
  flex: 1;
  min-height: 0;
  display: flex;
  flex-direction: column;
  overflow: hidden;
  background: var(--bg-secondary);
  border: 1px solid var(--border-color);
  border-radius: 8px;
}
.page-card-header {
  flex-shrink: 0;
  border-bottom: 1px solid var(--border-color);
}
.page-card-body {
  flex: 1;
  min-height: 0;
  overflow-y: auto;
  overflow-x: hidden;
  display: flex;
  flex-direction: column;
}
.page-card-footer {
  flex-shrink: 0;
  border-top: 1px solid var(--border-color);
}

/* Narrow (phone-width) windows: the card fills the page edge to edge, as in the setup wizard */
@media (max-width: 600px) {
  .page-card-outer {
    max-width: 100%;
    padding: 0;
  }
  .page-card {
    border: none;
    border-radius: 0;
  }
}
</style>
