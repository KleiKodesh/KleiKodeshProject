<template>
    <div :class="['theme-preview-pair', { clickable: !interactive, active: active }]"
         @click="handleClick">
        <ThemePreviewCard :colors="lightColors"
                          label="בהיר"
                          :active="interactive && activeVariant === 'light'"
                          :interactive="interactive"
                          @click="handleLightClick" />

        <ThemePreviewCard :colors="darkColors"
                          label="כהה"
                          :active="interactive && activeVariant === 'dark'"
                          :interactive="interactive"
                          @click="handleDarkClick" />
    </div>
</template>

<script setup lang="ts">
import { type ThemeColors } from '@/utils/themes'
import ThemePreviewCard from './ThemePreviewCard.vue'

const props = withDefaults(defineProps<{
    lightColors: ThemeColors
    darkColors: ThemeColors
    active?: boolean
    activeVariant?: 'light' | 'dark' | null
    interactive?: boolean // If true, individual cards are clickable; if false, entire pair is clickable
}>(), {
    active: false,
    activeVariant: null,
    interactive: true
})

const emit = defineEmits<{
    click: []
    'click:light': []
    'click:dark': []
}>()

function handleClick() {
    if (!props.interactive) {
        emit('click')
    }
}

function handleLightClick() {
    if (props.interactive) {
        emit('click:light')
    }
}

function handleDarkClick() {
    if (props.interactive) {
        emit('click:dark')
    }
}
</script>

<style scoped>
.theme-preview-pair {
    display: flex;
    gap: 8px;
}

.theme-preview-pair.clickable {
    cursor: pointer;
    padding: 4px;
    border-radius: 6px;
    transition: background 0.2s ease;
}

.theme-preview-pair.clickable:hover {
    background: var(--hover-bg);
}

.theme-preview-pair.clickable.active {
    background: var(--accent-bg);
}
</style>
