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
    background: var(--bg-primary);
    border: 1px solid var(--border-color);
    border-radius: 6px;
    box-shadow: 0 4px 12px rgba(0, 0, 0, 0.15);
    min-width: 150px;
    padding: 4px 0;
    direction: rtl;
}

.context-menu-item {
    padding: 8px 16px;
    cursor: pointer;
    user-select: none;
    transition: background-color 0.15s;
    text-align: right;
}

.context-menu-item:hover {
    background: var(--hover-bg);
}

.context-menu-item:active {
    background: var(--active-bg);
}
</style>
