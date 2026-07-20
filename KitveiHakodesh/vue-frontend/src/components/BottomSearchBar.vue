<template>
  <div class="bottom-search-bar">
    <div class="search-inner">
      <div v-if="$slots.left" class="slot-left"><slot name="left" /></div>
      <slot />
      <div v-if="$slots.right" class="slot-right"><slot name="right" /></div>
    </div>
  </div>
</template>

<style scoped>
.bottom-search-bar {
  padding: 4px 10px;
  /* Match the BookView toolbar surface so the search bars read as toolbars. */
  background: var(--bg-toolbar);
  /* Softer hairline divider (Fluent in-flow chrome) rather than a hard full-color line. */
  border-top: 1px solid color-mix(in srgb, var(--border-color) 60%, transparent);
  flex-shrink: 0;
}
/* The pill shape/border come from the global `.search-inner` rule (main.css).
   Here we add the bottom-bar rhythm and override the fill to the toolbar color so
   the field matches the BookView toolbar surface (not the default gray --input-bg).
   Scoped, so the other search bars (home, TOC, commentary, filters) are untouched. */
.search-inner {
  gap: 6px;
  padding: 3px 12px;
  background: var(--bg-toolbar);
}
.slot-left {
  display: flex;
  align-items: center;
  flex-shrink: 0;
}
.slot-right {
  display: flex;
  align-items: center;
  gap: 2px;
  flex-shrink: 0;
}
</style>
