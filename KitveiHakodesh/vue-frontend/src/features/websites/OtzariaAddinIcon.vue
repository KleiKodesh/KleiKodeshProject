<script setup lang="ts">
import { ref, watch } from 'vue'
import { documentIcon } from '@/utils/documentIcons'
import { otzariaAddinIconSource } from './otzariaAddinsList'

/** An Otzaria addin's own icon (from its manifest); the puzzle piece while it loads or when it has none. */
const props = defineProps<{ path: string; size?: number }>()

const source = ref('')
watch(
  () => props.path,
  async (path) => {
    source.value = ''
    const resolved = await otzariaAddinIconSource(path)
    if (props.path === path && resolved) source.value = resolved
  },
  { immediate: true },
)
const puzzle = documentIcon('addin')
</script>

<template>
  <img v-if="source" :src="source" :width="size ?? 24" :height="size ?? 24" class="addin-icon" alt="" @error="source = ''">
  <component :is="size && size <= 20 ? puzzle.icon20 : puzzle.icon24" v-else :style="{ color: puzzle.color, width: `${size ?? 24}px`, height: `${size ?? 24}px` }" />
</template>

<style scoped>
.addin-icon {
  object-fit: contain;
  flex-shrink: 0;
}
</style>
