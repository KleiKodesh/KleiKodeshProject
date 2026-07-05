<template>
  <div class="step-shortcuts">
    <h2 class="step-title">קיצורי מקשים שכדאי להכיר</h2>
    <div class="shortcuts-container">
      <ShortcutsReferenceList />
    </div>
  </div>
</template>

<script setup lang="ts">
import ShortcutsReferenceList from './ShortcutsReferenceList.vue';
</script>

<style scoped>
.step-shortcuts {
  display: flex;
  flex-direction: column;
  align-items: center;
  padding: 32px 24px 24px;
  height: 100%;
  overflow-y: auto;
  box-sizing: border-box;
}

.step-title {
  margin: 0 0 20px;
  font-size: 20px;
  font-weight: 700;
  color: var(--text-primary);
  text-align: center;
}

.shortcuts-container {
  width: 100%;
  max-width: 420px;
}
</style>
