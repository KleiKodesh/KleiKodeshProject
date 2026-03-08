<template>
    <Teleport to="body">
        <div v-if="isVisible"
             ref="menuRef"
             class="context-menu"
             :style="menuStyle"
             @click.stop>
            <div v-for="item in items"
                 :key="item.label"
                 class="context-menu-item"
                 @click="handleItemClick(item)">
                {{ item.label }}
            </div>
        </div>
    </Teleport>
</template>

<script setup lang="ts">
import { onMounted } from 'vue'
import { useContextMenu, type ContextMenuItem } from '@/components/shared/useContextMenu'

const props = defineProps<{
    items: ContextMenuItem[]
}>()

const {
    isVisible,
    menuRef,
    menuStyle,
    show,
    hide,
    handleItemClick,
    setupClickOutside
} = useContextMenu()

onMounted(() => {
    setupClickOutside()
})

defineExpose({
    show,
    hide
})
</script>

<style scoped>
.context-menu {
    position: fixed;
    z-index: 9999;
    background: var(--bg-secondary);
    border: 1px solid var(--border-color);
    border-radius: 0;
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.12), 0 8px 24px rgba(0, 0, 0, 0.08);
    min-width: 160px;
    padding: 0;
    direction: rtl;
    animation: contextMenuFadeIn 0.12s ease-out;
}

@keyframes contextMenuFadeIn {
    from {
        opacity: 0;
        transform: scale(0.95);
    }

    to {
        opacity: 1;
        transform: scale(1);
    }
}

.context-menu-item {
    padding: 8px 16px;
    cursor: pointer;
    user-select: none;
    transition: background-color 0.1s ease;
    text-align: right;
    border-radius: 0;
    font-size: 13px;
    line-height: 1.3;
    border-bottom: 1px solid var(--border-color);
}

.context-menu-item:last-child {
    border-bottom: none;
}

.context-menu-item:hover {
    background: var(--hover-bg);
}

.context-menu-item:active {
    background: var(--active-bg);
}

:root.dark .context-menu {
    box-shadow: 0 2px 8px rgba(0, 0, 0, 0.3), 0 8px 24px rgba(0, 0, 0, 0.2);
}
</style>
