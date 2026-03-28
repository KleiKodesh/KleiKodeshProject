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
  padding: 5px 10px 6px;
  background: var(--bg-secondary);
  border-top: 1px solid var(--border-color);
  flex-shrink: 0;
}
.search-inner {
  display: flex;
  align-items: center;
  gap: 4px;
  padding: 4px 10px;
}
.slot-left { display: flex; align-items: center; flex-shrink: 0; }
.slot-right { display: flex; align-items: center; gap: 2px; flex-shrink: 0; }
</style>
