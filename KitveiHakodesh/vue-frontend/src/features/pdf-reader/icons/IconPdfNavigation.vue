<!-- Navigation in the document (the side panel): the old PDF.js viewer's toggle - a window with
     a list pane of three lines - redrawn in the Fluent 20px set (1px outline, rounded corners).
     Mirrored in RTL, where the panel opens on the right. Filled fills the pane, lines cut out. -->
<script setup lang="ts">
defineProps<{ filled?: boolean }>()
const maskId = `pdf-nav-mask-${Math.random().toString(36).slice(2)}`
</script>

<template>
  <svg class="dir-icon-ltr" viewBox="0 0 20 20" width="1em" height="1em" fill="none" stroke="currentColor" stroke-width="1" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
    <rect x="2.5" y="4" width="15" height="12" rx="2" />
    <path d="M9 4v12" />
    <template v-if="filled">
      <mask :id="maskId">
        <rect width="20" height="20" fill="white" stroke="none" />
        <path d="M5 7.5h1.5M5 10h1.5M5 12.5h1.5" stroke="black" />
      </mask>
      <path d="M4.5 4h4.5v12h-4.5a2 2 0 0 1-2-2v-8a2 2 0 0 1 2-2z" fill="currentColor" :mask="`url(#${maskId})`" />
    </template>
    <path v-else d="M5 7.5h1.5M5 10h1.5M5 12.5h1.5" />
  </svg>
</template>
