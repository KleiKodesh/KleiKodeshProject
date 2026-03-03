<template>
    <div class="flex-column split-pane-container"
         :class="{ 'bottom-hidden': !showBottom }">
        <div class="top-pane"
             :style="{ height: showBottom ? `${topHeight}%` : '100%' }">
            <slot name="top"></slot>
        </div>

        <div v-show="showBottom"
             class="flex-center resize-handle"
             @mousedown="startResize"
             @touchstart="startResize">
            <div class="resize-handle-bar"></div>
        </div>

        <div v-show="showBottom"
             class="flex-column bottom-pane"
             :style="{ height: `${bottomHeight}%` }">
            <slot name="bottom"></slot>
        </div>
    </div>
</template>

<script setup lang="ts">
import { useSplitPane } from '@/components/shared/useSplitPane'

const props = defineProps<{
    initialTopHeight?: number
    showBottom?: boolean
}>()

const emit = defineEmits<{
    resize: [topHeight: number, bottomHeight: number]
}>()

const { topHeight, bottomHeight, startResize } = useSplitPane(props, emit)
</script>

<style scoped>
.split-pane-container,
.top-pane,
.bottom-pane,
.resize-handle,
.resize-handle-bar {
    transition: none;
    animation: none;
}

.split-pane-container {
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    bottom: 0;
    width: 100%;
    height: 100%;
}

.top-pane {
    overflow: hidden;
    direction: rtl;
    position: relative;
    flex-shrink: 0;
}

.resize-handle {
    height: 6px;
    background: var(--bg-secondary);
    border-top: 1px solid rgba(128, 128, 128, 0.15);
    cursor: ns-resize;
    flex-shrink: 0;
    user-select: none;
    touch-action: none;
}

.resize-handle:active {
    background: var(--hover-bg);
}

.resize-handle-bar {
    width: 40px;
    height: 2px;
    background: rgba(128, 128, 128, 0.2);
    border-radius: 2px;
    opacity: 0.5;
}

.resize-handle:hover .resize-handle-bar {
    background: var(--border-color);
    opacity: 0.8;
}

.resize-handle:active .resize-handle-bar {
    background: var(--accent-color);
    opacity: 1;
}

.bottom-pane {
    overflow: hidden;
    direction: rtl;
    position: relative;
    background: var(--bg-primary);
    min-height: 20%;
}
</style>
