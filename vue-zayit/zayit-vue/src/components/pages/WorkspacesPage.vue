<template>
  <div class="page-container">
    <WorkspaceManager />
  </div>
</template>

<script setup lang="ts">
import WorkspaceManager from '../WorkspaceManager.vue';
</script>

<style scoped>
.page-container {
  height: 100%;
  overflow-y: auto;
  background: var(--bg-primary);
}
</style>
