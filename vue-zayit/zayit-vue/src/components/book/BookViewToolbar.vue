<template>
    <div v-if="isVisible"
         ref="toolbarRef"
         class="bar book-view-toolbar"
         :class="[toolbarPositionClass, { 'is-dragging': isDragging }]"
         :style="adjustedDragStyle">
        <!-- Drag handle (only visible in float mode) -->
        <div v-if="isFloating"
             ref="dragHandleRef"
             class="drag-handle-bar"
             title="גרור להזזת סרגל הכלים">
            <Icon
                  :icon="toolbarPosition === 'float-vertical' ? 'fluent:re-order-dots-horizontal-24-regular' : 'fluent:re-order-dots-vertical-24-regular'" />
        </div>

        <!-- Toolbar position selector -->
        <div class="position-selector"
             ref="positionSelectorRef">
            <button @click="togglePositionDropdown"
                    class="flex-center c-pointer touch-interactive"
                    title="מיקום סרגל כלים">
                <Icon icon="fluent:arrow-move-24-regular" />
            </button>

            <transition name="slide">
                <div v-if="showPositionDropdown"
                     class="position-dropdown">
                    <div @click="setToolbarPosition('top')"
                         class="position-option"
                         :class="{ active: toolbarPosition === 'top' }">
                        למעלה
                    </div>
                    <div @click="setToolbarPosition('bottom')"
                         class="position-option"
                         :class="{ active: toolbarPosition === 'bottom' }">
                        למטה
                    </div>
                    <div @click="setToolbarPosition('left')"
                         class="position-option"
                         :class="{ active: toolbarPosition === 'left' }">
                        שמאל
                    </div>
                    <div @click="setToolbarPosition('right')"
                         class="position-option"
                         :class="{ active: toolbarPosition === 'right' }">
                        ימין
                    </div>
                    <div @click="setToolbarPosition('float-vertical')"
                         class="position-option"
                         :class="{ active: toolbarPosition === 'float-vertical' }">
                        צף מאונך
                    </div>
                    <div @click="setToolbarPosition('float-horizontal')"
                         class="position-option"
                         :class="{ active: toolbarPosition === 'float-horizontal' }">
                        צף מאוזן
                    </div>
                </div>
            </transition>
        </div>

        <div class="toolbar-separator"></div>
        <!-- Search button -->
        <button @click="handleSearchClick"
                class="flex-center c-pointer touch-interactive"
                title="חיפוש (Ctrl+F)">
            <Icon icon="fluent:search-24-regular" />
        </button>

        <!-- Commentary toggle button -->
        <button v-if="hasConnections"
                @click="handleToggleSplitPane"
                class="flex-center c-pointer touch-interactive"
                :title="isSplitPaneOpen ? 'הסתר מפרשים וקישורים' : 'הצג מפרשים וקישורים'">
            <CommentaryToggleIcon :is-open="isSplitPaneOpen" />
        </button>

        <div class="toolbar-separator"></div>

        <!-- Zoom out button -->
        <button @click="handleZoomOut"
                class="flex-center c-pointer touch-interactive"
                :disabled="currentZoom <= 50"
                :title="`הקטן (Ctrl-)\nזום: ${currentZoom}%\nאיפוס: Ctrl+0`">
            <Icon icon="fluent:zoom-out-24-regular" />
        </button>

        <!-- Zoom in button -->
        <button @click="handleZoomIn"
                class="flex-center c-pointer touch-interactive"
                :disabled="currentZoom >= 200"
                :title="`הגדל (Ctrl+)\nזום: ${currentZoom}%\nאיפוס: Ctrl+0`">
            <Icon icon="fluent:zoom-in-24-regular" />
        </button>

        <div class="toolbar-separator"></div>

        <!-- Diacritics toggle button -->
        <button @click="handleDiacriticsClick"
                class="flex-center c-pointer touch-interactive"
                :title="diacriticsTooltip">
            <component :is="diacriticsIcon"
                       class="diacritics-icon"
                       :class="diacriticsStateClass" />
        </button>

        <!-- Alt TOC toggle button -->
        <button @click="handleAltTocToggle"
                class="flex-center c-pointer touch-interactive"
                :title="isAltTocVisible ? 'הסתר כותרות נוספות' : 'הצג כותרות נוספות'">
            <Icon
                  :icon="isAltTocVisible ? 'fluent:eye-lines-28-filled' : 'fluent:eye-lines-28-regular'" />
        </button>

        <div class="toolbar-separator"></div>

        <!-- Theme toggle button -->
        <!-- <ThemeToggleButton /> -->
    </div>
</template>

<script setup lang="ts">
import { computed, ref } from 'vue'
import { Icon } from '@iconify/vue'
import { useToolbarPosition } from './useBookViewToolbarPosition'
import { useBookViewToolbarActions } from './useBookViewToolbar'
import CommentaryToggleIcon from '@/components/icons/CommentaryToggleIcon.vue'
// import ThemeToggleButton from '@/components/settings/ThemeToggleButton.vue'

