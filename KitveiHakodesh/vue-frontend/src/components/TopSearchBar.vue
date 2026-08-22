<template>
  <div class="top-search-bar">
    <div class="search-inner">
      <div v-if="$slots.left" class="slot-left"><slot name="left" /></div>
      <slot />
      <div v-if="$slots.right" class="slot-right"><slot name="right" /></div>
    </div>
  </div>
</template>

<style scoped>
.top-search-bar {
  /* Roomier than the bottom-docked bar: with no toolbar band behind it the pill
     would otherwise sit tight against the top edge of the page. */
  padding: 8px 10px 6px;
  /* No toolbar band and no divider: the bar sits directly on the page surface and
     the search field's own pill is the only chrome. (BottomSearchBar still carries
     both — it separates a docked strip from the content above it.) */
  flex-shrink: 0;
}
/* The pill shape/fill/border come from the global `.search-inner` rule (main.css):
   the field keeps its standard --input-bg fill so it reads as a distinct control
   against the page behind it (theme-aware, matching the app's other search
   fields), plus a subtle inset for depth. With the bar itself now transparent,
   this pill is what makes the control visible. Scoped, so the other search bars
   (home, TOC, commentary, filters) are untouched. */
.search-inner {
  gap: 6px;
  padding: 0 12px;
  box-shadow: inset 0 1px 1px color-mix(in srgb, var(--text-primary) 6%, transparent);
  /* The bar owns its height, so every page that uses it gets the same one. Without
     this the pill just took the height of its tallest child, which differs per page
     — a page with 20px icon buttons in its slots came out taller than one with only
     an input. Callers should not need to restate this. */
  height: 30px;
}
/* Also the bar's, not the page's: the field's type size is part of the control.
   Fill/border/outline/placeholder colors come from the global `.search-inner input`
   rule in main.css, so a caller's slotted input needs no styling of its own beyond
   `flex: 1` and its text direction. */
.search-inner :slotted(input) {
  font-size: 13px;
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
