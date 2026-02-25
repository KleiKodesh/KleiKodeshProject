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
import { ref, computed, nextTick } from 'vue'
import { onClickOutside } from '@vueuse/core'

export interface ContextMenuItem {
    label: string
    action: () => void
}

const props = defineProps<{
    items: ContextMenuItem[]
}>()

const isVisible = ref(false)
const x = ref(0)
const y = ref(0)
const menuRef = ref<HTMLElement>()

const menuStyle = computed(() => ({
    left: `${x.value}px`,
    top: `${y.value}px`
}))

async function show(event: MouseEvent) {
    event.preventDefault()
    event.stopPropagation()
    x.value = event.clientX
    y.value = event.clientY
    isVisible.value = true

    // Wait for next tick to set up click outside listener
    await nextTick()
}

function hide() {
    isVisible.value = false
}

function handleItemClick(item: ContextMenuItem) {
    item.action()
    hide()
}

// Close menu on click outside
onClickOutside(menuRef, () => {
    if (isVisible.value) {
        hide()
    }
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
