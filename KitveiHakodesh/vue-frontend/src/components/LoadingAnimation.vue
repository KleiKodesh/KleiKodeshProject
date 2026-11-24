<script setup lang="ts">
defineProps<{
  text?: string
}>()
</script>

<template>
  <div class="spinner-wrap">
    <div class="spinner-content">
      <div class="dots"><span /><span /><span /></div>
      <div v-if="text" class="spinner-text">{{ text }}</div>
    </div>
  </div>
</template>

<style scoped>
.spinner-wrap {
  display: flex;
  align-items: center;
  justify-content: center;
  height: 100%;
  width: 100%;
}

.spinner-content {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 12px;
}

.dots {
  display: flex;
  gap: 6px;
  align-items: center;
}

.dots span {
  width: 8px;
  height: 8px;
  border-radius: 50%;
  background: var(--accent-color);
  animation: bounce 1.2s ease-in-out infinite;
}

.dots span:nth-child(2) {
  animation-delay: 0.2s;
}
.dots span:nth-child(3) {
  animation-delay: 0.4s;
}

.spinner-text {
  font-size: 14px;
  color: var(--text-primary);
  font-weight: normal;
  white-space: nowrap;
  text-align: center;
}

@keyframes bounce {
  0%,
  80%,
  100% {
    transform: scale(0.6);
    opacity: 0.4;
  }
  40% {
    transform: scale(1);
    opacity: 1;
  }
}
</style>
