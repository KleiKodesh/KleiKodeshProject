<script setup lang="ts">
import AppTitleBar from '@/components/layout/AppTitleBar.vue'
import AppPageView from '@/components/layout/AppPageView.vue'
</script>

<template>
  <div class="app-layout">
    <AppTitleBar />
    <main class="app-content">
      <AppPageView />
    </main>
  </div>
</template>

<style scoped>
.app-layout {
  display: flex;
  flex-direction: column;
  height: 100%;
}
.app-content {
  flex: 1;
  overflow: hidden;
}
</style>
