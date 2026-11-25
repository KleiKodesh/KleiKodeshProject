<script setup lang="ts">
/**
 * The open-in-Excel glyph: WordDocumentGlyph's page and badge, with an X in the badge
 * in place of the W, so the two read as one family.
 *
 * Colour is inherited, not set: theme.css pins `svg { color }`, which otherwise cuts the
 * glyph off from its button's colour.
 */
</script>

<template>
  <svg xmlns="http://www.w3.org/2000/svg" width="16" height="16" viewBox="0 0 28 28">
    <path fill="currentColor" d="M14.566 2a2.4 2.4 0 0 1 1.698.703l6.93 6.93A2.75 2.75 0 0 1 24 11.579V23.6a2.4 2.4 0 0 1-2.4 2.4h-7.508a3.24 3.24 0 0 0 .82-1.5H21.6a.9.9 0 0 0 .9-.9V12H16a2 2 0 0 1-2-2V3.5H6.4a.9.9 0 0 0-.9.9V12H4V4.4A2.4 2.4 0 0 1 6.4 2zM15.5 10a.5.5 0 0 0 .5.5h5.94L15.5 4.06z" />
    <path fill="currentColor" fill-rule="evenodd" d="M11.75 13A2.25 2.25 0 0 1 14 15.25v8.5A2.25 2.25 0 0 1 11.75 26h-8.5A2.25 2.25 0 0 1 1 23.75v-8.5A2.25 2.25 0 0 1 3.25 13zM3.9 15.75h1.9l1.7 2.6l1.7-2.6h1.9l-2.65 3.75l2.65 3.75h-1.9l-1.7-2.6l-1.7 2.6h-1.9l2.65-3.75z" />
  </svg>
</template>

<style scoped>
svg {
  color: inherit;
}
</style>
