<template>
    <div class="theme-preview-card"
         :class="{ active, single, 'non-interactive': !interactive }"
         @click="handleClick"
         :style="{
            backgroundColor: colors.bgPrimary,
            borderColor: colors.borderColor
        }">
        <div class="preview-header"
             :style="{
                backgroundColor: colors.bgSecondary,
                color: colors.textPrimary
            }">
            {{ label }}
        </div>
        <div class="preview-content"
             :style="{ color: colors.textSecondary }">
            טקסט
        </div>
        <div class="preview-button"
             :style="{
                backgroundColor: colors.accentColor,
                color: '#fff'
            }">
            כפתור
        </div>
    </div>
</template>

<script setup lang="ts">
import type { ThemeColors } from '../config/themes'

const props = withDefaults(defineProps<{
    colors: ThemeColors
    label: string
    active?: boolean
    single?: boolean
    interactive?: boolean
}>(), {
    active: false,
    single: false,
    interactive: true
})

const emit = defineEmits<{
    click: []
}>()

function handleClick() {
    if (props.interactive) {
        emit('click')
    }
}
</script>

<style scoped>
.theme-preview-card {
    flex: 1;
    border: 2px solid;
    border-radius: 6px;
    padding: 6px;
    display: flex;
    flex-direction: column;
    gap: 4px;
    cursor: pointer;
    transition: transform 0.1s ease, box-shadow 0.1s ease;
}

.theme-preview-card.single {
    max-width: 50%;
}

.theme-preview-card:hover {
    transform: scale(1.02);
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
}

.theme-preview-card.active {
    border-width: 3px;
    box-shadow: 0 0 0 2px var(--accent-color);
}

.theme-preview-card.non-interactive {
    cursor: default;
    pointer-events: none;
}

.theme-preview-card.non-interactive:hover {
    transform: none;
    box-shadow: none;
}

.preview-header {
    padding: 4px 6px;
    border-radius: 3px;
    font-size: 10px;
    font-weight: bold;
}

.preview-content {
    padding: 3px 6px;
    font-size: 9px;
}

.preview-button {
    padding: 3px 8px;
    border-radius: 3px;
    font-size: 9px;
    text-align: center;
    align-self: flex-start;
}
</style>
