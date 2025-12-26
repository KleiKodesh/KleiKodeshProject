<template>
  <svg 
    :width="size" 
    :height="size" 
    viewBox="0 0 24 24" 
    :fill="color"
    class="icon"
  >
    <path d="M16.24,3.56L21.19,8.5C21.97,9.29 21.97,10.55 21.19,11.34L12,20.53C10.44,22.09 7.91,22.09 6.34,20.53L2.81,17C2.03,16.21 2.03,14.95 2.81,14.16L13.41,3.56C14.2,2.78 15.46,2.78 16.24,3.56M4.22,15.58L7.76,19.11C8.54,19.9 9.8,19.9 10.59,19.11L14.12,15.58L9.17,10.63L4.22,15.58Z"/>
  </svg>
</template>

<script setup lang="ts">
interface Props {
  size?: number | string
  color?: string
}

withDefaults(defineProps<Props>(), {
  size: 16,
  color: 'currentColor'
})
</script>

<style scoped>
.icon {
  display: inline-block;
  vertical-align: middle;
  flex-shrink: 0;
}
</style>