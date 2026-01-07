<template>
  <div v-if="showLoading"
       class="loading-container">
    <Icon icon="fluent:spinner-ios-20-regular"
          class="loading-spinner" />
    <div v-if="message"
         class="loading-text">{{ message }}</div>
  </div>
</template>

<script setup lang="ts">
import { ref, onMounted } from 'vue'
import { Icon } from '@iconify/vue';

const props = withDefaults(defineProps<{
  message?: string
  delay?: number
}>(), {
  message: 'טוען',
  delay: 300
})

const showLoading = ref(false)

onMounted(() => {
  setTimeout(() => {
    showLoading.value = true
  }, props.delay)
})
</script>

<style scoped>
.loading-container {
  display: flex;
  flex-direction: column;
  align-items: center;
  justify-content: center;
  gap: 16px;
  height: 100%;
  width: 100%;
  padding: 40px;
  text-align: center;
}

.loading-spinner {
  font-size: 32px;
  color: var(--accent-color);
  animation: spin 1s linear infinite;
}

@keyframes spin {
  from {
    transform: rotate(0deg);
  }

  to {
    transform: rotate(360deg);
  }
}

.loading-text {
  font-size: 16px;
  color: var(--text-secondary);
  font-weight: 500;
}
</style>
