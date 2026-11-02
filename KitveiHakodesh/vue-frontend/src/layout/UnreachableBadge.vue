<script setup lang="ts">
import { IconLinkDismiss16Filled } from '@iconify-prerendered/vue-fluent'

// Unreachable destination: a small broken-link glyph on the icon's right corner
// The parent must be position: relative.
</script>

<template>
  <span class="unreachable-badge" aria-hidden="true">
    <IconLinkDismiss16Filled />
  </span>
</template>

<style scoped>
.unreachable-badge {
  position: absolute;
  bottom: -4px;
  right: -4px;
  display: inline-flex;
  color: #c42b1c;
  font-size: 12px;
  line-height: 0;
  pointer-events: none;
}

.unreachable-badge svg {
  color: inherit;
}
</style>