const props = defineProps<{
    position: 'top' | 'bottom' | 'left' | 'right' | 'float-vertical' | 'float-horizontal'
}>()

const toolbarRef = ref<HTMLElement>()
const dragHandleRef = ref<HTMLElement>()

const {
    toolbarPosition,
    isFloating,
    isDragging,
    adjustedDragStyle,
    showPositionDropdown,
    positionSelectorRef,
    togglePositionDropdown,
    setToolbarPosition
} = useToolbarPosition(toolbarRef, dragHandleRef)

const {
    isVisible,
    currentZoom,
    hasConnections,
    isSplitPaneOpen,
    isAltTocVisible,
    diacriticsStateClass,
    diacriticsIcon,
    diacriticsTooltip,
    handleZoomIn,
    handleZoomOut,
    handleSearchClick,
    handleToggleSplitPane,
    handleAltTocToggle,
    handleDiacriticsClick
} = useBookViewToolbarActions()

const toolbarPositionClass = computed(() => {
    return `toolbar-${props.position}`
})

defineExpose({
    isVisible
})
</script>

<style scoped>
.book-view-toolbar {
    display: flex;
    align-items: center;
    justify-content: center;
    gap: 2px;
    padding: 2px 4px;
    flex-shrink: 0;
}

.book-view-toolbar.is-dragging {
    cursor: grabbing;
}

.drag-handle-bar {
    display: flex;
    align-items: center;
    justify-content: center;
    padding: 0 4px;
    cursor: grab;
    color: var(--text-secondary);
    transition: color 0.15s ease;
}

.drag-handle-bar:hover {
    color: var(--text-primary);
}

.drag-handle-bar:active {
    cursor: grabbing;
}

/* Position variants */
.toolbar-bottom,
.toolbar-left,
.toolbar-right {
    flex-shrink: 0;
}

.toolbar-bottom {
    border-top: 1px solid var(--border-color);
    border-bottom: none;
}

.toolbar-left,
.toolbar-right {
    flex-direction: column;
    width: 48px;
}

.toolbar-left {
    border-right: 1px solid var(--border-color);
    border-bottom: none;
}

.toolbar-right {
    border-left: 1px solid var(--border-color);
    border-bottom: none;
}

.toolbar-float {
    flex-wrap: wrap;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    background: var(--bg-primary);
}

.toolbar-float-horizontal {
    border: 1px solid var(--border-color);
    border-radius: 4px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    background: var(--bg-primary);
}

.toolbar-float-vertical {
    flex-direction: column;
    flex-wrap: nowrap;
    width: 48px;
    border: 1px solid var(--border-color);
    border-radius: 4px;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.15);
    background: var(--bg-primary);
}

.position-selector {
    position: relative;
}

.position-dropdown {
    position: absolute;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 4px;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    min-width: 100px;
    z-index: 1000;
}

.toolbar-top .position-dropdown {
    top: 100%;
    right: 0;
    margin-top: 4px;
}

.toolbar-bottom .position-dropdown {
    bottom: 100%;
    right: 0;
    margin-bottom: 4px;
}

.toolbar-left .position-dropdown {
    top: 0;
    left: 100%;
    margin-left: 4px;
}

.toolbar-right .position-dropdown {
    top: 0;
    right: 100%;
    margin-right: 4px;
}

.toolbar-float .position-dropdown {
    top: 100%;
    right: 0;
    margin-top: 4px;
}

.toolbar-float-horizontal .position-dropdown {
    top: 100%;
    right: 0;
    margin-top: 4px;
}

.toolbar-float-vertical .position-dropdown {
    top: 0;
    left: 100%;
    margin-left: 4px;
}

.position-option {
    padding: 8px 12px;
    cursor: pointer;
    transition: background-color 0.15s ease;
}

.position-option:hover {
    background: var(--hover-bg);
}

.position-option.active {
    background: var(--active-bg);
    font-weight: bold;
}

.toolbar-separator {
    background-color: var(--border-color);
}

.toolbar-top .toolbar-separator,
.toolbar-bottom .toolbar-separator,
.toolbar-float .toolbar-separator,
.toolbar-float-horizontal .toolbar-separator {
    width: 1px;
    height: 20px;
    margin: 0 2px;
}

.toolbar-left .toolbar-separator,
.toolbar-right .toolbar-separator,
.toolbar-float-vertical .toolbar-separator {
    width: 20px;
    height: 1px;
    margin: 2px 0;
}

button:disabled {
    opacity: 0.4;
    cursor: not-allowed;
}

.diacritics-icon {
    width: 20px;
    height: 20px;
    display: flex;
    align-items: center;
    justify-content: center;
    color: var(--text-primary);
}

.diacritics-icon :deep(svg) {
    fill: currentColor;
}

.diacritics-icon.state-1 :deep(svg) {
    fill: #ff8c00;
}

.diacritics-icon.state-2 :deep(svg) {
    fill: #ff4500;
}
</style>
