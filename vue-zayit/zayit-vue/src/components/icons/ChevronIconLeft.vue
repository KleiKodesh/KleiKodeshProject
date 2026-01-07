<template>
  <svg class="chevron-icon"
       width="12"
       height="12"
       viewBox="0 0 12 12"
       fill="none">
    <path d="M8 2L4 6L8 10"
          stroke="currentColor"
          stroke-width="1.5"
          stroke-linecap="round"
          stroke-linejoin="round" />
  </svg>
</template>

<style scoped>
.chevron-icon {
  color: var(--text-secondary);
  /* Use secondary text color (lighter than primary) */
  flex-shrink: 0;
  /* Don't shrink icon if space is tight */
  transition: transform 0.2s cubic-bezier(0.4, 0, 0.2, 1);
  /* Smooth rotation animation */
}
</style>
