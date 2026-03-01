<template>
    <div class="setting-group">
        <label class="setting-label bold">ערכת נושא</label>
        <ThemePreviewDropdown v-model="themeValue"
                              :show-custom-themes="true"
                              :show-delete="true"
                              @delete="$emit('delete', $event)" />
    </div>
</template>

<script setup lang="ts">
import { computed } from 'vue'
import { type ThemePreset } from '../config/themes'
import ThemePreviewDropdown from './ThemePreviewDropdown.vue'

const props = defineProps<{
    modelValue: ThemePreset
}>()

const emit = defineEmits<{
    'update:modelValue': [value: ThemePreset]
    'create': []
    'delete': [id: string]
}>()

const themeValue = computed({
    get: () => props.modelValue,
    set: (value) => emit('update:modelValue', value as ThemePreset)
})
</script>

<style scoped>
/* No additional styles needed */
</style>
