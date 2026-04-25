<script setup lang="ts">
</script>

<template>
  <div class="desktop-shell">
    <div class="desktop-shell__placeholder">
      <p>Desktop Shell - placeholder</p>
    </div>
  </div>
</template>

<style scoped>
.desktop-shell {
  display: flex;
  flex-direction: column;
  height: 100%;
  background: var(--bg-primary);
}

.desktop-shell__placeholder {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  color: var(--text-secondary);
  font-size: 1rem;
}
</style>